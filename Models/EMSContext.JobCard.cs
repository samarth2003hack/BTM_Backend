using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Job Card slice of the context: the SP wrappers behind JobCardController,
    /// JobCardStatusController and JobCardInspectionController (SQL/53_JobCard.sql).
    /// Reads are inline SQL in the controllers, so no DbSets are mapped here.
    /// Every wrapper returns (ID, Message): ID 0 = refused, Message says why.
    /// </summary>
    public partial class EMSContext
    {
        private static object JcValue(object? value) => value ?? DBNull.Value;

        private static (int ID, string Message) JcResult(SqlParameter id, SqlParameter msg) =>
            (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value), msg.Value?.ToString() ?? string.Empty);

        // ── SP_JobCardMst — Insert allocates JC-<FY>-<no> ────────────────
        public async Task<(int ID, string Message)> CallJobCardMstSP(SP_JobCardMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.VarChar, 250) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@Status",       SqlDbType.VarChar,  10) { Value = dto.Status },
                new SqlParameter("@MTransNo",     SqlDbType.Int)          { Value = dto.MTransNo },
                new SqlParameter("@JobDate",      SqlDbType.Date)         { Value = dto.JobDate.Date },
                new SqlParameter("@OAYear",       SqlDbType.Int)          { Value = dto.OAYear },
                new SqlParameter("@OANo",         SqlDbType.Int)          { Value = dto.OANo },
                new SqlParameter("@OAID",         SqlDbType.Int)          { Value = dto.OAID },
                new SqlParameter("@OALinkNo",     SqlDbType.Int)          { Value = dto.OALinkNo },
                new SqlParameter("@PartNo",       SqlDbType.Int)          { Value = dto.PartNo },
                new SqlParameter("@GradeNo",      SqlDbType.Int)          { Value = dto.GradeNo },
                new SqlParameter("@Grade",        SqlDbType.VarChar,  50) { Value = dto.Grade },
                new SqlParameter("@RouteNo",      SqlDbType.Int)          { Value = dto.RouteNo },
                new SqlParameter("@Qty",          SqlDbType.Decimal)      { Value = dto.Qty, Precision = 18, Scale = 3 },
                new SqlParameter("@RMPartNo",     SqlDbType.Int)          { Value = dto.RMPartNo },
                new SqlParameter("@RMGradeNo",    SqlDbType.Int)          { Value = dto.RMGradeNo },
                new SqlParameter("@RMGrade",      SqlDbType.VarChar,  50) { Value = dto.RMGrade },
                new SqlParameter("@RMQty",        SqlDbType.Decimal)      { Value = dto.RMQty, Precision = 18, Scale = 3 },
                new SqlParameter("@RMUnit",       SqlDbType.VarChar,  20) { Value = JcValue(dto.RMUnit) },
                new SqlParameter("@Operator",     SqlDbType.VarChar,  50) { Value = dto.Operator },
                new SqlParameter("@Shift",        SqlDbType.VarChar,  50) { Value = dto.Shift },
                new SqlParameter("@Remarks",      SqlDbType.VarChar, 250) { Value = JcValue(dto.Remarks) },
                new SqlParameter("@SystemIP",     SqlDbType.VarChar,  20) { Value = JcValue(dto.SystemIP) },
                new SqlParameter("@UserNo",       SqlDbType.Int)          { Value = dto.UserNo },
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = JcValue(dto.DeleteReason) },
                new SqlParameter("@SubscID",      SqlDbType.Int)          { Value = dto.SubscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_JobCardMst
                    @ID = @ID OUTPUT, @Message = @Message OUTPUT, @Status = @Status, @MTransNo = @MTransNo,
                    @JobDate = @JobDate, @OAYear = @OAYear, @OANo = @OANo, @OAID = @OAID, @OALinkNo = @OALinkNo,
                    @PartNo = @PartNo, @GradeNo = @GradeNo, @Grade = @Grade, @RouteNo = @RouteNo, @Qty = @Qty,
                    @RMPartNo = @RMPartNo, @RMGradeNo = @RMGradeNo, @RMGrade = @RMGrade, @RMQty = @RMQty,
                    @RMUnit = @RMUnit, @Operator = @Operator, @Shift = @Shift, @Remarks = @Remarks,
                    @SystemIP = @SystemIP, @UserNo = @UserNo, @DeleteReason = @DeleteReason, @SubscID = @SubscID
                """, p);

            return JcResult(id, msg);
        }

        // ── SP_JobCardEntrySave — replace (clear = false) or clear the entry set ──
        public async Task<(int ID, string Message)> CallJobCardEntrySaveSP(
            int mTransNo, bool clear, string entriesJson, string? reason, int userNo, int subscID)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.VarChar, 250) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int)           { Value = mTransNo },
                new SqlParameter("@Clear",    SqlDbType.Bit)           { Value = clear },
                new SqlParameter("@Entries",  SqlDbType.NVarChar, -1)  { Value = entriesJson },
                new SqlParameter("@Reason",   SqlDbType.VarChar, 250)  { Value = JcValue(reason) },
                new SqlParameter("@UserNo",   SqlDbType.Int)           { Value = userNo },
                new SqlParameter("@SubscID",  SqlDbType.Int)           { Value = subscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_JobCardEntrySave
                    @ID = @ID OUTPUT, @Message = @Message OUTPUT, @MTransNo = @MTransNo, @Clear = @Clear,
                    @Entries = @Entries, @Reason = @Reason, @UserNo = @UserNo, @SubscID = @SubscID
                """, p);

            return JcResult(id, msg);
        }

        // ── SP_JobCardInspection — Insert allocates INS-<FY>-<no> ────────
        public async Task<(int ID, string Message)> CallJobCardInspectionSP(SP_JobCardInspection dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.VarChar, 250) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@Status",         SqlDbType.VarChar,  10) { Value = dto.Status },
                new SqlParameter("@MTransNo",       SqlDbType.Int)          { Value = dto.MTransNo },
                new SqlParameter("@InspDate",       SqlDbType.Date)         { Value = dto.InspDate.Date },
                new SqlParameter("@JobCardTransNo", SqlDbType.Int)          { Value = dto.JobCardTransNo },
                new SqlParameter("@InspectedBy",    SqlDbType.VarChar, 100) { Value = dto.InspectedBy },
                new SqlParameter("@InspectedQty",   SqlDbType.Decimal)      { Value = dto.InspectedQty, Precision = 18, Scale = 3 },
                new SqlParameter("@OKQty",          SqlDbType.Decimal)      { Value = dto.OKQty,        Precision = 18, Scale = 3 },
                new SqlParameter("@ReworkQty",      SqlDbType.Decimal)      { Value = dto.ReworkQty,    Precision = 18, Scale = 3 },
                new SqlParameter("@RejectedQty",    SqlDbType.Decimal)      { Value = dto.RejectedQty,  Precision = 18, Scale = 3 },
                new SqlParameter("@Remarks",        SqlDbType.VarChar, 250) { Value = JcValue(dto.Remarks) },
                new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20) { Value = JcValue(dto.SystemIP) },
                new SqlParameter("@UserNo",         SqlDbType.Int)          { Value = dto.UserNo },
                new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250) { Value = JcValue(dto.DeleteReason) },
                new SqlParameter("@SubscID",        SqlDbType.Int)          { Value = dto.SubscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_JobCardInspection
                    @ID = @ID OUTPUT, @Message = @Message OUTPUT, @Status = @Status, @MTransNo = @MTransNo,
                    @InspDate = @InspDate, @JobCardTransNo = @JobCardTransNo, @InspectedBy = @InspectedBy,
                    @InspectedQty = @InspectedQty, @OKQty = @OKQty, @ReworkQty = @ReworkQty,
                    @RejectedQty = @RejectedQty, @Remarks = @Remarks, @SystemIP = @SystemIP,
                    @UserNo = @UserNo, @DeleteReason = @DeleteReason, @SubscID = @SubscID
                """, p);

            return JcResult(id, msg);
        }
    }
}
