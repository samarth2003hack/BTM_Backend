using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Maintenance reports — EMSSoft/Modules/MaintenanceReports/
    /// MachineMaintenanceReport.html and MachineHistoryCard.html.
    ///
    /// Report types (AllReports ?type=):
    ///   compliance  one row per machine per month; a covered machine with no
    ///               sheet for a month is "Not Started" (not before go-live =
    ///               the first sheet ever, nor before the machine was created)
    ///   due         monthly / half-yearly / yearly points due by the end of
    ///               the To month — never done and overdue first
    ///   breakdown   breakdowns reported in the period
    ///   history     PM sheets and breakdowns on one timeline
    /// Summary: avg compliance, overdue points, open breakdowns, downtime,
    /// MTTR and MTBF (available hours = days × 3 shifts × 8 h × machines).
    /// From / To default to the 1st of last month / today.
    /// </summary>
    [Route("api/MaintenanceReport")]
    public class MaintenanceReportController : MaintenanceControllerBase
    {
        public MaintenanceReportController(EMSContext context) : base(context) { }

        private static readonly string[] Types = { "compliance", "due", "breakdown", "history" };
        private static readonly string[] EventTypes = { "", "sheet", "nok", "periodic", "breakdown" };

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports(
            [FromQuery] int SubscID, [FromQuery] string? type, [FromQuery] int machineNo,
            [FromQuery] string? from, [FromQuery] string? to)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var t = MaintRules.Text(type).ToLowerInvariant();
            if (t == "") t = "compliance";
            if (!Types.Contains(t)) return BadRequestResult("Unknown report type.");
            var f = MaintenanceData.ReportFilters(from, to, Math.Max(0, machineNo));
            if (f.From > f.To) return ConflictResult("From Date cannot be after To Date.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, SubscID);
                object rows = t switch
                {
                    "due" => data.DueRows(f),
                    "breakdown" => data.BreakdownRows(f),
                    "history" => data.HistoryRows(f),
                    _ => data.ComplianceRows(f)
                };
                return OkResult(rows, ((System.Collections.ICollection)rows).Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Summary")]
        public async Task<IActionResult> Summary(
            [FromQuery] int SubscID, [FromQuery] int machineNo, [FromQuery] string? from, [FromQuery] string? to)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var f = MaintenanceData.ReportFilters(from, to, Math.Max(0, machineNo));
            if (f.From > f.To) return ConflictResult("From Date cannot be after To Date.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, SubscID);
                return OkResult(data.Kpis(f));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One machine's history card: { machine, summary, rows }.
        /// EventType: "" (all) | sheet | nok | periodic | breakdown.
        /// </summary>
        [HttpGet("MachineHistoryCard")]
        public async Task<IActionResult> MachineHistoryCard(
            [FromQuery] int SubscID, [FromQuery] int MachineNo, [FromQuery] string? EventType,
            [FromQuery] string? FromDate, [FromQuery] string? ToDate)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var ev = MaintRules.Text(EventType).ToLowerInvariant();
            if (!EventTypes.Contains(ev)) return BadRequestResult("Unknown event type.");
            var f = MaintenanceData.ReportFilters(FromDate, ToDate, MachineNo);
            if (f.From > f.To) return ConflictResult("From Date cannot be after To Date.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, SubscID);
                var mc = data.LiveMachine(MachineNo);
                if (mc == null) return ConflictResult("Select the machine.");
                return OkResult(data.HistoryCard(mc, f, ev));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
