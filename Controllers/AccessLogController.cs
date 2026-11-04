using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Login / logout audit trail. HomeController opens a row at sign-in; the
    /// client calls Logout when the session ends.
    /// </summary>
    [Route("api/[controller]")]
    public class AccessLogController : EmsControllerBase
    {
        private readonly EMSContext db;

        public AccessLogController(EMSContext context) => db = context;

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] SP_AccessLog dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.LoginId))
                return BadRequestResult("Login ID is required.");

            try
            {
                dto.IPAddress ??= ClientIp;
                dto.Status = "Insert";

                var result = await db.CallAccessLogSP(dto);

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
        /// Stamps logout on a session row. Pass mTransNo when known; otherwise
        /// the newest still-open session for the login is closed.
        /// </summary>
        [HttpPost("Logout")]
        public async Task<IActionResult> Logout([FromBody] SP_AccessLog dto)
        {
            if (dto == null || (dto.MTransNo <= 0 && string.IsNullOrWhiteSpace(dto.LoginId)))
                return BadRequestResult("Session number or Login ID is required.");

            try
            {
                dto.Status = "Logout";

                var result = await db.CallAccessLogSP(dto);

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
        /// Access log for a date range. Both dates are inclusive; omitting them
        /// defaults to the last 7 days so the endpoint never scans the whole table.
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

                var query = db.AccessLog
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.LoginDate >= from &&
                                x.LoginDate <= to);

                if (!string.IsNullOrWhiteSpace(loginId))
                    query = query.Where(x => x.LoginId == loginId);

                var data = await query
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.LoginId,
                        x.LoginType,
                        x.IPAddress,
                        x.LoginDate,
                        x.LoginTime,
                        x.LogoutDate,
                        x.LogoutTime,
                        x.LatLong,
                        x.Location
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Sessions that were never closed — useful for spotting stale logins.</summary>
        [HttpGet("GetOpenSessions/{subscID}")]
        public async Task<IActionResult> GetOpenSessions(int subscID)
        {
            try
            {
                var data = await db.AccessLog
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.LogoutDate == null)
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new { x.MTransNo, x.LoginId, x.IPAddress, x.LoginDate, x.LoginTime })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
