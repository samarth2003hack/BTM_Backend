using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Shared plumbing for the Job Card chain (JobCard, JobCardStatus,
    /// JobCardInspection): inline reads that join the job card to its part,
    /// route, OA and the progress views of SQL/53_JobCard.sql.
    ///
    /// Reads run on the context's connection and enlist in its current
    /// transaction, so a save can validate and write under one lock.
    /// Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    public abstract class JobCardControllerBase : EmsControllerBase
    {
        protected readonly EMSContext db;
        protected JobCardControllerBase(EMSContext context) => db = context;

        /// <summary>
        /// One row per live job card, decorated with everything the three
        /// pages show: product, route, OA, status, progress, inspection
        /// position and raw-material issue. Filter with a WHERE on alias j.
        /// </summary>
        protected const string JobCardSelect = @"
            SELECT  j.MTransNo                              AS mTransNo,
                    j.JobCardNo                             AS jobCardNo,
                    CONVERT(char(10), j.JobDate, 23)        AS jobDate,
                    j.OAYear                                AS oaYear,
                    j.OANo                                  AS oaNo,
                    j.OAID                                  AS oaID,
                    CONCAT(j.OAYear, '-', j.OANo, '/', j.OAID) AS oaLabel,
                    oa.VendorName                           AS customer,
                    j.PartNo                                AS partNo,
                    p.PartCode                              AS partCode,
                    p.PartName                              AS partName,
                    j.GradeNo                               AS gradeNo,
                    j.Grade                                 AS grade,
                    j.RouteNo                               AS routeNo,
                    r.RouteName                             AS routeName,
                    r.BatchQty                              AS batchQty,
                    dt.MTransNo                             AS dieTypeNo,
                    r.DieType                               AS dieTypeName,
                    j.Qty                                   AS qty,
                    j.RMPartNo                              AS rmPartNo,
                    rp.PartCode                             AS rmPartCode,
                    rp.PartName                             AS rmPartName,
                    j.RMGradeNo                             AS rmGradeNo,
                    j.RMGrade                               AS rmGrade,
                    j.RMQty                                 AS rmQty,
                    j.RMUnit                                AS rmUnit,
                    j.Operator                              AS operator,
                    j.Shift                                 AS shift,
                    j.Remarks                               AS remarks,
                    s.JobStatus                             AS status,
                    s.StepCount                             AS stepCount,
                    s.TotalTimeMin                          AS totalTimeMin,
                    s.CompletedQty                          AS completedQty,
                    s.BalanceQty                            AS balanceQty,
                    s.CurrentSlNo                           AS currentSlNo,
                    s.CurrentStep                           AS currentStep,
                    s.EntryCount                            AS entryCount,
                    CONVERT(char(10), s.LastEntryDate, 23)  AS lastEntryDate,
                    s.InspectionSlNo                        AS inspectionStepSlNo,
                    s.InspectionStep                        AS inspectionStepLabel,
                    s.InspectionEligibleQty                 AS inspectionEligibleQty,
                    s.InspectedQty                          AS inspectedQty,
                    s.OKQty                                 AS okQty,
                    s.ReworkQty                             AS reworkQty,
                    s.RejectedQty                           AS rejectedQty,
                    s.AwaitingInspectionQty                 AS awaitingInspectionQty,
                    s.InspectionCount                       AS inspectionCount,
                    s.RMIssuedQty                           AS rmIssuedQty,
                    s.RMBalanceQty                          AS rmBalanceQty,
                    CONVERT(char(10), j.CreatedOn, 23)      AS createdOn
              FROM  dbo.JobCardMst j
              JOIN  dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
              LEFT JOIN dbo.PartMaster p        ON p.MTransNo  = j.PartNo
              LEFT JOIN dbo.PartMaster rp       ON rp.MTransNo = j.RMPartNo
              LEFT JOIN dbo.RoutingChartMst r   ON r.MTransNo  = j.RouteNo
              OUTER APPLY (SELECT TOP (1) o.MTransNo FROM dbo.OtherMaster o
                            WHERE o.MasterType = 'Die Type' AND o.SubscID = j.SubscID
                              AND o.DeleteStatus = 'N' AND o.MasterName = r.DieType
                            ORDER BY o.MTransNo) dt
              OUTER APPLY (SELECT TOP (1) m.VendorName FROM dbo.OAMst m
                            WHERE m.SubscID = j.SubscID AND m.DocYear = j.OAYear AND m.DocNo = j.OANo
                              AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                            ORDER BY m.MTransNo DESC) oa
             WHERE  j.DeleteStatus = 'N'";

        /// <summary>Route steps of one job card with their progress (VW_JobCardStep).</summary>
        protected const string StepSelect = @"
            SELECT  v.SlNo              AS slNo,
                    v.OperationNo       AS operationNo,
                    v.OperationName     AS operationName,
                    v.MachineNo         AS machineNo,
                    v.MachineCode       AS machineCode,
                    v.MachineName       AS machineName,
                    v.TimeMin           AS timeMin,
                    v.ProcessedQty      AS processedQty,
                    v.PrevQty           AS prevQty,
                    v.AvailableQty      AS availableQty,
                    v.BalanceQty        AS balanceQty,
                    v.StepState         AS state,
                    CAST(CASE WHEN v.SlNo = s.CurrentSlNo THEN 1 ELSE 0 END AS bit) AS isCurrent,
                    CAST(v.IsInspection AS bit) AS isInspection
              FROM  dbo.VW_JobCardStep v
              JOIN  dbo.VW_JobCardSummary s ON s.JobCardTransNo = v.JobCardTransNo
             WHERE  v.JobCardTransNo = @MTransNo
             ORDER  BY v.SlNo";

        /// <summary>Runs a read on the context connection (in its transaction, if any).</summary>
        protected async Task<List<Dictionary<string, object?>>> Query(string sql, params SqlParameter[] parameters)
        {
            var con = db.Database.GetDbConnection();
            if (con.State != ConnectionState.Open)
                await con.OpenAsync();

            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var p in parameters) cmd.Parameters.Add(p);

            var rows = new List<Dictionary<string, object?>>();
            await using (DbDataReader reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = value is string s ? s.Trim() : value;
                    }
                    rows.Add(row);
                }
            }
            cmd.Parameters.Clear();
            return rows;
        }

        protected async Task<Dictionary<string, object?>?> QueryOne(string sql, params SqlParameter[] parameters) =>
            (await Query(sql, parameters)).FirstOrDefault();

        /// <summary>Read wrapped in the response envelope.</summary>
        protected async Task<IActionResult> RunQuery(string sql, params SqlParameter[] parameters)
        {
            try
            {
                return OkResult(await Query(sql, parameters));
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>One decorated job card, or null.</summary>
        protected Task<Dictionary<string, object?>?> JobCardRow(int mTransNo) =>
            QueryOne(JobCardSelect + " AND j.MTransNo = @MTransNo", Int("@MTransNo", mTransNo));

        protected static SqlParameter Int(string name, int value) =>
            new(name, SqlDbType.Int) { Value = value };

        protected static SqlParameter Str(string name, string? value, int size) =>
            new(name, SqlDbType.VarChar, size) { Value = (object?)value ?? DBNull.Value };

        protected static int I(Dictionary<string, object?>? row, string key) =>
            row != null && row.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;

        protected static decimal D(Dictionary<string, object?>? row, string key) =>
            row != null && row.TryGetValue(key, out var v) && v != null ? Convert.ToDecimal(v) : 0m;

        protected static string S(Dictionary<string, object?>? row, string key) =>
            row != null && row.TryGetValue(key, out var v) && v != null ? Convert.ToString(v)!.Trim() : "";

        /// <summary>Quantity as people write it: 100, 12.5 — never 100.000.</summary>
        protected static string Q(decimal q) => q.ToString("0.###");

        /// <summary>The grade row of a part, matched on its text (case-insensitive).</summary>
        protected async Task<(int GradeNo, string Grade)?> GradeOf(int partNo, string? grade, int subscID)
        {
            var g = (grade ?? "").Trim();
            if (g == "") return null;
            var row = await QueryOne(@"
                SELECT TOP (1) GradeNo, Grade FROM dbo.MaterialGrade
                 WHERE PartNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N'
                   AND LTRIM(RTRIM(Grade)) = @Grade
                 ORDER BY CASE WHEN LockStatus = 'Y' THEN 0 ELSE 1 END, MTransNo",
                Int("@PartNo", partNo), Int("@SubscID", subscID), Str("@Grade", g, 50));
            return row == null ? null : (I(row, "GradeNo"), S(row, "Grade"));
        }
    }

    /// <summary>
    /// Job Card — EMSSoft/Modules/Production/JobCard.html.
    ///
    ///     GetList/{subscID}               every live job card, decorated (also read by
    ///                                     Job Card Status, Inspection and Store Issue)
    ///     GetMasterData?MTransNo          one job card + its route steps with progress
    ///     GetMaxNo?DocDate&amp;SubscID        preview of the next JC-&lt;FY&gt;-&lt;no&gt;
    ///     SaveMasterData                  Status 'Insert' | 'Update' (Open job cards only)
    ///     RecDelete?MTransNo&amp;UserNo&amp;mReason  soft delete (Open, nothing issued)
    ///     GetOAYear / GetOANo / GetOAID / GetPartLoad   the OA cascade (live current OA)
    ///
    /// The raw-material picker reads the Conversion BOM API (api/ConvBOM/GetRMList).
    ///
    /// Rules (mirrors the confirmed production rules):
    ///   - the route must belong to the product and be of the job card's grade;
    ///   - the OA line (live, current revision; OA ID = OADtl.SlNo) must order the product;
    ///   - the raw material must be a grade-carrying part; when the product has a
    ///     Conversion BOM the raw material must be one of its lines;
    ///   - a job card can be edited or deleted only while Open (no production entries).
    /// </summary>
    [Route("api/[controller]")]
    public class JobCardController : JobCardControllerBase
    {
        public JobCardController(EMSContext context) : base(context) { }

        // ============================================================
        // READ
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return Task.FromResult(BadRequestResult("Invalid Subscription."));
            return RunQuery(JobCardSelect + " AND j.SubscID = @SubscID ORDER BY j.MTransNo DESC",
                Int("@SubscID", subscID));
        }

        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData([FromQuery] int MTransNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid job card.");
            try
            {
                var row = await JobCardRow(MTransNo);
                if (row == null) return NotFoundResult("Job card not found.");
                row["steps"] = await Query(StepSelect, Int("@MTransNo", MTransNo));
                return OkResult(row);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Preview only — the save allocates the number under a lock.</summary>
        [HttpGet("GetMaxNo")]
        public async Task<IActionResult> GetMaxNo([FromQuery] DateTime? DocDate, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var row = await QueryOne(@"
                    DECLARE @Year INT = dbo.FN_FYCode(@Date);
                    SELECT CONCAT('JC-', @Year, '-', RIGHT('00000' + CAST(ISNULL(MAX(DocNo), 0) + 1 AS VARCHAR(10)), 5)) AS No
                      FROM dbo.JobCardMst WHERE SubscID = @SubscID AND DocYear = @Year",
                    new SqlParameter("@Date", SqlDbType.Date) { Value = (DocDate ?? DateTime.Today).Date },
                    Int("@SubscID", SubscID));
                return OkResult(S(row, "No"));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // OA CASCADE — live, current OA revisions (same rule as FN_OALinkCheck)
        // ============================================================

        // FROM/WHERE of every live, current-revision OA line.
        private const string LiveOALines = @"
              FROM dbo.OAMst m
              JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
             WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'";

        [HttpGet("GetOAYear")]
        public Task<IActionResult> GetOAYear([FromQuery] int SubscID) =>
            RunQuery("SELECT DISTINCT m.DocYear AS oaYear" + LiveOALines + " ORDER BY m.DocYear DESC",
                Int("@SubscID", SubscID));

        [HttpGet("GetOANo")]
        public Task<IActionResult> GetOANo([FromQuery] int OAYear, [FromQuery] int SubscID) =>
            RunQuery("SELECT DISTINCT m.DocNo AS oaNo" + LiveOALines + " AND m.DocYear = @OAYear ORDER BY m.DocNo DESC",
                Int("@SubscID", SubscID), Int("@OAYear", OAYear));

        [HttpGet("GetOAID")]
        public Task<IActionResult> GetOAID([FromQuery] int OAYear, [FromQuery] int OANo, [FromQuery] int SubscID) =>
            RunQuery(@"SELECT CAST(d.SlNo AS INT) AS oaID, d.PartNo AS partNo, d.PartCode AS partCode,
                              d.PartDescription AS partName, d.Qty AS qty" + LiveOALines +
                     " AND m.DocYear = @OAYear AND m.DocNo = @OANo ORDER BY d.SlNo",
                Int("@SubscID", SubscID), Int("@OAYear", OAYear), Int("@OANo", OANo));

        /// <summary>The part and quantity one OA line orders, and what is already on job cards.</summary>
        [HttpGet("GetPartLoad")]
        public async Task<IActionResult> GetPartLoad(
            [FromQuery] int OAYear, [FromQuery] int OANo, [FromQuery] int OAID, [FromQuery] int SubscID)
        {
            try
            {
                var row = await OALine(OAYear, OANo, OAID, SubscID);
                return row == null ? NotFoundResult("Order acceptance line not found.") : OkResult(row);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One live OA line with its part, and the quantity already on job cards.</summary>
        private Task<Dictionary<string, object?>?> OALine(int oaYear, int oaNo, int oaId, int subscID) =>
            QueryOne(@"
                SELECT TOP (1) d.TransSlNo AS oaLinkNo, m.DocYear AS oaYear, m.DocNo AS oaNo,
                       CAST(d.SlNo AS INT) AS oaID, d.PartNo AS partNo, p.PartCode AS partCode,
                       p.PartName AS partName, d.Qty AS qty, d.Unit AS unit, m.VendorName AS customer,
                       ISNULL((SELECT SUM(j.Qty) FROM dbo.JobCardMst j
                                WHERE j.SubscID = m.SubscID AND j.DeleteStatus = 'N'
                                  AND j.OAYear = m.DocYear AND j.OANo = m.DocNo AND j.OAID = d.SlNo), 0) AS jobCardQty
                  FROM dbo.OAMst m
                  JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                   AND m.DocYear = @OAYear AND m.DocNo = @OANo AND d.SlNo = @OAID
                 ORDER BY m.MTransNo DESC",
                Int("@SubscID", subscID), Int("@OAYear", oaYear), Int("@OANo", oaNo), Int("@OAID", oaId));

        /// <summary>
        /// Raw-material part numbers in the live, Active Conversion BOM of
        /// @PartNo / @SubscID (SQL/54 ConvBOM: FPartNo = finished part, PartNo = RM).
        /// Empty when the product has no BOM — the job card then accepts any raw material.
        /// </summary>
        private const string BomSql = @"
            SELECT DISTINCT b.PartNo AS partNo
              FROM dbo.ConvBOM b
             WHERE b.FPartNo = @PartNo AND b.SubscID = @SubscID
               AND b.DeleteStatus = 'N' AND b.LockStatus = 'Y'";

        // ============================================================
        // SAVE / DELETE
        // ============================================================

        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] JobCardSaveRequest r)
        {
            if (r == null) return BadRequestResult("Invalid data.");
            if (r.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (r.UserNo <= 0) return BadRequestResult("Invalid user.");
            var isUpdate = string.Equals(r.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (!isUpdate && !string.Equals(r.Status, "Insert", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Status must be Insert or Update.");
            if (isUpdate && r.MTransNo <= 0) return BadRequestResult("Invalid job card.");

            try
            {
                // ── Product and route ──────────────────────────────────
                var part = await QueryOne(@"SELECT PartCode FROM dbo.PartMaster
                                             WHERE MTransNo = @P AND SubscID = @S AND DeleteStatus = 'N'",
                    Int("@P", r.PartNo), Int("@S", r.SubscID));
                if (part == null) return ConflictResult("Select a valid finished product.");

                var route = await QueryOne(@"
                    SELECT r.PartNo, r.GradeNo, r.RouteName,
                           (SELECT COUNT(*) FROM dbo.RoutingChartDtl d WHERE d.MTransNo = r.MTransNo AND d.DeleteStatus = 'N') AS Steps,
                           (SELECT TOP (1) g.Grade FROM dbo.MaterialGrade g
                             WHERE g.PartNo = r.PartNo AND g.GradeNo = r.GradeNo AND g.DeleteStatus = 'N') AS Grade
                      FROM dbo.RoutingChartMst r
                     WHERE r.MTransNo = @R AND r.SubscID = @S AND r.DeleteStatus = 'N'",
                    Int("@R", r.RouteNo), Int("@S", r.SubscID));
                if (route == null) return ConflictResult("Select a route.");
                if (I(route, "PartNo") != r.PartNo) return ConflictResult("The selected route does not belong to this product.");
                if (I(route, "Steps") == 0) return ConflictResult("The selected route has no operations.");
                if (r.Qty <= 0) return ConflictResult("Job Card Quantity must be greater than zero.");
                if (r.JobDate == null) return ConflictResult("Job Card Date is required.");

                // ── OA line ────────────────────────────────────────────
                var oa = await OALine(r.OAYear, r.OANo, r.OAID, r.SubscID);
                if (oa == null) return ConflictResult("Select the order acceptance (OA Year, OA No and OA ID).");
                if (I(oa, "partNo") != r.PartNo) return ConflictResult("The OA line does not order this product.");

                // ── Grade (resolved to MaterialGrade) ──────────────────
                if (string.IsNullOrWhiteSpace(r.Grade)) return ConflictResult("Grade is required.");
                var grade = await GradeOf(r.PartNo, r.Grade, r.SubscID);
                if (grade == null)
                    return ConflictResult($"{r.Grade!.Trim()} is not a grade of {S(part, "PartCode")}.");
                if (I(route, "GradeNo") != 0 && I(route, "GradeNo") != grade.Value.GradeNo)
                    return ConflictResult(
                        $"The route “{S(route, "RouteName")}” is for grade {S(route, "Grade")} — pick a route of grade {grade.Value.Grade}.");

                // ── Raw material ───────────────────────────────────────
                var rm = await QueryOne(@"SELECT PartCode, ISNULL(NULLIF(LTRIM(RTRIM(TxnUnit)), ''), PrimaryUnit) AS Unit
                                            FROM dbo.PartMaster
                                           WHERE MTransNo = @P AND SubscID = @S AND DeleteStatus = 'N'",
                    Int("@P", r.RMPartNo), Int("@S", r.SubscID));
                if (rm == null) return ConflictResult("Select the raw material.");
                var bom = await Query(BomSql, Int("@PartNo", r.PartNo), Int("@SubscID", r.SubscID));
                if (bom.Count > 0 && !bom.Any(b => I(b, "partNo") == r.RMPartNo))
                    return ConflictResult("The raw material is not in this product's Conversion BOM.");
                if (string.IsNullOrWhiteSpace(r.RMGrade)) return ConflictResult("RM Grade is required.");
                var rmGrade = await GradeOf(r.RMPartNo, r.RMGrade, r.SubscID);
                if (rmGrade == null)
                    return ConflictResult($"{r.RMGrade!.Trim()} is not a grade of {S(rm, "PartCode")}.");
                if (r.RMQty <= 0) return ConflictResult("RM Quantity must be greater than zero.");
                if (string.IsNullOrWhiteSpace(r.Operator)) return ConflictResult("Operator Name is required.");
                if (string.IsNullOrWhiteSpace(r.Shift)) return ConflictResult("Shift is required.");

                var result = await db.CallJobCardMstSP(new SP_JobCardMst
                {
                    Status = isUpdate ? "Update" : "Insert",
                    MTransNo = isUpdate ? r.MTransNo : 0,
                    JobDate = r.JobDate.Value.Date,
                    OAYear = r.OAYear,
                    OANo = r.OANo,
                    OAID = r.OAID,
                    OALinkNo = I(oa, "oaLinkNo"),
                    PartNo = r.PartNo,
                    GradeNo = grade.Value.GradeNo,
                    Grade = grade.Value.Grade,
                    RouteNo = r.RouteNo,
                    Qty = r.Qty,
                    RMPartNo = r.RMPartNo,
                    RMGradeNo = rmGrade.Value.GradeNo,
                    RMGrade = rmGrade.Value.Grade,
                    RMQty = Math.Round(r.RMQty, 3),
                    RMUnit = S(rm, "Unit"),
                    Operator = r.Operator!.Trim(),
                    Shift = r.Shift!.Trim(),
                    Remarks = string.IsNullOrWhiteSpace(r.Remarks) ? null : r.Remarks.Trim(),
                    SystemIP = ClientIp,
                    UserNo = r.UserNo,
                    SubscID = r.SubscID,
                });

                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", isUpdate ? "Record Edited Successfully" : "Record Saved Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("RecDelete")]
        public async Task<IActionResult> RecDelete([FromQuery] int MTransNo, [FromQuery] int UserNo, [FromQuery] string? mReason)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid job card.");
            if (UserNo <= 0) return BadRequestResult("Invalid user.");
            if (string.IsNullOrWhiteSpace(mReason)) return BadRequestResult("Enter the reason for deleting.");
            try
            {
                var row = await QueryOne("SELECT SubscID FROM dbo.JobCardMst WHERE MTransNo = @M AND DeleteStatus = 'N'",
                    Int("@M", MTransNo));
                if (row == null) return NotFoundResult("Job card not found.");

                var result = await db.CallJobCardMstSP(new SP_JobCardMst
                {
                    Status = "Delete",
                    MTransNo = MTransNo,
                    JobDate = DateTime.Today,
                    UserNo = UserNo,
                    DeleteReason = mReason.Trim().Length > 250 ? mReason.Trim()[..250] : mReason.Trim(),
                    SystemIP = ClientIp,
                    SubscID = I(row, "SubscID"),
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", "Record Deleted Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
