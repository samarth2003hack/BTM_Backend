using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class OtherMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public OtherMasterController(EMSContext context) => db = context;

        //=========================================
        // Master types available to this subscription
        //=========================================
        [HttpGet("GetMasterType/{subscID}")]
        public async Task<IActionResult> GetMasterType(int subscID, [FromQuery] string department = "")
        {
            try
            {
                var query = db.OtherMasterList
                    .AsNoTracking()
                    .Where(x => x.DeleteStatus == "N" && x.SubscID == subscID);

                if (!string.IsNullOrWhiteSpace(department))
                    query = query.Where(x => x.Department == department);

                var data = await query
                    .OrderBy(x => x.MasterType)
                    .Select(x => new { x.MTransNo, x.MasterType, x.Department })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_OtherMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.MasterType))
                return BadRequestResult("Master Type is required.");

            if (string.IsNullOrWhiteSpace(dto.MasterName))
                return BadRequestResult("Master Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallOtherMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Grid rows, optionally narrowed to one master type.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string masterType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.OtherMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(masterType))
                    query = query.Where(x => x.MasterType == masterType);

                var data = await query
                    .OrderBy(x => x.MasterType)
                    .ThenBy(x => x.MasterName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.MasterType,
                        x.MasterName,
                        x.Department,
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
                var data = await db.OtherMaster
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

        /// <summary>Active values of one master type — feeds every dropdown.</summary>
        [HttpGet("GetDropdown/{masterType}/{subscID}")]
        public async Task<IActionResult> GetDropdown(string masterType, int subscID)
        {
            if (string.IsNullOrWhiteSpace(masterType))
                return BadRequestResult("Master Type is required.");

            try
            {
                var data = await db.OtherMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y" &&
                                x.MasterType == masterType)
                    .OrderBy(x => x.MasterName)
                    .Select(x => new { x.MTransNo, x.MasterName })
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
                var result = await db.CallOtherMasterSP(new SP_OtherMaster
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
