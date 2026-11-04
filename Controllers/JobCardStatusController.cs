using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Job Card Status — EMSSoft/Modules/Production/JobCardStatus.html.
    /// The production entries of a job card: which route step ran on which
    /// machine (and die), when, and how many pieces came out.
    ///
    ///     GetDetailsList?MTransNo                    the job card's live entries
    ///     SaveDtlData { mTransNo, entries[] }        REPLACES the whole entry set
    ///     RecDelete?MTransNo&amp;UserNo&amp;mReason         clears every entry
    ///
    /// The set is validated as a whole, so step progress and die usage are
    /// recomputed, never added twice:
    ///   - each entry's step belongs to the job card's route and its machine is
    ///     mapped to the step's operation;
    ///   - a die must be of the job card's grade and of the route's die type, and
    ///     its lifetime capacity (opening + every job card's use) is never exceeded;
    ///   - partial flow: a step's cumulative qty never exceeds the previous step's
    ///     (step 1 is capped by the job card qty);
    ///   - pieces already inspected must stay covered at the inspection step;
    ///   - recording the first entry of a job card is refused while another job
    ///     card of the same product is In Progress on the same die (SQL 58: a
    ///     second die of the product, e.g. on another hammer, may run in
    ///     parallel; when either job card has no die the same-product rule holds);
    ///   - an entry's rejected qty is between 0 and its processed qty.
    /// Validation and write run in one transaction with the job card row locked.
    /// </summary>
    [Route("api/[controller]")]
    public class JobCardStatusController : JobCardControllerBase
    {
        public JobCardStatusController(EMSContext context) : base(context) { }

        [HttpGet("GetDetailsList")]
        public Task<IActionResult> GetDetailsList([FromQuery] int MTransNo) =>
            RunQuery(@"
                SELECT  e.TransSlNo                         AS transSlNo,
                        e.SlNo                              AS slNo,
                        e.StepSlNo                          AS stepSlNo,
                        e.OperationNo                       AS operationNo,
                        op.MasterName                       AS operationName,
                        e.MachineNo                         AS machineNo,
                        m.MachineCode                       AS machineCode,
                        m.MachineName                       AS machineName,
                        ISNULL(e.DieNo, 0)                  AS dieNo,
                        dp.PartCode                         AS dieCode,
                        dp.PartName                         AS dieName,
                        CONVERT(char(10), e.EntryDate, 23)  AS entryDate,
                        e.StartTime                         AS startTime,
                        e.EndTime                           AS endTime,
                        e.SettingMin                        AS settingMin,
                        e.IdleMin                           AS idleMin,
                        e.Qty                               AS qty,
                        e.Remarks                           AS remarks,
                        ISNULL(e.Operator, '')              AS operator,
                        ISNULL(e.Shift, '')                 AS shift,
                        ISNULL(e.IdleReason, '')            AS idleReason,
                        ISNULL(e.RejectedQty, 0)            AS rejectedQty
                  FROM  dbo.JobCardEntry e
                  LEFT JOIN dbo.OtherMaster op ON op.MTransNo = e.OperationNo
                  LEFT JOIN dbo.MachineMst  m  ON m.MTransNo  = e.MachineNo
                  LEFT JOIN dbo.DieMst      d  ON d.MTransNo  = e.DieNo
                  LEFT JOIN dbo.PartMaster  dp ON dp.MTransNo = d.PartNo
                 WHERE  e.MTransNo = @MTransNo AND e.DeleteStatus = 'N'
                 ORDER  BY e.StepSlNo, e.EntryDate, e.StartTime, e.TransSlNo",
                Int("@MTransNo", MTransNo));

        [HttpPost("SaveDtlData")]
        public async Task<IActionResult> SaveDtlData([FromBody] JobCardEntrySaveRequest r)
        {
            if (r == null || r.MTransNo <= 0) return BadRequestResult("Invalid job card.");
            var entries = r.Entries ?? new List<JobCardEntryInput>();
            var userNo = r.UserNo > 0 ? r.UserNo : entries.Select(e => e.UserNo).FirstOrDefault(u => u > 0);
            if (userNo <= 0) return BadRequestResult("Invalid user.");

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                // Lock the job card for the whole validate-and-write.
                var jc = await QueryOne(@"
                    SELECT j.MTransNo, j.JobCardNo, j.PartNo, j.GradeNo, j.Grade, j.Qty, j.SubscID,
                           ISNULL(j.DieNo, 0) AS DieNo,
                           p.PartCode, LTRIM(RTRIM(ISNULL(r.DieType, ''))) AS DieType,
                           (SELECT COUNT(*) FROM dbo.JobCardEntry e
                             WHERE e.MTransNo = j.MTransNo AND e.DeleteStatus = 'N') AS EntryCount
                      FROM dbo.JobCardMst j WITH (UPDLOCK, ROWLOCK)
                      LEFT JOIN dbo.PartMaster p      ON p.MTransNo = j.PartNo
                      LEFT JOIN dbo.RoutingChartMst r ON r.MTransNo = j.RouteNo
                     WHERE j.MTransNo = @M AND j.DeleteStatus = 'N'",
                    Int("@M", r.MTransNo));
                if (jc == null) return NotFoundResult("Job card not found.");
                if (r.SubscID > 0 && r.SubscID != I(jc, "SubscID")) return NotFoundResult("Job card not found.");

                var subscID = I(jc, "SubscID");
                var jobNo = S(jc, "JobCardNo");
                var jobQty = D(jc, "Qty");

                // ── One job card per die in progress at a time ────────────
                // Same product AND (either card has no die OR the same die).
                if (I(jc, "EntryCount") == 0 && entries.Count > 0)
                {
                    var blocker = await QueryOne(@"
                        SELECT TOP (1) j.JobCardNo, ISNULL(j.DieNo, 0) AS DieNo FROM dbo.JobCardMst j
                          JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
                         WHERE j.SubscID = @S AND j.PartNo = @P AND j.MTransNo <> @M
                           AND j.DeleteStatus = 'N' AND s.JobStatus = 'In Progress'
                           AND (@D = 0 OR j.DieNo IS NULL OR j.DieNo = @D)
                         ORDER BY j.MTransNo",
                        Int("@S", subscID), Int("@P", I(jc, "PartNo")), Int("@M", r.MTransNo), Int("@D", I(jc, "DieNo")));
                    if (blocker != null)
                        return ConflictResult(
                            $"{S(blocker, "JobCardNo")} for {S(jc, "PartCode")} is still In Progress" +
                            (I(jc, "DieNo") > 0 && I(blocker, "DieNo") > 0 ? " on the same die" : "") +
                            $". Complete it before starting {jobNo}.");
                }

                // ── Reference data ────────────────────────────────────────
                var steps = await Query(@"
                    SELECT v.SlNo, v.OperationNo, v.OperationName, v.IsInspection
                      FROM dbo.VW_JobCardStep v WHERE v.JobCardTransNo = @M ORDER BY v.SlNo",
                    Int("@M", r.MTransNo));
                var machines = (await Query(@"
                    SELECT MTransNo, MachineCode, ISNULL(Operation, '') AS Operation
                      FROM dbo.MachineMst WHERE SubscID = @S AND DeleteStatus = 'N'",
                    Int("@S", subscID))).ToDictionary(m => I(m, "MTransNo"));
                var dies = (await Query(@"
                    SELECT d.MTransNo, d.GradeNo, LTRIM(RTRIM(ISNULL(d.DieType, ''))) AS DieType,
                           ISNULL(d.MaxQty, 0) AS MaxQty, ISNULL(d.OpeningQty, 0) AS OpeningQty,
                           ISNULL(p.PartCode, CONCAT('Die ', d.MTransNo)) AS DieCode,
                           ISNULL((SELECT SUM(e.Qty) FROM dbo.JobCardEntry e
                                    WHERE e.DieNo = d.MTransNo AND e.DeleteStatus = 'N' AND e.MTransNo <> @M), 0) AS OtherUse,
                           (SELECT TOP (1) g.Grade FROM dbo.MaterialGrade g
                             WHERE g.PartNo = d.PartNo AND g.GradeNo = d.GradeNo AND g.DeleteStatus = 'N') AS Grade
                      FROM dbo.DieMst d
                      LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                     WHERE d.SubscID = @S AND d.DeleteStatus = 'N'",
                    Int("@S", subscID), Int("@M", r.MTransNo))).ToDictionary(d => I(d, "MTransNo"));

                // ── Each entry ────────────────────────────────────────────
                var rows = new List<object>();
                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var label = $"Entry {i + 1}";
                    var st = steps.FirstOrDefault(s => I(s, "SlNo") == e.StepSlNo);
                    if (st == null) return ConflictResult($"{label}: select an operation of this route.");
                    var opNo = I(st, "OperationNo");
                    var opName = S(st, "OperationName");

                    if (!machines.TryGetValue(e.MachineNo, out var mc) ||
                        !S(mc, "Operation").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Contains(opNo.ToString(CultureInfo.InvariantCulture)))
                        return ConflictResult($"{label}: the machine is not mapped to {opName}.");

                    if (e.DieNo > 0)
                    {
                        if (!dies.TryGetValue(e.DieNo, out var die)) return ConflictResult($"{label}: the die no longer exists.");
                        if (I(jc, "GradeNo") != 0 && I(die, "GradeNo") != 0 && I(die, "GradeNo") != I(jc, "GradeNo"))
                            return ConflictResult(
                                $"{label}: {S(die, "DieCode")} is for grade {S(die, "Grade")}, the job card is grade {S(jc, "Grade")}.");
                        if (S(jc, "DieType") != "" &&
                            !string.Equals(S(die, "DieType"), S(jc, "DieType"), StringComparison.OrdinalIgnoreCase))
                            return ConflictResult($"{label}: {S(die, "DieCode")} is not a {S(jc, "DieType")}.");
                    }

                    if (e.EntryDate == null) return ConflictResult($"{label}: Operation Date is required.");
                    if (e.EntryDate.Value.Date > DateTime.Today) return ConflictResult($"{label}: Operation Date cannot be in the future.");
                    var a = MinutesOf(e.StartTime);
                    var b = MinutesOf(e.EndTime);
                    if (a < 0 || b < 0) return ConflictResult($"{label}: enter valid start and end times.");
                    if (b <= a) return ConflictResult($"{label}: End Time must be after Start Time.");
                    if (e.SettingMin < 0 || e.IdleMin < 0) return ConflictResult($"{label}: setting and idle time cannot be negative.");
                    if (e.Qty <= 0) return ConflictResult($"{label}: Process Quantity must be greater than zero.");
                    if (e.RejectedQty < 0 || e.RejectedQty > e.Qty)
                        return ConflictResult($"{label}: Rejected Qty must be between 0 and the Process Quantity ({Q(e.Qty)}).");

                    rows.Add(new
                    {
                        stepSlNo = e.StepSlNo,
                        operationNo = opNo,
                        machineNo = e.MachineNo,
                        dieNo = e.DieNo,
                        entryDate = e.EntryDate.Value.ToString("yyyy-MM-dd"),
                        startTime = e.StartTime!.Trim().PadLeft(5, '0'),
                        endTime = e.EndTime!.Trim().PadLeft(5, '0'),
                        settingMin = e.SettingMin,
                        idleMin = e.IdleMin,
                        qty = e.Qty,
                        remarks = string.IsNullOrWhiteSpace(e.Remarks) ? null : Trunc(e.Remarks, 250),
                        // SQL 58 — written by SP_JobCardEntryPlanFields from this same JSON.
                        @operator = Trunc(e.Operator, 50),
                        shift = Trunc(e.Shift, 50),
                        idleReason = Trunc(e.IdleReason, 100),
                        rejectedQty = e.RejectedQty,
                    });
                }

                // ── Partial flow: each step capped by the one before ──────
                var cum = entries.GroupBy(e => e.StepSlNo).ToDictionary(g => g.Key, g => g.Sum(e => e.Qty));
                var prev = jobQty;
                for (var k = 0; k < steps.Count; k++)
                {
                    var slNo = I(steps[k], "SlNo");
                    var done = cum.TryGetValue(slNo, out var q) ? q : 0m;
                    if (done > prev)
                        return ConflictResult(
                            $"Step {slNo} – {S(steps[k], "OperationName")}: {Q(done)} processed, but only {Q(prev)} " +
                            (k == 0 ? "on the job card." : $"came out of step {I(steps[k - 1], "SlNo")}."));
                    prev = done;
                }

                // ── Die capacity (lifetime, in pieces) ────────────────────
                foreach (var g in entries.Where(e => e.DieNo > 0).GroupBy(e => e.DieNo))
                {
                    var die = dies[g.Key];
                    var total = D(die, "OpeningQty") + D(die, "OtherUse") + g.Sum(e => e.Qty);
                    if (total > D(die, "MaxQty"))
                        return ConflictResult(
                            $"{S(die, "DieCode")} would reach {Q(total)} pieces, above its capacity of {Q(D(die, "MaxQty"))}.");
                }

                // ── Inspection already recorded stays covered ─────────────
                var insp = steps.FirstOrDefault(s => I(s, "IsInspection") == 1);
                var eligible = insp != null && cum.TryGetValue(I(insp, "SlNo"), out var iq) ? iq : 0m;
                var inspected = D(await QueryOne(@"
                    SELECT ISNULL(SUM(InspectedQty), 0) AS Q FROM dbo.JobCardInspection
                     WHERE JobCardTransNo = @M AND DeleteStatus = 'N'", Int("@M", r.MTransNo)), "Q");
                if (inspected > eligible)
                    return ConflictResult(
                        $"{Q(inspected)} pieces are already inspected, but only {Q(eligible)} would have reached step " +
                        $"{(insp == null ? "" : I(insp, "SlNo").ToString())}. Correct the inspection first.");

                var json = JsonSerializer.Serialize(rows);
                var result = await db.CallJobCardEntrySaveSP(r.MTransNo, false, json, null, userNo, subscID);
                if (result.ID == 0) return ConflictResult(result.Message);

                // Operator / shift / idle reason / rejected qty per entry (SQL 58).
                var plan = await db.CallJobCardEntryPlanFieldsSP(r.MTransNo, json, userNo, subscID);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                await tx.CommitAsync();
                return OkResult($"OK^{r.MTransNo}", "Record Saved Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("RecDelete")]
        public async Task<IActionResult> RecDelete([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] string? mReason)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid job card.");
            if (UserNo <= 0) return BadRequestResult("Invalid user.");
            if (string.IsNullOrWhiteSpace(mReason)) return BadRequestResult("Enter the reason for clearing the entries.");
            try
            {
                var jc = await QueryOne("SELECT SubscID FROM dbo.JobCardMst WHERE MTransNo = @M AND DeleteStatus = 'N'",
                    Int("@M", MTransNo));
                if (jc == null) return NotFoundResult("Job card not found.");

                var result = await db.CallJobCardEntrySaveSP(
                    MTransNo, true, "[]", Trunc(mReason, 250), UserNo, I(jc, "SubscID"));
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", "Production entries cleared");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>"HH:MM" → minutes since midnight, or -1.</summary>
        private static int MinutesOf(string? hhmm)
        {
            var parts = (hhmm ?? "").Trim().Split(':');
            if (parts.Length != 2 || parts[1].Length != 2 ||
                !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) ||
                h < 0 || h > 23 || m < 0 || m > 59)
                return -1;
            return h * 60 + m;
        }

        private static string Trunc(string? s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
