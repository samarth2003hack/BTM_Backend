# Purchase Order → GRN → Store → Heat No. → Inward Inspection

**Module reference for the EMSSoft API.**
Companion to `API_DOCUMENTATION.md`, which covers the conventions all of these
endpoints follow (response envelope, `X-Token` authentication, soft deletes,
`subscID` / `userNo` parameters). Nothing in this document overrides those.

Built from sections **2.4** and **2.5** of
`Purchas, Store, Sales And Production Tables.txt`, with the process flow taken
from the AquaFlow application (`D:\MVC Software\AquaFlow`).

---

## 1. What this module covers

Six operations, in the order the material moves:

| # | Operation | Controller | Writes |
|---|---|---|---|
| 1 | Purchase Order | `PurchaseOrder` | `POMst`, `PODtl`, `POTerms`, `POExtras` |
| 2 | Goods Receipt (GRN) | `GRN` | `GRNMst`, `GRNDtl`, `GRNQRCode`, `GRNAttachment` |
| 3 | Store Allocation | `StoreAllocation` | `GRNDtl.Store` / `.Location`, `StoreLocation` |
| 4 | Heat No. Entry | `HeatNo` | `HeatNoMst`, `HeatNoDtl`, `HeatRefNo` |
| 5 | Inward Inspection (PO) | `InwardInspection` | `GRNDtl` inspection qty, `GRNNCDtl`, `GRNMst.InspNo` |
| 6 | Inward Inspection (Closure) | `InwardInspection` | `GRNMst.NCClose*` |

**13 tables · 20 stored procedures · 5 views · 5 controllers · 87 endpoints.**

| Controller | Endpoints |
|---|---|
| `PurchaseOrder` | 26 |
| `GRN` | 21 |
| `HeatNo` | 15 |
| `InwardInspection` | 15 |
| `StoreAllocation` | 10 |

---

## 2. Installing it

Run the three scripts in order against `EMSSoft`. All are idempotent.

```
EMSAPI/SQL/13_PurchaseOrderGRNTables.sql       tables, indexes, views, audit registration
EMSAPI/SQL/14_PurchaseOrderProcedures.sql      SP_PO*, SP_POGenerateFromQuote, source views
EMSAPI/SQL/15_GRNProcedures.sql                SP_GRN*, SP_Store*, SP_HeatNo*
```

```powershell
sqlcmd -S "lpc:SERVER\INSTANCE" -d EMSSoft -E -b -i 13_PurchaseOrderGRNTables.sql
sqlcmd -S "lpc:SERVER\INSTANCE" -d EMSSoft -E -b -i 14_PurchaseOrderProcedures.sql
sqlcmd -S "lpc:SERVER\INSTANCE" -d EMSSoft -E -b -i 15_GRNProcedures.sql
```

Script 13 calls `SP_AuditLog_Sync`, so all thirteen tables are registered in
`AuditConfig` and get their `TR_Audit_*` trigger automatically.

### One environment note

This server has stray `SP_POMst`, `SP_PODTL`, `SP_POExtras`, `SP_GRNMst` and
`SP_GRNDtl` procedures **in the `master` database**, left by an older script
that ran without a `USE` statement. Because the names begin with `sp_`, SQL
Server resolves them against `master` first, and `CREATE OR ALTER PROCEDURE
dbo.SP_POMst` inside `EMSSoft` therefore fails with
`Msg 208 … Invalid object name`.

Script 14 works around it — each procedure is created as an empty stub if
missing and then `ALTER`ed — so it installs cleanly either way. Calls are
unaffected because every `EXEC` in this codebase is schema-qualified
(`EXEC dbo.SP_…`), which pins the name to the current database.

Tidying `master` is still worth doing, separately and deliberately:

```sql
USE master;
DROP PROCEDURE dbo.SP_POMst, dbo.SP_PODTL, dbo.SP_POExtras,
               dbo.SP_GRNMst, dbo.SP_GRNDtl;
```

---

## 3. The flow

