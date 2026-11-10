using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// PurchaseReports/RMRequirementReport.html — raw-material bar needed for
    /// the pending orders (Basis = Pending) or for a production plan period
    /// (Basis = Plan), by RM part + grade (bar), allocated to stock, the stock
    /// reserved for open job cards and the open PO balances, with a
    /// purchase-requisition button for what is still short.
    ///
    ///   GET  GradeList?SubscID=
    ///   GET  Summary?Basis=Pending|Plan&AsOnDate=&PlanID=&Period=Month|Week|Day&WeekNo=&Date=&Grade=&Status=&SubscID=
    ///   GET  Lines?<same>
    ///   POST CreatePR?UserNo=&SubscID=   body [{ partNo, gradeNo, grade, dia, qtyKg, oaRefs, dueDate }]
    ///        → one Purchase Requisition (Draft) with a line per bar, each with its required-by date.
    /// Bar need of an OA line = qty × ConvBOM qty per piece (cut weight). Lines
    /// on customer-supplied material need no bar of ours ("Customer Material").
    /// </summary>
    [Route("api/[controller]")]
    public class RMRequirementController : PlanningControllerBase
    {
        public RMRequirementController(EMSContext context) : base(context) { }

        [HttpGet("GradeList")]
        public async Task<IActionResult> GradeList([FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            return await RunQuery(@"
                SELECT DISTINCT LTRIM(RTRIM(b.Grade)) AS grade
                  FROM dbo.ConvBOM b
                 WHERE b.SubscID = @SubscID AND b.DeleteStatus = 'N' AND ISNULL(b.Grade, '') <> ''
                 ORDER BY 1", Int("@SubscID", SubscID));
        }

        [HttpGet("Summary")]
        public async Task<IActionResult> Summary([FromQuery] string? Basis, [FromQuery] string? AsOnDate, [FromQuery] int? PlanID,
            [FromQuery] string? Period, [FromQuery] int? WeekNo, [FromQuery] string? Date, [FromQuery] string? Grade,
            [FromQuery] string? Status, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var (groups, _) = await Work(Basis, AsOnDate, PlanID ?? 0, Period, WeekNo ?? 0, Date, SubscID);
                var rows = groups.Where(r => GradeMatch(r["grade"], Grade) && StatusMatch((string?)r["status"], Status, true)).ToList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Lines")]
        public async Task<IActionResult> Lines([FromQuery] string? Basis, [FromQuery] string? AsOnDate, [FromQuery] int? PlanID,
            [FromQuery] string? Period, [FromQuery] int? WeekNo, [FromQuery] string? Date, [FromQuery] string? Grade,
            [FromQuery] string? Status, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var (_, lines) = await Work(Basis, AsOnDate, PlanID ?? 0, Period, WeekNo ?? 0, Date, SubscID);
                var rows = lines
                    .OrderBy(l => l.Grade, StringComparer.Ordinal).ThenBy(l => l.Dia)
                    .ThenBy(l => string.IsNullOrEmpty(l.NeededBy) ? l.DeliveryDate : l.NeededBy, StringComparer.Ordinal)
                    .Where(l => GradeMatch(l.Grade, Grade) && StatusMatch(l.Status, Status, false))
                    .Select(l => l.ToRow()).ToList();
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One Draft purchase requisition for the short bar (one line per RM part + grade).</summary>
        [HttpPost("CreatePR")]
        public async Task<IActionResult> CreatePR([FromQuery] int UserNo, [FromQuery] int SubscID, [FromBody] List<RMCreatePRLine>? lines)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (UserNo <= 0) return BadRequestResult("Invalid User.");
            var items = (lines ?? new()).Where(l => l.QtyKg > 0).ToList();
            if (items.Count == 0) return BadRequestResult("Nothing is short — no requisition to raise.");
            if (items.Any(l => l.PartNo <= 0 || l.GradeNo <= 0))
                return BadRequestResult("Every line needs its raw-material part and grade (from the Conversion BOM).");
            try
            {
                var series = S(await QueryOne(@"
                    SELECT TOP (1) LTRIM(RTRIM(MasterName)) AS s FROM dbo.OtherMaster
                     WHERE MasterType = 'DocSeries' AND SubscID = @SubscID AND DeleteStatus = 'N' AND ISNULL(LockStatus, 'Y') = 'Y'
                     ORDER BY MTransNo", Int("@SubscID", SubscID)), "s");
                if (series == "") return ConflictResult("No document series is set up (Other Master 'DocSeries').");
                var user = await QueryOne("SELECT ISNULL(UserName, UserId) AS n FROM dbo.UserMaster WHERE MTransNo = @U", Int("@U", UserNo));
                var due = items.Select(l => ParseDate(l.DueDate)).Where(d => d != null).OrderBy(d => d).FirstOrDefault() ?? DateTime.Today.AddDays(7);

                await using var tx = await db.Database.BeginTransactionAsync();
                var head = await db.CallPurReqMstSP(new SP_PurReqMst
                {
                    DocSeries = series, DocYear = DateTime.Today.Year, DocDate = DateTime.Today, DocAmdNo = 0,
                    ReqBy = S(user, "n"), ReqDept = "Production", Project = "Production Plan — RM Requirement",
                    DueDate = due, DocNotes = "Raised from RM Requirement for the short bar.", RFQReq = "N",
                    SystemIP = ClientIp, CreatedBy = UserNo, CreatedOn = DateTime.Now, LockStatus = "N", SubscID = SubscID,
                    Status = "Insert",
                });
                if (head.ID == 0) { await tx.RollbackAsync(); return ConflictResult(head.Message); }

                var slNo = 0;
                foreach (var l in items)
                {
                    var unit = S(await QueryOne("SELECT ISNULL(NULLIF(LTRIM(RTRIM(PrimaryUnit)), ''), 'KG') AS u FROM dbo.PartMaster WHERE MTransNo = @P",
                        Int("@P", l.PartNo)), "u");
                    var remarks = string.IsNullOrWhiteSpace(l.OaRefs) ? "RM Requirement" : "For " + l.OaRefs.Trim();
                    var line = await db.CallPurReqDtlSP(new SP_PurReqDtl
                    {
                        MTransNo = head.ID, SlNo = ++slNo, PartNo = l.PartNo, GradeNo = l.GradeNo, Grade = l.Grade,
                        Qty = Math.Ceiling(l.QtyKg), Unit = unit == "" ? "KG" : unit,
                        ItemRemarks = remarks.Length > 250 ? remarks[..250] : remarks,
                        SystemIP = ClientIp, CreatedBy = UserNo, CreatedOn = DateTime.Now, SubscID = SubscID, Status = "Insert",
                    });
                    if (line.ID == 0) { await tx.RollbackAsync(); return ConflictResult(line.Message); }
                    var plan = await db.CallPurReqDtlPlanFieldsSP(line.ID, ParseDate(l.DueDate), UserNo, SubscID);
                    if (plan.ID == 0) { await tx.RollbackAsync(); return ConflictResult(plan.Message); }
                }
                await tx.CommitAsync();
                var no = await QueryOne("SELECT CONCAT(RTRIM(DocSeries), '/', DocYear, '/', DocNo) AS no FROM dbo.PurReqMst WHERE MTransNo = @Id",
                    Int("@Id", head.ID));
                return OkResult($"OK^{items.Count}", "Purchase requisition " + S(no, "no") + " raised with " + items.Count + " line(s).");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<(List<Dictionary<string, object?>> Groups, List<NeedLine> Lines)> Work(string? basis, string? asOn,
            int planId, string? period, int weekNo, string? date, int subscID)
        {
            var data = await LoadPlanningData(subscID);
            var plans = await LoadPlans(subscID);
            if (basis != "Plan") return await PendingAllocation(data, plans, string.IsNullOrWhiteSpace(asOn) ? Today : asOn.Split('T')[0]);

            // Plan basis: the MAIN lines' bar in the period, needed by the first
            // day each line is forged in it; released lines draw on the bar
            // reserved for their job cards.
            var plan = plans.FirstOrDefault(p => p.MTransNo == planId);
            if (plan == null) return (new(), new());
            var info = await CalendarOf(plan.PlanMonth, data);
            var (from, to) = RangeOf(plan, info, string.IsNullOrWhiteSpace(period) ? "Month" : period, weekNo, (date ?? "").Split('T')[0]);
            var lines = new List<NeedLine>();
            var jobWork = new List<NeedLine>();
            foreach (var x in await LineFacts(plan, data))
            {
                var planQty = SumBetween(x.Planned, from, to);
                if (planQty <= 0) continue;
                // Only what is still to forge needs bar: forging already done
                // (in the period, or ahead of plan before it) is taken off.
                var (forgedCredit, _) = ForgedCredit(x, from, to);
                var qty = Math.Max(0, planQty - forgedCredit);
                // A released line draws on its own job card: the bar already
                // issued to it has left the store, so ask only for what is
                // still to be issued to that card.
                var jc = data.JobCardOf(plan.MTransNo, x.Seq, x.LineNo);
                var reqKg = x.CustomerMaterial ? 0 : R2(qty * x.CutWt);
                if (jc != null) reqKg = jc.Status == "Completed" ? 0 : R2(Math.Min(reqKg, Math.Max(0, jc.RMBalance)));
                if (qty <= 0 || (!x.CustomerMaterial && x.Bar.RMPartNo > 0 && reqKg <= 0)) continue;
                var first = x.Planned.Keys.Where(d => string.CompareOrdinal(d, from) >= 0 && string.CompareOrdinal(d, to) <= 0)
                                          .OrderBy(d => d, StringComparer.Ordinal).First();
                var l = new NeedLine
                {
                    PlanNo = plan.PlanNo, JobSeq = x.Seq, Planned = true, OAKey = x.OAKey,
                    OANo = x.Source == "OA" ? x.OANo : "Stock", Ref = x.Source == "OA" ? x.OANo : "Job " + x.Seq,
                    Customer = x.Customer, PartName = x.PartName, Grade = x.Bar.RMGrade, Dia = x.Bar.Dia, RMText = x.BarText,
                    BarKey = x.BarKey, RMPartNo = x.Bar.RMPartNo, RMGradeNo = x.Bar.RMGradeNo, DeliveryDate = x.DeliveryDate,
                    NeededBy = first, PendingQty = qty, PlanQty = planQty, CutWt = x.CutWt, Rate = x.Rate,
                    ReqKg = reqKg, JobWork = x.CustomerMaterial,
                    Released = x.JobCardNo != "",
                };
                if (l.JobWork) { l.Status = "Customer Material"; jobWork.Add(l); }
                else if (x.Bar.RMPartNo <= 0) { l.Status = "No BOM"; jobWork.Add(l); }
                else lines.Add(l);
            }
            lines = lines.OrderBy(l => l.NeededBy, StringComparer.Ordinal).ThenBy(l => l.JobSeq).ToList();
            var groups = await Allocate(data, lines, "");
            return (groups, lines.Concat(jobWork).ToList());
        }

        private static bool GradeMatch(object? grade, string? wanted) =>
            string.IsNullOrWhiteSpace(wanted) || string.Equals(grade?.ToString(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool StatusMatch(string? status, string? wanted, bool isGroup)
        {
            if (string.IsNullOrWhiteSpace(wanted) || wanted == "All") return true;
            if (isGroup && wanted == "On Order") return status == "Partial";
            return status == wanted;
        }

        private static DateTime? ParseDate(string? s) =>
            DateTime.TryParseExact((s ?? "").Split('T')[0], "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
}
