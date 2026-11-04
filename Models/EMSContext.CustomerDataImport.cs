using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Customer Data Import slice of the context. Everything else the import
    /// writes goes through the existing Call*SP helpers.
    /// </summary>
    public partial class EMSContext
    {
        // ── SP_CurrencyMaster (insert only, named parameters) ─────────────
        public async Task<(int ID, string Message)> CallCurrencyMasterImportSP(SP_CurrencyMasterImport dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
                id,
                new SqlParameter("@Country",       SqlDbType.VarChar, 50) { Value = (object?)dto.Country?.Trim() ?? DBNull.Value },
                new SqlParameter("@Currency",      SqlDbType.VarChar, 50) { Value = dto.Currency.Trim() },
                new SqlParameter("@CurrencyCode",  SqlDbType.VarChar, 10) { Value = dto.CurrencyCode.Trim() },
                new SqlParameter("@ExchangeRate",  SqlDbType.Decimal)     { Precision = 12, Scale = 2, Value = dto.ExchangeRate },
                new SqlParameter("@EffectiveDate", SqlDbType.Date)        { Value = dto.EffectiveDate },
                new SqlParameter("@SystemIP",      SqlDbType.VarChar, 20) { Value = (object?)dto.SystemIP ?? DBNull.Value },
                new SqlParameter("@LockStatus",    SqlDbType.Char, 1)     { Value = dto.LockStatus },
                new SqlParameter("@CreatedBy",     SqlDbType.Int)         { Value = dto.CreatedBy },
                new SqlParameter("@SubscID",       SqlDbType.Int)         { Value = dto.SubscID },
                msg
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_CurrencyMaster
                    @ID = @ID OUTPUT, @Country = @Country, @Currency = @Currency,
                    @CurrencyCode = @CurrencyCode, @ExchangeRate = @ExchangeRate,
                    @EffectiveDate = @EffectiveDate, @SystemIP = @SystemIP,
                    @LockStatus = @LockStatus, @CreatedBy = @CreatedBy,
                    @SubscID = @SubscID, @Status = 'Insert', @Message = @Message OUTPUT
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