```
PurReqMst (approved)
      │
      ├── has an APPROVED supplier quote ──────────┐
      │      pick PR No. + RFQ No.                 │
      │      order generates itself, no popup      │  TargetType 'PQ'
      │                                            │
      └── no approved quote ───────────────────────┤
             pick PR No., then items in the popup  │  TargetType 'PR'
                                                   ▼
                              (1) PURCHASE ORDER  POMst
                                  LockStatus 'Y' + DocAmdStatus 'N'
                                  = the live, receivable revision
                                                   │
                                                   ▼
                              (2) GOODS RECEIPT   GRNMst    GRNType 'N'
                                  BillQty ≤ PO line balance
                                                   │
                        ┌──────────────────────────┼──────────────────────┐
                        ▼                          │                      ▼
        (3) STORE ALLOCATION                       │        (4) HEAT No. ENTRY
            GRNDtl.Store / .Location               │            HeatNoMst → GRN
                                                   ▼
                              (5) INWARD INSPECTION (PO)     GRNType 'Q'
                                  Acc / Rej / Rew / AOD  +  GRNNCDtl
                                                   │
                                                   ▼
                              (6) INWARD INSPECTION (CLOSURE) GRNType 'C'
```

### GRNType — the inward state machine

| Value | Stage | Set by |
|---|---|---|
| `N` | Received, awaiting inspection | `SP_GRNMst` Insert |
| `Q` | Inspected — `InspNo`, `QCDate`, `QCverifyBy` stamped | `SP_GRNInspPost` Post |
| `C` | Non-conformance closed | `SP_GRNNCClose` Close |

`VW_GRNInwardStatus` exposes this as `StageName` (`Received` / `Inspected` /
`NC Closed`). All three worklists read that view, so they cannot disagree
about which stage a receipt is at.

### Why each stage is a separate procedure

Each stage writes only its own columns. Goods receipt never writes an
inspection quantity; inspection never writes a received quantity; store
allocation touches only `Store` and `Location`. That is what lets stores,
QC and the bin clerk work the same GRN at once without overwriting one
another — and it is why inspection is not folded into `SP_GRNDtl`.

---

## 4. Business rules the database enforces

These live in the stored procedures, not in the browser, so they hold however
the endpoint is called. Each returns `ID = 0` plus a message, which the
controller turns into **409 Conflict** carrying that wording verbatim.

### Purchase order

| Rule | Message when refused |
|---|---|
| Only the current, approved quote can be ordered | *Approved supplier quote not found. Only the current, approved revision can be ordered.* |
| A quote is ordered once | *A purchase order already exists against this quote.* |
| An approved order cannot be edited | *Purchase Order is approved. Unlock it, or raise an amendment, before editing.* |
| An order with no lines cannot be approved | *Add at least one line before approving.* |
| An order that has received goods cannot be unlocked or deleted | *Goods have been received against this order. …* |

### Goods receipt

| Rule | Message when refused |
|---|---|
| Receipt only against a current, approved PO | *Approved purchase order not found. Goods can only be received against the current, approved revision.* |
| `BillQty` ≤ the PO line's outstanding balance | *Billed quantity 150 exceeds the balance of 100 on purchase order line 1.* |
| An inspected GRN cannot be edited | *This GRN has been inspected. Reopen the inspection before changing lines.* |

### Inward inspection

| Rule | Message when refused |
|---|---|
| `AccQty + RejQty + RewQty + AODQty = RecQty`, per line | *Accepted + rejected + rework + AOD is 55, but 60 was received. Every piece must be accounted for.* |
| Every line inspected before posting | *Line 1 has not been fully inspected. …* |
| Non-conforming quantity backed by NC lines that **add up** | *Line 1: Reject quantity is 5 but the non-conformance detail adds up to 3.* |
| NC on a line cannot exceed what was received | *Non-conformance of 12 exceeds the received quantity of 10.* |
| Cannot post twice | *This GRN has already been inspected.* |

> The legacy AquaFlow application checked only that *at least one* NC row
> existed for a rejected quantity. Here the quantities must reconcile, so a
> rejection of 5 backed by a single NC line for 3 is caught.

### Closure

| Rule | Message when refused |
|---|---|
| Must be inspected first | *Post the inward inspection before closing it.* |
| Must actually have non-conformance | *There is no non-conformance on this GRN to close.* |
| Remarks required | *Closure remarks are required.* |

### Store allocation and heat numbers

