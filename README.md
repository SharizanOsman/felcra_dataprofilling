# FELCRA Cattle Breeding Data Profiling System

Digital recording and analytics for FELCRA cattle breeding farms: an **admin web portal** for HQ and farm managers
and an installable, offline-capable **field PWA** for officers at the pen, backed by an ASP.NET Core API and PostgreSQL.

- Requirements: [docs/URS.md](docs/URS.md)
- Delivery plan and status: [docs/ROADMAP.md](docs/ROADMAP.md)
- Deployment and operations: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) · Security: [docs/SECURITY.md](docs/SECURITY.md)
- User manuals: [docs/manual/](docs/manual/README.md) · UAT: [docs/UAT.md](docs/UAT.md) · Pilot, training and go-live: [docs/PILOT.md](docs/PILOT.md)
- Source documents (proposal, quotation, Pagoh census report): [Document/](Document/)

## Tech stack

.NET 10 · ASP.NET Core minimal APIs · EF Core + PostgreSQL (Npgsql) · ASP.NET Core Identity + JWT ·
Blazor WebAssembly + MudBlazor · PWA (service worker) · xUnit + Testcontainers · Docker.

## Solution layout

```
src/
  Felcra.Profiling.Domain/          Entities and business rules (no dependencies)
  Felcra.Profiling.Application/     Use-case interfaces and services
  Felcra.Profiling.Infrastructure/  EF Core, Identity, auditing, migrations, seeding
  Felcra.Profiling.Analytics/       Growth / lactation / ration models (pure C#, also runs in the browser)
  Felcra.Profiling.Contracts/       DTOs and validators shared by API and clients
  Felcra.Profiling.Api/             Web API; also serves both clients
  Felcra.Profiling.UI.Shared/       Shared Blazor components and client auth
  Felcra.Profiling.Web.Admin/       Admin portal (Blazor WASM)   -> /admin
  Felcra.Profiling.Web.Field/       Field app (Blazor WASM PWA)  -> /field
tests/
  Felcra.Profiling.Domain.Tests/
  Felcra.Profiling.Application.Tests/
  Felcra.Profiling.Analytics.Tests/
  Felcra.Profiling.Api.IntegrationTests/   (needs Docker)
  e2e/                                     Browser tests (Playwright), see tests/e2e/README.md
tools/
  uat-workbook.cs                          Builds the UAT Excel tracker from docs/UAT.md
deploy/                                    Production / staging stacks (see docs/DEPLOYMENT.md)
```

## Getting started

