using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using static EMSAPI.Models.PlanScheduleEngine;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// SalesReports/PendingOrderReport.html — every OA line still to forge, the
    /// live production plan that holds it, the dates it is planned on, and
    /// whether that is late against the delivery date and the forge-by date
    /// (delivery − the route's post-forging days − 1 dispatch day).
    ///
    ///   GET AllReports?Customer=&RMGrade=&MaterialStatus=&FromDate=&ToDate=&SubscID=
    ///   (FromDate / ToDate filter the OA date; RMGrade filters the line's grade.)
    /// </summary>
    [Route("api/[controller]")]
    public class PendingOrderReportController : PlanningControllerBase
    {
        public PendingOrderReportController(EMSContext context) : base(context) { }

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports([FromQuery] string? Customer, [FromQuery] string? RMGrade,
            [FromQuery] string? MaterialStatus, [FromQuery] string? FromDate, [FromQuery] string? ToDate, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(SubscID);
                var plans = await LoadPlans(SubscID);
                var held = PlannedByOa(plans, data, 0);
                var mat = await MaterialStatusMap(data, plans);
                var dates = await PlannedDatesByOa(plans);
                var rows = new List<object>();
                foreach (var o in data.OALines.Values
                             .OrderBy(x => x.OADate, StringComparer.Ordinal).ThenBy(x => x.OAYear).ThenBy(x => x.OANo).ThenBy(x => x.OAID))
                {
                    var pending = PendingOf(data, o);
                    if (pending <= 0) continue;
                    if (!string.IsNullOrWhiteSpace(FromDate) && string.CompareOrdinal(o.OADate, FromDate) < 0) continue;
                    if (!string.IsNullOrWhiteSpace(ToDate) && string.CompareOrdinal(o.OADate, ToDate) > 0) continue;
                    if (!string.IsNullOrWhiteSpace(Customer) && !string.Equals(o.Customer, Customer.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(RMGrade) && !string.Equals(o.Grade, RMGrade.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    var status = mat.TryGetValue(o.Key, out var ms) ? ms : "";
                    if (!string.IsNullOrWhiteSpace(MaterialStatus) && MaterialStatus != "All" && status != MaterialStatus) continue;

                    var bar = data.BarOf(o.PartNo, o.Grade);
                    var planned = Math.Min(pending, held.TryGetValue(o.Key, out var h) ? h.Qty : 0);
                    var d = dates.TryGetValue(o.Key, out var pd) ? pd : ((string From, string To)?)null;
                    var forgeBy = ForgeBy(o.PlanDate, data.PostForgeDaysOf(o.PartNo, o.Grade));
                    rows.Add(new
                    {
                        oaNo = o.Label, oaId = o.OAID, oaDate = o.OADate, customer = o.Customer, poNo = o.PoNo, poDate = o.PoDate,
                        partName = data.PartName(o.PartNo), grade = o.Grade, deliveryDate = o.DeliveryDate, committedDate = o.CommittedDate,
                        priority = o.Priority, oaQty = o.Qty, dispatchedQty = o.Dispatched, forgedQty = GoodForged(data, o),
                        pendingQty = pending, plannedQty = planned, unplannedQty = pending - planned, rate = o.Rate,
                        pendingValue = R2(pending * o.Rate), cutWt = bar.CutWt, pendingWt = R2(pending * bar.CutWt), barDia = bar.Dia,
                        orderType = o.CustomerMaterial ? "Job Work" : "Regular", materialStatus = status,
                        planNo = h.Plans == null ? "" : string.Join(", ", h.Plans),
                        plannedStart = d?.From ?? "", plannedEnd = d?.To ?? "",
                        lateVsDelivery = d == null ? "" : string.CompareOrdinal(d.Value.To, o.PlanDate) > 0 ? "Late" : "On time",
                        forgeBy,
                        lateVsForgeBy = d == null || forgeBy == "" ? "" : string.CompareOrdinal(d.Value.To, forgeBy) > 0 ? "Late" : "On time",
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
