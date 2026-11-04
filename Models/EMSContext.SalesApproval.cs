using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Input to SP_QtnApproval / SP_OAApproval (SQL/34_SalesApproval.sql).
    /// Status is 'Approve' (LockStatus → Y) or 'Reject' (LockStatus → R,
    /// RejectReason required).
    /// </summary>
    public class SP_SalesApproval
    {
        public int MTransNo { get; set; }
        public string? RejectReason { get; set; }
        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Approve";
    }

    /// <summary>
    /// Quotation / Order Acceptance approval: SP wrappers. Same contract as
    /// every other Call*SP helper — @ID output (affected key, or 0 on
    /// refusal) and @Message output.
    /// </summary>
    public partial class EMSContext
    {
        public Task<(int ID, string Message)> CallQtnApprovalSP(SP_SalesApproval dto) =>
            CallSalesApprovalSP("dbo.SP_QtnApproval", dto);

        public Task<(int ID, string Message)> CallOAApprovalSP(SP_SalesApproval dto) =>
            CallSalesApprovalSP("dbo.SP_OAApproval", dto);

        // Purchase Order uses the same contract (SQL/43_PurchaseOrderApproval.sql).
        public Task<(int ID, string Message)> CallPOApprovalSP(SP_SalesApproval dto) =>
            CallSalesApprovalSP("dbo.SP_POApproval", dto);

        private async Task<(int ID, string Message)> CallSalesApprovalSP(string procedure, SP_SalesApproval dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
                id,
                new SqlParameter("@MTransNo",     dto.MTransNo),
                new SqlParameter("@RejectReason", SqlDbType.VarChar, 250) { Value=(object?)dto.RejectReason ?? DBNull.Value },
                new SqlParameter("@SystemIP",     SqlDbType.VarChar, 20)  { Value=(object?)dto.SystemIP     ?? DBNull.Value },
                new SqlParameter("@ModifiedBy",   SqlDbType.Int)          { Value=(object?)dto.ModifiedBy   ?? DBNull.Value },
                new SqlParameter("@SubscID",      SqlDbType.Int)          { Value=(object?)dto.SubscID      ?? DBNull.Value },
                new SqlParameter("@Status",       SqlDbType.VarChar, 20)  { Value=dto.Status },
                msg
            };

            // procedure is one of the constants above, never caller input.
            await Database.ExecuteSqlRawAsync(
                $"EXEC {procedure} @ID OUTPUT, @MTransNo, @RejectReason, @SystemIP, " +
                "@ModifiedBy, @SubscID, @Status, @Message OUTPUT", p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
