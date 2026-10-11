# User acceptance test (UAT) plan

Acceptance of FELCRA Cattle Profiling by FELCRA before the Pagoh pilot goes live (quotation milestone: UAT /
pilot, 20%). Testers record results in [`uat/FELCRA-UAT-test-cases.xlsx`](uat/FELCRA-UAT-test-cases.xlsx), which is
generated from the tables in this document (`dotnet run tools/uat-workbook.cs`).

## 1. Scope and approach

| | |
|---|---|
| **Environment** | Staging (`https://staging.<domain>`), deployed as production (HTTPS, release image) with the Pagoh demo data (`SEED_DEMO_DATA=true`, census imported) |
| **Testers** | HQ: 1–2 veterinary / livestock officers · Pagoh: farm manager (PIC) · 2–3 field officers with their own phones (Android and iPhone if used) |
| **Duration** | 1 week of testing + 1 week of fixes and re-test |
| **Basis** | Requirements in [URS.md](URS.md) §4 (IDs FR-…); user manuals in [manual/](manual/) |
| **Already verified by the supplier** | 171 automated tests (unit, API, security) on every change in CI; 9 browser test suites (`tests/e2e`) run before each release; production stack, backup and restore drill (Step 9) |

UAT checks that the system does the job **for FELCRA's people and data**. It is not a repeat of the automated tests:
use real tags, real phones, real network conditions at the pen.

### Entry criteria
- Staging deployed with the release candidate; all automated tests green.
- Tester accounts created (one per person) and the manual handed out.
- Phones: Android 10+ with Chrome, or iPhone with iOS 16.4+; field app installed (manual §1).
- A sheet of printed QR labels for at least 10 Pagoh animals.

### Exit criteria (UAT sign-off)
- Every **Must** test case executed; **no open Critical or High** defects.
- Medium / Low defects either fixed or accepted into the punch list with a date.
- Sign-off by FELCRA's project owner (section 6).

### Defect severity
| Severity | Meaning | Example |
|---|---|---|
| Critical | Data lost or wrong, or a main task impossible; no workaround | Records made offline never reach the server |
| High | Main task blocked or wrong result, workaround is awkward | Census report counts wrong |
| Medium | Wrong but a workaround exists, or a secondary function fails | A filter ignores one category |
| Low | Cosmetic, wording, convenience | Typo, column too narrow |

