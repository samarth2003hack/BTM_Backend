using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// PM Check Sheet — EMSSoft/Modules/Maintenance/PMCheckSheet.html.
    ///
    /// One machine for one month (SQL/55_Maintenance.sql):
    ///   - Opening a sheet SNAPSHOTS the checklist points due that month;
    ///     half-yearly / yearly points only when last done + 6 / 12 months is
    ///     on or before the month end (or never done).
    ///   - Results per point × slot: day (Daily), week W1-W5 (Weekly), 0
    ///     (Monthly / Half-Yearly / Yearly, with the date done and who did it).
    ///     Idle days take no daily result; a Not OK needs a point remark.
    ///   - Approval: Dept (Production Supervisor) then Maint (Maintenance
    ///     Supervisor), only after the month is over; Dept approval locks it.
    ///   - Due / done / missed / compliance are recomputed on every read.
    /// </summary>
    [Route("api/PMCheckSheet")]
    public class PMCheckSheetController : MaintenanceControllerBase
    {
        public PMCheckSheetController(EMSContext context) : base(context) { }

        /// <summary>Every sheet, latest month first (headers and figures only).</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var rows = data.Sheets.Select(s => data.DecorateSheet(s, false))
                    .OrderByDescending(s => s.PeriodKey, StringComparer.Ordinal)
                    .ThenBy(s => s.MachineCode, StringComparer.Ordinal).ToList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One sheet with its points, results, idle days, operators and remarks.</summary>
        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData([FromQuery] int MTransNo)
        {
            try
            {
                var subscID = await SubscOfAsync("PMSheetMst", MTransNo);
                if (subscID <= 0) return ConflictResult("Check sheet not found.");
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var sh = data.Sheet(MTransNo);
                return sh == null ? ConflictResult("Check sheet not found.") : OkResult(data.DecorateSheet(sh, true));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// What a NEW sheet for a machine + month looks like: the checklist, the
        /// points due that month and Sundays pre-marked idle. Answers
        /// { existingId, sheetNo } instead when that month is already open.
        /// </summary>
        [HttpGet("GetTemplate")]
        public async Task<IActionResult> GetTemplate([FromQuery] int MachineNo, [FromQuery] int Year, [FromQuery] int Month)
        {
            try
            {
                var subscID = await SubscOfAsync("MachineMst", MachineNo);
                if (subscID <= 0) return ConflictResult("Select a machine.");
                if (Year < 2000 || Year > 2100 || Month < 1 || Month > 12) return ConflictResult("Select the month.");
                var data = await MaintenanceData.LoadAsync(db, subscID);
                var mc = data.LiveMachine(MachineNo)!;
                var existing = data.Sheets.FirstOrDefault(s => s.MachineNo == MachineNo && s.Year == Year && s.Month == Month);
                if (existing != null) return OkResult(new { existingId = existing.No, sheetNo = existing.SheetNo });
                var cl = data.ChecklistOfMachine(MachineNo);
                if (cl == null)
                    return ConflictResult("No active PM checklist covers " + mc.Code + ". Add it in PM Checklist Master first.");
                return OkResult(new
                {
                    existingId = 0,
                    machineNo = mc.No,
                    machineCode = mc.Code,
                    machineName = mc.Name,
                    checklistNo = cl.No,
                    checklistName = cl.Name,
                    docNo = cl.DocNo,
                    revNo = cl.RevNo,
                    revDate = MaintRules.Ymd(cl.RevDate),
                    year = Year,
                    month = Month,
                    periodLabel = MaintRules.PeriodLabel(Year, Month),
                    points = data.PointsDueFor(cl, MachineNo, Year, Month),
                    idleDays = MaintenanceData.SundaysOf(Year, Month)
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Saves a sheet (insert / update). Answers "OK^&lt;MTransNo&gt;",
        /// "duplicate fields" (the machine already has that month) or 409.
        /// </summary>
        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] PMSheetSaveDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            var isUpdate = dto.Status == "Update";
            if (!isUpdate && dto.Status != "Insert") return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (MaintRules.Text(dto.Remarks).Length > 500) return ConflictResult("Remarks cannot exceed 500 characters.");

            try
            {
                var data = await MaintenanceData.LoadAsync(db, dto.SubscID);
                var today = MaintRules.Today;
                int machineNo, y, m;
                List<PMDuePoint> points;

                if (isUpdate)
                {
                    var sheet = data.Sheet(dto.MTransNo);
                    if (sheet == null) return ConflictResult("Check sheet not found.");
                    if (sheet.DeptSupBy is > 0) return ConflictResult("This check sheet is approved and locked.");
                    machineNo = sheet.MachineNo; y = sheet.Year; m = sheet.Month;
                    points = sheet.Points.Select(p => new PMDuePoint
                    {
                        PointNo = p.PointNo, SlNo = p.SlNo, CheckPoint = p.CheckPoint, Frequency = p.Frequency, Category = p.Category
                    }).ToList();
                }
                else
                {
                    machineNo = dto.MachineNo; y = dto.Year; m = dto.Month;
                    var mc = data.LiveMachine(machineNo);
                    if (mc == null) return ConflictResult("Select a machine.");
                    if (y < 2000 || y > 2100 || m < 1 || m > 12) return ConflictResult("Select the month.");
                    if (MaintRules.MonthStart(y, m) > today) return ConflictResult("A check sheet cannot be opened for a future month.");
                    if (data.Sheets.Any(s => s.MachineNo == machineNo && s.Year == y && s.Month == m))
                        return OkResult("duplicate fields", "duplicate fields");
                    var cl = data.ChecklistOfMachine(machineNo);
                    if (cl == null) return ConflictResult("No active PM checklist covers " + mc.Code + ".");
                    points = data.PointsDueFor(cl, machineNo, y, m);
                }

                var dim = DateTime.DaysInMonth(y, m);
                var monthStart = MaintRules.MonthStart(y, m);
                var monthEnd = MaintRules.MonthEnd(y, m);
                var monthLabel = MaintRules.PeriodLabel(y, m);
                var idle = (dto.IdleDays ?? new()).Where(d => d >= 1 && d <= dim).Distinct().OrderBy(d => d).ToList();
                var pointMap = points.GroupBy(p => p.PointNo).ToDictionary(g => g.Key, g => g.First());

                // Results: unknown points / statuses and idle-day cells are dropped.
                var entries = new List<object>();
                var statusOf = new Dictionary<int, List<string>>();
                var seen = new HashSet<(int, int)>();
                foreach (var e in dto.Entries ?? new())
                {
                    if (e == null || !pointMap.TryGetValue(e.PointNo, out var pt)) continue;
                    var st = MaintRules.Text(e.Status).ToUpperInvariant();
                    if (!MaintRules.EntryStatuses.Contains(st)) continue;
                    var slot = e.Slot;
                    DateTime? doneOn = null;
                    int doneBy = 0;
                    if (pt.Frequency == "Daily")
                    {
                        if (slot < 1 || slot > dim || idle.Contains(slot)) continue;
                        doneOn = new DateTime(y, m, slot);
                        if (doneOn > today)
                            return ConflictResult("“" + pt.CheckPoint + "” cannot be recorded for a future day (" + slot + ").");
                    }
                    else
                    {
                        if (pt.Frequency == "Weekly")
                        {
                            if (slot < 1 || slot > MaintRules.WeeksIn(dim)) continue;
                        }
                        else slot = 0;
                        if (st != "NA")
                        {
                            var where = pt.Frequency == "Weekly" ? " (W" + slot + ")" : "";
                            if (!MaintRules.TryDate(e.DoneOn, out var dOn))
                                return ConflictResult("Enter the date “" + pt.CheckPoint + "”" + where + " was done.");
                            if (dOn < monthStart || dOn > monthEnd)
                                return ConflictResult("The date for “" + pt.CheckPoint + "”" + where + " must be inside " + monthLabel + ".");
                            if (dOn > today)
                                return ConflictResult("The date for “" + pt.CheckPoint + "”" + where + " cannot be in the future.");
                            if (!data.IsStaff(e.DoneBy))
                                return ConflictResult("Select who did “" + pt.CheckPoint + "”" + where + ".");
                            doneOn = dOn;
                            doneBy = e.DoneBy;
                        }
                    }
                    if (!seen.Add((pt.PointNo, slot))) continue;
                    entries.Add(new { pointNo = pt.PointNo, slot, status = st, doneOn = doneOn == null ? null : MaintRules.Ymd(doneOn), doneBy });
                    if (!statusOf.ContainsKey(pt.PointNo)) statusOf[pt.PointNo] = new();
                    statusOf[pt.PointNo].Add(st);
                }

                // Point remarks — a Not OK point needs one.
                var remarks = new Dictionary<int, string>();
                foreach (var kv in dto.PointRemarks ?? new())
                {
                    var text = JsonText(kv.Value);
                    if (text != "" && int.TryParse(kv.Key, out var pno) && pointMap.ContainsKey(pno))
                    {
                        if (text.Length > 1000) return ConflictResult("The remark for “" + pointMap[pno].CheckPoint + "” cannot exceed 1000 characters.");
                        remarks[pno] = text;
                    }
                }
                foreach (var kv in statusOf)
                    if (kv.Value.Contains("NOK") && !remarks.ContainsKey(kv.Key))
                        return ConflictResult("Enter a remark for “" + pointMap[kv.Key].CheckPoint + "” — it is marked Not OK.");

                // Day operators — only real operators on working days.
                var ops = new Dictionary<int, int>();
                foreach (var kv in dto.DayOperators ?? new())
                {
                    var op = JsonInt(kv.Value);
                    if (int.TryParse(kv.Key, out var d) && d >= 1 && d <= dim && !idle.Contains(d) && op > 0
                        && data.Operator(op) is { Deleted: false })
                        ops[d] = op;
                }
                var days = Enumerable.Range(1, dim)
                    .Where(d => idle.Contains(d) || ops.ContainsKey(d))
                    .Select(d => new { day = d, idle = idle.Contains(d), operatorNo = ops.TryGetValue(d, out var o) ? o : 0 })
                    .ToList();

                var result = await db.CallPMSheetSP(new SP_PMSheet
                {
                    MTransNo = isUpdate ? dto.MTransNo : 0,
                    MachineNo = machineNo,
                    Year = y,
                    Month = m,
                    Remarks = MaintRules.Text(dto.Remarks),
                    PointsJson = ToJson(points.Select(p => new { p.PointNo, p.SlNo, p.CheckPoint, p.Frequency, p.Category })),
                    EntriesJson = ToJson(entries),
                    DaysJson = ToJson(days),
                    RemarksJson = ToJson(remarks.Select(kv => new { pointNo = kv.Key, remarks = kv.Value })),
                    SystemIP = ClientIp,
                    UserNo = dto.UserNo,
                    SubscID = dto.SubscID,
                    Status = isUpdate ? "Update" : "Insert"
                });
                return SaveReply(result);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Approves a sheet at one level: "Dept" first, then "Maint".</summary>
        [HttpPost("Approve")]
        public async Task<IActionResult> Approve([FromBody] PMSheetApproveDto dto)
        {
            if (dto == null || dto.MTransNo <= 0) return BadRequestResult("Invalid request.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            try
            {
                var subscID = await SubscOfAsync("PMSheetMst", dto.MTransNo);
                if (subscID <= 0) return ConflictResult("Check sheet not found.");
                var result = await db.CallPMSheetApproveSP(dto.MTransNo, MaintRules.Text(dto.Level), dto.ApprovedBy,
                                                           ClientIp, dto.UserNo, subscID);
                return SaveReply(result);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Soft-deletes a draft sheet; breakdowns raised from it are unlinked.</summary>
        [HttpPost("RecDelete")]
        public async Task<IActionResult> RecDelete([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] string? mReason = "")
        {
            if (MTransNo <= 0 || UserNo <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var subscID = await SubscOfAsync("PMSheetMst", MTransNo);
                if (subscID <= 0) return ConflictResult("Check sheet not found.");
                var result = await db.CallPMSheetSP(new SP_PMSheet
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
