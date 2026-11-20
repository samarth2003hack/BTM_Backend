using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using static EMSAPI.Models.PlanScheduleEngine;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Production Dashboard — EMSSoft/Modules/Production/Dashboard.html.
    ///
    ///     GET api/ProductionDashboard/GetSummary?FromDate=&amp;ToDate=&amp;MachineNo=&amp;SubscID=&amp;Refresh=
    ///         the period: KPIs, plan vs actual trend, hammers, rejection, losses, job status
    ///     GET api/ProductionDashboard/GetCurrent?MachineNo=&amp;SubscID=&amp;Refresh=
    ///         now: yesterday / today / tomorrow, today's and tomorrow's plan, hammer load,
    ///         WIP, job cards behind plan, orders at risk
    ///
    /// Two calls: changing the period re-runs only the first; the second (the
    /// heavy one — orders at risk run the Pending Order Report's planning and
    /// material allocation) does not depend on the period. Both are cached for
    /// a few minutes (Models/DashboardSupport.cs). MachineNo 0 = all hammers.
    ///
    /// Rules — the planning module's own (PlanningControllerBase):
    ///   hammer     MachineMst live, active, with a Forging operation
    ///   forged     JobCardEntry.Qty at the route's Forging step (else the last
    ///              step) by EntryDate, on the entry's machine (else the job card's)
    ///   planned    ProductionPlanDay of MAIN jobs of plans that are Approved,
    ///              Released or Closed (a Draft is not a plan yet) by PlanDate
    ///   adherence  per plan line: forged on its job card in the period plus
    ///              forging done ahead of plan before it (ForgedCredit), capped
    ///              at the planned qty; Σ credited ÷ Σ planned
    ///   rejected   JobCardInspection.RejectedQty by InspDate, on the job card's
    ///              hammer (else the machine that forged most of it)
    ///   available  Work Calendar working days × net hours × MachineMst.ShiftsPerDay
    ///              (a month with no calendar: Monday–Saturday at the nearest
    ///              calendar's net hours, else 8)
    ///   run        what the job card entries on the hammer show it ran: EndTime −
    ///              StartTime − IdleMin − SettingMin per entry (never below 0),
    ///              capped at available;  utilisation = run ÷ available
    ///   losses     idle = JobCardEntry.IdleMin by IdleReason ('Breakdown' left
    ///              out — breakdowns come from BreakdownMst, BreakdownOn →
    ///              RepairEnd or now, by BreakdownType)
    /// </summary>
    [Route("api/ProductionDashboard")]
    public class ProductionDashboardController : PlanningControllerBase
    {
        private readonly DashboardCache cache;

        public ProductionDashboardController(EMSContext context, DashboardCache cache) : base(context) => this.cache = cache;

        private static readonly string[] CountedPlanStatuses = { "Approved", "Released", "Closed" };
        private const int RiskWindowDays = 7;

        [HttpGet("GetSummary")]
        public async Task<IActionResult> GetSummary([FromQuery] string? FromDate, [FromQuery] string? ToDate,
            [FromQuery] int MachineNo, [FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var bad = Dash.Range(FromDate, ToDate, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var data = await cache.GetOrBuild("Production", DashboardCache.KeyOf(SubscID, Iso(from), Iso(to), MachineNo), Refresh,
                    t => BuildPeriod(SubscID, from, to, Math.Max(0, MachineNo), t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetCurrent")]
        public async Task<IActionResult> GetCurrent([FromQuery] int MachineNo, [FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await cache.GetOrBuild("ProductionNow", DashboardCache.KeyOf(SubscID, MachineNo), Refresh,
                    t => BuildCurrent(SubscID, Math.Max(0, MachineNo), t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ================================================================
        // SHARED READS
        // ================================================================

        private async Task<List<HammerRow>> Hammers(int subscID) =>
            (await Query(@"
                SELECT m.MTransNo AS machineNo, ISNULL(m.MachineCode, '') AS code, ISNULL(m.MachineName, '') AS name,
                       ISNULL(m.ShiftsPerDay, 1) AS shifts, ISNULL(m.EfficiencyPct, 100) AS efficiency
                  FROM dbo.MachineMst m
                 WHERE m.SubscID = @SubscID AND ISNULL(m.DeleteStatus, 'N') = 'N' AND ISNULL(m.LockStatus, 'Y') = 'Y'
                   AND EXISTS (SELECT 1 FROM STRING_SPLIT(ISNULL(m.Operation, ''), ',') x
                                 JOIN dbo.OtherMaster o ON o.MTransNo = TRY_CONVERT(INT, LTRIM(RTRIM(x.value)))
                                WHERE o.MasterType = 'Operation' AND o.MasterName LIKE '%forg%')
                 ORDER BY m.MachineCode", Int("@SubscID", subscID)))
            .Select(r => new HammerRow
            {
                MachineNo = I(r, "machineNo"), Code = S(r, "code"), Name = S(r, "name"),
                Shifts = (double)D(r, "shifts"), Efficiency = (double)D(r, "efficiency"),
            }).ToList();

        /// <summary>Working day and net hours of each date (Work Calendar, with the fallback above).</summary>
        private async Task<Func<DateTime, (bool Working, double NetHours)>> CalendarDays(int subscID, List<HammerRow> hammers, DateTime from, DateTime to)
        {
            var data = new PlanningData { SubscID = subscID };
            data.Hammers.AddRange(hammers);
            var months = new Dictionary<string, CalendarInfo?>();
            for (var m = new DateTime(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                months[m.ToString("yyyy-MM", Inv)] = await CalendarOf(m.ToString("yyyy-MM", Inv), data);
            var known = months.Values.Where(c => c != null).Select(c => c!.NetHours).ToList();
            var fallbackNet = known.Count > 0 ? known.Last() : 8.0;
            var sets = months.ToDictionary(kv => kv.Key, kv => kv.Value == null ? null : new HashSet<string>(kv.Value.Dates));
            return d =>
            {
                var key = d.ToString("yyyy-MM", Inv);
                if (months.TryGetValue(key, out var info) && info != null)
                    return (sets[key]!.Contains(Iso(d)), info.NetHours);
                return (d.DayOfWeek != DayOfWeek.Sunday, fallbackNet);
            };
        }

        private sealed class LineRow
        {
            public int PlanId, Seq, Line, MachineNo, PartNo; public string PartCode = "", PartName = "", Grade = "", JobCardNo = "";
            public Dictionary<string, double> Planned = new(), Forged = new();

            /// <summary>Forged credited in [from, to]: in the period + ahead of plan before it, capped at planned.</summary>
            public (double Planned, double Credited) Credit(string from, string to)
            {
                var plannedIn = SumBetween(Planned, from, to);
                if (plannedIn <= 0) return (0, 0);
                var ahead = Math.Max(0, SumBefore(Forged, from) - SumBefore(Planned, from));
                var credited = Math.Min(plannedIn, SumBetween(Forged, from, to) + Math.Min(plannedIn, ahead));
                return (plannedIn, credited);
            }
        }

        /// <summary>
        /// MAIN plan lines of counted plans that have a planned day in [a, b],
        /// with every planned day and what their job cards forged, by date.
        /// </summary>
        private async Task<List<LineRow>> PlanLines(int subscID, DateTime a, DateTime b, int machineNo)
        {
            var statuses = string.Join(",", CountedPlanStatuses.Select(s => "'" + s + "'"));
            var plans = (await Query($@"
                SELECT DISTINCT d.MTransNo AS id
                  FROM dbo.ProductionPlanDay d
                  JOIN dbo.ProductionPlanMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.PlanStatus IN ({statuses})
                 WHERE m.SubscID = @SubscID AND d.PlanDate BETWEEN @A AND @B",
                Int("@SubscID", subscID), Dash.PDate("@A", a), Dash.PDate("@B", b))).Select(r => I(r, "id")).ToList();
            if (plans.Count == 0) return new List<LineRow>();
            var ids = string.Join(",", plans);   // integers only

            var lines = (await Query($@"
                SELECT l.MTransNo AS planId, l.JobSeq AS seq, l.LineSlNo AS line, ISNULL(pj.MachineNo, 0) AS machineNo,
                       pj.PartNo AS partNo, ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                       LTRIM(RTRIM(ISNULL(l.Grade, ''))) AS grade, ISNULL(jc.JobCardNo, '') AS jobCardNo
                  FROM dbo.ProductionPlanLine l
                  JOIN dbo.ProductionPlanJob pj ON pj.MTransNo = l.MTransNo AND pj.JobSeq = l.JobSeq AND pj.DeleteStatus = 'N' AND pj.PlanType = 'Main'
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = pj.PartNo
                  OUTER APPLY (SELECT TOP (1) j.JobCardNo FROM dbo.JobCardMst j
                                WHERE j.PlanID = l.MTransNo AND j.PlanJobSeq = l.JobSeq AND j.PlanLineSlNo = l.LineSlNo AND j.DeleteStatus = 'N'
                                ORDER BY j.MTransNo) jc
                 WHERE l.MTransNo IN ({ids}) AND l.DeleteStatus = 'N' AND (@MachineNo = 0 OR pj.MachineNo = @MachineNo)",
                Int("@MachineNo", machineNo)))
                .Select(r => new LineRow
                {
                    PlanId = I(r, "planId"), Seq = I(r, "seq"), Line = I(r, "line"), MachineNo = I(r, "machineNo"), PartNo = I(r, "partNo"),
                    PartCode = S(r, "partCode"), PartName = S(r, "partName"), Grade = S(r, "grade"), JobCardNo = S(r, "jobCardNo"),
                }).ToDictionary(l => (l.PlanId, l.Seq, l.Line));

            foreach (var r in await Query($@"
                SELECT MTransNo AS planId, JobSeq AS seq, LineSlNo AS line, CONVERT(char(10), PlanDate, 23) AS d, SUM(PlanQty) AS q
                  FROM dbo.ProductionPlanDay WHERE MTransNo IN ({ids}) GROUP BY MTransNo, JobSeq, LineSlNo, PlanDate"))
                if (lines.TryGetValue((I(r, "planId"), I(r, "seq"), I(r, "line")), out var l)) l.Planned[S(r, "d")] = (double)D(r, "q");

            foreach (var r in await Query(@"
                SELECT j.PlanID AS planId, j.PlanJobSeq AS seq, j.PlanLineSlNo AS line, CONVERT(char(10), e.EntryDate, 23) AS d, SUM(e.Qty) AS q" +
                Dash.ForgedEntriesFrom + $@" AND j.PlanID IN ({ids})
                 GROUP BY j.PlanID, j.PlanJobSeq, j.PlanLineSlNo, e.EntryDate", Int("@SubscID", subscID)))
                if (lines.TryGetValue((I(r, "planId"), I(r, "seq"), I(r, "line")), out var l)) l.Forged[S(r, "d")] = (double)D(r, "q");

            return lines.Values.ToList();
        }

        /// <summary>Forged pieces by date and machine in [a, b] (all job cards).</summary>
        private Task<List<Dictionary<string, object?>>> ForgedByDay(int subscID, DateTime a, DateTime b) => Query(@"
                SELECT CONVERT(char(10), e.EntryDate, 23) AS d, ISNULL(NULLIF(e.MachineNo, 0), ISNULL(j.MachineNo, 0)) AS machineNo, SUM(e.Qty) AS q" +
                Dash.ForgedEntriesFrom + @" AND e.EntryDate BETWEEN @A AND @B
                 GROUP BY e.EntryDate, ISNULL(NULLIF(e.MachineNo, 0), ISNULL(j.MachineNo, 0))",
            Int("@SubscID", subscID), Dash.PDate("@A", a), Dash.PDate("@B", b));

        // A job card's hammer: the one it was planned on, else the machine that forged most of it.
        private const string JobHammer = @"
              OUTER APPLY (SELECT TOP (1) e.MachineNo FROM dbo.JobCardEntry e
                            WHERE e.MTransNo = j.MTransNo AND e.DeleteStatus = 'N' AND ISNULL(e.MachineNo, 0) > 0
                            GROUP BY e.MachineNo ORDER BY SUM(e.Qty) DESC) fm";
        private const string JobHammerNo = "ISNULL(NULLIF(j.MachineNo, 0), ISNULL(fm.MachineNo, 0))";

        private Task<List<Dictionary<string, object?>>> RejectedByDay(int subscID, DateTime a, DateTime b) => Query(@"
                SELECT CONVERT(char(10), i.InspDate, 23) AS d, " + JobHammerNo + @" AS machineNo, SUM(ISNULL(i.RejectedQty, 0)) AS q
                  FROM dbo.JobCardInspection i
                  JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'" + JobHammer + @"
                 WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND ISNULL(i.RejectedQty, 0) > 0 AND i.InspDate BETWEEN @A AND @B
                 GROUP BY i.InspDate, " + JobHammerNo,
            Int("@SubscID", subscID), Dash.PDate("@A", a), Dash.PDate("@B", b));

        private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
            cells.ToDictionary(c => c.Key, c => c.Value);

        // ================================================================
        // PERIOD
        // ================================================================

        private async Task<Dictionary<string, object?>> BuildPeriod(int subscID, DateTime from, DateTime to, int machineNo, DashTimer t)
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            string fromIso = Iso(from), toIso = Iso(to);
            var allHammers = await t.Time("hammers", () => Hammers(subscID));
            var hammers = allHammers.Where(h => machineNo == 0 || h.MachineNo == machineNo).ToList();
            var hammerNos = hammers.Select(h => h.MachineNo).ToHashSet();
            bool Mine(int m) => machineNo == 0 || m == machineNo;

            // Available hours per hammer: working days up to today × net hours × shifts.
            var day = await t.Time("calendar", () => CalendarDays(subscID, allHammers, from, to));
            var available = hammers.ToDictionary(h => h.MachineNo, _ => 0.0);
            for (var d = from; d <= to && d <= today; d = d.AddDays(1))
            {
                var (working, net) = day(d);
                if (!working) continue;
                foreach (var h in hammers) available[h.MachineNo] += net * Math.Max(0, h.Shifts);
            }

            var forged = (await t.Time("forged", () => ForgedByDay(subscID, from, to))).Where(r => Mine(I(r, "machineNo"))).ToList();
            var rejected = (await t.Time("rejected", () => RejectedByDay(subscID, from, to))).Where(r => Mine(I(r, "machineNo"))).ToList();
            var lines = await t.Time("plan", () => PlanLines(subscID, from, to, machineNo));

            var idle = (await t.Time("idle", () => Query(@"
                SELECT ISNULL(e.MachineNo, 0) AS machineNo, ISNULL(NULLIF(LTRIM(RTRIM(e.IdleReason)), ''), 'Not given') AS reason,
                       SUM(ISNULL(e.IdleMin, 0)) AS minutes, COUNT(*) AS cnt
                  FROM dbo.JobCardEntry e
                  JOIN dbo.JobCardMst j ON j.MTransNo = e.MTransNo AND j.DeleteStatus = 'N'
                 WHERE e.DeleteStatus = 'N' AND j.SubscID = @SubscID AND e.EntryDate BETWEEN @A AND @B
                   AND ISNULL(e.IdleMin, 0) > 0 AND ISNULL(e.IdleReason, '') <> 'Breakdown'
                 GROUP BY ISNULL(e.MachineNo, 0), ISNULL(NULLIF(LTRIM(RTRIM(e.IdleReason)), ''), 'Not given')",
                Int("@SubscID", subscID), Dash.PDate("@A", from), Dash.PDate("@B", to)))).Where(r => hammerNos.Contains(I(r, "machineNo"))).ToList();

            // Run time the entries record on each machine (any step run on it).
            var runMin = (await t.Time("run time", () => Query(@"
                SELECT ISNULL(e.MachineNo, 0) AS machineNo,
                       SUM(CASE WHEN x.span - ISNULL(e.IdleMin, 0) - ISNULL(e.SettingMin, 0) > 0
                                THEN x.span - ISNULL(e.IdleMin, 0) - ISNULL(e.SettingMin, 0) ELSE 0 END) AS runMin
                  FROM dbo.JobCardEntry e
                  JOIN dbo.JobCardMst j ON j.MTransNo = e.MTransNo AND j.DeleteStatus = 'N'
                 CROSS APPLY (SELECT CASE WHEN TRY_CONVERT(time, e.StartTime) IS NULL OR TRY_CONVERT(time, e.EndTime) IS NULL THEN 0
                                          ELSE (DATEDIFF(minute, TRY_CONVERT(time, e.StartTime), TRY_CONVERT(time, e.EndTime)) + 1440) % 1440
                                     END AS span) x
                 WHERE e.DeleteStatus = 'N' AND j.SubscID = @SubscID AND e.EntryDate BETWEEN @A AND @B
                 GROUP BY ISNULL(e.MachineNo, 0)",
                Int("@SubscID", subscID), Dash.PDate("@A", from), Dash.PDate("@B", to))))
                .ToDictionary(r => I(r, "machineNo"), r => (double)D(r, "runMin"));

            var bds = (await t.Time("breakdowns", () => Query(@"
                SELECT b.MachineNo AS machineNo, ISNULL(NULLIF(b.BreakdownType, ''), 'Not set') AS type, b.BreakdownOn AS onAt,
                       b.RepairEnd AS endAt, b.BreakdownStatus AS status
                  FROM dbo.BreakdownMst b
                 WHERE b.SubscID = @SubscID AND b.DeleteStatus = 'N' AND CAST(b.BreakdownOn AS date) BETWEEN @A AND @B",
                Int("@SubscID", subscID), Dash.PDate("@A", from), Dash.PDate("@B", to))))
                .Where(r => hammerNos.Contains(I(r, "machineNo")))
                .Select(r =>
                {
                    var onAt = Dash.Date(r, "onAt") ?? now;
                    var endAt = S(r, "status") == "Closed" ? Dash.Date(r, "endAt") : now;
                    return (Machine: I(r, "machineNo"), Type: S(r, "type"), Hours: endAt == null ? 0 : MaintRules.Hours(onAt, endAt.Value));
                }).ToList();

            // ── per hammer ───────────────────────────────────────────────
            var perHammer = hammers.Select(h =>
            {
                var av = available[h.MachineNo];
                var run = Math.Min(av, (runMin.TryGetValue(h.MachineNo, out var rm) ? rm : 0) / 60.0);
                return new
                {
                    h,
                    forged = forged.Where(r => I(r, "machineNo") == h.MachineNo).Sum(r => (double)D(r, "q")),
                    rejected = rejected.Where(r => I(r, "machineNo") == h.MachineNo).Sum(r => (double)D(r, "q")),
                    planned = lines.Where(l => l.MachineNo == h.MachineNo).Sum(l => SumBetween(l.Planned, fromIso, toIso)),
                    av, run,
                };
            }).ToList();

            var hammerRows = Dash.TopWithOthers(perHammer.Select(x => Row(
                ("machineNo", x.h.MachineNo), ("machineCode", x.h.Code), ("machineName", x.h.Name), ("forgedQty", x.forged),
                ("plannedQty", x.planned), ("availableHrs", Dash.R1(x.av)), ("runHrs", Dash.R1(x.run)), ("utilisationPct", Dash.Pct(x.run, x.av)))),
                r => Dash.Dbl(r, "forgedQty"), rest => Row(
                ("machineNo", 9999), ("machineCode", "Others (" + rest.Count + ")"), ("machineName", rest.Count + " more hammers"),
                ("forgedQty", Dash.SumOf(rest, "forgedQty")), ("plannedQty", Dash.SumOf(rest, "plannedQty")),
                ("availableHrs", Dash.R1(Dash.SumOf(rest, "availableHrs"))), ("runHrs", Dash.R1(Dash.SumOf(rest, "runHrs"))),
                ("utilisationPct", Dash.Pct(Dash.SumOf(rest, "runHrs"), Dash.SumOf(rest, "availableHrs")))));

            var rejectionByHammer = Dash.TopWithOthers(perHammer.Select(x => Row(
                ("machineNo", x.h.MachineNo), ("machineCode", x.h.Code), ("machineName", x.h.Name), ("forgedQty", x.forged),
                ("rejectedQty", x.rejected), ("rejectionPct", Dash.Pct(x.rejected, x.forged)))),
                r => Dash.Dbl(r, "rejectedQty"), rest => Row(
                ("machineNo", 9999), ("machineCode", "Others (" + rest.Count + ")"), ("machineName", rest.Count + " more hammers"),
                ("forgedQty", Dash.SumOf(rest, "forgedQty")), ("rejectedQty", Dash.SumOf(rest, "rejectedQty")),
                ("rejectionPct", Dash.Pct(Dash.SumOf(rest, "rejectedQty"), Dash.SumOf(rest, "forgedQty")))));

            // ── totals and trend ─────────────────────────────────────────
            double totForged = forged.Sum(r => (double)D(r, "q")), totRejected = rejected.Sum(r => (double)D(r, "q"));
            double totPlanned = 0, totCredited = 0;
            foreach (var l in lines) { var (p, c) = l.Credit(fromIso, toIso); totPlanned += p; totCredited += c; }
            double totAvail = perHammer.Sum(x => x.av), totRun = perHammer.Sum(x => x.run);

            var bucket = Dash.BucketOf(from, to);
            var busy = forged.Select(r => S(r, "d")).Concat(lines.SelectMany(l => l.Planned.Keys)).ToHashSet();
            var buckets = Dash.Buckets(bucket, from, to, busy);
            var trend = buckets.Select((bk, i) =>
            {
                var bFrom = Iso(bk.Date);
                var bTo = i + 1 < buckets.Count ? Iso(buckets[i + 1].Date.AddDays(-1)) : Iso(to > today ? today : to);
                if (bucket != "day" && string.CompareOrdinal(bFrom, fromIso) < 0) bFrom = fromIso;
                bool In(string d) => string.CompareOrdinal(d, bFrom) >= 0 && string.CompareOrdinal(d, bTo) <= 0;
                double p = 0, c = 0;
                foreach (var l in lines) { var (lp, lc) = l.Credit(bFrom, bTo); p += lp; c += lc; }
                var b = rejected.Where(r => In(S(r, "d"))).Sum(r => (double)D(r, "q"));
                return new
                {
                    label = bk.Label, plannedQty = p, forgedQty = forged.Where(r => In(S(r, "d"))).Sum(r => (double)D(r, "q")),
                    rejectedQty = b, adherencePct = Dash.Pct(c, p),
                };
            }).ToList();

            // ── rejection reasons and losses ─────────────────────────────
            var reasons = (await t.Time("reasons", () => Query(@"
                SELECT LOWER(LTRIM(RTRIM(ISNULL(i.RejectionReason, '')))) AS reasonKey, MIN(LTRIM(RTRIM(ISNULL(i.RejectionReason, '')))) AS reason,
                       " + JobHammerNo + @" AS machineNo, SUM(i.RejectedQty) AS qty
                  FROM dbo.JobCardInspection i
                  JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'" + JobHammer + @"
                 WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND ISNULL(i.RejectedQty, 0) > 0 AND i.InspDate BETWEEN @A AND @B
                 GROUP BY LOWER(LTRIM(RTRIM(ISNULL(i.RejectionReason, '')))), " + JobHammerNo,
                Int("@SubscID", subscID), Dash.PDate("@A", from), Dash.PDate("@B", to))))
                .Where(r => Mine(I(r, "machineNo")))
                .GroupBy(r => S(r, "reasonKey"))
                .Select(g => new { reason = S(g.First(), "reason") == "" ? "Not given" : S(g.First(), "reason"), qty = g.Sum(r => (double)D(r, "qty")) })
                .OrderByDescending(x => x.qty).Take(100).ToList();

            var losses = idle.GroupBy(r => S(r, "reason"))
                .Select(g => new { reason = g.Key, source = "Idle", minutes = Math.Round(g.Sum(r => (double)D(r, "minutes"))), count = g.Sum(r => I(r, "cnt")) })
                .Concat(bds.GroupBy(b => b.Type).Select(g => new
                {
                    reason = "Breakdown – " + g.Key, source = "Breakdown", minutes = Math.Round(g.Sum(b => b.Hours) * 60), count = g.Count(),
                }))
                .OrderByDescending(x => x.minutes).ToList();

            // ── job cards: open / running now, completed in the period ───
            var js = (await t.Time("job status", () => Query(@"
                SELECT SUM(CASE WHEN s.JobStatus = 'Open' THEN 1 ELSE 0 END) AS openCnt,
                       SUM(CASE WHEN s.JobStatus = 'In Progress' THEN 1 ELSE 0 END) AS running,
                       SUM(CASE WHEN s.JobStatus = 'Completed' AND s.LastEntryDate BETWEEN @A AND @B THEN 1 ELSE 0 END) AS completed
                  FROM dbo.VW_JobCardSummary s
                  JOIN dbo.JobCardMst j ON j.MTransNo = s.JobCardTransNo AND j.DeleteStatus = 'N'
                 WHERE s.SubscID = @SubscID AND (@MachineNo = 0 OR j.MachineNo = @MachineNo)",
                Int("@SubscID", subscID), Dash.PDate("@A", from), Dash.PDate("@B", to), Int("@MachineNo", machineNo)))).FirstOrDefault();

            return new Dictionary<string, object?>
            {
                ["kpi"] = new
                {
                    forgedQty = totForged, plannedQty = totPlanned, adherencePct = Dash.Pct(totCredited, totPlanned),
                    rejectedQty = totRejected, rejectionPct = Dash.Pct(totRejected, totForged),
                    utilisationPct = Dash.Pct(totRun, totAvail), runHrs = Dash.R1(totRun), availableHrs = Dash.R1(totAvail),
                },
                ["trendBucket"] = bucket,
                ["trend"] = trend,
                ["hammers"] = hammerRows,
                ["rejectionByHammer"] = rejectionByHammer,
                ["rejectionReasons"] = reasons,
                ["losses"] = losses,
                ["jobStatus"] = new { open = I(js, "openCnt"), inProgress = I(js, "running"), completed = I(js, "completed") },
                ["machines"] = allHammers.Select(h => new { machineNo = h.MachineNo, machineCode = h.Code, machineName = h.Name }).ToList(),
            };
        }

        // ================================================================
        // NOW
        // ================================================================

        private async Task<Dictionary<string, object?>> BuildCurrent(int subscID, int machineNo, DashTimer t)
        {
            var today = DateTime.Today;
            var allHammers = await t.Time("hammers", () => Hammers(subscID));
            bool Mine(int m) => machineNo == 0 || m == machineNo;
            var codeOf = allHammers.ToDictionary(h => h.MachineNo, h => h.Code);

            // Yesterday / tomorrow = the last / next working day of the Work Calendar.
            var day = await t.Time("calendar", () => CalendarDays(subscID, allHammers, today.AddDays(-20), today.AddDays(20)));
            DateTime Step(DateTime d, int dir) { for (var i = 1; i <= 20; i++) { var x = d.AddDays(dir * i); if (day(x).Working) return x; } return d.AddDays(dir); }
            DateTime yd = Step(today, -1), tm = Step(today, 1);
            string ydIso = Iso(yd), tdIso = Iso(today), tmIso = Iso(tm);

            var lines = await t.Time("plan", () => PlanLines(subscID, yd, tm, machineNo));
            var forged = (await t.Time("forged", () => ForgedByDay(subscID, yd, today))).Where(r => Mine(I(r, "machineNo"))).ToList();
            var rejected = (await t.Time("rejected", () => RejectedByDay(subscID, yd, yd))).Where(r => Mine(I(r, "machineNo"))).ToList();

            double PlannedOn(string d) => lines.Sum(l => l.Planned.TryGetValue(d, out var q) ? q : 0);
            List<LineRow> LinesOn(string d) => lines.Where(l => l.Planned.TryGetValue(d, out var q) && q > 0).ToList();
            double yCredited = lines.Sum(l => l.Credit(ydIso, ydIso).Credited), yPlanned = PlannedOn(ydIso);

            var planRows = lines.Where(l => (l.Planned.TryGetValue(tdIso, out var a) && a > 0) || (l.Planned.TryGetValue(tmIso, out var b) && b > 0))
                .GroupBy(l => (l.MachineNo, l.PartNo, l.Grade, l.JobCardNo))
                .Select(g => new
                {
                    machineNo = g.Key.MachineNo, machineCode = codeOf.TryGetValue(g.Key.MachineNo, out var c) ? c : "",
                    partCode = g.First().PartCode, partName = g.First().PartName, grade = g.Key.Grade,
                    jobCardNo = g.Key.JobCardNo == "" ? "Not released" : g.Key.JobCardNo,
                    todayQty = g.Sum(l => l.Planned.TryGetValue(tdIso, out var q) ? q : 0),
                    tomorrowQty = g.Sum(l => l.Planned.TryGetValue(tmIso, out var q) ? q : 0),
                })
                .OrderBy(x => x.machineCode, StringComparer.Ordinal).ThenBy(x => x.partCode, StringComparer.Ordinal).ToList();

            // ── hammer load of this month's plan ─────────────────────────
            var month = today.ToString("yyyy-MM", Inv);
            var plan = (await t.Time("month plan", () => LoadPlans(subscID)))
                .Where(p => p.PlanMonth == month && p.PlanStatus != "Closed")
                .OrderBy(p => p.PlanStatus == "Draft" ? 1 : 0).ThenByDescending(p => p.MTransNo).FirstOrDefault();
            var hammerLoad = new List<object>();
            if (plan != null)
            {
                var pd = new PlanningData { SubscID = subscID };
                pd.Hammers.AddRange(allHammers);
                var built = BuildPlan(plan, await CalendarOf(month, pd));
                hammerLoad = built.Lanes.Where(kv => Mine(kv.Key)).OrderByDescending(kv => kv.Value.LoadPct).Select(kv => (object)new
                {
                    machineNo = kv.Key, machineCode = codeOf.TryGetValue(kv.Key, out var c) ? c : "",
                    machineName = pd.HammerName(kv.Key), usedDays = kv.Value.UsedDays, workDays = kv.Value.WorkDays,
                    idleDays = kv.Value.IdleDays, loadPct = kv.Value.LoadPct,
                }).ToList();
            }

            var wip = await t.Time("wip", () => Query(Dash.WipByOperationSql, Int("@SubscID", subscID), Int("@MachineNo", machineNo)));

            var behind = await t.Time("behind", () => Query(@"
                SELECT j.JobCardNo AS jobCardNo, ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                       ISNULL(mc.MachineCode, '') AS machineCode, CONVERT(char(10), j.PlannedEnd, 23) AS plannedEnd,
                       ISNULL(s.CompletedQty, 0) AS doneQty, ISNULL(j.Qty, 0) AS qty, DATEDIFF(day, j.PlannedEnd, @Today) AS daysOver,
                       ISNULL(s.CurrentStep, '') AS currentStep
                  FROM dbo.JobCardMst j
                  JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo
                  LEFT JOIN dbo.MachineMst mc ON mc.MTransNo = j.MachineNo
                 WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N' AND j.PlannedEnd < @Today
                   AND ISNULL(s.JobStatus, '') <> 'Completed' AND (@MachineNo = 0 OR j.MachineNo = @MachineNo)
                 ORDER BY j.PlannedEnd, j.JobCardNo",
                Int("@SubscID", subscID), Dash.PDate("@Today", today), Int("@MachineNo", machineNo)));

            // ── orders at risk: the Pending Order Report's rows ──────────
            var risk = await t.Time("orders at risk", async () =>
            {
                var data = await LoadPlanningData(subscID);
                var plans = await LoadPlans(subscID);
                var dates = await PlannedDatesByOa(plans);
                var mat = await MaterialStatusMap(data, plans);
                var soon = Iso(today.AddDays(RiskWindowDays));
                var rows = new List<(int? Late, object Row)>();
                foreach (var o in data.OALines.Values)
                {
                    var pending = PendingOf(data, o);
                    if (pending <= 0) continue;
                    if (machineNo > 0 && (data.RouteFor(o.PartNo, o.Grade)?.HammerNo ?? 0) != machineNo) continue;
                    var forgeBy = ForgeBy(o.PlanDate, data.PostForgeDaysOf(o.PartNo, o.Grade));
                    if (forgeBy == "") continue;
                    var planned = dates.TryGetValue(o.Key, out var d) ? d : ((string From, string To)?)null;
                    int? late = planned == null ? null : (int)(ParseIso(planned.Value.To) - ParseIso(forgeBy)).TotalDays;
                    var atRisk = planned != null ? late > 0 : string.CompareOrdinal(forgeBy, soon) <= 0;
                    if (!atRisk) continue;
                    rows.Add((late, new
                    {
                        oaYear = o.OAYear, oaNo = o.OANo, oaId = o.OAID, customer = o.Customer, partCode = data.PartCode(o.PartNo),
                        partName = data.PartName(o.PartNo), pendingQty = pending, forgeBy, plannedFinish = planned?.To ?? "",
                        daysLate = late, unplanned = planned == null, materialStatus = mat.TryGetValue(o.Key, out var ms) ? ms : "",
                    }));
                }
                // Unplanned first (nothing is booked to make them), then the latest.
                return rows.OrderByDescending(x => x.Late ?? int.MaxValue).Select(x => x.Row).ToList();
            });

            var totals = new Dictionary<string, int>();
            return new Dictionary<string, object?>
            {
                ["day"] = new
                {
                    yesterday = new
                    {
                        date = ydIso, plannedQty = yPlanned, forgedQty = forged.Where(r => S(r, "d") == ydIso).Sum(r => (double)D(r, "q")),
                        rejectedQty = rejected.Sum(r => (double)D(r, "q")), adherencePct = Dash.Pct(yCredited, yPlanned),
                    },
                    today = new
                    {
                        date = tdIso, plannedQty = PlannedOn(tdIso), forgedSoFar = forged.Where(r => S(r, "d") == tdIso).Sum(r => (double)D(r, "q")),
                        hammers = LinesOn(tdIso).Select(l => l.MachineNo).Distinct().Count(), jobs = LinesOn(tdIso).Count,
                    },
                    tomorrow = new
                    {
                        date = tmIso, plannedQty = PlannedOn(tmIso),
                        hammers = LinesOn(tmIso).Select(l => l.MachineNo).Distinct().Count(), jobs = LinesOn(tmIso).Count,
                    },
                },
                ["planRows"] = Dash.Limit(totals, "planRows", planRows),
                ["hammerLoad"] = Dash.Limit(totals, "hammerLoad", hammerLoad),
                ["wipByOperation"] = wip.Select(r => new { operation = S(r, "operation"), jobCards = I(r, "jobCards"), qty = (double)D(r, "qty") }).ToList(),
                ["jobsBehind"] = Dash.Limit(totals, "jobsBehind", behind.Select(r => new
                {
                    jobCardNo = S(r, "jobCardNo"), partCode = S(r, "partCode"), partName = S(r, "partName"), machineCode = S(r, "machineCode"),
                    plannedEnd = S(r, "plannedEnd"), doneQty = (double)D(r, "doneQty"), qty = (double)D(r, "qty"),
                    daysOver = I(r, "daysOver"), currentStep = S(r, "currentStep"),
                }).OrderByDescending(x => x.daysOver)),
                ["ordersAtRisk"] = Dash.Limit(totals, "ordersAtRisk", risk),
                ["listTotals"] = totals,
            };
        }
    }
}
