using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Shared plumbing of the maintenance controllers (PMChecklist,
    /// PMCheckSheet, Breakdown, MaintenanceStaff, MaintenanceReport) —
    /// SQL/55_Maintenance.sql. Reads are inline SQL / MaintenanceData; writes
    /// go through the SP_* procedures.
    /// </summary>
    public abstract class MaintenanceControllerBase : EmsControllerBase
    {
        protected readonly EMSContext db;

        protected MaintenanceControllerBase(EMSContext context) => db = context;

        protected static readonly JsonSerializerOptions CamelJson = new(JsonSerializerDefaults.Web);

        protected static string ToJson(object value) => JsonSerializer.Serialize(value, CamelJson);

        /// <summary>
        /// The answer to a save / delete: data "OK^&lt;id&gt;" on success,
        /// data "duplicate fields" when the SP says so (the pages show their
        /// own "already exists" alert), otherwise 409 with the SP's message.
        /// </summary>
        protected IActionResult SaveReply((int ID, string Message) result)
        {
            if (result.ID > 0) return OkResult("OK^" + result.ID, result.Message);
            if (result.Message == "duplicate fields") return OkResult("duplicate fields", "duplicate fields");
            return ConflictResult(string.IsNullOrWhiteSpace(result.Message) ? "The record could not be saved." : result.Message);
        }

        /// <summary>First column of the first row as an int, or 0.</summary>
        protected async Task<int> ScalarIntAsync(string sql, params SqlParameter[] parameters)
        {
            var con = db.Database.GetDbConnection();
            if (con.State != ConnectionState.Open) await con.OpenAsync();
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters);
            var v = await cmd.ExecuteScalarAsync();
            return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
        }

        /// <summary>The subscription a row of <paramref name="table"/> belongs to (0 = no such live row).</summary>
        protected Task<int> SubscOfAsync(string table, int mTransNo)
        {
            // table is one of this module's constant names, never user input.
            return ScalarIntAsync(
                $"SELECT SubscID FROM dbo.{table} WHERE MTransNo = @Id AND ISNULL(DeleteStatus, 'N') = 'N'",
                new SqlParameter("@Id", SqlDbType.Int) { Value = mTransNo });
        }

        /// <summary>A JSON value (string or number) as an int, or 0.</summary>
        protected static int JsonInt(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Number => e.TryGetInt32(out var n) ? n : 0,
            JsonValueKind.String => int.TryParse(e.GetString(), out var n) ? n : 0,
            _ => 0
        };

        /// <summary>A JSON value as trimmed text ("" for null).</summary>
        protected static string JsonText(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => (e.GetString() ?? "").Trim(),
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => e.ToString().Trim()
        };

        /// <summary>
        /// Runs one read-only query and returns its rows as column-name → value
        /// maps; strings are trimmed.
        /// </summary>
        protected async Task<IActionResult> RunQuery(string sql, params SqlParameter[] parameters)
        {
            try
            {
                var con = db.Database.GetDbConnection();
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

                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
