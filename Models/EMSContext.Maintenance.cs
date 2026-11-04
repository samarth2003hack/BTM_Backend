using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Maintenance module SP wrappers (SQL/55_Maintenance.sql). Same contract
    /// as every other Call*SP helper — @ID output (affected key, or 0 on a
    /// refusal) and @Message output (the refusal, shown to the user as is).
    /// </summary>
    public partial class EMSContext
    {
        private static SqlParameter MntOut(string name) =>
            new(name, SqlDbType.Int) { Direction = ParameterDirection.Output };

        private static SqlParameter MntMsg() =>
            new("@Message", SqlDbType.NVarChar, 400) { Direction = ParameterDirection.Output };

        private static SqlParameter MntStr(string name, string? value, int size) =>
            new(name, SqlDbType.VarChar, size) { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim() };

        private static SqlParameter MntJson(string name, string? value) =>
            new(name, SqlDbType.NVarChar, -1) { Value = (object?)value ?? DBNull.Value };

        private static SqlParameter MntInt(string name, int? value) =>
            new(name, SqlDbType.Int) { Value = (object?)value ?? DBNull.Value };

        private static (int ID, string Message) MntResult(SqlParameter id, SqlParameter msg) =>
            (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value), msg.Value?.ToString() ?? string.Empty);

        public async Task<(int ID, string Message)> CallPMChecklistSP(SP_PMChecklist dto)
        {
            var id = MntOut("@ID");
            var msg = MntMsg();
            var p = new[]
            {
                id,
                MntInt("@MTransNo", dto.MTransNo),
                MntStr("@ChecklistName", dto.ChecklistName, 100),
                MntStr("@DocNo", dto.DocNo, 30),
                MntStr("@RevNo", dto.RevNo, 10),
                new SqlParameter("@RevDate", SqlDbType.Date) { Value = (object?)dto.RevDate?.Date ?? DBNull.Value },
                MntStr("@Remarks", dto.Remarks, 200),
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = dto.LockStatus },
                MntJson("@MachinesJson", dto.MachinesJson),
                MntJson("@PointsJson", dto.PointsJson),
                MntStr("@SystemIP", dto.SystemIP, 20),
                MntInt("@UserNo", dto.UserNo),
                MntStr("@DeleteReason", dto.DeleteReason, 250),
                MntInt("@SubscID", dto.SubscID),
                MntStr("@Status", dto.Status, 20),
                msg
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_PMChecklist
                    @ID OUTPUT, @MTransNo, @ChecklistName, @DocNo, @RevNo, @RevDate, @Remarks, @LockStatus,
                    @MachinesJson, @PointsJson, @SystemIP, @UserNo, @DeleteReason, @SubscID, @Status,
                    @Message OUTPUT
                """, p);
            return MntResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallPMSheetSP(SP_PMSheet dto)
        {
            var id = MntOut("@ID");
            var msg = MntMsg();
            var p = new[]
            {
                id,
                MntInt("@MTransNo", dto.MTransNo),
                MntInt("@MachineNo", dto.MachineNo),
                MntInt("@Year", dto.Year),
                MntInt("@Month", dto.Month),
                MntStr("@Remarks", dto.Remarks, 500),
                MntJson("@PointsJson", dto.PointsJson),
                MntJson("@EntriesJson", dto.EntriesJson),
                MntJson("@DaysJson", dto.DaysJson),
                MntJson("@RemarksJson", dto.RemarksJson),
                MntStr("@SystemIP", dto.SystemIP, 20),
                MntInt("@UserNo", dto.UserNo),
                MntStr("@DeleteReason", dto.DeleteReason, 250),
                MntInt("@SubscID", dto.SubscID),
                MntStr("@Status", dto.Status, 20),
                msg
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_PMSheet
                    @ID OUTPUT, @MTransNo, @MachineNo, @Year, @Month, @Remarks, @PointsJson, @EntriesJson,
                    @DaysJson, @RemarksJson, @SystemIP, @UserNo, @DeleteReason, @SubscID, @Status,
                    @Message OUTPUT
                """, p);
            return MntResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallPMSheetApproveSP(
            int mTransNo, string level, int approvedBy, string? systemIP, int userNo, int subscID)
        {
            var id = MntOut("@ID");
            var msg = MntMsg();
            var p = new[]
            {
                id,
                MntInt("@MTransNo", mTransNo),
                MntStr("@Level", level, 10),
                MntInt("@ApprovedBy", approvedBy),
                MntStr("@SystemIP", systemIP, 20),
                MntInt("@UserNo", userNo),
                MntInt("@SubscID", subscID),
                msg
            };
            await Database.ExecuteSqlRawAsync(
                "EXEC dbo.SP_PMSheetApprove @ID OUTPUT, @MTransNo, @Level, @ApprovedBy, @SystemIP, @UserNo, @SubscID, @Message OUTPUT", p);
            return MntResult(id, msg);
        }

        public async Task<(int ID, string Message)> CallBreakdownSP(SP_Breakdown dto)
        {
            var id = MntOut("@ID");
            var msg = MntMsg();
            var p = new[]
            {
                id,
                MntInt("@MTransNo", dto.MTransNo),
                MntInt("@MachineNo", dto.MachineNo),
                new SqlParameter("@BreakdownOn", SqlDbType.DateTime2) { Value = dto.Status == "Delete" ? DBNull.Value : dto.BreakdownOn },
                MntStr("@BreakdownType", dto.BreakdownType, 20),
                MntStr("@ReportedBy", dto.ReportedBy, 100),
                MntStr("@Problem", dto.Problem, 500),
                MntInt("@AttendedBy", dto.AttendedBy),
                new SqlParameter("@RepairStart", SqlDbType.DateTime2) { Value = (object?)dto.RepairStart ?? DBNull.Value },
                new SqlParameter("@RepairEnd", SqlDbType.DateTime2) { Value = (object?)dto.RepairEnd ?? DBNull.Value },
                MntStr("@RootCause", dto.RootCause, 500),
                MntStr("@ActionTaken", dto.ActionTaken, 500),
                MntStr("@SparesUsed", dto.SparesUsed, 500),
                MntStr("@Remarks", dto.Remarks, 500),
                MntInt("@PMSheetNo", dto.PMSheetNo),
                MntStr("@BreakdownStatus", dto.BreakdownStatus, 20),
                MntStr("@SystemIP", dto.SystemIP, 20),
                MntInt("@UserNo", dto.UserNo),
                MntStr("@DeleteReason", dto.DeleteReason, 250),
                MntInt("@SubscID", dto.SubscID),
                MntStr("@Status", dto.Status, 20),
                msg
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_Breakdown
                    @ID OUTPUT, @MTransNo, @MachineNo, @BreakdownOn, @BreakdownType, @ReportedBy, @Problem,
                    @AttendedBy, @RepairStart, @RepairEnd, @RootCause, @ActionTaken, @SparesUsed, @Remarks,
                    @PMSheetNo, @BreakdownStatus, @SystemIP, @UserNo, @DeleteReason, @SubscID, @Status,
                    @Message OUTPUT
                """, p);
            return MntResult(id, msg);
        }
    }
}
