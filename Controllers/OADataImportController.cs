using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Bulk import of Order Acceptances (open customer orders) from the OA
    /// Data Import page. Same design as PartDataImportController: Validate and
    /// Import run one pipeline, Validate stops before writing, Import is
    /// all-or-nothing.
    ///
    /// Every OA is written through the same procedures the Order Acceptance
    /// page uses, in the same order:
    ///   1. header      SP_OAMst  (Insert, DocAmdNo 0 - the OA No is allocated there)
    ///   2. lines       SP_OADtl + SP_OADtlPlanFields
    ///   3. terms       SP_OATerms (optional: the standard CUSTOMER terms)
    ///   4. approval    SP_OAApproval 'Approve' (only when importing as Approved)
    ///
    /// Amounts are never read from the file. They are computed here with the
    /// page's formulas (OrderAcceptance.html 4.14-4.21 and 5.14): basic,
    /// discount, P&amp;F, taxable, GST split by the customer's type, exempt
    /// lines, INR round off and the currency amount. GST % and unit come from
    /// Part Master; the customer block from Customer Master.
    ///
    /// Nothing is created in any master: customer, part and grade must exist.
    /// The old system's order number (Order Ref) is kept in OtherRefNo and is
    /// the duplicate key - an Order Ref already on a live OA is skipped.
    /// </summary>
    [Route("api/[controller]")]
    public class OADataImportController : EmsControllerBase
    {
        private readonly EMSContext db;

        public OADataImportController(EMSContext context) => db = context;

        private const int MaxOrders = 500;
        private const int MaxLines = 5000;

        private const string SheetOrders = "Orders";
        private const string SheetLines = "Order Lines";

        private const string HomeCurrency = "INR";
        private const string SeriesType = "DocSeries";

        private static readonly string[] ApprovalModes = { "DRAFT", "SUBMITTED", "APPROVED" };

        private static readonly Regex Spaces = new(@"\s+");

        //======================================================================
        // Endpoints
        //======================================================================

        /// <summary>Current master values, for the template's "Lists" sheet and the page's options.</summary>
        [HttpGet("GetLookups/{subscID}")]
        public async Task<IActionResult> GetLookups(int subscID, [FromQuery] int userNo = 0)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var series = await db.OtherMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" &&
                                x.MasterType == SeriesType && x.MasterName != null)
                    .OrderBy(x => x.MasterName)
                    .Select(x => x.MasterName!)
                    .ToListAsync();

                var currencies = await db.CurrencyMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" && x.CurrencyCode != null)
                    .OrderBy(x => x.CurrencyCode)
                    .Select(x => new { x.CurrencyCode, x.Currency })
                    .ToListAsync();

                var customers = await db.CustomerMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" && x.CustomerCode != null)
                    .OrderBy(x => x.CustomerCode)
                    .Select(x => new { x.CustomerCode, x.CustomerName, x.CustomerType, x.TaxType, x.TxnCurrency })
                    .ToListAsync();

                var partGrades = await (
                        from g in db.MaterialGrade.AsNoTracking()
                        join p in db.PartMaster.AsNoTracking() on g.PartNo equals p.MTransNo
                        where g.SubscID == subscID && g.DeleteStatus == "N" && g.LockStatus == "Y" &&
                              p.SubscID == subscID && p.DeleteStatus == "N" && p.LockStatus == "Y" &&
                              p.PartCode != null && g.Grade != null
                        orderby p.PartCode, g.Grade
                        select new { p.PartCode, p.PartName, g.Grade, p.PrimaryUnit, p.TaxP })
                    .ToListAsync();

                var userType = userNo > 0
                    ? await db.UserMaster.AsNoTracking().Where(x => x.MTransNo == userNo)
                        .Select(x => x.UserType).FirstOrDefaultAsync()
                    : null;

                var termCount = await db.StandardTerms.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.TermType == "CUSTOMER" &&
                                     x.DeleteStatus == "N" && x.LockStatus == "Y");

                return OkResult(new
                {
                    Series = series,
                    Currencies = currencies,
                    Customers = customers,
                    PartGrades = partGrades,
                    StandardTermCount = termCount,
                    CanApprove = !IsExecutive(userType),
                    ApprovalModes,
                    YesNo = new[] { "Y", "N" }
                });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Dry run: every check the import makes, nothing written.</summary>
        [HttpPost("Validate")]
        public Task<IActionResult> Validate([FromBody] OAImportRequest req) => Run(req, dryRun: true);

        [HttpPost("Import")]
        public Task<IActionResult> Import([FromBody] OAImportRequest req) => Run(req, dryRun: false);

        //======================================================================
        // Pipeline
        //======================================================================

        private async Task<IActionResult> Run(OAImportRequest req, bool dryRun)
        {
            if (req == null)
                return BadRequestResult("Invalid request.");
            if (req.SubscID <= 0 || req.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            req.Orders ??= new();
            req.Lines ??= new();

            var mode = (req.ApprovalMode ?? "").Trim().ToUpperInvariant();
            if (!ApprovalModes.Contains(mode))
                return BadRequestResult("Import As must be DRAFT, SUBMITTED or APPROVED.");
            req.ApprovalMode = mode;

            if (req.Orders.Count == 0)
                return BadRequestResult("The Orders sheet has no data rows.");
            if (req.Lines.Count == 0)
                return BadRequestResult("The Order Lines sheet has no data rows.");
            if (req.Orders.Count > MaxOrders)
                return BadRequestResult($"At most {MaxOrders} orders can be imported at once; this file has {req.Orders.Count}. Split it.");
            if (req.Lines.Count > MaxLines)
                return BadRequestResult($"At most {MaxLines} order lines can be imported at once; this file has {req.Lines.Count}. Split it.");

            try
            {
                var lookups = await LookupCache.LoadAsync(db, req.SubscID, req.CreatedBy, req.AddStandardTerms);

                // Same rule as SP_OAApproval: an executive cannot approve.
                if (mode == "APPROVED" && IsExecutive(lookups.UserType))
                    return BadRequestResult("An executive cannot approve order acceptances. Import them as Submitted - a manager approves them on the Order Acceptance Approval page.");

                var plan = new ImportPlan(req, lookups);

                PlanOrders(plan);
                PlanLines(plan);
                FinishOrders(plan);

                var report = plan.BuildReport(dryRun);

                const string nothingToDo = "Nothing to import - every order in the file is already in the system.";

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

        private static bool IsExecutive(string? userType) =>
            string.Equals(userType?.Trim(), "EXECUTIVE", StringComparison.OrdinalIgnoreCase);

        //======================================================================
        // Execute
        //======================================================================

        private async Task<IActionResult> Execute(ImportPlan plan, OAImportReport report)
        {
            var ip = ClientIp;
            var req = plan.Request;
            var approve = req.ApprovalMode == "APPROVED";
            // Approved goes in as Submitted and is then approved through
            // SP_OAApproval, so the approval is recorded as on the approval page.
            var lockStatus = req.ApprovalMode == "DRAFT" ? "N" : "S";

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                foreach (var o in plan.Orders.Where(x => x.Action == "New"))
                {
                    // 1. Header ------------------------------------------------
                    var header = o.BuildHeader!(lockStatus);
                    header.SystemIP = ip;
                    var h = await AtRow(SheetOrders, o.RowNo, () => db.CallOAMstSP(header));
                    if (h.ID <= 0)
                        throw new ImportAbort(SheetOrders, o.RowNo, h.Message);
                    o.MTransNo = h.ID;

                    // 2. Lines -------------------------------------------------
                    foreach (var l in o.Lines)
                    {
                        var dto = l.Build!(o.MTransNo);
                        dto.SystemIP = ip;
                        var r = await AtRow(SheetLines, l.RowNo, () => db.CallOADtlSP(dto));
                        if (r.ID <= 0)
                            throw new ImportAbort(SheetLines, l.RowNo, r.Message);

                        var pf = await AtRow(SheetLines, l.RowNo, () => db.CallOADtlPlanFieldsSP(
                            r.ID, l.CommittedDate, l.Priority, l.CustomerMaterial, req.CreatedBy, req.SubscID));
                        if (pf.ID <= 0)
                            throw new ImportAbort(SheetLines, l.RowNo, pf.Message);

                        l.Result.Message = "Added.";
                    }

                    // 3. Terms -------------------------------------------------
                    foreach (var t in plan.Lookups.Terms)
                    {
                        var r = await AtRow(SheetOrders, o.RowNo, () => db.CallOATermsSP(new SP_OATerms
                        {
                            MTransNo = o.MTransNo,
                            Title = Cut(t.Title, 30),
                            Description = Cut(t.Description, 250),
                            SystemIP = ip,
                            CreatedBy = req.CreatedBy,
                            CreatedOn = DateTime.Now,
                            ModifiedBy = req.CreatedBy,
                            SubscID = req.SubscID,
                            Status = "Insert"
                        }));
                        if (r.ID <= 0)
                            throw new ImportAbort(SheetOrders, o.RowNo, "Term '" + t.Title + "': " + r.Message);
                    }

                    // 4. Approval ----------------------------------------------
                    if (approve)
                    {
                        var r = await AtRow(SheetOrders, o.RowNo, () => db.CallOAApprovalSP(new SP_SalesApproval
                        {
                            MTransNo = o.MTransNo,
                            SystemIP = ip,
                            ModifiedBy = req.CreatedBy,
                            SubscID = req.SubscID,
                            Status = "Approve"
                        }));
                        if (r.ID <= 0)
                            throw new ImportAbort(SheetOrders, o.RowNo, r.Message);
                    }

                    // The OA No SP_OAMst allocated (read inside the transaction).
                    var no = await db.OAMst.AsNoTracking()
                        .Where(x => x.MTransNo == o.MTransNo)
                        .Select(x => new { x.DocSeries, x.DocNo })
                        .FirstAsync();
                    o.Result.OANo = OaNo(no.DocSeries, no.DocNo);
                    o.Result.Message = "Created OA No " + o.Result.OANo +
                                       (approve ? " (approved)." : req.ApprovalMode == "DRAFT" ? " (draft)." : " (submitted for approval).");
                    foreach (var l in o.Lines) l.Result.OANo = o.Result.OANo;
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
                foreach (var res in report.Results)
                {
                    if (res.Sheet == sheet && res.RowNo == row) { res.Action = "Error"; res.Message = message; }
                    else if (res.Action != "Skip") res.Message = "Rolled back - nothing was imported.";
                    if (res.Action != "Skip") res.OANo = null;
                }

                var where = row > 0 ? $"{sheet} row {row}" : sheet;
                return Conflict(Envelope(false, $"Import failed at {where}: {message} Nothing was imported.", report, 409));
            }

            report.Imported = true;
            report.DryRun = false;

            var orders = plan.Orders.Count(x => x.Action == "New");
            var lines = plan.Orders.Where(x => x.Action == "New").Sum(x => x.Lines.Count);
            return OkResult(report, $"Import complete: {orders} order acceptance(s) with {lines} line(s) created.");
        }

        /// <summary>Runs one procedure call and pins any exception to its sheet row.</summary>
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

        private static string OaNo(string? series, int? docNo) =>
            (series ?? "").Trim() + " / " + (docNo ?? 0);

        //======================================================================
        // Planning - Orders
        //======================================================================

        private static void PlanOrders(ImportPlan p)
        {
            const string S = SheetOrders;
            var req = p.Request;
            var lk = p.Lookups;
            var today = DateTime.Today;
            var defaultSeries = Name(req.DocSeries);

            foreach (var r in req.Orders)
            {
                var errs = p.ErrorCount;
                var issuesBefore = p.Issues.Count;
                var orderRef = Name(r.OrderRef);
                var o = new OrderPlan { RowNo = r.RowNo };
                o.Result = new OAImportResult { Sheet = S, RowNo = r.RowNo, OrderRef = orderRef ?? "" };
                p.Orders.Add(o);

                // Order Ref - the key the lines link by and the duplicate check
                if (orderRef == null)
                {
                    p.Error(S, r.RowNo, "Order Ref", "Order Ref is required - it links the order to its lines (use the old OA number).");
                    o.Fail("Order Ref is required.");
                    continue;
                }
                if (orderRef.Length > 30)
                {
                    p.Error(S, r.RowNo, "Order Ref", $"Order Ref is {orderRef.Length} characters; the maximum is 30.");
                    o.Fail("Order Ref is too long.");
                    continue;
                }
                if (p.OrdersInFile.TryGetValue(Key(orderRef), out var first))
                {
                    p.Error(S, r.RowNo, "Order Ref", $"'{orderRef}' appears again - first on row {first.RowNo}. Each order once.");
                    o.Fail("Duplicate in file.");
                    continue;
                }
                p.OrdersInFile[Key(orderRef)] = o;

                // Customer
                var code = Clean(r.CustomerCode);
                CustomerMaster? cust = null;
                if (code == null)
                    p.Error(S, r.RowNo, "Customer Code", "Customer Code is required.");
                else if (!lk.Customers.TryGetValue(Key(code), out cust))
                    p.Error(S, r.RowNo, "Customer Code", $"Customer '{code}' is not in Customer Master.");
                else if (cust.LockStatus != "Y")
                {
                    p.Error(S, r.RowNo, "Customer Code", $"Customer '{code}' is inactive in Customer Master.");
                    cust = null;
                }
                o.Result.Customer = cust?.CustomerName ?? Name(r.CustomerName) ?? code;

                // Already imported? Skip it (and its lines) before anything else.
                if (lk.OAByRef.TryGetValue(Key(orderRef), out var existing))
                {
                    o.Action = "Skip";
                    o.Result.Action = "Skip";
                    o.Result.OANo = existing;
                    o.Result.Message = $"Already in the system as OA No {existing} - skipped.";
                    // A skipped row's own errors do not block the file.
                    p.Issues.RemoveRange(issuesBefore, p.Issues.Count - issuesBefore);
                    continue;
                }

                if (cust != null)
                {
                    var given = Name(r.CustomerName);
                    if (given != null && Key(given) != Key(cust.CustomerName ?? ""))
                        p.Warn(S, r.RowNo, "Customer Name", $"'{given}' differs from the master name '{cust.CustomerName}' of {cust.CustomerCode}. The master name is used.");
                    if (string.IsNullOrWhiteSpace(cust.CustomerAddress))
                        p.Error(S, r.RowNo, "Customer Code", $"Customer '{cust.CustomerCode}' has no address in Customer Master - an OA needs it.");
                    if (string.IsNullOrWhiteSpace(cust.TaxType))
                        p.Error(S, r.RowNo, "Customer Code", $"Customer '{cust.CustomerCode}' has no Tax Type in Customer Master - an OA needs it.");
                }

                // Series - Other Master, type DocSeries
                var seriesText = Name(r.Series) ?? defaultSeries;
                string? series = null;
                if (seriesText == null)
                    p.Error(S, r.RowNo, "Series", "Series is required - fill it in the row or choose a Default Series on the page.");
                else if (!lk.Series.TryGetValue(Key(seriesText), out series))
                    p.Error(S, r.RowNo, "Series", $"Series '{seriesText}' is not in Other Master (type DocSeries).");
                else if (series.Length > 10)
                {
                    p.Error(S, r.RowNo, "Series", $"Series '{series}' is longer than 10 characters.");
                    series = null;
                }

                // Dates
                var oaDate = p.ParseDate(S, r.RowNo, "OA Date", r.OADate, required: true);
                if (oaDate > today)
                    p.Error(S, r.RowNo, "OA Date", $"OA Date {Show(oaDate)} is in the future.");

                var poNo = Name(r.PONo);
                if (poNo == null) p.Error(S, r.RowNo, "PO No", "PO No is required.");
                p.Length(S, r.RowNo, "PO No", poNo, 50);

                var poDate = p.ParseDate(S, r.RowNo, "PO Date", r.PODate, required: true);
                if (poDate > today)
                    p.Error(S, r.RowNo, "PO Date", $"PO Date {Show(poDate)} is in the future.");
                else if (poDate != null && oaDate != null && poDate > oaDate)
                    p.Warn(S, r.RowNo, "PO Date", $"PO Date {Show(poDate)} is after the OA Date {Show(oaDate)}.");

                var dueDate = p.ParseDate(S, r.RowNo, "Customer Delivery Date", r.DeliveryDate, required: true);
                var targetDate = p.ParseDate(S, r.RowNo, "Our Committed Date", r.CommittedDate, required: true);
                if (dueDate != null && dueDate < today)
                    p.Warn(S, r.RowNo, "Customer Delivery Date", $"{Show(dueDate)} is in the past - the order will show as overdue.");
                if (targetDate != null && targetDate < today)
                    p.Warn(S, r.RowNo, "Our Committed Date", $"{Show(targetDate)} is in the past - the order will show as overdue in planning.");
                if (dueDate != null && oaDate != null && dueDate < oaDate)
                    p.Warn(S, r.RowNo, "Customer Delivery Date", $"{Show(dueDate)} is before the OA Date {Show(oaDate)}.");

                // Same PO already accepted?
                if (cust != null && poNo != null && poDate != null)
                {
                    var poKey = cust.MTransNo + "|" + Key(poNo) + "|" + poDate.Value.ToString("yyyyMMdd");
                    if (lk.OAByPO.TryGetValue(poKey, out var onOa))
                        p.Error(S, r.RowNo, "PO No", $"Customer {cust.CustomerCode} PO '{poNo}' dated {Show(poDate)} is already on OA No {onOa}. Remove the row, or give it that OA's number as Order Ref so it is skipped.");
                    else if (p.PoInFile.TryGetValue(poKey, out var poRow))
                        p.Error(S, r.RowNo, "PO No", $"The same customer, PO No and PO Date are on row {poRow} too.");
                    else
                        p.PoInFile[poKey] = r.RowNo;
                }

                // Currency - blank = the customer's currency, else INR
                var curText = Upper(r.Currency) ?? Upper(cust?.TxnCurrency) ?? HomeCurrency;
                string? currency = null;
                if (!lk.Currencies.Contains(curText) && curText != HomeCurrency)
                    p.Error(S, r.RowNo, "Currency", $"Currency '{curText}' is not in Currency Master.");
                else currency = curText;

                decimal rate = 1;
                if (Clean(r.CurrencyRate) is { } rateText)
                {
                    if (!TryDec(rateText, out rate) || rate <= 0 || rate > 9999.99m)
                    {
                        p.Error(S, r.RowNo, "Currency Rate", $"'{rateText}' must be a number above 0 and at most 9999.99.");
                        rate = 1;
                    }
                    else rate = Math.Round(rate, 2, MidpointRounding.AwayFromZero);
                }
                if (currency == HomeCurrency && rate != 1)
                {
                    p.Warn(S, r.RowNo, "Currency Rate", $"The currency is {HomeCurrency}, so the rate is taken as 1 (not {rate}).");
                    rate = 1;
                }
                else if (currency != null && currency != HomeCurrency && Clean(r.CurrencyRate) == null)
                    p.Error(S, r.RowNo, "Currency Rate", $"Currency Rate is required for {currency}.");

                var notes = Name(r.Notes);
                p.Length(S, r.RowNo, "Notes", notes, 250);

                if (p.ErrorCount > errs)
                {
                    o.Fail("Row has errors.");
                    continue;
                }

                o.Action = "New";
                o.Customer = cust;
                o.Series = series!;
                o.OADate = oaDate!.Value;
                o.DueDate = dueDate!.Value;
                o.Currency = currency!;
                o.Igst = IsIgstSupply(cust!);

                o.BuildHeader = lockStatus =>
                {
                    var sum = o.Totals();
                    return new SP_OAMst
                    {
                        MTransNo = 0,
                        DocSeries = series,
                        DocYear = DocYearFor(series!, oaDate.Value),
                        DocNo = 0,
                        DocAmdNo = 0,
                        DocDate = oaDate,
                        TargetAmdNo = 0,
                        VendorNo = cust!.MTransNo,
                        VendorCode = cust.CustomerCode,
                        VendorName = cust.CustomerName,
                        Address = cust.CustomerAddress,
                        GSTIN = cust.GSTIN ?? string.Empty,
                        StateName = cust.StateName,
                        PhoneNo = cust.PhoneNo ?? cust.MobileNo,
                        EmailID = cust.EmailID,
                        RefNo = poNo,
                        RefDate = poDate,
                        OtherRefNo = orderRef,
                        SalesExec = cust.SalesExec,
                        DueDate = dueDate,
                        TargetDate = targetDate,
                        DocNotes = notes ?? string.Empty,
                        TaxType = cust.TaxType,
                        BasicValue = sum.Basic,
                        ExemptValue = sum.Exempt,
                        TaxableValue = sum.Taxable,
                        PnFAmt = sum.PnF,
                        FreightAmt = sum.Freight,
                        IGSTAmt = sum.Igst,
                        CGSTAmt = sum.Cgst,
                        SGSTAmt = sum.Sgst,
                        RoundOff = sum.RoundOff,
                        NetValue = sum.Net,
                        TxnCurrency = currency,
                        CurrencyRate = rate,
                        CurrencyAmount = sum.CurrencyAmount(rate),
                        LockStatus = lockStatus,
                        CreatedBy = req.CreatedBy,
                        CreatedOn = DateTime.Now,
                        ModifiedBy = req.CreatedBy,
                        SubscID = req.SubscID,
                        Status = "Insert"
                    };
                };
                o.Rate = rate;
            }
        }

        //======================================================================
        // Planning - Lines
        //======================================================================

        private static void PlanLines(ImportPlan p)
        {
            const string S = SheetLines;
            var req = p.Request;
            var lk = p.Lookups;

            foreach (var r in req.Lines)
            {
                var errs = p.ErrorCount;
                var orderRef = Name(r.OrderRef);
                var partCode = Clean(r.PartCode);
                var line = new LinePlan { RowNo = r.RowNo };
                line.Result = new OAImportResult
                {
                    Sheet = S, RowNo = r.RowNo, OrderRef = orderRef ?? "",
                    Item = partCode + (Name(r.Grade) is { } gr ? " / " + gr : "")
                };
                p.Lines.Add(line);

                void Fail(string field, string msg)
                {
                    p.Error(S, r.RowNo, field, msg);
                    line.Result.Action = "Error";
                    line.Result.Message = msg;
                }

                // Which order
                if (orderRef == null)
                {
                    Fail("Order Ref", "Order Ref is required - it links the line to its order on the Orders sheet.");
                    continue;
                }
                if (!p.OrdersInFile.TryGetValue(Key(orderRef), out var order))
                {
                    Fail("Order Ref", $"Order Ref '{orderRef}' is not on the Orders sheet.");
                    continue;
                }
                line.Result.Customer = order.Result.Customer;

                if (order.Action == "Skip")
                {
                    line.Result.Action = "Skip";
                    line.Result.OANo = order.Result.OANo;
                    line.Result.Message = "Order already in the system - skipped.";
                    continue;
                }
                if (order.Action == "Error")
                {
                    Fail("Order Ref", $"Order '{orderRef}' (Orders row {order.RowNo}) has errors - fix that row first.");
                    continue;
                }

                // Part and grade
                PartMaster? part = null;
                if (partCode == null)
                    p.Error(S, r.RowNo, "Part Code", "Part Code is required.");
                else if (!lk.Parts.TryGetValue(Key(partCode), out part))
                    p.Error(S, r.RowNo, "Part Code", $"Part '{partCode}' is not in Part Master.");
                else if (part.LockStatus != "Y")
                {
                    p.Error(S, r.RowNo, "Part Code", $"Part '{partCode}' is inactive in Part Master.");
                    part = null;
                }

                var gradeText = Name(r.Grade);
                string? grade = null;
                if (gradeText == null)
                    p.Error(S, r.RowNo, "Grade", "Grade is required.");
                else if (part != null)
                {
                    lk.Grades.TryGetValue(part.MTransNo, out var grades);
                    if (grades == null || grades.Count == 0)
                        p.Error(S, r.RowNo, "Grade", $"Part '{part.PartCode}' has no grades in Part Master. Add its grades first (Part Data Import, Material Grades sheet).");
                    else if (!grades.TryGetValue(Key(gradeText), out grade))
                        p.Error(S, r.RowNo, "Grade", $"Grade '{gradeText}' is not a grade of part '{part.PartCode}'. Its grades: {string.Join(", ", grades.Values.OrderBy(x => x))}.");
                }

                if (part != null && grade != null)
                {
                    var k = part.MTransNo + "|" + Key(grade);
                    if (order.PartGrades.TryGetValue(k, out var firstRow))
                        p.Error(S, r.RowNo, "Grade", $"Part '{part.PartCode}' grade '{grade}' is already on this order (row {firstRow}). Each part and grade once per order - add the quantities together.");
                    else order.PartGrades[k] = r.RowNo;
                }

                // Quantities and money
                var qty = p.ParseNumber(S, r.RowNo, "Qty", r.Qty, 3, 0, 99999999m, required: true, positive: true);
                var rate = p.ParseNumber(S, r.RowNo, "Rate", r.Rate, 2, 0, 999999999999m, required: true, positive: true);
                var discP = p.ParseNumber(S, r.RowNo, "Disc %", r.DiscP, 2, 0, 100) ?? 0;
                var pnfP = p.ParseNumber(S, r.RowNo, "P&F %", r.PnFP, 2, 0, 100) ?? 0;
                var freight = p.ParseNumber(S, r.RowNo, "Freight Amount", r.FreightAmt, 2, 0, 999999999999m) ?? 0;

                // Dates - a blank delivery date takes the order's
                var delivery = p.ParseDate(S, r.RowNo, "Delivery Date", r.DeliveryDate, required: false);
                var committed = p.ParseDate(S, r.RowNo, "Committed Date", r.CommittedDate, required: false);

                var urgent = p.ParseYesNo(S, r.RowNo, "Urgent", r.Urgent, "URGENT", "NORMAL");
                var custMat = p.ParseYesNo(S, r.RowNo, "Customer Material", r.CustomerMaterial);

                var custPartNo = Clean(r.CustPartNo);
                p.Length(S, r.RowNo, "Customer Part No", custPartNo, 20);
                var remarks = Name(r.Remarks);
                p.Length(S, r.RowNo, "Remarks", remarks, 500);
                var deviation = Name(r.Deviation);
                p.Length(S, r.RowNo, "Deviation", deviation, 250);
                var specialReq = Name(r.SpecialReq);
                p.Length(S, r.RowNo, "Special Requirement", specialReq, 250);

                int? poId = null;
                if (Clean(r.POID) is { } poIdText)
                {
                    if (long.TryParse(poIdText.Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) &&
                        v > 0 && v <= int.MaxValue)
                        poId = (int)v;
                    else
                        p.Error(S, r.RowNo, "PO Line ID", $"'{poIdText}' must be a whole number above 0.");
                }

                if (part != null && (part.TaxP ?? 0) <= 0)
                    p.Warn(S, r.RowNo, "Part Code", $"Part '{part.PartCode}' has no Tax % in Part Master - the line is imported as an exempt (no GST) supply.");

                if (p.ErrorCount > errs)
                {
                    line.Result.Action = "Error";
                    line.Result.Message = "Row has errors.";
                    order.LineErrors = true;
                    continue;
                }

                line.Action = "New";
                line.Qty = qty!.Value;
                line.Rate = rate!.Value;
                line.DiscP = discP;
                line.PnFP = pnfP;
                line.Freight = freight;
                line.GstP = Math.Round(part!.TaxP ?? 0, 2);
                line.CommittedDate = committed;
                line.Priority = urgent == "Y" ? "Urgent" : "Normal";
                line.CustomerMaterial = custMat ?? "N";
                order.Lines.Add(line);

                line.Result.Action = "New";
                line.Result.Value = R2(line.Total);
                line.Result.Message = "Will be added.";

                var igst = order.Igst;
                var dueDate = order.DueDate;
                line.Build = mTransNo =>
                {
                    var (ig, cg, sg) = SplitGst(line.Gst, igst);
                    return new SP_OADtl
                    {
                        TransSlNo = 0,
                        MTransNo = mTransNo,
                        PartNo = part.MTransNo,
                        PartCode = part.PartCode,
                        PartDescription = Cut(part.PartName, 200),
                        ItemRemarks = remarks ?? string.Empty,
                        CustPartNo = custPartNo ?? string.Empty,
                        DeliveryDate = delivery ?? dueDate,
                        CommittedDate = committed,
                        Priority = line.Priority,
                        CustomerMaterial = line.CustomerMaterial,
                        Qty = line.Qty,
                        ConvQty = line.Qty,
                        TxnUnit = null,
                        Unit = Cut(part.PrimaryUnit, 10) ?? string.Empty,
                        Rate = line.Rate,
                        BasicValue = R2(line.Basic),
                        DiscP = line.DiscP,
                        DiscAmt = R2(line.Disc),
                        PnFP = line.PnFP,
                        PnFAmt = R2(line.PnF),
                        FreightAmt = R2(line.Freight),
                        TaxableValue = R2(line.Taxable),
                        GSTP = line.GstP,
                        IGSTAmt = ig,
                        CGSTAmt = cg,
                        SGSTAmt = sg,
                        TotalValue = R2(line.Total),
                        // The grade travels in MocCode / MocDescription, as on the page.
                        MocCode = grade,
                        MocDescription = grade,
                        ListRate = 0,
                        Deviation = deviation ?? string.Empty,
                        SpecialReq = specialReq ?? string.Empty,
                        AmdNo = 0,
                        POID = poId,
                        CreatedBy = req.CreatedBy,
                        CreatedOn = DateTime.Now,
                        ModifiedBy = req.CreatedBy,
                        SubscID = req.SubscID,
                        Status = "Insert"
                    };
                };
            }
        }

        /// <summary>An order is only imported whole: every line clean, at least one line.</summary>
        private static void FinishOrders(ImportPlan p)
        {
            foreach (var o in p.Orders.Where(x => x.Action == "New"))
            {
                if (o.LineErrors)
                {
                    o.Fail("One or more of its lines have errors.");
                    foreach (var l in o.Lines)
                    {
                        l.Action = "Error";
                        l.Result.Action = "Error";
                        l.Result.Message = "Another line of this order has errors.";
                    }
                    o.Lines.Clear();
                    continue;
                }
                if (o.Lines.Count == 0)
                {
                    p.Error(SheetOrders, o.RowNo, "Order Ref", $"Order '{o.Result.OrderRef}' has no lines on the Order Lines sheet.");
                    o.Fail("No lines.");
                    continue;
                }

                var sum = o.Totals();
                o.Result.Action = "New";
                o.Result.Value = sum.Net;
                o.Result.Message = $"Will be created with {o.Lines.Count} line(s), {o.Currency} {sum.Net:0.00}.";
            }
        }

        //======================================================================
        // The OA page's formulas
        //======================================================================

        private static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>OrderAcceptance.html 4.19a: IGST for inter-state, export and SEZ.</summary>
        private static bool IsIgstSupply(CustomerMaster c)
        {
            var type = (c.CustomerType ?? "").Trim().ToUpperInvariant();
            var tax = (c.TaxType ?? "").Trim().ToUpperInvariant();
            return type is "INTER STATE" or "EXPORT" || tax is "SEZ" or "EXPORT";
        }

        /// <summary>OrderAcceptance.html 4.19b: one GST amount into IGST / CGST / SGST.</summary>
        private static (decimal Igst, decimal Cgst, decimal Sgst) SplitGst(decimal amount, bool igst)
        {
            var gst = R2(amount);
            if (igst) return (gst, 0, 0);
            var half = R2(gst / 2);
            return (0, half, R2(gst - half));
        }

        /// <summary>
        /// OrderAcceptance.html 4.4b: the number after the series' first three
        /// characters ("AF 2526" -> 2526), else the year of the OA date.
        /// </summary>
        private static int DocYearFor(string series, DateTime docDate)
        {
            var s = series.Trim();
            var rest = s.Length > 3 ? s.Substring(3).Trim() : "";
            return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n
                : docDate.Year;
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

        private static string? Cut(string? v, int max) =>
            v == null ? null : v.Length <= max ? v : v.Substring(0, max);

        private static string Show(DateTime? d) => d?.ToString("dd-MM-yyyy") ?? "";

        private static bool TryDec(string text, out decimal value) =>
            decimal.TryParse(text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d",
            "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy",
            "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yy", "d-MMM-yy",
            "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"
        };

        /// <summary>A date cell: ISO / Indian day-first text, or an Excel serial number.</summary>
        private static bool TryDate(string text, out DateTime value)
        {
            var t = text.Trim();
            if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                value = value.Date;
                return true;
            }
            // Excel serial (days since 1899-12-30), e.g. 45992
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) &&
                serial >= 20000 && serial <= 80000)
            {
                value = DateTime.FromOADate(Math.Floor(serial)).Date;
                return true;
            }
            value = default;
            return false;
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

        private sealed record OrderTotals(decimal Basic, decimal Exempt, decimal Taxable, decimal PnF, decimal Freight,
            decimal Igst, decimal Cgst, decimal Sgst, decimal RoundOff, decimal Net, decimal NetExact)
        {
            /// <summary>OrderAcceptance.html 5.14: net x currency rate.</summary>
            public decimal CurrencyAmount(decimal rate) => R2(NetExact * rate);
        }

        private sealed class OrderPlan
        {
            public int RowNo;
            public string Action = "Error";
            public CustomerMaster? Customer;
            public string Series = "";
            public DateTime OADate;
            public DateTime DueDate;
            public string Currency = HomeCurrency;
            public decimal Rate = 1;
            public bool Igst;
            public bool LineErrors;
            public List<LinePlan> Lines = new();
            public Dictionary<string, int> PartGrades = new();     // "PartNo|KEY(grade)" -> row
            public Func<string, SP_OAMst>? BuildHeader;
            public int MTransNo;
            public OAImportResult Result = null!;

            public void Fail(string message)
            {
                Action = "Error";
                Result.Action = "Error";
                Result.Message = message;
            }

            /// <summary>
            /// OrderAcceptance.html computeSummary + buildHeaderPayload + 5.14:
            /// header GST is the split of the summed line GST; a line with no
            /// GST is exempt; INR rounds the net to the rupee.
            /// </summary>
            public OrderTotals Totals()
            {
                decimal basic = 0, pnf = 0, freight = 0, exempt = 0, taxable = 0, gst = 0, total = 0;
                foreach (var l in Lines)
                {
                    basic += l.Basic;
                    pnf += l.PnF;
                    freight += l.Freight;
                    if (l.GstP > 0) { taxable += l.Taxable; gst += l.Gst; }
                    else exempt += l.Taxable;
                    total += l.Total;
                }
                var (ig, cg, sg) = SplitGst(gst, Igst);
                var home = Currency == HomeCurrency;
                var roundOff = home
                    ? Math.Round(total, 0, MidpointRounding.AwayFromZero) - Math.Round(total, 2, MidpointRounding.AwayFromZero)
                    : 0;
                var netExact = total + roundOff;
                var net = home ? Math.Round(netExact, 0, MidpointRounding.AwayFromZero) : R2(netExact);
                return new OrderTotals(R2(basic), R2(exempt), R2(taxable), R2(pnf), R2(freight),
                    ig, cg, sg, R2(roundOff), net, netExact);
            }
        }

        private sealed class LinePlan
        {
            public int RowNo;
            public string Action = "Error";
            public decimal Qty, Rate, DiscP, PnFP, Freight, GstP;
            public DateTime? CommittedDate;
            public string Priority = "Normal";
            public string CustomerMaterial = "N";
            public Func<int, SP_OADtl>? Build;
            public OAImportResult Result = null!;

            // OrderAcceptance.html 4.14-4.19
            public decimal Basic => Qty * Rate;
            public decimal Disc => Basic * DiscP / 100m;
            public decimal PnF => (Basic - Disc) * PnFP / 100m;
            public decimal Taxable => Basic - Disc + PnF + Freight;
            public decimal Gst => Taxable * GstP / 100m;
            public decimal Total => Taxable + Gst;
        }

        private sealed record Term(string Title, string Description);

        /// <summary>Everything the checks compare against, read once per request.</summary>
        private sealed class LookupCache
        {
            public Dictionary<string, CustomerMaster> Customers = new();          // KEY(code)
            public Dictionary<string, PartMaster> Parts = new();                  // KEY(code)
            public Dictionary<int, Dictionary<string, string>> Grades = new();    // PartNo -> KEY(grade) -> stored
            public Dictionary<string, string> Series = new();                     // KEY -> stored
            public HashSet<string> Currencies = new();                            // upper code
            public Dictionary<string, string> OAByRef = new();                    // KEY(OtherRefNo) -> OA No
            public Dictionary<string, string> OAByPO = new();                     // VendorNo|KEY(PO)|yyyyMMdd -> OA No
            public List<Term> Terms = new();
            public string? UserType;

            public static async Task<LookupCache> LoadAsync(EMSContext db, int subscID, int userNo, bool terms)
            {
                var c = new LookupCache();

                foreach (var x in await db.CustomerMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.CustomerCode != null)
                             .OrderByDescending(x => x.LockStatus == "Y").ThenBy(x => x.MTransNo)
                             .ToListAsync())
                    c.Customers.TryAdd(Key(x.CustomerCode!), x);

                foreach (var x in await db.PartMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.PartCode != null)
                             .OrderByDescending(x => x.LockStatus == "Y").ThenBy(x => x.MTransNo)
                             .ToListAsync())
                    c.Parts.TryAdd(Key(x.PartCode!), x);

                // The grades the OA page's Grade dropdown offers (MaterialGrade GetDropdown).
                foreach (var g in await db.MaterialGrade.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" && x.Grade != null)
                             .Select(x => new { x.PartNo, x.Grade }).ToListAsync())
                {
                    if (!c.Grades.TryGetValue(g.PartNo, out var d)) c.Grades[g.PartNo] = d = new();
                    d.TryAdd(Key(g.Grade!), g.Grade!.Trim());
                }

                foreach (var s in await db.OtherMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" &&
                                         x.MasterType == SeriesType && x.MasterName != null)
                             .Select(x => x.MasterName!).ToListAsync())
                    c.Series.TryAdd(Key(s), s.Trim());

                foreach (var cur in await db.CurrencyMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.LockStatus == "Y" && x.CurrencyCode != null)
                             .Select(x => x.CurrencyCode!).ToListAsync())
                    c.Currencies.Add(cur.Trim().ToUpperInvariant());

                // Live, current-revision OAs: the duplicate checks.
                foreach (var o in await db.OAMst.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.DocAmdStatus == "N")
                             .Select(x => new { x.DocSeries, x.DocNo, x.OtherRefNo, x.VendorNo, x.RefNo, x.RefDate })
                             .ToListAsync())
                {
                    var no = OaNo(o.DocSeries, o.DocNo);
                    if (!string.IsNullOrWhiteSpace(o.OtherRefNo))
                        c.OAByRef.TryAdd(Key(o.OtherRefNo), no);
                    if (o.VendorNo != null && !string.IsNullOrWhiteSpace(o.RefNo) && o.RefDate != null)
                        c.OAByPO.TryAdd(o.VendorNo + "|" + Key(o.RefNo) + "|" + o.RefDate.Value.ToString("yyyyMMdd"), no);
                }

                if (terms)
                    c.Terms = (await db.StandardTerms.AsNoTracking()
                            .Where(x => x.SubscID == subscID && x.TermType == "CUSTOMER" &&
                                        x.DeleteStatus == "N" && x.LockStatus == "Y")
                            .OrderBy(x => x.TermSlNo)
                            .Select(x => new { x.TermTitle, x.TermDescription })
                            .ToListAsync())
                        .Where(x => !string.IsNullOrWhiteSpace(x.TermTitle) || !string.IsNullOrWhiteSpace(x.TermDescription))
                        .Select(x => new Term((x.TermTitle ?? "").Trim(), (x.TermDescription ?? "").Trim()))
                        .ToList();

                c.UserType = await db.UserMaster.AsNoTracking()
                    .Where(x => x.MTransNo == userNo)
                    .Select(x => x.UserType).FirstOrDefaultAsync();

                return c;
            }
        }

        private sealed class ImportPlan
        {
            public OAImportRequest Request { get; }
            public LookupCache Lookups { get; }

            public List<CustomerImportIssue> Issues { get; } = new();
            public List<OrderPlan> Orders { get; } = new();
            public Dictionary<string, OrderPlan> OrdersInFile { get; } = new();
            public Dictionary<string, int> PoInFile { get; } = new();
            public List<LinePlan> Lines { get; } = new();

            public int ErrorCount => Issues.Count(i => i.Level == "Error");

            public ImportPlan(OAImportRequest req, LookupCache lookups)
            {
                Request = req;
                Lookups = lookups;
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

            public DateTime? ParseDate(string sheet, int row, string field, string? text, bool required)
            {
                var v = Clean(text);
                if (v == null)
                {
                    if (required) Error(sheet, row, field, $"{field} is required.");
                    return null;
                }
                if (TryDate(v, out var d)) return d;
                Error(sheet, row, field, $"'{v}' is not a date. Use DD-MM-YYYY (or an Excel date).");
                return null;
            }

            public decimal? ParseNumber(string sheet, int row, string field, string? text, int scale,
                decimal min, decimal max, bool required = false, bool positive = false)
            {
                var v = Clean(text);
                if (v == null)
                {
                    if (required) Error(sheet, row, field, $"{field} is required.");
                    return null;
                }
                var t = v.TrimEnd('%').Trim();
                if (TryDec(t, out var d))
                {
                    d = Math.Round(d, scale, MidpointRounding.AwayFromZero);
                    if (positive && d <= 0)
                    {
                        Error(sheet, row, field, $"{field} must be greater than 0.");
                        return null;
                    }
                    if (d >= min && d <= max) return d;
                }
                Error(sheet, row, field, $"'{v}' must be a number from {min} to {max:0.##}.");
                return null;
            }

            /// <summary>Y / N (also Yes / No, 1 / 0, True / False, and the field's own word for Y / N).</summary>
            public string? ParseYesNo(string sheet, int row, string field, string? text,
                string? yesWord = null, string? noWord = null)
            {
                var v = Clean(text);
                if (v == null) return null;
                var u = v.ToUpperInvariant();
                if (u is "Y" or "YES" or "1" or "TRUE" || (yesWord != null && u == yesWord)) return "Y";
                if (u is "N" or "NO" or "0" or "FALSE" || (noWord != null && u == noWord)) return "N";
                Error(sheet, row, field, $"'{v}' is not valid. Use Y or N.");
                return null;
            }

            public OAImportReport BuildReport(bool dryRun)
            {
                var results = Orders.Select(o => o.Result).Concat(Lines.Select(l => l.Result)).ToList();

                CustomerImportCount Count(string sheet, int rows) => new()
                {
                    Sheet = sheet,
                    Rows = rows,
                    New = results.Count(r => r.Sheet == sheet && r.Action == "New"),
                    Update = 0,
                    Skip = results.Count(r => r.Sheet == sheet && r.Action == "Skip"),
                    Errors = results.Count(r => r.Sheet == sheet && r.Action == "Error")
                };

                var errors = ErrorCount;
                var work = Orders.Count(o => o.Action == "New");

                if (errors == 0 && work == 0)
                    Issues.Add(new CustomerImportIssue
                    {
                        Sheet = "Import", Level = "Warning", Field = "",
                        Message = "Every order in the file is already in the system and would be skipped."
                    });

                return new OAImportReport
                {
                    DryRun = dryRun,
                    CanImport = errors == 0 && work > 0,
                    ErrorCount = errors,
                    WarningCount = Issues.Count(i => i.Level == "Warning"),
                    ApprovalMode = Request.ApprovalMode,
                    Summary = new List<CustomerImportCount>
                    {
                        Count(SheetOrders, Request.Orders.Count),
                        Count(SheetLines, Request.Lines.Count)
                    },
                    Issues = Issues.OrderBy(i => i.Level == "Error" ? 0 : 1)
                                   .ThenBy(i => i.Sheet == SheetOrders ? 0 : 1).ThenBy(i => i.RowNo).ToList(),
                    Results = results
                };
            }
        }
    }
}
