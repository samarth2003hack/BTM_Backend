# EMSAPI

ASP.NET Core 7 Web API for **EMSSoft**, built to the same shape as LMSAPI:
EF Core for reads, stored procedures for every write, and a single response
envelope across the whole surface.

```
D:\WEB API\EMSServer\
  ├─ EMSAPI\     this project
  └─ EMSSoft\    front-end scaffold (see EMSSoft/README.md)
```

---

## 1. Setup

### Database

Run the scripts in `SQL/` in order against your SQL Server instance:

| Script | Creates |
|--------|---------|
| `01_CreateDatabase.sql` | the `EMSSoft` database |
| `02_Tables.sql` | the 15 core tables, constraints and filtered indexes |
| `03_StoredProcedures.sql` | 15 save/delete procedures |
| `04_SeedData.sql` | states, currencies, menu, access template, admin user |
| `05_AccountingTables.sql` | GroupHeads, AccountsMaster, AccountConfig, TaxMaster, GroupView |
| `06_AccountingProcedures.sql` | 4 accounting procedures |
| `07_AccountingSeed.sql` | chart of accounts, GST slabs, account mappings |
| `09_MasterTables.sql` | the 16 trading / inventory masters, constraints and filtered indexes |
| `10_MasterProcedures.sql` | 16 save/delete procedures for those masters |
| `11_MenulistFolders.sql` | the 42-module navigation tree for the four `EMSSoft/Modules/` folders |

(`08_TestData.sql` is optional sample data, not part of the schema.)

