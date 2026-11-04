using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Packing Slip slice of the context: DbSets for reads, SP wrappers for
    /// writes. Same @ID / @Message contract as every other Call*SP helper.
    /// </summary>
    public partial class EMSContext
    {
        public DbSet<PackingSlipMst> PackingSlipMst { get; set; } = null!;
        public DbSet<PackingSlipDtl> PackingSlipDtl { get; set; } = null!;

        // ── SP_PackingSlipMst ─────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPackingSlipMstSP(SP_PackingSlipMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@DocSeries",      SqlDbType.VarChar,  10)  { Value = (object?)dto.DocSeries?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)           { Value = (object?)dto.DocYear                ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)           { Value = (object?)dto.DocNo                  ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)          { Value = (object?)dto.DocDate                ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",  SqlDbType.Int)           { Value = (object?)dto.TargetTransNo          ?? DBNull.Value },
        new SqlParameter("@TargetDocYear",  SqlDbType.Int)           { Value = (object?)dto.TargetDocYear          ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",    SqlDbType.Int)           { Value = (object?)dto.TargetDocNo            ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",    SqlDbType.Int)           { Value = (object?)dto.TargetAmdNo            ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)           { Value = (object?)dto.VendorNo               ?? DBNull.Value },
        new SqlParameter("@VendorCode",     SqlDbType.VarChar,  20)  { Value = (object?)dto.VendorCode?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Address",        SqlDbType.VarChar, 500)  { Value = (object?)dto.Address?.Trim()        ?? DBNull.Value },
        new SqlParameter("@GSTIN",          SqlDbType.VarChar,  20)  { Value = (object?)dto.GSTIN?.Trim()          ?? DBNull.Value },
        new SqlParameter("@StateName",      SqlDbType.VarChar,  50)  { Value = (object?)dto.StateName?.Trim()      ?? DBNull.Value },
        new SqlParameter("@PhoneNo",        SqlDbType.VarChar,  30)  { Value = (object?)dto.PhoneNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@EmailID",        SqlDbType.VarChar,  80)  { Value = (object?)dto.EmailID?.Trim()        ?? DBNull.Value },
        new SqlParameter("@OAYear",         SqlDbType.Int)           { Value = (object?)dto.OAYear                 ?? DBNull.Value },
        new SqlParameter("@OANo",           SqlDbType.Int)           { Value = (object?)dto.OANo                   ?? DBNull.Value },
        new SqlParameter("@OADate",         SqlDbType.Date)          { Value = (object?)dto.OADate                 ?? DBNull.Value },
        new SqlParameter("@RefNo",          SqlDbType.VarChar,  50)  { Value = (object?)dto.RefNo?.Trim()          ?? DBNull.Value },
        new SqlParameter("@RefDate",        SqlDbType.Date)          { Value = (object?)dto.RefDate                ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@TotalCases",     SqlDbType.Int)           { Value = (object?)dto.TotalCases             ?? DBNull.Value },
        new SqlParameter("@TotalWeight",    SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.TotalWeight ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP               ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)           { Value = (object?)dto.CreatedBy              ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn              ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)           { Value = (object?)dto.ModifiedBy             ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn             ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason           ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1)  { Value = (object?)dto.LockStatus             ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)           { Value = (object?)dto.SubscID                ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_PackingSlipMst
            @ID OUTPUT, @MTransNo,
            @DocSeries, @DocYear, @DocNo, @DocDate,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
            @StateName, @PhoneNo, @EmailID,
            @OAYear, @OANo, @OADate, @RefNo, @RefDate,
            @DocNotes, @TotalCases, @TotalWeight,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_PackingSlipDtl ─────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPackingSlipDtlSP(SP_PackingSlipDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",       dto.TransSlNo),
        new SqlParameter("@MTransNo",        dto.MTransNo),
        new SqlParameter("@SlNo",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.SlNo        ?? DBNull.Value },
        new SqlParameter("@OAID",            SqlDbType.Int)          { Value = (object?)dto.OAID                                  ?? DBNull.Value },
        new SqlParameter("@CaseNo",          SqlDbType.Int)          { Value = (object?)dto.CaseNo                                ?? DBNull.Value },
        new SqlParameter("@PartNo",          SqlDbType.Int)          { Value = (object?)dto.PartNo                                ?? DBNull.Value },
        new SqlParameter("@PartCode",        SqlDbType.VarChar,  30) { Value = (object?)dto.PartCode?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@PartDescription", SqlDbType.VarChar, 250) { Value = (object?)dto.PartDescription?.Trim()               ?? DBNull.Value },
        new SqlParameter("@MOC",             SqlDbType.VarChar, 250) { Value = (object?)dto.MOC?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@OARemarks",       SqlDbType.VarChar, 250) { Value = (object?)dto.OARemarks?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",     SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim()                   ?? DBNull.Value },
        new SqlParameter("@HSNCode",         SqlDbType.VarChar,  10) { Value = (object?)dto.HSNCode?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@Qty",             SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.Qty         ?? DBNull.Value },
        new SqlParameter("@Unit",            SqlDbType.VarChar,  20) { Value = (object?)dto.Unit?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@CaseSize",        SqlDbType.VarChar,  50) { Value = (object?)dto.CaseSize?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@NetWeight",       SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.NetWeight   ?? DBNull.Value },
        new SqlParameter("@GrossWeight",     SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.GrossWeight ?? DBNull.Value },
        new SqlParameter("@ValveSlNo",       SqlDbType.VarChar, 200) { Value = (object?)dto.ValveSlNo?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@BoxType",         SqlDbType.VarChar,  50) { Value = (object?)dto.BoxType?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@SystemIP",        SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                              ?? DBNull.Value },
        new SqlParameter("@CreatedBy",       SqlDbType.Int)          { Value = (object?)dto.CreatedBy                             ?? DBNull.Value },
        new SqlParameter("@CreatedOn",       SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                             ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",      SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                            ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                            ?? DBNull.Value },
        new SqlParameter("@DeleteReason",    SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                          ?? DBNull.Value },
        new SqlParameter("@LockStatus",      SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                            ?? "N"         },
        new SqlParameter("@SubscID",         SqlDbType.Int)          { Value = (object?)dto.SubscID                               ?? DBNull.Value },
        new SqlParameter("@Status",          dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_PackingSlipDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @OAID, @CaseNo, @PartNo, @PartCode, @PartDescription,
            @MOC, @OARemarks, @ItemRemarks, @HSNCode,
            @Qty, @Unit, @CaseSize, @NetWeight, @GrossWeight,
            @ValveSlNo, @BoxType,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