| Rule | Message when refused |
|---|---|
| The bin must exist on `StoreLocation` | *Store / location "NOWHERE / RACK-Z" is not on the store location master.* |
| Heat quantities ≤ the heat document's quantity | *Heat quantities would total 70, more than the 60 on the document.* |
| One heat number per part | *Heat number HT-9001 has already been recorded for this part.* |
| The part must have been received on that GRN | *That part was not received on this GRN.* |

---

## 5. `api/PurchaseOrder`

### 5.1 Source picker

This is the part of the screen that decides whether the buyer sees an item
popup or not.

| # | Method | Route | Parameters |
|---|---|---|---|
| 1 | `GET` | `/Source/PurReqList/{subscID}` | *(query)* `docYear`, `sourceMode`, `search` |
| 2 | `GET` | `/Source/QuoteList/{prTransNo}/{subscID}` | — |
| 3 | `GET` | `/Source/QuoteItems/{quoteTransNo}/{subscID}` | — |
| 4 | `GET` | `/Source/PurReqItems/{prTransNo}/{subscID}` | *(query)* `onlyPending=true` |

**`Source/PurReqList`** returns approved requisitions, each already told apart:

```json
{ "prTransNo": 2, "prDocYear": 2627, "prDocNo": 1,
  "reqBy": "Ravi", "reqDept": "Production", "rfqReq": "Y",
  "lineCount": 2, "rfqCount": 1, "quoteCount": 2, "approvedQuotes": 1,
  "hasRFQ": "Y", "sourceMode": "Quote" }
```

`sourceMode` drives the screen:

- **`Quote`** — an approved supplier quote exists. Show the RFQ No. dropdown
  (`Source/QuoteList`), and on selection call `Mst/GenerateFromQuote`.
  **No item popup** — there is nothing left to choose.
- **`Items`** — no approved quote. Open the item popup
  (`Source/PurReqItems`), then `Mst/SaveData` + `Dtl/SaveData` per item.

**`Source/QuoteList`** lists only quotes that are the current revision
(`DocAmdStatus = 'N'`) **and approved** (`LockStatus = 'Y'`). A quote already
turned into an order still appears, with `poTransNo` / `poDocNo` set, so the
buyer can see what happened rather than wonder where it went.

**`Source/PurReqItems`** subtracts everything already ordered — by *either*
route, `TargetType 'PR'` directly or `TargetType 'PQ'` through a quote — so a
part cannot be ordered twice by taking two different paths to it.

### 5.2 Header

| # | Method | Route | Parameters |
|---|---|---|---|
| 5 | `POST` | `/Mst/GenerateFromQuote` | *(body)* `SP_POGenerateFromQuote` |
| 6 | `POST` | `/Mst/SaveData` | *(body)* `SP_POMst` |
| 7 | `POST` | `/Mst/DeleteData/{id}` | *(query)* `userNo`, `subscID`, `reason` |
| 8 | `POST` | `/Mst/LockUnlock/{id}` | *(query)* `userNo`, `subscID`, `action=Lock\|Unlock` |
| 9 | `POST` | `/Mst/Amend/{id}` | *(query)* `userNo`, `subscID`, `notes`, `docDate` |
| 10 | `GET` | `/Mst/GetList/{subscID}` | *(query)* `docYear`, `docSeries`, `vendorNo`, `targetType`, `lockStatus`, `currentOnly=true`, `search` |
| 11 | `GET` | `/Mst/GetData/{id}/{subscID}` | — |
| 12 | `GET` | `/Mst/GetFullData/{id}/{subscID}` | — |
| 13 | `GET` | `/Mst/GetAmendments/{docSeries}/{docYear}/{docNo}/{subscID}` | — |

**`Mst/GenerateFromQuote`** is the "no item picker" path. It builds the entire
order in one call — header, lines, rates and terms — and returns it loaded,
so the screen can display it without a second round trip.

```json
POST /api/PurchaseOrder/Mst/GenerateFromQuote
{
  "quoteTransNo": 1,
  "docSeries": "PO", "docYear": 2627, "docDate": "2026-09-21",
  "dueDate": "2026-10-05", "purSubject": "From approved quote",
  "refNo": "Q-101", "refDate": "2026-09-21",
  "txnCurrency": "INR", "subscID": 1, "createdBy": 3
}
```

