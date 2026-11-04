using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Append-only error sink. The front-end posts client-side failures here so
    /// they land next to server-side ones.
    /// </summary>
    [Route("api/[controller]")]
    public class ErrorLogController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ErrorLogController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_ErrorLog dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ErrorMsg))
                return BadRequestResult("Error message is required.");

            try
            {
                var result = await db.CallErrorLogSP(dto);

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
        /// Errors for a date range. Defaults to the last 7 days so the endpoint
        /// never scans the whole table.
        /// </summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string loginId = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var to = (toDate ?? DateTime.Today).Date;
                var from = (fromDate ?? to.AddDays(-7)).Date;

                var query = db.ErrorLog
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.ErrorDate >= from &&
                                x.ErrorDate <= to);

                if (!string.IsNullOrWhiteSpace(loginId))
                    query = query.Where(x => x.LoginId == loginId);

                var data = await query
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.ErrorDate,
                        x.ErrorTime,
                        x.LoginId,
                        x.LoginType,
                        x.ErrorMsg
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Full row including the stack trace.</summary>
        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.ErrorLog
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id && x.SubscID == subscID);

                if (data == null)
                    return NotFoundResult();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
