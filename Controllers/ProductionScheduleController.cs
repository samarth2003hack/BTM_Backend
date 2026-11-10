using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Production/ProductionSchedule.html — the daily production sheet and the
    /// weekly plan, per hammer, read off a production plan's stored days (SQL 59).
    ///
    ///   GET GetSchedule?PlanID=&Period=Day|Week&Date=&WeekNo=&Hammer=&SubscID=
    ///   → { plan, period, from, to, label, dates[], rows[] }
    ///   Day  → one row per plan line due that day or still owed from before:
    ///          plannedQty, carryOverQty (planned before less forged before),
    ///          toForgeQty, forgedQty, balanceQty, rmKg.
    ///   Week → one row per plan line touching the week, with `days` { date: qty }.
    ///   Carry-over is only owed by a day that has come; Hammer is "All" or the
    ///   hammer's Machine Master name.
    /// </summary>
    [Route("api/[controller]")]
    public class ProductionScheduleController : PlanningControllerBase
    {
        public ProductionScheduleController(EMSContext context) : base(context) { }

        [HttpGet("GetSchedule")]
        public async Task<IActionResult> GetSchedule([FromQuery] int? PlanID, [FromQuery] string? Period, [FromQuery] string? Date,
            [FromQuery] int? WeekNo, [FromQuery] string? Hammer, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var plan = (await LoadPlans(SubscID, PlanID ?? 0)).FirstOrDefault();
                if (plan == null || PlanID <= 0) return OkResult(new { dates = new List<string>(), rows = new List<object>() }, "No records found.");
                var data = await LoadPlanningData(SubscID);
                var info = await CalendarOf(plan.PlanMonth, data);
                var periods = PeriodsOf(plan, info);
                var facts = await LineFacts(plan, data);
                var hammer = string.IsNullOrWhiteSpace(Hammer) || Hammer == "All" ? "" : Hammer.Trim();
                var rows = new List<Dictionary<string, object?>>();

                if (Period == "Week")
                {
                    var w = info?.Weeks.FirstOrDefault(x => x.WeekNo == (WeekNo ?? 0));
                    if (w == null) return OkResult(new { dates = new List<string>(), rows }, "No records found.");
                    var weekStatus = plan.Weeks.TryGetValue(w.WeekNo, out var ws) ? ws : "Open";
                    foreach (var x in facts)
                    {
                        if (hammer != "" && x.Hammer != hammer) continue;
                        var planned = SumBetween(x.Planned, w.From, w.To);
                        var carryIn = string.CompareOrdinal(w.From, Today) > 0 ? 0 : Math.Max(0, SumBefore(x.Planned, w.From) - SumBefore(x.Forged, w.From));
                        if (planned == 0 && carryIn == 0) continue;
                        var forged = SumBetween(x.Forged, w.From, w.To);
                        // Forged ahead of plan (before the week) comes off the sheet.
                        var (_, early) = ForgedCredit(x, w.From, w.To);
                        var toForge = Math.Max(0, planned + carryIn - early);
                        var row = Pick(x);
                        row["weekNo"] = w.WeekNo;
                        row["weekStatus"] = weekStatus;
                        row["days"] = w.Dates.Where(d => x.Planned.ContainsKey(d)).ToDictionary(d => d, d => x.Planned[d]);
                        row["plannedQty"] = planned;
                        row["carryOverQty"] = carryIn;
                        row["forgedEarlyQty"] = early;
                        row["toForgeQty"] = toForge;
                        row["forgedQty"] = forged;
                        row["balanceQty"] = Math.Max(0, toForge - forged);
                        row["rmKg"] = R2(toForge * x.CutWt);
                        rows.Add(row);
                    }
                    return OkResult(new { plan = periods, period = "Week", from = w.From, to = w.To, label = w.Label, weekStatus,
                        dates = w.Dates, rows }, rows.Count > 0 ? "Data found." : "No records found.");
                }

                var date = (Date ?? "").Split('T')[0];
                var weekNo = info?.WeekOfDate(date) ?? 0;
                foreach (var x in facts)
                {
                    if (hammer != "" && x.Hammer != hammer) continue;
                    var planned = x.Planned.TryGetValue(date, out var pq) ? pq : 0;
                    var carry = string.CompareOrdinal(date, Today) > 0 ? 0 : Math.Max(0, SumBefore(x.Planned, date) - SumBefore(x.Forged, date));
                    if (planned == 0 && carry == 0) continue;
                    var forged = x.Forged.TryGetValue(date, out var fq) ? fq : 0;
                    // Forged ahead of plan (before the day) comes off the sheet.
                    var (_, early) = ForgedCredit(x, date, date);
                    var toForge = Math.Max(0, planned + carry - early);
                    var row = Pick(x);
                    row["date"] = date;
                    row["weekNo"] = weekNo;
                    row["weekStatus"] = weekNo > 0 ? (plan.Weeks.TryGetValue(weekNo, out var st) ? st : "Open") : "";
                    row["plannedQty"] = planned;
                    row["carryOverQty"] = carry;
                    row["forgedEarlyQty"] = early;
                    row["toForgeQty"] = toForge;
                    row["forgedQty"] = forged;
                    row["balanceQty"] = Math.Max(0, toForge - forged);
                    row["rmKg"] = R2(toForge * x.CutWt);
                    rows.Add(row);
                }
                return OkResult(new { plan = periods, period = "Day", from = date, to = date, label = date,
                    working = info?.Dates.Contains(date) ?? false, dates = new[] { date }, rows },
                    rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private static Dictionary<string, object?> Pick(LineFact x) => new()
        {
            ["seq"] = x.Seq, ["lineNo"] = x.LineNo, ["partName"] = x.PartName, ["dieName"] = x.DieName, ["hammer"] = x.Hammer,
            ["source"] = x.Source, ["oaNo"] = x.OANo, ["oaText"] = x.OAText, ["customer"] = x.Customer, ["grade"] = x.Grade,
            ["barText"] = x.BarText, ["deliveryDate"] = x.DeliveryDate, ["cutWt"] = x.CutWt, ["rate"] = x.Rate,
            ["routeName"] = x.RouteName, ["jobCardNo"] = x.JobCardNo, ["jobStart"] = x.JobStart, ["jobEnd"] = x.JobEnd,
        };
    }
}
