using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Chart-of-accounts group tree. A primary group (GrpStatus 'P') sits at the
    /// root; sub-groups ('S') hang off a parent via GrpMastNo.
    /// </summary>
    [Route("api/[controller]")]
    public class GroupHeadsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public GroupHeadsController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_GroupHeads dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.GrpName))
                return BadRequestResult("Group Name is required.");

            if (string.IsNullOrWhiteSpace(dto.BSStatus))
                return BadRequestResult("Balance Sheet / P&L status is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallGroupHeadsSP(dto);

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
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GroupHeads
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.GrpName)
                    .Select(x => new
                    {
                        x.GrpNo,
                        x.GrpName,
                        x.GrpMastNo,
                        x.GrpStatus,
                        x.BSStatus,
                        x.FAStatus,
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

        [HttpGet("GetData/{grpNo}/{subscID}")]
        public async Task<IActionResult> GetData(int grpNo, int subscID)
        {
            if (grpNo <= 0)
                return BadRequestResult("Invalid GrpNo.");

            try
            {
                var data = await db.GroupHeads
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.GrpNo == grpNo &&
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

        /// <summary>Active groups — feeds the "group head" picker on Account Master.</summary>
        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            try
            {
                var data = await db.GroupHeads
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.GrpName)
                    .Select(x => new { x.GrpNo, x.GrpName, x.GrpStatus, x.BSStatus })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// The tree flattened to four levels via dbo.GroupView, ordered by the
        /// LNo path so a report prints parents immediately above their children.
        /// </summary>
        [HttpGet("GetTree/{subscID}")]
        public async Task<IActionResult> GetTree(int subscID)
        {
            try
            {
                var data = await db.GroupView
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID)
                    .OrderBy(x => x.LNo)
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{grpNo}")]
        public async Task<IActionResult> DeleteData(
            int grpNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (grpNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGroupHeadsSP(new SP_GroupHeads
                {
                    GrpNo = grpNo,
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
