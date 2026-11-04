using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Job Card Report — moved out of ProductionReportsController so it lives
    /// in its own file and its route api/JobCardReport/AllReports is owned
    /// exclusively by this class.
    ///
    /// MIGRATION STEPS (one-time):
    ///   1. Delete the following block from ProductionReportsController.cs:
    ///        private static readonly string[] JobCardTypes = { ... };
    ///        [HttpGet("api/JobCardReport/AllReports")]
    ///        public Task[IActionResult] JobCardReport(...) => RunReport(...)
    ///   2. OARef and JobCardCte constants are duplicated here — if they are
    ///      still needed by other methods that remain in ProductionReportsController
    ///      keep them there; otherwise remove them from that file too.
    ///   3. Drop this file into Controllers/ and build.
    ///
    /// ETypes accepted (query-string EType param):
    ///   All              — every live job card in the date range (default)
    ///   Open             — job cards not yet started
    ///   In Progress      — job cards partially processed
    ///   Pending          — Open + In Progress with balance qty > 0
    ///   Completed        — job cards fully processed
    ///   Operation Wise   — same rows as All (kept as an alias)
    ///   Every EType returns one row per job card x route step. Inspection columns are filled
    ///   only on the inspection step (VW_JobCardStep.IsInspection = 1), one row per live
    ///   inspection of that job card; all other steps leave them blank.
    ///
    /// Query-string filters:
    ///   EType      — one of the values above (default: All)
    ///   FromDate   — inclusive start of JobCardMst.JobDate (required)
    ///   ToDate     — inclusive end (required)
    ///   SubscID    — subscription ID (required)
    ///   PartNo     — PartMaster.MTransNo of the FG part; 0 = every part
    ///   JobCardNo  — free-text contains search on JobCardMst.JobCardNo; blank = every JC
    /// </summary>
    [Route("api/[controller]")]
    public class JobCardReportController : EmsControllerBase
    {
        private readonly EMSContext db;
        public JobCardReportController(EMSContext context) => db = context;

        // ── Filter class ─────────────────────────────────────────────
        public class JobCardFilter
        {
            public string?   EType     { get; set; }
            public DateTime? FromDate  { get; set; }
            public DateTime? ToDate    { get; set; }
            public int       SubscID   { get; set; }
            /// <summary>PartMaster.MTransNo of the finished part; 0 = every part.</summary>
            public int?      PartNo    { get; set; }
            /// <summary>Free-text search against JobCardMst.JobCardNo; blank = every JC.</summary>
            public string?   JobCardNo { get; set; }
        }

        private static readonly string[] AllowedTypes =
            { "All", "Open", "In Progress", "Pending", "Completed", "Operation Wise" };

        // ── OA reference label (same format used across all production pages) ──
        private const string OARef = @"
            CASE WHEN ISNULL(j.OANo, 0) > 0
                 THEN CAST(j.OAYear AS varchar(10)) + '-' + CAST(j.OANo AS varchar(20))
                      + ' / ' + CAST(j.OAID AS varchar(10))
                 ELSE '' END";

        // ── Base CTE — one row per live job card in the period ────────
        // Joins VW_JobCardSummary for all roll-up columns so we never
        // re-derive progress, inspection or RM issued qty here.
        private const string JobCardCte = @"
                jc AS (
                    SELECT j.MTransNo, j.JobCardNo, j.JobDate, j.DocYear, j.DocNo,
                           j.OAYear, j.OANo, j.OAID, j.PartNo, j.Grade, j.Qty,
                           j.RMGrade, j.RMQty, j.RMUnit, j.Operator, j.Shift, j.Remarks,
                           " + OARef + @"                      AS OARef,
                           ISNULL(oa.VendorName, '')            AS Customer,
                           ISNULL(p.PartCode,  '')              AS PartCode,
                           ISNULL(p.PartName,  '')              AS PartName,
                           ISNULL(p.PrimaryUnit, '')            AS Unit,
                           ISNULL(r.RouteName, '')              AS RouteName,
                           ISNULL(rm.PartCode, '')              AS RMCode,
                           ISNULL(rm.PartName, '')              AS RMName,
                           s.StepCount, s.EntryCount, s.LastEntryDate,
                           s.CompletedQty, s.BalanceQty, s.JobStatus, s.CurrentStep,
                           s.InspectionStep, s.InspectionEligibleQty, s.InspectedQty,
                           s.OKQty, s.ReworkQty, s.RejectedQty, s.InspectionCount,
                           s.LastInspDate, s.AwaitingInspectionQty,
                           s.RMIssuedQty, s.RMBalanceQty
                    FROM dbo.JobCardMst j
                    JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
                    LEFT JOIN dbo.PartMaster p      ON p.MTransNo  = j.PartNo
                    LEFT JOIN dbo.PartMaster rm     ON rm.MTransNo = j.RMPartNo
                    LEFT JOIN dbo.RoutingChartMst r ON r.MTransNo  = j.RouteNo
                    OUTER APPLY (
                        SELECT TOP (1) o.VendorName
                        FROM dbo.OAMst o
                        WHERE o.SubscID = j.SubscID AND o.DocYear = j.OAYear AND o.DocNo = j.OANo
                          AND o.DeleteStatus = 'N' AND o.DocAmdStatus = 'N'
                        ORDER BY o.MTransNo DESC
                    ) oa
                    WHERE j.SubscID      = @SubscID
                      AND j.DeleteStatus = 'N'
                      AND j.JobDate BETWEEN @FromDate AND @ToDate
                      AND (@PartNo    = 0  OR j.PartNo    = @PartNo)
                      AND (@JobCardNo = '' OR j.JobCardNo LIKE '%' + @JobCardNo + '%')
                )";

        // ── SQL per EType ─────────────────────────────────────────────

        // One row per job card x route step for EVERY EType.
        // The page grid has step columns (stepNo, operation, machine, processedQty ...),
        // so All / Open / In Progress / Pending / Completed all use this shape and
        // differ only by the status filter at the bottom.
        // Inspection columns come from the latest JobCardInspection record per job card.
        private static string StepSQL => @"
                WITH " + JobCardCte + @"
                SELECT
                    CAST(c.MTransNo AS varchar(12))
                        + '-' + CAST(v.SlNo AS varchar(12))
                        + '-' + CAST(ISNULL(i.MTransNo, 0) AS varchar(12)) AS rowId,
                    c.MTransNo                                AS mTransNo,
                    c.JobCardNo                               AS jobCardNo,
                    CONVERT(char(10), c.JobDate, 23)          AS jobDate,
                    c.OARef                                   AS oaNo,
                    c.Customer                                AS customer,
                    c.PartCode                                AS partCode,
                    c.PartName                                AS partName,
                    ISNULL(c.Grade, '')                       AS grade,
                    c.RouteName                               AS routeName,
                    c.Qty                                     AS jobQty,
                    c.Unit                                    AS unit,
                    LTRIM(RTRIM(ISNULL(c.RMCode,'')
                        + CASE WHEN ISNULL(c.RMName,'') = '' THEN '' ELSE ' - ' + c.RMName END
                        + CASE WHEN LTRIM(ISNULL(c.RMGrade,'')) = '' THEN ''
                               ELSE ' (' + RTRIM(c.RMGrade) + ')' END)) AS rmText,
                    v.SlNo                                    AS stepNo,
                    v.OperationName                           AS operation,
                    LTRIM(RTRIM(ISNULL(v.MachineCode,'')
                        + CASE WHEN ISNULL(v.MachineName,'') = '' THEN ''
                               ELSE ' - ' + v.MachineName END)) AS machine,
                    ISNULL(c.Operator, '')                    AS operator,
                    ISNULL(CONVERT(char(10), le.LastEntryDate, 23), '') AS stepDate,
                    v.ProcessedQty                            AS processedQty,
                    v.BalanceQty                              AS balanceQty,
                    ISNULL(i.InspectionNo, '')                AS inspNo,
                    ISNULL(CONVERT(char(10), i.InspDate, 23), '') AS inspDate,
                    ISNULL(i.InspectedQty, 0)                 AS inspectedQty,
                    ISNULL(i.OKQty,        0)                 AS okQty,
                    ISNULL(i.ReworkQty,    0)                 AS reworkQty,
                    ISNULL(i.RejectedQty,  0)                 AS rejectedQty,
                    c.JobStatus                               AS status,
                    c.JobDate AS sortDate, c.DocYear AS sortYear,
                    c.DocNo   AS sortNo,  v.SlNo    AS sortSl,
                    i.InspDate AS sortInspDate, ISNULL(i.MTransNo, 0) AS sortInspId
                FROM jc c
                JOIN dbo.VW_JobCardStep v    ON v.JobCardTransNo = c.MTransNo
                -- Inspection belongs to the inspection step only (VW_JobCardStep.IsInspection).
                -- A job card can be inspected lot by lot, so every live inspection gets its own row;
                -- steps with no inspection (or any other step) stay a single row with blank inspection columns.
                LEFT JOIN dbo.JobCardInspection i
                       ON i.JobCardTransNo = c.MTransNo AND i.DeleteStatus = 'N' AND v.IsInspection = 1
                OUTER APPLY (
                    SELECT MAX(x.EntryDate) AS LastEntryDate
                    FROM dbo.JobCardEntry x
                    WHERE x.MTransNo = c.MTransNo AND x.StepSlNo = v.SlNo
                      AND x.DeleteStatus = 'N'
                ) le
                WHERE @EType IN ('All', 'Operation Wise')
                   OR (@EType = 'Pending' AND c.JobStatus IN ('Open', 'In Progress') AND c.BalanceQty > 0)
                   OR c.JobStatus = @EType
                ORDER BY sortDate, sortYear, sortNo, sortSl, sortInspDate, sortInspId";

        // ── Endpoint ──────────────────────────────────────────────────

        // ── Filter lists ─────────────────────────────────────────────
        // The Product dropdown holds the finished parts that actually have job cards. The option
        // value is the part id because AllReports filters on PartNo (PartMaster.MTransNo).
        [HttpGet("FilterOptions/{subscID}")]
        public async Task<IActionResult> FilterOptions(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open) await con.OpenAsync();

                await using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                    SELECT DISTINCT j.PartNo                AS partNo,
                                    ISNULL(p.PartCode, '')  AS partCode,
                                    ISNULL(p.PartName, '')  AS partName
                    FROM dbo.JobCardMst j
                    JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo
                    WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N'
                    ORDER BY partCode, partName";
                cmd.Parameters.Add(new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });

                var products = new List<Dictionary<string, object?>>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    products.Add(new Dictionary<string, object?>
                    {
                        ["partNo"]   = reader.GetValue(0),
                        ["partCode"] = Convert.ToString(reader.GetValue(1))?.Trim(),
                        ["partName"] = Convert.ToString(reader.GetValue(2))?.Trim()
                    });
                }
                return OkResult(new { products });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports([FromQuery] JobCardFilter f)
        {
            // ── Validate ───────────────────────────────────────────────
            if (f.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (f.FromDate == null || f.ToDate == null)
                return BadRequestResult("From Date and To Date are required.");
            if (f.FromDate.Value.Date > f.ToDate.Value.Date)
                return BadRequestResult("From Date cannot be after To Date.");

            var wanted = (f.EType ?? "All").Trim();
            var type   = AllowedTypes.FirstOrDefault(t =>
                string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return BadRequestResult(
                    $"EType must be one of: {string.Join(", ", AllowedTypes)}.");

            // ── Choose SQL ─────────────────────────────────────────────
            var sql = StepSQL;

            // ── Execute ────────────────────────────────────────────────
            try
            {
                var con = db.Database.GetDbConnection();
                if (con.State != ConnectionState.Open) await con.OpenAsync();

                await using var cmd = con.CreateCommand();
                cmd.CommandText = sql;
                cmd.Parameters.AddRange(new SqlParameter[]
                {
                    new("@SubscID",   SqlDbType.Int)          { Value = f.SubscID },
                    new("@FromDate",  SqlDbType.Date)         { Value = f.FromDate!.Value.Date },
                    new("@ToDate",    SqlDbType.Date)         { Value = f.ToDate!.Value.Date },
                    new("@EType",     SqlDbType.VarChar,  30) { Value = type },
                    new("@PartNo",    SqlDbType.Int)          { Value = f.PartNo ?? 0 },
                    new("@JobCardNo", SqlDbType.VarChar, 100) { Value = (f.JobCardNo ?? "").Trim() },
                });

                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        // Strip sort-only columns (prefix "sort")
                        var colName = reader.GetName(i);
                        if (colName.StartsWith("sort", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[colName] = value is string s ? s.Trim() : value;
                    }
                    rows.Add(row);
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
