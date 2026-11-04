# Production Planning: What Was Built (Frontend + Backend)

Simple explanation of every change, for the team and the backend developer.

**Branches**
- Frontend `C:\BIS\BTM_Soft` → branch **`feature/production-planning`**
- Backend `C:\BIS\BTM_Backend_Collab` → new git repo: `main` = the backend as received (baseline), **`feature/production-planning`** = all the changes below

---

## 1. How to deploy and test (do these in order)

1. **Database:** run these new scripts on the EMSSoft database, **in this order**, from SSMS (or `sqlcmd` with `-I`):
   1. `SQL/58_PlanningFieldsExistingPages.sql`: new columns on existing tables, plus the Idle Reason / Rejection Reason lists.
   2. `SQL/59_ProductionPlanning.sql`: Work Calendar and Production Plan tables and procedures.
   3. `SQL/60_CustomerMaterialInward.sql`: Customer Material Inward tables, procedures and the balance view.

   All three are safe to run again. They do not touch existing data.
2. **API:** build and publish the backend from the `feature/production-planning` branch. The API needs script 58 to have run first, because it reads the new columns.
3. **Frontend:** the planning pages now call the real API (`USE_MOCK_DATA = false`). The dummy data file `Config/js/planning-mock-store.js` is kept only for reference and offline demos; no page loads it.
4. **Menu:** add the new pages to the menu and access template yourself, as you planned:
   - `Modules/Masters/WorkCalendar.html`
   - `Modules/Production/ProductionPlan.html`
   - `Modules/Production/ProductionSchedule.html`
   - `Modules/PurchaseReports/PlanVsActualReport.html`
   - `Modules/PurchaseReports/RMRequirementReport.html`
   - `Modules/PurchaseReports/RMStockStatement.html`
   - `Modules/SalesReports/PendingOrderReport.html`
   - `Modules/Store/CustomerMaterialInward.html`
5. **Master data the plan needs**, so the pages show something:
   - **Machine Master:** hammers have the Forging operation ticked; fill Shifts/Day, Efficiency % and Min/Max piece weight.
   - **Routing Chart:** each part + grade has a route with a Forging step. Fill Post-Forging Days on the header and Setup Time on the Forging step.
   - **ConvBOM:** each part + grade has its raw-material line (Cut Weight per piece, Bar Dia).
   - **Die Master:** one die per part + grade.
   - **Work Calendar:** one active calendar per month.

---

## 2. New pages and what each one does

| Page | What it does | Backend it calls |
|---|---|---|
| **Work Calendar** | Shift time, break, weekly off and holidays per month → working days and hours | `WorkCalendarController` |
| **Production Plan** | Monthly plan: pick pending orders → jobs per die on a hammer → hammers run in parallel → Approve → Firm week → **Release week creates the job cards** → Reschedule with a reason | `ProductionPlanController` |
| **Production Schedule** | Daily sheet and weekly plan per hammer, with carry-over of unforged qty | `ProductionScheduleController` |
| **Plan vs Actual** | Planned vs forged (Forging-step entries) vs rejected (inspection), with on-time against forge-by | `PlanVsActualReportController` |
| **RM Requirement** | Bar kg needed (pending orders or a plan period) vs stock, reserved stock and open POs → **Create PR** | `RMRequirementController` |
| **RM Stock Statement** | Bar register: opening, receipt, issued, end piece, closing, value, plan need in the next 7 days | `RMStockStatementController` |
| **Pending Order Report** | Every OA line still to forge: planned or not, planned dates, late against delivery and against forge-by | `PendingOrderReportController` |
| **Customer Material Inward** | Bar the customer sends on their DC, recorded against the OA and kept out of our stock; customer balance view | `CustomerMaterialInwardController` |

---

## 3. The rules the backend follows (simple)

