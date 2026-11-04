using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Masters/WorkCalendar.html — when the shop works: one row per month +
    /// shift team with shift start/end, break, weekly off and holidays (SQL 59).
    /// The calendar holds no machines; the Production Plan takes the hammers
    /// and their shifts / efficiency from Machine Master.
    ///
    ///   POST   SaveData                          status Insert | Update → "OK^id"
    ///   GET    GetList/{subscID}                 every calendar, holidays included
    ///   GET    GetData/{id}/{subscID}            one calendar (as a one-row list)
    ///   GET    GetByMonth?Month=&SubscID=        the ACTIVE calendar(s) of a month
    ///   DELETE DeleteData/{id}?userNo=&subscID=&reason=
    /// </summary>
    [Route("api/[controller]")]
    public class WorkCalendarController : PlanningControllerBase
    {
        public WorkCalendarController(EMSContext context) : base(context) { }

        // ============================================================
        // SAVE / UPDATE
        // ============================================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] WorkCalendarSaveRequest dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var userNo = dto.CreatedBy > 0 ? dto.CreatedBy : dto.UserNo;
            if (userNo <= 0) return BadRequestResult("Invalid User.");
            var isUpdate = string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (isUpdate && dto.MTransNo <= 0) return BadRequestResult("Invalid MTransNo.");
            try
            {
                var holidays = JsonSerializer.Serialize((dto.Holidays ?? new()).Select(h => new
                {
                    holiDate = (h.HoliDate ?? "").Split('T')[0],
                    reason = (h.Reason ?? "").Trim(),
                }));
                var result = await db.CallWorkCalendarSP(dto, holidays, userNo, isUpdate ? "Update" : "Insert", null, ClientIp);
                // The page reads "duplicate fields" from a successful reply.
                if (result.ID == 0 && result.Message == "duplicate fields") return OkResult("duplicate fields", "Duplicate month and shift team.");
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE
        // ============================================================

        [HttpDelete("DeleteData/{id}")]
        [HttpPost("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string reason = "")
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var result = await db.CallWorkCalendarSP(new WorkCalendarSaveRequest { MTransNo = id, SubscID = subscID }, "[]",
                    userNo, "Delete", string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(), ClientIp);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET LIST / GET DATA / GET BY MONTH
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadCalendars(subscID, 0, "", false);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0 || subscID <= 0) return BadRequestResult("Invalid request.");
            try
            {
                var data = await LoadCalendars(subscID, id, "", false);
                if (data.Count == 0) return NotFoundResult("Work calendar not found.");
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetByMonth")]
        public async Task<IActionResult> GetByMonth([FromQuery] string Month, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LoadCalendars(SubscID, 0, (Month ?? "").Trim(), true);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<List<Dictionary<string, object?>>> LoadCalendars(int subscID, int id, string month, bool activeOnly)
        {
            var rows = await Query(@"
                SELECT c.MTransNo AS mTransNo, c.CalMonth AS calMonth, c.ShiftName AS shiftName, c.ShiftStart AS shiftStart,
                       c.ShiftEnd AS shiftEnd, c.BreakMin AS breakMin, c.WeeklyOff AS weeklyOff, ISNULL(c.Remarks, '') AS remarks,
                       c.LockStatus AS lockStatus, CONVERT(char(10), c.CreatedOn, 23) AS createdOn
                  FROM dbo.WorkCalendarMst c
                 WHERE c.SubscID = @SubscID AND c.DeleteStatus = 'N'
                   AND (@Id = 0 OR c.MTransNo = @Id)
                   AND (@Month = '' OR c.CalMonth = @Month)
                   AND (@Active = 0 OR c.LockStatus = 'Y')
                 ORDER BY c.CalMonth DESC, c.ShiftName",
                Int("@SubscID", subscID), Int("@Id", id), Str("@Month", month, 7), Int("@Active", activeOnly ? 1 : 0));
            if (rows.Count == 0) return rows;
            var ids = string.Join(",", rows.Select(r => I(r, "mTransNo")));
            var hol = await Query($@"
                SELECT MTransNo AS id, CONVERT(char(10), HoliDate, 23) AS holiDate, ISNULL(Reason, '') AS reason
                  FROM dbo.WorkCalendarHoliday WHERE MTransNo IN ({ids}) AND DeleteStatus = 'N' ORDER BY HoliDate");
            foreach (var r in rows)
            {
                var id2 = I(r, "mTransNo");
                r["holidays"] = hol.Where(h => I(h, "id") == id2)
                                   .Select(h => new { holiDate = S(h, "holiDate"), reason = S(h, "reason") }).ToList();
            }
            return rows;
        }
    }
}