Report each defect with: test ID, steps, expected vs actual, screenshot, user, time. Log them in the workbook's
*Defects* sheet (or FELCRA's tracker).

## 2. Test cases

`Pri` = priority from the URS (M must, S should, C could). Roles: **HQ** HQ admin, **FM** farm manager,
**FO** field officer. Each case starts signed in as that role unless stated.

### 2.1 Sign-in and access

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-01 | M | All | FR-AUTH-01 | Sign in to the portal / app with your account. Then sign out and try a wrong password 5 times. | Correct password signs in. After 5 wrong tries the account is locked for 15 minutes with the same message as a wrong password. |
| UAT-02 | M | FO | FR-AUTH-02 | Sign in on the phone, close the app, switch on airplane mode the next day and open the app. | Still signed in; animals and tasks available offline. |
| UAT-03 | M | FM, FO | FR-AUTH-03 | Ask HQ for the tag of an animal on another farm; search for it and scan its label. | Not shown / "This animal is not on any of your farms". |
| UAT-04 | M | HQ | FR-AUTH-04 | Create a field officer for Pagoh, sign in as them; then deactivate the user. | New user works on the phone; after deactivation they cannot sign in, and an open session ends within 15 minutes. |
| UAT-05 | S | All | FR-AUTH-01 | Change your own password (portal: your name → Change password; app: Sync → Account). | Wrong current password is refused; new password works; other devices are signed out. |
| UAT-06 | M | FO | FR-AUTH-03 | Open the admin portal address with a field officer account. | Access refused: field officers use the field app only. |

### 2.2 Farm and master data

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-07 | M | HQ | FR-MD-01, FR-MD-02 | Add a test farm with address, area, capacity and projects EPP5 and RP4. Edit it. | Saved and listed; changes appear in the audit log. |
| UAT-08 | M | FM | FR-MD-03 | Add groups/pens to Pagoh and put two animals in a group. | Group shown on the animal and in the animal list. |
| UAT-09 | M | HQ | FR-MD-04, FR-MD-05 | Add a breed and a treatment product with a withdrawal period; deactivate an old product. | New items available in the app after sync; inactive product no longer offered. |
| UAT-10 | S | HQ | FR-MD-06 | Enter feed ingredients with FELCRA's analysed values and prices. | Values used by the ration planner (UAT-37). |
| UAT-11 | S | HQ | FR-MD-07 | Review the rule values in the HQ guide §7 with the veterinary team. | Values confirmed, or changes requested from IT (server settings, no screen). |

### 2.3 Animal registry

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-12 | M | FM | FR-ANI-01, FR-ANI-03, FR-ANI-04 | Register an animal with an estimated birth date (dentition), breed, project, dam and sire. | Saved with a new animal no.; age shown with "~". |
| UAT-13 | M | FM | FR-ANI-02 | Filter **Duplicate tags** and **Untagged**; re-tag one duplicate with **Change tag**. | Pagoh's duplicates (107, 08478, 135, 136) and 8 untagged animals listed; old tag kept in the tag history. |
| UAT-14 | M | FM | FR-ANI-05 | Mark an animal sold, another dead, with dates and reasons. | They leave the active list, appear in the performance report's "left the herd" and in mortality. |
| UAT-15 | S | FM | FR-ANI-06 | Change a female calf to dam; look at the census history and audit log. | The census keeps the category at the time of each census; the change is in the audit log. |
| UAT-16 | M | FM | FR-ANI-07 | Print QR labels for 10 animals on sticker paper; attach and scan them at the pen. | Labels print legibly; each scan opens the right animal, also for duplicated tag numbers. |
| UAT-17 | M | FM | FR-ANI-08 | Import a census sheet with an error (two animals with the same tag, one row without category). | Preview lists the errors per row; nothing is saved until fixed; then the import succeeds. |

### 2.4 Recording at the pen (field app)

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-18 | M | FO | FR-REC-01 | Scan an animal, record its weight (scale) and a weight by heart-girth tape for another. | Saved; shown in the history and in the portal's field records. |
| UAT-19 | M | FO | FR-REC-02 | Record BCS for 3 animals; try an impossible value. | Saved; impossible value refused with a message. |
| UAT-20 | S | FO | FR-REC-03 | Record milk yield for a dam (if any are milked). | Saved; appears in the lactation analysis. |
| UAT-21 | M | FO | FR-REC-04 | Record a vaccination with dose, route, batch and next due date. | Saved; withdrawal date shown; a task appears near the due date. |
| UAT-22 | S | FO | FR-REC-05 | Record an observation (legs/hooves) with two photos. | Saved; photos viewable in the portal. |
| UAT-23 | M | FO | FR-REC-06, FR-SYNC-01 | In airplane mode: find 5 animals, record weights, a treatment and an observation with a photo. | All saved on the phone ("will be sent when you are back online"); the pending count shows them. |
| UAT-24 | M | FO | FR-SYNC-02 | Switch the network back on. | Records sent automatically within a few minutes; pending count returns to 0; visible in the portal. |
| UAT-25 | M | FO, FM | FR-SYNC-03 | While FO is offline, FM marks one animal sold. FO records its weight offline, then goes online. | The weight is "Not accepted" with the reason; FO can discard it. Other records are not affected. |

### 2.5 Fertility and breeding

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-26 | M | FO | FR-BRD-01 | Record heat for one dam, AI (with straw code) for another, natural service with a bull for a third. | Saved; status "Served"; pregnancy check task 45 days after service. |
| UAT-27 | M | FO | FR-BRD-02 | Record a pregnancy check (pregnant, gestation 60 days). | Status "Pregnant"; expected calving date shown; calving task near the date. |
| UAT-28 | M | FO | FR-BRD-03 | Record a calving with one calf (sex, tag, birth weight) and an assisted calving; record an abortion for another dam. | Calf registered as a new animal with its dam; events in history. |
| UAT-29 | S | FO | FR-BRD-04 | Record a bull breeding soundness exam. | Saved with the result on the bull. |
| UAT-30 | M | FM | FR-BRD-05 | Open Breeding; compare figures with a manual count for Pagoh. | Pregnancy rate, calving interval, calves and calving rate match the manual count. |
| UAT-31 | M | FM | FR-BRD-06 | Review the culling candidates against the rules (age ≥ 8, not pregnant 12 months after calving, 2 empty checks, low BCS). | List matches the rules; reasons shown per animal; exported PDF / Excel identical. |

### 2.6 Census programme

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-32 | M | FM | FR-CEN-01 | Create a census session with dates, PIC, population on record and programme treatments. | Saved and listed. |
| UAT-33 | M | FM | FR-CEN-02 | Add / edit entries: weight, estimated age, remarks, selected. | Saved; selected count updates. |
| UAT-34 | M | FM | FR-CEN-03 | Compare on-record vs counted per category on the session page. | Correct counts and differences (Pagoh: P 11→13, I 61→60, AJ 28→31, AB 40→36). |
| UAT-35 | M | FM, HQ | FR-CEN-04 | Download the census & profiling form (PDF) for the Pagoh July 2026 session; compare with the original report. | Same layout and figures as the FFISB form; selected animals highlighted; English wording (URS question 13). |

### 2.7 Analytics

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-36 | M | FM | FR-ANL-01 | Open Growth; check 3 animals in the "below the curve" list at the pen. | Curve plausible for Pagoh cattle; listed animals are indeed light for their age. |
| UAT-37 | M | FM, FO | FR-ANL-03 | Plan a ration for 20 growing animals of 200 kg with FELCRA's feeds; repeat offline on the phone. | Feasible least-cost mix with cost per day; the same result offline. A nutritionist confirms it is sensible. |
| UAT-38 | S | FM | FR-ANL-02 | Open Lactation (only if milk is recorded). | Curve and 305-day yield; otherwise a clear "no milk yields" message. |

### 2.8 Reminders, dashboard and reports

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-39 | M | FO | FR-NOT-01 | Check the task list after UAT-21, UAT-26, UAT-27. | Treatment due, pregnancy check and calving tasks listed; overdue in red. |
| UAT-40 | S | FO | FR-NOT-02 | Turn on task notifications on the phone; tap **Send a test**; next morning check for the task message. | Test notification arrives; morning message summarises the tasks; tapping opens the app. |
| UAT-41 | M | HQ, FM | FR-RPT-01 | Compare the dashboard (all farms and Pagoh) with the animal list and breeding page. | Head counts, averages, pregnancy rate, deaths / mortality and tag problems agree. |
| UAT-42 | M | FM | FR-RPT-02 | Download each report (farm performance, treatments, culling, census form, selected animals) as PDF and Excel. | Files open; figures agree with the screens; Excel suitable for further analysis. |
| UAT-43 | C | HQ | FR-RPT-03 | **Send me a test e-mail**; after the 1st of the month check the managers' mailboxes. | E-mail with 3 PDF reports arrives; managers receive only their farms. |

### 2.9 Audit and recovery

| ID | Pri | Role | Requirement | Steps | Expected result |
|---|---|---|---|---|---|
| UAT-44 | M | HQ | FR-AUD-01 | Find in the audit log the changes made in UAT-07, UAT-13, UAT-14. | Who, when, old and new values shown for each. |
| UAT-45 | M | HQ | FR-AUD-02 | Delete a test animal, then restore it from **Animals → Deleted animals**. | Animal and its census entries back; "Deleted" and "Restored" in the audit log. |

## 3. Requirement coverage

| Requirement | Status | Test | Notes |
|---|---|---|---|
| FR-AUTH-01–04 | Implemented | UAT-01–06 | Self-service password change added |
| FR-MD-01–06 | Implemented | UAT-07–10 | |
| FR-MD-07 | Implemented as server settings | UAT-11 | Rule values are changed by IT, not on a screen — confirm this is acceptable |
| FR-ANI-01–05, 07, 08 | Implemented | UAT-12–14, 16, 17 | |
| FR-ANI-06 | Implemented via census entries + audit log | UAT-15 | No separate category-history screen — confirm |
| FR-REC-01–06 | Implemented | UAT-18–23 | BCS scale still to confirm (URS question 1) |
| FR-BRD-01–06 | Implemented | UAT-26–31 | |
| FR-CEN-01–04 | Implemented | UAT-32–35 | Report in English (URS question 13) |
| FR-ANL-01, 03 | Implemented | UAT-36, 37 | |
| FR-ANL-02 | Implemented (dairy only) | UAT-38 | Pagoh has no milk records |
| FR-NOT-01, 02 | Implemented | UAT-39, 40 | Push on iPhone needs the app on the Home Screen |
| FR-RPT-01 | Implemented per farm | UAT-41 | Mortality added before UAT. Per-project filter not yet — punch list P-01 |
| FR-RPT-02, 03 | Implemented | UAT-42, 43 | E-mail needs FELCRA SMTP |
| FR-SYNC-01–03 | Implemented | UAT-23–25 | Phone records are append-only, so profile conflicts cannot arise offline; rejected records are kept for review on the phone |
| FR-AUD-01, 02 | Implemented | UAT-44, 45 | Restore of deleted animals added before UAT |

## 4. Known items going into UAT (punch list)

| ID | Item | Proposed |
|---|---|---|
| P-01 | Dashboard filter by project (FR-RPT-01 "per farm/project") | Add after UAT if needed |
| P-02 | Farm managers viewing their farms' audit log (URS §2.1); today HQ only | Add after UAT if needed |
| P-03 | Malay user interface for field officers (URS question 7) | Decide in UAT; the app is prepared for translation |
| P-04 | Two-factor sign-in for HQ admins ([SECURITY.md](SECURITY.md)) | Decide with FELCRA IT |
| P-05 | Breed not captured by the census import (form shows "Not recorded") | Fill breeds per animal, or add a breed column to the sheet |

## 5. Schedule

| Day | Activity |
|---|---|
| 1 | Kick-off and training (see [PILOT.md](PILOT.md) §3); accounts and phones checked |
| 2–3 | HQ and farm manager cases (portal) |
| 3–4 | Field cases at the Pagoh pens, including offline (UAT-18–29) |
| 5 | Reports, analytics, audit; defect triage |
| 6–9 | Fixes deployed to staging; re-test of failed cases |
| 10 | Sign-off meeting |

## 6. Sign-off

| Role | Name | Decision (accepted / accepted with punch list / not accepted) | Signature | Date |
|---|---|---|---|---|
| FELCRA project owner | | | | |
| HQ veterinary representative | | | | |
| Pagoh farm manager (PIC) | | | | |
| Supplier project manager | | | | |
