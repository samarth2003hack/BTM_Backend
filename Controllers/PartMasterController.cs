using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Items / parts. Deleting a part soft-deletes its material grades and
    /// price-list rates; its drawings survive, since a drawing is its own master
    /// record (SP_PartMaster).
    ///
    /// GetList is capped — a parts list is the one master that routinely runs to
    /// tens of thousands of rows, and the message says when the cap was hit.
    /// </summary>
    [Route("api/[controller]")]
    public class PartMasterController : EmsControllerBase
    {
        private const int DefaultTop = 1000;
        private const int MaxTop = 10000;

        private readonly EMSContext db;

        public PartMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_PartMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.PartCode))
                return BadRequestResult("Part Code is required.");

            if (string.IsNullOrWhiteSpace(dto.PartName))
                return BadRequestResult("Part Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallPartMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Parts, optionally narrowed by free text, category or part type.
        /// </summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] string search = "",
            [FromQuery] string category = "",
            [FromQuery] string partType = "",
            [FromQuery] int top = DefaultTop)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                if (top is < 1 or > MaxTop) top = DefaultTop;

                var query = db.PartMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.PartCode!.Contains(search) ||
                                             x.PartName!.Contains(search) ||
                                             x.DrawingNo!.Contains(search));

                if (!string.IsNullOrWhiteSpace(category))
                    query = query.Where(x => x.Category == category);

                if (!string.IsNullOrWhiteSpace(partType))
                    query = query.Where(x => x.PartType == partType);

                var data = await query
                    .OrderBy(x => x.PartCode)
                    .Take(top)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.PartCode,
                        x.PartName,
                        x.ShortDescription,
                        x.DrawingNo,
                        x.DrgRevNo,
                        x.Category,
                        x.SubCategory,
                        x.InventoryGroup,
                        x.Brand,
                        x.PartType,
                        x.PartClass,
                        x.HSNCode,
                        x.TaxP,
                        x.PrimaryUnit,
                        x.SPart,
                        x.PPart,
                        x.MPart,
                        x.MinStock,
                        x.MaxStock,
                        x.MOQ,
                        x.CostPrice,
                        x.SalesPrice,
                        x.MRP,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                var message = data.Count == 0 ? "No records found."
                            : data.Count == top ? $"Showing the first {top} records — narrow the search to see more."
                            : "Data found.";

                return OkResult(data, message);
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
                var data = await db.PartMaster
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

        /// <summary>The part with its material grades and drawings in one call.</summary>
        [HttpGet("GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> GetFullData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var part = await db.PartMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id &&
                                              x.SubscID == subscID &&
                                              x.DeleteStatus == "N");

                if (part == null)
                    return NotFoundResult();

                var grades = await db.MaterialGrade
                    .AsNoTracking()
                    .Where(x => x.PartNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.GradeNo)
                    .ToListAsync();

                var drawings = await db.DrawingNos
                    .AsNoTracking()
                    .Where(x => x.PartNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.DrawingNo)
                    .Select(x => new { x.MTransNo, x.DrawingNo, x.DrawingType, x.RevNo, x.RevDate, x.FileAttach })
                    .ToListAsync();

                return OkResult(new { Part = part, Grades = grades, Drawings = drawings });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Part picker. sPart / pPart / mPart narrow it to the parts a sales,
        /// purchase or production screen may use.
        /// </summary>
        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(
            int subscID,
            [FromQuery] string PartType = "",
            [FromQuery] string flag = "",
            [FromQuery] string search = "",
            [FromQuery] int top = DefaultTop)
        {
            try
            {
                if (top is < 1 or > MaxTop) top = DefaultTop;

                var query = db.PartMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y");

                query = (flag ?? "").Trim().ToUpperInvariant() switch
                {
                    "S" => query.Where(x => x.SPart == "Y"),
                    "P" => query.Where(x => x.PPart == "Y"),
                    "M" => query.Where(x => x.MPart == "Y"),
                    _ => query
                };

                if (!string.IsNullOrWhiteSpace(PartType))
                    query = query.Where(x => x.PartType == PartType);

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.PartCode!.Contains(search) || x.PartName!.Contains(search));

                var data = await query
                    .OrderBy(x => x.PartCode)
                    .Take(top)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.PartCode,
                        x.PartName,
                        x.PrimaryUnit,
                        x.SecondaryUnit,
                        x.ConversionRatio,
                        x.TxnUnit,
                        x.HSNCode,
                        x.TaxP,
                        x.SalesPrice,
                        x.CostPrice,
                        x.MRP,
                        x.PartType
                    })
                    .ToListAsync();

                return OkResult(data);
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
                var result = await db.CallPartMasterSP(new SP_PartMaster
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
