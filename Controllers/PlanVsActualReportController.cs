using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// PurchaseReports/PlanVsActualReport.html — what a production plan said
    /// against what the shop forged (JobCardEntry at the Forging step) and
    /// rejected (JobCardInspection), per plan line, for a month, week or day.
    ///
    ///   GET AllReports?PlanID=&Hammer=&Period=Month|Week|Day&WeekNo=&Date=&SubscID=
    ///   → one row per MAIN plan line with: plannedQty, forgedQty, carryInQty
    ///     (Week / Day: owed when the period began), planned / actual start and
    ///     end, rejectedQty, forgeBy, deliveryDate. Facts only — the page works
    ///     out balance, adherence, status and on-time.
    /// </summary>
    [Route("api/[controller]")]
    public class PlanVsActualReportController : PlanningControllerBase
    {
        public PlanVsActualReportController(EMSContext context) : base(context) { }

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports([FromQuery] int? PlanID, [FromQuery] string? Hammer, [FromQuery] string? Period,
            [FromQuery] int? WeekNo, [FromQuery] string? Date, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var plan = PlanID > 0 ? (await LoadPlans(SubscID, PlanID ?? 0)).FirstOrDefault() : null;
                if (plan == null) return OkResult(new List<object>(), "No records found.");
                var data = await LoadPlanningData(SubscID);
                var info = await CalendarOf(plan.PlanMonth, data);
                var period = string.IsNullOrWhiteSpace(Period) ? "Month" : Period;
                var (from, to) = RangeOf(plan, info, period, WeekNo ?? 0, (Date ?? "").Split('T')[0]);
                var hammer = string.IsNullOrWhiteSpace(Hammer) || Hammer == "All" ? "" : Hammer.Trim();
                var rows = new List<object>();
                foreach (var x in await LineFacts(plan, data))
                {
                    if (hammer != "" && x.Hammer != hammer) continue;
                    var plannedDates = x.Planned.Keys.Where(d => In(d, from, to)).OrderBy(d => d, StringComparer.Ordinal).ToList();
                    var forgedDates = x.Forged.Keys.Where(d => In(d, from, to)).OrderBy(d => d, StringComparer.Ordinal).ToList();
                    var planned = SumBetween(x.Planned, from, to);
                    var forged = SumBetween(x.Forged, from, to);
                    var carryIn = (period == "Week" || period == "Day") && string.CompareOrdinal(from, Today) <= 0
                        ? Math.Max(0, SumBefore(x.Planned, from) - SumBefore(x.Forged, from)) : 0;
                    if (planned == 0 && forged == 0 && carryIn == 0) continue;
                    rows.Add(new
                    {
                        carryInQty = carryIn, seq = x.Seq, partName = x.PartName, hammer = x.Hammer,
                        plannedStart = plannedDates.FirstOrDefault() ?? "", plannedEnd = plannedDates.LastOrDefault() ?? "",
                        cutWt = x.CutWt, lineNo = x.LineNo, oaNo = x.OANo,
                        customerName = x.Source == "OA" ? x.Customer : "For stock", grade = x.Grade,
                        plannedQty = planned, rate = x.Rate, jobCardNo = x.JobCardNo, forgedQty = forged,
                        actualStart = forgedDates.FirstOrDefault() ?? "", actualEnd = forgedDates.LastOrDefault() ?? "",
                        shortClosed = x.ShortClosed, remarks = x.Remarks,
                        rejectedQty = SumBetween(x.Rejected, from, to), forgeBy = x.ForgeBy, deliveryDate = x.DeliveryDate,
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private static bool In(string d, string from, string to) =>
            string.CompareOrdinal(d, from) >= 0 && string.CompareOrdinal(d, to) <= 0;
    }
}
