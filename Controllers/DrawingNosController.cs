using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Drawings. PartNo is optional — a drawing is its own master record, so it
    /// outlives the part it happens to be tagged to. DrawingNo is unique per
    /// subscription.
    /// </summary>
    [Route("api/[controller]")]
    public class DrawingNosController : EmsControllerBase
    {
        private readonly EMSContext db;

        public DrawingNosController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_DrawingNos dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.DrawingNo))
                return BadRequestResult("Drawing No is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallDrawingNosSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] string search = "",
            [FromQuery] string drawingGroup = "",
            [FromQuery] string drawingType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.DrawingNos
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.DrawingNo!.Contains(search) ||
                                             x.DrawingDescription!.Contains(search));

                if (!string.IsNullOrWhiteSpace(drawingGroup))
                    query = query.Where(x => x.DrawingGroup == drawingGroup);

                if (!string.IsNullOrWhiteSpace(drawingType))
                    query = query.Where(x => x.DrawingType == drawingType);

                var data = await query
                    .OrderBy(x => x.DrawingGroup)
                    .ThenBy(x => x.PSlNo)
                    .ThenBy(x => x.DrawingNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DrawingNo,
                        x.PartNo,
                        x.DrawingType,
                        x.DrawingGroup,
                        x.PartGroup,
                        x.PSlNo,
                        x.DrawingDescription,
                        x.DrawingRemarks,
                        x.RevNo,
                        x.RevDate,
                        x.RevStatus,
                        x.FileAttach,
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
                var data = await db.DrawingNos
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

        /// <summary>Drawings tagged to one part.</summary>
        [HttpGet("GetByPart/{partNo}/{subscID}")]
        public async Task<IActionResult> GetByPart(int partNo, int subscID)
        {
            if (partNo <= 0)
                return BadRequestResult("Invalid PartNo.");

            try
            {
                var data = await db.DrawingNos
                    .AsNoTracking()
                    .Where(x => x.PartNo == partNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.DrawingNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DrawingNo,
                        x.DrawingType,
                        x.DrawingDescription,
                        x.RevNo,
                        x.RevDate,
                        x.RevStatus,
                        x.FileAttach
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            try
            {
                var data = await db.DrawingNos
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.DrawingNo)
                    .Select(x => new { x.MTransNo, x.DrawingNo, x.DrawingDescription, x.RevNo, x.PartNo })
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
                var result = await db.CallDrawingNosSP(new SP_DrawingNos
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
