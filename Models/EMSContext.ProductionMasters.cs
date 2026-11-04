using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Production masters slice of the context (SQL 54): the Conversion BOM
    /// table and the procedures added for the Machine / Die / Routing / BOM
    /// pages. SP_MachineMst / SP_DieMst / SP_RoutingChartMst keep their
    /// callers in EMSContext.cs (their signatures did not change).
    /// </summary>
    public partial class EMSContext
    {
        public DbSet<ConvBOM> ConvBOM { get; set; } = null!;

        private static object PmDbValue(object? value) => value ?? DBNull.Value;

        // ── Inline read helper for the production master lists: one dictionary
        //    per row, keyed by the column alias (aliases are camelCase, since
        //    dictionary keys are serialised as-is). Strings come back trimmed. ──
        public async Task<List<Dictionary<string, object?>>> PmQueryRowsAsync(
            string sql, params SqlParameter[] parameters)
        {
            var con = Database.GetDbConnection();
            if (con.State != ConnectionState.Open)
                await con.OpenAsync();

            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value is string s ? s.Trim() : value;
                }
                rows.Add(row);
            }
            return rows;
        }

        // ── True when a user table exists. JobCardMst / JobCardEntry (SQL 53)
        //    and the maintenance tables (SQL 55) may not exist on every server. ──
        public async Task<bool> PmTableExistsAsync(string table)
        {
            var rows = await PmQueryRowsAsync(
                "SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NULL THEN 0 ELSE 1 END AS ok",
                new SqlParameter("@t", SqlDbType.NVarChar, 256) { Value = "dbo." + table });
            return rows.Count > 0 && Convert.ToInt32(rows[0]["ok"]) == 1;
        }

        // ── SP_ConvBOM — Insert / Update / Delete; ID 0 = refused (see Message) ──
        public async Task<(int ID, string Message)> CallConvBOMSP(
            ConvBOMSaveRequest dto, string status, string? reason, string? systemIP)
        {
            var id  = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo",     SqlDbType.Int)           { Value = dto.MTransNo },
                new SqlParameter("@FPartNo",      SqlDbType.Int)           { Value = dto.FPartNo },
                new SqlParameter("@FGradeNo",     SqlDbType.Int)           { Value = dto.FGradeNo },
                new SqlParameter("@PartNo",       SqlDbType.Int)           { Value = dto.PartNo },
                new SqlParameter("@GradeNo",      SqlDbType.Int)           { Value = dto.GradeNo },
                new SqlParameter("@Qty",          SqlDbType.Decimal)       { Precision = 12, Scale = 4, Value = dto.Qty },
                new SqlParameter("@Remarks",      SqlDbType.VarChar,  250) { Value = PmDbValue(dto.Remarks) },
                new SqlParameter("@LockStatus",   SqlDbType.Char,       1) { Value = PmDbValue(dto.LockStatus) },
                new SqlParameter("@UserNo",       SqlDbType.Int)           { Value = dto.UserNo },
                new SqlParameter("@SystemIP",     SqlDbType.VarChar,   20) { Value = PmDbValue(systemIP) },
                new SqlParameter("@DeleteReason", SqlDbType.VarChar,  250) { Value = PmDbValue(reason) },
                new SqlParameter("@SubscID",      SqlDbType.Int)           { Value = dto.SubscID },
                new SqlParameter("@Status",       SqlDbType.VarChar,   20) { Value = status },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_ConvBOM
                    @ID = @ID OUTPUT, @MTransNo = @MTransNo, @FPartNo = @FPartNo, @FGradeNo = @FGradeNo,
                    @PartNo = @PartNo, @GradeNo = @GradeNo, @Qty = @Qty, @Remarks = @Remarks,
                    @LockStatus = @LockStatus, @UserNo = @UserNo, @SystemIP = @SystemIP,
                    @DeleteReason = @DeleteReason, @SubscID = @SubscID, @Status = @Status,
                    @Message = @Message OUTPUT
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RoutingChartSteps — replace a route's whole step list (JSON) ──
        public async Task<(int ID, string Message)> CallRoutingChartStepsSP(
            int mTransNo, string stepsJson, int userNo, int subscID)
        {
            var id  = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@MTransNo", SqlDbType.Int)          { Value = mTransNo },
                new SqlParameter("@Steps",    SqlDbType.NVarChar, -1) { Value = stepsJson },
                new SqlParameter("@UserNo",   SqlDbType.Int)          { Value = userNo },
                new SqlParameter("@SubscID",  SqlDbType.Int)          { Value = subscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_RoutingChartSteps
                    @ID = @ID OUTPUT, @MTransNo = @MTransNo, @Steps = @Steps,
                    @UserNo = @UserNo, @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
    }
}
