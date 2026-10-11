# Pilot, training and go-live plan

From an accepted system to daily use on all FELCRA farms. Follows the quotation's last two milestones:
**20%** on UAT completion and the pilot at selected farms, **10%** on launch across all farms, user training and the
final documentation, then **3 months** of warranty (minor defect rectification).

```
Prepare ─► Train ─► UAT (staging) ─► Pagoh pilot (production) ─► Review ─► Roll-out farm by farm ─► Warranty (3 months)
 1–2 wk     1 day     2 wk              4–6 wk                     1 wk      ~1 wk per farm          from go-live
```

## 1. Preparation (before UAT)

| # | Task | Who | Done |
|---|---|---|---|
| 1 | Answer the open questions in [URS.md](URS.md) §10, at least: BCS scale (1), Malay UI (7), Pagoh count discrepancies (9–11), SMTP (12), report language (13), hosting (14) | FELCRA | ☐ |
| 2 | VPS, domain names and SMTP account provided; staging deployed ([DEPLOYMENT.md](DEPLOYMENT.md)) | FELCRA IT + supplier | ☐ |
| 3 | Feed ingredient values and prices (analysed, current) | FELCRA nutritionist | ☐ |
| 4 | User list: name, e-mail, role, farm — for HQ, Pagoh manager and field officers | FELCRA | ☐ |
| 5 | Phones for field officers: Android 10+ (Chrome) or iPhone iOS 16.4+, mobile data at the pens | FELCRA | ☐ |
| 6 | QR sticker labels (A4 sticker sheets, weatherproof if possible) and a printer | FELCRA | ☐ |
| 7 | Decide the census sheet to load into production: the July 2026 census as is, or after fixing the duplicate / missing tags | FELCRA + supplier | ☐ |

## 2. UAT

As in [UAT.md](UAT.md): one week of testing on staging with Pagoh's demo data, one week for fixes and re-tests,
sign-off. Output: signed UAT form, defect list closed or moved to the punch list.

## 3. Training

Short, hands-on sessions with each group's own devices. Trainees use the staging site (it can be reset) and the
[user manual](manual/).

| Session | Who | Length | Content | Hands-on exercise |
|---|---|---|---|---|
| A. Field app | Field officers (+ farm manager) | 3 h, at the pens | Install on own phone, sign in; scan / find; record weight, BCS, treatment, observation with photo; breeding events incl. calving; offline and Sync screen; notifications; signing out on shared phones | Each officer records 10 animals, 3 of them with the phone in airplane mode, then syncs and finds their records in the portal |
| B. Farm management | Farm managers | 3 h | Dashboard; animals (register, re-tag, status); QR labels; census session and import; breeding tasks and culling; growth and ration; reports | Run a mini census for 10 animals: create the session, import a sheet with one error, fix it, print the form |
| C. Administration | HQ admins | 2 h | Users and farms; master data; audit log and restore; monthly e-mail; what IT changes (rules, backups) | Set up a test farm with a manager and an officer, end to end |
| D. Train the trainer | 1–2 HQ staff + Pagoh manager | 2 h | Running sessions A and B for the next farms; common questions | Deliver part of session A to colleagues |

**Training videos** (2–4 minutes each, screen recording with voice-over, recorded on staging): 1. Installing the
app and signing in · 2. Scanning and recording a weight · 3. Recording a treatment · 4. Breeding: service, pregnancy
check, calving · 5. Working without internet · 6. Running a census and printing the form · 7. Reports and the
dashboard · 8. Managing users (HQ). Each follows the matching section of the manual.

Attendance and a short practical check (each trainee completes the exercise) are recorded for the final milestone.

## 4. Pagoh pilot (production)

**Go-live checklist (production)**