```json
{ "success": true,
  "message": "Purchase Order generated from quote 2627-1 with 2 line(s).",
  "data": {
    "header": { "mTransNo": 4, "docYear": 2627, "docNo": 2,
                "targetType": "PQ", "targetTransNo": 1, "source": "PR 2627-1",
                "vendorName": "…", "taxType": "Intra State",
                "basicValue": 43200.00, "taxableValue": 43200.00,
                "cgstAmt": 3888.00, "sgstAmt": 3888.00, "netValue": 50976.00 },
    "lines":  [ … ], "terms": [ … ], "extras": [ ] } }
```

The requisition stays traceable without a column of its own:
`POMst → RFQPriceMst → RFQMst.PRTransNo → PurReqMst`, and `POMst.Source`
carries the readable breadcrumb (`"PR 2627-1"`) for the printed order.

**`Mst/SaveData`** — the typed path. `targetType` must be `PQ`, `PR`, `Direct`
or `Pattern`; `PQ` and `PR` require `targetTransNo`.

Not in the request, on purpose:

- **the vendor block** (code, name, address, GSTIN, state, tax type) — the
  procedure snapshots it from `SupplierMaster`, so an order printed next year
  keeps the address it was placed on;
- **the totals** — recomputed from the lines on every write, so the printed
  order can never disagree with what is on it.

Tax type comes from `SupplierMaster.TaxType`; when that is blank it is derived
by comparing `SupplierMaster.StateCode` against `Company.StateCode`
(same → `Intra State`, different → `Inter State`).

**`Mst/Amend/{id}`** opens the next revision as a copy of the current one —
lines, terms and extras all carried forward, the old revision flipped to
`DocAmdStatus = 'Y'`. Returns the new revision loaded. *(This is an addition:
AquaFlow made the buyer re-key an amendment by hand.)*

### 5.3 Lines, terms, user fields

| # | Method | Route | Parameters |
|---|---|---|---|
| 14 | `POST` | `/Dtl/SaveData` | *(body)* `SP_PODtl` |
| 15 | `POST` | `/Dtl/DeleteData/{transSlNo}` | *(query)* `mTransNo`, `userNo`, `subscID`, `reason` |
| 16 | `POST` | `/Dtl/DeleteAll/{mTransNo}` | *(query)* `userNo`, `subscID`, `reason` |
| 17 | `GET` | `/Dtl/GetList/{mTransNo}/{subscID}` | — |
| 18 | `GET` | `/Dtl/GetData/{transSlNo}/{subscID}` | — |
| 19–23 | | `/Terms/SaveData`, `/Terms/DeleteData/{transSlNo}`, `/Terms/DeleteAll/{mTransNo}`, `/Terms/GetList/{mTransNo}/{subscID}` | *(body)* `SP_POLine` |
| 24–27 | | `/Extras/…` — same four shapes | *(body)* `SP_POLine` |

`Dtl/SaveData` takes only what is negotiable: `partNo`, `qty`, `rate`,
`discP`, `pnFP`, `freightAmt`, `deleveryDate`, `grade`, `itemRemarks`. Part
code, description, HSN, units, conversion ratio and the GST percentage are
read from `PartMaster` by the procedure.

Line arithmetic (`SP_PODtlPrice`, one definition shared by the typed and the
generated path):

```
BasicValue   = Qty × Rate
DiscAmt      = BasicValue × DiscP%
BasicRate    = Rate − (Rate × DiscP%)
PnFAmt       = (BasicValue − DiscAmt) × PnFP%
TaxableValue = BasicValue − DiscAmt + PnFAmt + FreightAmt
GST          Intra State → CGST + SGST at half the rate each
             Inter State → IGST at the full rate
             anything else (export, exempt) → none
TotalValue   = TaxableValue + IGST + CGST + SGST
```

Header: `NetValue = TaxableValue + GST + OtherAmt + RoundOff`.

---

## 6. `api/GRN`

