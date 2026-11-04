using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Input to SP_QtnFollowUp (SQL/50_QuotationFollowUp.sql).
    /// Status is 'Insert', 'Update' or 'Delete'.
    /// </summary>
    public class SP_QtnFollowUp
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public DateTime? FollowupDate { get; set; }
        public string? FollowUpType { get; set; }
        public string? ContactPerson { get; set; }
        public string? Response { get; set; }
        public DateTime? NextVisitDate { get; set; }
        public string? FollowupNotes { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    /// <summary>
    /// Quotation follow-up + truncate: SP wrappers. Same contract as every
    /// other Call*SP helper — @ID output (affected key, or 0 on refusal) and
    /// @Message output.
    /// </summary>
    public partial class EMSContext
    {
        public async Task<(int ID, string Message)> CallQtnFollowUpSP(SP_QtnFollowUp dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
                id,
                new SqlParameter("@TransSlNo",     dto.TransSlNo),
                new SqlParameter("@MTransNo",      dto.MTransNo),
                new SqlParameter("@FollowupDate",  SqlDbType.Date)          { Value = (object?)dto.FollowupDate?.Date  ?? DBNull.Value },
                new SqlParameter("@FollowUpType",  SqlDbType.VarChar,   30) { Value = (object?)dto.FollowUpType?.Trim()  ?? DBNull.Value },
                new SqlParameter("@ContactPerson", SqlDbType.VarChar,  100) { Value = (object?)dto.ContactPerson?.Trim() ?? DBNull.Value },
                new SqlParameter("@Response",      SqlDbType.VarChar,   30) { Value = (object?)dto.Response?.Trim()      ?? DBNull.Value },
                new SqlParameter("@NextVisitDate", SqlDbType.Date)          { Value = (object?)dto.NextVisitDate?.Date ?? DBNull.Value },
                new SqlParameter("@FollowupNotes", SqlDbType.VarChar, 2000) { Value = (object?)dto.FollowupNotes?.Trim() ?? DBNull.Value },
                new SqlParameter("@SystemIP",      SqlDbType.VarChar,   20) { Value = (object?)dto.SystemIP              ?? DBNull.Value },
                new SqlParameter("@CreatedBy",     SqlDbType.Int)           { Value = (object?)dto.CreatedBy             ?? DBNull.Value },
                new SqlParameter("@CreatedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn             ?? DBNull.Value },
                new SqlParameter("@ModifiedBy",    SqlDbType.Int)           { Value = (object?)dto.ModifiedBy            ?? DBNull.Value },
                new SqlParameter("@ModifiedOn",    SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn            ?? DBNull.Value },
                new SqlParameter("@DeleteReason",  SqlDbType.VarChar,  250) { Value = (object?)dto.DeleteReason          ?? DBNull.Value },
                new SqlParameter("@SubscID",       SqlDbType.Int)           { Value = (object?)dto.SubscID               ?? DBNull.Value },
                new SqlParameter("@Status",        SqlDbType.VarChar,   20) { Value = dto.Status },
                msg
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_QtnFollowUp
                    @ID OUTPUT, @TransSlNo, @MTransNo, @FollowupDate, @FollowUpType,
                    @ContactPerson, @Response, @NextVisitDate, @FollowupNotes, @SystemIP,
                    @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn, @DeleteReason,
                    @SubscID, @Status, @Message OUTPUT
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        public async Task<(int ID, string Message)> CallQtnTruncateSP(
            int mTransNo, string? reason, string? systemIP, int modifiedBy, int subscID)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
                id,
                new SqlParameter("@MTransNo",       mTransNo),
                new SqlParameter("@TruncateReason", SqlDbType.VarChar, 500) { Value = (object?)reason   ?? DBNull.Value },
                new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20) { Value = (object?)systemIP ?? DBNull.Value },
                new SqlParameter("@ModifiedBy",     modifiedBy),
                new SqlParameter("@SubscID",        subscID),
                msg
            };

            await Database.ExecuteSqlRawAsync(
                "EXEC dbo.SP_QtnTruncate @ID OUTPUT, @MTransNo, @TruncateReason, @SystemIP, " +
                "@ModifiedBy, @SubscID, @Message OUTPUT", p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
