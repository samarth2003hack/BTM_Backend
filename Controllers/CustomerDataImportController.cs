using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Bulk import of customers and their contacts, terms and shipping
    /// addresses from the Customer Data Import page.
    ///
    /// Validate and Import run the same pipeline; Validate stops before writing.
    /// Import is all-or-nothing: any row error blocks it, and any procedure
    /// refusal mid-way rolls the whole file back.
    ///
    /// Order inside the transaction:
    ///   1. lookup values the file uses but the masters lack (Category and
    ///      Sales Exec in Other Master, State Master, Currency Master)
    ///   2. customers - SP_CustomerMaster allocates the codes (SQL/37)
    ///   3. contacts, terms, shipping addresses, linked by Customer Name
    ///
    /// Every write goes through the same SP_* procedures as the master pages,
    /// so their rules and the audit triggers apply unchanged.
    /// </summary>
    [Route("api/[controller]")]
    public class CustomerDataImportController : EmsControllerBase
    {
        private readonly EMSContext db;

        public CustomerDataImportController(EMSContext context) => db = context;

        // Other Master types CustomerMaster.html reads its dropdowns from.
        private const string CategoryType = "Vendor Category";
        private const string SalesExecType = "Sales Executive";
        private const string LookupDepartment = "Sales";

        private const int MaxRows = 10000;

        private const string SheetCustomers = "Customers";
        private const string SheetContacts = "Contacts";
        private const string SheetTerms = "Terms";
        private const string SheetShipping = "Shipping Address";

        // Closed lists - the same values the Customer Master page offers.
        private static readonly string[] CustomerTypes = { "Inter State", "Intra State", "Export" };
        private static readonly string[] TaxTypes = { "REGULAR", "URD", "COMPOSITE", "SEZ", "EXPORT" };
        private static readonly string[] Titles = { "Mr", "Mrs", "Ms" };
        private static readonly string[] TermTypes = { "Fix", "Editable" };

        private static readonly Regex GstinRx = new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$");
        private static readonly Regex PanRx = new(@"^[A-Z]{5}[0-9]{4}[A-Z]$");
        private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        private static readonly Regex Spaces = new(@"\s+");

        //======================================================================
        // Endpoints
        //======================================================================

        /// <summary>
        /// Current master values, for the "Lists" sheet of the downloadable
        /// template and the field hints on the page.
        /// </summary>
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

                var others = await db.OtherMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" &&
                                (x.MasterType == CategoryType || x.MasterType == SalesExecType))
                    .OrderBy(x => x.MasterName)
                    .Select(x => new { x.MasterType, x.MasterName })
                    .ToListAsync();

                return OkResult(new
                {
                    States = states,
                    Currencies = currencies,
                    Categories = others.Where(x => x.MasterType == CategoryType).Select(x => x.MasterName),
                    SalesExecs = others.Where(x => x.MasterType == SalesExecType).Select(x => x.MasterName),
                    CustomerTypes,
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
        public Task<IActionResult> Validate([FromBody] CustomerImportRequest req) => Run(req, dryRun: true);

        [HttpPost("Import")]
        public Task<IActionResult> Import([FromBody] CustomerImportRequest req) => Run(req, dryRun: false);

        //======================================================================
        // Pipeline
        //======================================================================

        private async Task<IActionResult> Run(CustomerImportRequest req, bool dryRun)
        {
            if (req == null)
                return BadRequestResult("Invalid request.");
            if (req.SubscID <= 0 || req.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            var type = (req.ImportType ?? "ALL").Trim().ToUpperInvariant();
            if (type is not ("ALL" or "CUSTOMER" or "CONTACT" or "TERM" or "SHIPPING"))
                return BadRequestResult("Import Type must be ALL, CUSTOMER, CONTACT, TERM or SHIPPING.");

            // The page only sends the sheets of the chosen type; drop anything else.
            if (type is not ("ALL" or "CUSTOMER")) req.Customers = new();
            if (type is not ("ALL" or "CONTACT")) req.Contacts = new();
            if (type is not ("ALL" or "TERM")) req.Terms = new();
            if (type is not ("ALL" or "SHIPPING")) req.Shipping = new();

            var total = req.Customers.Count + req.Contacts.Count + req.Terms.Count + req.Shipping.Count;
            if (total == 0)
                return BadRequestResult("The file has no data rows for the selected import type.");
            if (total > MaxRows)
                return BadRequestResult($"At most {MaxRows} rows can be imported at once; this file has {total}. Split it.");

            try
            {
                var plan = new ImportPlan(req, await LookupCache.LoadAsync(db, req.SubscID));

                PlanCustomers(plan);
                PlanContacts(plan);
                PlanTerms(plan);
                PlanShipping(plan);

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

        private async Task<IActionResult> Execute(ImportPlan plan, CustomerImportReport report)
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
                        case "Sales Exec":
                            var masterType = m.Master == "Category" ? CategoryType : SalesExecType;
                            await EnsureMasterTypeListed(masterType, req.SubscID);
                            r = await db.CallOtherMasterSP(new SP_OtherMaster
                            {
                                MasterType = masterType,
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

                // 2. Customers ----------------------------------------------
                foreach (var c in plan.Customers.Where(x => x.Action is "New" or "Update"))
                {
                    c.Dto!.SystemIP = ip;
                    var r = await AtRow(SheetCustomers, c.RowNo, () => db.CallCustomerMasterSP(c.Dto));
                    if (r.ID <= 0)
                        throw new ImportAbort(SheetCustomers, c.RowNo, r.Message);

                    c.MTransNo = r.ID;
                    c.Result.CustomerCode = await db.CustomerMaster.AsNoTracking()
                        .Where(x => x.MTransNo == r.ID)
                        .Select(x => x.CustomerCode)
                        .FirstOrDefaultAsync();
                    c.Result.Message = c.Action == "New" ? "Created." : "Updated.";
                }

                // 3. Children -----------------------------------------------
                foreach (var ch in plan.Children.Where(x => x.Action is "New" or "Update"))
                {
                    var parentId = ch.Parent.MTransNo;
                    if (parentId <= 0)
                        throw new ImportAbort(ch.Sheet, ch.RowNo, $"Customer '{ch.Result.CustomerName}' was not saved.");

                    var r = await AtRow(ch.Sheet, ch.RowNo, () => ch.Execute!(parentId, ip));
                    if (r.ID <= 0)
                        throw new ImportAbort(ch.Sheet, ch.RowNo, r.Message);

                    ch.Result.CustomerCode = ch.Parent.Code;
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
                    if (res.Sheet == SheetCustomers && res.Action == "New") res.CustomerCode = "(auto)";
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
        /// Runs one procedure call and pins any exception to its sheet row. A
        /// procedure whose CATCH swallows an error inside our transaction leaves
        /// it uncommittable, and SQL Server then raises at the end of the batch -
        /// without this the report could not say which row did it.
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

        /// <summary>
        /// The Other Master page only offers types listed in OtherMasterList, so
        /// a type the import starts using is added there too (SQL/37 seeds it for
        /// the subscriptions that existed when it ran).
        /// </summary>
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
        // Planning - Customers
        //======================================================================

        private static void PlanCustomers(ImportPlan p)
        {
            const string S = SheetCustomers;
            var req = p.Request;
            var update = p.UpdateExisting;

            foreach (var r in req.Customers)
            {
                var errs = p.Issues.Count(i => i.Level == "Error");
                var name = Name(r.CustomerName);
                var plan = new CustomerPlan { RowNo = r.RowNo, Name = name ?? "" };
                plan.Result = new CustomerImportResult { Sheet = S, RowNo = r.RowNo, CustomerName = name ?? "" };
                p.Customers.Add(plan);

                if (name == null)
                {
                    p.Error(S, r.RowNo, "Customer Name", "Customer Name is required.");
                    plan.Fail("Customer Name is required.");
                    continue;
                }
                p.Length(S, r.RowNo, "Customer Name", name, 100);

                if (p.CustomersInFile.ContainsKey(Key(name)))
                {
                    var first = p.CustomersInFile[Key(name)].RowNo;
                    p.Error(S, r.RowNo, "Customer Name", $"'{name}' appears again - first on row {first}. Each customer once.");
                    plan.Fail("Duplicate in file.");
                    continue;
                }
                p.CustomersInFile[Key(name)] = plan;

                p.Lookups.Customers.TryGetValue(Key(name), out var ex);
                plan.Existing = ex;

                if (ex != null && !update)
                {
                    plan.Action = "Skip";
                    plan.MTransNo = ex.MTransNo;
                    plan.Code = ex.CustomerCode;
                    plan.CustomerType = ex.CustomerType;
                    plan.TaxType = ex.TaxType;
                    plan.Result.Action = "Skip";
                    plan.Result.CustomerCode = ex.CustomerCode;
                    plan.Result.Message = "Already in Customer Master - skipped.";
                    continue;
                }

                var isNew = ex == null;

                // Customer Type
                string? custType = ex?.CustomerType;
                if (Clean(r.CustomerType) is { } ct)
                {
                    custType = Pick(ct, CustomerTypes);
                    if (custType == null)
                        p.Error(S, r.RowNo, "Customer Type", $"'{ct}' is not valid. Use: {string.Join(", ", CustomerTypes)}.");
                }
                else if (isNew)
                    p.Error(S, r.RowNo, "Customer Type", $"Customer Type is required ({string.Join(", ", CustomerTypes)}).");

                // Address
                var address = Clean(r.CustomerAddress) ?? ex?.CustomerAddress;
                if (address == null && isNew)
                    p.Error(S, r.RowNo, "Customer Address", "Customer Address is required.");
                p.Length(S, r.RowNo, "Customer Address", address, 300);

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

                // Phone
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
                taxType ??= custType == "Export" ? "EXPORT" : gstin == null ? "URD" : "REGULAR";

                var gstExempt = custType == "Export" || taxType is "URD" or "EXPORT";
                if (gstin == null && !gstExempt)
                    p.Error(S, r.RowNo, "GSTIN", $"GSTIN is required for Tax Type {taxType} (leave it blank only for URD / EXPORT / Export customers).");

                if (gstinIn != null && GstinRx.IsMatch(gstinIn) && stateCode is int sc && sc < 100 &&
                    gstinIn[..2] != sc.ToString("00"))
                    p.Warn(S, r.RowNo, "GSTIN", $"GSTIN starts with {gstinIn[..2]} but the state code is {sc:00}.");

                var panIn = Upper(r.PAN);
                if (panIn != null && !PanRx.IsMatch(panIn))
                    p.Error(S, r.RowNo, "PAN", $"'{panIn}' is not a valid 10-character PAN.");
                var pan = panIn ?? ex?.PAN;
                if (pan == null && gstinIn != null && GstinRx.IsMatch(gstinIn))
                    pan = gstinIn.Substring(2, 10);   // PAN is characters 3-12 of the GSTIN

                // Currency, Category, Sales Exec - created when missing
                var currency = ex?.TxnCurrency;
                if (Clean(r.TxnCurrency) is { } cur)
                    currency = p.ResolveCurrency(S, r.RowNo, cur);
                else if (isNew)
                    currency = "INR";

                var category = ex?.Category;
                if (Name(r.Category) is { } cat)
                    category = p.ResolveOther(S, r.RowNo, "Category", cat, p.Lookups.Categories, p.NewCategories);

                var salesExec = ex?.SalesExec;
                if (Name(r.SalesExec) is { } se)
                    salesExec = p.ResolveOther(S, r.RowNo, "Sales Exec", se, p.Lookups.SalesExecs, p.NewSalesExecs);

                // Numbers
                var creditPeriod = ex?.CreditPeriod;
                if (Clean(r.CreditPeriod) is { } cpText)
                {
                    if (TryInt(cpText, out var cp) && cp >= 0 && cp <= 999) creditPeriod = cp;
                    else p.Error(S, r.RowNo, "Credit Period", $"'{cpText}' must be a whole number of days (0-999).");
                }

                var creditLimit = ex?.CreditLimit;
                if (Clean(r.CreditLimit) is { } clText)
                {
                    if (TryDec(clText, out var cl) && cl >= 0) creditLimit = cl;
                    else p.Error(S, r.RowNo, "Credit Limit", $"'{clText}' must be a number, 0 or more.");
                }

                var alias = Name(r.AliasName) ?? ex?.AliasName;
                p.Length(S, r.RowNo, "Alias Name", alias, 100);
                var vendorCode = Clean(r.VendorCode) ?? ex?.VendorCode;
                p.Length(S, r.RowNo, "Vendor Code", vendorCode, 30);
                var pin = Clean(r.PinCode) ?? ex?.PinCode;
                p.Length(S, r.RowNo, "Pin Code", pin, 30);
                var pinLoc = Clean(r.PinCodeLocation) ?? ex?.PinCodeLocation;
                p.Length(S, r.RowNo, "Pin Code Location", pinLoc, 50);
                var remarks = Clean(r.Remarks) ?? ex?.Remarks;
                p.Length(S, r.RowNo, "Remarks", remarks, 500);

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                plan.CustomerType = custType;
                plan.TaxType = taxType;

                if (p.Issues.Count(i => i.Level == "Error") > errs)
                {
                    plan.Fail("Row has errors.");
                    continue;
                }

                plan.Action = isNew ? "New" : "Update";
                plan.Code = ex?.CustomerCode;
                plan.Result.Action = plan.Action;
                plan.Result.CustomerCode = isNew ? "(auto)" : ex!.CustomerCode;
                plan.Result.Message = isNew ? "Will be created - code allocated on import." : "Will update the existing customer.";

                plan.Dto = new SP_CustomerMaster
                {
                    MTransNo = ex?.MTransNo ?? 0,
                    MCount = isNew ? 0 : (ex!.LockCounter ?? 0) + 1,
                    CustomerCode = ex?.CustomerCode ?? string.Empty,   // blank = SP allocates
                    CustomerType = custType!,
                    // The name is the match key - an update keeps the stored spelling.
                    CustomerName = ex?.CustomerName ?? name,
                    AliasName = alias,
                    Category = category,
                    CustomerAddress = address,
                    PinCode = pin,
                    PinCodeLocation = pinLoc,
                    StateName = stateName,
                    StateCode = stateCode,
                    PhoneNo = phone,
                    MobileNo = mobile,
                    EmailID = email,
                    Website = website,
                    TaxType = taxType,
                    GSTIN = gstin,
                    PAN = pan,
                    TxnCurrency = currency ?? "INR",
                    SalesExec = salesExec,
                    CreditPeriod = creditPeriod,
                    CreditLimit = creditLimit,
                    CustomerPriceList = ex?.CustomerPriceList,
                    VendorCode = vendorCode,
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
                var ch = p.NewChild(S, r.RowNo, r.CustomerName, cpName);
                if (ch == null) continue;

                if (cpName == null) p.Error(S, r.RowNo, "Contact Name", "Contact Name is required.");
                p.Length(S, r.RowNo, "Contact Name", cpName, 50);

                ContactRow? ex = null;
                if (cpName != null)
                {
                    var k = ch.Parent.Key + "|" + Key(cpName);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Contact Name", $"'{cpName}' is already listed for this customer on row {first}.");
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
                ch.Execute = (mTransNo, ip) => p.Db.CallCustomerContactsSP(new SP_CustomerContacts
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
                var ch = p.NewChild(S, r.RowNo, r.CustomerName, title);
                if (ch == null) continue;

                if (title == null) p.Error(S, r.RowNo, "Term Title", "Term Title is required.");
                p.Length(S, r.RowNo, "Term Title", title, 30);

                TermRow? ex = null;
                if (title != null)
                {
                    var k = ch.Parent.Key + "|" + Key(title);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Term Title", $"'{title}' is already listed for this customer on row {first}.");
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
                ch.Execute = (mTransNo, ip) => p.Db.CallCustomerTermSP(new SP_CustomerTerm
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

        private static void PlanShipping(ImportPlan p)
        {
            const string S = SheetShipping;
            var req = p.Request;
            var seen = new Dictionary<string, int>();

            foreach (var r in req.Shipping)
            {
                var errs = p.ErrorCount;
                var shipName = Name(r.ShipName);
                var ch = p.NewChild(S, r.RowNo, r.CustomerName, shipName);
                if (ch == null) continue;

                if (shipName == null) p.Error(S, r.RowNo, "Ship Name", "Ship Name is required.");
                p.Length(S, r.RowNo, "Ship Name", shipName, 100);

                ShippingRow? ex = null;
                if (shipName != null)
                {
                    var k = ch.Parent.Key + "|" + Key(shipName);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Ship Name", $"'{shipName}' is already listed for this customer on row {first}.");
                    else seen[k] = r.RowNo;

                    if (ch.Parent.ExistingId > 0)
                        p.Lookups.Shipping.TryGetValue(ch.Parent.ExistingId + "|" + Key(shipName), out ex);
                }

                var alias = Name(r.ShipAlias) ?? ex?.ShipAlias;
                p.Length(S, r.RowNo, "Ship Alias", alias, 100);
                var address = Clean(r.ShipAddress) ?? ex?.ShipAddress;
                if (address == null) p.Error(S, r.RowNo, "Ship Address", "Ship Address is required.");
                p.Length(S, r.RowNo, "Ship Address", address, 300);

                string? stateName = ex?.ShipState;
                int? stateCode = ex?.ShipStateCode;
                if (Clean(r.ShipState) != null || Clean(r.ShipStateCode) != null)
                {
                    var st = p.ResolveState(S, r.RowNo, "State", r.ShipState, r.ShipStateCode);
                    if (st != null) { stateName = st.Value.Name; stateCode = st.Value.Code; }
                }
                else if (stateName == null)
                    p.Error(S, r.RowNo, "State", "State is required.");

                var pin = Clean(r.PinCode) ?? ex?.PinCode;
                if (pin == null) p.Error(S, r.RowNo, "Pin Code", "Pin Code is required.");
                p.Length(S, r.RowNo, "Pin Code", pin, 30);
                var pinLoc = Clean(r.PinCodeLocation) ?? ex?.PinCodeLocation;
                p.Length(S, r.RowNo, "Pin Code Location", pinLoc, 50);
                var phone = Clean(r.PhoneNo) ?? ex?.PhoneNo;
                if (phone == null) p.Error(S, r.RowNo, "Phone No", "Phone No is required.");
                p.Length(S, r.RowNo, "Phone No", phone, 30);
                var mobile = Clean(r.MobileNo) ?? ex?.MobileNo;
                p.Length(S, r.RowNo, "Mobile No", mobile, 30);
                var email = Clean(r.EmailID) ?? ex?.EmailID;
                p.Email(S, r.RowNo, "Email", Clean(r.EmailID));
                p.Length(S, r.RowNo, "Email", email, 80);

                var gstinIn = Upper(r.GSTIN);
                if (gstinIn != null && !GstinRx.IsMatch(gstinIn))
                    p.Error(S, r.RowNo, "GSTIN", $"'{gstinIn}' is not a valid 15-character GSTIN.");
                var gstin = gstinIn ?? ex?.GSTIN;
                var exempt = ch.Parent.CustomerType == "Export" || ch.Parent.TaxType is "URD" or "EXPORT";
                if (gstin == null && !exempt)
                    p.Error(S, r.RowNo, "GSTIN", "GSTIN is required (optional only for URD / Export customers).");

                var panIn = Upper(r.PAN);
                if (panIn != null && !PanRx.IsMatch(panIn))
                    p.Error(S, r.RowNo, "PAN", $"'{panIn}' is not a valid 10-character PAN.");
                var pan = panIn ?? ex?.PAN;
                if (pan == null && gstinIn != null && GstinRx.IsMatch(gstinIn))
                    pan = gstinIn.Substring(2, 10);

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                if (!p.Settle(ch, errs, ex != null)) continue;

                var transSlNo = ex?.TransSlNo ?? 0;
                ch.Execute = (mTransNo, ip) => p.Db.CallShippingAddressSP(new SP_ShippingAddress
                {
                    TransSlNo = transSlNo,
                    MTransNo = mTransNo,
                    ShipName = shipName!,
                    ShipAlias = alias,
                    ShipAddress = address,
                    ShipState = stateName,
                    ShipStateCode = stateCode,
                    PinCodeLocation = pinLoc,
                    PinCode = pin,
                    PhoneNo = phone,
                    MobileNo = mobile,
                    EmailID = email,
                    GSTIN = gstin,
                    PAN = pan,
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

        /// <summary>Trimmed text, or null when blank.</summary>
        private static string? Clean(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        /// <summary>A name: trimmed with runs of whitespace collapsed to one space.</summary>
        private static string? Name(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v.Trim(), " ");

        /// <summary>Upper-cased with all whitespace removed (GSTIN, PAN, codes).</summary>
        private static string? Upper(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v, "").ToUpperInvariant();

        /// <summary>Case- and space-insensitive key, matching the database collation.</summary>
        private static string Key(string v) => Spaces.Replace(v.Trim(), " ").ToUpperInvariant();

        /// <summary>Matches a value to a closed list ignoring case, spaces and hyphens.</summary>
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

        private sealed class CustomerPlan
        {
            public int RowNo;
            public string Name = "";
            public string Action = "Error";
            public CustomerMaster? Existing;
            public SP_CustomerMaster? Dto;
            public int MTransNo;
            public string? Code;
            public string? CustomerType;
            public string? TaxType;
            public CustomerImportResult Result = null!;

            public void Fail(string message)
            {
                Action = "Error";
                Result.Action = "Error";
                Result.Message = message;
            }
        }

        /// <summary>The customer a child row hangs off - a row of this file, or one already saved.</summary>
        private sealed class ParentRef
        {
            public string Key = "";
            public CustomerPlan? Plan;
            public CustomerMaster? Existing;

            public int ExistingId => Plan?.Existing?.MTransNo ?? Existing?.MTransNo ?? 0;
            public int MTransNo => Plan?.MTransNo ?? Existing?.MTransNo ?? 0;
            public string? Code => Plan != null ? Plan.Result.CustomerCode : Existing?.CustomerCode;
            public string? CustomerType => Plan?.CustomerType ?? Existing?.CustomerType;
            public string? TaxType => Plan?.TaxType ?? Existing?.TaxType;
        }

        private sealed class ChildPlan
        {
            public string Sheet = "";
            public int RowNo;
            public string Action = "Error";
            public ParentRef Parent = null!;
            public Func<int, string?, Task<(int ID, string Message)>>? Execute;
            public CustomerImportResult Result = null!;
        }

        private sealed record ContactRow(int TransSlNo, string? Title, string? Designation, string? PhoneNo,
            string? MobileNo, string? Email, string? LockStatus);
        private sealed record TermRow(int TransSlNo, decimal? TermSlNo, string? TermDescription, string? TermType,
            string? LockStatus);
        private sealed record ShippingRow(int TransSlNo, string? ShipAlias, string? ShipAddress, string? ShipState,
            int? ShipStateCode, string? PinCodeLocation, string? PinCode, string? PhoneNo, string? MobileNo,
            string? EmailID, string? GSTIN, string? PAN, string? LockStatus);

        /// <summary>Everything the checks compare against, read once per request.</summary>
        private sealed class LookupCache
        {
            public EMSContext Db = null!;
            public Dictionary<string, (string Name, int Code)> StatesByName = new();
            public Dictionary<int, string> StatesByCode = new();
            public Dictionary<string, string> CurrencyByCode = new();   // KEY -> stored code
            public Dictionary<string, string> CurrencyByName = new();   // KEY(name) -> stored code
            public Dictionary<string, string> Categories = new();       // KEY -> stored spelling
            public Dictionary<string, string> SalesExecs = new();
            public Dictionary<string, CustomerMaster> Customers = new();
            public Dictionary<string, ContactRow> Contacts = new();     // "MTransNo|KEY(CPName)"
            public Dictionary<string, TermRow> Terms = new();           // "MTransNo|KEY(TermTitle)"
            public Dictionary<string, ShippingRow> Shipping = new();    // "MTransNo|KEY(ShipName)"

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

                foreach (var o in await db.OtherMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.MasterName != null &&
                                         (x.MasterType == CategoryType || x.MasterType == SalesExecType))
                             .Select(x => new { x.MasterType, x.MasterName }).ToListAsync())
                {
                    var target = o.MasterType == CategoryType ? c.Categories : c.SalesExecs;
                    target.TryAdd(Key(o.MasterName!), o.MasterName!.Trim());
                }

                foreach (var cm in await db.CustomerMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.CustomerName != null)
                             .ToListAsync())
                    c.Customers.TryAdd(Key(cm.CustomerName!), cm);

                foreach (var x in await db.CustomerContacts.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.CPName != null).ToListAsync())
                    c.Contacts.TryAdd(x.MTransNo + "|" + Key(x.CPName!),
                        new ContactRow(x.TransSlNo, x.Title, x.Designation, x.PhoneNo, x.MobileNo, x.Email, x.LockStatus));

                foreach (var x in await db.CustomerTerm.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.TermTitle != null).ToListAsync())
                    c.Terms.TryAdd(x.MTransNo + "|" + Key(x.TermTitle!),
                        new TermRow(x.TransSlNo, x.TermSlNo, x.TermDescription, x.TermType, x.LockStatus));

                foreach (var x in await db.ShippingAddress.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.ShipName != null).ToListAsync())
                    c.Shipping.TryAdd(x.MTransNo + "|" + Key(x.ShipName!),
                        new ShippingRow(x.TransSlNo, x.ShipAlias, x.ShipAddress, x.ShipState, x.ShipStateCode,
                            x.PinCodeLocation, x.PinCode, x.PhoneNo, x.MobileNo, x.EmailID, x.GSTIN, x.PAN, x.LockStatus));

                return c;
            }
        }

        private sealed class ImportPlan
        {
            public CustomerImportRequest Request { get; }
            public LookupCache Lookups { get; }
            public EMSContext Db => Lookups.Db;
            public bool UpdateExisting { get; }

            public List<CustomerImportIssue> Issues { get; } = new();
            public List<CustomerPlan> Customers { get; } = new();
            public Dictionary<string, CustomerPlan> CustomersInFile { get; } = new();
            public List<ChildPlan> Children { get; } = new();

            // New master values, KEY -> value as first written in the file.
            public Dictionary<string, string> NewCategories { get; } = new();
            public Dictionary<string, string> NewSalesExecs { get; } = new();
            public Dictionary<string, int> NewStates { get; } = new();            // KEY(name) -> code
            private readonly Dictionary<string, string> newStateNames = new();    // KEY(name) -> name
            private readonly Dictionary<int, string> newStateCodes = new();       // code -> name
            private readonly Dictionary<string, string> newCurrencies = new();    // KEY -> code

            public List<CustomerImportMaster> MastersToCreate { get; } = new();

            public int ErrorCount => Issues.Count(i => i.Level == "Error");

            public ImportPlan(CustomerImportRequest req, LookupCache lookups)
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

            /// <summary>
            /// State by name, or by code when only the code is given. A state the
            /// master lacks is created - but only with a code, since StateCode
            /// drives GST.
            /// </summary>
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

            /// <summary>Category / Sales Exec value; created in Other Master when missing.</summary>
            public string? ResolveOther(string sheet, int row, string field, string value,
                Dictionary<string, string> existing, Dictionary<string, string> planned)
            {
                if (value.Length > 50)
                {
                    Error(sheet, row, field, $"{field} is {value.Length} characters; the maximum is 50.");
                    return null;
                }
                var k = Key(value);
                if (existing.TryGetValue(k, out var stored)) return stored;
                if (planned.TryGetValue(k, out stored)) return stored;

                planned[k] = value;
                MastersToCreate.Add(new CustomerImportMaster
                {
                    Master = field, Value = value,
                    Detail = $"Other Master - {(field == "Category" ? CategoryType : SalesExecType)}"
                });
                return value;
            }

            /// <summary>
            /// Starts a child row and links it to its customer: a valid row of the
            /// Customers sheet first, else a customer already in the master.
            /// Returns null (row already reported) when the customer cannot be found.
            /// </summary>
            public ChildPlan? NewChild(string sheet, int row, string? customerName, string? item)
            {
                var name = Name(customerName);
                var ch = new ChildPlan
                {
                    Sheet = sheet,
                    RowNo = row,
                    Result = new CustomerImportResult { Sheet = sheet, RowNo = row, CustomerName = name ?? "", Item = item }
                };
                Children.Add(ch);

                void Fail(string msg)
                {
                    Error(sheet, row, "Customer Name", msg);
                    ch.Result.Action = "Error";
                    ch.Result.Message = msg;
                }

                if (name == null)
                {
                    Fail("Customer Name is required - it links this row to its customer.");
                    return null;
                }

                if (CustomersInFile.TryGetValue(Key(name), out var plan))
                {
                    if (plan.Action == "Error")
                    {
                        Fail($"Customer '{name}' (Customers sheet row {plan.RowNo}) has errors - fix that row first.");
                        return null;
                    }
                    ch.Parent = new ParentRef { Key = Key(name), Plan = plan };
                    return ch;
                }

                if (Lookups.Customers.TryGetValue(Key(name), out var existing))
                {
                    ch.Parent = new ParentRef { Key = Key(name), Existing = existing };
                    return ch;
                }

                Fail($"Customer '{name}' is not in Customer Master" +
                     (Request.Customers.Count > 0 ? " or the Customers sheet." : " - import the customer first, or use import type 'All'."));
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

                ch.Result.CustomerCode = ch.Parent.Code;

                if (exists && !UpdateExisting)
                {
                    ch.Action = "Skip";
                    ch.Result.Action = "Skip";
                    ch.Result.Message = "Already on this customer - skipped.";
                    return false;
                }

                ch.Action = exists ? "Update" : "New";
                ch.Result.Action = ch.Action;
                ch.Result.Message = exists ? "Will update the existing row." : "Will be added.";
                return true;
            }

            public CustomerImportReport BuildReport(bool dryRun)
            {
                var results = Customers.Select(c => c.Result).Concat(Children.Select(c => c.Result)).ToList();

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
                if (Request.Customers.Count > 0) summary.Add(Count(SheetCustomers, Request.Customers.Count));
                if (Request.Contacts.Count > 0) summary.Add(Count(SheetContacts, Request.Contacts.Count));
                if (Request.Terms.Count > 0) summary.Add(Count(SheetTerms, Request.Terms.Count));
                if (Request.Shipping.Count > 0) summary.Add(Count(SheetShipping, Request.Shipping.Count));

                var errors = ErrorCount;
                var work = results.Count(r => r.Action is "New" or "Update");

                if (errors == 0 && work == 0)
                    Issues.Add(new CustomerImportIssue
                    {
                        Sheet = "Import", Level = "Warning", Field = "",
                        Message = "Every row already exists and would be skipped. Choose 'Update existing' to overwrite them."
                    });

                return new CustomerImportReport
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