| # | Method | Route | Parameters |
|---|---|---|---|
| 1 | `GET` | `/Source/POList/{subscID}` | *(query)* `docYear`, `vendorNo`, `search` |
| 2 | `GET` | `/Source/POLines/{poTransNo}/{subscID}` | *(query)* `excludeGRN=0`, `onlyPending=true` |
| 3 | `POST` | `/Mst/SaveData` | *(body)* `SP_GRNMst` |
| 4 | `POST` | `/Mst/DeleteData/{id}` | *(query)* `userNo`, `subscID`, `reason` |
| 5 | `POST` | `/Mst/LockUnlock/{id}` | *(query)* `userNo`, `subscID`, `action` |
| 6 | `GET` | `/Mst/GetList/{subscID}` | *(query)* `docYear`, `docSeries`, `vendorNo`, `grnType`, `targetType`, `fromDate`, `toDate`, `search` |
| 7 | `GET` | `/Mst/GetData/{id}/{subscID}` | — |
| 8 | `GET` | `/Mst/GetFullData/{id}/{subscID}` | — |
| 9 | `GET` | `/Mst/GetByPO/{poTransNo}/{subscID}` | — |
| 10–14 | | `/Dtl/SaveData`, `/Dtl/DeleteData/{transSlNo}`, `/Dtl/DeleteAll/{mTransNo}`, `/Dtl/GetList/…`, `/Dtl/GetData/…` | |
| 15–18 | | `/QRCode/Generate`, `/QRCode/DeleteData/{transSlNo}`, `/QRCode/DeleteAll/{mTransNo}`, `/QRCode/GetList/{mTransNo}/{subscID}` | |
| 19–21 | | `/Attachment/Upload`, `/Attachment/DeleteData/{transSlNo}`, `/Attachment/GetList/{mTransNo}/{subscID}` | |

### Receiving against an order

**`Source/POLines`** returns each order line with `balanceQty` — the most a new
receipt may bill. Pass `excludeGRN` when **editing** a saved GRN, so that
GRN's own quantities are added back into the balance; without it, reopening a
receipt would show the lines it already booked as unavailable.

```json
{ "poTransSlNo": 7, "linkNo": 1.00, "partNo": 11, "partCode": "ZZP1",
  "unit": "Nos", "rate": 250.00, "discP": 10.00, "gstP": 18.00,
  "poQty": 100.0000, "billedQty": 0.0000, "balanceQty": 100.0000,
  "grnTransSlNo": null, "grnBillQty": 0.0, "grnRecQty": 0.0 }
```

**`Dtl/SaveData`** — `poTransSlNo` names the order line being received.
From it the procedure takes the **line number, the rate, the discount and
packing percentages, the grade and the order quantity**, so a receipt cannot
quietly be booked at a rate the order does not carry. On a direct GRN, send
`poTransSlNo: 0` and supply the rate.

`GRNDtl.SlNo` carries the `PODtl.SlNo` of the line received. That is the join
the PO balance is computed over (`VW_POPendingGRN`), so it is never renumbered
on a PO-backed GRN.

The value of a receipt follows the **billed** quantity — that is what the
supplier is owed for. What physically arrived (`RecQty`) and what survives
inspection are tracked separately.

GRN header adds TDS, TCS and the bill comparison:

```
NetValue    = Taxable + GST + OtherAmt + TCSAmt − TDSAmt + RoundOff
DebitValue  = BillValue − NetValue   when the supplier over-billed
CreditValue = NetValue − BillValue   when the supplier under-billed
```

### QR labels

`QRCode/Generate` prints `lQty` labels for one receipt line, numbered from the
last one already issued on that line — so a reprint of extra labels continues
the sequence rather than restarting it. The code reads
`{DocYear}-{DocNo}-{LineNo}-{Serial}`, e.g. `2627-1-2-3`. Capped at 1000 per
call.

### Attachments

`Attachment/Upload` is `multipart/form-data`: `file`, `mTransNo`, `userNo`,
`subscID`, `attNotes`.

The bytes go to `wwwroot/Uploads/GRN/{mTransNo}/` and only the relative path
is stored on the row. Three rules are enforced in `GrnUploads`:

