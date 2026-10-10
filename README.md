# FELCRA Cattle Breeding Data Profiling System

Digital recording and analytics for FELCRA cattle breeding farms: an **admin web portal** for HQ and farm managers
and an installable, offline-capable **field PWA** for officers at the pen, backed by an ASP.NET Core API and PostgreSQL.

- Requirements: [docs/URS.md](docs/URS.md)
- Delivery plan and status: [docs/ROADMAP.md](docs/ROADMAP.md)
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
  Felcra.Profiling.Analytics/       Growth / lactation / ration models (Step 7)
  Felcra.Profiling.Contracts/       DTOs and validators shared by API and clients
  Felcra.Profiling.Api/             Web API; also serves both clients
  Felcra.Profiling.UI.Shared/       Shared Blazor components and client auth
  Felcra.Profiling.Web.Admin/       Admin portal (Blazor WASM)   -> /admin
  Felcra.Profiling.Web.Field/       Field app (Blazor WASM PWA)  -> /field
tests/
  Felcra.Profiling.Domain.Tests/
  Felcra.Profiling.Analytics.Tests/
  Felcra.Profiling.Api.IntegrationTests/   (needs Docker)
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

To run the whole stack in containers instead: `docker compose --profile app up --build`, then browse to http://localhost:8080/admin/.

## Common tasks

```bash
dotnet build Felcra.Profiling.slnx
dotnet test Felcra.Profiling.slnx

# Add an EF Core migration
dotnet ef migrations add <Name> --project src/Felcra.Profiling.Infrastructure \
  --startup-project src/Felcra.Profiling.Api --output-dir Persistence/Migrations
```

> **Note:** the service worker only caches assets in published builds. To test offline behaviour,
> run `dotnet publish` (or the Docker image) rather than `dotnet run`.

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

## Key design decisions

- **UUIDv7 IDs** generated client-side, so offline devices can create records without clashes.
- **Ear-tag number is not the identity.** Pagoh data has duplicate and missing tags; QR labels encode the system ID.
- **Farm scoping** is enforced by EF Core global query filters: managers and officers only ever query their assigned farms.
- **Audit trail and soft delete** are applied automatically by a `SaveChanges` interceptor.
- **Refresh-token rotation** with reuse detection: replaying an old refresh token revokes all of that user's sessions.
- Both Blazor clients are hosted by the API under `/admin` and `/field`; shared library assets live at `/_content`
  and are rewritten for each base path.
