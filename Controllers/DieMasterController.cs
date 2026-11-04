using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Die Master (Masters/DieMaster.html). A die is a Part Master part in one
    /// of its MaterialGrade grades (one die per part + grade); its code and
    /// name are the part's. DieMst.DieType stores the Other Master 'Die Type'
    /// NAME — the API resolves it to/from dieTypeNo. Capacity:
    ///     usedQty      = live JobCardEntry.Qty against the die
    ///     processedQty = openingQty + usedQty
    ///     remainingQty = MAX(0, maxQty - processedQty)
    /// Rules live in dbo.SP_DieMst (SQL 54). Saves answer "OK^&lt;MTransNo&gt;";
    /// a refused save / delete is a 409 with the reason as the message.
    /// </summary>
    [Route("api/[controller]")]
    public class DieMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public DieMasterController(EMSContext context) => db = context;

        // ============================================================
        // SAVE / UPDATE
        // Body: { mTransNo, partNo, grade (text) | gradeNo, dieTypeNo,
        //         maxQty, openingQty, remarks, lockStatus, createdBy,
        //         subscID, status }
        // ============================================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] DieSaveRequest dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var userNo = dto.CreatedBy > 0 ? dto.CreatedBy : dto.UserNo;
            if (userNo <= 0)
                return BadRequestResult("Invalid User.");

            var isUpdate = string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (isUpdate && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (dto.PartNo <= 0)
                return ConflictResult("Select a die from the Part Master list.");

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
                        return ConflictResult($"{gradeText} is not a grade of the selected part.");
                }
                if (gradeNo <= 0)
                    return ConflictResult("Grade is required.");

                // Die Type id → its Other Master name (inactive types stay valid for a saved die).
                var dieType = (dto.DieType ?? "").Trim();
                if (dto.DieTypeNo > 0)
                {
                    dieType = await db.OtherMaster.AsNoTracking()
                        .Where(o => o.MTransNo == dto.DieTypeNo && o.MasterType == "Die Type" &&
                                    o.SubscID == dto.SubscID && o.DeleteStatus == "N")
                        .Select(o => o.MasterName)
                        .FirstOrDefaultAsync() ?? "";
                    if (dieType == "")
                        return ConflictResult("The selected Die Type no longer exists.");
                }

                var now = DateTime.Now;
                var result = await db.CallDieMstSP(new SP_DieMst
                {
                    MTransNo   = isUpdate ? dto.MTransNo : 0,
                    PartNo     = dto.PartNo,
                    GradeNo    = gradeNo,
                    DieType    = dieType,
                    MaxQty     = dto.MaxQty,
                    OpeningQty = dto.OpeningQty,
                    Remarks    = (dto.Remarks ?? "").Trim(),
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
        // DELETE — refused while job-card entries use the die.
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
                var result = await db.CallDieMstSP(new SP_DieMst
                {
                    MTransNo     = id,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(),
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET LIST
        // Row: { mTransNo, id, partNo, partCode, partName, dieCode, dieName,
        //        grade, gradeNo, dieTypeNo, dieTypeName, dieType, maxQty,
        //        openingQty, usedQty, processedQty, remainingQty,
        //        entryUseCount, lockStatus, remarks, createdOn }
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] int? partNo = null,
            [FromQuery] string? dieType = null,
            [FromQuery] string? lockStatus = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadDies(subscID, 0, partNo ?? 0, dieType, lockStatus, search);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET DATA — one die, same shape as a GetList row.
        // ============================================================

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadDies(subscID, id, 0, null, null, null);
                if (data.Count == 0) return NotFoundResult("Die not found.");
                return OkResult(data[0]);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<List<Dictionary<string, object?>>> LoadDies(
            int subscID, int id, int partNo, string? dieType, string? lockStatus, string? search)
        {
            // Live production per die; before SQL 53 the stored UsedQty stands in.
            var usage = await db.PmTableExistsAsync("JobCardEntry")
                ? "SELECT COUNT(*) AS n, ISNULL(SUM(e.Qty), 0) AS q FROM dbo.JobCardEntry e WHERE e.DieNo = d.MTransNo AND e.DeleteStatus = 'N'"
                : "SELECT 0 AS n, d.UsedQty AS q";

            var sql = $"""
                SELECT d.MTransNo                          AS mTransNo,
                       d.MTransNo                          AS id,
                       d.PartNo                            AS partNo,
                       ISNULL(p.PartCode, '')              AS partCode,
                       ISNULL(p.PartName, '')              AS partName,
                       ISNULL(p.PartCode, '')              AS dieCode,
                       ISNULL(p.PartName, '')              AS dieName,
                       ISNULL(g.Grade, '')                 AS grade,
                       d.GradeNo                           AS gradeNo,
                       ISNULL(t.MTransNo, 0)               AS dieTypeNo,
                       d.DieType                           AS dieTypeName,
                       d.DieType                           AS dieType,
                       d.MaxQty                            AS maxQty,
                       d.OpeningQty                        AS openingQty,
                       CONVERT(numeric(12,2), u.q)         AS usedQty,
                       CONVERT(numeric(12,2), d.OpeningQty + u.q) AS processedQty,
                       CONVERT(numeric(12,2), CASE WHEN d.MaxQty - (d.OpeningQty + u.q) > 0
                                                   THEN d.MaxQty - (d.OpeningQty + u.q) ELSE 0 END) AS remainingQty,
                       u.n                                 AS entryUseCount,
                       d.LockStatus                        AS lockStatus,
                       ISNULL(d.Remarks, '')               AS remarks,
                       CONVERT(char(10), d.CreatedOn, 23)  AS createdOn
                  FROM dbo.DieMst d
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                  OUTER APPLY (SELECT TOP (1) mg.Grade FROM dbo.MaterialGrade mg
                                WHERE mg.PartNo = d.PartNo AND mg.GradeNo = d.GradeNo AND mg.SubscID = d.SubscID
                                ORDER BY CASE WHEN mg.DeleteStatus = 'N' THEN 0 ELSE 1 END, mg.MTransNo DESC) g
                  OUTER APPLY (SELECT TOP (1) om.MTransNo FROM dbo.OtherMaster om
                                WHERE om.MasterType = 'Die Type' AND om.MasterName = d.DieType
                                  AND om.SubscID = d.SubscID AND om.DeleteStatus = 'N'
                                ORDER BY CASE WHEN om.LockStatus = 'Y' THEN 0 ELSE 1 END, om.MTransNo) t
                  OUTER APPLY ({usage}) u
                 WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N'
                   AND (@Id = 0 OR d.MTransNo = @Id)
                   AND (@PartNo = 0 OR d.PartNo = @PartNo)
                   AND (@DieType = '' OR d.DieType = @DieType)
                   AND (@Lock = '' OR d.LockStatus = @Lock)
                   AND (@Search = '' OR p.PartCode LIKE '%' + @Search + '%'
                                     OR p.PartName LIKE '%' + @Search + '%'
                                     OR d.DieType  LIKE '%' + @Search + '%'
                                     OR d.Remarks  LIKE '%' + @Search + '%')
                 ORDER BY p.PartCode, g.Grade
                """;

            return await db.PmQueryRowsAsync(sql,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@Id",      SqlDbType.Int) { Value = id },
                new SqlParameter("@PartNo",  SqlDbType.Int) { Value = partNo },
                new SqlParameter("@DieType", SqlDbType.VarChar, 50)  { Value = (dieType ?? "").Trim() },
                new SqlParameter("@Lock",    SqlDbType.VarChar, 1)   { Value = (lockStatus ?? "").Trim() },
                new SqlParameter("@Search",  SqlDbType.VarChar, 100) { Value = (search ?? "").Trim() });
        }
    }
}
