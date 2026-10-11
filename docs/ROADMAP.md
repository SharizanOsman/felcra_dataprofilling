# Roadmap

Delivery is split into steps; each is demoable and deployable to staging. Milestones refer to quotation QTN-26-08-002.

| Step | Scope | Status |
|---|---|---|
| 0 | URS & data dictionary | Draft — [URS.md](URS.md), awaiting FELCRA review |
| 1 | Foundation (solution, PostgreSQL, auth, audit, CI) | Done |
| 2 | Master data, farm & user management | Done — model parameters & reminder settings moved to Steps 7 & 6, where they are used |
| 3 | Animal registry, census & import | Done — Pagoh census (140 head) imports cleanly; dam/sire pickers come with breeding in Step 6 |
| 4 | Field PWA (online) | Done — QR scan, find by tag, weight / BCS / milk, treatments, observations with photos |
| 5 | Offline-first & sync | Done — farm data on the phone, outbox with retry, rejected records kept for review; animal registration offline comes with calving in Step 6 |
| 6 | Fertility, breeding & reminders | Not started |
| 7 | Analytics engine & dashboard | Not started |
| 8 | Reports & export | Not started |
| 9 | Hardening & DevOps | Not started |
| 10 | UAT, Pagoh pilot, training & go-live | Not started |

## Steps

Each step is demoable and deployable to staging. Mapped to the quotation's payment milestones.

### Step 0 — URS & Data Dictionary *(40% milestone)*
- Write `docs/URS.md`: roles & access, screen list, workflow (pen → dashboard), data definitions (based on the Pagoh census form).
- Initial ERD + simple wireframes (admin & field).
- List of open questions for FELCRA (see URS §10).
- **Output:** approved URS, ERD, wireframes.

### Step 1 — Foundation
- Scaffold the solution (see README), `Directory.Packages.props` (central package management), `.editorconfig`.
- PostgreSQL via `docker-compose` (dev), EF Core migrations.
- Auth: Identity + JWT, roles `HqAdmin`, `FarmManager`, `FieldOfficer`; data scoped by farm (managers/officers only see their own farms) via global query filters.
- Audit trail: `SaveChangesInterceptor` → `AuditLog` table (who, when, entity, old/new values).
- Soft delete, `CreatedAt/UpdatedAt/CreatedBy`, UUIDv7 for all IDs (offline-ready).
- CI (GitHub Actions): build + test + `docker build`.
- **Output:** login works in both apps; API documented via OpenAPI/Scalar.

### Step 2 — Master Data, Farm & User Management (Admin Web)
- CRUD: Projects/Schemes (EPP5, RP4…), Farms/Premises (address, area in ha, capacity), Groups/Pens, Breeds, Medicines/Vaccines/Treatments, Feed Ingredients (nutrients + price), model parameters, reminder settings.
- User management & farm assignment; activity log.
- **Output:** admin can fully set up the Pagoh farm.

### Step 3 — Animal Registry, Census & Data Import
- `Animal` entity (AnimalId, current tag no. + tag history, sex, category P/I/AJ/AB, breed, birth date + IsEstimated, dam/sire if known, farm/project/group, status: active/sold/dead/culled).
- `CensusSession` (start–end date, farm, PIC, programme treatments) + `CensusEntry` per animal (weight, estimated age, remarks, `Selected`).
- Excel import (template based on the Pagoh form) with error report and duplicate/missing tag detection.
- Generate & print QR labels (PDF sheet) for ear tags — QR encodes a short `AnimalId`, not the tag number.
- **Output:** the 140 Pagoh records imported; animal list & profile in admin.

### Step 4 — Field PWA (online first)
- Manifest + service worker (Blazor PWA template), installable on Android/iOS.
- Scan QR → open animal profile; manual search by tag no. (show choices on duplicates).
- Record: body weight, BCS (1–5 or 1–9 per URS), milk yield, treatments/vaccinations, remarks + photos.
- Field-friendly UI: large buttons, minimal typing.
- **Output:** officers can record at the pen (with internet).

### Step 5 — Offline-first & Sync
- IndexedDB (Dexie) caches the officer's farm animals + an outbox of new records.
- Measurements (weight/BCS/milk/treatments/breeding events) are **append-only events** with client-generated IDs → near-zero conflicts.
- Animal profile edits: last-write-wins + `xmin`/version check; conflicts flagged for manager review.
- `POST /sync/push` (batched, idempotent) & `GET /sync/pull?since=cursor` endpoints.
- Background sync when online + status indicator ("N records pending").
- **Output:** record 50+ animals with no internet, auto-sync when back online.

### Step 6 — Fertility, Breeding & Reminders
- Events: heat, AI/natural service (bull), pregnancy check (+ gestation age), calving (auto-registers calf), abortion, bull BSE.
- Calculations: expected calving date, calving interval, days open → flag culling candidates (e.g. dam not pregnant in 12–18 months, age ≥ 8 years — per the Pagoh report).
- Reminders via Hangfire: vaccination due, pregnancy check, expected calving, low BCS → Web Push + in-app inbox (iOS: push requires the PWA to be installed, iOS 16.4+).
- **Output:** daily task list for officers; culling candidate list for managers.

### Step 7 — Analytics Engine & Dashboard *(30% milestone after Steps 4–7)*
- `Analytics` project (pure functions, unit-tested against reference data):
  - **Gompertz:** W(t) = A·exp(−b·exp(−k·t)), fitted per animal/group → expected weight, ADG, animals below curve.
  - **Ali-Schaeffer:** y(t) = a + b(t/305) + c(t/305)² + d·ln(305/t) + e·ln²(305/t) (linear least squares) → expected lactation yield.
  - **Least-cost ration:** LP (OR-Tools GLOP) — minimise cost subject to nutrient requirements (DM, CP, TDN/ME, Ca, P) per group & target.
- Admin dashboard: KPIs per farm/project (population by category, average weight/BCS, pregnancy rate, calving rate, mortality), trends, cross-project comparison.
- **Output:** HQ dashboard + ration screen in the field app.

### Step 8 — Reports & Export
- PDF & Excel: Census & Profiling Form (same layout as the Pagoh report), selected animals list, health/treatment report, farm performance, culling candidates.
- Scheduled monthly reports, auto-generated and emailed.
- **Output:** the Pagoh report can be regenerated from the system.

### Step 9 — Hardening & DevOps
- Production Docker Compose: API (hosting both clients), PostgreSQL, Caddy (automatic HTTPS), staging + production.
- Daily `pg_dump` backups + offsite copy, health checks, Serilog → file/Seq, rate limiting, security headers, OWASP checklist, basic penetration test.
- **Output:** staging & production live on the VPS.

### Step 10 — UAT, Pagoh Pilot, Training & Go-Live *(20% → 10% milestones)*
- FAT/UAT with HQ, managers and field officers; pilot at Pagoh (existing 140-head data).
- User manual, short training videos, punch list, final documentation & handover.

---

