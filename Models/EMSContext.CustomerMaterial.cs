using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Customer Material Inward slice of the context: the SP wrappers behind
    /// CustomerMaterialInwardController (SQL/60_CustomerMaterialInward.sql).
    /// Reads are inline SQL in the controller (PmQueryRowsAsync), so no DbSets
    /// are mapped here. Every wrapper returns (ID, Message): ID 0 = refused,
    /// Message says why.
    /// </summary>
    public partial class EMSContext
    {
        private static object CmValue(object? value) => value ?? DBNull.Value;

        private static (int ID, string Message) CmResult(SqlParameter id, SqlParameter msg) =>
            (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value), msg.Value?.ToString() ?? string.Empty);

        // ── SP_CustMatInwardMst — Insert allocates CMI-<FY>-<no> ─────────
        public async Task<(int ID, string Message)> CallCustMatInwardMstSP(SP_CustMatInwardMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@Status",       SqlDbType.VarChar,  10) { Value = dto.Status },
                new SqlParameter("@MTransNo",     SqlDbType.Int)          { Value = dto.MTransNo },
                new SqlParameter("@InwardDate",   SqlDbType.Date)         { Value = dto.InwardDate.Date },
                new SqlParameter("@CustomerNo",   SqlDbType.Int)          { Value = dto.CustomerNo },
                new SqlParameter("@OAYear",       SqlDbType.Int)          { Value = dto.OAYear },
                new SqlParameter("@OANo",         SqlDbType.Int)          { Value = dto.OANo },
                new SqlParameter("@DCNo",         SqlDbType.VarChar,  30) { Value = CmValue(dto.DCNo) },
                new SqlParameter("@DCDate",       SqlDbType.Date)         { Value = CmValue(dto.DCDate?.Date) },
                new SqlParameter("@ReceivedBy",   SqlDbType.VarChar, 100) { Value = CmValue(dto.ReceivedBy) },
                new SqlParameter("@Remarks",      SqlDbType.VarChar, 250) { Value = CmValue(dto.Remarks) },
                new SqlParameter("@DocStatus",    SqlDbType.VarChar,  20) { Value = CmValue(dto.DocStatus) },
                new SqlParameter("@SystemIP",     SqlDbType.VarChar,  20) { Value = CmValue(dto.SystemIP) },
                new SqlParameter("@UserNo",       SqlDbType.Int)          { Value = dto.UserNo },
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = CmValue(dto.DeleteReason) },
                new SqlParameter("@SubscID",      SqlDbType.Int)          { Value = dto.SubscID },
                new SqlParameter("@DocType",      SqlDbType.Char,      1) { Value = string.IsNullOrWhiteSpace(dto.DocType) ? "I" : dto.DocType },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_CustMatInwardMst
                    @ID = @ID OUTPUT, @Message = @Message OUTPUT, @Status = @Status, @MTransNo = @MTransNo,
                    @InwardDate = @InwardDate, @CustomerNo = @CustomerNo, @OAYear = @OAYear, @OANo = @OANo,
                    @DCNo = @DCNo, @DCDate = @DCDate, @ReceivedBy = @ReceivedBy, @Remarks = @Remarks,
                    @DocStatus = @DocStatus, @SystemIP = @SystemIP, @UserNo = @UserNo,
                    @DeleteReason = @DeleteReason, @SubscID = @SubscID, @DocType = @DocType
                """, p);

            return CmResult(id, msg);
        }

        // ── SP_CustMatInwardDtlSave — replace an inward's whole line set (JSON) ──
        public async Task<(int ID, string Message)> CallCustMatInwardDtlSaveSP(
            int mTransNo, string linesJson, int userNo, int subscID)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int)          { Value = mTransNo },
                new SqlParameter("@Lines",    SqlDbType.NVarChar, -1) { Value = linesJson },
                new SqlParameter("@UserNo",   SqlDbType.Int)          { Value = userNo },
                new SqlParameter("@SubscID",  SqlDbType.Int)          { Value = subscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_CustMatInwardDtlSave
                    @ID = @ID OUTPUT, @MTransNo = @MTransNo, @Lines = @Lines,
                    @UserNo = @UserNo, @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);

            return CmResult(id, msg);
        }
    }
}