- **Hammer:** a Machine Master machine with the Forging operation ticked. Its speed per day = Shifts/Day × Efficiency %.
- **Job:** one die setup on one hammer. Lines of the same part and the same die go together; Die Master keeps one die per part + grade.
- **Parallel hammers:** each hammer has its own queue, and different hammers work on the same day.
- **Day split:** the server works out the days with **the same rules as the page** (`Models/PlanScheduleEngine.cs` is a copy of `UI_Components/widgets/plan-schedule.js`) and stores them in `ProductionPlanDay`. Every report reads those stored days. **If you change one rule file, change the other.**
- **Pending to forge** = OA qty − MAX(dispatched, good forged). Dispatched is the invoiced qty. Good forged is the job cards' Forging-step qty minus the qty rejected at inspection.
- **Forge-by** = delivery date (the line's Committed Date if filled) − the route's Post-Forging Days − 1 dispatch day.
- **Material allocation**, for each bar (RM part + grade from ConvBOM):
  1. Stock still owed to open job cards (reserved) goes to lines that already have a job card.
  2. Free stock goes next.
  3. Open POs come last, earliest due first.
  4. A PO arriving after the line is needed counts as **PO Late**.

  Customer-material lines need no bar of ours.
- **Release week:** one job card per main plan line whose job **starts** in that week. It carries:
  - the route, die and hammer;
  - the planned start and end;
  - the RM part and grade, with RM kg = qty × cut weight.

  Operator and shift stay blank until production starts. A stock line's job card has OA No 0. A line that is already released is never released twice.
- **Plan status:** Draft → Approved → Released. Jobs change only in Draft. After that, change them only with **Reschedule** (it needs a reason and is logged). An amendment (new revision) puts the plan back to Draft.

---

## 4. Changes to existing pages (frontend) and their backend

Every new field is additive. Old requests still work, and a request that leaves a new field out keeps the stored value or the default.

| Page | New fields / behaviour | Why |
|---|---|---|
| Machine Master | Capacity Unit, Shifts/Day, Efficiency %, Min/Max Piece Wt | The plan uses the hammer's real speed and puts a part only on a hammer that can take its weight |
| Routing Chart | Post-Forging Days (header), Setup Time (step); time labelled per batch | Forge-by date and correct job days |
| Die Master | Die Status (Ready / In Repair / Scrapped) | The plan warns when a job uses a die that is not ready |
| ConvBOM | Qty shown as Cut Weight (kg/pc); Bar Dia, Cut Length, Yield % | RM need by bar size |
| Order Acceptance (line) | Committed Date, Urgent, Customer-supplied material | The plan works to the promise, puts urgent orders first, and buys no bar for job work |
| Job Card | Production Plan link (read-only). On a plan job card the planned fields are **locked**; operator and shift can stay blank until production. Qty cannot exceed the OA line | Plan and job card always agree. Change dates and qty through Reschedule |
| Job Card Status | Operator, Shift, Idle Reason, Rejected Qty per entry. The "same product in progress" block now applies only to the **same die** | Shift and operator performance, loss reasons, parallel dies |
| Job Card Inspection | Rejection Reason (needed when rejected > 0) | Quality analysis. Rejected qty goes back to pending |
| Part Master (grade) | Safety Stock, Lead Time (days) | Material planning |
| Purchase Requisition (line) | Required By date | A PR raised from RM Requirement fills it |
| Issue Return | Return Type: Unused / End Piece / Scrap | Track end pieces |
| Store Issue | Heat No per line, picked from the approved heats of that part (new API `HeatNo/Dtl/GetByPart`) | Traceability from bar to forging |

**How the backend saves these:** each existing controller first calls its existing stored procedure. It then calls a small new companion procedure (`SP_<Table>PlanFields`, script 58) in the same transaction. No existing stored procedure was rewritten.

**New Other Master lists (seeded by script 58):**
- Idle Reason: Die Change, Waiting For Material, Breakdown, No Operator, Power Cut.
- Rejection Reason: Crack, Under Fill, Lap, Mismatch, Dimension.

---

## 5. Backend files

| File | What |
|---|---|
| `SQL/58_PlanningFieldsExistingPages.sql` | New columns and companion procedures for the existing pages; Idle / Rejection Reason lists |
| `SQL/59_ProductionPlanning.sql` | `WorkCalendarMst/Holiday`; `ProductionPlanMst/Week/Job/Line/Day/Reschedule`; procedures `SP_WorkCalendar`, `SP_ProductionPlanMst/Dtl/Days/Action/Release/Reschedule` |
| `SQL/60_CustomerMaterialInward.sql` | `CustMatInwardMst/Dtl`, `VW_CustomerMaterialStock`, its procedures |
| `Models/PlanScheduleEngine.cs` | The planning rules (same as the page's `plan-schedule.js`) |
| `Models/PlanningModels.cs`, `EMSContext.Planning.cs` | Request models and procedure wrappers for planning |
| `Models/EMSContext.PlanningFields.cs` | Procedure wrappers for the existing-page fields |
| `Models/CustomerMaterialModels.cs`, `EMSContext.CustomerMaterial.cs` | Customer material models and wrappers |
| `Controllers/PlanningControllerBase.cs` | Shared reads and maths: hammers, calendar, plan, day split, pending, material allocation |
| `Controllers/WorkCalendarController.cs`, `ProductionPlanController.cs`, `ProductionScheduleController.cs`, `PlanVsActualReportController.cs`, `PendingOrderReportController.cs`, `RMRequirementController.cs`, `RMStockStatementController.cs`, `CustomerMaterialInwardController.cs` | The new APIs |
| Existing controllers and models changed | MachineMaster, RoutingChart, ConvBOM, DieMaster, OA, MaterialGrade, PurReq, StoreIssue, StoreReturns, HeatNo, JobCard, JobCardStatus, JobCardInspection: they read and write the new fields |

---

## 6. What was tested

All tests ran on a copy of the database (`EMSSoft_Planning`, restored from `EMSSoft.bak` on this PC); the real database was not touched.
- **Scripts:** all existing scripts 02–57, then 58, 59 and 60, ran twice with no errors.
- **Build:** the backend builds with 0 errors. It targets .NET 7; it was run here on .NET 8 with roll-forward.
- **Planning, end to end through the API:**
  - Calendar save, including the duplicate check.
  - Plan save → jobs → approve.
  - Saving jobs after approval is refused, and releasing before firming is refused.
  - Firm → **release created 2 job cards** carrying the plan link, route, RM part and kg, die and hammer, with blank operator and shift.
  - Reschedule, with a reason, is logged.
  - The schedule (day and week), plan vs actual, pending order report, RM summary and lines (pending and plan basis) and RM stock statement all return data.
  - **Create PR** made a Draft PR whose line has Required By and the OA references.
- **Existing-page fields:** each one was saved and read back through the API. Refusal cases were tested too: efficiency > 100, yield > 100, a bad status, a rejected qty above the processed qty, a missing rejection reason, and an unapproved heat number.
- **Same-die rule:** tested both cases. A card with no die is still blocked; a card on a different die runs in parallel.
- **Customer Material Inward:** create, read, edit, balance, delete with reason, and its refusals were tested.
- **Production Plan page:** its own script was run against the live test API, and it reads every real response correctly.

**Not tested:** the pages have not been opened in a browser against the new API. `session.js` points to the main server. Please test there once scripts 58–60 are run and the API is deployed.

---

## 7. Known limits / next steps

- **Customer material stock:** the inward is recorded and the balance shows received kg, but **issued and returned are still 0**. Store Issue and the outward DC do not yet draw from the customer balance. RM Requirement treats customer-material lines as needing no bar of ours.
- **Return Type:** Scrap returns are still counted back into stock by `VW_StoreStock`. Excluding them is a decision for later.
- **Die Status:** it is not yet checked when production entries are saved; only the plan warns.
- **Stock job cards:** they carry OA No 0, so the Job Card page shows OA "0-0/0" for them.
- **Planned machine downtime** from Maintenance PM dates is not applied to hammer capacity yet (phase 2).
- **`PurchaseRequisitionA.html`:** this older copy of the PR page was not changed.
- **Two rule files:** `Models/PlanScheduleEngine.cs` (server) and `UI_Components/widgets/plan-schedule.js` (page) must stay identical.