- **extension allowlist** (`.pdf .jpg .jpeg .png .gif .bmp .webp .tif .tiff
  .doc .docx .xls .xlsx .csv .txt .rtf .zip .rar .7z .eml .msg .dwg .dxf`) —
  the upload folder sits under the web root and is reachable by URL, so an
  `.html` or `.js` file dropped into it would be served from the
  application's own origin, which is how an attachment field turns into
  stored XSS;
- **generated filename** — only the extension survives, so a traversal name
  or a collision is impossible;
- **25 MB limit**.

Deleting an attachment soft-deletes the row and **keeps** the file, so the
audit trail still leads to what was attached.

---

## 7. `api/StoreAllocation`

| # | Method | Route | Parameters |
|---|---|---|---|
| 1 | `GET` | `/Worklist/{subscID}` | *(query)* `docYear`, `vendorNo`, `includeDraft=false`, `includeDone=false` |
| 2 | `GET` | `/Lines/GetList/{mTransNo}/{subscID}` | *(query)* `onlyPending=false` |
| 3 | `POST` | `/Allocate` | *(body)* `SP_GRNStoreAlloc` |
| 4 | `POST` | `/AllocateAll/{mTransNo}` | *(query)* `userNo`, `subscID`, `store`, `location` |
| 5 | `POST` | `/Clear/{transSlNo}` | *(query)* `mTransNo`, `userNo`, `subscID` |
| 6 | `POST` | `/Master/SaveData` | *(body)* `SP_StoreLocation` |
| 7 | `POST` | `/Master/DeleteData/{id}` | *(query)* `userNo`, `subscID`, `reason` |
| 8 | `GET` | `/Master/GetList/{subscID}` | *(query)* `partNo`, `store`, `search` |
| 9 | `GET` | `/Master/GetStores/{subscID}` | — |
| 10 | `GET` | `/Master/GetLocations/{subscID}` | *(query)* `store`, `partNo=0` |

`AllocateAll` puts every **unallocated** line of a GRN into one bin — the
common case, one delivery to one place. Lines already allocated are left alone.

`Lines/GetList` returns each line with `suggestedBins`: the part's own
registered bins first, then the general ones. A `StoreLocation` row with
`partNo = 0` is a bin any part may go in; a row naming a part is that part's
own.

Allocation is deliberately **not** gated on inspection. Goods have to go
somewhere the moment they are off the lorry, and the inspector needs to be
told where to find them.

---

## 8. `api/HeatNo`

| # | Method | Route | Parameters |
|---|---|---|---|
| 1 | `GET` | `/Source/GRNList/{subscID}` | *(query)* `docYear`, `vendorNo`, `search` |
| 2 | `GET` | `/Source/GRNParts/{grnTransNo}/{subscID}` | — |
| 3 | `POST` | `/Mst/SaveData` | *(body)* `SP_HeatNoMst` |
| 4 | `POST` | `/Mst/DeleteData/{id}` | *(query)* `userNo`, `subscID`, `reason` |
| 5 | `POST` | `/Mst/LockUnlock/{id}` | *(query)* `userNo`, `subscID`, `action` |
| 6 | `GET` | `/Mst/GetList/{subscID}` | *(query)* `docYear`, `docSeries`, `partNo`, `grnTransNo`, `search` |
| 7 | `GET` | `/Mst/GetData/{id}/{subscID}` | — |
| 8 | `GET` | `/Mst/GetFullData/{id}/{subscID}` | — |
| 9–12 | | `/Dtl/SaveData`, `/Dtl/DeleteData/{transSlNo}`, `/Dtl/DeleteAll/{mTransNo}`, `/Dtl/GetList/{mTransNo}/{subscID}` | |
| 13 | `GET` | `/Dtl/CheckHeatNo/{subscID}` | *(query)* `heatNo`, `partNo`, `excludeTransSlNo=0` |
| 14 | `GET` | `/Dtl/GetRefNos/{hTransSlNo}/{subscID}` | — |
| 15 | `GET` | `/Trace/{subscID}` | *(query)* `heatNo` |

A heat document is raised **against a GRN** (`targetTransNo`). The header names
the receipt, the part, the grade and the total quantity covered; each line is
one heat number with its test certificate.