Prerequisites: [.NET SDK 10.0.401+](https://dotnet.microsoft.com/download) and
[Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL and the integration tests).

```bash
dotnet tool restore                 # installs dotnet-ef locally
docker compose up -d db             # PostgreSQL on localhost:5432
dotnet run --project src/Felcra.Profiling.Api --launch-profile http
```

In Development the API applies migrations and seeds data on startup. Then open:

| URL | What |
|---|---|
| http://localhost:5237/admin/ | Admin portal |
| http://localhost:5237/field/ | Field app (installable PWA) |
| http://localhost:5237/scalar/v1 | API reference (Development only) |
| http://localhost:5237/health | Health check |

Development accounts (password `Felcra@2026`):

| Email | Role | Farms |
|---|---|---|
| admin@felcra.local | HQ Admin | all |
| manager@felcra.local | Farm Manager | PAGOH |
| officer@felcra.local | Field Officer | PAGOH |

HQ admins use the admin portal; field officers use the field app; farm managers can use both.

### Loading the Pagoh census

`Document/pagoh-census-2026-07.csv` (not in git — client data) holds the 140 animals from the July 2026 Pagoh report
in the FFISB form layout. In the admin portal: **Census → New session** (farm PAGOH), then **Import sheet**, turn
**Match existing animals by tag** off for this first registration, check the preview and import. A blank template is
available from the same dialog.

To run the whole stack in containers instead: `docker compose --profile app up --build`, then browse to http://localhost:8080/admin/.

## Common tasks

```bash
dotnet build Felcra.Profiling.slnx
dotnet test Felcra.Profiling.slnx

# Browser end-to-end tests (resets the local database; see tests/e2e/README.md)
powershell -File tests/e2e/run-all.ps1

# Regenerate the UAT workbook after editing docs/UAT.md
dotnet run tools/uat-workbook.cs

# Add an EF Core migration
dotnet ef migrations add <Name> --project src/Felcra.Profiling.Infrastructure \
  --startup-project src/Felcra.Profiling.Api --output-dir Persistence/Migrations
```

> **Note:** the service worker only caches assets in published builds. To test offline behaviour,
> run `dotnet publish` (or the Docker image) rather than `dotnet run`.

### How the field app works offline

- After sign-in the app downloads the officer's farm animals, treatment list, open programmes, tasks and feed
  ingredients to the phone
  (IndexedDB), then refreshes them every few minutes and whenever the connection returns (`GET /api/sync/pull`,
  incremental by timestamp).
- Records are saved on the phone first (an outbox) and sent in order through the normal record endpoints. Each record
  carries an ID made on the phone, so a resend after a dropped connection is stored once.
- If the server rejects a record (e.g. the animal was sold meanwhile) it stays on the phone, marked "Not accepted",
  until the officer retries or discards it on the **Sync** screen.
- Signing out deletes the phone's copy (shared phones); the app warns first if records have not been sent.
- History is shown as last seen online; latest weight and BCS are part of the downloaded data.
- **Task notifications** (Sync screen → Turn on): each morning a phone gets one message when pregnancy checks,
  calvings or treatments are due on the officer's farms (Web Push). On iPhone this needs iOS 16.4+ with the app added
  to the Home Screen. Signing out turns them off for that phone.

### Analytics

| Model | Where | Notes |
|---|---|---|
| Gompertz growth W(t) = A·exp(−b·exp(−k·t)) | Admin → Analytics → Growth (`GET /api/analytics/growth`) | Fitted per sex by Levenberg–Marquardt to all weights (field and census) of animals with a birth date; animals ≥ 15% below the curve for their age are listed (from 3 months old) |
| Ali-Schaeffer lactation | Admin → Analytics → Lactation (`GET /api/analytics/lactation`) | Linear least squares on daily milk yields, using days since the last calving; needs ≥ 6 test days |
| Least-cost ration | Admin → Analytics → Ration planner; field app → Ration planner | Linear program (two-phase simplex) run in the browser / phone from Master data feed ingredients, so it works offline |

The dashboard (`GET /api/analytics/dashboard`) summarises one farm or all farms. Development data includes
**indicative** values for eight common Malaysian feeds (Napier, Brachiaria, PKC, rice bran, soybean meal, molasses,
limestone, dicalcium phosphate). They are for trying the planner only: FELCRA should replace them with analysed
values and current prices, and have the ration targets confirmed by a nutritionist.

### Reports

Admin → **Reports** (HQ and farm managers), each as PDF for printing and Excel for analysis:

| Report | Covers |
|---|---|
| Census and profiling form | A census programme in the layout of the Pagoh report: summary, farm box, the animal list with P / I / AJ / AB ticks and selected animals highlighted, sign-off. The Excel version uses the import template's columns, so a corrected sheet can be imported again |
| Selected animals | The animals selected in a census programme |
| Health and treatments | Treatments given in a period, with dose, batch and withdrawal date |
| Farm performance | Herd by category, records made in a period, animals that left the herd, breeding, growth, data quality |
| Culling candidates | Animals meeting the culling rules today |

The census form and culling list can also be downloaded from the census programme and Breeding pages. On the 1st of
each month (configurable) last month's farm performance, treatment and culling reports are e-mailed as PDFs: HQ admins
get all farms, each farm manager gets their own farms. Each month is sent once, even across restarts or several server
instances. HQ can send themselves a test copy from the Reports page. In development the e-mails are written to
`src/Felcra.Profiling.Api/App_Data/mail` as `.eml` files (open them with any mail program).

PDFs embed the Carlito font (SIL Open Font Licence, metric-compatible with Calibri), so they look the same on Windows
and on the Linux server.

### Trying the field app on a phone

Browsers only allow camera access (QR scanning, photos) on **HTTPS** pages or `localhost`. Opening
`http://<laptop-ip>:5237/field/` from a phone therefore loads the app but the scanner cannot start. To test on a
real phone before the staging server exists (Step 9), expose the local API through an HTTPS tunnel such as
`cloudflared tunnel --url http://localhost:5237` and open the tunnel URL + `/field/` on the phone.
The **Find by tag number** screen works without a camera.

Uploaded photos are stored under `src/Felcra.Profiling.Api/App_Data/uploads` in development (`Storage:Path`).

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Jwt:SigningKey` | ≥ 32-character secret. **Must** be set per environment (env var `Jwt__SigningKey`); the value in `appsettings.Development.json` is for local use only |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | Token lifetimes (default 15 min / 30 days) |
| `Database:MigrateOnStartup` / `Database:SeedOnStartup` | Apply migrations / seed roles and the first admin at startup |
| `Seed:AdminEmail` / `Seed:AdminPassword` | First HQ admin, created only when no users exist |
| `Seed:DevelopmentData` | Adds the Pagoh farm and sample manager / officer accounts |
| `RateLimiting:LoginPermitsPerMinute` | Login/refresh attempts per IP per minute (default 10) |
| `RateLimiting:ApiRequestsPerMinute` | API calls per user (or IP before sign-in) per minute (default 600) |
| `ReverseProxy:Enabled` | Trust `X-Forwarded-For` / `-Proto` from the reverse proxy (on in production; only when the API is reachable through the proxy alone) |
| `DataProtection:KeysPath` | Folder for ASP.NET Core data-protection keys (production: `/app/data/keys`) |
| `Storage:Path` | Folder for uploaded photos (default `App_Data/uploads`, relative to the API); mount as a volume in Docker |
| `Breeding:GestationDays` | Gestation length used for calving due dates (default 285) |
| `Breeding:PregnancyCheckAfterDays` | Days after service when a pregnancy check becomes due (default 45) |
| `Breeding:CullOpenDays` / `CullAgeYears` / `CullConsecutiveOpenChecks` | Culling rules (defaults 365 days, 8 years, 2 checks — from the Pagoh report) |
| `Breeding:LowBcs` | BCS below this is flagged (default 2.0 for a 1–5 scale; use 4 for 1–9) |
| `Email:From` | Sender of report e-mails, e.g. `FELCRA Cattle Profiling <no-reply@felcra.com.my>` |
| `Email:Smtp:Host` / `Port` / `UserName` / `Password` | SMTP server for outgoing e-mail (port 587 with STARTTLS by default) |
| `Email:PickupDirectory` | Write e-mails as `.eml` files here instead of sending them (development: `App_Data/mail`) |
| `Reports:Monthly:Enabled` | Send last month's reports by e-mail each month (off by default; on in development) |
| `Reports:Monthly:DayOfMonth` / `Hour` | When to send them, Malaysia time (default day 1, 07:00) |
| `Reports:Monthly:PortalUrl` | Portal address for links in the e-mail |
| `Push:PublicKey` / `PrivateKey` | Web Push (VAPID) key pair for task notifications; generate with `dotnet Felcra.Profiling.Api.dll vapid-keys`. Notifications are off without them |
| `Push:Subject` | Contact sent to push services, e.g. `mailto:it@felcra.com.my` |
| `Push:Hour` | Hour (Malaysia time) from which the daily task notification is sent (default 7) |

## Key design decisions

- **UUIDv7 IDs** generated client-side, so offline devices can create records without clashes.
- **Ear-tag number is not the identity.** Pagoh data has duplicate and missing tags; QR labels encode the system ID.
- **Farm scoping** is enforced by EF Core global query filters: managers and officers only ever query their assigned farms.
- **Audit trail and soft delete** are applied automatically by a `SaveChanges` interceptor.
- **Refresh-token rotation** with reuse detection: replaying an old refresh token revokes all of that user's sessions.
- **Analytics without dependencies:** curve fitting and linear programming are small, tested pure-C# routines, so the
  same library runs on the server and in the Blazor clients (offline ration planning), and charts are plain SVG.
- Both Blazor clients are hosted by the API under `/admin` and `/field`; shared library assets live at `/_content`
  and are rewritten for each base path.
