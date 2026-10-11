# FELCRA Cattle Profiling — user manual

FELCRA Cattle Profiling records every animal on FELCRA cattle farms — weights, health, breeding — from the pen
to HQ. It has two parts:

| | **Field app** | **Admin portal** |
|---|---|---|
| For | Field officers (and farm managers at the pen) | Farm managers and HQ |
| Used on | Phone (Android or iPhone), works without internet | Computer browser |
| Address | `https://<your-domain>/field/` | `https://<your-domain>/admin/` |
| Main jobs | Scan an ear tag, record weight, treatment, breeding | Census, reports, dashboards, breeding follow-up, set-up |

## Guides

- [Field officer guide](field-officer.md) — install the app, scan, record, work offline.
- [Farm manager guide](farm-manager.md) — dashboard, animals, census programme, breeding, analytics, reports.
- [HQ administrator guide](hq-admin.md) — users, farms, master data, audit log, restoring records, monthly e-mail.

## Words used in the system

| Term | Meaning |
|---|---|
| **P / I / AJ / AB** | Census categories, as on the FFISB form: **P** bull (*pejantan*), **I** dam (*induk*), **AJ** male calf (*anak jantan*), **AB** female calf (*anak betina*) |
| **Animal no.** (e.g. A000005) | The system's own number for an animal. It never changes, even if the ear tag is lost or replaced |
| **Tag no.** | The ear-tag number. It can be missing or (by mistake) used twice; the system warns about both |
| **QR label** | A sticker printed by the system for the ear tag; scanning it opens the right animal even if the tag number is duplicated |
| **BCS** | Body condition score |
| **Census programme** | A headcount and health programme on one farm over a few days (e.g. "Headcount & health programme, July 2026") |
| **Selected** | An animal that meets the programme's specification (highlighted yellow on the census form) |
| **~ (tilde) before an age** | The age is estimated (usually from the teeth) |
| **Sync** | Sending records made on the phone to the server, and getting the latest farm data |

## Getting help

1. Check the *Problems and answers* section of your guide.
2. Ask your farm manager.
3. Contact FELCRA IT support: **[name, phone, e-mail — to be filled in at go-live]**.

When reporting a problem, say what you were doing, the animal number, and the time. A screenshot helps.
