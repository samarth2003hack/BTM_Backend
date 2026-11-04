using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Generic table browser / editor for support work — browse any table, run a
    /// SELECT, and patch rows without a SQL client.
    ///
    /// SECURITY — READ BEFORE EXPOSING THIS
    /// ------------------------------------
    /// These endpoints are unauthenticated and let the caller read and modify
    /// ANY table in the database. That is inherent to what the screen does; it
    /// must sit behind an admin-only authorization policy before the API is
    /// reachable off localhost. Deleting this controller is a reasonable choice
    /// if no one needs the screen.
    ///
    /// The LMSAPI original additionally concatenated caller-supplied values
    /// straight into the SQL text, so any caller could append their own
    /// statement. This version closes that hole:
    ///
    ///   * table and column names are resolved against sys.tables / sys.columns
    ///     and re-emitted through QUOTENAME, so only real identifiers reach the
    ///     query — anything else is rejected;
    ///   * every value is passed as a SqlParameter, never inlined;
    ///   * QueryExecute stays SELECT-only and now also rejects batch separators
    ///     and comment markers, which the original allowed through.
    /// </summary>
    [Route("api/[controller]")]
    public class TableUpdationController : EmsControllerBase
    {
        private readonly EMSContext db;
        private readonly IConfiguration config;

        public TableUpdationController(EMSContext context, IConfiguration configuration)
        {
            db = context;
            config = configuration;
        }

        private string ConnectionString =>
            config.GetConnectionString("EMSConnection")
            ?? throw new InvalidOperationException("Connection string 'EMSConnection' is not configured.");

        //======================================================================
        // Identifier validation
        //======================================================================

        /// <summary>
        /// Returns the real table name as SQL Server spells it, or null when no
        /// such table exists. Matching against the catalog is what makes it safe
        /// to interpolate the result into a query.
        /// </summary>
        private static async Task<string?> ResolveTableAsync(SqlConnection con, string? table)
        {
            if (string.IsNullOrWhiteSpace(table)) return null;

            var cmd = new SqlCommand(
                "SELECT name FROM sys.tables WHERE name = @t AND schema_id = SCHEMA_ID('dbo')", con);
            cmd.Parameters.AddWithValue("@t", table.Trim());

            return await cmd.ExecuteScalarAsync() as string;
        }

        /// <summary>Returns the real column name on the given table, or null.</summary>
        private static async Task<string?> ResolveColumnAsync(SqlConnection con, string table, string? column)
        {
            if (string.IsNullOrWhiteSpace(column)) return null;

            var cmd = new SqlCommand(
                @"SELECT c.name FROM sys.columns c
                  WHERE c.object_id = OBJECT_ID(@t) AND c.name = @c", con);
            cmd.Parameters.AddWithValue("@t", "dbo." + table);
            cmd.Parameters.AddWithValue("@c", column.Trim());

            return await cmd.ExecuteScalarAsync() as string;
        }

        private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

        /// <summary>Coerces a string value to the type the caller declared.</summary>
        private static object ToValue(string? raw, string? dataType)
        {
            if (raw == null) return DBNull.Value;

            return (dataType ?? "").Trim().ToLowerInvariant() switch
            {
                "number" => decimal.TryParse(raw, out var d) ? d
                            : throw new FormatException($"'{raw}' is not a number."),
                "date" => DateTime.TryParse(raw, out var dt) ? dt
                            : throw new FormatException($"'{raw}' is not a date."),
                _ => raw
            };
        }

        private static List<Dictionary<string, object?>> ReadAll(SqlDataReader reader)
        {
            var rows = new List<Dictionary<string, object?>>();

            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();

                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                rows.Add(row);
            }

            return rows;
        }

        //======================================================================
        // Catalogue
        //======================================================================

        [HttpGet("GetAllTables")]
        public async Task<IActionResult> GetAllTables()
        {
            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var cmd = new SqlCommand(
                    "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') ORDER BY name", con);

                await using var reader = await cmd.ExecuteReaderAsync();

                var tables = new List<string>();
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

                return OkResult(tables);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetFieldName")]
        public async Task<IActionResult> GetFieldName([FromQuery] string mTable = "")
        {
            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var table = await ResolveTableAsync(con, mTable);

                if (table == null)
                    return BadRequestResult($"Unknown table '{mTable}'.");

                var cmd = new SqlCommand(
                    @"SELECT c.name, t.name AS DataType, c.is_nullable, c.is_identity
                      FROM sys.columns c
                      JOIN sys.types t ON t.user_type_id = c.user_type_id
                      WHERE c.object_id = OBJECT_ID(@t)
                      ORDER BY c.column_id", con);
                cmd.Parameters.AddWithValue("@t", "dbo." + table);

                await using var reader = await cmd.ExecuteReaderAsync();

                var columns = ReadAll(reader);

                return OkResult(columns);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //======================================================================
        // Browse
        //======================================================================

        /// <summary>
        /// Rows from one table, optionally filtered on a single column.
        /// Capped at 1000 rows so a mistyped filter cannot pull a whole table.
        /// </summary>
        [HttpGet("DataDisplay")]
        public async Task<IActionResult> DataDisplay(
            [FromQuery] string qTableName = "",
            [FromQuery] string qField = "",
            [FromQuery] string qDataType = "",
            [FromQuery] string qValue = "",
            [FromQuery] string orderField = "",
            [FromQuery] int top = 1000)
        {
            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var table = await ResolveTableAsync(con, qTableName);

                if (table == null)
                    return BadRequestResult($"Unknown table '{qTableName}'.");

                if (top is < 1 or > 10000) top = 1000;

                var sql = $"SELECT TOP ({top}) * FROM dbo.{Quote(table)}";
                var parameters = new List<SqlParameter>();

                if (!string.IsNullOrWhiteSpace(qField) && !string.IsNullOrWhiteSpace(qValue))
                {
                    var field = await ResolveColumnAsync(con, table, qField);

                    if (field == null)
                        return BadRequestResult($"Unknown column '{qField}' on {table}.");

                    if (string.Equals(qDataType, "Text", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(qDataType))
                    {
                        sql += $" WHERE {Quote(field)} LIKE @v";
                        parameters.Add(new SqlParameter("@v", "%" + qValue + "%"));
                    }
                    else
                    {
                        sql += $" WHERE {Quote(field)} = @v";
                        parameters.Add(new SqlParameter("@v", ToValue(qValue, qDataType)));
                    }
                }

                if (!string.IsNullOrWhiteSpace(orderField))
                {
                    var order = await ResolveColumnAsync(con, table, orderField);

                    if (order == null)
                        return BadRequestResult($"Unknown column '{orderField}' on {table}.");

                    sql += $" ORDER BY {Quote(order)}";
                }

                var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 };
                cmd.Parameters.AddRange(parameters.ToArray());

                await using var reader = await cmd.ExecuteReaderAsync();

                return OkResult(ReadAll(reader));
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Ad-hoc read-only query.</summary>
        [HttpGet("QueryExecute")]
        public async Task<IActionResult> QueryExecute([FromQuery] string sqlQuery = "")
        {
            if (string.IsNullOrWhiteSpace(sqlQuery))
                return BadRequestResult("SQL query is required.");

            var trimmed = sqlQuery.TrimStart();

            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Only SELECT queries are allowed.");

            // "SELECT 1; DROP TABLE x" and "SELECT 1 -- ..." both start with
            // SELECT, so the prefix check alone is not enough.
            if (sqlQuery.Contains(';') || sqlQuery.Contains("--") || sqlQuery.Contains("/*"))
                return BadRequestResult("Statement separators and comments are not allowed.");

            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var cmd = new SqlCommand(sqlQuery, con)
                {
                    CommandType = CommandType.Text,
                    CommandTimeout = 60
                };

                await using var reader = await cmd.ExecuteReaderAsync();

                return OkResult(ReadAll(reader));
            }
            catch (SqlException ex)
            {
                return ErrorResult(ex);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //======================================================================
        // Edit
        //======================================================================

        /// <summary>
        /// Sets one column on the rows matching one condition. A WHERE clause is
        /// required — the LMSAPI variant allowed an unfiltered UPDATE, which
        /// rewrites the whole table on a single mis-click.
        /// </summary>
        [HttpPost("UpdateTable")]
        public async Task<IActionResult> UpdateTable(
            [FromQuery] string uTableName = "",
            [FromQuery] string uFieldName = "",
            [FromQuery] string uValueType = "",
            [FromQuery] string uValue = "",
            [FromQuery] string uWhereField = "",
            [FromQuery] string uWhereValueType = "",
            [FromQuery] string uWhereValue = "")
        {
            if (string.IsNullOrWhiteSpace(uWhereField))
                return BadRequestResult("A WHERE column is required — refusing to update every row.");

            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var table = await ResolveTableAsync(con, uTableName);
                if (table == null)
                    return BadRequestResult($"Unknown table '{uTableName}'.");

                var field = await ResolveColumnAsync(con, table, uFieldName);
                if (field == null)
                    return BadRequestResult($"Unknown column '{uFieldName}' on {table}.");

                var whereField = await ResolveColumnAsync(con, table, uWhereField);
                if (whereField == null)
                    return BadRequestResult($"Unknown column '{uWhereField}' on {table}.");

                var sql = $"UPDATE dbo.{Quote(table)} SET {Quote(field)} = @val WHERE {Quote(whereField)} = @where";

                var cmd = new SqlCommand(sql, con) { CommandTimeout = 120 };
                cmd.Parameters.AddWithValue("@val", ToValue(uValue, uValueType));
                cmd.Parameters.AddWithValue("@where", ToValue(uWhereValue, uWhereValueType));

                var affected = await cmd.ExecuteNonQueryAsync();

                return OkResult(new { RowsAffected = affected }, $"{affected} row(s) updated.");
            }
            catch (FormatException ex)
            {
                return BadRequestResult(ex.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Inserts one row. Column names are validated against the table, values
        /// are parameterised, and the standard audit columns are appended only
        /// when the target table actually has them.
        /// </summary>
        [HttpPost("InsertTableData")]
        public async Task<IActionResult> InsertTableData([FromBody] InsertRowRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TableName))
                return BadRequestResult("Table name is required.");

            if (request.Values == null || request.Values.Count == 0)
                return BadRequestResult("At least one column value is required.");

            if (request.UserNo <= 0)
                return BadRequestResult("Invalid user.");

            try
            {
                await using var con = new SqlConnection(ConnectionString);
                await con.OpenAsync();

                var table = await ResolveTableAsync(con, request.TableName);
                if (table == null)
                    return BadRequestResult($"Unknown table '{request.TableName}'.");

                var columns = new List<string>();
                var parameters = new List<SqlParameter>();
                var i = 0;

                foreach (var kv in request.Values)
                {
                    var column = await ResolveColumnAsync(con, table, kv.Key);

                    if (column == null)
                        return BadRequestResult($"Unknown column '{kv.Key}' on {table}.");

                    columns.Add(column);
                    parameters.Add(new SqlParameter($"@p{i}", kv.Value ?? (object)DBNull.Value));
                    i++;
                }

                // Fill in the audit columns the master tables share, but only
                // those that exist here and were not supplied by the caller.
                var defaults = new Dictionary<string, object>
                {
                    ["SystemIP"]     = ClientIp ?? (object)DBNull.Value,
                    ["LockStatus"]   = "Y",
                    ["LockCounter"]  = 1,
                    ["CreatedBy"]    = request.UserNo,
                    ["CreatedOn"]    = DateTime.Now,
                    ["DeleteStatus"] = "N",
                    ["SubscID"]      = request.SubscID
                };

                foreach (var kv in defaults)
                {
                    if (columns.Any(c => string.Equals(c, kv.Key, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var column = await ResolveColumnAsync(con, table, kv.Key);
                    if (column == null) continue;

                    columns.Add(column);
                    parameters.Add(new SqlParameter($"@p{i}", kv.Value));
                    i++;
                }

                var columnList = string.Join(", ", columns.Select(Quote));
                var valueList = string.Join(", ", parameters.Select(p => p.ParameterName));

                var cmd = new SqlCommand(
                    $"INSERT INTO dbo.{Quote(table)} ({columnList}) VALUES ({valueList})", con)
                {
                    CommandTimeout = 120
                };
                cmd.Parameters.AddRange(parameters.ToArray());

                var affected = await cmd.ExecuteNonQueryAsync();

                return OkResult(new { RowsAffected = affected }, "Row inserted.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }

    /// <summary>Payload for TableUpdation/InsertTableData.</summary>
    public class InsertRowRequest
    {
        public string TableName { get; set; } = string.Empty;

        /// <summary>Column name to value. Names are validated against the table.</summary>
        public Dictionary<string, string?> Values { get; set; } = new();

        public int UserNo { get; set; }
        public int SubscID { get; set; } = 1;
    }
}
