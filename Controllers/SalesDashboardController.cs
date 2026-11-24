using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Sales Dashboard — EMSSoft/Modules/Sales/SalesDashboard.html.
    ///
    /// One read-only endpoint that returns every dataset the page draws, in the
    /// same shapes as the page's SECTION 3 variables, so the page only swaps its
    /// data source and keeps all of its own calculations.
    ///
    ///     GET api/SalesDashboard/GetDashboard/{subscID}?asOfDate=YYYY-MM-DD
    ///
    /// Period: the financial year (1 April) that contains asOfDate, up to asOfDate.
    /// asOfDate defaults to today's server date.
    ///
    /// Rules — taken from the existing screens, reports and procedures:
    ///   - Live = DeleteStatus 'N'. Enquiry / Quotation / OA read only the
    ///     current revision (DocAmdStatus 'N')            (SalesReportsController)
    ///   - Quotation / OA LockStatus: N draft, S submitted, Y approved,
    ///     R rejected                                      (SQL/34_SalesApproval.sql)
    ///   - Order intake and the order book count APPROVED OAs (LockStatus 'Y').
    ///     Qty invoiced against an OA line is matched across the OA's revisions
    ///     (an amendment is a new row with new line ids; invoices keep the old ones).
    ///   - Conversions are linked, not count ratios: lead → enquiry (EnqMst.DrvNo),
    ///     enquiry → quotation and quotation → OA (TargetTransNo, any revision).
    ///   - Open quotation and its follow-up state: same rule as the Follow-Up
    ///     screen                                          (QtnFollowUpController)
    ///   - Invoiced qty against an OA line: STxnDtl.LinkNo = OADtl.TransSlNo
    ///     of that line in whichever revision the invoice was raised against.
    ///   - Lead / Enquiry still open: DocStatus is not 'Y' (taken forward).
    ///   - Quotation validity = QtnMst.TargetDate (the "Validity Date" field).
    ///   - Export invoice = STxnMst.InvType 'Export'.
    ///   - Sales invoices and credit notes have no draft step: both screens save
    ///     LockStatus 'Y', so their "draft" counts are LockStatus other than 'Y'.
    ///   - Money is in INR: NetValue (and the tax columns) are stored in the
    ///     document currency, so every amount is multiplied by CurrencyRate
    ///     (a missing / zero rate counts as 1).
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    ///
    /// No sales target exists in the database (SalesTeam carries names only), so
    /// executive targets are returned as null.
    /// </summary>
    [Route("api/SalesDashboard")]
    public class SalesDashboardController : EmsControllerBase
    {
        private readonly EMSContext db;
        private readonly DashboardCache cache;

        public SalesDashboardController(EMSContext context, DashboardCache cache)
        {
            db = context;
            this.cache = cache;
        }

        // Days the page's alert / "next 5 days" view covers (ALERT_WINDOW_DAYS).
        private const int AgendaWindowDays = 5;

        // Key customers shown in the monthly table.
        private const int KeyCustomers = 12;

        // Daily view and order lists (client's daily dashboard): a list carries at most
        // ListLimit rows plus its full count; a chart its top ChartLimit plus one "Others".
        private const int ListLimit = 50;
        private const int ChartLimit = 10;
        private const int RecentOrderDays = 7;

        // INR value of a document amount.
        private const string Rate = "ISNULL(NULLIF(m.CurrencyRate, 0), 1)";

        // Qty invoiced against OA line d of OA m, across every revision of that OA:
        // an amendment is a new OAMst row with new line ids, and an invoice keeps
        // the line id (STxnDtl.LinkNo) of the revision it was raised against.
        private const string InvoicedQty = @"
                        SELECT SUM(sd.Qty) AS Qty
                        FROM dbo.STxnDtl sd
                        JOIN dbo.STxnMst sm ON sm.MTransNo = sd.MTransNo AND sm.DeleteStatus = 'N'
                        JOIN dbo.OADtl od ON od.TransSlNo = sd.LinkNo
                        JOIN dbo.OAMst om ON om.MTransNo = od.MTransNo
                        WHERE sd.DeleteStatus = 'N'
                          AND om.SubscID = m.SubscID AND om.DocSeries = m.DocSeries
                          AND om.DocYear = m.DocYear AND om.DocNo = m.DocNo
                          AND od.SlNo = d.SlNo";

        // Every revision of the quotation "c" (same document), as QtnFollowUpController.
        private const string SameDocument = @"
                      r.SubscID   = c.SubscID
                  AND r.DocSeries = c.DocSeries
                  AND r.DocYear   = c.DocYear
                  AND r.DocNo     = c.DocNo";

        // Cached for a few minutes (Models/DashboardSupport.cs); refresh=true rebuilds.
        [HttpGet("GetDashboard/{subscID}")]
        public async Task<IActionResult> GetDashboard(int subscID, [FromQuery] DateTime? asOfDate = null, [FromQuery] bool refresh = false)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var asOf = (asOfDate ?? DateTime.Today).Date;
            try
            {
                var data = await cache.GetOrBuild("Sales", DashboardCache.KeyOf(subscID, Dash.Iso(asOf)), refresh,
                    async _ => DashboardCache.FromObject(await BuildDashboard(subscID, asOf)));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        private async Task<object> BuildDashboard(int subscID, DateTime asOf)
        {
            var fyStart = new DateTime(asOf.Month >= 4 ? asOf.Year : asOf.Year - 1, 4, 1);
            var windowEnd = asOf.AddDays(AgendaWindowDays - 1);

            SqlParameter[] P() => new[]
            {
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = fyStart },
                new SqlParameter("@AsOf", SqlDbType.Date) { Value = asOf },
                new SqlParameter("@WindowEnd", SqlDbType.Date) { Value = windowEnd }
            };

            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open)
                    await con.OpenAsync();

                // ---------------------------------------------------------
                // Months of the period: April .. the as-of month (MTD).
                // ---------------------------------------------------------
                var months = new List<DateTime>();
                for (var mo = fyStart; mo <= asOf; mo = mo.AddMonths(1))
                    months.Add(mo);
                var labels = months.Select((mo, i) =>
                    mo.ToString("MMM-yy", CultureInfo.InvariantCulture) + (i == months.Count - 1 ? " (MTD)" : ""))
                    .ToList();
                int MonthIndex(object? d) =>
                    d is DateTime dt ? (dt.Year - fyStart.Year) * 12 + dt.Month - fyStart.Month : -1;

                // ---------------------------------------------------------
                // 3.2 Monthly trend — invoiced vs order intake (INR)
                // ---------------------------------------------------------
                var invoicedRows = await Rows(con, @"
                    SELECT DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1) AS mon,
                           SUM(ISNULL(m.NetValue, 0) * " + Rate + @") AS val
                    FROM dbo.STxnMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P());

                var intakeRows = await Rows(con, @"
                    SELECT DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1) AS mon,
                           SUM(ISNULL(m.NetValue, 0) * " + Rate + @") AS val
                    FROM dbo.OAMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P());

                var invoiced = new decimal[months.Count];
                var orderIntake = new decimal[months.Count];
                foreach (var r in invoicedRows) { var i = MonthIndex(r["mon"]); if (i >= 0 && i < months.Count) invoiced[i] = Round(Dec(r, "val")); }
                foreach (var r in intakeRows) { var i = MonthIndex(r["mon"]); if (i >= 0 && i < months.Count) orderIntake[i] = Round(Dec(r, "val")); }

                // Order book at each month-end: approved, not short-closed OA lines
                // dated on or before that day, less what was invoiced against them by
                // then (across revisions). Element 0 is the day before the year starts
                // (brought forward); then one per month, the last at asOfDate.
                // A short-closed OA (DocStatus 'Y') carries no closing date, so it is
                // left out of every month-end.
                var monthEnds = new List<DateTime> { fyStart.AddDays(-1) };
                monthEnds.AddRange(months.Select(mo => mo.AddMonths(1).AddDays(-1) < asOf ? mo.AddMonths(1).AddDays(-1) : asOf));
                var monthEndValues = string.Join(", ", monthEnds.Select(d => "('" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "')"));
                var bookRows = await Rows(con, @"
                    WITH me AS (SELECT CAST(v AS date) AS E FROM (VALUES " + monthEndValues + @") t(v)),
                    ln AS (
                        SELECT m.DocDate, m.SubscID, m.DocSeries, m.DocYear, m.DocNo, d.SlNo,
                               ISNULL(d.Qty, 0) AS Qty,
                               CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.Qty ELSE 0 END * " + Rate + @" AS UnitValue
                        FROM dbo.OAMst m
                        JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y' AND ISNULL(m.DocStatus, 'N') <> 'Y'
                          AND m.DocDate <= @AsOf
                    )
                    SELECT me.E AS monthEnd,
                           SUM(CASE WHEN ln.Qty - ISNULL(inv.Qty, 0) > 0
                                    THEN (ln.Qty - ISNULL(inv.Qty, 0)) * ln.UnitValue ELSE 0 END) AS val
                    FROM me
                    JOIN ln ON ln.DocDate <= me.E
                    OUTER APPLY (
                        SELECT SUM(sd.Qty) AS Qty
                        FROM dbo.STxnDtl sd
                        JOIN dbo.STxnMst sm ON sm.MTransNo = sd.MTransNo AND sm.DeleteStatus = 'N'
                        JOIN dbo.OADtl od ON od.TransSlNo = sd.LinkNo
                        JOIN dbo.OAMst om ON om.MTransNo = od.MTransNo
                        WHERE sd.DeleteStatus = 'N' AND sm.DocDate <= me.E
                          AND om.SubscID = ln.SubscID AND om.DocSeries = ln.DocSeries
                          AND om.DocYear = ln.DocYear AND om.DocNo = ln.DocNo
                          AND od.SlNo = ln.SlNo
                    ) inv
                    GROUP BY me.E", P());
                var bookByEnd = bookRows.ToDictionary(r => (DateTime)r["monthEnd"]!, r => Round(Dec(r, "val")));
                var bookSeries = monthEnds.Select(d => bookByEnd.TryGetValue(d, out var v) ? v : 0m).ToList();
                var openingOrderBook = bookSeries[0];
                var orderBookSeries = bookSeries.Skip(1).ToArray();

                // Linked conversions of this year's documents (each through the
                // link the next document stores): lead → enquiry (EnqMst.DrvNo),
                // enquiry → quotation (QtnMst.TargetTransNo), quotation → approved OA
                // (OAMst.TargetTransNo); any revision of the target counts.
                var conv = (await Rows(con, @"
                    WITH lead AS (
                        SELECT m.MTransNo FROM dbo.LeadGeneration m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ),
                    enq AS (
                        SELECT m.SubscID, m.DocSeries, m.DocYear, m.DocNo, m.DrvNo FROM dbo.EnqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ),
                    qtn AS (
                        SELECT m.SubscID, m.DocSeries, m.DocYear, m.DocNo FROM dbo.QtnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    )
                    SELECT
                      (SELECT COUNT(*) FROM lead l
                        WHERE EXISTS (SELECT 1 FROM dbo.EnqMst e
                                       WHERE e.DrvNo = l.MTransNo AND e.SubscID = @SubscID AND e.DeleteStatus = 'N')) AS leadsConverted,
                      (SELECT COUNT(*) FROM enq c
                        WHERE EXISTS (SELECT 1 FROM dbo.QtnMst q JOIN dbo.EnqMst r ON r.MTransNo = q.TargetTransNo
                                       WHERE q.SubscID = c.SubscID AND q.DeleteStatus = 'N'
                                         AND r.SubscID = c.SubscID AND r.DocSeries = c.DocSeries
                                         AND r.DocYear = c.DocYear AND r.DocNo = c.DocNo))                       AS enquiriesConverted,
                      (SELECT COUNT(*) FROM qtn c
                        WHERE EXISTS (SELECT 1 FROM dbo.OAMst o JOIN dbo.QtnMst r ON r.MTransNo = o.TargetTransNo
                                       WHERE o.SubscID = c.SubscID AND o.DeleteStatus = 'N' AND o.DocAmdStatus = 'N'
                                         AND o.LockStatus = 'Y'
                                         AND r.SubscID = c.SubscID AND r.DocSeries = c.DocSeries
                                         AND r.DocYear = c.DocYear AND r.DocNo = c.DocNo))                       AS quotationsConverted,
                      (SELECT COUNT(*) FROM lead l
                        WHERE EXISTS (SELECT 1 FROM dbo.EnqMst e
                                        JOIN dbo.EnqMst er ON er.SubscID = e.SubscID AND er.DocSeries = e.DocSeries
                                                          AND er.DocYear = e.DocYear AND er.DocNo = e.DocNo
                                        JOIN dbo.QtnMst q  ON q.TargetTransNo = er.MTransNo AND q.DeleteStatus = 'N'
                                        JOIN dbo.QtnMst qr ON qr.SubscID = q.SubscID AND qr.DocSeries = q.DocSeries
                                                          AND qr.DocYear = q.DocYear AND qr.DocNo = q.DocNo
                                        JOIN dbo.OAMst o   ON o.TargetTransNo = qr.MTransNo AND o.DeleteStatus = 'N'
                                                          AND o.DocAmdStatus = 'N' AND o.LockStatus = 'Y'
                                       WHERE e.DrvNo = l.MTransNo AND e.SubscID = @SubscID AND e.DeleteStatus = 'N')) AS leadsToOrder", P())).First();

                // ---------------------------------------------------------
                // 3.3 Pipeline counts — documents raised this year
                // ---------------------------------------------------------
                var counts = (await Rows(con, @"
                    SELECT
                      (SELECT COUNT(*) FROM dbo.LeadGeneration m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS leads,
                      (SELECT COUNT(*) FROM dbo.EnqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS enquiries,
                      (SELECT COUNT(*) FROM dbo.QtnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS quotations,
                      (SELECT COUNT(*) FROM dbo.OAMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS orderAcceptances,
                      (SELECT COUNT(*) FROM dbo.STxnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS invoices,
                      (SELECT COUNT(*) FROM dbo.SalesReturnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                       AS salesReturns", P())).First();

                // ---------------------------------------------------------
                // 3.4 Quotation status of this year's quotations
                // ---------------------------------------------------------
                var qStatus = (await Rows(con, @"
                    SELECT
                      SUM(CASE WHEN m.LockStatus = 'Y' THEN 1 ELSE 0 END) AS approved,
                      SUM(CASE WHEN m.LockStatus = 'S' THEN 1 ELSE 0 END) AS submitted,
                      SUM(CASE WHEN m.LockStatus = 'R' THEN 1 ELSE 0 END) AS rejected,
                      SUM(CASE WHEN ISNULL(m.LockStatus, 'N') NOT IN ('Y', 'S', 'R') THEN 1 ELSE 0 END) AS draft
                    FROM dbo.QtnMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();

                // ---------------------------------------------------------
                // Open quotations (any date) with their follow-up roll-up —
                // feeds 3.5, 3.6, 3.10 and the validity items of 3.12.
                // ---------------------------------------------------------
                var openQtn = await Rows(con, @"
                    SELECT
                        c.DocSeries                             AS docSeries,
                        c.DocNo                                 AS docNo,
                        c.DocAmdNo                              AS docAmdNo,
                        c.VendorName                            AS customer,
                        c.RefProject                            AS project,
                        c.SalesExec                             AS salesExec,
                        ISNULL(c.NetValue, 0) * ISNULL(NULLIF(c.CurrencyRate, 0), 1) AS netValue,
                        DATEDIFF(day, c.DocDate, @AsOf)         AS ageDays,
                        CONVERT(char(10), c.TargetDate, 23)     AS validityDate,
                        ISNULL(s.FUCount, 0)                    AS fuCount,
                        CONVERT(char(10), n.NextVisitDate, 23)  AS nextFollowUp,
                        CASE
                            WHEN ISNULL(s.FUCount, 0) = 0 THEN 'Not Started'
                            WHEN n.NextVisitDate IS NULL  THEN 'No Next Date'
                            WHEN n.NextVisitDate < @AsOf  THEN 'Overdue'
                            WHEN n.NextVisitDate = @AsOf  THEN 'Due Today'
                            ELSE 'Scheduled'
                        END                                     AS status
                    FROM dbo.QtnMst c
                    OUTER APPLY (
                        SELECT COUNT(*) AS FUCount
                          FROM dbo.QtnFollowUp f
                          JOIN dbo.QtnMst r ON r.MTransNo = f.MTransNo
                         WHERE f.DeleteStatus = 'N' AND " + SameDocument + @"
                    ) s
                    OUTER APPLY (
                        -- The latest entry's next date; an older one's is superseded.
                        SELECT TOP (1) f.NextVisitDate
                          FROM dbo.QtnFollowUp f
                          JOIN dbo.QtnMst r ON r.MTransNo = f.MTransNo
                         WHERE f.DeleteStatus = 'N' AND " + SameDocument + @"
                         ORDER BY f.FollowupDate DESC, f.TransSlNo DESC
                    ) n
                    WHERE c.SubscID      = @SubscID
                      AND c.DeleteStatus = 'N'
                      AND c.DocAmdStatus = 'N'
                      AND c.DocStatus    = 'N'
                      AND c.DocDate     <= @AsOf
                      AND NOT EXISTS (
                            SELECT 1
                              FROM dbo.OAMst o
                              JOIN dbo.QtnMst r ON r.MTransNo = o.TargetTransNo
                             WHERE o.SubscID = c.SubscID AND o.DeleteStatus = 'N' AND " + SameDocument + @")
                    ORDER BY c.MTransNo DESC", P());

                var followUpStatus = new[] { "Overdue", "Due Today", "Scheduled", "Not Started", "No Next Date" }
                    .Select(label => new { label, value = openQtn.Count(r => Str(r, "status") == label) })
                    .ToList();

                var quotationAgeing = new[]
                {
                    new { label = "0–15 days",  value = openQtn.Count(r => Int(r, "ageDays") <= 15) },
                    new { label = "16–30 days", value = openQtn.Count(r => Int(r, "ageDays") is > 15 and <= 30) },
                    new { label = "31–45 days", value = openQtn.Count(r => Int(r, "ageDays") is > 30 and <= 45) },
                    new { label = "45+ days",   value = openQtn.Count(r => Int(r, "ageDays") > 45) }
                };

                var followUpsDue = openQtn.Select(r => new
                {
                    docSeries = Str(r, "docSeries"),
                    docNo = Int(r, "docNo"),
                    docAmdNo = Int(r, "docAmdNo"),
                    customer = Str(r, "customer"),
                    project = Str(r, "project"),
                    salesExec = Str(r, "salesExec"),
                    netValue = Round(Dec(r, "netValue")),
                    ageDays = Int(r, "ageDays"),
                    nextFollowUp = Str(r, "nextFollowUp"),
                    status = Str(r, "status")
                }).ToList();

                // ---------------------------------------------------------
                // 3.7a Customer-wise monthly sales — key customers
                // ---------------------------------------------------------
                var custRows = await Rows(con, @"
                    SELECT m.VendorNo                                                   AS vendorNo,
                           MAX(COALESCE(NULLIF(RTRIM(cm.CustomerName), ''), m.VendorName)) AS customer,
                           DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)          AS mon,
                           SUM(ISNULL(m.NetValue, 0) * " + Rate + @")                   AS val
                    FROM dbo.STxnMst m
                    LEFT JOIN dbo.CustomerMaster cm ON cm.MTransNo = m.VendorNo
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY m.VendorNo, DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P());

                var customerMonthlySales = custRows
                    .GroupBy(r => Int(r, "vendorNo"))
                    .Select(g =>
                    {
                        var monthly = new decimal[months.Count];
                        foreach (var r in g) { var i = MonthIndex(r["mon"]); if (i >= 0 && i < months.Count) monthly[i] += Dec(r, "val"); }
                        return new
                        {
                            customer = Str(g.First(), "customer"),
                            monthly = monthly.Select(Round).ToArray()
                        };
                    })
                    .OrderByDescending(c => c.monthly.Sum())
                    .Take(KeyCustomers)
                    .ToList();

                // ---------------------------------------------------------
                // 3.8 Sales executives — activity this year (no targets stored)
                // ---------------------------------------------------------
                var execRows = await Rows(con, @"
                    SELECT executive,
                           SUM(enq) AS enquiries, SUM(qtn) AS quotations, SUM(ord) AS orders, SUM(sales) AS salesValue
                    FROM (
                        SELECT RTRIM(m.SalesExec) AS executive, 1 AS enq, 0 AS qtn, 0 AS ord, CAST(0 AS numeric(18, 2)) AS sales
                          FROM dbo.EnqMst m
                         WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                           AND m.DocDate BETWEEN @FromDate AND @AsOf
                        UNION ALL
                        SELECT RTRIM(m.SalesExec), 0, 1, 0, 0
                          FROM dbo.QtnMst m
                         WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                           AND m.DocDate BETWEEN @FromDate AND @AsOf
                        UNION ALL
                        SELECT RTRIM(m.SalesExec), 0, 0, 1, 0
                          FROM dbo.OAMst m
                         WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                           AND m.LockStatus = 'Y' AND m.DocDate BETWEEN @FromDate AND @AsOf
                        UNION ALL
                        SELECT RTRIM(m.SalesRep), 0, 0, 0, ISNULL(m.NetValue, 0) * " + Rate + @"
                          FROM dbo.STxnMst m
                         WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                           AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ) x
                    WHERE ISNULL(executive, '') <> ''
                    GROUP BY executive
                    ORDER BY salesValue DESC", P());
                var executivePerformance = execRows.Select(r => new
                {
                    executive = Str(r, "executive"),
                    region = "",
                    enquiries = Int(r, "enquiries"),
                    quotations = Int(r, "quotations"),
                    orders = Int(r, "orders"),
                    salesValue = Round(Dec(r, "salesValue")),
                    target = (decimal?)null
                }).ToList();

                // ---------------------------------------------------------
                // 3.9 GST summary this year (INR)
                // ---------------------------------------------------------
                var gst = (await Rows(con, @"
                    SELECT
                      SUM(ISNULL(m.TaxableValue, 0) * " + Rate + @")                                   AS taxableValue,
                      SUM(CASE WHEN m.InvType = 'Export' THEN ISNULL(m.TaxableValue, 0) * " + Rate + @" ELSE 0 END) AS exportLutValue,
                      SUM(ISNULL(m.CGSTAmt, 0) * " + Rate + @")                                        AS cgst,
                      SUM(ISNULL(m.SGSTAmt, 0) * " + Rate + @")                                        AS sgst,
                      SUM(ISNULL(m.IGSTAmt, 0) * " + Rate + @")                                        AS igst,
                      (SELECT SUM(ISNULL(m.NetValue, 0) * " + Rate + @")
                         FROM dbo.SalesReturnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                                   AS creditNoteValue
                    FROM dbo.STxnMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();

                // ---------------------------------------------------------
                // 3.11 Pending actions (open now, any date)
                // ---------------------------------------------------------
                var pend = (await Rows(con, @"
                    SELECT
                      (SELECT COUNT(*) FROM dbo.QtnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'S')                                AS qtnApproval,
                      (SELECT COUNT(*) FROM dbo.OAMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'S')                                AS oaApproval,
                      (SELECT COUNT(*) FROM dbo.PackingSlipMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND ISNULL(m.LockStatus, 'N') = 'N')                   AS packingDraft,
                      (SELECT COUNT(*) FROM dbo.STxnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND ISNULL(m.LockStatus, 'N') <> 'Y')                  AS invoiceDraft,
                      (SELECT COUNT(*) FROM dbo.SalesReturnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND ISNULL(m.LockStatus, 'N') <> 'Y')                  AS returnPending", P())).First();

                var pendingActions = new[]
                {
                    new { label = "Quotations pending approval", count = Int(pend, "qtnApproval"),  href = "QuotationApproval.html", icon = "bi bi-file-earmark-check" },
                    new { label = "OAs pending approval",        count = Int(pend, "oaApproval"),   href = "OAApproval.html",        icon = "bi bi-journal-check" },
                    new { label = "Overdue follow-ups",          count = followUpStatus[0].value,   href = "FollowUp.html",          icon = "bi bi-alarm" },
                    new { label = "Packing slips in draft",      count = Int(pend, "packingDraft"), href = "PackingSlip.html",       icon = "bi bi-box-seam" },
                    new { label = "Sales invoices in draft",     count = Int(pend, "invoiceDraft"), href = "SalesInvoice.html",      icon = "bi bi-receipt" },
                    new { label = "Returns pending approval",    count = Int(pend, "returnPending"), href = "SalesReturns.html",     icon = "bi bi-arrow-return-left" }
                };

                // ---------------------------------------------------------
                // 3.12 Dated pending items — from the start of the year up to the
                // end of the 5-day window (older ones belong in the reports).
                // ---------------------------------------------------------
                var dated = new List<object>();

                foreach (var r in openQtn.Where(r => InWindow(Str(r, "validityDate"), fyStart, windowEnd)))
                    dated.Add(new
                    {
                        type = "validity", docSeries = Str(r, "docSeries"), docNo = Int(r, "docNo"), docAmdNo = Int(r, "docAmdNo"),
                        customer = Str(r, "customer"), detail = Str(r, "project"), owner = Str(r, "salesExec"),
                        value = (decimal?)Round(Dec(r, "netValue")), date = Str(r, "validityDate")
                    });

                var oaLines = await Rows(con, @"
                    SELECT m.DocSeries AS docSeries, m.DocNo AS docNo, m.DocAmdNo AS docAmdNo,
                           m.VendorName AS customer, m.SalesExec AS owner,
                           ISNULL(d.PartDescription, '') AS part,
                           ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0) AS pendingQty,
                           ISNULL(d.Unit, '') AS unit,
                           CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.Qty ELSE 0 END
                             * (ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0)) * " + Rate + @" AS value,
                           CONVERT(char(10), d.CommittedDate, 23) AS date
                    FROM dbo.OAMst m
                    JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (" + InvoicedQty + @") inv
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y' AND ISNULL(m.DocStatus, 'N') <> 'Y'
                      AND ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0) > 0
                      AND d.CommittedDate BETWEEN @FromDate AND @WindowEnd", P());
                foreach (var r in oaLines)
                    dated.Add(new
                    {
                        type = "oaDelivery", docSeries = Str(r, "docSeries"), docNo = Int(r, "docNo"), docAmdNo = Int(r, "docAmdNo"),
                        customer = Str(r, "customer"),
                        detail = Str(r, "part") + " — " + Dec(r, "pendingQty").ToString("0.####", CultureInfo.InvariantCulture) + " " + Str(r, "unit"),
                        owner = Str(r, "owner"), value = (decimal?)Round(Dec(r, "value")), date = Str(r, "date")
                    });

                var crm = await Rows(con, @"
                    SELECT 'lead' AS type, m.DocSeries AS docSeries, m.DocNo AS docNo, 0 AS docAmdNo,
                           m.VendorName AS customer, LEFT(ISNULL(m.Product, ''), 80) AS detail,
                           m.SalesExecutive AS owner, CONVERT(char(10), m.Followupdate, 23) AS date
                      FROM dbo.LeadGeneration m
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                       AND ISNULL(m.DocStatus, 'N') <> 'Y'
                       AND m.Followupdate BETWEEN @FromDate AND @WindowEnd
                    UNION ALL
                    SELECT 'enquiry', m.DocSeries, m.DocNo, m.DocAmdNo,
                           m.VendorName, COALESCE(NULLIF(m.RefProject, ''), m.DocNotes, ''),
                           m.SalesExec, CONVERT(char(10), m.Nextfollowupdate, 23)
                      FROM dbo.EnqMst m
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND ISNULL(m.DocStatus, 'N') <> 'Y'
                       AND m.Nextfollowupdate BETWEEN @FromDate AND @WindowEnd", P());
                foreach (var r in crm)
                    dated.Add(new
                    {
                        type = Str(r, "type"), docSeries = Str(r, "docSeries"), docNo = Int(r, "docNo"), docAmdNo = Int(r, "docAmdNo"),
                        customer = Str(r, "customer"), detail = Str(r, "detail"), owner = Str(r, "owner"),
                        value = (decimal?)null, date = Str(r, "date")
                    });

                // ---------------------------------------------------------
                // 3.16 Daily view (client's daily dashboard): yesterday's and
                // today's sales and orders booked, and this month so far against
                // the same days of last month. "Yesterday" is the calendar day
                // before asOfDate. Sales = invoices (STxnMst, same rule as 3.2);
                // orders booked = approved OAs by OA date (same rule as order intake).
                // ---------------------------------------------------------
                var yesterday = asOf.AddDays(-1);
                var monthStart = new DateTime(asOf.Year, asOf.Month, 1);
                var prevFrom = monthStart.AddMonths(-1);
                var prevTo = prevFrom.AddDays(asOf.Day - 1);
                if (prevTo >= monthStart) prevTo = monthStart.AddDays(-1);
                var recentFrom = asOf.AddDays(-(RecentOrderDays - 1));
                SqlParameter[] PD() => new[]
                {
                    new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                    new SqlParameter("@AsOf", SqlDbType.Date) { Value = asOf },
                    new SqlParameter("@Yesterday", SqlDbType.Date) { Value = yesterday },
                    new SqlParameter("@MonthStart", SqlDbType.Date) { Value = monthStart },
                    new SqlParameter("@PrevFrom", SqlDbType.Date) { Value = prevFrom },
                    new SqlParameter("@PrevTo", SqlDbType.Date) { Value = prevTo },
                    new SqlParameter("@RecentFrom", SqlDbType.Date) { Value = recentFrom }
                };
                var day = (await Rows(con, @"
                    WITH inv AS (
                        SELECT m.DocDate, ISNULL(m.NetValue, 0) * " + Rate + @" AS val
                        FROM dbo.STxnMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @PrevFrom AND @AsOf
                    ),
                    oa AS (
                        SELECT m.DocDate, ISNULL(m.NetValue, 0) * " + Rate + @" AS val
                        FROM dbo.OAMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y'
                          AND m.DocDate BETWEEN @PrevFrom AND @AsOf
                    )
                    SELECT
                      (SELECT SUM(val) FROM inv WHERE DocDate = @AsOf)                          AS todayInvoiced,
                      (SELECT COUNT(*) FROM inv WHERE DocDate = @AsOf)                          AS todayInvoices,
                      (SELECT SUM(val) FROM oa  WHERE DocDate = @AsOf)                          AS todayOrderValue,
                      (SELECT COUNT(*) FROM oa  WHERE DocDate = @AsOf)                          AS todayOrders,
                      (SELECT SUM(val) FROM inv WHERE DocDate = @Yesterday)                     AS yInvoiced,
                      (SELECT COUNT(*) FROM inv WHERE DocDate = @Yesterday)                     AS yInvoices,
                      (SELECT SUM(val) FROM oa  WHERE DocDate = @Yesterday)                     AS yOrderValue,
                      (SELECT COUNT(*) FROM oa  WHERE DocDate = @Yesterday)                     AS yOrders,
                      (SELECT SUM(val) FROM inv WHERE DocDate BETWEEN @MonthStart AND @AsOf)    AS mInvoiced,
                      (SELECT COUNT(*) FROM inv WHERE DocDate BETWEEN @MonthStart AND @AsOf)    AS mInvoices,
                      (SELECT SUM(val) FROM oa  WHERE DocDate BETWEEN @MonthStart AND @AsOf)    AS mOrderValue,
                      (SELECT COUNT(*) FROM oa  WHERE DocDate BETWEEN @MonthStart AND @AsOf)    AS mOrders,
                      (SELECT SUM(val) FROM inv WHERE DocDate BETWEEN @PrevFrom AND @PrevTo)    AS pInvoiced,
                      (SELECT SUM(val) FROM oa  WHERE DocDate BETWEEN @PrevFrom AND @PrevTo)    AS pOrderValue", PD())).First();

                // ---------------------------------------------------------
                // 3.17 Pending orders — approved, not short-closed OA lines with
                // quantity still to invoice (the order book rule above). Due date =
                // our committed date, else the customer's delivery date.
                // ---------------------------------------------------------
                var pendRows = await Rows(con, @"
                    SELECT m.DocSeries AS docSeries, m.DocNo AS docNo, m.DocAmdNo AS docAmdNo,
                           CONVERT(char(10), m.DocDate, 23) AS oaDate,
                           m.VendorName AS customer,
                           ISNULL(d.PartDescription, '') AS part,
                           ISNULL(d.Unit, '') AS unit,
                           ISNULL(d.Qty, 0) AS orderQty,
                           ISNULL(inv.Qty, 0) AS invoicedQty,
                           ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0) AS pendingQty,
                           CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.Qty ELSE 0 END
                             * (ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0)) * " + Rate + @" AS value,
                           CONVERT(char(10), COALESCE(d.CommittedDate, d.DeliveryDate, m.DueDate), 23) AS dueDate
                    FROM dbo.OAMst m
                    JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (" + InvoicedQty + @") inv
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y' AND ISNULL(m.DocStatus, 'N') <> 'Y'
                      AND m.DocDate <= @AsOf
                      AND ISNULL(d.Qty, 0) - ISNULL(inv.Qty, 0) > 0", PD());

                var pendingLines = pendRows.Select(r =>
                {
                    var due = Str(r, "dueDate");
                    var hasDue = DateTime.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dueDt);
                    var daysLate = hasDue && dueDt < asOf ? (asOf - dueDt).Days : 0;
                    return new
                    {
                        docSeries = Str(r, "docSeries"),
                        docNo = Int(r, "docNo"),
                        docAmdNo = Int(r, "docAmdNo"),
                        oaDate = Str(r, "oaDate"),
                        customer = Str(r, "customer"),
                        part = Str(r, "part"),
                        unit = Str(r, "unit"),
                        orderQty = Dec(r, "orderQty"),
                        invoicedQty = Dec(r, "invoicedQty"),
                        pendingQty = Dec(r, "pendingQty"),
                        value = Round(Dec(r, "value")),
                        dueDate = due,
                        daysLate,
                        status = !hasDue ? "No Date" : dueDt < asOf ? "Overdue" : dueDt <= asOf.AddDays(6) ? "Due This Week" : "On Time"
                    };
                })
                // Most urgent first: overdue (longest late first), then by due date; undated last.
                .OrderBy(l => l.dueDate == "" ? 1 : 0).ThenBy(l => l.dueDate).ThenByDescending(l => l.value)
                .ToList();

                var pendingByCustomer = pendingLines
                    .GroupBy(l => l.customer)
                    .Select(g => new { customer = g.Key, lines = g.Count(), value = Round(g.Sum(l => l.value)), isOther = false })
                    .OrderByDescending(c => c.value)
                    .ToList();
                if (pendingByCustomer.Count > ChartLimit)
                {
                    var rest = pendingByCustomer.Skip(ChartLimit).ToList();
                    pendingByCustomer = pendingByCustomer.Take(ChartLimit)
                        .Append(new { customer = "Others (" + rest.Count + ")", lines = rest.Sum(c => c.lines), value = Round(rest.Sum(c => c.value)), isOther = true })
                        .ToList();
                }

                var overdueLines = pendingLines.Where(l => l.status == "Overdue").ToList();
                var pendingOrders = new
                {
                    lines = pendingLines.Count,
                    orders = pendingLines.Select(l => l.docSeries + "|" + l.docNo).Distinct().Count(),
                    customers = pendingLines.Select(l => l.customer).Distinct().Count(),
                    value = Round(pendingLines.Sum(l => l.value)),
                    overdueLines = overdueLines.Count,
                    overdueValue = Round(overdueLines.Sum(l => l.value)),
                    dueThisWeekLines = pendingLines.Count(l => l.status == "Due This Week"),
                    listTotal = pendingLines.Count,
                    list = pendingLines.Take(ListLimit).ToList(),
                    byCustomer = pendingByCustomer
                };

                // ---------------------------------------------------------
                // 3.18 Orders booked recently — approved OAs dated in the last
                // RecentOrderDays days, newest first.
                // ---------------------------------------------------------
                var recentRows = await Rows(con, @"
                    SELECT m.DocSeries AS docSeries, m.DocNo AS docNo, m.DocAmdNo AS docAmdNo,
                           CONVERT(char(10), m.DocDate, 23) AS date,
                           m.VendorName AS customer, m.SalesExec AS salesExec,
                           ISNULL(m.NetValue, 0) * " + Rate + @" AS value,
                           (SELECT COUNT(*) FROM dbo.OADtl d WHERE d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N') AS lines
                    FROM dbo.OAMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y'
                      AND m.DocDate BETWEEN @RecentFrom AND @AsOf
                    ORDER BY m.DocDate DESC, m.DocNo DESC", PD());
                var recentOrders = new
                {
                    fromDate = recentFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    count = recentRows.Count,
                    value = Round(recentRows.Sum(r => Dec(r, "value"))),
                    list = recentRows.Take(ListLimit).Select(r => new
                    {
                        docSeries = Str(r, "docSeries"),
                        docNo = Int(r, "docNo"),
                        docAmdNo = Int(r, "docAmdNo"),
                        date = Str(r, "date"),
                        customer = Str(r, "customer"),
                        salesExec = Str(r, "salesExec"),
                        lines = Int(r, "lines"),
                        value = Round(Dec(r, "value"))
                    }).ToList()
                };

                // Fallback series for a reference without one: the latest quotation's.
                var docSeries = followUpsDue.Select(f => f.docSeries).FirstOrDefault(s => s != "") ?? "";

                var data = new
                {
                    period = new
                    {
                        fyLabel = $"FY {fyStart.Year}-{(fyStart.Year + 1) % 100:00}",
                        fromDate = fyStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    },
                    docSeries,
                    monthlyTrend = new { labels, invoiced, orderIntake },
                    openingOrderBook,
                    orderBookSeries,
                    conversions = new
                    {
                        leadsConverted = Int(conv, "leadsConverted"),
                        enquiriesConverted = Int(conv, "enquiriesConverted"),
                        quotationsConverted = Int(conv, "quotationsConverted"),
                        leadsToOrder = Int(conv, "leadsToOrder")
                    },
                    pipelineCounts = new
                    {
                        leads = Int(counts, "leads"),
                        enquiries = Int(counts, "enquiries"),
                        quotations = Int(counts, "quotations"),
                        orderAcceptances = Int(counts, "orderAcceptances"),
                        invoices = Int(counts, "invoices"),
                        salesReturns = Int(counts, "salesReturns")
                    },
                    quotationStatus = new[]
                    {
                        new { label = "Approved",         value = Int(qStatus, "approved") },
                        new { label = "Pending Approval", value = Int(qStatus, "submitted") },
                        new { label = "Draft",            value = Int(qStatus, "draft") },
                        new { label = "Rejected",         value = Int(qStatus, "rejected") }
                    },
                    followUpStatus,
                    quotationAgeing,
                    customerMonthlySales,
                    executivePerformance,
                    gstSummary = new
                    {
                        taxableValue = Round(Dec(gst, "taxableValue")),
                        exportLutValue = Round(Dec(gst, "exportLutValue")),
                        cgst = Round(Dec(gst, "cgst")),
                        sgst = Round(Dec(gst, "sgst")),
                        igst = Round(Dec(gst, "igst")),
                        creditNoteValue = Round(Dec(gst, "creditNoteValue"))
                    },
                    followUpsDue,
                    pendingActions,
                    datedPending = dated,
                    daily = new
                    {
                        today = new
                        {
                            date = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            invoiced = Round(Dec(day, "todayInvoiced")), invoices = Int(day, "todayInvoices"),
                            ordersBooked = Int(day, "todayOrders"), orderValue = Round(Dec(day, "todayOrderValue"))
                        },
                        yesterday = new
                        {
                            date = yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            invoiced = Round(Dec(day, "yInvoiced")), invoices = Int(day, "yInvoices"),
                            ordersBooked = Int(day, "yOrders"), orderValue = Round(Dec(day, "yOrderValue"))
                        },
                        monthToDate = new
                        {
                            fromDate = monthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            invoiced = Round(Dec(day, "mInvoiced")), invoices = Int(day, "mInvoices"),
                            ordersBooked = Int(day, "mOrders"), orderValue = Round(Dec(day, "mOrderValue"))
                        },
                        lastMonthSamePeriod = new
                        {
                            fromDate = prevFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            toDate = prevTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            invoiced = Round(Dec(day, "pInvoiced")), orderValue = Round(Dec(day, "pOrderValue"))
                        }
                    },
                    pendingOrders,
                    recentOrders
                };

                return data;
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>Runs one read-only query; rows as column-name → value maps, strings trimmed.</summary>
        private static async Task<List<Dictionary<string, object?>>> Rows(DbConnection con, string sql, SqlParameter[] parameters)
        {
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value is string s ? s.Trim() : value;
                }
                rows.Add(row);
            }
            return rows;
        }

        private static decimal Dec(Dictionary<string, object?>? r, string key) =>
            r != null && r.TryGetValue(key, out var v) && v != null ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;

        private static int Int(Dictionary<string, object?>? r, string key) =>
            r != null && r.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

        private static string Str(Dictionary<string, object?>? r, string key) =>
            r != null && r.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim() : "";

        private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        private static bool InWindow(string isoDate, DateTime from, DateTime to) =>
            DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d >= from && d <= to;
    }
}
