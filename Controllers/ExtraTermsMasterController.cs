using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class ExtraTermsMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ExtraTermsMasterController(EMSContext context) => db = context;

        //=========================================
        // SAVE / UPDATE
        //=========================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_ExtraTermsMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.ModuleName))
                return BadRequestResult("Module Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequestResult("Description is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallExtraTermsMasterSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // DELETE
        //=========================================

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
                var result = await db.CallExtraTermsMasterSP(new SP_ExtraTermsMaster
                {
                    MTransNo     = id,
                    ModifiedBy   = (short)userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = (short)subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // LOCK / UNLOCK
        //=========================================

        [HttpPost("LockUnlock/{id}")]
        public async Task<IActionResult> LockUnlock(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string action = "Lock")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (action != "Lock" && action != "Unlock")
                return BadRequestResult("Action must be 'Lock' or 'Unlock'.");

            try
            {
                var result = await db.CallExtraTermsMasterSP(new SP_ExtraTermsMaster
                {
                    MTransNo   = id,
                    ModifiedBy = (short)userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = (short)subscID,
                    Status     = action
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // GET LIST
        //=========================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] string? moduleName = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.ExtraTermsMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == (short)subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(moduleName))
                    query = query.Where(x => x.ModuleName == moduleName.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.Title!.Contains(search)       ||
                        x.Description!.Contains(search) ||
                        x.ModuleName!.Contains(search));

                var data = await query
                    .OrderBy(x => x.ModuleName)
                    .ThenBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SlNo,
                        x.ModuleName,
                        x.Title,
                        x.Description,
                        x.ConfYN,
                        x.TempNo,
                        x.LockStatus,
                        x.LockCounter,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.Modifiedby,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // GET DATA
        //=========================================

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.ExtraTermsMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id            &&
                        x.SubscID      == (short)subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // GET DROPDOWN
        //=========================================

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(
            int subscID,
            [FromQuery] string? moduleName = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.ExtraTermsMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID      == (short)subscID &&
                                x.DeleteStatus == "N"            &&
                                x.LockStatus   == "Y");

                if (!string.IsNullOrWhiteSpace(moduleName))
                    query = query.Where(x => x.ModuleName == moduleName.Trim());

                var data = await query
                    .OrderBy(x => x.ModuleName)
                    .ThenBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SlNo,
                        x.ModuleName,
                        x.Title,
                        x.Description,
                        x.ConfYN,
                        x.TempNo
                    })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
