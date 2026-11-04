using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Stocking grades of a part. Min/max stock and MOQ live here rather than on
    /// PartMaster because they differ per grade. Send GradeNo 0 and the procedure
    /// allocates the next number for the part.
    /// </summary>
    [Route("api/[controller]")]
    public class MaterialGradeController : EmsControllerBase
    {
        private readonly EMSContext db;

        public MaterialGradeController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_MaterialGrade dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.PartNo <= 0)
                return BadRequestResult("Part is required.");

            if (string.IsNullOrWhiteSpace(dto.Grade))
                return BadRequestResult("Grade is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallMaterialGradeSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Grades of one part.</summary>
        [HttpGet("GetList/{partNo}/{subscID}")]
        public async Task<IActionResult> GetList(int partNo, int subscID)
        {
            if (partNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var data = await db.MaterialGrade
                    .AsNoTracking()
                    .Where(x => x.PartNo == partNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.GradeNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.PartNo,
                        x.GradeNo,
                        x.Grade,
                        x.MinStock,
                        x.MaxStock,
                        x.MOQ,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.MaterialGrade
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id &&
                                              x.SubscID == subscID &&
                                              x.DeleteStatus == "N");

                if (data == null)
                    return NotFoundResult();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Grade picker for one part — active grades only.</summary>
        [HttpGet("GetDropdown/{partNo}/{subscID}")]
        public async Task<IActionResult> GetDropdown(int partNo, int subscID)
        {
            try
            {
                var data = await db.MaterialGrade
                    .AsNoTracking()
                    .Where(x => x.PartNo == partNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.GradeNo)
                    .Select(x => new { x.MTransNo, x.GradeNo, x.Grade, x.MinStock, x.MaxStock, x.MOQ })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Parts stocked in a given grade — the reverse lookup of GetList, useful
        /// when a material shortage is traced back to what it feeds.
        /// </summary>
        [HttpGet("GetByGrade/{grade}/{subscID}")]
        public async Task<IActionResult> GetByGrade(string grade, int subscID)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return BadRequestResult("Grade is required.");

            try
            {
                var name = grade.Trim();

                var data = await (from g in db.MaterialGrade.AsNoTracking()
                                  join p in db.PartMaster.AsNoTracking() on g.PartNo equals p.MTransNo
                                  where g.SubscID == subscID &&
                                        g.Grade == name &&
                                        g.DeleteStatus == "N" &&
                                        p.DeleteStatus == "N"
                                  orderby p.PartCode
                                  select new
                                  {
                                      g.MTransNo,
                                      g.PartNo,
                                      p.PartCode,
                                      p.PartName,
                                      g.GradeNo,
                                      g.Grade,
                                      g.MinStock,
                                      g.MaxStock,
                                      g.MOQ
                                  })
                                 .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
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
                var result = await db.CallMaterialGradeSP(new SP_MaterialGrade
                {
                    MTransNo = id,
                    CreatedBy = userNo,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "Delete"
                });

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
