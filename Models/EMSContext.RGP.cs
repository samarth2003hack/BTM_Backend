using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// RGP / Delivery Challan slice of the context. The DbSets and the
    /// original Call*SP helpers for the RGP tables live in EMSContext.cs; this
    /// file carries what SQL 28 added on top of them.
    /// </summary>
    public partial class EMSContext
    {
        // ── SP_RGPInwDtl, with @LinkNo ───────────────────────────────
        // Same contract as CallRGPInwDtlSP, plus the RGPDtl line the inward
        // line receives against. Named parameters, so the call does not
        // depend on @LinkNo's position at the end of the signature.
        public async Task<(int ID, string Message)> CallRGPInwDtlLinkedSP(SP_RGPInwDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
                id,
                new SqlParameter("@TransSlNo",       dto.TransSlNo),
                new SqlParameter("@MTransNo",        dto.MTransNo),
                new SqlParameter("@SlNo",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.SlNo ?? DBNull.Value },
                new SqlParameter("@PartNo",          SqlDbType.Int)          { Value = (object?)dto.PartNo ?? DBNull.Value },
                new SqlParameter("@PartCode",        SqlDbType.VarChar,  30) { Value = (object?)dto.PartCode?.Trim() ?? DBNull.Value },
                new SqlParameter("@PartDescription", SqlDbType.VarChar, 100) { Value = (object?)dto.PartDescription?.Trim() ?? DBNull.Value },
                new SqlParameter("@ItemRemarks",     SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim() ?? DBNull.Value },
                new SqlParameter("@HSNCode",         SqlDbType.VarChar,  10) { Value = (object?)dto.HSNCode?.Trim() ?? DBNull.Value },
                new SqlParameter("@RGPQty",          SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.RGPQty ?? DBNull.Value },
                new SqlParameter("@Qty",             SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.Qty ?? DBNull.Value },
                new SqlParameter("@Unit",            SqlDbType.VarChar,  20) { Value = (object?)dto.Unit?.Trim() ?? DBNull.Value },
                new SqlParameter("@Rate",            SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.Rate ?? DBNull.Value },
                new SqlParameter("@GradeNo",         SqlDbType.Int)          { Value = (object?)dto.GradeNo ?? DBNull.Value },
                new SqlParameter("@Grade",           SqlDbType.VarChar,  50) { Value = (object?)dto.Grade?.Trim() ?? DBNull.Value },
                new SqlParameter("@SystemIP",        SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP ?? DBNull.Value },
                new SqlParameter("@CreatedBy",       SqlDbType.Int)          { Value = (object?)dto.CreatedBy ?? DBNull.Value },
                new SqlParameter("@CreatedOn",       SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn ?? DBNull.Value },
                new SqlParameter("@ModifiedBy",      SqlDbType.Int)          { Value = (object?)dto.ModifiedBy ?? DBNull.Value },
                new SqlParameter("@ModifiedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn ?? DBNull.Value },
                new SqlParameter("@DeleteReason",    SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason ?? DBNull.Value },
                new SqlParameter("@LockStatus",      SqlDbType.Char,      1) { Value = (object?)dto.LockStatus ?? "N" },
                new SqlParameter("@SubscID",         SqlDbType.Int)          { Value = (object?)dto.SubscID ?? DBNull.Value },
                new SqlParameter("@Status",          dto.Status),
                msg,
                new SqlParameter("@LinkNo",          SqlDbType.Int)          { Value = (object?)dto.LinkNo ?? DBNull.Value },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_RGPInwDtl
                    @ID = @ID OUTPUT, @TransSlNo = @TransSlNo, @MTransNo = @MTransNo, @SlNo = @SlNo,
                    @PartNo = @PartNo, @PartCode = @PartCode, @PartDescription = @PartDescription,
                    @ItemRemarks = @ItemRemarks, @HSNCode = @HSNCode,
                    @RGPQty = @RGPQty, @Qty = @Qty, @Unit = @Unit, @Rate = @Rate,
                    @GradeNo = @GradeNo, @Grade = @Grade,
                    @SystemIP = @SystemIP, @CreatedBy = @CreatedBy, @CreatedOn = @CreatedOn,
                    @ModifiedBy = @ModifiedBy, @ModifiedOn = @ModifiedOn,
                    @DeleteReason = @DeleteReason, @LockStatus = @LockStatus, @SubscID = @SubscID,
                    @Status = @Status, @Message = @Message OUTPUT, @LinkNo = @LinkNo
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