| # | Item | Done |
|---|---|---|
| 1 | UAT signed off; no open Critical / High defects | ☐ |
| 2 | Production deployed from a released version; HTTPS working; `/health` monitored | ☐ |
| 3 | Production secrets generated on the server; push keys created; SMTP tested ("Send me a test e-mail") | ☐ |
| 4 | Nightly backups running **and copied off-site**; one restore tested on staging | ☐ |
| 5 | HQ admin account created; master data entered (projects, breeds, treatments with withdrawal periods, feeds) | ☐ |
| 6 | Farm PAGOH created; Pagoh census imported into production; counts checked (140 head, or the corrected figures) | ☐ |
| 7 | Users created and passwords handed over; officers' phones installed and synced | ☐ |
| 8 | QR labels printed and attached for the pilot animals | ☐ |
| 9 | Support contact and escalation path announced (section 6) | ☐ |

**During the pilot (4–6 weeks)** all recording at Pagoh is done in the system; paper forms only as a fall-back.
Weekly 30-minute check-in with the farm manager and HQ: questions, problems, figures.

**Success measures** (reviewed at the end of the pilot):

| Measure | Target |
|---|---|
| Active animals with a QR label | 100% |
| Duplicate and missing tags resolved | 0 left |
| Animals weighed at least once in the pilot | ≥ 90% |
| Breeding females with a breeding record (pregnancy check) | ≥ 90% |
| Records sent from phones without loss | 100% (no unresolved "Not accepted") |
| Monthly report produced from the system instead of by hand | Yes |
| Users able to do their tasks without help (training check) | All |
| Open Critical / High defects | 0 |

**Pilot review:** findings, punch-list decisions, go / no-go for roll-out, and the list of next farms. The pilot's
completion with the signed UAT is the **20% milestone**.

## 5. Roll-out to all farms

Per farm (about one week each, two farms in parallel once HQ trainers are confident):

1. HQ creates the farm, groups, users ([HQ guide](manual/hq-admin.md) §1).
2. Census sheet prepared in the paper-form layout and imported; duplicates and missing tags listed for tagging.
3. Training sessions A and B at the farm (delivered by the trained HQ staff, supported by the supplier).
4. QR labels printed and attached during the next handling of the herd.
5. First week of recording followed up by HQ (dashboard: "Weighed in last 90 days", tag problems).

Launch on all farms with training completed and the final documentation handed over is the **10% milestone**.
Sizing check after each few farms: server load, disk use for photos and backups ([DEPLOYMENT.md](DEPLOYMENT.md) §7).

## 6. Support and warranty

| Level | Who | Handles |
|---|---|---|
| 1 | Farm manager / HQ trainer | "How do I…", passwords, phones, re-tagging |
| 2 | FELCRA IT | Accounts, server, backups, monitoring |
| 3 | Supplier | Defects, data problems, releases |

**Warranty: 3 months from go-live** — rectification of minor defects at no charge. Report defects with the steps,
screenshot, user and time; Critical defects (data loss, system unusable) by phone as well. Fixes are released to
staging first, then production ([DEPLOYMENT.md](DEPLOYMENT.md) §4). After the warranty, support continues under a
separate maintenance contract (quotation term 6).

## 7. Handover (final documentation)

| Item | Where |
|---|---|
| Source code and history | Git repository (this repository) |
| Requirements and traceability | [URS.md](URS.md), [UAT.md](UAT.md) §3 |
| User manuals (HQ, manager, field officer) | [manual/](manual/) |
| Deployment and operations runbook | [DEPLOYMENT.md](DEPLOYMENT.md) |
| Security checklist | [SECURITY.md](SECURITY.md) |
| Architecture, configuration, development set-up | [README.md](../README.md) |
| Automated tests (unit, API, browser) | `tests/` — how to run them in the README |
| Release images | GitHub Container Registry (`.github/workflows/release.yml`) |
| Signed UAT, training records, pilot review | FELCRA project file |
| Credentials (server, registry, SMTP, push keys, admin) | Handed to FELCRA IT in person / password manager — never by e-mail or in the repository |
