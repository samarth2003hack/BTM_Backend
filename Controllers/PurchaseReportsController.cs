using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Purchase Reports — the read-only registers behind EMSSoft/Modules/PurchaseReports
    /// (Purchase Order, GRN, RFQ, RFQ Approval).
    ///
    /// Every report answers GET api/{Report}/AllReports with the legacy filter names
    /// (PValue = DocSeries, EType = report type, Customer = supplier NAME, FromDate,
    /// ToDate, SubscID) and returns a flat row list whose camelCase keys are exactly
    /// the grid column fields of the matching page. Blank PValue = every series.
    ///
    /// Shared rules:
    ///   - Cancelled = soft-deleted (DeleteStatus 'Y'); the delete SPs only flag the
    ///     master, so detail lines of a cancelled document stay DeleteStatus 'N'.
    ///   - PO / RFQ / supplier quotation read only the current revision (DocAmdStatus 'N').
    ///   - PO and GRN registers (other than Cancelled) show approved documents only
    ///     (LockStatus 'Y'), as the legacy reports did.
    ///   - GRN line -> PO line: GRNMst.TargetTransNo = POMst.MTransNo (TargetType 'PO')
    ///     and GRNDtl.SlNo = PODtl.SlNo — the same link VW_POPendingGRN uses.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    public class PurchaseReportsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PurchaseReportsController(EMSContext context) => db = context;

        /// <summary>Query-string filters shared by every purchase report.</summary>
        public class PurchaseReportFilter
        {
            public string? PValue { get; set; }
            public string? EType { get; set; }
            public string? Customer { get; set; }
            public DateTime? FromDate { get; set; }
            public DateTime? ToDate { get; set; }
            public int SubscID { get; set; }
            /// <summary>RFQ reports only: calendar DocYear (e.g. 2026); 0/blank = all.</summary>
            public int? DocYear { get; set; }
            /// <summary>RFQ report only: free text matched against the notes.</summary>
            public string? Search { get; set; }
        }

        // Header filter for PO / GRN, master aliased "m" (supplier matched by exact name).
        private const string CommonWhere = @"
            m.SubscID = @SubscID
            AND m.DocDate BETWEEN @FromDate AND @ToDate
            AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
            AND (@Customer = '' OR m.VendorName = @Customer)";

        // ============================================================
        // PURCHASE ORDER REGISTER — POMst + PODtl (one row per PO line)
        //   Open      = approved, not closed (DocStatus <> 'Y') and the line still
        //               has a GRN balance (PO qty - billed qty > 0).
        //   Closed    = approved and closed (DocStatus 'Y') or the line fully received.
        //   Cancelled = deleted PO (current revision).
        //   All       = Open + Closed.
        // ============================================================

        [HttpGet("api/PurchaseOrderReport/AllReports")]
        public Task<IActionResult> PurchaseOrderReport([FromQuery] PurchaseReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                SELECT * FROM (
                    SELECT
                        d.TransSlNo                               AS rowId,
                        m.MTransNo                                AS mTransNo,
                        RTRIM(m.DocSeries)                        AS docSeries,
                        CAST(m.DocNo AS varchar(20))              AS poNo,
                        CONVERT(char(10), m.DocDate, 23)          AS poDate,
                        ISNULL(m.VendorName, '')                  AS supplier,
                        ISNULL(d.PartCode, '')                    AS partCode,
                        ISNULL(d.PartDescription, '')
                          + CASE WHEN ISNULL(d.Grade, '') <> '' THEN ' - ' + d.Grade ELSE '' END AS itemDesc,
                        ISNULL(d.ItemRemarks, '')                 AS remarks,
                        ISNULL(d.Unit, '')                        AS unit,
                        d.Qty                                     AS qty,
                        ISNULL(g.BilledQty, 0)                    AS grnQty,
                        d.Qty - ISNULL(g.BilledQty, 0)            AS balQty,
                        d.Rate                                    AS rate,
                        d.BasicValue                              AS amount,
                        CONVERT(char(10), ISNULL(d.DeleveryDate, m.DueDate), 23) AS deliveryDate,
                        CASE WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                             WHEN RTRIM(m.DocStatus) = 'Y'
                               OR d.Qty - ISNULL(g.BilledQty, 0) <= 0 THEN 'Closed'
                             ELSE 'Open' END                      AS type,
                        m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortSl
                    FROM dbo.POMst m
                    JOIN dbo.PODtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (
                        SELECT SUM(gd.BillQty) AS BilledQty
                        FROM dbo.GRNDtl gd
                        JOIN dbo.GRNMst gm ON gm.MTransNo = gd.MTransNo
                        WHERE gd.DeleteStatus = 'N' AND gm.DeleteStatus = 'N'
                          AND gm.TargetType = 'PO' AND gm.TargetTransNo = m.MTransNo
                          AND gd.SlNo = d.SlNo
                    ) g
                    WHERE " + CommonWhere + @"
                      AND m.DocAmdStatus = 'N'
                      AND (m.DeleteStatus = 'Y' OR m.LockStatus = 'Y')
                ) r
                WHERE (@EType = 'All' AND r.type <> 'Cancelled') OR r.type = @EType
                ORDER BY r.sortDate, r.sortNo, r.sortSl");

        // ============================================================
        // GRN REGISTER — GRNMst + GRNDtl (one row per GRN line)
        //   Bill Pending = approved GRN with no supplier bill (RefNo blank).
        //   QA Pending   = approved GRN not yet inspected (GRNType 'N').
        //   Cancelled    = deleted GRN.
        //   All          = every approved live GRN.
        // A row can be both Bill Pending and QA Pending; under "All" the status
        // column shows QA Pending first, then Bill Pending, else Completed.
        // ============================================================

        [HttpGet("api/GRNReport/AllReports")]
        public Task<IActionResult> GRNReport([FromQuery] PurchaseReportFilter f) =>
            RunReport(f, new[] { "All", "Bill Pending", "QA Pending", "Cancelled" }, @"
                SELECT * FROM (
                    SELECT
                        d.TransSlNo                               AS rowId,
                        m.MTransNo                                AS mTransNo,
                        RTRIM(m.DocSeries)                        AS docSeries,
                        CAST(m.DocNo AS varchar(20))              AS grnNo,
                        CONVERT(char(10), m.DocDate, 23)          AS grnDate,
                        ISNULL(CAST(m.TargetDocNo AS varchar(20)), '') AS poNo,
                        CONVERT(char(10), p.DocDate, 23)          AS poDate,
                        ISNULL(m.VendorName, '')                  AS supplier,
                        ISNULL(m.RefNo, '')                       AS invoiceNo,
                        CONVERT(char(10), m.RefDate, 23)          AS invoiceDate,
                        ISNULL(m.DCNo, '')                        AS dcNo,
                        CONVERT(char(10), m.DCDate, 23)           AS dcDate,
                        ISNULL(d.PartCode, '')                    AS partCode,
                        ISNULL(d.PartDescription, '')
                          + CASE WHEN ISNULL(d.Grade, '') <> '' THEN ' - ' + d.Grade ELSE '' END AS itemDesc,
                        ISNULL(d.Unit, '')                        AS unit,
                        ISNULL(d.POQty, ISNULL(pd.Qty, 0))        AS poQty,
                        d.BillQty                                 AS billQty,
                        d.RecQty                                  AS receivedQty,
                        d.AccQty                                  AS accQty,
                        d.RejQty                                  AS rejQty,
                        -- PO line still to be received, across every live GRN on it.
                        CASE WHEN pd.TransSlNo IS NULL THEN 0
                             ELSE pd.Qty - ISNULL(b.BilledQty, 0) END AS balQty,
                        CASE WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                             WHEN ISNULL(m.GRNType, 'N') = 'N' THEN 'QA Pending'
                             WHEN LTRIM(ISNULL(m.RefNo, '')) = '' THEN 'Bill Pending'
                             ELSE 'Completed' END                 AS rowStatus,
                        CASE WHEN ISNULL(m.GRNType, 'N') = 'N' THEN 1 ELSE 0 END AS sortQA,
                        CASE WHEN LTRIM(ISNULL(m.RefNo, '')) = '' THEN 1 ELSE 0 END AS sortBill,
                        m.DeleteStatus AS sortDel,
                        m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortSl
                    FROM dbo.GRNMst m
                    JOIN dbo.GRNDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.POMst p ON m.TargetType = 'PO' AND p.MTransNo = m.TargetTransNo
                    LEFT JOIN dbo.PODtl pd ON pd.MTransNo = p.MTransNo AND pd.SlNo = d.SlNo
                                          AND pd.DeleteStatus = 'N'
                    OUTER APPLY (
                        SELECT SUM(gd.BillQty) AS BilledQty
                        FROM dbo.GRNDtl gd
                        JOIN dbo.GRNMst gm ON gm.MTransNo = gd.MTransNo
                        WHERE gd.DeleteStatus = 'N' AND gm.DeleteStatus = 'N'
                          AND gm.TargetType = 'PO' AND gm.TargetTransNo = m.TargetTransNo
                          AND gd.SlNo = d.SlNo
                    ) b
                    WHERE " + CommonWhere + @"
                      AND (m.DeleteStatus = 'Y' OR m.LockStatus = 'Y')
                ) r
                WHERE (@EType = 'All'          AND r.sortDel = 'N')
                   OR (@EType = 'Bill Pending' AND r.sortDel = 'N' AND r.sortBill = 1)
                   OR (@EType = 'QA Pending'   AND r.sortDel = 'N' AND r.sortQA = 1)
                   OR (@EType = 'Cancelled'    AND r.sortDel = 'Y')
                ORDER BY r.sortDate, r.sortNo, r.sortSl",
                // The status column echoes the chosen filter, so a row that is both
                // Bill Pending and QA Pending reads as whatever the user asked for.
                typeColumn: "rowStatus");

        // ============================================================
        // RFQ REGISTER — RFQMst (current revision, live), filtered by RFQ date.
        //   Open     = DocStatus 'N' (no winning supplier picked yet)
        //   Closed   = DocStatus 'Y' (a supplier has been selected)
        //   Approved = LockStatus 'Y' (approved to be sent out for quotes)
        //   Pending  = LockStatus 'N' (awaiting approval)
        // Customer is ignored; Search matches the notes.
        // ============================================================

        [HttpGet("api/RFQReport/AllReports")]
        public Task<IActionResult> RFQReport([FromQuery] PurchaseReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Approved", "Pending" }, @"
                SELECT
                    m.MTransNo                                    AS mTransNo,
                    RTRIM(m.DocSeries) + '-' + CAST(m.DocNo AS varchar(20))
                      + CASE WHEN m.DocAmdNo > 0 THEN ' (A' + CAST(m.DocAmdNo AS varchar(10)) + ')' ELSE '' END AS rfqNo,
                    CONVERT(char(10), m.DocDate, 23)              AS rfqDate,
                    ISNULL(RTRIM(pr.DocSeries) + '-' + CAST(pr.DocNo AS varchar(20)), '') AS prNo,
                    CONVERT(char(10), m.ValidTill, 23)            AS validTill,
                    ISNULL(m.DocNotes, '')                        AS docNotes,
                    (SELECT COUNT(*) FROM dbo.RFQPriceMst q
                      WHERE q.TargetTransNo = m.MTransNo AND q.DeleteStatus = 'N'
                        AND q.DocAmdStatus = 'N')                 AS quotes,
                    CASE WHEN RTRIM(m.DocStatus) = 'Y' THEN 'Closed' ELSE 'Open' END AS docStatus,
                    CASE WHEN m.LockStatus = 'Y' THEN 'Approved' ELSE 'Pending' END AS lockStatus
                FROM dbo.RFQMst m
                LEFT JOIN dbo.PurReqMst pr ON pr.MTransNo = m.PRTransNo
                WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                  AND m.DocDate BETWEEN @FromDate AND @ToDate
                  AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                  AND (@DocYear = 0 OR m.DocYear = @DocYear)
                  AND (@Search = '' OR m.DocNotes LIKE '%' + @Search + '%' OR m.DocAmdNotes LIKE '%' + @Search + '%')
                  AND (@EType = 'All'
                       OR (@EType = 'Open'     AND RTRIM(m.DocStatus) <> 'Y')
                       OR (@EType = 'Closed'   AND RTRIM(m.DocStatus) = 'Y')
                       OR (@EType = 'Approved' AND m.LockStatus = 'Y')
                       OR (@EType = 'Pending'  AND m.LockStatus <> 'Y'))
                ORDER BY m.DocDate, m.DocNo");

        // ============================================================
        // RFQ APPROVAL REGISTER — RFQPriceMst (supplier quotations, current revision)
        //   Approved     = LockStatus 'Y' (the winning quote)
        //   Pending      = LockStatus 'N' and DocStatus 'N' (no decision yet)
        //   Not Selected = LockStatus 'N' and DocStatus 'Y' (closed by the approval)
        // Customer = part of the supplier name (contains).
        // ============================================================

        [HttpGet("api/RFQApprovalReport/AllReports")]
        public Task<IActionResult> RFQApprovalReport([FromQuery] PurchaseReportFilter f) =>
            RunReport(f, new[] { "All", "Approved", "Pending", "Not Selected" }, @"
                SELECT * FROM (
                    SELECT
                        m.MTransNo                                AS mTransNo,
                        RTRIM(m.DocSeries) + '-' + CAST(m.DocNo AS varchar(20))
                          + CASE WHEN m.DocAmdNo > 0 THEN ' (A' + CAST(m.DocAmdNo AS varchar(10)) + ')' ELSE '' END AS quoteNo,
                        CONVERT(char(10), m.DocDate, 23)          AS quoteDate,
                        ISNULL(m.VendorName, '')                  AS supplier,
                        ISNULL(m.RefNo, '')                       AS refNo,
                        CONVERT(char(10), m.RefDate, 23)          AS refDate,
                        ISNULL(RTRIM(r.DocSeries) + '-' + CAST(r.DocNo AS varchar(20)), '') AS rfqNo,
                        COALESCE(RTRIM(pr.DocSeries) + '-' + CAST(pr.DocNo AS varchar(20)),
                                 CASE WHEN m.PRNo > 0 THEN CAST(m.PRNo AS varchar(20)) END, '') AS prNo,
                        CONVERT(char(10), m.ValidTill, 23)        AS validTill,
                        CASE WHEN m.LockStatus = 'Y' THEN 'Approved'
                             WHEN RTRIM(m.DocStatus) = 'Y' THEN 'Not Selected'
                             ELSE 'Pending' END                   AS approval,
                        m.DocDate AS sortDate, m.DocNo AS sortNo
                    FROM dbo.RFQPriceMst m
                    LEFT JOIN dbo.RFQMst r ON r.MTransNo = m.TargetTransNo
                    LEFT JOIN dbo.PurReqMst pr ON pr.MTransNo = r.PRTransNo
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@DocYear = 0 OR m.DocYear = @DocYear)
                      AND (@Customer = '' OR m.VendorName LIKE '%' + @Customer + '%')
                ) x
                WHERE @EType = 'All' OR x.approval = @EType
                ORDER BY x.sortDate, x.sortNo");

        // ============================================================
        // SHARED RUNNER
        // ============================================================

        /// <summary>
        /// Validates the report filters, then runs the report query. When
        /// <paramref name="typeColumn"/> is given and a specific type was chosen,
        /// that column is renamed to "type" and set to the chosen type; otherwise
        /// it is renamed to "type" as-is.
        /// </summary>
        private async Task<IActionResult> RunReport(PurchaseReportFilter f, string[] types, string sql,
                                                    string? typeColumn = null)
        {
            if (f.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (f.FromDate == null || f.ToDate == null)
                return BadRequestResult("From Date and To Date are required.");
            if (f.FromDate.Value.Date > f.ToDate.Value.Date)
                return BadRequestResult("From Date cannot be after To Date.");

            var type = types.FirstOrDefault(t =>
                string.Equals(t, (f.EType ?? "").Trim() == "" ? "All" : f.EType!.Trim(),
                              StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return BadRequestResult($"Type must be one of: {string.Join(", ", types)}.");

            return await RunQuery(sql, typeColumn, type,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = f.SubscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = f.FromDate.Value.Date },
                new SqlParameter("@ToDate", SqlDbType.Date) { Value = f.ToDate.Value.Date },
                new SqlParameter("@Series", SqlDbType.VarChar, 10) { Value = (f.PValue ?? "").Trim() },
                new SqlParameter("@Customer", SqlDbType.VarChar, 100) { Value = (f.Customer ?? "").Trim() },
                new SqlParameter("@EType", SqlDbType.VarChar, 20) { Value = type },
                new SqlParameter("@DocYear", SqlDbType.Int) { Value = f.DocYear ?? 0 },
                new SqlParameter("@Search", SqlDbType.VarChar, 100) { Value = (f.Search ?? "").Trim() });
        }

        /// <summary>
        /// Runs one read-only query and returns its rows as column-name → value
        /// maps. Helper columns prefixed "sort" are dropped; strings are trimmed.
        /// </summary>
        private async Task<IActionResult> RunQuery(string sql, string? typeColumn, string type,
                                                   params SqlParameter[] parameters)
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
                        if (value is string s) value = s.Trim();

                        if (typeColumn != null && name == typeColumn)
                        {
                            name = "type";
                            if (type != "All") value = type;
                        }
                        row[name] = value;
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
