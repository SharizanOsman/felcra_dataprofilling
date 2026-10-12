# Browser end-to-end tests

Playwright scripts that drive the real apps in Microsoft Edge (or Chrome): the admin portal and the field PWA on
a phone-sized screen, including QR scanning with a fake camera, offline recording and sync. Each prints
`PASS` / `FAIL` per check and exits non-zero on any failure, including browser console errors (so a
Content-Security-Policy violation fails the run).

## Setup

```powershell
docker compose up -d db          # from the repository root
dotnet build Felcra.Profiling.slnx
cd tests/e2e; npm install
```

The suites use the Pagoh census sheet `Document/pagoh-census-2026-07.csv`. It is FELCRA client data and is **not in
the repository**; get it from the project file. Set `E2E_CHANNEL=chrome` to use Chrome instead of Edge.

## Run everything

```powershell
powershell -File tests\e2e\run-all.ps1
```

Resets the development database between groups (it **deletes** local data), starts the API, imports Pagoh where
needed, publishes a release build for the offline suite, and prints a summary. About 10 minutes. Screenshots go
to `tests/e2e/out`.

## Suites

| Script | Covers | Needs |
|---|---|---|
| `admin-e2e.mjs` | Sign-in, farms, groups, master data, users, audit log | empty database |
| `census-e2e.mjs` | Census session, import preview and import of the Pagoh sheet, entries | empty database, CSV |
| `field-e2e.mjs` | QR scan (fake camera), find, weight / BCS / treatment / observation with photo | Pagoh imported |
| `offline-e2e.mjs` | Offline start, recording without network, outbox, sync, rejected records | release build on :5299, Pagoh |
| `breeding-e2e.mjs` | AI, pregnancy check, calving with calf registration, tasks, culling (field + admin) | Pagoh imported |
| `analytics-e2e.mjs` | Dashboard, growth, lactation, ration planner (admin and offline on the phone) | Pagoh imported, before other recording |
| `reports-e2e.mjs` | Reports page downloads (PDF / Excel), census and breeding shortcuts, monthly e-mail test | Pagoh imported |
| `account-e2e.mjs` | Change password in both apps | any data |
| `language-map-e2e.mjs` | Bahasa Melayu default and the BM/EN switch in both apps; farm map, place and GPS search, location picker | seeded data, internet (OpenStreetMap) |

The apps start in Bahasa Melayu; `english.mjs` makes every suite's browser start in English, since the checks
look for English text.

| `prod-smoke-e2e.mjs` | Production stack behind Caddy over HTTPS: dashboard, PDF, offline PWA, Web Push | `deploy/` stack, `NODE_TLS_REJECT_UNAUTHORIZED=0` for Caddy's local certificate |
| `restore-drill.mjs` | Backup / restore drill helper (`before`, `after`, `check`) | `deploy/` stack |
| `manual-shots.mjs` | Regenerates the user-manual screenshots (`docs/manual/images`) | Pagoh imported |
| `import-pagoh.mjs` | Imports the census CSV through the API (`BASE` sets the server) | CSV |
