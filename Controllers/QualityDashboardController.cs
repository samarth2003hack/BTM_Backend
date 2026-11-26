using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EMSAPI.Models.Dash;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Quality Dashboard — EMSSoft/Modules/Quality/QualityDashboard.html.
    ///
    ///     GET api/QualityDashboard/GetSummary?FromDate=&amp;ToDate=&amp;SubscID=&amp;Refresh=
    ///
    /// Sources (all totals are summed in SQL; only small grouped rows come back):
    ///   in-process   JobCardInspection by InspDate: InspectedQty / OKQty /
    ///                ReworkQty / RejectedQty (SQL 79: inspected and rework from first-pass
    ///                inspections only; rejected from every inspection); reasons = RejectionReason trimmed,
    ///                compared case-insensitively ("Crack" / "crack " together)
    ///   loss value   rejected qty × the job card's OA line Rate (current revision)
    ///   incoming     GRNDtl Rec / Acc / Rej / Rew / AOD qty of inspected GRNs
    ///                (GRNMst.GRNType 'Q' or 'C') whose QC date (else GRN date)
    ///                is in the period, by supplier
    ///   returns      SalesReturnMst / SalesReturnDtl dated in the period. The
    ///                software has no complaint register and returns carry no
    ///                reason, so returns stand in for complaints.
    ///   awaiting     VW_JobCardSummary.AwaitingInspectionQty (now)
    /// Answers are cached for a few minutes (Models/DashboardSupport.cs).
    /// </summary>
    [Route("api/QualityDashboard")]
    public class QualityDashboardController : EmsControllerBase
    {
        private readonly EMSContext db;
        private readonly DashboardCache cache;

        public QualityDashboardController(EMSContext context, DashboardCache cache)
        {
            db = context;
            this.cache = cache;
        }

        // OA line rate of a job card: the current revision of its OA, same line.
        private const string OaRate = @"
            OUTER APPLY (SELECT TOP (1) ISNULL(od.Rate, 0) * ISNULL(NULLIF(om.CurrencyRate, 0), 1) AS Rate
                           FROM dbo.OAMst om
                           JOIN dbo.OADtl od ON od.MTransNo = om.MTransNo AND od.DeleteStatus = 'N' AND od.SlNo = j.OAID
                          WHERE om.SubscID = j.SubscID AND om.DocYear = j.OAYear AND om.DocNo = j.OANo
                            AND om.DeleteStatus = 'N'
                          ORDER BY CASE WHEN om.DocAmdStatus = 'N' THEN 0 ELSE 1 END, om.MTransNo DESC) oa";

        [HttpGet("GetSummary")]
        public async Task<IActionResult> GetSummary([FromQuery] string? FromDate, [FromQuery] string? ToDate,
            [FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var bad = Range(FromDate, ToDate, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var data = await cache.GetOrBuild("Quality", DashboardCache.KeyOf(SubscID, Iso(from), Iso(to)), Refresh,
                    t => Build(SubscID, from, to, t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private async Task<Dictionary<string, object?>> Build(int subscID, DateTime from, DateTime to, DashTimer t)
        {
            var con = db.Database.GetDbConnection();
            Microsoft.Data.SqlClient.SqlParameter[] P() => new[] { PInt("@SubscID", subscID), PDate("@From", from), PDate("@To", to) };
            const string InPeriod = @"
                  FROM dbo.JobCardInspection i
                  JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'
                 WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND i.InspDate BETWEEN @From AND @To";

            // ── in-process inspection: by day, by part, by reason ────────
            var byDay = await t.Time("inspection by day", () => Rows(con, @"
                SELECT i.InspDate AS d, SUM(CASE WHEN i.InspType = 'Rework' THEN 0 ELSE ISNULL(i.InspectedQty, 0) END) AS inspected, SUM(ISNULL(i.RejectedQty, 0)) AS rejected,
                       SUM(CASE WHEN i.InspType = 'Rework' THEN 0 ELSE ISNULL(i.ReworkQty, 0) END) AS rework" + InPeriod + @"
                 GROUP BY i.InspDate", P()));

            var partRows = await t.Time("inspection by part", () => Rows(con, @"
                SELECT j.PartNo AS partNo, ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                       LTRIM(RTRIM(ISNULL(j.Grade, ''))) AS grade,
                       SUM(CASE WHEN i.InspType = 'Rework' THEN 0 ELSE ISNULL(i.InspectedQty, 0) END) AS inspected, SUM(ISNULL(i.RejectedQty, 0)) AS rejected,
                       SUM(CASE WHEN i.InspType = 'Rework' THEN 0 ELSE ISNULL(i.ReworkQty, 0) END) AS rework, SUM(ISNULL(i.RejectedQty, 0) * ISNULL(oa.Rate, 0)) AS lossValue
                  FROM dbo.JobCardInspection i
                  JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo" + OaRate + @"
                 WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND i.InspDate BETWEEN @From AND @To
                 GROUP BY j.PartNo, p.PartCode, p.PartName, LTRIM(RTRIM(ISNULL(j.Grade, '')))", P()));

            var reasonRows = await t.Time("reasons", () => Rows(con, @"
                SELECT j.PartNo AS partNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))) AS grade,
                       LOWER(LTRIM(RTRIM(ISNULL(i.RejectionReason, '')))) AS reasonKey,
                       MIN(LTRIM(RTRIM(ISNULL(i.RejectionReason, '')))) AS reason, SUM(i.RejectedQty) AS qty" + InPeriod + @"
                   AND ISNULL(i.RejectedQty, 0) > 0
                 GROUP BY j.PartNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))), LOWER(LTRIM(RTRIM(ISNULL(i.RejectionReason, ''))))", P()));

            string ReasonText(Dictionary<string, object?> r) => Str(r, "reason") == "" ? "Not given" : Str(r, "reason");
            var reasons = reasonRows.GroupBy(r => Str(r, "reasonKey"))
                .Select(g => new { reason = ReasonText(g.First()), qty = g.Sum(r => Dbl(r, "qty")) })
                .OrderByDescending(x => x.qty).Take(100).ToList();

            var parts = partRows.Select(r =>
            {
                var mine = reasonRows.Where(x => Int(x, "partNo") == Int(r, "partNo") && Str(x, "grade") == Str(r, "grade"))
                                     .OrderByDescending(x => Dbl(x, "qty")).FirstOrDefault();
                double ins = Dbl(r, "inspected"), rej = Dbl(r, "rejected"), rw = Dbl(r, "rework");
                return new
                {
                    partCode = Str(r, "partCode"), partName = Str(r, "partName"), grade = Str(r, "grade"),
                    inspectedQty = ins, rejectedQty = rej, reworkQty = rw, rejectionPct = Pct(rej, ins), reworkPct = Pct(rw, ins),
                    topReason = rej > 0 && mine != null ? ReasonText(mine) : "—", lossValue = Math.Round(Dbl(r, "lossValue")),
                };
            }).ToList();

            double inspected = byDay.Sum(r => Dbl(r, "inspected")), rejected = byDay.Sum(r => Dbl(r, "rejected")),
                   rework = byDay.Sum(r => Dbl(r, "rework"));

            var bucket = BucketOf(from, to);
            var busy = byDay.Select(r => Date(r, "d")).Where(d => d != null).Select(d => Iso(d!.Value)).ToHashSet();
            var trend = Buckets(bucket, from, to, busy).Select(bk =>
            {
                var mine = byDay.Where(r => Date(r, "d") is DateTime d && BucketKey(bucket, d).Key == bk.Key).ToList();
                double a = mine.Sum(r => Dbl(r, "inspected")), b = mine.Sum(r => Dbl(r, "rejected"));
                return new { label = bk.Label, inspectedQty = a, rejectedQty = b, reworkQty = mine.Sum(r => Dbl(r, "rework")), rejectionPct = Pct(b, a) };
            }).ToList();

            // ── incoming (GRN QC) by supplier ────────────────────────────
            var incRows = await t.Time("incoming", () => Rows(con, @"
                SELECT ISNULL(NULLIF(m.VendorName, ''), 'Supplier ' + CAST(m.VendorNo AS varchar(12))) AS supplier,
                       MAX(ISNULL(d.Unit, '')) AS unit, COUNT(DISTINCT m.MTransNo) AS grns,
                       SUM(ISNULL(d.RecQty, 0)) AS receivedQty, SUM(ISNULL(d.AccQty, 0)) AS acceptedQty,
                       SUM(ISNULL(d.RejQty, 0)) AS rejectedQty, SUM(ISNULL(d.RewQty, 0)) AS reworkQty, SUM(ISNULL(d.AODQty, 0)) AS aodQty
                  FROM dbo.GRNMst m
                  JOIN dbo.GRNDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.GRNType IN ('Q', 'C')
                   AND CAST(ISNULL(m.QCDate, m.DocDate) AS date) BETWEEN @From AND @To
                 GROUP BY ISNULL(NULLIF(m.VendorName, ''), 'Supplier ' + CAST(m.VendorNo AS varchar(12)))", P()));
            var suppliers = incRows.Select(r => new
            {
                supplier = Str(r, "supplier"), unit = Str(r, "unit"), grns = Int(r, "grns"),
                receivedQty = Dbl(r, "receivedQty"), acceptedQty = Dbl(r, "acceptedQty"), rejectedQty = Dbl(r, "rejectedQty"),
                reworkQty = Dbl(r, "reworkQty"), aodQty = Dbl(r, "aodQty"), rejectionPct = Pct(Dbl(r, "rejectedQty"), Dbl(r, "receivedQty")),
            }).OrderByDescending(x => x.rejectionPct).ThenByDescending(x => x.rejectedQty).ToList();
            double incReceived = suppliers.Sum(x => x.receivedQty), incRejected = suppliers.Sum(x => x.rejectedQty);

            // ── customer returns ─────────────────────────────────────────
            const string Returns = @"
                  FROM dbo.SalesReturnMst m
                  JOIN dbo.SalesReturnDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocDate BETWEEN @From AND @To";
            var custRows = await t.Time("returns by customer", () => Rows(con, @"
                SELECT ISNULL(NULLIF(m.VendorName, ''), 'Customer ' + CAST(m.VendorNo AS varchar(12))) AS customer,
                       COUNT(DISTINCT m.MTransNo) AS returns, SUM(ISNULL(d.ReturnQty, 0)) AS qty,
                       SUM(ISNULL(d.TotalValue, 0) * ISNULL(NULLIF(m.CurrencyRate, 0), 1)) AS value" + Returns + @"
                 GROUP BY ISNULL(NULLIF(m.VendorName, ''), 'Customer ' + CAST(m.VendorNo AS varchar(12)))", P()));
            var latestRows = await t.Time("latest returns", () => Rows(con, @"
                SELECT TOP (8) CONCAT(RTRIM(ISNULL(m.DocSeries, '')), CASE WHEN ISNULL(m.DocSeries, '') = '' THEN '' ELSE '/' END,
                              m.DocYear, '/', RIGHT('0000' + CAST(m.DocNo AS varchar(10)), 4)) AS docNo,
                       CONVERT(char(10), m.DocDate, 23) AS date, ISNULL(m.VendorName, '') AS customer,
                       ISNULL(d.PartCode, '') AS partCode, ISNULL(NULLIF(d.Description, ''), ISNULL(p.PartName, '')) AS partName,
                       ISNULL(d.ReturnQty, 0) AS qty, ISNULL(d.TotalValue, 0) * ISNULL(NULLIF(m.CurrencyRate, 0), 1) AS value,
                       CASE m.LockStatus WHEN 'Y' THEN 'Approved' WHEN 'S' THEN 'Submitted' WHEN 'R' THEN 'Rejected' ELSE 'Draft' END AS status
                  FROM dbo.SalesReturnMst m
                  JOIN dbo.SalesReturnDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocDate BETWEEN @From AND @To
                 ORDER BY m.DocDate DESC, m.MTransNo DESC, d.SlNo", P()));

            var byCustomer = TopWithOthers(custRows.Select(r => new Dictionary<string, object?>
            {
                ["customer"] = Str(r, "customer"), ["returns"] = Int(r, "returns"), ["qty"] = Dbl(r, "qty"), ["value"] = Math.Round(Dbl(r, "value")),
            }), r => Dbl(r, "value"), rest => new Dictionary<string, object?>
            {
                ["customer"] = "Others (" + rest.Count + ")", ["returns"] = (int)SumOf(rest, "returns"),
                ["qty"] = SumOf(rest, "qty"), ["value"] = SumOf(rest, "value"),
            });

            // ── awaiting inspection (now) ────────────────────────────────
            var awaiting = (await t.Time("awaiting", () => Rows(con, @"
                SELECT COUNT(*) AS jobCards, ISNULL(SUM(s.AwaitingInspectionQty), 0) AS qty
                  FROM dbo.VW_JobCardSummary s
                  JOIN dbo.JobCardMst j ON j.MTransNo = s.JobCardTransNo AND j.DeleteStatus = 'N'
                 WHERE s.SubscID = @SubscID AND ISNULL(s.AwaitingInspectionQty, 0) > 0", PInt("@SubscID", subscID)))).FirstOrDefault();

            var totals = new Dictionary<string, int>();
            return new Dictionary<string, object?>
            {
                ["kpi"] = new
                {
                    inspectedQty = inspected, rejectedQty = rejected, rejectionPct = Pct(rejected, inspected),
                    reworkQty = rework, reworkPct = Pct(rework, inspected),
                    incomingRejectionPct = Pct(incRejected, incReceived), incomingRejectedQty = incRejected,
                    returnCount = custRows.Sum(r => Int(r, "returns")), returnQty = custRows.Sum(r => Dbl(r, "qty")),
                    returnValue = Math.Round(custRows.Sum(r => Dbl(r, "value"))),
                },
                ["trendBucket"] = bucket,
                ["trend"] = trend,
                ["rejectionReasons"] = reasons,
                ["byPart"] = Limit(totals, "byPart", parts.OrderByDescending(p => p.lossValue).ThenByDescending(p => p.rejectedQty)),
                // Its own top 10, so the rework chart is right even when byPart is cut.
                ["reworkByPart"] = parts.Where(p => p.reworkQty > 0).OrderByDescending(p => p.reworkPct).ThenByDescending(p => p.reworkQty)
                    .Take(ChartLimit).Select(p => new { p.partCode, p.partName, p.reworkQty, p.reworkPct }).ToList(),
                ["awaitingInspection"] = new { jobCards = Int(awaiting, "jobCards"), qty = Dbl(awaiting, "qty") },
                ["incoming"] = new
                {
                    summary = new
                    {
                        grns = suppliers.Sum(x => x.grns), receivedQty = incReceived, rejectedQty = incRejected,
                        reworkQty = suppliers.Sum(x => x.reworkQty), aodQty = suppliers.Sum(x => x.aodQty), rejectionPct = Pct(incRejected, incReceived),
                    },
                    bySupplier = Limit(totals, "bySupplier", suppliers),
                },
                ["customerReturns"] = new
                {
                    byCustomer,
                    latest = latestRows.Select(r => new
                    {
                        docNo = Str(r, "docNo"), date = Str(r, "date"), customer = Str(r, "customer"), partCode = Str(r, "partCode"),
                        partName = Str(r, "partName"), qty = Dbl(r, "qty"), value = Math.Round(Dbl(r, "value")), status = Str(r, "status"),
                    }).ToList(),
                },
                ["listTotals"] = totals,
            };
        }
    }
}
