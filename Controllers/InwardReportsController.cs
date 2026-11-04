using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Gate pass reports behind EMSSoft/Modules/PurchaseReports:
    /// DC Report, RGP Report and RGP Inward Report.
    ///
    /// Each answers GET api/{Report}/AllReports with the legacy filter names
    /// (PValue = DocSeries, EType = report type, Customer = supplier NAME,
    /// FromDate, ToDate, SubscID) and returns one row per item line whose
    /// camelCase keys are exactly the grid column fields of the matching page.
    ///
    /// Shared rules:
    ///   - RGPMst / RGPDtl hold both documents: RGPType 'DC' (non-returnable)
    ///     and 'RGP' (returnable). Each report reads only its own type.
    ///   - RGPInwMst / RGPInwDtl hold material received back against an RGP:
    ///     RGPInwMst.TargetTransNo = RGPMst.MTransNo, RGPInwDtl.LinkNo = RGPDtl.TransSlNo.
    ///   - "All" = approved (LockStatus 'Y') live documents, as in the legacy
    ///     reports; "Cancelled" = soft-deleted (DeleteStatus 'Y').
    ///   - Series blank = all series; supplier blank = all suppliers.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    public class InwardReportsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public InwardReportsController(EMSContext context) => db = context;

        /// <summary>Query-string filters shared by every gate pass report.</summary>
        public class InwardReportFilter
        {
            public string? PValue { get; set; }
            public string? EType { get; set; }
            public string? Customer { get; set; }
            public DateTime? FromDate { get; set; }
            public DateTime? ToDate { get; set; }
            public int SubscID { get; set; }
        }

        // Header filter every report applies to its master table, aliased "m".
        private const string CommonWhere = @"
            m.SubscID = @SubscID
            AND m.DocDate BETWEEN @FromDate AND @ToDate
            AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
            AND (@Customer = '' OR m.VendorName = @Customer)";

        // Item description as the legacy reports print it: "Description-Grade".
        private const string ItemDesc = @"
            ISNULL(d.PartDescription, '')
              + CASE WHEN LTRIM(ISNULL(d.Grade, '')) = '' THEN '' ELSE '-' + RTRIM(d.Grade) END";

        // ============================================================
        // DC REPORT — RGPMst (RGPType 'DC') + RGPDtl
        // ============================================================

        [HttpGet("api/DCReport/AllReports")]
        public Task<IActionResult> DCReport([FromQuery] InwardReportFilter f) =>
            RunReport(f, new[] { "All", "Cancelled" }, @"
                SELECT
                    m.MTransNo                                AS mTransNo,
                    CAST(m.MTransNo AS varchar(12)) + '-' + CAST(d.TransSlNo AS varchar(12)) AS rowId,
                    RTRIM(ISNULL(m.DocSeries, ''))            AS docSeries,
                    CAST(m.DocNo AS varchar(20))              AS dcNo,
                    CONVERT(char(10), m.DocDate, 23)          AS dcDate,
                    ISNULL(m.VendorName, '')                  AS supplier,
                    ISNULL(m.RefNo, '')                       AS refNo,
                    CONVERT(char(10), m.RefDate, 23)          AS refDate,
                    ISNULL(d.PartCode, '')                    AS partCode,
                    " + ItemDesc + @"                         AS itemDesc,
                    ISNULL(d.Qty, 0)                          AS qty,
                    ISNULL(d.Unit, '')                        AS unit,
                    ISNULL(d.Rate, 0)                         AS rate,
                    ISNULL(d.Amount, 0)                       AS amount,
                    ISNULL(d.ItemRemarks, '')                 AS remarks,
                    CASE WHEN m.DeleteStatus = 'Y' THEN 'Cancelled' ELSE 'Approved' END AS type,
                    m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortSl
                FROM dbo.RGPMst m
                JOIN dbo.RGPDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                WHERE " + CommonWhere + @"
                  AND m.RGPType = 'DC'
                  AND ((@EType = 'All'       AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y')
                    OR (@EType = 'Cancelled' AND m.DeleteStatus = 'Y'))
                ORDER BY sortDate, sortNo, sortSl");

        // ============================================================
        // RGP REPORT — RGPMst (RGPType 'RGP') + RGPDtl, with what came back
        // ============================================================

        [HttpGet("api/RGPReport/AllReports")]
        public Task<IActionResult> RGPReport([FromQuery] InwardReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                WITH ret AS (
                    -- Qty received back per gate pass line: live inward line on a live inward.
                    SELECT i.LinkNo, SUM(ISNULL(i.Qty, 0)) AS RetQty, MAX(im.DocDate) AS LastDate
                    FROM dbo.RGPInwDtl i
                    JOIN dbo.RGPInwMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                    WHERE i.DeleteStatus = 'N' AND i.LinkNo IS NOT NULL AND i.SubscID = @SubscID
                    GROUP BY i.LinkNo
                ),
                lines AS (
                    SELECT m.MTransNo, m.DocSeries, m.DocNo, m.DocDate, m.DueDate, m.VendorName,
                           m.RefNo, m.RefDate, m.DeleteStatus, m.DocStatus,
                           d.TransSlNo, d.SlNo, d.PartCode, d.PartDescription, d.Grade, d.Unit, d.Rate, d.Amount,
                           d.ItemRemarks,
                           ISNULL(d.Qty, 0)                        AS SentQty,
                           ISNULL(r.RetQty, 0)                     AS RetQty,
                           ISNULL(d.Qty, 0) - ISNULL(r.RetQty, 0)  AS BalQty,
                           r.LastDate
                    FROM dbo.RGPMst m
                    JOIN dbo.RGPDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN ret r ON r.LinkNo = d.TransSlNo
                    WHERE " + CommonWhere + @"
                      AND m.RGPType = 'RGP'
                      AND ((m.DeleteStatus = 'N' AND m.LockStatus = 'Y') OR m.DeleteStatus = 'Y')
                ),
                hdr AS (
                    -- Document status: Open while any line still has material out.
                    SELECT MTransNo,
                        CASE WHEN MAX(DeleteStatus) = 'Y' THEN 'Cancelled'
                             WHEN MAX(CASE WHEN BalQty > 0 THEN 1 ELSE 0 END) = 1 THEN 'Open'
                             ELSE 'Closed' END AS RptStatus
                    FROM lines
                    GROUP BY MTransNo
                )
                SELECT
                    l.MTransNo                                AS mTransNo,
                    CAST(l.MTransNo AS varchar(12)) + '-' + CAST(l.TransSlNo AS varchar(12)) AS rowId,
                    RTRIM(ISNULL(l.DocSeries, ''))            AS docSeries,
                    CAST(l.DocNo AS varchar(20))              AS rgpNo,
                    CONVERT(char(10), l.DocDate, 23)          AS rgpDate,
                    ISNULL(l.VendorName, '')                  AS supplier,
                    ISNULL(l.RefNo, '')                       AS refNo,
                    CONVERT(char(10), l.RefDate, 23)          AS refDate,
                    ISNULL(l.PartCode, '')                    AS partCode,
                    ISNULL(l.PartDescription, '')
                      + CASE WHEN LTRIM(ISNULL(l.Grade, '')) = '' THEN '' ELSE '-' + RTRIM(l.Grade) END AS itemDesc,
                    ISNULL(l.Unit, '')                        AS unit,
                    l.SentQty                                 AS sentQty,
                    l.RetQty                                  AS returnedQty,
                    CASE WHEN l.BalQty > 0 THEN l.BalQty ELSE 0 END AS balanceQty,
                    ISNULL(l.Rate, 0)                         AS rate,
                    ISNULL(l.Amount, 0)                       AS amount,
                    CONVERT(char(10), l.DueDate, 23)          AS expectedBack,
                    CONVERT(char(10), l.LastDate, 23)         AS receivedBack,
                    ISNULL(l.ItemRemarks, '')                 AS remarks,
                    h.RptStatus                               AS type,
                    l.DocDate AS sortDate, l.DocNo AS sortNo, l.SlNo AS sortSl
                FROM lines l
                JOIN hdr h ON h.MTransNo = l.MTransNo
                WHERE (@EType = 'All' AND h.RptStatus <> 'Cancelled')
                   OR h.RptStatus = @EType
                ORDER BY sortDate, sortNo, sortSl");

        // ============================================================
        // RGP INWARD REPORT — RGPInwMst + RGPInwDtl, with the gate pass it closes
        // ============================================================

        [HttpGet("api/RGPInwardReport/AllReports")]
        public Task<IActionResult> RGPInwardReport([FromQuery] InwardReportFilter f) =>
            RunReport(f, new[] { "All", "Cancelled" }, @"
                SELECT
                    m.MTransNo                                AS mTransNo,
                    CAST(m.MTransNo AS varchar(12)) + '-' + CAST(d.TransSlNo AS varchar(12)) AS rowId,
                    RTRIM(ISNULL(m.DocSeries, ''))            AS docSeries,
                    CAST(m.DocNo AS varchar(20))              AS inwNo,
                    CONVERT(char(10), m.DocDate, 23)          AS inwDate,
                    ISNULL(m.VendorName, '')                  AS supplier,
                    ISNULL(CAST(g.DocNo AS varchar(20)), CAST(m.TargetDocNo AS varchar(20))) AS rgpNo,
                    CONVERT(char(10), g.DocDate, 23)          AS rgpDate,
                    ISNULL(m.DCNo, '')                        AS dcNo,
                    CONVERT(char(10), m.DCDate, 23)           AS dcDate,
                    ISNULL(d.PartCode, '')                    AS partCode,
                    " + ItemDesc + @"                         AS itemDesc,
                    ISNULL(d.RGPQty, 0)                       AS rgpQty,
                    ISNULL(d.Qty, 0)                          AS qty,
                    ISNULL(d.Unit, '')                        AS unit,
                    CONVERT(char(10), g.DueDate, 23)          AS expectedBack,
                    ISNULL(d.ItemRemarks, '')                 AS remarks,
                    CASE WHEN m.DeleteStatus = 'Y' THEN 'Cancelled' ELSE 'Approved' END AS type,
                    m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortSl
                FROM dbo.RGPInwMst m
                JOIN dbo.RGPInwDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                LEFT JOIN dbo.RGPMst g ON g.MTransNo = m.TargetTransNo
                WHERE " + CommonWhere + @"
                  AND ((@EType = 'All'       AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y')
                    OR (@EType = 'Cancelled' AND m.DeleteStatus = 'Y'))
                ORDER BY sortDate, sortNo, sortSl");

        // ============================================================
        // SHARED RUNNER
        // ============================================================

        /// <summary>Validates the report filters, then runs the report query.</summary>
        private async Task<IActionResult> RunReport(InwardReportFilter f, string[] types, string sql)
        {
            if (f.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (f.FromDate == null || f.ToDate == null)
                return BadRequestResult("From Date and To Date are required.");
            if (f.FromDate.Value.Date > f.ToDate.Value.Date)
                return BadRequestResult("From Date cannot be after To Date.");

            var type = types.FirstOrDefault(t =>
                string.Equals(t, (f.EType ?? "All").Trim(), StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return BadRequestResult($"Type must be one of: {string.Join(", ", types)}.");

            return await RunQuery(sql,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = f.SubscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = f.FromDate.Value.Date },
                new SqlParameter("@ToDate", SqlDbType.Date) { Value = f.ToDate.Value.Date },
                new SqlParameter("@Series", SqlDbType.VarChar, 10) { Value = (f.PValue ?? "").Trim() },
                new SqlParameter("@Customer", SqlDbType.VarChar, 100) { Value = (f.Customer ?? "").Trim() },
                new SqlParameter("@EType", SqlDbType.VarChar, 20) { Value = type });
        }

        /// <summary>
        /// Runs one read-only query and returns its rows as column-name → value
        /// maps. Helper columns prefixed "sort" are dropped; strings are trimmed.
        /// </summary>
        private async Task<IActionResult> RunQuery(string sql, params SqlParameter[] parameters)
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
                        var name = reader.GetName(i);
                        if (name.StartsWith("sort", StringComparison.Ordinal)) continue;
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[name] = value is string s ? s.Trim() : value;
                    }
                    rows.Add(row);
                }

                return OkResult(rows);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
