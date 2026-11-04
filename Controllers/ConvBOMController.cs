using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Conversion BOM (Production/ConvBOM.html; GetRMList feeds the Job Card
    /// page). One ConvBOM row = a finished part (+ optional FG grade; blank =
    /// every grade) and one raw-material line: RM part + RM grade, qty per
    /// finished piece, RM unit. Rules live in dbo.SP_ConvBOM (SQL 54).
    /// Saves answer "OK^&lt;MTransNo&gt;"; a refusal is a 409 with the reason.
    /// </summary>
    [Route("api/[controller]")]
    public class ConvBOMController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ConvBOMController(EMSContext context) => db = context;

        private const string SelectBOM = """
            SELECT b.MTransNo                          AS mTransNo,
                   b.FPartNo                           AS fPartNo,
                   ISNULL(fp.PartCode, '')             AS fPartCode,
                   ISNULL(fp.PartName, '')             AS fPartName,
                   ISNULL(b.FGradeNo, 0)               AS fGradeNo,
                   ISNULL(b.FGrade, '')                AS fGrade,
                   b.PartNo                            AS partNo,
                   ISNULL(rp.PartCode, '')             AS partCode,
                   ISNULL(rp.PartName, '')             AS partName,
                   ISNULL(b.GradeNo, 0)                AS gradeNo,
                   ISNULL(b.Grade, '')                 AS grade,
                   b.Qty                               AS qty,
                   ISNULL(b.BarDia, 0)                 AS barDia,
                   ISNULL(b.CutLength, 0)              AS cutLength,
                   ISNULL(b.YieldPct, 0)               AS yieldPct,
                   ISNULL(NULLIF(b.Unit, ''), ISNULL(rp.PrimaryUnit, '')) AS unit,
                   ISNULL(b.Remarks, '')               AS remarks,
                   b.LockStatus                        AS lockStatus,
                   b.CreatedBy                         AS createdBy,
                   CONVERT(char(10), b.CreatedOn, 23)  AS createdOn
              FROM dbo.ConvBOM b
              LEFT JOIN dbo.PartMaster fp ON fp.MTransNo = b.FPartNo
              LEFT JOIN dbo.PartMaster rp ON rp.MTransNo = b.PartNo
            """;

        // ============================================================
        // GET LIST — every live BOM line
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] int fPartNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PmQueryRowsAsync(SelectBOM + """
                     WHERE b.SubscID = @SubscID AND b.DeleteStatus = 'N'
                       AND (@FPartNo = 0 OR b.FPartNo = @FPartNo)
                     ORDER BY fp.PartCode, b.FGrade, rp.PartCode
                    """,
                    new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                    new SqlParameter("@FPartNo", SqlDbType.Int) { Value = fPartNo });

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET DATA — one BOM line, same shape as a GetList row
        // ============================================================

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PmQueryRowsAsync(SelectBOM + """
                     WHERE b.MTransNo = @Id AND b.SubscID = @SubscID AND b.DeleteStatus = 'N'
                    """,
                    new SqlParameter("@Id",      SqlDbType.Int) { Value = id },
                    new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });

                if (data.Count == 0) return NotFoundResult();
                return OkResult(data[0]);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // CHECK DUPLICATE — "OK", or "Duplicate,1,<FG code> - <FG name>"
        // ?MTransNo=&FPartNo=&FGradeNo=&PartNo=&SubscID=
        // ============================================================

        [HttpGet("CheckDuplicate")]
        public async Task<IActionResult> CheckDuplicate(
            [FromQuery] int mTransNo,
            [FromQuery] int fPartNo,
            [FromQuery] int partNo,
            [FromQuery] int subscID,
            [FromQuery] int fGradeNo = 0)
        {
            if (fPartNo <= 0 || partNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var rows = await db.PmQueryRowsAsync("""
                    SELECT TOP (1) ISNULL(fp.PartCode, '') + ' - ' + ISNULL(fp.PartName, '') AS fg
                      FROM dbo.ConvBOM b
                      LEFT JOIN dbo.PartMaster fp ON fp.MTransNo = b.FPartNo
                     WHERE b.FPartNo = @FPartNo AND ISNULL(b.FGradeNo, 0) = @FGradeNo AND b.PartNo = @PartNo
                       AND b.SubscID = @SubscID AND b.DeleteStatus = 'N' AND b.MTransNo <> @MTransNo
                    """,
                    new SqlParameter("@FPartNo",  SqlDbType.Int) { Value = fPartNo },
                    new SqlParameter("@FGradeNo", SqlDbType.Int) { Value = Math.Max(0, fGradeNo) },
                    new SqlParameter("@PartNo",   SqlDbType.Int) { Value = partNo },
                    new SqlParameter("@SubscID",  SqlDbType.Int) { Value = subscID },
                    new SqlParameter("@MTransNo", SqlDbType.Int) { Value = Math.Max(0, mTransNo) });

                return OkResult(rows.Count > 0 ? $"Duplicate,1,{rows[0]["fg"]}" : "OK");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SAVE — insert when mTransNo is 0, else update
        // Body: { mTransNo, fPartNo, fGradeNo, partNo, gradeNo, qty,
        //         BarDia, CutLength, YieldPct, remarks, lockStatus, userNo, subscID }
        // qty = cut weight (kg per piece). Bar details: SQL 58, absent = keep.
        // ============================================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] ConvBOMSaveRequest dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");

            try
            {
                dto.Remarks = (dto.Remarks ?? "").Trim();
                dto.LockStatus = dto.LockStatus is "Y" or "S" ? dto.LockStatus : "N";

                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await db.CallConvBOMSP(dto, dto.MTransNo > 0 ? "Update" : "Insert", null, ClientIp);
                if (result.ID == 0) return ConflictResult(result.Message);

                var plan = await db.CallConvBOMPlanFieldsSP(result.ID, dto.BarDia, dto.CutLength, dto.YieldPct,
                    dto.UserNo, dto.SubscID);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                await tx.CommitAsync();
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE — soft delete with a reason
        // ============================================================

        [HttpDelete("DeleteData/{id}")]
        [HttpPost("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallConvBOMSP(
                    new ConvBOMSaveRequest { MTransNo = id, UserNo = userNo, SubscID = subscID },
                    "Delete",
                    string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(),
                    ClientIp);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET RM LIST — raw materials of a finished part, for the Job Card
        // GetRMList?PartNo=&SubscID=[&FGradeNo=|&FGrade=]
        // Row: { partNo (RM), partCode, partName, unit, grade, gradeNo,
        //        qtyPerPc, barDia, cutLength, yieldPct, fgGrade, fgGradeNo, lockStatus }
        // A grade filter keeps the lines of that FG grade plus the lines
        // that apply to every grade (blank FG grade).
        // ============================================================

        [HttpGet("GetRMList")]
        public async Task<IActionResult> GetRMList(
            [FromQuery] int partNo,
            [FromQuery] int subscID,
            [FromQuery] int fGradeNo = 0,
            [FromQuery] string? fGrade = null)
        {
            if (partNo <= 0) return BadRequestResult("Invalid Part.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PmQueryRowsAsync("""
                    SELECT b.PartNo                            AS partNo,
                           ISNULL(rp.PartCode, '')             AS partCode,
                           ISNULL(rp.PartName, '')             AS partName,
                           ISNULL(NULLIF(b.Unit, ''), ISNULL(rp.PrimaryUnit, '')) AS unit,
                           ISNULL(b.Grade, '')                 AS grade,
                           ISNULL(b.GradeNo, 0)                AS gradeNo,
                           b.Qty                               AS qtyPerPc,
                           ISNULL(b.BarDia, 0)                 AS barDia,
                           ISNULL(b.CutLength, 0)              AS cutLength,
                           ISNULL(b.YieldPct, 0)               AS yieldPct,
                           ISNULL(b.FGrade, '')                AS fgGrade,
                           ISNULL(b.FGradeNo, 0)               AS fgGradeNo,
                           b.LockStatus                        AS lockStatus
                      FROM dbo.ConvBOM b
                      LEFT JOIN dbo.PartMaster rp ON rp.MTransNo = b.PartNo
                     WHERE b.FPartNo = @PartNo AND b.SubscID = @SubscID AND b.DeleteStatus = 'N'
                       AND (@FGradeNo = 0 OR ISNULL(b.FGradeNo, 0) IN (0, @FGradeNo))
                       AND (@FGrade = '' OR ISNULL(b.FGrade, '') IN ('', @FGrade))
                     ORDER BY rp.PartCode, b.Grade
                    """,
                    new SqlParameter("@PartNo",   SqlDbType.Int) { Value = partNo },
                    new SqlParameter("@SubscID",  SqlDbType.Int) { Value = subscID },
                    new SqlParameter("@FGradeNo", SqlDbType.Int) { Value = Math.Max(0, fGradeNo) },
                    new SqlParameter("@FGrade",   SqlDbType.VarChar, 50) { Value = (fGrade ?? "").Trim() });

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
