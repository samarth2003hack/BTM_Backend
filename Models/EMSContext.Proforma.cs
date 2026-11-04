using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Proforma Invoice: DbSets and SP wrappers (SQL/19_ProformaInvoice.sql).
    /// Same contract as every other Call*SP helper — @ID output (affected key,
    /// or 0 on refusal) and @Message output.
    /// </summary>
    public partial class EMSContext
    {
        public DbSet<ProformaMst> ProformaMst { get; set; } = null!;
        public DbSet<ProformaDtl> ProformaDtl { get; set; } = null!;
        public DbSet<ProformaTerms> ProformaTerms { get; set; } = null!;
        public DbSet<ProformaExtra> ProformaExtra { get; set; } = null!;

        // ── SP_ProformaMst ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallProformaMstSP(SP_ProformaMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@DocSeries",      SqlDbType.Char,     10)  { Value = (object?)dto.DocSeries?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)           { Value = (object?)dto.DocYear                ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)           { Value = (object?)dto.DocNo                  ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)          { Value = (object?)dto.DocDate                ?? DBNull.Value },
        new SqlParameter("@InvType",        SqlDbType.VarChar,  20)  { Value = (object?)dto.InvType?.Trim()        ?? DBNull.Value },
        new SqlParameter("@AmdNo",          SqlDbType.Int)           { Value = (object?)dto.AmdNo                  ?? DBNull.Value },
        new SqlParameter("@AmdNotes",       SqlDbType.VarChar, 100)  { Value = (object?)dto.AmdNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",  SqlDbType.Int)           { Value = (object?)dto.TargetTransNo          ?? DBNull.Value },
        new SqlParameter("@TargetDocYear",  SqlDbType.Int)           { Value = (object?)dto.TargetDocYear          ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",    SqlDbType.Int)           { Value = (object?)dto.TargetDocNo            ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",    SqlDbType.Int)           { Value = (object?)dto.TargetAmdNo            ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)           { Value = (object?)dto.VendorNo               ?? DBNull.Value },
        new SqlParameter("@VendorNoShip",   SqlDbType.Int)           { Value = (object?)dto.VendorNoShip           ?? DBNull.Value },
        new SqlParameter("@FromAccount",    SqlDbType.Int)           { Value = (object?)dto.FromAccount            ?? DBNull.Value },
        new SqlParameter("@VendorCode",     SqlDbType.VarChar,  20)  { Value = (object?)dto.VendorCode?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Address",        SqlDbType.VarChar, 500)  { Value = (object?)dto.Address?.Trim()        ?? DBNull.Value },
        new SqlParameter("@GSTIN",          SqlDbType.VarChar,  20)  { Value = (object?)dto.GSTIN?.Trim()          ?? DBNull.Value },
        new SqlParameter("@StateName",      SqlDbType.VarChar,  50)  { Value = (object?)dto.StateName?.Trim()      ?? DBNull.Value },
        new SqlParameter("@PhoneNo",        SqlDbType.VarChar,  30)  { Value = (object?)dto.PhoneNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@EmailID",        SqlDbType.VarChar,  80)  { Value = (object?)dto.EmailID?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefNo",          SqlDbType.VarChar,  50)  { Value = (object?)dto.RefNo?.Trim()          ?? DBNull.Value },
        new SqlParameter("@RefDate",        SqlDbType.Date)          { Value = (object?)dto.RefDate                ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@TaxType",        SqlDbType.VarChar,  20)  { Value = (object?)dto.TaxType?.Trim()        ?? DBNull.Value },
        new SqlParameter("@BasicValue",     SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue      ?? DBNull.Value },
        new SqlParameter("@DiscountAmt",    SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.DiscountAmt     ?? DBNull.Value },
        new SqlParameter("@ExemptValue",    SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.ExemptValue     ?? DBNull.Value },
        new SqlParameter("@TaxableValue",   SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.TaxableValue    ?? DBNull.Value },
        new SqlParameter("@PnFAmt",         SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.PnFAmt          ?? DBNull.Value },
        new SqlParameter("@PnFGSTP",        SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.PnFGSTP         ?? DBNull.Value },
        new SqlParameter("@PnFGSTAmt",      SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.PnFGSTAmt       ?? DBNull.Value },
        new SqlParameter("@FreightAmt",     SqlDbType.Decimal)       { Precision = 14, Scale = 2, Value = (object?)dto.FreightAmt      ?? DBNull.Value },
        new SqlParameter("@FrightGSTP",     SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.FrightGSTP      ?? DBNull.Value },
        new SqlParameter("@FrightGSTAmt",   SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.FrightGSTAmt    ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.IGSTAmt         ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.CGSTAmt         ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.SGSTAmt         ?? DBNull.Value },
        new SqlParameter("@OtherCharges",   SqlDbType.VarChar,  50)  { Value = (object?)dto.OtherCharges?.Trim()   ?? DBNull.Value },
        new SqlParameter("@OtherAmt",       SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.OtherAmt        ?? DBNull.Value },
        new SqlParameter("@OtherP",         SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.OtherP          ?? DBNull.Value },
        new SqlParameter("@RoundOff",       SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.RoundOff        ?? DBNull.Value },
        new SqlParameter("@NetValue",       SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.NetValue        ?? DBNull.Value },
        new SqlParameter("@AdvanceP",       SqlDbType.Decimal)       { Precision = 12, Scale = 0, Value = (object?)dto.AdvanceP        ?? DBNull.Value },
        new SqlParameter("@Advance",        SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.Advance         ?? DBNull.Value },
        new SqlParameter("@Grossweight",    SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.Grossweight     ?? DBNull.Value },
        new SqlParameter("@Netweight",      SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.Netweight       ?? DBNull.Value },
        new SqlParameter("@TxnCurrency",    SqlDbType.VarChar,  30)  { Value = (object?)dto.TxnCurrency?.Trim()    ?? DBNull.Value },
        new SqlParameter("@CurrencyRate",   SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.CurrencyRate    ?? DBNull.Value },
        new SqlParameter("@CurrencyAmount", SqlDbType.Decimal)       { Precision = 14, Scale = 2, Value = (object?)dto.CurrencyAmount  ?? DBNull.Value },
        new SqlParameter("@ProjectNo",      SqlDbType.Int)           { Value = (object?)dto.ProjectNo              ?? DBNull.Value },
        new SqlParameter("@SalesRep",       SqlDbType.VarChar,  50)  { Value = (object?)dto.SalesRep?.Trim()       ?? DBNull.Value },
        new SqlParameter("@TxnGSTIN",       SqlDbType.VarChar,  20)  { Value = (object?)dto.TxnGSTIN?.Trim()       ?? DBNull.Value },
        new SqlParameter("@IECodeNo",       SqlDbType.VarChar,  50)  { Value = (object?)dto.IECodeNo?.Trim()       ?? DBNull.Value },
        new SqlParameter("@Declaration",    SqlDbType.VarChar, 500)  { Value = (object?)dto.Declaration?.Trim()    ?? DBNull.Value },
        new SqlParameter("@BankNo",         SqlDbType.Int)           { Value = (object?)dto.BankNo                 ?? DBNull.Value },
        new SqlParameter("@InvTag",         SqlDbType.Char,     10)  { Value = (object?)dto.InvTag?.Trim()         ?? DBNull.Value },
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
        EXEC dbo.SP_ProformaMst
            @ID OUTPUT, @MTransNo,
            @DocSeries, @DocYear, @DocNo, @DocDate, @InvType, @AmdNo, @AmdNotes,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @VendorNo, @VendorNoShip, @FromAccount,
            @VendorCode, @VendorName, @Address, @GSTIN, @StateName, @PhoneNo, @EmailID,
            @RefNo, @RefDate, @DocNotes, @TaxType,
            @BasicValue, @DiscountAmt, @ExemptValue, @TaxableValue,
            @PnFAmt, @PnFGSTP, @PnFGSTAmt, @FreightAmt, @FrightGSTP, @FrightGSTAmt,
            @IGSTAmt, @CGSTAmt, @SGSTAmt,
            @OtherCharges, @OtherAmt, @OtherP, @RoundOff, @NetValue,
            @AdvanceP, @Advance, @Grossweight, @Netweight,
            @TxnCurrency, @CurrencyRate, @CurrencyAmount,
            @ProjectNo, @SalesRep, @TxnGSTIN, @IECodeNo, @Declaration, @BankNo, @InvTag,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_ProformaDtl ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallProformaDtlSP(SP_ProformaDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",      dto.TransSlNo),
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@SlNo",           SqlDbType.Int)          { Value = (object?)dto.SlNo                  ?? DBNull.Value },
        new SqlParameter("@PartNo",         SqlDbType.Int)          { Value = (object?)dto.PartNo                ?? DBNull.Value },
        new SqlParameter("@PartCode",       SqlDbType.VarChar,  50) { Value = (object?)dto.PartCode?.Trim()      ?? DBNull.Value },
        new SqlParameter("@Description",    SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()   ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",    SqlDbType.VarChar, 250) { Value = (object?)dto.ItemRemarks?.Trim()   ?? DBNull.Value },
        new SqlParameter("@CustPartNo",     SqlDbType.VarChar,  30) { Value = (object?)dto.CustPartNo?.Trim()    ?? DBNull.Value },
        new SqlParameter("@HSNCode",        SqlDbType.VarChar,  10) { Value = (object?)dto.HSNCode?.Trim()       ?? DBNull.Value },
        new SqlParameter("@Qty",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.Qty           ?? DBNull.Value },
        new SqlParameter("@Unit",           SqlDbType.VarChar,  20) { Value = (object?)dto.Unit?.Trim()          ?? DBNull.Value },
        new SqlParameter("@Rate",           SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.Rate          ?? DBNull.Value },
        new SqlParameter("@BasicValue",     SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue    ?? DBNull.Value },
        new SqlParameter("@DiscP",          SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.DiscP         ?? DBNull.Value },
        new SqlParameter("@DiscAmt",        SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.DiscAmt       ?? DBNull.Value },
        new SqlParameter("@PnFP",           SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.PnFP          ?? DBNull.Value },
        new SqlParameter("@PnFAmt",         SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.PnFAmt        ?? DBNull.Value },
        new SqlParameter("@PnFGSTAmt",      SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.PnFGSTAmt     ?? DBNull.Value },
        new SqlParameter("@FreightRate",    SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.FreightRate   ?? DBNull.Value },
        new SqlParameter("@FreightAmt",     SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.FreightAmt    ?? DBNull.Value },
        new SqlParameter("@FreightGSTAmt",  SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.FreightGSTAmt ?? DBNull.Value },
        new SqlParameter("@TaxableValue",   SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.TaxableValue  ?? DBNull.Value },
        new SqlParameter("@GSTP",           SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.GSTP          ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",        SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.IGSTAmt       ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",        SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.CGSTAmt       ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",        SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.SGSTAmt       ?? DBNull.Value },
        new SqlParameter("@TotalValue",     SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.TotalValue    ?? DBNull.Value },
        new SqlParameter("@DTargetDocNo",   SqlDbType.Int)          { Value = (object?)dto.DTargetDocNo          ?? DBNull.Value },
        new SqlParameter("@DTargetDocYear", SqlDbType.Int)          { Value = (object?)dto.DTargetDocYear        ?? DBNull.Value },
        new SqlParameter("@Packing",        SqlDbType.VarChar,  50) { Value = (object?)dto.Packing?.Trim()       ?? DBNull.Value },
        new SqlParameter("@BoxNo",          SqlDbType.Int)          { Value = (object?)dto.BoxNo                 ?? DBNull.Value },
        new SqlParameter("@NetWeight",      SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.NetWeight     ?? DBNull.Value },
        new SqlParameter("@ChassisNo",      SqlDbType.VarChar,  30) { Value = (object?)dto.ChassisNo?.Trim()     ?? DBNull.Value },
        new SqlParameter("@EngineNo",       SqlDbType.VarChar,  30) { Value = (object?)dto.EngineNo?.Trim()      ?? DBNull.Value },
        new SqlParameter("@RegdNo",         SqlDbType.VarChar,  30) { Value = (object?)dto.RegdNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@LinkNo",         SqlDbType.Int)          { Value = (object?)dto.LinkNo                ?? DBNull.Value },
        new SqlParameter("@DeleveryDate",   SqlDbType.Date)         { Value = (object?)dto.DeleveryDate          ?? DBNull.Value },
        new SqlParameter("@Store",          SqlDbType.VarChar,  50) { Value = (object?)dto.Store?.Trim()         ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)          { Value = (object?)dto.VendorNo              ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP              ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)          { Value = (object?)dto.CreatedBy             ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn             ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)          { Value = (object?)dto.ModifiedBy            ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn            ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason          ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1) { Value = (object?)dto.LockStatus            ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)          { Value = (object?)dto.SubscID               ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_ProformaDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @PartNo, @PartCode, @Description, @ItemRemarks, @CustPartNo, @HSNCode,
            @Qty, @Unit, @Rate, @BasicValue, @DiscP, @DiscAmt, @PnFP, @PnFAmt, @PnFGSTAmt,
            @FreightRate, @FreightAmt, @FreightGSTAmt, @TaxableValue, @GSTP,
            @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
            @DTargetDocNo, @DTargetDocYear, @Packing, @BoxNo, @NetWeight,
            @ChassisNo, @EngineNo, @RegdNo, @LinkNo, @DeleveryDate, @Store, @VendorNo,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_ProformaTerms ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallProformaTermsSP(SP_ProformaTerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_ProformaTerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_ProformaExtra ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallProformaExtraSP(SP_ProformaExtra dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_ProformaExtra
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
