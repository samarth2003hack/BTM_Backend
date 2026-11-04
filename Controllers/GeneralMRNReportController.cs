using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// General MRN Report — read-only register behind
    /// EMSSoft/Modules/ProductionReports/GeneralMRNReport.html.
    ///
    /// One row per live General MRN (MRNType = 'GEN') detail line whose
    /// DocDate falls in the period. The issued qty is summed across every
    /// live Store Issue line that draws on the MRN line
    /// (StoreIssueDtl.LinkNo = MRNDtl.TransSlNo,
    ///  StoreIssueMst.TargetTransNo = MRNMst.MTransNo,
    ///  TargetType is not 'Job Card' — SQL 56 guard).
    ///
    /// Filters (query string):
    ///   EType       — Issue Pending / Part Issued / Issued / All (default All)
    ///   Department  — exact match on MRNMst.Department; blank = every dept
    ///   ItemType    — exact match on MRNDtl part type filter (PartMaster.PartType);
    ///                 blank = every type
    ///   MRNNo       — free-text contains match on DocNo; blank = every MRN
    ///   FromDate    — inclusive start of MRNMst.DocDate range (required)
    ///   ToDate      — inclusive end (required)
    ///   SubscID     — subscription ID (required)
    ///
    /// Response column keys match the frontend REPORT_COLUMNS exactly:
    ///   mrnNo, mrnDate, requiredBy, department, oaNo,
    ///   itemCode, itemName, itemType, qty, unit,
    ///   issueNo, issueDate, issuedQty, balQty, status
    /// </summary>
    [Route("api/[controller]")]
    public class GeneralMRNReportController : EmsControllerBase
    {
        private readonly EMSContext db;
        public GeneralMRNReportController(EMSContext context) => db = context;

        public class GeneralMRNFilter
        {
            public string? EType      { get; set; }
            public string? Department { get; set; }
            public string? ItemType   { get; set; }
            public string? MRNNo      { get; set; }
            public DateTime? FromDate { get; set; }
            public DateTime? ToDate   { get; set; }
            public int SubscID        { get; set; }
        }

        private static readonly string[] AllowedTypes =
            { "All", "Issue Pending", "Part Issued", "Issued" };

        // ── Filter lists ─────────────────────────────────────────────
        // The Department and Item Type dropdowns hold the distinct values that exist in the
        // General MRN data itself (there is no department master in the project), so every
        // option is one the report can return, whatever date range is picked.
        [HttpGet("FilterOptions/{subscID}")]
        public async Task<IActionResult> FilterOptions(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open) await con.OpenAsync();

                var departments = await ReadColumn(con, @"
                    SELECT DISTINCT LTRIM(RTRIM(m.Department)) AS department
                    FROM dbo.MRNMst m
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.MRNType = 'GEN'
                      AND ISNULL(LTRIM(RTRIM(m.Department)), '') <> ''
                    ORDER BY department", subscID);

                var itemTypes = await ReadColumn(con, @"
                    SELECT DISTINCT p.PartType AS itemType
                    FROM dbo.MRNMst m
                    JOIN dbo.MRNDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.MRNType = 'GEN'
                      AND ISNULL(p.PartType, '') <> ''
                    ORDER BY itemType", subscID);

                return OkResult(new { departments, itemTypes });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // First column of a single-parameter (@SubscID) query as trimmed, non-empty strings.
        private static async Task<List<string>> ReadColumn(DbConnection con, string sql, int subscID)
        {
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(0)) continue;
                var s = Convert.ToString(reader.GetValue(0))?.Trim();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports([FromQuery] GeneralMRNFilter f)
        {
            if (f.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (f.FromDate == null || f.ToDate == null)
                return BadRequestResult("From Date and To Date are required.");
            if (f.FromDate.Value.Date > f.ToDate.Value.Date)
                return BadRequestResult("From Date cannot be after To Date.");

            var wanted = (f.EType ?? "All").Trim();
            var type = AllowedTypes.FirstOrDefault(t =>
                string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return BadRequestResult(
                    $"Type must be one of: {string.Join(", ", AllowedTypes)}.");

            // The issue columns are the LATEST Store Issue against this MRN line.
            // A line can be issued more than once (partial issues); we surface
            // the last issue document and the running total issued qty.
            const string sql = @"
                WITH issued AS (
                    SELECT sd.LinkNo,
                           sm.TargetTransNo                            AS MRNTransNo,
                           SUM(sd.Qty)                                 AS TotalIssuedQty,
                           MAX(sm.MTransNo)                            AS LastIssueTransNo
                    FROM dbo.StoreIssueDtl sd
                    JOIN dbo.StoreIssueMst sm ON sm.MTransNo = sd.MTransNo
                    WHERE sd.DeleteStatus  = 'N'
                      AND sm.DeleteStatus  = 'N'
                      AND ISNULL(sm.TargetType, '') <> 'Job Card'
                    GROUP BY sd.LinkNo, sm.TargetTransNo
                ),
                lastIssue AS (
                    SELECT sd.LinkNo,
                           sm.TargetTransNo                            AS MRNTransNo,
                           CAST(sm.DocNo AS varchar(20))               AS IssueNo,
                           CONVERT(char(10), sm.DocDate, 23)           AS IssueDate
                    FROM dbo.StoreIssueDtl sd
                    JOIN dbo.StoreIssueMst sm ON sm.MTransNo = sd.MTransNo
                    JOIN issued i ON i.LastIssueTransNo = sm.MTransNo
                                 AND i.LinkNo = sd.LinkNo
                    WHERE sd.DeleteStatus  = 'N'
                      AND sm.DeleteStatus  = 'N'
                    -- one row per MRN line even if the last issue document carries the line twice
                    GROUP BY sd.LinkNo, sm.TargetTransNo, sm.DocNo, sm.DocDate
                ),
                lines AS (
                    SELECT
                        m.MTransNo                                     AS MRNTransNo,
                        CAST(m.DocNo AS varchar(20))                   AS MRNNo,
                        CONVERT(char(10), m.DocDate, 23)               AS MRNDate,
                        ISNULL(m.Requiredby, '')                       AS RequiredBy,
                        ISNULL(m.Department, '')                       AS Department,
                        CASE WHEN ISNULL(d.OANo, 0) > 0
                             THEN CAST(d.OAYear AS varchar(10)) + '-'
                                  + CAST(d.OANo AS varchar(20))
                                  + CASE WHEN ISNULL(d.OAID, 0) > 0
                                         THEN ' / ' + CAST(d.OAID AS varchar(10))
                                         ELSE '' END
                             ELSE '' END                               AS OANo,
                        ISNULL(p.PartCode, '')                         AS ItemCode,
                        COALESCE(NULLIF(LTRIM(RTRIM(d.Description)), ''),
                                 p.PartName, '')                       AS ItemName,
                        ISNULL(p.PartType, '')                         AS ItemType,
                        ISNULL(d.Qty, 0)                               AS Qty,
                        COALESCE(NULLIF(LTRIM(RTRIM(d.Unit)), ''),
                                 p.PrimaryUnit, '')                    AS Unit,
                        ISNULL(i.TotalIssuedQty, 0)                    AS IssuedQty,
                        CASE WHEN ISNULL(d.Qty, 0) - ISNULL(i.TotalIssuedQty, 0) > 0
                             THEN ISNULL(d.Qty, 0) - ISNULL(i.TotalIssuedQty, 0)
                             ELSE 0 END                                AS BalQty,
                        ISNULL(li.IssueNo, '')                         AS IssueNo,
                        ISNULL(li.IssueDate, '')                       AS IssueDate,
                        CASE WHEN m.LockStatus <> 'Y' THEN 'Not Approved'
                             WHEN ISNULL(i.TotalIssuedQty, 0) <= 0 THEN 'Issue Pending'
                             WHEN ISNULL(d.Qty, 0) - ISNULL(i.TotalIssuedQty, 0) > 0 THEN 'Part Issued'
                             ELSE 'Issued' END                         AS Status,
                        m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortSl
                    FROM dbo.MRNMst m
                    JOIN dbo.MRNDtl d  ON d.MTransNo  = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    LEFT JOIN issued   i  ON i.LinkNo = d.TransSlNo AND i.MRNTransNo = m.MTransNo
                    LEFT JOIN lastIssue li ON li.LinkNo = d.TransSlNo AND li.MRNTransNo = m.MTransNo
                    WHERE m.SubscID      = @SubscID
                      AND m.MRNType     = 'GEN'
                      AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Department = '' OR m.Department = @Department)
                      AND (@ItemType   = '' OR p.PartType  = @ItemType)
                      AND (@MRNNo      = '' OR CAST(m.DocNo AS varchar(20)) LIKE '%' + @MRNNo + '%')
                )
                SELECT
                    mrnNo       = l.MRNNo,
                    mrnDate     = l.MRNDate,
                    requiredBy  = l.RequiredBy,
                    department  = l.Department,
                    oaNo        = l.OANo,
                    itemCode    = l.ItemCode,
                    itemName    = l.ItemName,
                    itemType    = l.ItemType,
                    qty         = l.Qty,
                    unit        = l.Unit,
                    issueNo     = l.IssueNo,
                    issueDate   = l.IssueDate,
                    issuedQty   = l.IssuedQty,
                    balQty      = l.BalQty,
                    status      = l.Status
                FROM lines l
                WHERE @EType = 'All'
                   OR (@EType = 'Issue Pending' AND l.Status = 'Issue Pending')
                   OR (@EType = 'Part Issued'   AND l.Status = 'Part Issued')
                   OR (@EType = 'Issued'        AND l.Status = 'Issued')
                ORDER BY l.sortDate, l.sortNo, l.sortSl";

            try
            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open) await con.OpenAsync();

                await using var cmd = con.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@SubscID",    SqlDbType.Int)           { Value = f.SubscID },
                    new SqlParameter("@FromDate",   SqlDbType.Date)          { Value = f.FromDate!.Value.Date },
                    new SqlParameter("@ToDate",     SqlDbType.Date)          { Value = f.ToDate!.Value.Date },
                    new SqlParameter("@EType",      SqlDbType.VarChar,  30)  { Value = type },
                    new SqlParameter("@Department", SqlDbType.VarChar, 100)  { Value = (f.Department ?? "").Trim() },
                    new SqlParameter("@ItemType",   SqlDbType.VarChar,  50)  { Value = (f.ItemType   ?? "").Trim() },
                    new SqlParameter("@MRNNo",      SqlDbType.VarChar,  50)  { Value = (f.MRNNo      ?? "").Trim() },
                });

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
