using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Bulk import of suppliers and their contacts and terms from the Supplier
    /// Data Import page. Same design as CustomerDataImportController, without
    /// shipping addresses:
    ///
    ///   Validate and Import run one pipeline; Validate stops before writing.
    ///   Import is all-or-nothing. Inside the transaction:
    ///     1. lookup values the file uses but the masters lack (Category in
    ///        Other Master 'Vendor Category', State Master, Currency Master)
    ///     2. suppliers - SP_SupplierMaster allocates the codes (SQL/38)
    ///     3. contacts and terms, linked by Supplier Name
    ///
    /// Every write goes through the SP_* procedures the Supplier Master page
    /// uses, so their rules and the audit triggers apply unchanged.
    /// </summary>
    [Route("api/[controller]")]
    public class SupplierDataImportController : EmsControllerBase
    {
        private readonly EMSContext db;

        public SupplierDataImportController(EMSContext context) => db = context;

        // Other Master type SupplierMaster.html reads its Category dropdown from.
        private const string CategoryType = "Vendor Category";
        private const string LookupDepartment = "Sales";

        private const int MaxRows = 10000;

        private const string SheetSuppliers = "Suppliers";
        private const string SheetContacts = "Contacts";
        private const string SheetTerms = "Terms";

        // Closed lists - the same values the Supplier Master page offers.
        private static readonly string[] SupplierTypes = { "Inter State", "Intra State", "Export" };
        private static readonly string[] TaxTypes = { "REGULAR", "URD", "COMPOSITE", "SEZ", "EXPORT" };
        private static readonly string[] Titles = { "Mr", "Mrs", "Ms" };
        private static readonly string[] TermTypes = { "Fix", "Editable" };

        private static readonly Regex GstinRx = new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$");
        private static readonly Regex PanRx = new(@"^[A-Z]{5}[0-9]{4}[A-Z]$");
        private static readonly Regex IfscRx = new(@"^[A-Z]{4}0[A-Z0-9]{6}$");
        private static readonly Regex AccountRx = new(@"^[A-Z0-9]+$");
        private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        private static readonly Regex Spaces = new(@"\s+");

        //======================================================================
        // Endpoints
        //======================================================================

        /// <summary>Current master values, for the template's "Lists" sheet.</summary>
        [HttpGet("GetLookups/{subscID}")]
        public async Task<IActionResult> GetLookups(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var states = await db.StateMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.StateCode)
                    .Select(x => new { x.State, x.StateCode })
                    .ToListAsync();

                var currencies = await db.CurrencyMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.CurrencyCode)
                    .Select(x => new { x.CurrencyCode, x.Currency, x.Country })
                    .ToListAsync();

                var categories = await db.OtherMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.MasterType == CategoryType)
                    .OrderBy(x => x.MasterName)
                    .Select(x => x.MasterName)
                    .ToListAsync();

                return OkResult(new
                {
                    States = states,
                    Currencies = currencies,
                    Categories = categories,
                    SupplierTypes,
                    TaxTypes,
                    Titles,
                    TermTypes,
                    StatusValues = new[] { "Active", "Inactive" }
                });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Dry run: every check the import makes, nothing written.</summary>
        [HttpPost("Validate")]
        public Task<IActionResult> Validate([FromBody] SupplierImportRequest req) => Run(req, dryRun: true);

        [HttpPost("Import")]
        public Task<IActionResult> Import([FromBody] SupplierImportRequest req) => Run(req, dryRun: false);

        //======================================================================
        // Pipeline
        //======================================================================

        private async Task<IActionResult> Run(SupplierImportRequest req, bool dryRun)
        {
            if (req == null)
                return BadRequestResult("Invalid request.");
            if (req.SubscID <= 0 || req.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            var type = (req.ImportType ?? "ALL").Trim().ToUpperInvariant();
            if (type is not ("ALL" or "SUPPLIER" or "CONTACT" or "TERM"))
                return BadRequestResult("Import Type must be ALL, SUPPLIER, CONTACT or TERM.");

            if (type is not ("ALL" or "SUPPLIER")) req.Suppliers = new();
            if (type is not ("ALL" or "CONTACT")) req.Contacts = new();
            if (type is not ("ALL" or "TERM")) req.Terms = new();

            var total = req.Suppliers.Count + req.Contacts.Count + req.Terms.Count;
            if (total == 0)
                return BadRequestResult("The file has no data rows for the selected import type.");
            if (total > MaxRows)
                return BadRequestResult($"At most {MaxRows} rows can be imported at once; this file has {total}. Split it.");

            try
            {
                var plan = new ImportPlan(req, await LookupCache.LoadAsync(db, req.SubscID));

                PlanSuppliers(plan);
                PlanContacts(plan);
                PlanTerms(plan);

                var report = plan.BuildReport(dryRun);

                // No errors but nothing to do: every row exists and the mode is Skip.
                const string nothingToDo = "Nothing to import - every row already exists. Choose 'Update' to overwrite them.";

                if (dryRun)
                    return OkResult(report,
                        report.CanImport ? "Validation passed. The file is ready to import."
                        : report.ErrorCount == 0 ? nothingToDo
                        : $"Validation found {report.ErrorCount} error(s). Fix them and validate again.");

                if (!report.CanImport)
                    return Conflict(Envelope(false,
                        report.ErrorCount == 0 ? nothingToDo
                        : $"Import blocked: {report.ErrorCount} error(s) in the file. Nothing was imported.",
                        report, 409));

                return await Execute(plan, report);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        private static object Envelope(bool success, string message, object? data, int status) =>
            new { success, message, data, statusCode = status };

        //======================================================================
        // Execute
        //======================================================================

        private async Task<IActionResult> Execute(ImportPlan plan, SupplierImportReport report)
        {
            var ip = ClientIp;
            var req = plan.Request;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                // 1. Masters -------------------------------------------------
                foreach (var m in plan.MastersToCreate)
                {
                    (int ID, string Message) r;
                    switch (m.Master)
                    {
                        case "Category":
                            await EnsureMasterTypeListed(CategoryType, req.SubscID);
                            r = await db.CallOtherMasterSP(new SP_OtherMaster
                            {
                                MasterType = CategoryType,
                                MasterName = m.Value,
                                Department = LookupDepartment,
                                SystemIP = ip,
                                LockStatus = "Y",
                                CreatedBy = req.CreatedBy,
                                SubscID = req.SubscID,
                                Status = "Insert"
                            });
                            break;

                        case "State":
                            r = await db.CallStateMasterSP(new SP_StateMaster
                            {
                                State = m.Value,
                                StateCode = plan.NewStates[Key(m.Value)],
                                SystemIP = ip,
                                LockStatus = "Y",
                                CreatedBy = req.CreatedBy,
                                SubscID = req.SubscID,
                                Status = "Insert"
                            });
                            break;

                        case "Currency":
                            r = await db.CallCurrencyMasterImportSP(new SP_CurrencyMasterImport
                            {
                                Currency = m.Value,
                                CurrencyCode = m.Value,
                                ExchangeRate = 1,
                                SystemIP = ip,
                                CreatedBy = req.CreatedBy,
                                SubscID = req.SubscID
                            });
                            break;

                        default:
                            continue;
                    }

                    if (r.ID <= 0)
                        throw new ImportAbort("Masters", 0, $"{m.Master} '{m.Value}' could not be created: {r.Message}");
                    m.Created = true;
                }

                // 2. Suppliers ----------------------------------------------
                foreach (var s in plan.Suppliers.Where(x => x.Action is "New" or "Update"))
                {
                    s.Dto!.SystemIP = ip;
                    var r = await AtRow(SheetSuppliers, s.RowNo, () => db.CallSupplierMasterSP(s.Dto));
                    if (r.ID <= 0)
                        throw new ImportAbort(SheetSuppliers, s.RowNo, r.Message);

                    s.MTransNo = r.ID;
                    s.Result.SupplierCode = await db.SupplierMaster.AsNoTracking()
                        .Where(x => x.MTransNo == r.ID)
                        .Select(x => x.SupplierCode)
                        .FirstOrDefaultAsync();
                    s.Result.Message = s.Action == "New" ? "Created." : "Updated.";
                }

                // 3. Children -----------------------------------------------
                foreach (var ch in plan.Children.Where(x => x.Action is "New" or "Update"))
                {
                    var parentId = ch.Parent.MTransNo;
                    if (parentId <= 0)
                        throw new ImportAbort(ch.Sheet, ch.RowNo, $"Supplier '{ch.Result.SupplierName}' was not saved.");

                    var r = await AtRow(ch.Sheet, ch.RowNo, () => ch.Execute!(parentId, ip));
                    if (r.ID <= 0)
                        throw new ImportAbort(ch.Sheet, ch.RowNo, r.Message);

                    ch.Result.SupplierCode = ch.Parent.Code;
                    ch.Result.Message = ch.Action == "New" ? "Created." : "Updated.";
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                // A procedure that rolled back on its own has already ended the
                // transaction; rolling back again would throw and hide the cause.
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }

                var abort = ex as ImportAbort;
                var sheet = abort?.Sheet ?? "Import";
                var row = abort?.RowNo ?? 0;
                var message = abort?.Message ?? GetFullExceptionMessage(ex);

                report.Imported = false;
                report.CanImport = false;
                report.ErrorCount++;
                report.Issues.Insert(0, new CustomerImportIssue
                {
                    Sheet = sheet, RowNo = row, Level = "Error", Field = "", Message = message
                });
                foreach (var m in report.MastersToCreate) m.Created = false;
                foreach (var res in report.Results)
                {
                    if (res.Sheet == SheetSuppliers && res.Action == "New") res.SupplierCode = "(auto)";
                    if (res.Sheet == sheet && res.RowNo == row) { res.Action = "Error"; res.Message = message; }
                    else if (res.Action != "Skip") res.Message = "Rolled back - nothing was imported.";
                }

                var where = row > 0 ? $"{sheet} row {row}" : sheet;
                return Conflict(Envelope(false, $"Import failed at {where}: {message} Nothing was imported.", report, 409));
            }

            report.Imported = true;
            report.DryRun = false;

            var done = report.Summary.Sum(s => s.New + s.Update);
            var created = report.MastersToCreate.Count;
            return OkResult(report,
                $"Import complete: {done} row(s) saved" +
                (created > 0 ? $", {created} master value(s) created." : "."));
        }

        /// <summary>
        /// Runs one procedure call and pins any exception to its sheet row (a
        /// swallowed error inside our transaction surfaces at the end of the batch).
        /// </summary>
        private static async Task<(int ID, string Message)> AtRow(
            string sheet, int rowNo, Func<Task<(int ID, string Message)>> call)
        {
            try
            {
                return await call();
            }
            catch (Exception ex) when (ex is not ImportAbort)
            {
                throw new ImportAbort(sheet, rowNo, GetFullExceptionMessage(ex));
            }
        }

        /// <summary>The Other Master page only offers types listed in OtherMasterList.</summary>
        private async Task EnsureMasterTypeListed(string masterType, int subscID)
        {
            var listed = await db.OtherMasterList.AsNoTracking()
                .AnyAsync(x => x.MasterType == masterType && x.SubscID == subscID && x.DeleteStatus == "N");
            if (listed) return;

            var r = await db.CallOtherMasterListSP(new SP_OtherMasterList
            {
                MasterType = masterType,
                Department = LookupDepartment,
                SubscID = subscID,
                Status = "Insert"
            });
            if (r.ID <= 0)
                throw new ImportAbort("Masters", 0, $"Other Master type '{masterType}' could not be registered: {r.Message}");
        }

        //======================================================================
        // Planning - Suppliers
        //======================================================================

        private static void PlanSuppliers(ImportPlan p)
        {
            const string S = SheetSuppliers;
            var req = p.Request;
            var update = p.UpdateExisting;

            foreach (var r in req.Suppliers)
            {
                var errs = p.ErrorCount;
                var name = Name(r.SupplierName);
                var plan = new SupplierPlan { RowNo = r.RowNo, Name = name ?? "" };
                plan.Result = new SupplierImportResult { Sheet = S, RowNo = r.RowNo, SupplierName = name ?? "" };
                p.Suppliers.Add(plan);

                if (name == null)
                {
                    p.Error(S, r.RowNo, "Supplier Name", "Supplier Name is required.");
                    plan.Fail("Supplier Name is required.");
                    continue;
                }
                p.Length(S, r.RowNo, "Supplier Name", name, 100);

                if (p.SuppliersInFile.TryGetValue(Key(name), out var firstPlan))
                {
                    p.Error(S, r.RowNo, "Supplier Name", $"'{name}' appears again - first on row {firstPlan.RowNo}. Each supplier once.");
                    plan.Fail("Duplicate in file.");
                    continue;
                }
                p.SuppliersInFile[Key(name)] = plan;

                p.Lookups.Suppliers.TryGetValue(Key(name), out var ex);
                plan.Existing = ex;

                if (ex != null && !update)
                {
                    plan.Action = "Skip";
                    plan.MTransNo = ex.MTransNo;
                    plan.SupplierType = ex.SupplierType;
                    plan.TaxType = ex.TaxType;
                    plan.Result.Action = "Skip";
                    plan.Result.SupplierCode = ex.SupplierCode;
                    plan.Result.Message = "Already in Supplier Master - skipped.";
                    continue;
                }

                var isNew = ex == null;

                // Supplier Type
                string? supType = ex?.SupplierType;
                if (Clean(r.SupplierType) is { } st0)
                {
                    supType = Pick(st0, SupplierTypes);
                    if (supType == null)
                        p.Error(S, r.RowNo, "Supplier Type", $"'{st0}' is not valid. Use: {string.Join(", ", SupplierTypes)}.");
                }
                else if (isNew)
                    p.Error(S, r.RowNo, "Supplier Type", $"Supplier Type is required ({string.Join(", ", SupplierTypes)}).");

                // Address
                var address = Clean(r.SupplierAddress) ?? ex?.SupplierAddress;
                if (address == null && isNew)
                    p.Error(S, r.RowNo, "Supplier Address", "Supplier Address is required.");
                p.Length(S, r.RowNo, "Supplier Address", address, 300);

                // State
                string? stateName = ex?.StateName;
                int? stateCode = ex?.StateCode;
                if (Clean(r.StateName) != null || Clean(r.StateCode) != null)
                {
                    var st = p.ResolveState(S, r.RowNo, "State", r.StateName, r.StateCode);
                    if (st != null) { stateName = st.Value.Name; stateCode = st.Value.Code; }
                }
                else if (isNew)
                    p.Error(S, r.RowNo, "State", "State is required.");

                // Phone / contact details
                var phone = Clean(r.PhoneNo) ?? ex?.PhoneNo;
                if (phone == null && isNew)
                    p.Error(S, r.RowNo, "Phone No", "Phone No is required.");
                p.Length(S, r.RowNo, "Phone No", phone, 30);

                var mobile = Clean(r.MobileNo) ?? ex?.MobileNo;
                p.Length(S, r.RowNo, "Mobile No", mobile, 30);

                var email = Clean(r.EmailID) ?? ex?.EmailID;
                p.Email(S, r.RowNo, "Email", Clean(r.EmailID));
                p.Length(S, r.RowNo, "Email", email, 80);

                var website = Clean(r.Website) ?? ex?.Website;
                p.Length(S, r.RowNo, "Website", website, 80);

                // GSTIN / PAN / Tax Type
                var gstinIn = Upper(r.GSTIN);
                if (gstinIn != null && !GstinRx.IsMatch(gstinIn))
                    p.Error(S, r.RowNo, "GSTIN", $"'{gstinIn}' is not a valid 15-character GSTIN.");
                var gstin = gstinIn ?? ex?.GSTIN;

                string? taxType = ex?.TaxType;
                if (Clean(r.TaxType) is { } tt)
                {
                    taxType = Pick(tt, TaxTypes);
                    if (taxType == null)
                        p.Error(S, r.RowNo, "Tax Type", $"'{tt}' is not valid. Use: {string.Join(", ", TaxTypes)}.");
                }
                taxType ??= supType == "Export" ? "EXPORT" : gstin == null ? "URD" : "REGULAR";

                var gstExempt = supType == "Export" || taxType is "URD" or "EXPORT";
                if (gstin == null && !gstExempt)
                    p.Error(S, r.RowNo, "GSTIN", $"GSTIN is required for Tax Type {taxType} (leave it blank only for URD / EXPORT / Export suppliers).");

                if (gstinIn != null && GstinRx.IsMatch(gstinIn) && stateCode is int sc && sc < 100 &&
                    gstinIn[..2] != sc.ToString("00"))
                    p.Warn(S, r.RowNo, "GSTIN", $"GSTIN starts with {gstinIn[..2]} but the state code is {sc:00}.");

                var panIn = Upper(r.PAN);
                if (panIn != null && !PanRx.IsMatch(panIn))
                    p.Error(S, r.RowNo, "PAN", $"'{panIn}' is not a valid 10-character PAN.");
                var pan = panIn ?? ex?.PAN;
                if (pan == null && gstinIn != null && GstinRx.IsMatch(gstinIn))
                    pan = gstinIn.Substring(2, 10);   // PAN is characters 3-12 of the GSTIN

                // Currency, Category - created when missing
                var currency = ex?.TxnCurrency;
                if (Clean(r.TxnCurrency) is { } cur)
                    currency = p.ResolveCurrency(S, r.RowNo, cur);
                else if (isNew)
                    currency = "INR";

                var category = ex?.Category;
                if (Name(r.Category) is { } cat)
                    category = p.ResolveCategory(S, r.RowNo, cat);

                // Credit period
                var creditPeriod = ex?.CreditPeriod;
                if (Clean(r.CreditPeriod) is { } cpText)
                {
                    if (TryInt(cpText, out var cp) && cp >= 0 && cp <= 999) creditPeriod = cp;
                    else p.Error(S, r.RowNo, "Credit Period", $"'{cpText}' must be a whole number of days (0-999).");
                }

                // Bank
                var bankName = Name(r.BankName) ?? ex?.BankName;
                p.Length(S, r.RowNo, "Bank Name", bankName, 50);

                var accountIn = Upper(r.BankAccountNo);
                if (accountIn != null && !AccountRx.IsMatch(accountIn))
                    p.Error(S, r.RowNo, "Bank Account No", $"'{accountIn}' may contain only letters and digits.");
                var account = accountIn ?? ex?.BankAccountNo;
                p.Length(S, r.RowNo, "Bank Account No", account, 30);

                var ifscIn = Upper(r.IFSC);
                if (ifscIn != null && !IfscRx.IsMatch(ifscIn))
                    p.Error(S, r.RowNo, "IFSC", $"'{ifscIn}' is not a valid 11-character IFSC (e.g. SBIN0001234).");
                var ifsc = ifscIn ?? ex?.IFSC;

                if (accountIn != null && ifsc == null)
                    p.Warn(S, r.RowNo, "IFSC", "Bank Account No is given without an IFSC.");

                var alias = Name(r.AliasName) ?? ex?.AliasName;
                p.Length(S, r.RowNo, "Alias Name", alias, 100);
                var pin = Clean(r.PinCode) ?? ex?.PinCode;
                p.Length(S, r.RowNo, "Pin Code", pin, 30);
                var pinLoc = Clean(r.PinCodeLocation) ?? ex?.PinCodeLocation;
                p.Length(S, r.RowNo, "Pin Code Location", pinLoc, 50);
                var remarks = Clean(r.Remarks) ?? ex?.Remarks;
                p.Length(S, r.RowNo, "Remarks", remarks, 500);

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                plan.SupplierType = supType;
                plan.TaxType = taxType;

                if (p.ErrorCount > errs)
                {
                    plan.Fail("Row has errors.");
                    continue;
                }

                plan.Action = isNew ? "New" : "Update";
                plan.Result.Action = plan.Action;
                plan.Result.SupplierCode = isNew ? "(auto)" : ex!.SupplierCode;
                plan.Result.Message = isNew ? "Will be created - code allocated on import." : "Will update the existing supplier.";

                plan.Dto = new SP_SupplierMaster
                {
                    MTransNo = ex?.MTransNo ?? 0,
                    MCount = isNew ? 0 : (ex!.LockCounter ?? 0) + 1,
                    SupplierCode = ex?.SupplierCode ?? string.Empty,   // blank = SP allocates
                    SupplierType = supType!,
                    // The name is the match key - an update keeps the stored spelling.
                    SupplierName = ex?.SupplierName ?? name,
                    AliasName = alias,
                    Category = category,
                    SupplierAddress = address,
                    StateName = stateName,
                    StateCode = stateCode,
                    PinCode = pin,
                    PinCodeLocation = pinLoc,
                    PhoneNo = phone,
                    MobileNo = mobile,
                    EmailID = email,
                    Website = website,
                    TaxType = taxType,
                    GSTIN = gstin,
                    PAN = pan,
                    TxnCurrency = currency ?? "INR",
                    CreditPeriod = creditPeriod,
                    BankName = bankName,
                    BankAccountNo = account,
                    IFSC = ifsc,
                    Remarks = remarks,
                    LockStatus = lockStatus,
                    CreatedBy = req.CreatedBy,
                    CreatedOn = DateTime.Now,
                    SubscID = req.SubscID,
                    Status = isNew ? "Insert" : "Update"
                };
            }
        }

        //======================================================================
        // Planning - Children
        //======================================================================

        private static void PlanContacts(ImportPlan p)
        {
            const string S = SheetContacts;
            var req = p.Request;
            var seen = new Dictionary<string, int>();

            foreach (var r in req.Contacts)
            {
                var errs = p.ErrorCount;
                var cpName = Name(r.CPName);
                var ch = p.NewChild(S, r.RowNo, r.SupplierName, cpName);
                if (ch == null) continue;

                if (cpName == null) p.Error(S, r.RowNo, "Contact Name", "Contact Name is required.");
                p.Length(S, r.RowNo, "Contact Name", cpName, 50);

                ContactRow? ex = null;
                if (cpName != null)
                {
                    var k = ch.Parent.Key + "|" + Key(cpName);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Contact Name", $"'{cpName}' is already listed for this supplier on row {first}.");
                    else seen[k] = r.RowNo;

                    if (ch.Parent.ExistingId > 0)
                        p.Lookups.Contacts.TryGetValue(ch.Parent.ExistingId + "|" + Key(cpName), out ex);
                }

                string? title = ex?.Title;
                if (Clean(r.Title) is { } t)
                {
                    title = Pick(t.TrimEnd('.'), Titles);
                    if (title == null) p.Error(S, r.RowNo, "Title", $"'{t}' is not valid. Use: {string.Join(", ", Titles)}.");
                }

                var designation = Name(r.Designation) ?? ex?.Designation;
                p.Length(S, r.RowNo, "Designation", designation, 30);
                var phone = Clean(r.PhoneNo) ?? ex?.PhoneNo;
                if (phone == null) p.Error(S, r.RowNo, "Phone No", "Phone No is required.");
                p.Length(S, r.RowNo, "Phone No", phone, 30);
                var mobile = Clean(r.MobileNo) ?? ex?.MobileNo;
                p.Length(S, r.RowNo, "Mobile No", mobile, 30);
                var email = Clean(r.Email) ?? ex?.Email;
                p.Email(S, r.RowNo, "Email", Clean(r.Email));
                p.Length(S, r.RowNo, "Email", email, 80);
                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                if (!p.Settle(ch, errs, ex != null)) continue;

                var transSlNo = ex?.TransSlNo ?? 0;
                ch.Execute = (mTransNo, ip) => p.Db.CallSupplierContactsSP(new SP_SupplierContacts
                {
                    TransSlNo = transSlNo,
                    MTransNo = mTransNo,
                    Title = title,
                    CPName = cpName!,
                    Designation = designation,
                    PhoneNo = phone,
                    MobileNo = mobile,
                    Email = email,
                    SystemIP = ip,
                    LockStatus = lockStatus,
                    CreatedBy = req.CreatedBy,
                    SubscID = req.SubscID,
                    Status = transSlNo > 0 ? "Update" : "Insert"
                });
            }
        }

        private static void PlanTerms(ImportPlan p)
        {
            const string S = SheetTerms;
            var req = p.Request;
            var seen = new Dictionary<string, int>();

            foreach (var r in req.Terms)
            {
                var errs = p.ErrorCount;
                var title = Clean(r.TermTitle);
                var ch = p.NewChild(S, r.RowNo, r.SupplierName, title);
                if (ch == null) continue;

                if (title == null) p.Error(S, r.RowNo, "Term Title", "Term Title is required.");
                p.Length(S, r.RowNo, "Term Title", title, 30);

                TermRow? ex = null;
                if (title != null)
                {
                    var k = ch.Parent.Key + "|" + Key(title);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Term Title", $"'{title}' is already listed for this supplier on row {first}.");
                    else seen[k] = r.RowNo;

                    if (ch.Parent.ExistingId > 0)
                        p.Lookups.Terms.TryGetValue(ch.Parent.ExistingId + "|" + Key(title), out ex);
                }

                var desc = Clean(r.TermDescription) ?? ex?.TermDescription;
                if (desc == null) p.Error(S, r.RowNo, "Term Description", "Term Description is required.");
                p.Length(S, r.RowNo, "Term Description", desc, 250);

                var termType = ex?.TermType ?? "Fix";
                if (Clean(r.TermType) is { } tt)
                {
                    var picked = Pick(tt, TermTypes);
                    if (picked == null) p.Error(S, r.RowNo, "Term Type", $"'{tt}' is not valid. Use: {string.Join(", ", TermTypes)}.");
                    else termType = picked;
                }

                decimal? slNo = ex?.TermSlNo;
                if (Clean(r.TermSlNo) is { } slText)
                {
                    if (TryDec(slText, out var sl) && sl > 0 && sl < 100000000) slNo = Math.Round(sl, 2);
                    else p.Error(S, r.RowNo, "Term Sl No", $"'{slText}' must be a number greater than 0 (e.g. 1, 2, 1.10).");
                }

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                if (!p.Settle(ch, errs, ex != null)) continue;

                var transSlNo = ex?.TransSlNo ?? 0;
                ch.Execute = (mTransNo, ip) => p.Db.CallSupplierTermSP(new SP_SupplierTerm
                {
                    TransSlNo = transSlNo,
                    MTransNo = mTransNo,
                    TermSlNo = slNo ?? 0,          // 0 = next number (SP)
                    TermTitle = title,
                    TermDescription = desc,
                    TermType = termType,
                    SystemIP = ip,
                    LockStatus = lockStatus,
                    CreatedBy = req.CreatedBy,
                    SubscID = req.SubscID,
                    Status = transSlNo > 0 ? "Update" : "Insert"
                });
            }
        }

        //======================================================================
        // Text helpers
        //======================================================================

        private static string? Clean(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        private static string? Name(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v.Trim(), " ");

        private static string? Upper(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v, "").ToUpperInvariant();

        private static string Key(string v) => Spaces.Replace(v.Trim(), " ").ToUpperInvariant();

        private static string? Pick(string value, IEnumerable<string> allowed)
        {
            static string Flat(string s) => Regex.Replace(s, @"[\s\-_]", "").ToUpperInvariant();
            var v = Flat(value);
            return allowed.FirstOrDefault(a => Flat(a) == v);
        }

        private static bool TryDec(string text, out decimal value) =>
            decimal.TryParse(text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        private static bool TryInt(string text, out int value)
        {
            value = 0;
            if (!TryDec(text, out var d) || d != Math.Floor(d) || d > int.MaxValue || d < int.MinValue) return false;
            value = (int)d;
            return true;
        }

        //======================================================================
        // Plan state
        //======================================================================

        private sealed class ImportAbort : Exception
        {
            public string Sheet { get; }
            public int RowNo { get; }
            public ImportAbort(string sheet, int rowNo, string message) : base(message)
            {
                Sheet = sheet;
                RowNo = rowNo;
            }
        }

        private sealed class SupplierPlan
        {
            public int RowNo;
            public string Name = "";
            public string Action = "Error";
            public SupplierMaster? Existing;
            public SP_SupplierMaster? Dto;
            public int MTransNo;
            public string? SupplierType;
            public string? TaxType;
            public SupplierImportResult Result = null!;

            public void Fail(string message)
            {
                Action = "Error";
                Result.Action = "Error";
                Result.Message = message;
            }
        }

        /// <summary>The supplier a child row hangs off - a row of this file, or one already saved.</summary>
        private sealed class ParentRef
        {
            public string Key = "";
            public SupplierPlan? Plan;
            public SupplierMaster? Existing;

            public int ExistingId => Plan?.Existing?.MTransNo ?? Existing?.MTransNo ?? 0;
            public int MTransNo => Plan?.MTransNo ?? Existing?.MTransNo ?? 0;
            public string? Code => Plan != null ? Plan.Result.SupplierCode : Existing?.SupplierCode;
        }

        private sealed class ChildPlan
        {
            public string Sheet = "";
            public int RowNo;
            public string Action = "Error";
            public ParentRef Parent = null!;
            public Func<int, string?, Task<(int ID, string Message)>>? Execute;
            public SupplierImportResult Result = null!;
        }

        private sealed record ContactRow(int TransSlNo, string? Title, string? Designation, string? PhoneNo,
            string? MobileNo, string? Email, string? LockStatus);
        private sealed record TermRow(int TransSlNo, decimal? TermSlNo, string? TermDescription, string? TermType,
            string? LockStatus);

        /// <summary>Everything the checks compare against, read once per request.</summary>
        private sealed class LookupCache
        {
            public EMSContext Db = null!;
            public Dictionary<string, (string Name, int Code)> StatesByName = new();
            public Dictionary<int, string> StatesByCode = new();
            public Dictionary<string, string> CurrencyByCode = new();   // KEY -> stored code
            public Dictionary<string, string> CurrencyByName = new();   // KEY(name) -> stored code
            public Dictionary<string, string> Categories = new();       // KEY -> stored spelling
            public Dictionary<string, SupplierMaster> Suppliers = new();
            public Dictionary<string, ContactRow> Contacts = new();     // "MTransNo|KEY(CPName)"
            public Dictionary<string, TermRow> Terms = new();           // "MTransNo|KEY(TermTitle)"

            public static async Task<LookupCache> LoadAsync(EMSContext db, int subscID)
            {
                var c = new LookupCache { Db = db };

                foreach (var s in await db.StateMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.State != null)
                             .Select(x => new { x.State, x.StateCode }).ToListAsync())
                {
                    c.StatesByName.TryAdd(Key(s.State!), (s.State!.Trim(), s.StateCode ?? 0));
                    if (s.StateCode is int code) c.StatesByCode.TryAdd(code, s.State!.Trim());
                }

                foreach (var cur in await db.CurrencyMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.CurrencyCode != null)
                             .Select(x => new { x.CurrencyCode, x.Currency }).ToListAsync())
                {
                    c.CurrencyByCode.TryAdd(Key(cur.CurrencyCode!), cur.CurrencyCode!.Trim());
                    if (!string.IsNullOrWhiteSpace(cur.Currency))
                        c.CurrencyByName.TryAdd(Key(cur.Currency), cur.CurrencyCode!.Trim());
                }

                foreach (var name in await db.OtherMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.MasterName != null &&
                                         x.MasterType == CategoryType)
                             .Select(x => x.MasterName).ToListAsync())
                    c.Categories.TryAdd(Key(name!), name!.Trim());

                foreach (var sm in await db.SupplierMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.SupplierName != null)
                             .ToListAsync())
                    c.Suppliers.TryAdd(Key(sm.SupplierName!), sm);

                foreach (var x in await db.SupplierContacts.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.CPName != null).ToListAsync())
                    c.Contacts.TryAdd(x.MTransNo + "|" + Key(x.CPName!),
                        new ContactRow(x.TransSlNo, x.Title, x.Designation, x.PhoneNo, x.MobileNo, x.Email, x.LockStatus));

                foreach (var x in await db.SupplierTerm.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.TermTitle != null).ToListAsync())
                    c.Terms.TryAdd(x.MTransNo + "|" + Key(x.TermTitle!),
                        new TermRow(x.TransSlNo, x.TermSlNo, x.TermDescription, x.TermType, x.LockStatus));

                return c;
            }
        }

        private sealed class ImportPlan
        {
            public SupplierImportRequest Request { get; }
            public LookupCache Lookups { get; }
            public EMSContext Db => Lookups.Db;
            public bool UpdateExisting { get; }

            public List<CustomerImportIssue> Issues { get; } = new();
            public List<SupplierPlan> Suppliers { get; } = new();
            public Dictionary<string, SupplierPlan> SuppliersInFile { get; } = new();
            public List<ChildPlan> Children { get; } = new();

            // New master values, KEY -> value as first written in the file.
            private readonly Dictionary<string, string> newCategories = new();
            public Dictionary<string, int> NewStates { get; } = new();            // KEY(name) -> code
            private readonly Dictionary<string, string> newStateNames = new();    // KEY(name) -> name
            private readonly Dictionary<int, string> newStateCodes = new();       // code -> name
            private readonly Dictionary<string, string> newCurrencies = new();    // KEY -> code

            public List<CustomerImportMaster> MastersToCreate { get; } = new();

            public int ErrorCount => Issues.Count(i => i.Level == "Error");

            public ImportPlan(SupplierImportRequest req, LookupCache lookups)
            {
                Request = req;
                Lookups = lookups;
                UpdateExisting = string.Equals(req.ExistingMode?.Trim(), "UPDATE", StringComparison.OrdinalIgnoreCase);
            }

            public void Error(string sheet, int row, string field, string message) =>
                Issues.Add(new CustomerImportIssue { Sheet = sheet, RowNo = row, Level = "Error", Field = field, Message = message });

            public void Warn(string sheet, int row, string field, string message) =>
                Issues.Add(new CustomerImportIssue { Sheet = sheet, RowNo = row, Level = "Warning", Field = field, Message = message });

            public void Length(string sheet, int row, string field, string? value, int max)
            {
                if (value != null && value.Length > max)
                    Error(sheet, row, field, $"{field} is {value.Length} characters; the maximum is {max}.");
            }

            public void Email(string sheet, int row, string field, string? value)
            {
                if (value != null && !EmailRx.IsMatch(value))
                    Error(sheet, row, field, $"'{value}' is not a valid email address.");
            }

            /// <summary>Y / N from Active / Inactive / Y / N / Yes / No; null when blank.</summary>
            public string? ParseStatus(string sheet, int row, string? text)
            {
                var v = Clean(text);
                if (v == null) return null;
                switch (v.ToUpperInvariant())
                {
                    case "ACTIVE": case "Y": case "YES": case "1": return "Y";
                    case "INACTIVE": case "N": case "NO": case "0": return "N";
                }
                Error(sheet, row, "Status", $"'{v}' is not valid. Use Active or Inactive.");
                return null;
            }

            /// <summary>State by name (or by code alone); a missing state is created only with its code.</summary>
            public (string Name, int Code)? ResolveState(string sheet, int row, string field, string? nameText, string? codeText)
            {
                var name = Name(nameText);
                int? code = null;
                if (Clean(codeText) is { } ct)
                {
                    if (TryInt(ct, out var c) && c > 0) code = c;
                    else { Error(sheet, row, "State Code", $"'{ct}' is not a valid state code."); return null; }
                }

                if (name == null)
                {
                    if (code is int only && Lookups.StatesByCode.TryGetValue(only, out var byCode))
                        return (byCode, only);
                    if (code is int only2 && newStateCodes.TryGetValue(only2, out var planned))
                        return (planned, only2);
                    Error(sheet, row, field, $"State code {code} is not in State Master - give the State name too so it can be created.");
                    return null;
                }

                if (Lookups.StatesByName.TryGetValue(Key(name), out var st))
                {
                    if (code != null && code != st.Code)
                        Warn(sheet, row, "State Code", $"State Master has {st.Name} as code {st.Code}; {code} in the file is ignored.");
                    return st;
                }

                if (NewStates.TryGetValue(Key(name), out var plannedCode))
                {
                    if (code != null && code != plannedCode)
                        Error(sheet, row, "State Code", $"New state '{name}' was given code {plannedCode} on another row; this row says {code}.");
                    return (newStateNames[Key(name)], plannedCode);
                }

                if (code == null)
                {
                    Error(sheet, row, field, $"State '{name}' is not in State Master. Add its State Code so it can be created, or correct the spelling.");
                    return null;
                }
                if (Lookups.StatesByCode.TryGetValue(code.Value, out var other) || newStateCodes.TryGetValue(code.Value, out other))
                {
                    Error(sheet, row, field, $"State '{name}' is not in State Master and code {code} already belongs to {other}.");
                    return null;
                }
                if (name.Length > 50)
                {
                    Error(sheet, row, field, "State name is longer than 50 characters.");
                    return null;
                }

                NewStates[Key(name)] = code.Value;
                newStateNames[Key(name)] = name;
                newStateCodes[code.Value] = name;
                MastersToCreate.Add(new CustomerImportMaster { Master = "State", Value = name, Detail = $"State Code {code}" });
                return (name, code.Value);
            }

            /// <summary>Currency code (by code or by name). An unknown code is created at rate 1.00.</summary>
            public string? ResolveCurrency(string sheet, int row, string text)
            {
                var k = Key(text);
                if (Lookups.CurrencyByCode.TryGetValue(k, out var code)) return code;
                if (Lookups.CurrencyByName.TryGetValue(k, out code)) return code;
                if (newCurrencies.TryGetValue(k, out code)) return code;

                var newCode = Upper(text)!;
                if (newCode.Length > 10)
                {
                    Error(sheet, row, "Currency", $"Currency '{text}' is not in Currency Master. Use its code (INR, USD...) - codes are at most 10 characters.");
                    return null;
                }

                newCurrencies[k] = newCode;
                MastersToCreate.Add(new CustomerImportMaster
                {
                    Master = "Currency", Value = newCode, Detail = "Exchange rate 1.00 - correct it in Currency Master"
                });
                Warn(sheet, row, "Currency", $"Currency '{newCode}' will be created with exchange rate 1.00 - set the real rate in Currency Master.");
                return newCode;
            }

            /// <summary>Category value; created in Other Master 'Vendor Category' when missing.</summary>
            public string? ResolveCategory(string sheet, int row, string value)
            {
                if (value.Length > 50)
                {
                    Error(sheet, row, "Category", $"Category is {value.Length} characters; the maximum is 50.");
                    return null;
                }
                var k = Key(value);
                if (Lookups.Categories.TryGetValue(k, out var stored)) return stored;
                if (newCategories.TryGetValue(k, out stored)) return stored;

                newCategories[k] = value;
                MastersToCreate.Add(new CustomerImportMaster
                {
                    Master = "Category", Value = value, Detail = $"Other Master - {CategoryType}"
                });
                return value;
            }

            /// <summary>
            /// Starts a child row and links it to its supplier: a valid row of the
            /// Suppliers sheet first, else a supplier already in the master.
            /// </summary>
            public ChildPlan? NewChild(string sheet, int row, string? supplierName, string? item)
            {
                var name = Name(supplierName);
                var ch = new ChildPlan
                {
                    Sheet = sheet,
                    RowNo = row,
                    Result = new SupplierImportResult { Sheet = sheet, RowNo = row, SupplierName = name ?? "", Item = item }
                };
                Children.Add(ch);

                void Fail(string msg)
                {
                    Error(sheet, row, "Supplier Name", msg);
                    ch.Result.Action = "Error";
                    ch.Result.Message = msg;
                }

                if (name == null)
                {
                    Fail("Supplier Name is required - it links this row to its supplier.");
                    return null;
                }

                if (SuppliersInFile.TryGetValue(Key(name), out var plan))
                {
                    if (plan.Action == "Error")
                    {
                        Fail($"Supplier '{name}' (Suppliers sheet row {plan.RowNo}) has errors - fix that row first.");
                        return null;
                    }
                    ch.Parent = new ParentRef { Key = Key(name), Plan = plan };
                    return ch;
                }

                if (Lookups.Suppliers.TryGetValue(Key(name), out var existing))
                {
                    ch.Parent = new ParentRef { Key = Key(name), Existing = existing };
                    return ch;
                }

                Fail($"Supplier '{name}' is not in Supplier Master" +
                     (Request.Suppliers.Count > 0 ? " or the Suppliers sheet." : " - import the supplier first, or use import type 'All'."));
                return null;
            }

            /// <summary>Decides New / Update / Skip / Error once a child row's checks are done.</summary>
            public bool Settle(ChildPlan ch, int errorsBefore, bool exists)
            {
                if (ErrorCount > errorsBefore)
                {
                    ch.Result.Action = "Error";
                    ch.Result.Message = "Row has errors.";
                    return false;
                }

                ch.Result.SupplierCode = ch.Parent.Code;

                if (exists && !UpdateExisting)
                {
                    ch.Action = "Skip";
                    ch.Result.Action = "Skip";
                    ch.Result.Message = "Already on this supplier - skipped.";
                    return false;
                }

                ch.Action = exists ? "Update" : "New";
                ch.Result.Action = ch.Action;
                ch.Result.Message = exists ? "Will update the existing row." : "Will be added.";
                return true;
            }

            public SupplierImportReport BuildReport(bool dryRun)
            {
                var results = Suppliers.Select(s => s.Result).Concat(Children.Select(c => c.Result)).ToList();

                CustomerImportCount Count(string sheet, int rows) => new()
                {
                    Sheet = sheet,
                    Rows = rows,
                    New = results.Count(r => r.Sheet == sheet && r.Action == "New"),
                    Update = results.Count(r => r.Sheet == sheet && r.Action == "Update"),
                    Skip = results.Count(r => r.Sheet == sheet && r.Action == "Skip"),
                    Errors = results.Count(r => r.Sheet == sheet && r.Action == "Error")
                };

                var summary = new List<CustomerImportCount>();
                if (Request.Suppliers.Count > 0) summary.Add(Count(SheetSuppliers, Request.Suppliers.Count));
                if (Request.Contacts.Count > 0) summary.Add(Count(SheetContacts, Request.Contacts.Count));
                if (Request.Terms.Count > 0) summary.Add(Count(SheetTerms, Request.Terms.Count));

                var errors = ErrorCount;
                var work = results.Count(r => r.Action is "New" or "Update");

                if (errors == 0 && work == 0)
                    Issues.Add(new CustomerImportIssue
                    {
                        Sheet = "Import", Level = "Warning", Field = "",
                        Message = "Every row already exists and would be skipped. Choose 'Update existing' to overwrite them."
                    });

                return new SupplierImportReport
                {
                    DryRun = dryRun,
                    CanImport = errors == 0 && work > 0,
                    ErrorCount = errors,
                    WarningCount = Issues.Count(i => i.Level == "Warning"),
                    Summary = summary,
                    MastersToCreate = MastersToCreate,
                    Issues = Issues.OrderBy(i => i.Level == "Error" ? 0 : 1)
                                   .ThenBy(i => i.Sheet).ThenBy(i => i.RowNo).ToList(),
                    Results = results
                };
            }
        }
    }
}