Every script is idempotent — re-running only adds what is missing. The one
exception is `11_MenulistFolders.sql`, which also **removes** `Menulist` rows
(and their `AccessTemplate` grants) for modules whose `.html` page does not
exist — that is the point of it, and it is why it runs last. See
[The menu is the folder tree](#the-menu-is-the-folder-tree).

### Connection string

`appsettings.json`:

```json
"ConnectionStrings": {
  "EMSConnection": "Server=DESKTOP-R3L704H\\SQLEXPRESS;Database=EMSSoft;Trusted_Connection=True;Encrypt=False;TrustServerCertificate=True"
}
```

### Run

```powershell
cd "D:\WEB API\EMSServer\EMSAPI"
dotnet run
```

Swagger is at `/swagger` in Development. Default ports: `https://localhost:7020`,
`http://localhost:5225`.

### Default login

```
User Id  : admin
Password : Admin@123
```

**Change it immediately** via `POST /api/Home/UpdatePassword`.

---

## 2. Authentication — token on every call

Every endpoint except sign-in requires a token that matches a live
`UserMaster.TokenNo`. The token is the **only** credential — an API key: the
caller never states who they are, because the token itself answers that. This is
enforced globally by `TokenAuthFilter` (`Models/TokenAuthFilter.cs`), registered
as an MVC filter in `Program.cs`, so a new controller is protected the moment it
is added — there is nothing to remember to opt into.

### How it works

1. `GET /api/Home/login` verifies the password, generates a `TokenNo` that no
   other user currently holds, stores it on the user row, and returns the
   encrypted form of it — **and nothing else**:

   ```json
   { "success": true, "message": "Login successful.", "data": { "tokenNo": "..." }, "statusCode": 200 }
   ```

   Name, user type, subscription, home page and the password-expiry flag come
   from `GET /api/Home/GetHomeInfo`, which the token unlocks.
2. The client sends that one value back on every subsequent call.
3. The filter decrypts it to the underlying number and looks for the active
   `UserMaster` row holding it. A row found means the request runs, and the
   verified caller is left in `HttpContext.Items` (`UserNo`, `UserId`,
   `SubscID`). No row found is **400 Bad Request**.

### Sending the token

Header (preferred):

```
X-Token: <tokenNo returned by login>
```

Query string also works, which suits the existing page-based front-end:

```
/api/StateMaster/GetList/1?tokenNo=<token>
```

### Rejection cases — all 400

| Case | Response |
|------|----------|
| No token sent | `Authentication token is required. Send it in the X-Token header.` |
| Malformed token (does not decrypt to a number) | `Invalid or expired token. Please sign in again.` |
| Token matches no user | same message as above |
| Deleted user, or `LockStatus <> 'Y'` | same message as above |
| User has no active token (signed out) | same message as above |

The last four share one message deliberately: a distinct message per case would
let a caller probe which tokens exist and which accounts are locked.

### Session lifecycle

- Signing in issues a **new** `TokenNo`, so any earlier token stops working —
  one active session per account.
- `POST /api/Home/logout` clears `TokenNo`, invalidating the token at once and
  closing the `AccessLog` row.

### The exempt endpoints

Three, all marked `[SkipTokenCheck]`:

| Route | Why it is exempt |
|-------|------------------|
| `GET /api/Home/login` | It is what issues a token. |
| `GET /api/Home/SessionHeartbeat` | A browser whose session was taken over holds a token that no longer matches, so the filter would answer 400 — the silent failure the heartbeat exists to replace. It reads nothing a stale token could not already infer, and changes nothing. |
| `POST /api/Home/EndSession` | Same reason. It authorises itself instead: nothing is cleared unless the token matches the current session. |

### Single active session

Signing in registers the session in `UserSessionRegistry` (in-memory, singleton).
A second sign-in with the same login replaces that entry, and the first browser's
next `SessionHeartbeat` comes back `takenover` with who took it. A restart empties
the register and every heartbeat answers `unknown`, **which the client must
ignore** — the durable half of the story is `UserMaster.TokenNo`, which is rotated
on every sign-in regardless.

> **Scope.** This is authentication, not authorization. It proves *who* is
> calling; it does not yet check *what* they may touch. `SubscID` still arrives
> as a parameter, so any signed-in user can currently query any subscription,
> and the `AccessTemplate` grants are returned to the UI but not enforced
> server-side. Both are worth closing before this faces the internet.

---

## 3. Response envelope

Every endpoint answers with the same shape, so one client-side handler covers
the whole API:

```json
{ "success": true, "message": "Data found.", "data": [], "statusCode": 200 }
```

| Status | Meaning |
|--------|---------|
| 200 | success — read `data` |
| 400 | bad input, or token missing/mismatched |
| 401 | bad credentials (login and password endpoints only) |
| 404 | record not found |
| 409 | rejected by a stored procedure — duplicate, or a rule such as "this group still has sub-groups". `message` carries the reason. |
| 500 | unhandled error; `message` carries the flattened exception chain |

`message` is always safe to show the user — it comes from the procedure's
`@Message` output.

---

## 4. Endpoints

### Standard master set

`Company`, `BranchMaster`, `BankMaster`, `CurrencyMaster`, `StateMaster`,
`OtherMaster`, `TaxMaster`, `GroupHeads` (keyed on `grpNo`),
`AccountMaster` (keyed on `acNo`), and from the trading module `CustomerMaster`,
`SupplierMaster`, `PartMaster`, `StandardTerms`, `PartCodeMaster`,
`PartParameterList`, `DrawingNos`, `MaterialStd`, `HSNMaster`:

| Method | Route |
|--------|-------|
| POST | `/api/{master}/SaveData` |
| GET | `/api/{master}/GetList/{subscID}` |
| GET | `/api/{master}/GetData/{id}/{subscID}` |
| GET | `/api/{master}/GetDropdown/{subscID}` |
| DELETE | `/api/{master}/DeleteData/{id}?userNo=&subscID=&reason=` |

`SaveData` takes the matching `SP_*` model. `Status: "Insert"` creates;
`Status: "Update"` with a non-zero id edits. `GetDropdown` returns only rows with
`LockStatus = 'Y'` (active).

### Child tables — the same five, scoped to a parent

`CustomerContacts`, `CustomerTerm`, `ShippingAddress`, `SupplierContacts`,
`SupplierTerm` and `MaterialGrade` are keyed on their own row (`TransSlNo`, or
`MTransNo` for `MaterialGrade`) and hang off a parent, so their list is scoped to
one parent rather than the whole subscription:

| Method | Route |
|--------|-------|
| POST | `/api/{child}/SaveData` — send the parent in `MTransNo` (`PartNo` for `MaterialGrade`) |
| GET | `/api/{child}/GetList/{parentId}/{subscID}` |
| GET | `/api/{child}/GetData/{id}/{subscID}` |
| DELETE | `/api/{child}/DeleteData/{id}?userNo=&subscID=&reason=` |

Deleting a parent soft-deletes its children, so a child list never returns rows
whose parent is gone:

| Deleting | Also soft-deletes |
|----------|-------------------|
| `CustomerMaster` | `CustomerContacts`, `CustomerTerm`, `ShippingAddress` |
| `SupplierMaster` | `SupplierContacts`, `SupplierTerm`, the vendor's `PriceList` rates |
| `PartMaster` | `MaterialGrade`, the part's `PriceList` rates |

`DrawingNos` is deliberately **not** cascaded from `PartMaster` — a drawing is its
own master record and outlives the part it happens to be tagged to.

### Beyond the standard set

Trading module:

| Route | Purpose |
|-------|---------|
| `GET /api/CustomerMaster/GetFullData/{id}/{subscID}` | customer + contacts + terms + ship-to addresses in one call |
| `GET /api/SupplierMaster/GetFullData/{id}/{subscID}` | supplier + contacts + terms |
| `GET /api/PartMaster/GetFullData/{id}/{subscID}` | part + material grades + drawings |
| `GET /api/PartMaster/GetList/{subscID}` | `?search=&category=&partType=&top=` — capped at 1000 rows by default; the message says when the cap was hit |
| `GET /api/PartMaster/GetDropdown/{subscID}` | `?flag=S\|P\|M&search=&top=` — sales, purchase or production parts |
| `GET /api/StandardTerms/GetDropdown/{termType}/{subscID}` | the term library for one side of the ledger |
| `GET /api/ShippingAddress/GetDropdown/{mTransNo}/{subscID}` | ship-to picker for one customer |
| `GET /api/MaterialGrade/GetDropdown/{partNo}/{subscID}` | grade picker for one part |
| `GET /api/MaterialGrade/GetByGrade/{grade}/{subscID}` | which parts are stocked in a grade |
| `GET /api/MaterialStd/GetByGrade/{grade}/{subscID}` | the standards one grade satisfies |
| `GET /api/PartCodeMaster/GetDropdown/{parameter}/{subscID}` | codes one parameter accepts |
| `GET /api/PartCodeMaster/GetParameters/{subscID}` | the parameter names in use |
| `GET /api/PartParameterList/GetLayout/{partType}/{subscID}` | the whole part-code layout — parameters in position order, each with its codes |
| `GET /api/PartParameterList/GetPartTypes/{subscID}` | part types that have a layout defined |
| `GET /api/DrawingNos/GetByPart/{partNo}/{subscID}` | drawings tagged to a part |
| `GET /api/PriceList/GetList/{subscID}` | `?vendorNo=&partNo=` — rates with vendor and part names resolved |
| `GET /api/PriceList/GetCurrentRates/{partNo}/{subscID}` | `?asOn=` — each vendor's rate in force on that date, cheapest first |
| `GET /api/PriceList/GetHistory/{vendorNo}/{partNo}/{subscID}` | the rate history of one vendor/part pair |

Core:

| Route | Purpose |
|-------|---------|
| `GET /api/Home/login` | sign in — the one endpoint needing no token |
| `POST /api/Home/logout` | clear the token, close the AccessLog row — no parameters |
| `GET /api/Home/TokenVerification` | confirm a token is still current — no parameters |
| `GET /api/Home/GetHomeInfo` | everything the Home page needs in one call — no parameters |
| `GET /api/Home/SessionHeartbeat` | `?userNo=&tokenNo=` — is this browser still the live session? |
| `POST /api/Home/EndSession` | `?userNo=&tokenNo=&reason=` — clean sign-out for the session watcher |
| `GET /api/Home/CheckPageAccess` | `?userNo=&page=&dept=` — may this user open this page? |
| `POST /api/Home/UpdatePassword` | change your own password (min 8 chars) |
| `POST /api/Home/PasswordReset` | admin reset; caller must be an active ADMIN |
| `GET /api/Home/GetMenuList` | modules the user may open |
| `GET /api/Home/GetMenuSection` | sidebar sections |
| `GET /api/Home/GetFavorites` | pinned modules |
| `GET /api/UserMaster/GetData/{id}` | user record, password decrypted for the edit form |
| `POST /api/UserMaster/ResetLockCounter/{id}` | unlock an account locked by bad logins |
| `GET /api/CurrencyMaster/GetRate/{code}/{subscID}` | latest effective rate |
| `GET /api/OtherMaster/GetDropdown/{masterType}/{subscID}` | values of one master type |
| `GET /api/GroupHeads/GetTree/{subscID}` | 4-level flattened chart of accounts |
| `GET /api/AccountMaster/FromAccount/{subscID}?payMode=` | cash/bank ledgers |
| `GET /api/AccountMaster/ToAccount/{subscID}` | everything that is not cash/bank |
| `GET /api/AccountConfig/GetAccount/{module}/{accType}/{subscID}` | resolve a posting account |
| `POST /api/EinvSetup/RefreshToken` | store new e-Invoice gateway tokens |
| `GET /api/EinvDtl/GetByTransaction/{mTransNo}/{subscID}` | IRN/e-Way-Bill for a transaction |
| `GET /api/AccessLog/GetList/{subscID}` | login audit (defaults to the last 7 days) |
| `GET /api/ErrorLog/GetList/{subscID}` | error log (defaults to the last 7 days) |
| `POST /api/Backup/Backup` | run `BACKUP DATABASE` |
| `GET /api/Backup/Download?fileName=` | download as an AES-256 zip |
| `GET /api/TableUpdation/*` | generic table browser/editor |

---

## 5. Conventions worth knowing

**`LockStatus` is the active flag, not a lock.** `'Y'` active, `'N'` inactive,
`'B'` blocked. The column default is `'N'`, so a user created without an explicit
`LockStatus` **cannot sign in** until it is set to `'Y'`. `LockCounter` is the
separate failed-login counter, compared against the user's `BadLogins` allowance.

**Deletes are soft.** `DeleteStatus` flips to `'Y'`; every read filters on
`'N'`. `Menulist` is the exception — it is hard-deleted, along with the
`AccessTemplate` grants pointing at it.

**`Menulist.MTransNo` is assigned by hand,** not an identity. It is the module
number `AccessTemplate.ModuleNo` refers to. Pass `0` to `SaveData` to have the
next free number allocated.

**Writes go through stored procedures,** never straight through EF. Validation,
duplicate checks and referential rules live in the procedure, so they hold no
matter which caller writes.

**Sequence numbers are allocated by the procedure.** Send `0` (or omit) for
`CustomerTerm.TermSlNo`, `SupplierTerm.TermSlNo`, `StandardTerms.TermSlNo`,
`MaterialGrade.GradeNo` or `PartParameterList.SlNo` and the next number for that
parent is filled in. Term numbers are `numeric(10,2)` so terms can be
sub-numbered by hand — send `1.10`, `1.20` to place one between two others.

**`PriceList` is a history, not a current-value table.** Saving does not
overwrite the last rate; it adds a row with a new effective date. What is
rejected is a second live rate for the same vendor/part/grade on the *same* date.
`GetCurrentRates` is the "what does this cost today" lookup; `GetHistory` is the
audit trail.

**The trading masters use filtered unique keys** — `UNIQUE (…) WHERE
DeleteStatus = 'N'` — so a soft-deleted row never blocks a new one with the same
code. That is the fix the older `UQ_*` constraints in `02_Tables.sql` still want
(see section 6), and it is why the newer procedures need no delete-collision
pre-check.

---

## 6. Corrections made to the supplied DDL

The table definitions as supplied do not compile. Each fix is flagged inline in
`SQL/02_Tables.sql` with a `[FIX n]` marker:

1. **`EinvDtl`** — a trailing comma after `CONSTRAINT PK_EinvDtl ...` made the
   `CREATE TABLE` a syntax error.
2. **`AccessTemplate`** — its index targeted a table `dbo.TemplateMaster` that is
   never created, and included a column `MF` that does not exist (the column is
   `MyFavorite`). Retargeted and renamed; `PK`/`UQ` renamed to match the table.
3. **`OtherMasterList`** — `UQ_OtherMasterList` and `IX_OtherMasterList_MasterType`
   both reference a `SubscID` column the definition omits. Column added.
4. **`Menulist`** — `IX_Menulist_Ordering` was declared over `(Product, PSLNo,
   SSLNo, MSLNo)`; `Product` and `PSLNo` do not exist here. Rebuilt over
   `(Category, CSLNo, SSLNo, MSLNo)`.

### One design note, preserved as supplied

The natural keys are `UNIQUE (Name, DeleteStatus, SubscID)`. Because
`DeleteStatus` is part of the key, only **one** row per name can ever sit in the
deleted state — soft-deleting a second record with the same name collides with
the first. The schema is kept exactly as given, and each `Delete` branch
pre-checks for the collision and returns a clear message rather than a raw
constraint error. To lift the limit, swap each `UQ_*` for a filtered unique
index:

```sql
CREATE UNIQUE INDEX UQ_Company ON dbo.Company (CompName, SubscID)
    WHERE DeleteStatus = 'N';
```

### Changes to the ported LMSAPI procedures

Flagged inline in `SQL/06_AccountingProcedures.sql`:

1. The LoanDRC originals end their `CATCH` with `RAISERROR`, which re-raises to
   the client — so `ExecuteSqlRawAsync` throws and the `@Message` the procedure
   just set is never read. These set `@ID = 0` / `@Message = ERROR_MESSAGE()` and
   return normally.
2. Added a `Delete` branch to each, so soft deletes go through the procedure
   like every other write.
3. `SP_AccountMaster` and `SP_TaxMaster` did not re-check for duplicates on
   update, so an edit could rename one record onto another. Both now check.
4. `SP_GroupHeads` declared `@MCount` as `OUTPUT` while the caller passed it by
   value. It is a plain input now.

`GroupView` also gained a `SubscID` column and a `DeleteStatus` filter — the
original mixed every subscription's groups together and included deleted ones.

### The menu is the folder tree

`dbo.Menulist` is the navigation catalogue: one row per module, grouped by
`Section`, and the sidebar on every EMSSoft page is built from it. `Section`
mirrors a folder under `EMSSoft/Modules/` and `ModulePage` is the page's path
relative to the EMSSoft root — `Modules/Utilities/MenuList.html`, never a bare
filename and never a leading slash. `navigation.js` prepends the depth prefix
for whichever page is asking, so a row that stores anything else lands on a 404.

`11_MenulistFolders.sql` owns the whole tree — 42 modules across the four
folders, one Section each:

| Section | Folder | Modules | Pages built |
|---|---|---|---|
| Masters | `Modules/Masters/` | 23 | 0 |
| Administration | `Modules/Administration/` | 6 | 0 |
| Accounts | `Modules/Accounts/` | 8 | 5 |
| Utilities | `Modules/Utilities/` | 5 | 3 |

**Only 8 of the 42 `.html` files exist today.** The other 34 are listed so the
sidebar shows the whole application structure as pages are written; until then
those links land on a 404. Every one of the 42 is backed by a real EMSAPI
controller, so the menu and the API agree even where the page does not exist
yet.

The three log/register screens (Access Log, Error Log, E-Invoice Register) sit
under Administration rather than in their own section. `Modules/Logs/` exists on
disk but is not one of the four folders the menu is built from; move those three
rows to a `Logs` section with `SSLNo 5` if you would rather they were separate.

Two things to know when adding a module:

1. **`MTransNo` is hand-assigned and is what `AccessTemplate.ModuleNo` points
   at.** `SP_Menulist` allocates the next free number when you pass 0. Never
   reuse a number freed by a delete — a surviving grant would silently re-point
   at a different module.
2. **A row in `Menulist` is not enough to make a module visible.**
   `GetMenuList` joins to `AccessTemplate` for any user that has a template, so
   an ungranted module is invisible even to the seeded admin. Step 4 of
   `11_MenulistFolders.sql` grants everything to `Administrator` on every run.

### The trading masters as supplied

`SQL/09_MasterTables.sql` keeps the supplied column definitions verbatim,
including the fixes already marked in that DDL (the missing comma after
`PartMaster.Rack`, the indexes that named columns which did not exist, and the
`StanderTems` / `CustomerTermsMaster` / `SupplierTermsMaster` tables collapsed
into one `StandardTerms` keyed by `TermType`). Two things were added around it:

1. Every statement is wrapped in an existence check, matching the other scripts,
   so the file can be re-run against a database that already has some of it.
2. The optional `UX_PriceList_Vendor_Part_Grade_Dt` index is left commented out as
   supplied — `SP_PriceList` enforces the same rule and returns a readable message
   instead of a constraint violation. Uncomment it if you would rather the engine
   held the line.

Three rules in `10_MasterProcedures.sql` do not come from the DDL, because the
schema cannot express them:

1. **Child rows are matched to their parent** on subscription as well as key. The
   foreign keys only cover `MTransNo`, so without this a contact could be filed
   under another subscription's customer.
2. **Parent deletes cascade** to children (the table in section 4), so no child
   list can return rows whose parent is gone.
3. **`GradeNo` is checked against the part** on `PriceList`, and `HSNCode` against
   `PartMaster` before an HSN row is deleted. Both links are by value, with no
   foreign key to lean on.

---

## 7. Known security work

Carried over from LMSAPI and worth addressing before this is publicly reachable:

- **Passwords are reversibly encrypted**, not hashed (`Models/CryptoHelper.cs`),
  because the user-edit screen displays the current password. Anyone who can read
  the database or this source can recover every password. The fix is a one-way
  hash (PBKDF2/bcrypt/Argon2) plus a "reset password" flow instead of "reveal
  password".
- **`TableUpdation` and `Backup` are unauthenticated beyond the token** — any
  signed-in user reaches them. Both are admin-grade tools: one edits arbitrary
  tables, the other hands over the entire dataset in one file. They need an
  admin-only policy.
- **No per-subscription authorization** — see the note in section 2.

Two things were *not* carried over from LMSAPI, deliberately:

- The hardcoded master password (`CPW = "BISSYS"`) in the login path, which let
  anyone sign in as any user.
- `TableUpdation`'s string-concatenated SQL. Table and column names are now
  resolved against `sys.tables`/`sys.columns` and values are parameterised, so
  the endpoints keep working without being an injection vector.

---

## 8. Verification

`dotnet build` is clean. The API was run against the live `EMSSoft` database and
exercised end to end — 47 checks covering token enforcement (missing, wrong,
wrong-user, query-string, re-login invalidation, logout), sign-in, navigation,
reads across all 12 masters, a full insert/duplicate/read/update/delete round
trip, accounting referential rules, the TableUpdation injection guards, logging,
and the password policy. All 47 passed.

### Trading masters

`09_MasterTables.sql` and `10_MasterProcedures.sql` were run against the live
database (16 tables, 16 procedures created), then exercised twice:

- **45 procedure calls** driven straight from T-SQL, each `@ID`/`@Message` pair
  compared against its expected outcome: every insert, update
  and delete path, plus the negative cases — duplicate code, duplicate name,
  orphan child, negative credit limit, max stock below min, tax over 100%, a
  grade that does not belong to the part, a rate with no rate, a second rate on
  the same date, deleting a priced grade, deleting an HSN code that parts use,
  deleting a missing row, and deleting across subscriptions. Confirmed the
  allocated sequence numbers, the filled defaults
  (`CUSTOMER`/`SUPPLIER`/`INR`/`ConversionRatio = 1`/unit from the part), the
  three cascades, that drawings survive a part delete, and that a code freed by a
  soft delete can be reused.
- **61 automated HTTP assertions** over the 16 controllers: token enforcement, every `SaveData`,
  `GetList`, `GetData`, `GetFullData`, `GetDropdown` and `DeleteData`, the
  price-list history and as-at-date lookups, the part-code layout, and the
  400/404/409 responses. Confirmed `numeric` precision survives the round trip
  (`CreditLimit` 250000.75, `PartWeight` 38.125, `Rate` 6875.25).

Every procedure call returned its expected result and all 61 HTTP assertions
passed. Test rows were written under `SubscID 999` and removed afterwards — the
16 tables are empty.
