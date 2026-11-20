using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EMSAPI.Models.Dash;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Maintenance Dashboard — EMSSoft/Modules/Maintenance/MaintenanceDashboard.html.
    ///
    ///     GET api/MaintenanceDashboard/GetSummary?FromDate=&amp;ToDate=&amp;SubscID=&amp;Refresh=
    ///
    /// Every figure is the one the maintenance module already computes
    /// (Models/MaintenanceEngine.cs — the Maintenance Report and history card
    /// use the same code): downtime = BreakdownOn → RepairEnd (or now while
    /// open) for breakdowns that started in the period; MTTR / MTBF / PM
    /// compliance = MaintenanceReport/Summary; due points = MaintenanceReport
    /// ?type=due. The machine board is "now": Breakdown = an open breakdown,
    /// Running = a job card entry today on the machine, Idle = neither (the
    /// software stores no running flag, so that is the evidence it has).
    /// Answers are cached for a few minutes (Models/DashboardSupport.cs).
    /// </summary>
    [Route("api/MaintenanceDashboard")]
    public class MaintenanceDashboardController : MaintenanceControllerBase
    {
        private readonly DashboardCache cache;

        public MaintenanceDashboardController(EMSContext context, DashboardCache cache) : base(context) => this.cache = cache;

        private const int PmListDays = 45;   // the "upcoming" list: overdue + due within 45 days
        private const int PmKpiDays = 30;    // the KPI: overdue + due within 30 days

        [HttpGet("GetSummary")]
        public async Task<IActionResult> GetSummary([FromQuery] string? FromDate, [FromQuery] string? ToDate,
            [FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var bad = Range(FromDate, ToDate, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var data = await cache.GetOrBuild("Maintenance", DashboardCache.KeyOf(SubscID, Iso(from), Iso(to)), Refresh,
                    t => Build(SubscID, from, to, t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private async Task<Dictionary<string, object?>> Build(int subscID, DateTime from, DateTime to, DashTimer t)
        {
            var today = MaintRules.Today;
            var md = await t.Time("load", () => MaintenanceData.LoadAsync(db, subscID));
            var f = new MaintenanceData.Filters(from, to, 0);
            var con = db.Database.GetDbConnection();

            // ── machine board (now) ──────────────────────────────────────
            var active = md.Machines.Where(m => !m.Deleted && m.Lock != "N").ToList();
            var todayRows = await t.Time("today entries", () => Rows(con, @"
                SELECT x.machineNo, x.jobCardNo, x.part
                  FROM (SELECT e.MachineNo AS machineNo, ISNULL(j.JobCardNo, '') AS jobCardNo,
                               CONCAT(ISNULL(p.PartCode, ''), ' — ', ISNULL(p.PartName, '')) AS part,
                               ROW_NUMBER() OVER (PARTITION BY e.MachineNo ORDER BY ISNULL(e.EndTime, e.StartTime) DESC, e.TransSlNo DESC) AS rn
                          FROM dbo.JobCardEntry e
                          JOIN dbo.JobCardMst j ON j.MTransNo = e.MTransNo AND j.DeleteStatus = 'N' AND j.SubscID = @SubscID
                          LEFT JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo
                         WHERE e.DeleteStatus = 'N' AND e.EntryDate = @Today AND ISNULL(e.MachineNo, 0) > 0) x
                 WHERE x.rn = 1", PInt("@SubscID", subscID), PDate("@Today", today)));
            var runningToday = todayRows.ToDictionary(r => Int(r, "machineNo"));

            var openBds = md.Breakdowns.Where(b => b.Status != "Closed").Select(md.DecorateBreakdown).ToList();
            var openByMachine = openBds.GroupBy(b => b.MachineNo).ToDictionary(g => g.Key, g => g.OrderBy(b => b.BreakdownOn, StringComparer.Ordinal).First());

            var board = active.Select(m =>
            {
                var isHammer = md.OperationNames(m).Any(n => n.Contains("forg", StringComparison.OrdinalIgnoreCase));
                openByMachine.TryGetValue(m.No, out var bd);
                runningToday.TryGetValue(m.No, out var run);
                var status = bd != null ? "Breakdown" : run != null ? "Running" : "Idle";
                return new Dictionary<string, object?>
                {
                    ["machineNo"] = m.No, ["machineCode"] = m.Code, ["machineName"] = m.Name,
                    ["kind"] = isHammer ? "Hammer" : "Other", ["status"] = status,
                    ["jobCardNo"] = run != null ? Str(run, "jobCardNo") : "", ["part"] = run != null ? Str(run, "part") : "",
                    ["breakdownNo"] = bd?.BreakdownNo ?? "", ["since"] = bd?.BreakdownOn ?? "",
                };
            }).ToList();
            var order = new Dictionary<string, int> { ["Breakdown"] = 0, ["Running"] = 1, ["Idle"] = 2 };
            board = board.OrderBy(b => order[(string)b["status"]!]).ThenBy(b => (string)b["machineCode"]!, StringComparer.Ordinal).ToList();
            int running = board.Count(b => (string)b["status"]! == "Running"), broken = board.Count(b => (string)b["status"]! == "Breakdown");

            // ── breakdowns of the period ─────────────────────────────────
            var bds = t.Time("breakdowns", () => md.BreakdownRows(f));
            var kpis = t.Time("reliability", () => md.Kpis(f));

            var byMachine = TopWithOthers(bds.GroupBy(b => b.MachineNo).Select(g => new Dictionary<string, object?>
            {
                ["machineCode"] = g.First().MachineCode, ["machineName"] = g.First().MachineName,
                ["hours"] = R2(g.Sum(b => b.DowntimeHrs)), ["count"] = g.Count(),
            }), r => Dbl(r, "hours"), rest => new Dictionary<string, object?>
            {
                ["machineCode"] = "Others (" + rest.Count + ")", ["machineName"] = rest.Count + " more machines",
                ["hours"] = R2(SumOf(rest, "hours")), ["count"] = (int)SumOf(rest, "count"),
            });
            var byType = bds.GroupBy(b => string.IsNullOrEmpty(b.BreakdownType) ? "Not set" : b.BreakdownType)
                .Select(g => new { type = g.Key, hours = R2(g.Sum(b => b.DowntimeHrs)), count = g.Count() })
                .OrderByDescending(x => x.hours).ToList();

            var bucket = BucketOf(from, to);
            var busy = bds.Select(b => b.BreakdownDate).ToHashSet();
            var trend = Buckets(bucket, from, to, busy).Select(bk =>
            {
                var mine = bds.Where(b => ParseDate(b.BreakdownDate) is DateTime d && BucketKey(bucket, d).Key == bk.Key).ToList();
                return new { label = bk.Label, hours = R1(mine.Sum(b => b.DowntimeHrs)), count = mine.Count };
            }).ToList();

            // ── preventive maintenance due ───────────────────────────────
            var due = t.Time("pm due", () => md.DueRows(f with { From = today, To = today.AddDays(PmListDays) }));
            bool Within(DueRow r, int days) =>
                r.DueStatus != "Due" || (ParseDate(r.NextDue) is DateTime d && d <= today.AddDays(days));
            var upcoming = due.Where(r => Within(r, PmListDays))
                .OrderBy(r => r.NextDue == "" ? 0 : 1).ThenBy(r => r.NextDue, StringComparer.Ordinal)
                .ThenBy(r => r.MachineCode, StringComparer.Ordinal).ToList();

            var totals = new Dictionary<string, int>();
            return new Dictionary<string, object?>
            {
                ["kpi"] = new
                {
                    activeMachines = active.Count, running, underBreakdown = broken, idle = active.Count - running - broken,
                    downtimeHrs = R2(bds.Sum(b => b.DowntimeHrs)), breakdownCount = bds.Count,
                    pmDue = due.Count(r => Within(r, PmKpiDays)), pmOverdue = due.Count(r => r.DueStatus != "Due"),
                },
                ["machineStatus"] = Limit(totals, "machineStatus", board),
                ["openBreakdowns"] = Limit(totals, "openBreakdowns", openBds.OrderByDescending(b => b.DowntimeHrs).Select(b => new
                {
                    no = b.BreakdownNo, machineCode = b.MachineCode, machineName = b.MachineName, type = b.BreakdownType,
                    problem = b.Problem, breakdownOn = b.BreakdownOn, status = b.BreakdownStatus, hoursDown = b.DowntimeHrs,
                })),
                ["downtimeByMachine"] = byMachine,
                ["downtimeByType"] = byType,
                ["trendBucket"] = bucket,
                ["trend"] = trend,
                ["reliability"] = new
                {
                    breakdownCount = kpis.BreakdownCount, downtimeHrs = kpis.DowntimeHrs, mttrHrs = kpis.MttrHrs,
                    mtbfHrs = kpis.MtbfHrs, pmCompliancePct = kpis.AvgCompliance,
                },
                ["upcomingPm"] = Limit(totals, "upcomingPm", upcoming),
                ["listTotals"] = totals,
            };
        }
    }
}
