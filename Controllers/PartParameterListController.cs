using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Which parameters make up the code for a part type, and in what order.
    /// SlNo is the segment's position in the generated code; the values each
    /// segment may take live in PartCodeMaster.
    /// </summary>
    [Route("api/[controller]")]
    public class PartParameterListController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PartParameterListController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_PartParameterList dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.PartType))
                return BadRequestResult("Part Type is required.");

            if (string.IsNullOrWhiteSpace(dto.Parameter))
                return BadRequestResult("Parameter is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallPartParameterListSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>All rows, or the parameters of one part type, in code order.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string partType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PartParameterList
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(partType))
                    query = query.Where(x => x.PartType == partType);

                var data = await query
                    .OrderBy(x => x.PartType)
                    .ThenBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.PartType,
                        x.Parameter,
                        x.SlNo,
                        x.MaxLength,
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
                var data = await db.PartParameterList
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

        /// <summary>
        /// The code layout for one part type — the parameters in position order,
        /// each with the codes it accepts. One call is enough to render the
        /// part-code builder.
        /// </summary>
        [HttpGet("GetLayout/{partType}/{subscID}")]
        public async Task<IActionResult> GetLayout(string partType, int subscID)
        {
            if (string.IsNullOrWhiteSpace(partType))
                return BadRequestResult("Part Type is required.");

            try
            {
                var type = partType.Trim();

                var parameters = await db.PartParameterList
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.PartType == type &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new { x.MTransNo, x.Parameter, x.SlNo, x.MaxLength })
                    .ToListAsync();

                if (parameters.Count == 0)
                    return OkResult(Array.Empty<object>(), "No parameters defined for this part type.");

                var names = parameters.Select(p => p.Parameter).ToList();

                var codes = await db.PartCodeMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y" &&
                                names.Contains(x.Parameter))
                    .OrderBy(x => x.Code)
                    .Select(x => new { x.MTransNo, x.Parameter, x.Code, x.Description, x.ValveType })
                    .ToListAsync();

                var layout = parameters
                    .Select(p => new
                    {
                        p.Parameter,
                        p.SlNo,
                        p.MaxLength,
                        Codes = codes.Where(c => c.Parameter == p.Parameter).ToList()
                    })
                    .ToList();

                return OkResult(layout);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>The distinct part types that have a code layout defined.</summary>
        [HttpGet("GetPartTypes/{subscID}")]
        public async Task<IActionResult> GetPartTypes(int subscID)
        {
            try
            {
                var data = await db.PartParameterList
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => x.PartType)
                    .Distinct()
                    .OrderBy(x => x)
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
                var result = await db.CallPartParameterListSP(new SP_PartParameterList
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
