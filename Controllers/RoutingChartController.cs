using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Routing Chart (Masters/RoutingChart.html; read by the Job Card page too).
    /// A route is a finished part + grade with an ordered list of steps, each an
    /// Other Master 'Operation' on a machine mapped to it. Rules live in
    /// dbo.SP_RoutingChartMst and dbo.SP_RoutingChartSteps (SQL 54):
    /// route name unique per part + grade; while live job cards use a route its
    /// part, grade and steps are locked and it cannot be deleted.
    /// Saves answer "OK^&lt;MTransNo&gt;"; a refusal is a 409 with the reason.
    /// </summary>
    [Route("api/[controller]")]
    public class RoutingChartController : EmsControllerBase
    {
        private readonly EMSContext db;

        public RoutingChartController(EMSContext context) => db = context;

        // ============================================================
        // SAVE MASTER DATA — the route header
        // Body: { mTransNo, partNo, grade (text) | gradeNo, routeName,
        //         batchQty, userNo, subscID, status }
        // ============================================================

        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] RouteSaveRequest dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var userNo = dto.UserNo > 0 ? dto.UserNo : dto.CreatedBy;
            if (userNo <= 0)
                return BadRequestResult("Invalid User.");

            var isUpdate = string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (isUpdate && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (dto.PartNo <= 0)
                return ConflictResult("Select a valid finished product.");

            try
            {
                // Grade text → MaterialGrade.GradeNo of this part.
                var gradeNo = dto.GradeNo;
                var gradeText = (dto.Grade ?? "").Trim();
                if (gradeNo <= 0 && gradeText != "")
                {
                    gradeNo = await db.MaterialGrade.AsNoTracking()
                        .Where(g => g.PartNo == dto.PartNo && g.Grade == gradeText &&
                                    g.SubscID == dto.SubscID && g.DeleteStatus == "N")
                        .Select(g => g.GradeNo)
                        .FirstOrDefaultAsync();
                    if (gradeNo <= 0)
                        return ConflictResult($"{gradeText} is not a grade of the selected product.");
                }
                if (gradeNo <= 0)
                    return ConflictResult("Grade is required.");

                var now = DateTime.Now;
                var result = await db.CallRoutingChartMstSP(new SP_RoutingChartMst
                {
                    MTransNo   = isUpdate ? dto.MTransNo : 0,
                    PartNo     = dto.PartNo,
                    GradeNo    = gradeNo,
                    RouteName  = (dto.RouteName ?? "").Trim(),
                    BatchQty   = dto.BatchQty,
                    DieType    = "",               // the SP copies it from the part + grade die
                    LockStatus = dto.LockStatus == "N" ? "N" : "Y",
                    CreatedBy  = userNo,
                    CreatedOn  = now,
                    ModifiedBy = userNo,
                    ModifiedOn = now,
                    SubscID    = dto.SubscID,
                    Status     = isUpdate ? "Update" : "Insert"
                });

                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SAVE DTL DATA — replaces the route's whole step list atomically
        // Body: { mTransNo, steps: [{ slNo, operationNo, machineNo, timeMin }],
        //         userNo, subscID }   (serial order = array order)
        // ============================================================

        [HttpPost("SaveDtlData")]
        public async Task<IActionResult> SaveDtlData([FromBody] RoutingChartSaveDtlRequest req)
        {
            if (req == null)
                return BadRequestResult("Invalid request.");
            if (req.MTransNo <= 0)
                return BadRequestResult("Invalid Routing Chart.");
            if (req.UserNo <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                var subscID = req.SubscID > 0 ? req.SubscID : await RouteSubscID(req.MTransNo);
                if (subscID <= 0)
                    return NotFoundResult("Route not found.");

                var json = JsonSerializer.Serialize((req.Steps ?? new()).Select(s => new
                {
                    operationNo = s.OperationNo,
                    machineNo   = s.MachineNo,
                    timeMin     = s.TimeMin
                }));

                var result = await db.CallRoutingChartStepsSP(req.MTransNo, json, req.UserNo, subscID);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE — RecDelete?MTransNo=&UserNo=&mReason=[&SubscID=]
        // ============================================================

        [HttpPost("RecDelete")]
        [HttpDelete("RecDelete")]
        public async Task<IActionResult> RecDelete(
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID = 0,
            [FromQuery] string mReason = "")
        {
            if (mTransNo <= 0 || userNo <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                if (subscID <= 0) subscID = await RouteSubscID(mTransNo);
                if (subscID <= 0) return NotFoundResult("Route not found.");

                var result = await db.CallRoutingChartMstSP(new SP_RoutingChartMst
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(mReason) ? "No reason provided" : mReason.Trim(),
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET LIST
        // Row: { mTransNo, id, partNo, partCode, partName, grade, gradeNo,
        //        routeName, batchQty, dieType, dieTypeName, dieTypeNo,
        //        stepCount, totalTimeMin, sequence ("Cutting → Heating → …"),
        //        jobCardCount, lockStatus, createdOn }
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] int? partNo = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadRoutes(subscID, 0, partNo ?? 0, search);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET MASTER DATA — GetMasterData?MTransNo=[&SubscID=]
        // One route, same shape as a GetList row.
        // ============================================================

        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData(
            [FromQuery] int mTransNo,
            [FromQuery] int subscID = 0)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid MTransNo.");

            try
            {
                if (subscID <= 0) subscID = await RouteSubscID(mTransNo);
                var data = subscID > 0 ? await LoadRoutes(subscID, mTransNo, 0, null) : new();
                if (data.Count == 0) return NotFoundResult("Route not found.");
                return OkResult(data[0]);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET DETAILS LIST — GetDetailsList?MTransNo=[&SubscID=]
        // Steps by SlNo: { transSlNo, slNo, operationNo, operationName,
        //                  machineNo, machineCode, machineName, timeMin }
        // ============================================================

        [HttpGet("GetDetailsList")]
        public async Task<IActionResult> GetDetailsList(
            [FromQuery] int mTransNo,
            [FromQuery] int subscID = 0)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.PmQueryRowsAsync("""
                    SELECT d.TransSlNo                    AS transSlNo,
                           d.SlNo                         AS slNo,
                           d.Operation                    AS operationNo,
                           ISNULL(om.MasterName, '')      AS operationName,
                           d.MachineNo                    AS machineNo,
                           ISNULL(m.MachineCode, '')      AS machineCode,
                           ISNULL(m.MachineName, '')      AS machineName,
                           ISNULL(d.TimeMin, 0)           AS timeMin
                      FROM dbo.RoutingChartDtl d
                      LEFT JOIN dbo.OtherMaster om ON om.MTransNo = d.Operation
                      LEFT JOIN dbo.MachineMst  m  ON m.MTransNo  = d.MachineNo
                     WHERE d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'
                       AND (@SubscID = 0 OR d.SubscID = @SubscID)
                     ORDER BY d.SlNo, d.TransSlNo
                    """,
                    new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                    new SqlParameter("@SubscID",  SqlDbType.Int) { Value = subscID });

                return OkResult(data, data.Count > 0 ? "Data found." : "No steps found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<int> RouteSubscID(int mTransNo) =>
            await db.RoutingChartMst.AsNoTracking()
                .Where(x => x.MTransNo == mTransNo && x.DeleteStatus == "N")
                .Select(x => x.SubscID ?? 0)
                .FirstOrDefaultAsync();

        private async Task<List<Dictionary<string, object?>>> LoadRoutes(int subscID, int id, int partNo, string? search)
        {
            var jobCards = await db.PmTableExistsAsync("JobCardMst")
                ? "(SELECT COUNT(*) FROM dbo.JobCardMst j WHERE j.RouteNo = h.MTransNo AND j.DeleteStatus = 'N')"
                : "0";

            var sql = $"""
                SELECT h.MTransNo                          AS mTransNo,
                       h.MTransNo                          AS id,
                       h.PartNo                            AS partNo,
                       ISNULL(p.PartCode, '')              AS partCode,
                       ISNULL(p.PartName, '')              AS partName,
                       ISNULL(g.Grade, '')                 AS grade,
                       h.GradeNo                           AS gradeNo,
                       h.RouteName                         AS routeName,
                       h.BatchQty                          AS batchQty,
                       ISNULL(dt.DieType, '')              AS dieType,
                       ISNULL(dt.DieType, '')              AS dieTypeName,
                       ISNULL(t.MTransNo, 0)               AS dieTypeNo,
                       s.stepCount                         AS stepCount,
                       s.totalTimeMin                      AS totalTimeMin,
                       ISNULL(sq.sequence, '')             AS sequence,
                       {jobCards}                          AS jobCardCount,
                       h.LockStatus                        AS lockStatus,
                       CONVERT(char(10), h.CreatedOn, 23)  AS createdOn
                  FROM dbo.RoutingChartMst h
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = h.PartNo
                  OUTER APPLY (SELECT TOP (1) mg.Grade FROM dbo.MaterialGrade mg
                                WHERE mg.PartNo = h.PartNo AND mg.GradeNo = h.GradeNo AND mg.SubscID = h.SubscID
                                ORDER BY CASE WHEN mg.DeleteStatus = 'N' THEN 0 ELSE 1 END, mg.MTransNo DESC) g
                  -- The die type follows the part + grade die; the stored copy is the fallback.
                  OUTER APPLY (SELECT COALESCE(
                                   (SELECT TOP (1) NULLIF(dm.DieType, '') FROM dbo.DieMst dm
                                     WHERE dm.PartNo = h.PartNo AND dm.GradeNo = h.GradeNo
                                       AND dm.SubscID = h.SubscID AND dm.DeleteStatus = 'N'
                                     ORDER BY dm.MTransNo),
                                   NULLIF(h.DieType, '')) AS DieType) dt
                  OUTER APPLY (SELECT TOP (1) om.MTransNo FROM dbo.OtherMaster om
                                WHERE om.MasterType = 'Die Type' AND om.MasterName = dt.DieType
                                  AND om.SubscID = h.SubscID AND om.DeleteStatus = 'N'
                                ORDER BY CASE WHEN om.LockStatus = 'Y' THEN 0 ELSE 1 END, om.MTransNo) t
                  OUTER APPLY (SELECT COUNT(*) AS stepCount, ISNULL(SUM(rd.TimeMin), 0) AS totalTimeMin
                                 FROM dbo.RoutingChartDtl rd
                                WHERE rd.MTransNo = h.MTransNo AND rd.DeleteStatus = 'N') s
                  OUTER APPLY (SELECT STRING_AGG(CONVERT(nvarchar(200), ISNULL(om.MasterName, '')), @Arrow)
                                      WITHIN GROUP (ORDER BY rd.SlNo, rd.TransSlNo) AS sequence
                                 FROM dbo.RoutingChartDtl rd
                                 LEFT JOIN dbo.OtherMaster om ON om.MTransNo = rd.Operation
                                WHERE rd.MTransNo = h.MTransNo AND rd.DeleteStatus = 'N') sq
                 WHERE h.SubscID = @SubscID AND h.DeleteStatus = 'N'
                   AND (@Id = 0 OR h.MTransNo = @Id)
                   AND (@PartNo = 0 OR h.PartNo = @PartNo)
                   AND (@Search = '' OR h.RouteName LIKE '%' + @Search + '%'
                                     OR p.PartCode  LIKE '%' + @Search + '%'
                                     OR p.PartName  LIKE '%' + @Search + '%')
                 ORDER BY p.PartCode, h.RouteName
                """;

            return await db.PmQueryRowsAsync(sql,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@Id",      SqlDbType.Int) { Value = id },
                new SqlParameter("@PartNo",  SqlDbType.Int) { Value = partNo },
                new SqlParameter("@Search",  SqlDbType.VarChar, 100) { Value = (search ?? "").Trim() },
                new SqlParameter("@Arrow",   SqlDbType.NVarChar, 5)  { Value = " → " });
        }
    }
}