**`Dtl/SaveData`** takes `refFrom` / `refTo` — the piece serial numbers this
heat covers, expanded into `HeatRefNo`. They are **rewritten whole** on every
save of the line, so correcting a range replaces it rather than adding to it.
Send `0` / `0` for none. Ranges over 10,000 pieces are refused; split them
across heat lines.

**`Dtl/CheckHeatNo`** is meant to be called as the heat-number field is left,
so the operator is told before the save is refused:

```json
{ "success": true, "message": "Heat number HT-9001 is already recorded for this part.",
  "data": { "exists": true, "existing": { "transSlNo": 9, "heatNo": "HT-9001",
            "grade": "GR2500", "qty": 40.0, "tcNo": "TC-1" } } }
```

**`Trace`** answers the traceability question: which receipt a heat arrived
on, from which supplier, and how many pieces it covers.

**`Source/GRNParts`** shows each part received on a GRN with how much is
already covered by a heat document (`heatCoveredQty`, `pendingQty`), so the
same receipt is not recorded twice by accident.

---

## 9. `api/InwardInspection`

Both inspection screens, because they are two ends of one process and share a
worklist.

| # | Method | Route | Parameters |
|---|---|---|---|
| 1 | `GET` | `/Worklist/Pending/{subscID}` | *(query)* `docYear`, `vendorNo`, `includeDraft=false`, `search` |
| 2 | `GET` | `/Worklist/Inspected/{subscID}` | *(query)* `docYear`, `vendorNo` |
| 3 | `GET` | `/Worklist/Closure/{subscID}` | *(query)* `docYear`, `vendorNo`, `includeClosed=false` |
| 4 | `GET` | `/Lines/GetList/{mTransNo}/{subscID}` | — |
| 5 | `POST` | `/Lines/SaveData` | *(body)* `SP_GRNInspDtl` |
| 6 | `POST` | `/NC/SaveData` | *(body)* `SP_GRNNCDtl` |
| 7 | `POST` | `/NC/DeleteData/{transSlNo}` | *(query)* `userNo`, `subscID`, `reason` |
| 8 | `POST` | `/NC/DeleteAll/{mTransNo}` | *(query)* `userNo`, `subscID`, `reason` |
| 9 | `GET` | `/NC/GetList/{grnTransSlNo}/{subscID}` | *(query)* `ncType` |
| 10 | `GET` | `/NC/GetByGRN/{mTransNo}/{subscID}` | — |
| 11 | `POST` | `/Post/{mTransNo}` | *(query)* `userNo`, `subscID`, `qcVerifyBy`, `qcDate` |
| 12 | `POST` | `/Reopen/{mTransNo}` | *(query)* `userNo`, `subscID` |
| 13 | `POST` | `/Closure/Close/{mTransNo}` | *(form)* `userNo`, `subscID`, `remarks`, `closeDate`, `file` |
| 14 | `POST` | `/Closure/Reopen/{mTransNo}` | *(query)* `userNo`, `subscID` |
| 15 | `GET` | `/Closure/GetData/{mTransNo}/{subscID}` | — |

### Inward Inspection (PO)

**`Lines/GetList`** gives the inspector everything needed in one read — the
quantities, the verdict so far, and how much non-conformance has been detailed
per type, which is what posting will check:

```json
{ "transSlNo": 4, "slNo": 1.00, "partCode": "ZZP1", "unit": "Nos",
  "billQty": 60.0, "recQty": 60.0,
  "accQty": 55.0, "rejQty": 5.0, "rewQty": 0.0, "aodQty": 0.0,
  "ncRejectQty": 5.0, "ncReworkQty": 0.0, "ncAODQty": 0.0,
  "inspected": true }
```

**`Lines/SaveData`** — `status: "Save"` records the verdict, `"Clear"` zeroes
it. The four quantities must add up to `recQty`.

**`NC/SaveData`** — `ncType` is `Reject`, `Rework` or `AOD`. The part, grade
and unit are copied from the receipt line, so an NC cannot describe something
that was not received.

> `NC/DeleteAll/{mTransNo}` takes the **GRN**, not a receipt line — it clears
> every non-conformance on the document.

**`Post`** allocates the inspection number, stamps `QCDate` / `QCverifyBy`,
and moves the GRN to `Q`. This is where the whole document is checked, because
only here is it certain the inspector has finished. A refusal names the line
and the shortfall. Returns the refreshed `VW_GRNInwardStatus` row.

