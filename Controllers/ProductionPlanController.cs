using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using static EMSAPI.Models.PlanScheduleEngine;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Production/ProductionPlan.html — the monthly production plan (SQL 59).
    ///
    ///   GET  GetList/{subscID}                          plan headers with hammer loads, weeks, job cards
    ///   GET  GetMasterData?MTransNo=&subscID=           one header (one-row list)
    ///   GET  GetDetailsList?MTransNo=&subscID=          one row per plan line, its job repeated
    ///   GET  GetMaxNo?PlanDate=&subscID=                next PP/yy-yy/nnnn (preview)
    ///   POST SaveMasterData                             header → "OK^id" | "duplicate fields"
    ///   POST SaveDtlData                                jobs + lines (Draft) → days worked out here
    ///   POST RecDelete?MTransNo=&UserNo=&subscID=&mReason=
    ///   POST Approve?MTransNo=&UserNo=&subscID=
    ///   POST FirmWeek?MTransNo=&WeekNo=&UserNo=&subscID=
    ///   POST ReleaseWeek?MTransNo=&WeekNo=&UserNo=&subscID=   makes the week's job cards
    ///   POST Reschedule                                 { mTransNo, jobSeq, fixedStart, daysRequired, reason, userNo }
    ///   GET  GetPendingLines?PlanMonth=&ExcludePlan=&subscID=
    ///   GET  GetPlanningParts/{subscID}
    ///   GET  GetPlanningRoutes/{subscID}
    ///   GET  GetPlanPeriods/{subscID}
    /// The day split is always worked out on the server (PlanScheduleEngine, the
    /// same rules as the page) and stored in ProductionPlanDay.
    /// </summary>
    [Route("api/[controller]")]
    public class ProductionPlanController : PlanningControllerBase
    {
        public ProductionPlanController(EMSContext context) : base(context) { }

        // ============================================================
        // READ
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(subscID);
                var plans = await LoadPlans(subscID);
                var rows = new List<object>();
                foreach (var p in plans) rows.Add(await Header(p, data));
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData([FromQuery] int MTransNo, [FromQuery] int subscID)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid plan.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var plan = (await LoadPlans(subscID, MTransNo)).FirstOrDefault();
                if (plan == null) return NotFoundResult("Production plan not found.");
                var data = await LoadPlanningData(subscID);
                return OkResult(new List<object> { await Header(plan, data) });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetDetailsList")]
        public async Task<IActionResult> GetDetailsList([FromQuery] int MTransNo, [FromQuery] int subscID)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid plan.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var plan = (await LoadPlans(subscID, MTransNo)).FirstOrDefault();
                if (plan == null) return OkResult(new List<object>(), "No records found.");
                var data = await LoadPlanningData(subscID);
                var rows = new List<object>();
                foreach (var j in plan.Jobs)
                    foreach (var l in j.Lines)
                    {
                        var o = l.Source == "OA" ? data.OALine(l.OAYear, l.OANo, l.OAID) : null;
                        var jc = data.JobCardOf(plan.MTransNo, j.Seq, l.SlNo);
                        rows.Add(new
                        {
                            jobSeq = j.Seq, planType = j.PlanType, partNo = j.PartNo, dieNo = j.DieNo, machineNo = j.MachineNo,
                            daysRequired = j.DaysRequired, fixedStart = j.FixedStart, jobRemarks = j.Remarks, lineSlNo = l.SlNo,
                            source = l.Source, oaYear = l.OAYear, oaNo = l.OANo, oaId = l.OAID, grade = l.Grade,
                            planQty = l.PlanQty, rate = o?.Rate ?? l.Rate, reason = l.Reason, routeNo = l.RouteNo,
                            jobCardNo = jc?.No ?? "", forgedQty = jc?.Forged ?? 0,
                        });
                    }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Preview only — the save allocates the number under a lock.</summary>
        [HttpGet("GetMaxNo")]
        public async Task<IActionResult> GetMaxNo([FromQuery] DateTime? PlanDate, [FromQuery] int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var row = await QueryOne(@"
                    DECLARE @Year INT = dbo.FN_FYCode(@Date);
                    SELECT CONCAT('PP/', LEFT(CAST(@Year AS VARCHAR(4)), 2), '-', RIGHT(CAST(@Year AS VARCHAR(4)), 2), '/',
                                  RIGHT('0000' + CAST(ISNULL(MAX(DocNo), 0) + 1 AS VARCHAR(10)), 4)) AS No
                      FROM dbo.ProductionPlanMst WHERE SubscID = @SubscID AND DocYear = @Year",
                    new Microsoft.Data.SqlClient.SqlParameter("@Date", System.Data.SqlDbType.Date) { Value = (PlanDate ?? DateTime.Today).Date },
                    Int("@SubscID", subscID));
                return OkResult(S(row, "No"));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SAVE
        // ============================================================

        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] ProductionPlanSaveRequest dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var userNo = dto.CreatedBy > 0 ? dto.CreatedBy : dto.ModifiedBy > 0 ? dto.ModifiedBy : dto.UserNo;
            if (userNo <= 0) return BadRequestResult("Invalid User.");
            var isUpdate = string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (isUpdate && dto.MTransNo <= 0) return BadRequestResult("Invalid MTransNo.");
            if (!DateTime.TryParse(dto.PlanDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var planDate))
                return BadRequestResult("Enter the plan date.");
            try
            {
                var result = await db.CallProductionPlanMstSP(isUpdate ? dto.MTransNo : 0, planDate.Date, (dto.PlanMonth ?? "").Trim(),
                    dto.RevNo, dto.PlanStatus, dto.Remarks, userNo, ClientIp, null, dto.SubscID, isUpdate ? "Update" : "Insert");
                if (result.ID == 0 && result.Message == "duplicate fields") return OkResult("duplicate fields", "This month already has a production plan.");
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("SaveDtlData")]
        public async Task<IActionResult> SaveDtlData([FromBody] ProductionPlanDtlSaveRequest req)
        {
            if (req == null || req.MTransNo <= 0) return BadRequestResult("Invalid plan.");
            if (req.UserNo <= 0) return BadRequestResult("Invalid User.");
            try
            {
                var subscID = req.SubscID > 0 ? req.SubscID : await PlanSubscID(req.MTransNo);
                var jobs = JsonSerializer.Serialize((req.Jobs ?? new()).Select(j => new
                {
                    seq = j.Seq, planType = j.PlanType == "Alternate" ? "Alternate" : "Main", partNo = j.PartNo, dieNo = j.DieNo,
                    machineNo = j.MachineNo, daysRequired = j.DaysRequired, fixedStart = (j.FixedStart ?? "").Split('T')[0],
                    remarks = j.Remarks ?? "",
                    lines = (j.Lines ?? new()).Select(l => new
                    {
                        slNo = l.SlNo, source = l.Source == "STOCK" ? "STOCK" : "OA", oaYear = l.OAYear, oaNo = l.OANo, oaId = l.OAId,
                        grade = (l.Grade ?? "").Trim(), planQty = l.PlanQty, rate = l.Rate, routeNo = l.RouteNo, reason = l.Reason ?? "",
                    }),
                }));

                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await db.CallProductionPlanDtlSP(req.MTransNo, jobs, req.UserNo, subscID);
                if (result.ID == 0) { await tx.RollbackAsync(); return ConflictResult(result.Message); }
                var days = await StoreDays(req.MTransNo, subscID);
                if (days.ID == 0) { await tx.RollbackAsync(); return ConflictResult(days.Message); }
                await tx.CommitAsync();
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("RecDelete")]
        [HttpDelete("RecDelete")]
        public async Task<IActionResult> RecDelete([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] int subscID = 0,
            [FromQuery] string mReason = "")
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid plan.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var reason = string.IsNullOrWhiteSpace(mReason) ? "No reason provided" : mReason.Trim();
                var result = await db.CallProductionPlanMstSP(MTransNo, null, null, 0, null, null, UserNo, ClientIp,
                    reason.Length > 250 ? reason[..250] : reason, subscID, "Delete");
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // WORKFLOW
        // ============================================================

        [HttpPost("Approve")]
        public async Task<IActionResult> Approve([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] int subscID = 0)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid plan.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var result = await db.CallProductionPlanActionSP(MTransNo, "Approve", 0, UserNo, subscID);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("FirmWeek")]
        public async Task<IActionResult> FirmWeek([FromQuery] int MTransNo, [FromQuery] int WeekNo, [FromQuery] int UserNo,
            [FromQuery] int subscID = 0)
        {
            if (MTransNo <= 0 || WeekNo <= 0) return BadRequestResult("Invalid plan or week.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var result = await db.CallProductionPlanActionSP(MTransNo, "FirmWeek", WeekNo, UserNo, subscID);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Firm → Released. One job card per MAIN line of every job whose first
        /// planned day falls in the week (a line already released is skipped):
        /// route, raw material (ConvBOM: qty × cut weight), die, hammer and the
        /// line's planned days. Operator and shift stay blank until production
        /// starts. A STOCK line's job card carries OA No 0.
        /// </summary>
        [HttpPost("ReleaseWeek")]
        public async Task<IActionResult> ReleaseWeek([FromQuery] int MTransNo, [FromQuery] int WeekNo, [FromQuery] int UserNo,
            [FromQuery] int subscID = 0)
        {
            if (MTransNo <= 0 || WeekNo <= 0) return BadRequestResult("Invalid plan or week.");
            try
            {
                subscID = subscID > 0 ? subscID : await PlanSubscID(MTransNo);
                var plan = (await LoadPlans(subscID, MTransNo)).FirstOrDefault();
                if (plan == null) return NotFoundResult("Production plan not found.");
                var data = await LoadPlanningData(subscID);
                var info = await CalendarOf(plan.PlanMonth, data);
                var week = info?.Weeks.FirstOrDefault(w => w.WeekNo == WeekNo);
                if (week == null) return ConflictResult("Week " + WeekNo + " is not in the plan month's work calendar.");
                var days = await StoredDays(MTransNo);

                var cards = new List<object>();
                foreach (var j in plan.Jobs.Where(j => j.PlanType == "Main"))
                {
                    var jobDates = j.Lines.SelectMany(l => days.TryGetValue((j.Seq, l.SlNo), out var m) ? m.Keys : Enumerable.Empty<string>())
                                          .OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var start = jobDates.FirstOrDefault();
                    if (start == null || string.CompareOrdinal(start, week.From) < 0 || string.CompareOrdinal(start, week.To) > 0) continue;
                    foreach (var l in j.Lines)
                    {
                        var lineDates = days.TryGetValue((j.Seq, l.SlNo), out var lm)
                            ? lm.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
                        var o = l.Source == "OA" ? data.OALine(l.OAYear, l.OANo, l.OAID) : null;
                        var bar = data.BarOf(j.PartNo, l.Grade);
                        var route = l.RouteNo > 0 && data.Routes.ContainsKey(l.RouteNo) ? data.Routes[l.RouteNo] : data.RouteFor(j.PartNo, l.Grade, j.MachineNo);
                        data.GradeNos.TryGetValue((j.PartNo, l.Grade.ToUpperInvariant()), out var gradeNo);
                        cards.Add(new
                        {
                            jobSeq = j.Seq, lineSlNo = l.SlNo, jobDate = Today,
                            oaYear = l.Source == "OA" ? l.OAYear : 0, oaNo = l.Source == "OA" ? l.OANo : 0,
                            oaId = l.Source == "OA" ? l.OAID : 0, oaLinkNo = o?.TransSlNo ?? 0,
                            partNo = j.PartNo, gradeNo, grade = l.Grade, routeNo = route?.RouteNo ?? 0, qty = l.PlanQty,
                            rmPartNo = bar.RMPartNo, rmGradeNo = bar.RMGradeNo, rmGrade = bar.RMGrade,
                            rmQty = Math.Round(l.PlanQty * bar.CutWt, 3, MidpointRounding.AwayFromZero), rmUnit = bar.Unit,
                            plannedStart = lineDates.FirstOrDefault() ?? start, plannedEnd = lineDates.LastOrDefault() ?? jobDates.Last(),
                            machineNo = j.MachineNo, dieNo = j.DieNo,
                        });
                    }
                }
                var result = await db.CallProductionPlanReleaseSP(MTransNo, WeekNo, JsonSerializer.Serialize(cards), UserNo, ClientIp, subscID);
                if (result.ID == 0 && !result.Message.Contains("released")) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The one change an approved or released plan takes: a job's fixed start and days. Logged; days re-worked.</summary>
        [HttpPost("Reschedule")]
        public async Task<IActionResult> Reschedule([FromBody] ProductionPlanRescheduleRequest req)
        {
            if (req == null || req.MTransNo <= 0 || req.JobSeq <= 0) return BadRequestResult("Invalid request.");
            try
            {
                var subscID = req.SubscID > 0 ? req.SubscID : await PlanSubscID(req.MTransNo);
                var plan = (await LoadPlans(subscID, req.MTransNo)).FirstOrDefault();
                if (plan == null) return NotFoundResult("Production plan not found.");
                var job = plan.Jobs.FirstOrDefault(j => j.Seq == req.JobSeq);
                if (job == null) return NotFoundResult("Job not found.");
                var data = await LoadPlanningData(subscID);
                var info = await CalendarOf(plan.PlanMonth, data);
                var before = BuildPlan(plan, info).Jobs[plan.Jobs.IndexOf(job)];
                var fixedStart = (req.FixedStart ?? "").Split('T')[0];
                DateTime? fixedDate = DateTime.TryParseExact(fixedStart, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var fd) ? fd : null;
                var fromText = (string.IsNullOrEmpty(before.StartDate) ? "—" : before.StartDate) + " · " + job.DaysRequired.ToString("0.##", Inv) + " d";
                var toText = (fixedDate == null ? "in sequence" : fixedStart) + " · " +
                             (req.DaysRequired > 0 ? req.DaysRequired : (decimal)job.DaysRequired).ToString("0.##", Inv) + " d";

                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await db.CallProductionPlanRescheduleSP(req.MTransNo, req.JobSeq, fixedDate, req.DaysRequired,
                    (req.Reason ?? "").Trim(), fromText, toText, req.UserNo, subscID);
                if (result.ID == 0) { await tx.RollbackAsync(); return ConflictResult(result.Message); }
                var days = await StoreDays(req.MTransNo, subscID);
                if (days.ID == 0) { await tx.RollbackAsync(); return ConflictResult(days.Message); }
                await tx.CommitAsync();
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // PLANNING LOOKUPS
        // ============================================================

        /// <summary>Every OA line still to forge, less what OTHER live plans hold of it.</summary>
        [HttpGet("GetPendingLines")]
        public async Task<IActionResult> GetPendingLines([FromQuery] string PlanMonth, [FromQuery] int ExcludePlan, [FromQuery] int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(subscID);
                var plans = await LoadPlans(subscID);
                var held = PlannedByOa(plans, data, ExcludePlan);
                var mat = await MaterialStatusMap(data, plans);
                var rows = new List<object>();
                foreach (var o in data.OALines.Values.OrderBy(x => x.PlanDate, StringComparer.Ordinal).ThenBy(x => x.OANo).ThenBy(x => x.OAID))
                {
                    var pending = PendingOf(data, o);
                    if (pending <= 0) continue;
                    var route = data.RouteFor(o.PartNo, o.Grade);
                    rows.Add(new
                    {
                        oaYear = o.OAYear, oaNo = o.OANo, oaId = o.OAID, oaLabel = o.Label, oaDate = o.OADate, customer = o.Customer,
                        poNo = o.PoNo, poDate = o.PoDate, deliveryDate = o.PlanDate, customerDeliveryDate = o.DeliveryDate,
                        partNo = o.PartNo, grade = o.Grade, pendingQty = pending,
                        plannedElsewhere = held.TryGetValue(o.Key, out var h) ? h.Qty : 0, rate = o.Rate,
                        materialStatus = mat.TryGetValue(o.Key, out var ms) ? ms : "",
                        orderType = o.CustomerMaterial ? "Job Work" : "Regular", priority = o.Priority,
                        forgeBy = ForgeBy(o.PlanDate, route?.PostForgeDays ?? 0),
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One row per part that is ordered, routed or planned: code, name, cut
        /// weight, die, default hammer, forging rate and setup — and per grade
        /// what it is cut from (ConvBOM) and its die (DieMst, one per part + grade).
        /// </summary>
        [HttpGet("GetPlanningParts/{subscID}")]
        public async Task<IActionResult> GetPlanningParts(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(subscID);
                var plans = await LoadPlans(subscID);
                var grades = new Dictionary<int, SortedSet<string>>();
                void Add(int partNo, string grade)
                {
                    if (partNo <= 0) return;
                    if (!grades.TryGetValue(partNo, out var set)) grades[partNo] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(grade)) set.Add(grade.Trim());
                }
                foreach (var o in data.OALines.Values) Add(o.PartNo, o.Grade);
                foreach (var r in data.Routes.Values) Add(r.PartNo, r.Grade);
                foreach (var p in plans) foreach (var j in p.Jobs) foreach (var l in j.Lines) Add(j.PartNo, l.Grade);

                var dies = await Query(@"
                    SELECT dm.MTransNo AS dieNo, dm.PartNo AS partNo, LTRIM(RTRIM(ISNULL(mg.Grade, ''))) AS grade
                      FROM dbo.DieMst dm
                      OUTER APPLY (SELECT TOP (1) g.Grade FROM dbo.MaterialGrade g
                                    WHERE g.PartNo = dm.PartNo AND g.GradeNo = dm.GradeNo AND g.SubscID = dm.SubscID AND g.DeleteStatus = 'N') mg
                     WHERE dm.SubscID = @SubscID AND ISNULL(dm.DeleteStatus, 'N') = 'N' AND ISNULL(dm.LockStatus, 'Y') = 'Y'
                     ORDER BY dm.MTransNo", Int("@SubscID", subscID));

                var rows = new List<object>();
                foreach (var kv in grades.OrderBy(k => data.PartCode(k.Key), StringComparer.OrdinalIgnoreCase))
                {
                    var partNo = kv.Key;
                    var gradeRows = kv.Value.Select(g =>
                    {
                        var bar = data.BarOf(partNo, g);
                        var die = dies.FirstOrDefault(x => I(x, "partNo") == partNo && string.Equals(S(x, "grade"), g, StringComparison.OrdinalIgnoreCase));
                        var dieNo = die == null ? 0 : I(die, "dieNo");
                        var dieInfo = dieNo > 0 && data.Dies.TryGetValue(dieNo, out var di) ? di : ("", "Ready", 0d);
                        return new
                        {
                            grade = g, cutWt = bar.CutWt, rmGrade = bar.RMGrade, barDia = bar.Dia, rmText = bar.Text,
                            rmPartNo = bar.RMPartNo, dieNo, dieName = dieInfo.Item1, dieStatus = dieInfo.Item2, dieRemaining = dieInfo.Item3,
                        };
                    }).ToList();
                    var firstWithBar = gradeRows.FirstOrDefault(g => g.cutWt > 0) ?? gradeRows.FirstOrDefault();
                    var firstDie = gradeRows.FirstOrDefault(g => g.dieNo > 0);
                    var route = data.Routes.Values.Where(r => r.PartNo == partNo).OrderBy(r => r.RouteNo).FirstOrDefault();
                    var pcsPerHr = route != null && route.ForgeTimeMin > 0 && route.BatchQty > 0
                        ? Math.Round(route.BatchQty * 60.0 / route.ForgeTimeMin, 2) : 0;
                    rows.Add(new
                    {
                        partNo, partCode = data.PartCode(partNo), partName = data.PartName(partNo),
                        cutWt = firstWithBar?.cutWt ?? 0, barDia = firstWithBar?.barDia ?? 0,
                        dieNo = firstDie?.dieNo ?? 0, dieName = firstDie?.dieName ?? "", dieRemaining = firstDie?.dieRemaining ?? 0,
                        dieStatus = firstDie?.dieStatus ?? "Ready",
                        machineNo = route?.HammerNo ?? 0, pcsPerHr, setupHrs = Math.Round((route?.SetupMin ?? 0) / 60.0, 2),
                        grades = gradeRows,
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The Routing Chart: every route with its steps; hammerNo = the machine of its Forging step.</summary>
        [HttpGet("GetPlanningRoutes/{subscID}")]
        public async Task<IActionResult> GetPlanningRoutes(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(subscID);
                var steps = await Query(@"
                    SELECT rd.MTransNo AS routeNo, rd.SlNo AS slNo, rd.Operation AS operationNo, ISNULL(o.MasterName, '') AS operationName,
                           rd.MachineNo AS machineNo, ISNULL(m.MachineName, '') AS machineName, ISNULL(rd.TimeMin, 0) AS timeMin,
                           ISNULL(rd.SetupMin, 0) AS setupMin
                      FROM dbo.RoutingChartDtl rd
                      JOIN dbo.RoutingChartMst h ON h.MTransNo = rd.MTransNo
                      LEFT JOIN dbo.OtherMaster o ON o.MTransNo = rd.Operation
                      LEFT JOIN dbo.MachineMst m ON m.MTransNo = rd.MachineNo
                     WHERE h.SubscID = @SubscID AND ISNULL(rd.DeleteStatus, 'N') = 'N'
                     ORDER BY rd.MTransNo, rd.SlNo", Int("@SubscID", subscID));
                var rows = data.Routes.Values.OrderBy(r => r.RouteNo).Select(r => new
                {
                    routeNo = r.RouteNo, partNo = r.PartNo, grade = r.Grade, routeName = r.Name, batchQty = r.BatchQty,
                    hammerNo = r.HammerNo, setupMin = r.SetupMin, postForgeDays = r.PostForgeDays,
                    steps = steps.Where(s => I(s, "routeNo") == r.RouteNo).Select(s => new
                    {
                        slNo = I(s, "slNo"), operationNo = I(s, "operationNo"), operationName = S(s, "operationName"),
                        machineNo = I(s, "machineNo"), machineName = S(s, "machineName"), timeMin = D(s, "timeMin"), setupMin = D(s, "setupMin"),
                    }).ToList(),
                }).ToList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Every plan with its working dates and weeks — the period pickers of the reports.</summary>
        [HttpGet("GetPlanPeriods/{subscID}")]
        public async Task<IActionResult> GetPlanPeriods(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadPlanningData(subscID);
                var rows = new List<object>();
                foreach (var p in await LoadPlans(subscID)) rows.Add(PeriodsOf(p, await CalendarOf(p.PlanMonth, data)));
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<int> PlanSubscID(int planId) =>
            I(await QueryOne("SELECT SubscID FROM dbo.ProductionPlanMst WHERE MTransNo = @Id", Int("@Id", planId)), "SubscID");

        /// <summary>Works the plan's days out again and stores them (inside the caller's transaction).</summary>
        private async Task<(int ID, string Message)> StoreDays(int planId, int subscID)
        {
            var plan = (await LoadPlans(subscID, planId)).FirstOrDefault();
            if (plan == null) return (0, "Production plan not found.");
            var data = await LoadPlanningData(subscID);
            var info = await CalendarOf(plan.PlanMonth, data);
            var built = BuildPlan(plan, info);
            return await db.CallProductionPlanDaysSP(planId, JsonSerializer.Serialize(DaysOf(plan, built)), subscID);
        }

        /// <summary>A plan header as GetList returns it (main-job totals, hammer loads, weeks, job cards).</summary>
        private async Task<object> Header(PlanHead p, PlanningData data)
        {
            var info = await CalendarOf(p.PlanMonth, data);
            var built = BuildPlan(p, info);
            double qty = 0, wt = 0, value = 0, days = 0;
            int main = 0, alt = 0;
            foreach (var j in p.Jobs)
            {
                if (j.PlanType != "Main") { alt++; continue; }
                main++;
                days += j.DaysRequired;
                foreach (var l in j.Lines)
                {
                    var o = l.Source == "OA" ? data.OALine(l.OAYear, l.OANo, l.OAID) : null;
                    qty += l.PlanQty;
                    wt += l.PlanQty * data.BarOf(j.PartNo, l.Grade).CutWt;
                    value += l.PlanQty * (o?.Rate ?? l.Rate);
                }
            }
            var hammerLoads = built.Lanes.Select(kv => new
            {
                machineNo = kv.Key, hammer = data.HammerName(kv.Key), usedDays = kv.Value.UsedDays, workDays = kv.Value.WorkDays,
                idleDays = kv.Value.IdleDays, capacityDays = kv.Value.CapacityDays, loadPct = kv.Value.LoadPct,
            }).OrderByDescending(x => x.loadPct).ToList();
            var periods = PeriodsOf(p, info);
            return new
            {
                mTransNo = p.MTransNo, planNo = p.PlanNo, planDate = p.PlanDate, planMonth = p.PlanMonth, revNo = p.RevNo,
                planStatus = p.PlanStatus, remarks = p.Remarks, mainJobs = main, altJobs = alt, totalQty = qty,
                totalWt = R2(wt), totalValue = R2(value), daysRequired = days, idleDays = built.IdleDays,
                workingDays = info?.WorkingDays ?? 0, createdOn = p.CreatedOn,
                hammerLoads, loadPct = info == null ? 0 : built.PeakLoadPct,
                weeks = (info?.Weeks ?? new List<Week>()).Select(w => new
                {
                    weekNo = w.WeekNo, label = w.Label, from = w.From, to = w.To,
                    status = p.Weeks.TryGetValue(w.WeekNo, out var st) ? st : "Open",
                }).ToList(),
                jobCards = data.JobCards.Count(j => j.PlanId == p.MTransNo),
                reschedules = p.Reschedules,
            };
        }
    }
}
