using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Breakdown Maintenance — EMSSoft/Modules/Maintenance/BreakdownMaintenance.html
    /// (and Production/JobCardStatus.html via GetOpenByMachine).
    ///
    /// Rules (SQL/55_Maintenance.sql):
    ///   - One live (Open / In Progress) breakdown per machine.
    ///   - In Progress / Closed need who attended and the repair start;
    ///     Closed also needs the repair end, root cause and action taken.
    ///   - No time may be in the future; the repair cannot start before the
    ///     breakdown nor end before it started.
    ///   - Only an Open breakdown nobody has attended can be deleted.
    ///   - Downtime runs from the breakdown to the repair end (to now while open).
    ///   - Numbered BD-&lt;FY&gt;-0001 by the breakdown's financial year.
    /// </summary>
    [Route("api/Breakdown")]
    public class BreakdownController : MaintenanceControllerBase
    {
        public BreakdownController(EMSContext context) : base(context) { }

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var rows = data.BreakdownList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0 || subscID <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var b = data.Breakdown(id);
                return b == null ? ConflictResult("Breakdown not found.") : OkResult(data.DecorateBreakdown(b));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The number the next breakdown reported now will get.</summary>
        [HttpGet("GetMaxNo/{subscID}")]
        public async Task<IActionResult> GetMaxNo(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var prefix = "BD-" + MaintRules.FyCode(MaintRules.Today) + "-";
                var max = await ScalarIntAsync(@"
                    SELECT ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(BreakdownNo, LEN(@Prefix) + 1, 10))), 0)
                      FROM dbo.BreakdownMst
                     WHERE SubscID = @SubscID AND BreakdownNo LIKE @Prefix + '%'",
                    new SqlParameter("@Prefix", SqlDbType.VarChar, 10) { Value = prefix },
                    new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });
                return OkResult(prefix + (max + 1).ToString("0000"));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The machine's breakdown that is still Open / In Progress, or null.</summary>
        [HttpGet("GetOpenByMachine")]
        public async Task<IActionResult> GetOpenByMachine([FromQuery] int MachineNo)
        {
            if (MachineNo <= 0) return BadRequestResult("Invalid machine.");
            try
            {
                var subscID = await SubscOfAsync("MachineMst", MachineNo);
                if (subscID <= 0) return OkResult(null, "No records found.");
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var b = data.Breakdowns.Where(x => x.MachineNo == MachineNo && x.Status != "Closed")
                                       .OrderByDescending(x => x.BreakdownOn).FirstOrDefault();
                return OkResult(b == null ? null : data.DecorateBreakdown(b), b == null ? "No records found." : "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetTypes")]
        public IActionResult GetTypes() => OkResult(MaintRules.BreakdownTypes);

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] BreakdownSaveDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy <= 0) return BadRequestResult("Invalid User.");
            var isUpdate = dto.Status == "Update";
            if (!isUpdate && dto.Status != "Insert") return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (isUpdate && dto.MTransNo <= 0) return BadRequestResult("Invalid breakdown.");

            var now = MaintRules.Now;
            var st = MaintRules.Text(dto.BreakdownStatus);
            if (st == "") st = "Open";
            var type = MaintRules.Text(dto.BreakdownType);
            var reportedBy = MaintRules.Text(dto.ReportedBy);
            var problem = MaintRules.Text(dto.Problem);
            var rootCause = MaintRules.Text(dto.RootCause);
            var actionTaken = MaintRules.Text(dto.ActionTaken);
            var startText = MaintRules.Text(dto.RepairStart);
            var endText = MaintRules.Text(dto.RepairEnd);

            try
            {
                var data = await MaintenanceData.LoadAsync(db, dto.SubscID);
                var mc = data.LiveMachine(dto.MachineNo);

                if (mc == null) return ConflictResult("Select the machine.");
                if (!MaintRules.BreakdownStatuses.Contains(st)) return ConflictResult("Select the status.");
                if (!MaintRules.TryDateTime(dto.BreakdownOn, out var on)) return ConflictResult("Enter the breakdown date and time.");
                if (on > now) return ConflictResult("The breakdown time cannot be in the future.");
                if (!MaintRules.BreakdownTypes.Contains(type)) return ConflictResult("Select the breakdown type.");
                if (reportedBy == "") return ConflictResult("Select who reported the breakdown.");
                if (problem == "") return ConflictResult("Describe the problem.");
                if (dto.AttendedBy > 0 && !data.IsStaff(dto.AttendedBy)) return ConflictResult("Select who attended the breakdown.");

                DateTime? start = null, end = null;
                if (startText != "")
                {
                    if (!MaintRules.TryDateTime(startText, out var s)) return ConflictResult("Enter a valid repair start time.");
                    start = s;
                }
                if (endText != "")
                {
                    if (!MaintRules.TryDateTime(endText, out var e)) return ConflictResult("Enter a valid repair end time.");
                    end = e;
                }
                if (start != null && start < on) return ConflictResult("Repair cannot start before the breakdown time.");
                if (start != null && start > now) return ConflictResult("The repair start time cannot be in the future.");
                if (st != "Open")
                {
                    if (dto.AttendedBy <= 0) return ConflictResult("Select who attended the breakdown.");
                    if (start == null) return ConflictResult("Enter the repair start time.");
                }
                if (st == "Closed")
                {
                    if (end == null) return ConflictResult("Enter the repair end time to close the breakdown.");
                    if (end < start) return ConflictResult("Repair end cannot be before repair start.");
                    if (end > now) return ConflictResult("The repair end time cannot be in the future.");
                    if (rootCause == "") return ConflictResult("Enter the root cause to close the breakdown.");
                    if (actionTaken == "") return ConflictResult("Enter the action taken to close the breakdown.");
                }
                else end = null;

                if (reportedBy.Length > 100) return ConflictResult("Reported By cannot exceed 100 characters.");
                foreach (var (label, value) in new[]
                         {
                             ("Problem", problem), ("Root Cause", rootCause), ("Action Taken", actionTaken),
                             ("Spares Used", MaintRules.Text(dto.SparesUsed)), ("Remarks", MaintRules.Text(dto.Remarks))
                         })
                    if (value.Length > 500) return ConflictResult(label + " cannot exceed 500 characters.");

                if (isUpdate && data.Breakdown(dto.MTransNo) == null) return ConflictResult("Breakdown not found.");

                // One live breakdown per machine (the SP re-checks under a lock).
                if (st != "Closed")
                {
                    var live = data.Breakdowns.FirstOrDefault(x => x.MachineNo == mc.No && x.Status != "Closed" && x.No != dto.MTransNo);
                    if (live != null)
                        return ConflictResult(mc.Code + " already has an open breakdown (" + live.BreakdownNo + "). Close it first.");
                }

                var result = await db.CallBreakdownSP(new SP_Breakdown
                {
                    MTransNo = isUpdate ? dto.MTransNo : 0,
                    MachineNo = mc.No,
                    BreakdownOn = on,
                    BreakdownType = type,
                    ReportedBy = reportedBy,
                    Problem = problem,
                    AttendedBy = dto.AttendedBy > 0 ? dto.AttendedBy : null,
                    RepairStart = start,
                    RepairEnd = end,
                    RootCause = rootCause,
                    ActionTaken = actionTaken,
                    SparesUsed = MaintRules.Text(dto.SparesUsed),
                    Remarks = MaintRules.Text(dto.Remarks),
                    PMSheetNo = dto.PMSheetNo > 0 ? dto.PMSheetNo : null,
                    BreakdownStatus = st,
                    SystemIP = ClientIp,
                    UserNo = dto.CreatedBy,
                    SubscID = dto.SubscID,
                    Status = isUpdate ? "Update" : "Insert"
                });
                return SaveReply(result);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Soft-deletes an Open breakdown nobody has attended.</summary>
        [HttpDelete("DeleteData/{id}")]
        [HttpPost("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string? reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var result = await db.CallBreakdownSP(new SP_Breakdown
                {
                    MTransNo = id,
                    SystemIP = ClientIp,
                    UserNo = userNo,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(),
                    SubscID = subscID,
                    Status = "Delete"
                });
                return result.ID > 0 ? OkResult("OK", result.Message) : ConflictResult(result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
