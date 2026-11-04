using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Models
{
    /// <summary>
    /// Production Planning slice (SQL 59): the stored-procedure wrappers of
    /// the work calendar and the production plan. Reads are raw SQL in the
    /// controllers (PlanningControllerBase).
    /// </summary>
    public partial class EMSContext
    {
        private static object PlDbValue(object? value) => value ?? DBNull.Value;

        private static (SqlParameter Id, SqlParameter Msg) PlOutputs() => (
            new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output },
            new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output });

        private static (int ID, string Message) PlResult(SqlParameter id, SqlParameter msg) =>
            (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value), msg.Value?.ToString() ?? string.Empty);

        public async Task<(int ID, string Message)> CallWorkCalendarSP(WorkCalendarSaveRequest dto, string holidaysJson,
            int userNo, string status, string? deleteReason, string? systemIP)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = dto.MTransNo },
                new SqlParameter("@CalMonth", SqlDbType.Char, 7) { Value = PlDbValue(dto.CalMonth) },
                new SqlParameter("@ShiftName", SqlDbType.VarChar, 50) { Value = PlDbValue(dto.ShiftName) },
                new SqlParameter("@ShiftStart", SqlDbType.Char, 5) { Value = PlDbValue(dto.ShiftStart) },
                new SqlParameter("@ShiftEnd", SqlDbType.Char, 5) { Value = PlDbValue(dto.ShiftEnd) },
                new SqlParameter("@BreakMin", SqlDbType.Int) { Value = dto.BreakMin },
                new SqlParameter("@WeeklyOff", SqlDbType.VarChar, 10) { Value = PlDbValue(dto.WeeklyOff) },
                new SqlParameter("@Holidays", SqlDbType.NVarChar, -1) { Value = holidaysJson },
                new SqlParameter("@Remarks", SqlDbType.VarChar, 250) { Value = PlDbValue(dto.Remarks) },
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = dto.LockStatus == "N" ? "N" : "Y" },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = PlDbValue(systemIP) },
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = PlDbValue(deleteReason) },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = dto.SubscID },
                new SqlParameter("@Status", SqlDbType.VarChar, 20) { Value = status },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_WorkCalendar @ID = @ID OUTPUT, @MTransNo = @MTransNo, @CalMonth = @CalMonth,
                     @ShiftName = @ShiftName, @ShiftStart = @ShiftStart, @ShiftEnd = @ShiftEnd, @BreakMin = @BreakMin,
                     @WeeklyOff = @WeeklyOff, @Holidays = @Holidays, @Remarks = @Remarks, @LockStatus = @LockStatus,
                     @UserNo = @UserNo, @SystemIP = @SystemIP, @DeleteReason = @DeleteReason, @SubscID = @SubscID,
                     @Status = @Status, @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanMstSP(int mTransNo, DateTime? planDate,
            string? planMonth, int revNo, string? planStatus, string? remarks, int userNo, string? systemIP,
            string? deleteReason, int subscID, string status)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@PlanDate", SqlDbType.Date) { Value = PlDbValue(planDate) },
                new SqlParameter("@PlanMonth", SqlDbType.Char, 7) { Value = PlDbValue(planMonth) },
                new SqlParameter("@RevNo", SqlDbType.Int) { Value = revNo },
                new SqlParameter("@PlanStatus", SqlDbType.VarChar, 12) { Value = PlDbValue(planStatus) },
                new SqlParameter("@Remarks", SqlDbType.VarChar, 250) { Value = PlDbValue(remarks) },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = PlDbValue(systemIP) },
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = PlDbValue(deleteReason) },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@Status", SqlDbType.VarChar, 20) { Value = status },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanMst @ID = @ID OUTPUT, @MTransNo = @MTransNo, @PlanDate = @PlanDate,
                     @PlanMonth = @PlanMonth, @RevNo = @RevNo, @PlanStatus = @PlanStatus, @Remarks = @Remarks,
                     @UserNo = @UserNo, @SystemIP = @SystemIP, @DeleteReason = @DeleteReason, @SubscID = @SubscID,
                     @Status = @Status, @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanDtlSP(int mTransNo, string jobsJson, int userNo, int subscID)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@Jobs", SqlDbType.NVarChar, -1) { Value = jobsJson },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanDtl @ID = @ID OUTPUT, @MTransNo = @MTransNo, @Jobs = @Jobs,
                     @UserNo = @UserNo, @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanDaysSP(int mTransNo, string daysJson, int subscID)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@Days", SqlDbType.NVarChar, -1) { Value = daysJson },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanDays @ID = @ID OUTPUT, @MTransNo = @MTransNo, @Days = @Days,
                     @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanActionSP(int mTransNo, string action, int weekNo,
            int userNo, int subscID)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@Action", SqlDbType.VarChar, 20) { Value = action },
                new SqlParameter("@WeekNo", SqlDbType.Int) { Value = weekNo },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanAction @ID = @ID OUTPUT, @MTransNo = @MTransNo, @Action = @Action,
                     @WeekNo = @WeekNo, @UserNo = @UserNo, @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanReleaseSP(int mTransNo, int weekNo, string cardsJson,
            int userNo, string? systemIP, int subscID)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@WeekNo", SqlDbType.Int) { Value = weekNo },
                new SqlParameter("@Cards", SqlDbType.NVarChar, -1) { Value = cardsJson },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = PlDbValue(systemIP) },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanRelease @ID = @ID OUTPUT, @MTransNo = @MTransNo, @WeekNo = @WeekNo,
                     @Cards = @Cards, @UserNo = @UserNo, @SystemIP = @SystemIP, @SubscID = @SubscID,
                     @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallProductionPlanRescheduleSP(int mTransNo, int jobSeq,
            DateTime? fixedStart, decimal daysRequired, string reason, string fromText, string toText, int userNo, int subscID)
        {
            var (id, msg) = PlOutputs();
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@JobSeq", SqlDbType.Int) { Value = jobSeq },
                new SqlParameter("@FixedStart", SqlDbType.Date) { Value = PlDbValue(fixedStart) },
                new SqlParameter("@DaysRequired", SqlDbType.Decimal) { Precision = 9, Scale = 2, Value = daysRequired },
                new SqlParameter("@Reason", SqlDbType.VarChar, 250) { Value = reason },
                new SqlParameter("@FromText", SqlDbType.VarChar, 100) { Value = fromText },
                new SqlParameter("@ToText", SqlDbType.VarChar, 100) { Value = toText },
                new SqlParameter("@UserNo", SqlDbType.Int) { Value = userNo },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ProductionPlanReschedule @ID = @ID OUTPUT, @MTransNo = @MTransNo, @JobSeq = @JobSeq,
                     @FixedStart = @FixedStart, @DaysRequired = @DaysRequired, @Reason = @Reason,
                     @FromText = @FromText, @ToText = @ToText, @UserNo = @UserNo, @SubscID = @SubscID,
                     @Message = @Message OUTPUT
                """, p);
            return PlResult(id, msg);
        }
    }
}