**`Reopen`** takes it back to `N`. Refused once the non-conformance is closed
— reopen the closure first.

### Inward Inspection (Closure)

**`Closure/Close`** accepts `multipart/form-data` (with a file) or
`application/x-www-form-urlencoded` (without). Evidence goes to
`wwwroot/Uploads/GRNNC/{mTransNo}/` under the same rules as GRN attachments.

```
POST /api/InwardInspection/Closure/Close/3
Content-Type: application/x-www-form-urlencoded

userNo=3&subscID=1&remarks=Debit%20note%20raised%20on%20supplier
```

```json
{ "success": true, "message": "Non-conformance closed against 2 line(s).",
  "data": { "mTransNo": 3, "stageName": "NC Closed", "grnType": "C",
            "inspNo": "1", "ncCloseDate": "2026-09-21T00:00:00",
            "ncLines": 2, "totalNCQty": 5.0000 } }
```

---

## 10. Views

| View | Answers |
|---|---|
| `VW_PurReqForPO` | Which requisitions can be ordered, and by which route (`SourceMode`) |
| `VW_QuoteForPO` | Which approved quotes a requisition offers, and whether each is already ordered |
| `VW_PurReqItemsForPO` | The item picker's rows, net of everything already ordered by either route |
| `VW_POPendingGRN` | PO lines with something still to receive (`BalanceQty`) |
| `VW_GRNInwardStatus` | One row per GRN: stage, quantity totals, NC totals, unallocated lines |

All five are keyless and mapped with `ToView`, so EF never writes through them.

---

## 11. Deviations from the source documents

**1. `UQ_GRNDtl`.** `Purchas, Store, Sales And Production Tables.txt` declares
`UNIQUE (DocNo, DeleteStatus, SubscID)` on `GRNDtl`, which would permit exactly
**one line per GRN**. Replaced with a filtered unique index,
`UQ_GRNDtl_Line` on `(SubscID, MTransNo, SlNo) WHERE DeleteStatus = 'N'` — one
*live* line per line number per GRN. The filter matters: a PO-backed line takes
its number from the order, so the number comes back if the line is deleted and
re-added, and a `DeleteStatus`-in-the-key constraint would then collide on the
second delete.

**2. `GRNMst.GRNType` defaults to `'N'`.** The source declares it nullable and
the legacy application leaves it null. It is the stage marker for the whole
inward flow and every worklist filters on it, so it now defaults to `'N'`
(received). Nothing else about the column changes.

**3. Additions beyond AquaFlow.** Each earns its place:

- NC **quantities must reconcile** with the rejected/rework/AOD quantity, not
  merely exist.
- `Acc + Rej + Rew + AOD = RecQty` is enforced per line.
- `BillQty` is checked against the PO line balance **in the procedure**;
  AquaFlow computed the balance for display only.
- Store and location are checked against `StoreLocation`.
- Heat totals and duplicate heat numbers are checked.
- `Mst/Amend` copies a revision forward instead of making the buyer re-key it.
- Header totals are recomputed from the lines rather than posted by the caller.
- Attachments have an extension allowlist and generated filenames.

---

## 12. What is not included

- **Front-end pages.** `EMSSoft/Modules/Purchase/` is still empty. The sidebar
  is driven by `dbo.Menulist`, and no `Menulist` rows were seeded, since the
  convention here is to seed only pages that exist on disk.
- **Stock posting.** Accepted quantity (`AccQty + AODQty`) is recorded but not
  yet posted to a stock ledger — section 2.9 (`MRNMst`, `StoreIssueMst`,
  `StoreReturnsMst`) is where that belongs and is not part of this work.
- **Job work, pattern movement and RGP** (sections 2.6–2.8), which are
  material-movement branches of the same module.
- **Reports and print layouts** — AquaFlow's `PurchaseOrderReportController`,
  `GRNReportController` and `GRNQCReportController` have no equivalent here.
- **Authorization.** As noted in `API_DOCUMENTATION.md §1.3`, `TokenAuthFilter`
  authenticates but does not check that the caller is entitled to the `subscID`
  they pass.
