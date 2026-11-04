using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class ENQFeasibilityMstController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ENQFeasibilityMstController(EMSContext context) => db = context;

        //=========================================
        // SAVE / UPDATE
        //=========================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_ENQFeasibilityMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.FeasibilityPoint))
                return BadRequestResult("Feasibility Point is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallENQFeasibilityMstSP(dto);
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
                var result = await db.CallENQFeasibilityMstSP(new SP_ENQFeasibilityMst
                {
                    MTransNo     = id,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
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
                var result = await db.CallENQFeasibilityMstSP(new SP_ENQFeasibilityMst
                {
                    MTransNo   = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
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
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.ENQFeasibilityMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.FeasibilityPoint!.Contains(search) ||
                        x.Remarks!.Contains(search));

                var data = await query
                    .OrderBy(x => x.SlNo)
                    .ThenBy(x => x.FeasibilityPoint)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SlNo,
                        x.FeasibilityPoint,
                        x.Remarks,
                        x.AmdNo,
                        x.AmdNotes,
                        x.DocStatus,
                        x.DocStatusNote,
                        x.LockStatus,
                        x.LockCounter,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
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
                var data = await db.ENQFeasibilityMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id      &&
                        x.SubscID      == subscID  &&
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
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.ENQFeasibilityMst
                    .AsNoTracking()
                    .Where(x => x.SubscID      == subscID &&
                                x.DeleteStatus == "N"     &&
                                x.LockStatus   == "Y")
                    .OrderBy(x => x.SlNo)
                    .ThenBy(x => x.FeasibilityPoint)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SlNo,
                        x.FeasibilityPoint,
                        x.Remarks,
                        x.DocStatus
                    })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
