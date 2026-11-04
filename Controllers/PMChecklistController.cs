using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// PM Checklist Master — EMSSoft/Modules/Masters/PMChecklistMaster.html.
    ///
    /// A checklist (paper form MNT/F/01) lists check points by frequency and
    /// category and covers one or more machines; a machine follows ONE active
    /// checklist. Points keep their id across edits so their "last done"
    /// history carries on (SQL/55_Maintenance.sql).
    /// </summary>
    [Route("api/PMChecklist")]
    public class PMChecklistController : MaintenanceControllerBase
    {
        public PMChecklistController(EMSContext context) : base(context) { }

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var rows = data.Checklists.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                                          .Select(c => data.DecorateChecklist(c, false)).ToList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One checklist with its check points (Daily first … Yearly last).</summary>
        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData([FromQuery] int MTransNo)
        {
            try
            {
                var subscID = await SubscOfAsync("PMChecklistMst", MTransNo);
                if (subscID <= 0) return ConflictResult("Checklist not found.");
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var c = data.Checklist(MTransNo);
                if (c == null) return ConflictResult("Checklist not found.");
                return OkResult(data.DecorateChecklist(c, true));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetFrequencies")]
        public IActionResult GetFrequencies() =>
            OkResult(MaintRules.Frequencies.Select(f => new { name = f, months = MaintRules.FreqMonths(f) }).ToList());

        [HttpGet("GetCategories")]
        public IActionResult GetCategories() => OkResult(MaintRules.Categories);

        /// <summary>
        /// Saves the header, its machines and its whole point list. Answers
        /// "OK^&lt;MTransNo&gt;", "duplicate fields" (name taken) or 409.
        /// </summary>
        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] PMChecklistSaveDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            var isUpdate = dto.Status == "Update";
            if (!isUpdate && dto.Status != "Insert") return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (isUpdate && dto.MTransNo <= 0) return BadRequestResult("Invalid checklist.");

            var name = MaintRules.Text(dto.ChecklistName);
            if (name == "") return ConflictResult("Checklist Name is required.");
            if (name.Length > 100) return ConflictResult("Checklist Name cannot exceed 100 characters.");
            var docNo = MaintRules.Text(dto.DocNo);
            var revNo = MaintRules.Text(dto.RevNo);
            if (docNo.Length > 30) return ConflictResult("Doc No cannot exceed 30 characters.");
            if (revNo.Length > 10) return ConflictResult("Rev No cannot exceed 10 characters.");
            if (MaintRules.Text(dto.Remarks).Length > 200) return ConflictResult("Remarks cannot exceed 200 characters.");
            DateTime? revDate = null;
            if (MaintRules.Text(dto.RevDate) != "")
            {
                if (!MaintRules.TryDate(MaintRules.Text(dto.RevDate).Length > 10 ? dto.RevDate!.Trim()[..10] : dto.RevDate, out var rd))
                    return ConflictResult("Enter a valid Rev Date.");
                revDate = rd;
            }

            var machines = (dto.Machines ?? new()).Where(n => n > 0).Distinct().ToList();
            if (machines.Count == 0) return ConflictResult("Select at least one machine for the checklist.");

            var points = (dto.Points ?? new())
                .Select((x, idx) => new
                {
                    idx,
                    transSlNo = x.TransSlNo,
                    frequency = MaintRules.Text(x.Frequency),
                    category = MaintRules.Text(x.Category),
                    checkPoint = MaintRules.Text(x.CheckPoint)
                })
                .Where(x => x.checkPoint != "")
                .ToList();
            if (points.Count == 0) return ConflictResult("Add at least one check point.");
            for (var i = 0; i < points.Count; i++)
            {
                var pt = points[i];
                if (MaintRules.FreqIndex(pt.frequency) == 99 || !MaintRules.Categories.Contains(pt.category))
                    return ConflictResult("Choose the frequency and category for “" + pt.checkPoint + "”.");
                if (pt.checkPoint.Length > 200)
                    return ConflictResult("“" + pt.checkPoint[..40] + "…” cannot exceed 200 characters.");
                for (var j = 0; j < i; j++)
                    if (points[j].frequency == pt.frequency
                        && string.Equals(points[j].checkPoint, pt.checkPoint, StringComparison.OrdinalIgnoreCase))
                        return ConflictResult("“" + pt.checkPoint + "” is listed twice under " + pt.frequency + ".");
            }

            try
            {
                var data = await MaintenanceData.LoadAsync(db, dto.SubscID);
                if (machines.Any(no => data.LiveMachine(no) == null))
                    return ConflictResult("One of the selected machines no longer exists.");
                if (isUpdate && data.Checklist(dto.MTransNo) == null)
                    return ConflictResult("Checklist not found.");

                // Serial numbers restart within each frequency, in the order entered.
                var counters = new Dictionary<string, int>();
                var ordered = points.OrderBy(x => MaintRules.FreqIndex(x.frequency)).ThenBy(x => x.idx)
                    .Select(x =>
                    {
                        counters[x.frequency] = counters.TryGetValue(x.frequency, out var k) ? k + 1 : 1;
                        return new { x.transSlNo, x.frequency, x.category, slNo = counters[x.frequency], x.checkPoint };
                    }).ToList();

                var result = await db.CallPMChecklistSP(new SP_PMChecklist
                {
                    MTransNo = isUpdate ? dto.MTransNo : 0,
                    ChecklistName = name,
                    DocNo = docNo,
                    RevNo = revNo,
                    RevDate = revDate,
                    Remarks = MaintRules.Text(dto.Remarks),
                    LockStatus = dto.LockStatus == "N" ? "N" : "Y",
                    MachinesJson = ToJson(machines),
                    PointsJson = ToJson(ordered),
                    SystemIP = ClientIp,
                    UserNo = dto.UserNo,
                    SubscID = dto.SubscID,
                    Status = isUpdate ? "Update" : "Insert"
                });
                return SaveReply(result);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Soft-deletes a checklist no check sheet uses.</summary>
        [HttpPost("RecDelete")]
        public async Task<IActionResult> RecDelete([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] string? mReason = "")
        {
            if (MTransNo <= 0 || UserNo <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var subscID = await SubscOfAsync("PMChecklistMst", MTransNo);
                if (subscID <= 0) return ConflictResult("Checklist not found.");
                var result = await db.CallPMChecklistSP(new SP_PMChecklist
                {
                    MTransNo = MTransNo,
                    SystemIP = ClientIp,
                    UserNo = UserNo,
                    DeleteReason = string.IsNullOrWhiteSpace(mReason) ? "No reason provided" : mReason.Trim(),
                    SubscID = subscID,
                    Status = "Delete"
                });
                return result.ID > 0 ? OkResult("OK", result.Message) : ConflictResult(result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
