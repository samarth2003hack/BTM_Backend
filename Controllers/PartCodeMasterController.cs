using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// The building blocks of a generated part code: for one parameter (say
    /// "BODY MATERIAL"), every code it may take and what that code means.
    /// The order the parameters go in is PartParameterList.
    /// </summary>
    [Route("api/[controller]")]
    public class PartCodeMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PartCodeMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_PartCodeMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.Parameter))
                return BadRequestResult("Parameter is required.");

            if (string.IsNullOrWhiteSpace(dto.Code))
                return BadRequestResult("Code is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallPartCodeMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>All codes, or those of one parameter / valve type.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] string parameter = "",
            [FromQuery] string valveType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PartCodeMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(parameter))
                    query = query.Where(x => x.Parameter == parameter);

                if (!string.IsNullOrWhiteSpace(valveType))
                    query = query.Where(x => x.ValveType == valveType);

                var data = await query
                    .OrderBy(x => x.Parameter)
                    .ThenBy(x => x.Code)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.Parameter,
                        x.Code,
                        x.Description,
                        x.ValveType,
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
                var data = await db.PartCodeMaster
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

        /// <summary>Active codes of one parameter, for the code-builder picker.</summary>
        [HttpGet("GetDropdown/{parameter}/{subscID}")]
        public async Task<IActionResult> GetDropdown(string parameter, int subscID)
        {
            if (string.IsNullOrWhiteSpace(parameter))
                return BadRequestResult("Parameter is required.");

            try
            {
                var name = parameter.Trim();

                var data = await db.PartCodeMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.Parameter == name &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.Code)
                    .Select(x => new { x.MTransNo, x.Code, x.Description, x.ValveType })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>The distinct parameter names in use — drives the first picker.</summary>
        [HttpGet("GetParameters/{subscID}")]
        public async Task<IActionResult> GetParameters(int subscID)
        {
            try
            {
                var data = await db.PartCodeMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => x.Parameter)
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
                var result = await db.CallPartCodeMasterSP(new SP_PartCodeMaster
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
