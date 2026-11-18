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
    /// Purchase Dashboard — EMSSoft/Modules/Purchase/PurchaseDashboard.html.
    ///
    /// One read-only endpoint that returns every dataset the page draws, in the
    /// same shapes as the page's SECTION 3 variables, so the page only swaps its
    /// data source and keeps all of its own calculations.
    ///
    ///     GET api/PurchaseDashboard/GetDashboard/{subscID}?asOfDate=YYYY-MM-DD
    ///
    /// Period: the financial year (1 April) that contains asOfDate, up to asOfDate.
    /// asOfDate defaults to today's server date.
    ///
    /// Rules — taken from the existing screens, reports, views and procedures:
    ///   - Live = DeleteStatus 'N'; PR / RFQ / quotation / PO read only the current
    ///     revision (DocAmdStatus 'N').                   (PurchaseReportsController)
    ///   - PR / RFQ approved = LockStatus 'Y'. PO LockStatus: N draft, S submitted,
    ///     Y approved, R rejected.          (PurchaseOrderApproval.html, SQL/43)
    ///   - Supplier quotation: Approved = LockStatus 'Y'; Open = LockStatus 'N' and
    ///     DocStatus 'N'; Not Approved = DocStatus 'Y'.    (RFQApprovalReport)
    ///   - PO from a PR: TargetType 'PR' (direct) or 'PQ' (via the quote's RFQ);
    ///     anything else is a manual PO.                   (VW_PurReqForPO)
    ///   - Received against a PO line: GRNDtl.BillQty where GRNMst.TargetType 'PO',
    ///     matched to the PO across its revisions on series + year + number + line
    ///     (an amended PO is a new row; GRNs keep the old one). (PurchaseStatusReports)
    ///   - GRN confirmed = LockStatus 'Y'; QC stage GRNType N / Q / C. (VW_GRNInwardStatus)
    ///   - Store allocation counts accepted lines only (AccQty &gt; 0). (SQL/31)
    ///   - Heat No pending: a confirmed GRN with a line whose part has no live
    ///     HeatNoMst against that GRN.                      (HeatNoController)
    ///   - RGP: RGPType 'RGP'; returned qty from RGPInwDtl.LinkNo = RGPDtl.TransSlNo.
    ///                                                     (RGPStatusReport)
    ///   - Money is in INR: amounts are stored in the document currency, so they
    ///     are multiplied by CurrencyRate (missing / zero = 1). RGP and RFQ
    ///     quotation lines carry no currency and are taken as INR.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    ///
    /// Approval dates are not stored and the database has no audit triggers, so
    /// the cycle-time legs use only dates the documents carry: PR → RFQ →
    /// quotations → PO → GRN → QC. (QC → store is left out: GRNDtl.ModifiedOn,
    /// the only allocation time, changes on any later edit of the line.)
    ///
    /// On-time delivery counts only GRNs whose PO line (or PO) has a delivery date.
    /// </summary>
    [Route("api/PurchaseDashboard")]
    public class PurchaseDashboardController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PurchaseDashboardController(EMSContext context) => db = context;

        private const int AgendaWindowDays = 5;   // the page's ALERT_WINDOW_DAYS
        private const int MaxCategorySlices = 4;  // the page's palette has four slots
        private const int MaxDepartments = 6;
        private const int TopSupplierCount = 8;
        private const int ScorecardCount = 6;
        private const int RecentCount = 8;

        private const string Rate = "ISNULL(NULLIF(m.CurrencyRate, 0), 1)";

        // Value of one unit on GRN line d, on the same basis as GRNMst.NetValue
        // (the line total incl. tax over the billed qty), so rejected / rework
        // value can be taken off the received value like for like.
        private const string LineUnitValue =
            "(CASE WHEN ISNULL(d.BillQty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.BillQty ELSE ISNULL(d.Rate, 0) END)";
        private const string HomeCurrency = "ISNULL(NULLIF(RTRIM(m.TxnCurrency), ''), 'INR') = 'INR'";

        // Received (billed) qty of PO line d of PO m, across every revision of the
        // PO: an amendment is a new POMst row, and a GRN keeps pointing at the
        // revision it was made against (PurchaseStatusReportsController matches
        // the same way, on series + year + number + line).
        private const string ReceivedQty = @"
                        SELECT SUM(gd.BillQty) AS Qty
                        FROM dbo.GRNDtl gd
                        JOIN dbo.GRNMst gm ON gm.MTransNo = gd.MTransNo
                        JOIN dbo.POMst  gp ON gp.MTransNo = gm.TargetTransNo
                        WHERE gd.DeleteStatus = 'N' AND gm.DeleteStatus = 'N' AND gm.TargetType = 'PO'
                          AND gp.SubscID = m.SubscID AND gp.DocSeries = m.DocSeries
                          AND gp.DocYear = m.DocYear AND gp.DocNo = m.DocNo
                          AND gd.SlNo = d.SlNo";

        // The PR a PO was raised from (directly, or through the quote's RFQ).
        private const string PoPr = @"
                    OUTER APPLY (
                        SELECT TOP (1) pr.DocSeries, pr.DocNo
                        FROM dbo.PurReqMst pr
                        WHERE (m.TargetType = 'PR' AND pr.MTransNo = m.TargetTransNo)
                           OR (m.TargetType = 'PQ' AND pr.MTransNo =
                                 (SELECT rr.PRTransNo FROM dbo.RFQPriceMst qq
                                    JOIN dbo.RFQMst rr ON rr.MTransNo = qq.TargetTransNo
                                   WHERE qq.MTransNo = m.TargetTransNo))
                    ) prx";

        [HttpGet("GetDashboard/{subscID}")]
        public async Task<IActionResult> GetDashboard(int subscID, [FromQuery] DateTime? asOfDate = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var asOf = (asOfDate ?? DateTime.Today).Date;
            var fyStart = new DateTime(asOf.Month >= 4 ? asOf.Year : asOf.Year - 1, 4, 1);
            var windowEnd = asOf.AddDays(AgendaWindowDays - 1);

            SqlParameter[] P() => new[]
            {
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = fyStart },
                new SqlParameter("@AsOf", SqlDbType.Date) { Value = asOf },
                new SqlParameter("@WindowEnd", SqlDbType.Date) { Value = windowEnd }
            };

            try
            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open)
                    await con.OpenAsync();

                var months = new List<DateTime>();
                for (var mo = fyStart; mo <= asOf; mo = mo.AddMonths(1))
                    months.Add(mo);
                var labels = months.Select((mo, i) =>
                    mo.ToString("MMM-yy", CultureInfo.InvariantCulture) + (i == months.Count - 1 ? " (MTD)" : ""))
                    .ToList();
                int MonthIndex(object? d) =>
                    d is DateTime dt ? (dt.Year - fyStart.Year) * 12 + dt.Month - fyStart.Month : -1;

                // ---------------------------------------------------------
                // 3.2 Monthly trend — ordered (approved PO) vs received (GRN),
                // plus the average PO → GRN days of each month's receipts.
                // ---------------------------------------------------------
                var orderedRows = await Rows(con, @"
                    SELECT DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1) AS mon,
                           SUM(ISNULL(m.NetValue, 0) * " + Rate + @") AS val
                    FROM dbo.POMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P());

                var receivedRows = await Rows(con, @"
                    SELECT DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1) AS mon,
                           SUM(ISNULL(m.NetValue, 0) * " + Rate + @") AS val,
                           AVG(CASE WHEN po.DocDate IS NOT NULL AND po.DocDate <= m.DocDate
                                    THEN CAST(DATEDIFF(day, po.DocDate, m.DocDate) AS float) END) AS leadDays
                    FROM dbo.GRNMst m
                    LEFT JOIN dbo.POMst po ON m.TargetType = 'PO' AND po.MTransNo = m.TargetTransNo
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P());

                var ordered = new decimal[months.Count];
                var received = new decimal[months.Count];
                var leadDays = new double?[months.Count];
                foreach (var r in orderedRows) { var i = MonthIndex(r["mon"]); if (i >= 0 && i < months.Count) ordered[i] = Round(Dec(r, "val")); }
                foreach (var r in receivedRows)
                {
                    var i = MonthIndex(r["mon"]);
                    if (i < 0 || i >= months.Count) continue;
                    received[i] = Round(Dec(r, "val"));
                    leadDays[i] = r["leadDays"] is double ld ? Math.Round(ld, 1) : null;
                }

                // Open PO value at each month-end: approved, not closed PO lines dated
                // on or before that day, less what was received against them by then
                // (across revisions). Element 0 is the day before the year starts
                // (brought forward); then one per month, the last at asOfDate. A
                // closed PO (DocStatus 'Y') carries no closing date, so it is left out.
                var monthEnds = new List<DateTime> { fyStart.AddDays(-1) };
                monthEnds.AddRange(months.Select(mo => mo.AddMonths(1).AddDays(-1) < asOf ? mo.AddMonths(1).AddDays(-1) : asOf));
                var monthEndValues = string.Join(", ", monthEnds.Select(d => "('" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "')"));
                var openRows = await Rows(con, @"
                    WITH me AS (SELECT CAST(v AS date) AS E FROM (VALUES " + monthEndValues + @") t(v)),
                    ln AS (
                        SELECT m.DocDate, m.SubscID, m.DocSeries, m.DocYear, m.DocNo, d.SlNo,
                               ISNULL(d.Qty, 0) AS Qty,
                               CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.Qty ELSE 0 END * " + Rate + @" AS UnitValue
                        FROM dbo.POMst m
                        JOIN dbo.PODtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y' AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                          AND m.DocDate <= @AsOf
                    )
                    SELECT me.E AS monthEnd,
                           SUM(CASE WHEN ln.Qty - ISNULL(g.Qty, 0) > 0
                                    THEN (ln.Qty - ISNULL(g.Qty, 0)) * ln.UnitValue ELSE 0 END) AS val
                    FROM me
                    JOIN ln ON ln.DocDate <= me.E
                    OUTER APPLY (
                        SELECT SUM(gd.BillQty) AS Qty
                        FROM dbo.GRNDtl gd
                        JOIN dbo.GRNMst gm ON gm.MTransNo = gd.MTransNo
                        JOIN dbo.POMst  gp ON gp.MTransNo = gm.TargetTransNo
                        WHERE gd.DeleteStatus = 'N' AND gm.DeleteStatus = 'N' AND gm.TargetType = 'PO'
                          AND gm.DocDate <= me.E
                          AND gp.SubscID = ln.SubscID AND gp.DocSeries = ln.DocSeries
                          AND gp.DocYear = ln.DocYear AND gp.DocNo = ln.DocNo
                          AND gd.SlNo = ln.SlNo
                    ) g
                    GROUP BY me.E", P());
                var openByEnd = openRows.ToDictionary(r => (DateTime)r["monthEnd"]!, r => Round(Dec(r, "val")));
                var openSeriesAll = monthEnds.Select(d => openByEnd.TryGetValue(d, out var v) ? v : 0m).ToList();
                var openingOpenPoValue = openSeriesAll[0];
                var openPoSeries = openSeriesAll.Skip(1).ToArray();

                // ---------------------------------------------------------
                // 3.3 Document counts this year
                // ---------------------------------------------------------
                var c = (await Rows(con, @"
                    WITH pr AS (
                        SELECT m.MTransNo, m.LockStatus, m.RFQReq
                        FROM dbo.PurReqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ),
                    grn AS (
                        SELECT m.MTransNo, m.DocDate, m.GRNType,
                               (SELECT MIN(ISNULL(pd.DeleveryDate, po.DueDate))
                                  FROM dbo.GRNDtl gd
                                  JOIN dbo.POMst po ON po.MTransNo = m.TargetTransNo
                                  JOIN dbo.PODtl pd ON pd.MTransNo = po.MTransNo AND pd.SlNo = gd.SlNo
                                                   AND pd.DeleteStatus = 'N'
                                 WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N'
                                   AND m.TargetType = 'PO')                           AS DueDate,
                               (SELECT COUNT(*) FROM dbo.GRNNCDtl nc
                                 WHERE nc.MTransNo = m.MTransNo AND nc.DeleteStatus = 'N') AS NCLines
                        FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    )
                    SELECT
                      (SELECT COUNT(*) FROM pr)                                                   AS requisitions,
                      (SELECT COUNT(*) FROM pr WHERE pr.LockStatus = 'Y')                         AS prApproved,
                      (SELECT COUNT(*) FROM pr WHERE pr.LockStatus = 'Y' AND pr.RFQReq = 'Y')     AS prRfqRequired,
                      (SELECT COUNT(*) FROM pr WHERE pr.LockStatus = 'Y' AND pr.RFQReq = 'Y'
                          AND EXISTS (SELECT 1 FROM dbo.RFQMst r
                                       WHERE r.PRTransNo = pr.MTransNo AND r.DeleteStatus = 'N'))   AS prRfqRaised,
                      (SELECT COUNT(*) FROM pr
                        WHERE EXISTS (
                            SELECT 1 FROM dbo.POMst om
                              LEFT JOIN dbo.RFQPriceMst qq ON om.TargetType = 'PQ' AND qq.MTransNo = om.TargetTransNo
                              LEFT JOIN dbo.RFQMst      rr ON rr.MTransNo = qq.TargetTransNo
                             WHERE om.SubscID = @SubscID AND om.DeleteStatus = 'N' AND om.DocAmdStatus = 'N'
                               AND ((om.TargetType = 'PR' AND om.TargetTransNo = pr.MTransNo)
                                 OR (om.TargetType = 'PQ' AND rr.PRTransNo = pr.MTransNo))))     AS prOrdered,
                      (SELECT COUNT(*) FROM dbo.RFQMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS rfqs,
                      (SELECT COUNT(*) FROM dbo.RFQMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y' AND m.DocDate BETWEEN @FromDate AND @AsOf)       AS rfqApproved,
                      (SELECT COUNT(*) FROM dbo.RFQPriceMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS quotations,
                      (SELECT COUNT(*) FROM dbo.POMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS purchaseOrders,
                      (SELECT COUNT(*) FROM dbo.POMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND ISNULL(m.TargetType, '') NOT IN ('PR', 'PQ')
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS manualPOs,
                      (SELECT COUNT(*) FROM grn)                                                  AS grns,
                      (SELECT COUNT(*) FROM grn WHERE grn.DueDate IS NOT NULL)                    AS grnsDue,
                      (SELECT COUNT(*) FROM grn WHERE grn.DueDate IS NOT NULL AND grn.DocDate <= grn.DueDate) AS grnsOnTime,
                      (SELECT COUNT(*) FROM grn WHERE grn.GRNType IN ('Q', 'C'))                  AS inspections,
                      (SELECT COUNT(*) FROM grn WHERE grn.GRNType IN ('Q', 'C') AND grn.NCLines > 0) AS ncGrns,
                      (SELECT COUNT(*) FROM grn WHERE grn.GRNType = 'C')                          AS ncClosed,
                      (SELECT COUNT(*) FROM dbo.StoreIssueMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS storeIssues,
                      (SELECT COUNT(*) FROM dbo.StoreReturnsMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf)                              AS issueReturns", P())).First();

                // ---------------------------------------------------------
                // Pending actions — open now, any date.
                // ---------------------------------------------------------
                var pend = (await Rows(con, @"
                    SELECT
                      -- Open = not closed and still has a line to order (VW_PurReqItemsForPO
                      -- balance: PR qty less the qty ordered on POs raised from it).
                      (SELECT COUNT(*) FROM dbo.PurReqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                          AND EXISTS (SELECT 1 FROM dbo.VW_PurReqItemsForPO v
                                       WHERE v.PRTransNo = m.MTransNo AND v.BalanceQty > 0))                AS openRequisitions,
                      (SELECT COUNT(*) FROM dbo.PurReqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND ISNULL(m.LockStatus, 'N') <> 'Y' AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y')  AS prApproval,
                      (SELECT COUNT(*) FROM dbo.RFQMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND ISNULL(m.LockStatus, 'N') <> 'Y' AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y')  AS rfqApproval,
                      (SELECT COUNT(*) FROM dbo.POMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'S')                                                         AS poApproval,
                      (SELECT COUNT(*) FROM dbo.PurReqMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y' AND m.RFQReq = 'Y' AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                          AND NOT EXISTS (SELECT 1 FROM dbo.RFQMst r
                                           WHERE r.PRTransNo = m.MTransNo AND r.DeleteStatus = 'N'))      AS awaitingRfq,
                      (SELECT COUNT(*) FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND ISNULL(m.GRNType, 'N') NOT IN ('Q', 'C'))                                   AS pendingQc,
                      (SELECT COUNT(*) FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.GRNType = 'Q'
                          AND EXISTS (SELECT 1 FROM dbo.GRNNCDtl nc
                                       WHERE nc.MTransNo = m.MTransNo AND nc.DeleteStatus = 'N'))         AS ncOpen,
                      (SELECT COUNT(*) FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.GRNType IN ('Q', 'C')
                          AND EXISTS (SELECT 1 FROM dbo.GRNDtl gd
                                       WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N'
                                         AND gd.AccQty > 0 AND NULLIF(LTRIM(RTRIM(gd.Store)), '') IS NULL)) AS storePending,
                      (SELECT COUNT(*) FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND EXISTS (SELECT 1 FROM dbo.GRNDtl gd
                                       WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N'
                                         AND NOT EXISTS (SELECT 1 FROM dbo.HeatNoMst h
                                                          WHERE h.TargetTransNo = m.MTransNo AND h.PartNo = gd.PartNo
                                                            AND h.SubscID = m.SubscID AND h.DeleteStatus = 'N'))) AS heatNo", P())).First();

                // ---------------------------------------------------------
                // 3.4 Store allocation of this year's GRNs (accepted lines only)
                // ---------------------------------------------------------
                var store = (await Rows(con, @"
                    SELECT
                      SUM(CASE WHEN x.Inspected = 1 AND (x.AccLines = 0 OR x.Allocated = x.AccLines) THEN 1 ELSE 0 END) AS allocated,
                      SUM(CASE WHEN x.Inspected = 1 AND x.Allocated > 0 AND x.Allocated < x.AccLines THEN 1 ELSE 0 END)   AS partial,
                      SUM(CASE WHEN x.Inspected = 0 OR (x.AccLines > 0 AND x.Allocated = 0) THEN 1 ELSE 0 END)           AS awaiting
                    FROM (
                        SELECT CASE WHEN m.GRNType IN ('Q', 'C') THEN 1 ELSE 0 END AS Inspected,
                               ISNULL(a.AccLines, 0) AS AccLines, ISNULL(a.Allocated, 0) AS Allocated
                        FROM dbo.GRNMst m
                        OUTER APPLY (
                            SELECT COUNT(*) AS AccLines,
                                   SUM(CASE WHEN NULLIF(LTRIM(RTRIM(gd.Store)), '') IS NULL THEN 0 ELSE 1 END) AS Allocated
                            FROM dbo.GRNDtl gd
                            WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N' AND gd.AccQty > 0
                        ) a
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ) x", P())).First();

                // ---------------------------------------------------------
                // 3.5 Cycle time — average days per leg, legs ending this year
                // ---------------------------------------------------------
                var cyc = (await Rows(con, @"
                    SELECT
                      (SELECT AVG(CAST(DATEDIFF(day, pr.DocDate, r.DocDate) AS float))
                         FROM dbo.RFQMst r JOIN dbo.PurReqMst pr ON pr.MTransNo = r.PRTransNo
                        WHERE r.SubscID = @SubscID AND r.DeleteStatus = 'N' AND r.DocAmdStatus = 'N'
                          AND r.DocDate BETWEEN @FromDate AND @AsOf AND r.DocDate >= pr.DocDate)          AS prToRfq,
                      (SELECT AVG(CAST(DATEDIFF(day, r.DocDate, q.DocDate) AS float))
                         FROM dbo.RFQPriceMst q JOIN dbo.RFQMst r ON r.MTransNo = q.TargetTransNo
                        WHERE q.SubscID = @SubscID AND q.DeleteStatus = 'N' AND q.DocAmdStatus = 'N'
                          AND q.DocDate BETWEEN @FromDate AND @AsOf AND q.DocDate >= r.DocDate)           AS rfqToQuote,
                      (SELECT AVG(CAST(DATEDIFF(day, q.DocDate, m.DocDate) AS float))
                         FROM dbo.POMst m JOIN dbo.RFQPriceMst q ON q.MTransNo = m.TargetTransNo
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.TargetType = 'PQ'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf AND m.DocDate >= q.DocDate)           AS quoteToPo,
                      (SELECT AVG(CAST(DATEDIFF(day, po.DocDate, m.DocDate) AS float))
                         FROM dbo.GRNMst m JOIN dbo.POMst po ON po.MTransNo = m.TargetTransNo
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.TargetType = 'PO'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf AND m.DocDate >= po.DocDate)          AS poToGrn,
                      (SELECT AVG(CAST(DATEDIFF(day, m.DocDate, m.QCDate) AS float))
                         FROM dbo.GRNMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.GRNType IN ('Q', 'C') AND m.QCDate IS NOT NULL
                          AND m.DocDate BETWEEN @FromDate AND @AsOf AND m.QCDate >= m.DocDate)            AS grnToQc", P())).First();

                double? Avg(string key) => cyc[key] is double v ? Math.Round(v, 1) : null;
                var cycleDays = new[]
                {
                    new { from = "PR",         to = "RFQ",           days = Avg("prToRfq") },
                    new { from = "RFQ",        to = "Quotations in", days = Avg("rfqToQuote") },
                    new { from = "Quotations", to = "PO",            days = Avg("quoteToPo") },
                    new { from = "PO",         to = "GRN",           days = Avg("poToGrn") },
                    new { from = "GRN",        to = "QC",            days = Avg("grnToQc") }
                };

                // ---------------------------------------------------------
                // 3.6 PO status, 3.7 supplier quotation outcome (this year)
                // ---------------------------------------------------------
                var poStatus = (await Rows(con, @"
                    SELECT
                      SUM(CASE WHEN m.LockStatus = 'Y' THEN 1 ELSE 0 END) AS approved,
                      SUM(CASE WHEN m.LockStatus = 'S' THEN 1 ELSE 0 END) AS submitted,
                      SUM(CASE WHEN ISNULL(m.LockStatus, 'N') NOT IN ('Y', 'S', 'R') THEN 1 ELSE 0 END) AS draft,
                      SUM(CASE WHEN m.LockStatus = 'R' THEN 1 ELSE 0 END) AS rejected
                    FROM dbo.POMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();

                var quoteStatus = (await Rows(con, @"
                    SELECT
                      SUM(CASE WHEN m.LockStatus = 'Y' THEN 1 ELSE 0 END)                                         AS approved,
                      SUM(CASE WHEN ISNULL(m.LockStatus, 'N') <> 'Y' AND ISNULL(RTRIM(m.DocStatus), 'N') <> 'Y' THEN 1 ELSE 0 END) AS openQ,
                      SUM(CASE WHEN ISNULL(m.LockStatus, 'N') <> 'Y' AND RTRIM(m.DocStatus) = 'Y' THEN 1 ELSE 0 END) AS notApproved
                    FROM dbo.RFQPriceMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();

                // ---------------------------------------------------------
                // 3.8 Requisitions by department, 3.9 spend by material type
                // ---------------------------------------------------------
                var deptRows = await Rows(con, @"
                    SELECT ISNULL(NULLIF(RTRIM(m.ReqDept), ''), 'Not specified') AS label, COUNT(*) AS value
                    FROM dbo.PurReqMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY ISNULL(NULLIF(RTRIM(m.ReqDept), ''), 'Not specified')", P());
                var departmentPrs = TopWithOthers(deptRows.Select(r => (Str(r, "label"), Dec(r, "value"))).ToList(), MaxDepartments)
                    .Select(x => new { label = x.label, value = (int)x.value }).ToList();

                var typeRows = await Rows(con, @"
                    SELECT ISNULL(NULLIF(UPPER(RTRIM(p.PartType)), ''), '') AS partType,
                           SUM(ISNULL(d.TotalValue, 0) * " + Rate + @") AS value
                    FROM dbo.POMst m
                    JOIN dbo.PODtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY ISNULL(NULLIF(UPPER(RTRIM(p.PartType)), ''), '')", P());
                var categorySpend = TopWithOthers(
                        typeRows.Select(r => (PartTypeLabel(Str(r, "partType")), Round(Dec(r, "value")))).ToList(), MaxCategorySlices)
                    .Select(x => new { x.label, x.value }).ToList();

                // ---------------------------------------------------------
                // 3.10 Top suppliers / 3.11 scorecards (approved POs this year)
                // ---------------------------------------------------------
                var supRows = await Rows(con, @"
                    WITH po AS (
                        SELECT m.VendorNo, COUNT(*) AS POs, SUM(ISNULL(m.NetValue, 0) * " + Rate + @") AS Value,
                               MAX(m.VendorName) AS VendorName
                        FROM dbo.POMst m
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                          AND m.LockStatus = 'Y' AND m.DocDate BETWEEN @FromDate AND @AsOf
                        GROUP BY m.VendorNo
                    ),
                    grn AS (
                        SELECT m.VendorNo, COUNT(*) AS GRNs,
                               SUM(CASE WHEN d.DueDate IS NOT NULL THEN 1 ELSE 0 END) AS DueGRNs,
                               SUM(CASE WHEN d.DueDate IS NOT NULL AND m.DocDate <= d.DueDate THEN 1 ELSE 0 END) AS OnTime,
                               AVG(CASE WHEN po.DocDate <= m.DocDate THEN CAST(DATEDIFF(day, po.DocDate, m.DocDate) AS float) END) AS LeadDays,
                               SUM(CASE WHEN m.GRNType IN ('Q', 'C') THEN q.RecQty ELSE 0 END) AS RecQty,
                               SUM(CASE WHEN m.GRNType IN ('Q', 'C') THEN q.RejQty ELSE 0 END) AS RejQty
                        FROM dbo.GRNMst m
                        LEFT JOIN dbo.POMst po ON m.TargetType = 'PO' AND po.MTransNo = m.TargetTransNo
                        OUTER APPLY (
                            -- The PO is joined here (not taken from the outer query): SQL Server
                            -- rejects an aggregate that mixes inner and outer columns (Msg 8124).
                            SELECT MIN(ISNULL(pd.DeleveryDate, dp.DueDate)) AS DueDate
                            FROM dbo.GRNDtl gd
                            JOIN dbo.POMst dp ON dp.MTransNo = m.TargetTransNo
                            JOIN dbo.PODtl pd ON pd.MTransNo = dp.MTransNo AND pd.SlNo = gd.SlNo AND pd.DeleteStatus = 'N'
                            WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N'
                              AND m.TargetType = 'PO'
                        ) d
                        OUTER APPLY (
                            SELECT SUM(ISNULL(gd.RecQty, 0)) AS RecQty, SUM(ISNULL(gd.RejQty, 0)) AS RejQty
                            FROM dbo.GRNDtl gd
                            WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N'
                        ) q
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                        GROUP BY m.VendorNo
                    )
                    SELECT po.VendorNo AS vendorNo,
                           COALESCE(NULLIF(RTRIM(s.SupplierName), ''), po.VendorName, '') AS name,
                           ISNULL(RTRIM(s.PinCodeLocation), '')  AS city,
                           ISNULL(RTRIM(s.TaxType), '')          AS taxType,
                           ISNULL(s.CreditPeriod, 0)             AS creditDays,
                           po.POs AS purchaseOrders, po.Value AS value,
                           ISNULL(grn.GRNs, 0) AS grns, ISNULL(grn.DueGRNs, 0) AS dueGrns, ISNULL(grn.OnTime, 0) AS onTime,
                           grn.LeadDays AS leadDays, ISNULL(grn.RecQty, 0) AS recQty, ISNULL(grn.RejQty, 0) AS rejQty
                    FROM po
                    LEFT JOIN grn ON grn.VendorNo = po.VendorNo
                    LEFT JOIN dbo.SupplierMaster s ON s.MTransNo = po.VendorNo
                    ORDER BY po.Value DESC", P());

                var topSuppliers = supRows.Take(TopSupplierCount).Select(r => new
                {
                    name = Str(r, "name"),
                    city = Str(r, "city") + (string.Equals(Str(r, "taxType"), "Export", StringComparison.OrdinalIgnoreCase)
                        ? (Str(r, "city") == "" ? "Import" : " · Import") : ""),
                    value = Round(Dec(r, "value"))
                }).ToList();
                var otherSupplierCount = Math.Max(0, supRows.Count - topSuppliers.Count);

                var supplierScorecards = supRows.Where(r => Int(r, "dueGrns") > 0).Take(ScorecardCount).Select(r => new
                {
                    name = Str(r, "name"),
                    type = Str(r, "taxType"),
                    purchaseOrders = Int(r, "purchaseOrders"),
                    grns = Int(r, "grns"),
                    onTimePct = (int)Math.Round(Int(r, "onTime") * 100m / Int(r, "dueGrns"), MidpointRounding.AwayFromZero),
                    rejectionPct = Dec(r, "recQty") > 0 ? Math.Round(Dec(r, "rejQty") * 100m / Dec(r, "recQty"), 1) : 0m,
                    leadDays = r["leadDays"] is double ld ? (int)Math.Round(ld, MidpointRounding.AwayFromZero) : 0,
                    creditDays = Int(r, "creditDays")
                }).ToList();

                // ---------------------------------------------------------
                // 3.12 PO value make-up (approved POs this year)
                // ---------------------------------------------------------
                var tax = (await Rows(con, @"
                    SELECT
                      SUM(CASE WHEN " + HomeCurrency + @" THEN ISNULL(m.TaxableValue, 0) * " + Rate + @" ELSE 0 END) AS domesticTaxable,
                      SUM(CASE WHEN " + HomeCurrency + @" THEN ISNULL(m.CGSTAmt, 0) * " + Rate + @" ELSE 0 END)      AS cgst,
                      SUM(CASE WHEN " + HomeCurrency + @" THEN ISNULL(m.SGSTAmt, 0) * " + Rate + @" ELSE 0 END)      AS sgst,
                      SUM(CASE WHEN " + HomeCurrency + @" THEN ISNULL(m.IGSTAmt, 0) * " + Rate + @" ELSE 0 END)      AS igst,
                      SUM(CASE WHEN NOT (" + HomeCurrency + @") THEN ISNULL(m.NetValue, 0) * " + Rate + @" ELSE 0 END) AS importValue,
                      SUM(CASE WHEN NOT (" + HomeCurrency + @") THEN ISNULL(m.NetValue, 0) ELSE 0 END)           AS importForeign,
                      COUNT(DISTINCT CASE WHEN NOT (" + HomeCurrency + @") THEN RTRIM(m.TxnCurrency) END)         AS importCurrencies,
                      MAX(CASE WHEN NOT (" + HomeCurrency + @") THEN RTRIM(m.TxnCurrency) END)                    AS importCurrency
                    FROM dbo.POMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y' AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();
                // One foreign currency → its amount and average rate; several → INR only.
                var singleImportCurrency = Int(tax, "importCurrencies") == 1;
                var importForeign = singleImportCurrency ? Dec(tax, "importForeign") : 0m;

                // ---------------------------------------------------------
                // 3.13 Recent purchase orders
                // ---------------------------------------------------------
                var recentPoRows = await Rows(con, @"
                    SELECT TOP (" + RecentCount + @")
                           m.DocSeries AS docSeries, m.DocNo AS docNo, m.DocAmdNo AS docAmdNo,
                           CONVERT(char(10), m.DocDate, 23) AS docDate,
                           m.VendorName AS supplier,
                           prx.DocSeries AS prSeries, prx.DocNo AS prNo,
                           CONVERT(char(10), COALESCE(m.DueDate,
                               (SELECT MIN(d.DeleveryDate) FROM dbo.PODtl d
                                 WHERE d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N')), 23) AS deliveryDate,
                           ISNULL(NULLIF(RTRIM(m.TxnCurrency), ''), 'INR') AS currency,
                           ISNULL(m.NetValue, 0) AS currencyValue,
                           ISNULL(m.NetValue, 0) * " + Rate + @" AS netValue,
                           CASE m.LockStatus WHEN 'Y' THEN 'Approved' WHEN 'S' THEN 'Submitted'
                                             WHEN 'R' THEN 'Rejected' ELSE 'Draft' END AS status
                    FROM dbo.POMst m" + PoPr + @"
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate <= @AsOf
                    ORDER BY m.DocDate DESC, m.DocNo DESC", P());
                var recentPos = recentPoRows.Select(r => new
                {
                    docSeries = Str(r, "docSeries"),
                    docNo = Int(r, "docNo"),
                    docAmdNo = Int(r, "docAmdNo"),
                    docDate = Str(r, "docDate"),
                    supplier = Str(r, "supplier"),
                    prNo = Int(r, "prNo") > 0 ? Ref(Str(r, "prSeries"), Int(r, "prNo")) : "",
                    deliveryDate = Str(r, "deliveryDate"),
                    currency = Str(r, "currency"),
                    currencyValue = Round(Dec(r, "currencyValue")),
                    netValue = Round(Dec(r, "netValue")),
                    status = Str(r, "status")
                }).ToList();

                // ---------------------------------------------------------
                // 3.14 QC this year (inspected GRNs), 3.15 store issue types
                // ---------------------------------------------------------
                var qc = (await Rows(con, @"
                    SELECT SUM(ISNULL(d.RecQty, 0)) AS receivedQty,
                           SUM(ISNULL(d.RewQty, 0)) AS reworkQty,
                           SUM(ISNULL(d.AODQty, 0)) AS aodQty,
                           SUM(ISNULL(d.RejQty, 0)) AS rejectQty,
                           SUM(ISNULL(d.RewQty, 0) * " + LineUnitValue + @" * " + Rate + @") AS reworkValue,
                           SUM(ISNULL(d.RejQty, 0) * " + LineUnitValue + @" * " + Rate + @") AS rejectValue
                    FROM dbo.GRNMst m
                    JOIN dbo.GRNDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                      AND m.GRNType IN ('Q', 'C')
                      AND m.DocDate BETWEEN @FromDate AND @AsOf", P())).First();

                var issueRows = await Rows(con, @"
                    SELECT ISNULL(NULLIF(RTRIM(m.TargetType), ''), 'Other') AS label, COUNT(*) AS value
                    FROM dbo.StoreIssueMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @AsOf
                    GROUP BY ISNULL(NULLIF(RTRIM(m.TargetType), ''), 'Other')
                    ORDER BY value DESC", P());
                var issueTypes = issueRows.Select(r => new { label = Str(r, "label"), value = Int(r, "value") }).ToList();

                // ---------------------------------------------------------
                // 3.16 Recent GRNs with QC stage and store status
                // ---------------------------------------------------------
                var recentGrnRows = await Rows(con, @"
                    SELECT TOP (" + RecentCount + @")
                           m.DocSeries AS docSeries, m.DocNo AS docNo,
                           CONVERT(char(10), m.DocDate, 23) AS docDate,
                           m.TargetType AS targetType, po.DocSeries AS poSeries, po.DocNo AS poNo,
                           m.VendorName AS supplier, ISNULL(m.RefNo, '') AS billNo,
                           ISNULL(m.NetValue, 0) * " + Rate + @" AS netValue,
                           CASE WHEN m.GRNType = 'C' THEN 'NC Closed'
                                WHEN m.GRNType = 'Q' AND nc.NCLines > 0 THEN 'NC Pending'
                                WHEN m.GRNType = 'Q' THEN 'Inspected'
                                ELSE 'Pending QC' END AS qc,
                           CASE WHEN ISNULL(m.GRNType, 'N') NOT IN ('Q', 'C') THEN 'Pending'
                                WHEN a.AccLines = 0 OR a.Allocated = a.AccLines THEN 'Allocated'
                                WHEN a.Allocated > 0 THEN 'Partial'
                                ELSE 'Pending' END AS store
                    FROM dbo.GRNMst m
                    LEFT JOIN dbo.POMst po ON m.TargetType = 'PO' AND po.MTransNo = m.TargetTransNo
                    OUTER APPLY (SELECT COUNT(*) AS NCLines FROM dbo.GRNNCDtl n
                                  WHERE n.MTransNo = m.MTransNo AND n.DeleteStatus = 'N') nc
                    OUTER APPLY (
                        SELECT COUNT(*) AS AccLines,
                               ISNULL(SUM(CASE WHEN NULLIF(LTRIM(RTRIM(gd.Store)), '') IS NULL THEN 0 ELSE 1 END), 0) AS Allocated
                        FROM dbo.GRNDtl gd
                        WHERE gd.MTransNo = m.MTransNo AND gd.DeleteStatus = 'N' AND gd.AccQty > 0
                    ) a
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                      AND m.DocDate <= @AsOf
                    ORDER BY m.DocDate DESC, m.DocNo DESC", P());
                var recentGrns = recentGrnRows.Select(r => new
                {
                    docSeries = Str(r, "docSeries"),
                    docNo = Int(r, "docNo"),
                    docDate = Str(r, "docDate"),
                    poNo = Int(r, "poNo") > 0 ? Ref(Str(r, "poSeries"), Int(r, "poNo"))
                         : (Str(r, "targetType") == "" ? "Direct" : Str(r, "targetType")),
                    supplier = Str(r, "supplier"),
                    billNo = Str(r, "billNo"),
                    netValue = Round(Dec(r, "netValue")),
                    qc = Str(r, "qc"),
                    store = Str(r, "store")
                }).ToList();

                // ---------------------------------------------------------
                // 3.17 Open PO lines — every approved, not-closed line with a
                // balance and a delivery date (any year: they are still due).
                // ---------------------------------------------------------
                var openLineRows = await Rows(con, @"
                    SELECT m.DocSeries AS docSeries, m.DocNo AS docNo, m.VendorName AS supplier,
                           COALESCE(NULLIF(d.PartDescription, ''), d.PartCode, '') AS part,
                           ISNULL(d.Qty, 0) - ISNULL(g.Qty, 0) AS pendingQty,
                           ISNULL(d.Unit, '') AS uom,
                           CASE WHEN ISNULL(d.Qty, 0) > 0 THEN ISNULL(d.TotalValue, 0) / d.Qty ELSE 0 END
                             * (ISNULL(d.Qty, 0) - ISNULL(g.Qty, 0)) * " + Rate + @" AS value,
                           CONVERT(char(10), ISNULL(d.DeleveryDate, m.DueDate), 23) AS deliveryDate
                    FROM dbo.POMst m
                    JOIN dbo.PODtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (" + ReceivedQty + @") g
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.LockStatus = 'Y' AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                      AND ISNULL(d.Qty, 0) - ISNULL(g.Qty, 0) > 0
                      AND ISNULL(d.DeleveryDate, m.DueDate) IS NOT NULL", P());
                var openPoLines = openLineRows.Select(r => new
                {
                    poNo = Ref(Str(r, "docSeries"), Int(r, "docNo")),
                    supplier = Str(r, "supplier"),
                    part = Str(r, "part"),
                    pendingQty = Dec(r, "pendingQty"),
                    uom = Str(r, "uom"),
                    value = Round(Dec(r, "value")),
                    deliveryDate = Str(r, "deliveryDate")
                }).ToList();

                // ---------------------------------------------------------
                // 3.18 / 3.19 Returnable gate passes raised this year
                // ---------------------------------------------------------
                var rgpSum = (await Rows(con, @"
                    WITH ln AS (
                        SELECT m.MTransNo, m.LockStatus,
                               ISNULL(d.Qty, 0) AS Qty, ISNULL(d.Rate, 0) AS Rate,
                               ISNULL((SELECT SUM(ISNULL(i.Qty, 0)) FROM dbo.RGPInwDtl i
                                        JOIN dbo.RGPInwMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                                       WHERE i.LinkNo = d.TransSlNo AND i.DeleteStatus = 'N'), 0) AS RetQty
                        FROM dbo.RGPMst m
                        LEFT JOIN dbo.RGPDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.RGPType = 'RGP'
                          AND m.DocDate BETWEEN @FromDate AND @AsOf
                    ),
                    hd AS (
                        SELECT MTransNo, MAX(LockStatus) AS LockStatus,
                               SUM(CASE WHEN Qty - RetQty > 0 THEN 1 ELSE 0 END) AS OpenLines,
                               SUM(Qty * Rate) AS SentValue
                        FROM ln GROUP BY MTransNo
                    )
                    SELECT COUNT(*) AS rgps,
                           SUM(CASE WHEN LockStatus = 'Y' AND OpenLines = 0 THEN 1 ELSE 0 END) AS closed,
                           SUM(CASE WHEN LockStatus = 'S' THEN 1 ELSE 0 END)                    AS submitted,
                           SUM(CASE WHEN ISNULL(LockStatus, 'N') NOT IN ('Y', 'S') THEN 1 ELSE 0 END) AS draft,
                           SUM(CASE WHEN LockStatus = 'Y' THEN SentValue ELSE 0 END)            AS valueSent,
                           (SELECT COUNT(*) FROM dbo.RGPInwMst im
                             WHERE im.SubscID = @SubscID AND im.DeleteStatus = 'N'
                               AND im.DocDate BETWEEN @FromDate AND @AsOf)                     AS inwards
                    FROM hd", P())).First();

                var openRgpRows = await Rows(con, @"
                    WITH ln AS (
                        SELECT m.MTransNo, d.SlNo, d.PartNo,
                               COALESCE(NULLIF(d.PartDescription, ''), d.PartCode, '') AS Item,
                               ISNULL(d.ItemRemarks, '') AS Remarks, ISNULL(d.Unit, '') AS Unit,
                               ISNULL(d.Qty, 0) AS Qty, ISNULL(d.Rate, 0) AS Rate,
                               ISNULL((SELECT SUM(ISNULL(i.Qty, 0)) FROM dbo.RGPInwDtl i
                                        JOIN dbo.RGPInwMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                                       WHERE i.LinkNo = d.TransSlNo AND i.DeleteStatus = 'N'), 0) AS RetQty
                        FROM dbo.RGPMst m
                        JOIN dbo.RGPDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                        WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.RGPType = 'RGP'
                          AND m.LockStatus = 'Y' AND m.DocDate BETWEEN @FromDate AND @AsOf
                    )
                    SELECT m.DocSeries AS docSeries, m.DocNo AS docNo,
                           CONVERT(char(10), m.DocDate, 23) AS docDate,
                           m.VendorName AS supplier,
                           f.Item AS item, f.LineCount AS lineCount,
                           COALESCE(NULLIF(f.Remarks, ''), m.DocNotes, '') AS purpose,
                           ISNULL(p.PartType, '') AS type,
                           t.SentQty AS sentQty, t.RetQty AS returnedQty, f.Unit AS uom,
                           t.Value AS value,
                           CONVERT(char(10), ISNULL(m.DueDate, m.DocDate), 23) AS dueDate
                    FROM dbo.RGPMst m
                    CROSS APPLY (SELECT SUM(Qty) AS SentQty, SUM(RetQty) AS RetQty,
                                        SUM(CASE WHEN Qty > RetQty THEN (Qty - RetQty) * Rate ELSE 0 END) AS Value,
                                        SUM(CASE WHEN Qty > RetQty THEN 1 ELSE 0 END) AS OpenLines
                                   FROM ln WHERE ln.MTransNo = m.MTransNo) t
                    CROSS APPLY (SELECT TOP (1) ln.Item, ln.Remarks, ln.Unit, ln.PartNo,
                                        (SELECT COUNT(*) FROM ln l2 WHERE l2.MTransNo = m.MTransNo) AS LineCount
                                   FROM ln WHERE ln.MTransNo = m.MTransNo ORDER BY ln.SlNo) f
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = f.PartNo
                    WHERE t.OpenLines > 0
                    ORDER BY m.DocDate DESC, m.DocNo DESC", P());
                var openRgps = openRgpRows.Select(r => new
                {
                    docSeries = Str(r, "docSeries"),
                    docNo = Int(r, "docNo"),
                    docDate = Str(r, "docDate"),
                    supplier = Str(r, "supplier"),
                    item = Str(r, "item") + (Int(r, "lineCount") > 1 ? $" +{Int(r, "lineCount") - 1} more" : ""),
                    purpose = Str(r, "purpose"),
                    // Same wording as Spend by Material Type ("Raw Material", not "RAW MATERIAL").
                    type = Str(r, "type") == "" ? "" : PartTypeLabel(Str(r, "type").ToUpperInvariant()),
                    sentQty = Dec(r, "sentQty"),
                    returnedQty = Dec(r, "returnedQty"),
                    uom = Str(r, "uom"),
                    value = Round(Dec(r, "value")),
                    dueDate = Str(r, "dueDate")
                }).ToList();

                // ---------------------------------------------------------
                // 3.20 Other dated items — RFQ / quotation validity, PR due date,
                // from the start of the year to the end of the 5-day window.
                // ---------------------------------------------------------
                var datedRows = await Rows(con, @"
                    SELECT 'rfqValidity' AS type, m.DocSeries AS docSeries, m.DocNo AS docNo,
                           CAST(ISNULL(s.Suppliers, 0) AS varchar(10)) + N' suppliers · '
                             + CAST(ISNULL(q.Quotes, 0) AS varchar(10)) + N' quoted' AS party,
                           COALESCE(NULLIF(m.DocNotes, ''), pr.Project, '') AS detail,
                           CAST(NULL AS numeric(18, 2)) AS value,
                           CONVERT(char(10), m.ValidTill, 23) AS date
                      FROM dbo.RFQMst m
                      LEFT JOIN dbo.PurReqMst pr ON pr.MTransNo = m.PRTransNo
                      OUTER APPLY (SELECT COUNT(*) AS Suppliers FROM dbo.RFQSupplier x
                                    WHERE x.MTransNo = m.MTransNo AND x.DeleteStatus = 'N') s
                      OUTER APPLY (SELECT COUNT(*) AS Quotes FROM dbo.RFQPriceMst x
                                    WHERE x.TargetTransNo = m.MTransNo AND x.DeleteStatus = 'N' AND x.DocAmdStatus = 'N') q
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                       AND m.ValidTill BETWEEN @FromDate AND @WindowEnd
                    UNION ALL
                    SELECT 'quoteValidity', m.DocSeries, m.DocNo,
                           m.VendorName,
                           CASE WHEN m.LockStatus = 'Y' THEN N'Approved quote · RFQ ' ELSE N'Open quote · RFQ ' END
                             + ISNULL(RTRIM(r.DocSeries) + '-' + CAST(r.DocNo AS varchar(20)), '')
                             + CASE WHEN m.LockStatus = 'Y' THEN N' · no PO yet' ELSE '' END,
                           (SELECT SUM(ISNULL(x.Qty, 0) * ISNULL(x.Rate, 0)) FROM dbo.RFQPriceDtl x
                             WHERE x.MTransNo = m.MTransNo AND x.DeleteStatus = 'N'),
                           CONVERT(char(10), m.ValidTill, 23)
                      FROM dbo.RFQPriceMst m
                      LEFT JOIN dbo.RFQMst r ON r.MTransNo = m.TargetTransNo
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND m.ValidTill BETWEEN @FromDate AND @WindowEnd
                       AND ((m.LockStatus = 'Y'
                             AND NOT EXISTS (SELECT 1 FROM dbo.POMst po
                                              WHERE po.TargetType = 'PQ' AND po.TargetTransNo = m.MTransNo
                                                AND po.DeleteStatus = 'N' AND po.DocAmdStatus = 'N'))
                         OR (ISNULL(m.LockStatus, 'N') <> 'Y' AND ISNULL(RTRIM(m.DocStatus), 'N') <> 'Y'))
                    UNION ALL
                    SELECT 'prDue', m.DocSeries, m.DocNo,
                           ISNULL(NULLIF(RTRIM(m.ReqDept), ''), 'Not specified'),
                           ISNULL(NULLIF(m.Project, ''), 'Requisition') + N' — '
                             + CASE WHEN ISNULL(m.LockStatus, 'N') <> 'Y' THEN 'awaiting approval'
                                    WHEN EXISTS (SELECT 1 FROM dbo.RFQMst x
                                                  WHERE x.PRTransNo = m.MTransNo AND x.DeleteStatus = 'N') THEN 'RFQ raised'
                                    ELSE 'not yet ordered' END,
                           NULL,
                           CONVERT(char(10), m.DueDate, 23)
                      FROM dbo.PurReqMst m
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND ISNULL(RTRIM(m.DocStatus), '') <> 'Y'
                       AND m.DueDate BETWEEN @FromDate AND @WindowEnd
                       AND NOT EXISTS (
                            SELECT 1 FROM dbo.POMst om
                              LEFT JOIN dbo.RFQPriceMst qq ON om.TargetType = 'PQ' AND qq.MTransNo = om.TargetTransNo
                              LEFT JOIN dbo.RFQMst      rr ON rr.MTransNo = qq.TargetTransNo
                             WHERE om.SubscID = m.SubscID AND om.DeleteStatus = 'N' AND om.DocAmdStatus = 'N'
                               AND ((om.TargetType = 'PR' AND om.TargetTransNo = m.MTransNo)
                                 OR (om.TargetType = 'PQ' AND rr.PRTransNo = m.MTransNo)))", P());
                var datedPending = datedRows.Select(r => new
                {
                    type = Str(r, "type"),
                    @ref = Ref(Str(r, "docSeries"), Int(r, "docNo")),
                    party = Str(r, "party"),
                    detail = Str(r, "detail"),
                    value = r["value"] == null ? (decimal?)null : Round(Dec(r, "value")),
                    date = Str(r, "date")
                }).ToList();

                var pendingCounts = new
                {
                    prApproval = Int(pend, "prApproval"),
                    rfqApproval = Int(pend, "rfqApproval"),
                    poApproval = Int(pend, "poApproval"),
                    awaitingRfq = Int(pend, "awaitingRfq"),
                    pendingQc = Int(pend, "pendingQc"),
                    ncOpen = Int(pend, "ncOpen"),
                    storePending = Int(pend, "storePending"),
                    heatNo = Int(pend, "heatNo")
                };

                var data = new
                {
                    period = new
                    {
                        fyLabel = $"FY {fyStart.Year}-{(fyStart.Year + 1) % 100:00}",
                        fyCode = $"{fyStart.Year % 100:00}{(fyStart.Year + 1) % 100:00}",
                        fromDate = fyStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    },
                    monthlyTrend = new { labels, ordered, received, leadDays },
                    openingOpenPoValue,
                    openPoSeries,
                    pipelineCounts = new
                    {
                        requisitions = Int(c, "requisitions"),
                        prApproved = Int(c, "prApproved"),
                        prRfqRequired = Int(c, "prRfqRequired"),
                        prRfqRaised = Int(c, "prRfqRaised"),
                        prOrdered = Int(c, "prOrdered"),
                        rfqs = Int(c, "rfqs"),
                        rfqApproved = Int(c, "rfqApproved"),
                        quotations = Int(c, "quotations"),
                        purchaseOrders = Int(c, "purchaseOrders"),
                        manualPOs = Int(c, "manualPOs"),
                        grns = Int(c, "grns"),
                        grnsDue = Int(c, "grnsDue"),
                        grnsOnTime = Int(c, "grnsOnTime"),
                        inspections = Int(c, "inspections"),
                        ncGrns = Int(c, "ncGrns"),
                        ncClosed = Int(c, "ncClosed"),
                        heatNoPending = pendingCounts.heatNo,
                        storeIssues = Int(c, "storeIssues"),
                        issueReturns = Int(c, "issueReturns")
                    },
                    pendingCounts,
                    openRequisitions = Int(pend, "openRequisitions"),
                    grnStore = new
                    {
                        allocated = Int(store, "allocated"),
                        partial = Int(store, "partial"),
                        awaiting = Int(store, "awaiting")
                    },
                    cycleDays,
                    poStatus = new[]
                    {
                        new { label = "Approved",  value = Int(poStatus, "approved") },
                        new { label = "Submitted", value = Int(poStatus, "submitted") },
                        new { label = "Draft",     value = Int(poStatus, "draft") },
                        new { label = "Rejected",  value = Int(poStatus, "rejected") }
                    },
                    quotationStatus = new[]
                    {
                        new { label = "Approved",     value = Int(quoteStatus, "approved") },
                        new { label = "Open",         value = Int(quoteStatus, "openQ") },
                        new { label = "Not Approved", value = Int(quoteStatus, "notApproved") }
                    },
                    departmentPrs,
                    categorySpend,
                    topSuppliers,
                    otherSupplierCount,
                    supplierScorecards,
                    poTaxSummary = new
                    {
                        domesticTaxable = Round(Dec(tax, "domesticTaxable")),
                        cgst = Round(Dec(tax, "cgst")),
                        sgst = Round(Dec(tax, "sgst")),
                        igst = Round(Dec(tax, "igst")),
                        importValue = Round(Dec(tax, "importValue")),
                        importCurrency = singleImportCurrency ? Str(tax, "importCurrency") : "",
                        importForeign = Round(importForeign),
                        importRate = importForeign > 0 ? Math.Round(Dec(tax, "importValue") / importForeign, 2) : 0m
                    },
                    recentPos,
                    qcSummary = new
                    {
                        receivedQty = Dec(qc, "receivedQty"),
                        reworkQty = Dec(qc, "reworkQty"),
                        aodQty = Dec(qc, "aodQty"),
                        rejectQty = Dec(qc, "rejectQty"),
                        reworkValue = Round(Dec(qc, "reworkValue")),
                        rejectValue = Round(Dec(qc, "rejectValue"))
                    },
                    issueTypes,
                    recentGrns,
                    openPoLines,
                    rgpSummary = new
                    {
                        rgps = Int(rgpSum, "rgps"),
                        closed = Int(rgpSum, "closed"),
                        submitted = Int(rgpSum, "submitted"),
                        draft = Int(rgpSum, "draft"),
                        inwards = Int(rgpSum, "inwards"),
                        valueSent = Round(Dec(rgpSum, "valueSent"))
                    },
                    openRgps,
                    datedPending
                };

                return OkResult(data, "Data found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
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

        /// <summary>Document reference as the Purchase grids show it: "&lt;series&gt;-&lt;no, 3 digits&gt;".</summary>
        private static string Ref(string series, int docNo) =>
            (series == "" ? "" : series + "-") + docNo.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');

        /// <summary>PartMaster.PartType as the dashboard labels it (RGP.html lists the types).</summary>
        private static string PartTypeLabel(string partType) => partType switch
        {
            "" => "Unclassified",
            "RAW MATERIAL" => "Raw Material",
            "BOUGHTOUT" => "Bought-out",
            "CONSUMABLE" => "Consumable",
            "SERVICE" => "Service",
            "COMPONENT" => "Component",
            "ASSET" => "Asset",
            "PRODUCT" => "Product",
            "NON STOCK" => "Non Stock",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(partType.ToLowerInvariant())
        };

        /// <summary>
        /// The largest groups by value; when there are more than <paramref name="slots"/>,
        /// the tail is folded into one "Others" entry so the total is unchanged.
        /// </summary>
        private static List<(string label, decimal value)> TopWithOthers(List<(string label, decimal value)> items, int slots)
        {
            var sorted = items.Where(i => i.value != 0).OrderByDescending(i => i.value).ToList();
            if (sorted.Count <= slots) return sorted;
            var head = sorted.Take(slots - 1).ToList();
            head.Add(("Others", sorted.Skip(slots - 1).Sum(i => i.value)));
            return head;
        }
    }
}
