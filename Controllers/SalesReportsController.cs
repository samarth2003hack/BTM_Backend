using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Sales Reports — the eight read-only registers behind EMSSoft/Modules/SalesReports,
    /// plus the three attachment lists (Enquiry / Quotation / OA) in the same folder.
    ///
    /// Every report answers GET api/{Report}/AllReports with the legacy filter names
    /// (PValue = DocSeries, EType = report type, Customer = customer NAME, FromDate,
    /// ToDate, SubscID) and returns a flat row list whose camelCase keys are exactly
    /// the REPORT_COLUMNS keys of the matching page.
    ///
    /// Shared rules:
    ///   - Cancelled = soft-deleted (DeleteStatus 'Y').
    ///   - Enquiry / Quotation / OA read only the current revision (DocAmdStatus 'N').
    ///   - Links: QtnMst.TargetTransNo = EnqMst.MTransNo, OAMst.TargetTransNo = QtnMst.MTransNo,
    ///     STxnDtl.LinkNo = OADtl.TransSlNo.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    ///
    /// The SQL lives here rather than in stored procedures so the reports need no
    /// DB deployment beyond the sales tables themselves.
    /// </summary>
    public class SalesReportsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public SalesReportsController(EMSContext context) => db = context;

        /// <summary>Query-string filters shared by every sales report.</summary>
        public class SalesReportFilter
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

        // ============================================================
        // SALES REGISTER — STxnMst
        // ============================================================

        [HttpGet("api/SalesReport/AllReports")]
        public Task<IActionResult> SalesReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Summary", "Cancelled" }, @"
                SELECT
                    CAST(m.DocNo AS varchar(20))              AS invNo,
                    CONVERT(char(10), m.DocDate, 23)          AS invDate,
                    ISNULL(m.RefNo, '')                       AS poNo,
                    ISNULL(m.VendorName, '')                  AS customerName,
                    ISNULL(m.GSTIN, '')                       AS gstin,
                    ISNULL(m.BasicValue, 0)                   AS basicValue,
                    ISNULL(m.PnFAmt, 0) + ISNULL(m.FreightAmt, 0) + ISNULL(m.OtherAmt, 0) AS othersValue,
                    ISNULL(m.ExemptValue, 0)                  AS exemptValue,
                    ISNULL(m.TaxableValue, 0)                 AS taxableValue,
                    ISNULL(m.IGSTAmt, 0)                      AS igst,
                    ISNULL(m.CGSTAmt, 0)                      AS cgst,
                    ISNULL(m.SGSTAmt, 0)                      AS sgst,
                    ISNULL(m.RoundOff, 0)                     AS roundOff,
                    ISNULL(m.NetValue, 0)                     AS netValue
                FROM dbo.STxnMst m
                WHERE " + CommonWhere + @"
                  -- All = every invoice, Summary = live only, Cancelled = deleted only.
                  AND (@EType = 'All'
                       OR (@EType = 'Summary'   AND m.DeleteStatus = 'N')
                       OR (@EType = 'Cancelled' AND m.DeleteStatus = 'Y'))
                ORDER BY m.DocDate, m.DocNo");

        // ============================================================
        // SALES RETURN REGISTER — SalesReturnMst (+ SalesReturnDtl for All)
        // ============================================================
        //
        // All       = live credit notes, one row per returned ITEM.
        // Summary   = live credit notes, one row per note with its tax split.
        // Cancelled = deleted credit notes, same shape as Summary.
        // A line stored with no return quantity never came back and is left out.

        [HttpGet("api/SalesReturnReport/AllReports")]
        public Task<IActionResult> SalesReturnReport([FromQuery] SalesReportFilter f) =>
            string.Equals((f.EType ?? "All").Trim(), "All", StringComparison.OrdinalIgnoreCase)
                ? RunReport(f, new[] { "All" }, @"
                    SELECT
                        m.MTransNo                                AS mTransNo,
                        RTRIM(m.DocSeries) + ' - ' + CAST(m.DocNo AS varchar(20)) AS cnNo,
                        CONVERT(char(10), m.DocDate, 23)          AS cnDate,
                        " + SalesReturnInvNo + @"                 AS invNo,
                        ISNULL(m.VendorName, '')                  AS customerName,
                        CASE WHEN ISNULL(d.PartCode, '') = '' THEN ISNULL(d.Description, '')
                             WHEN ISNULL(d.Description, '') = '' THEN d.PartCode
                             ELSE d.PartCode + ' - ' + d.Description END AS product,
                        ISNULL(d.MocDescription, '')              AS grade,
                        ISNULL(d.Qty, 0)                          AS invQty,
                        ISNULL(d.ReturnQty, 0)                    AS returnQty,
                        ISNULL(d.Unit, '')                        AS unit,
                        ISNULL(d.Rate, 0)                         AS rate,
                        ISNULL(d.GSTP, 0)                         AS gstP,
                        CAST(ROUND(ISNULL(d.ReturnQty, 0) * ISNULL(d.Rate, 0), 2) AS numeric(16, 2)) AS amount,
                        m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortLine
                    FROM dbo.SalesReturnMst m
                    JOIN dbo.SalesReturnDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                                             AND ISNULL(d.ReturnQty, 0) > 0
                    LEFT JOIN dbo.STxnMst inv ON inv.MTransNo = m.TargetTransNo
                    WHERE " + CommonWhere + @"
                      AND m.DeleteStatus = 'N'
                    ORDER BY m.DocDate, m.DocNo, d.SlNo")
                : RunReport(f, new[] { "Summary", "Cancelled" }, @"
                    SELECT
                        m.MTransNo                                AS mTransNo,
                        RTRIM(m.DocSeries) + ' - ' + CAST(m.DocNo AS varchar(20)) AS cnNo,
                        CONVERT(char(10), m.DocDate, 23)          AS cnDate,
                        " + SalesReturnInvNo + @"                 AS invNo,
                        ISNULL(m.VendorName, '')                  AS customerName,
                        ISNULL(m.TxnGSTIN, '')                    AS gstin,
                        ISNULL(m.TaxableValue, 0)                 AS taxableValue,
                        ISNULL(m.IGSTAmt, 0)                      AS igst,
                        ISNULL(m.CGSTAmt, 0)                      AS cgst,
                        ISNULL(m.SGSTAmt, 0)                      AS sgst,
                        ISNULL(m.RoundOff, 0)                     AS roundOff,
                        ISNULL(m.NetValue, 0)                     AS netValue
                    FROM dbo.SalesReturnMst m
                    LEFT JOIN dbo.STxnMst inv ON inv.MTransNo = m.TargetTransNo
                    WHERE " + CommonWhere + @"
                      AND m.DeleteStatus = CASE @EType WHEN 'Cancelled' THEN 'Y' ELSE 'N' END
                    ORDER BY m.DocDate, m.DocNo");

        // The invoice a credit note was raised against, as "<series> - <no>"; falls
        // back to the number stored on the note when the invoice row is gone.
        private const string SalesReturnInvNo = @"COALESCE(
                            RTRIM(inv.DocSeries) + ' - ' + CAST(inv.DocNo AS varchar(20)),
                            CAST(m.TargetDocNo AS varchar(20)), '')";

        // ============================================================
        // LEAD REGISTER — LeadGeneration
        // ============================================================

        [HttpGet("api/LeadReport/AllReports")]
        public Task<IActionResult> LeadReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                SELECT * FROM (
                    SELECT
                        CAST(m.DocNo AS varchar(20))          AS leadNo,
                        CONVERT(char(10), m.DocDate, 23)      AS leadDate,
                        ISNULL(m.VendorName, '')              AS customerName,
                        ISNULL(m.VendorAddress, '')           AS address,
                        ISNULL(m.City, '')                    AS city,
                        ISNULL(m.PhoneNo, '')                 AS phoneNo,
                        ISNULL(m.Emailid, '')                 AS emailId,
                        ISNULL(m.Product, '')                 AS productName,
                        ISNULL(m.Qty, 0)                      AS qty,
                        ISNULL(m.DocNotes, '')                AS docNotes,
                        CONVERT(char(10), m.Followupdate, 23) AS followupDate,
                        ISNULL(u.UserName, '')                AS userName,
                        CASE WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                             WHEN m.DocStatus = 'Y'    THEN 'Closed'
                             ELSE 'Open' END                  AS status,
                        m.DocDate AS sortDate, m.DocNo AS sortNo
                    FROM dbo.LeadGeneration m
                    LEFT JOIN dbo.UserMaster u ON u.MTransNo = m.CreatedBy
                    WHERE " + CommonWhere + @"
                ) r
                WHERE @EType = 'All' OR r.status = @EType
                ORDER BY r.sortDate, r.sortNo");

        // ============================================================
        // ENQUIRY REGISTER — EnqMst + EnqDtl (one row per item)
        // ============================================================

        [HttpGet("api/EnquiryReport/AllReports")]
        public Task<IActionResult> EnquiryReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Quoted", "Executed", "Cancelled" }, @"
                WITH hdr AS (
                    SELECT m.*,
                        CASE
                            WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                            -- Executed: an Order Acceptance was raised on one of its quotations.
                            WHEN EXISTS (SELECT 1 FROM dbo.QtnMst q
                                         JOIN dbo.OAMst o ON o.TargetTransNo = q.MTransNo
                                                         AND o.DeleteStatus = 'N' AND o.DocAmdStatus = 'N'
                                         WHERE q.TargetTransNo = m.MTransNo
                                           AND q.DeleteStatus = 'N' AND q.SubscID = m.SubscID) THEN 'Executed'
                            WHEN EXISTS (SELECT 1 FROM dbo.QtnMst q
                                         WHERE q.TargetTransNo = m.MTransNo
                                           AND q.DeleteStatus = 'N' AND q.SubscID = m.SubscID) THEN 'Quoted'
                            ELSE 'Open'
                        END AS RptStatus
                    FROM dbo.EnqMst m
                    WHERE " + CommonWhere + @"
                      AND m.DocAmdStatus = 'N'
                )
                SELECT
                    CAST(h.DocNo AS varchar(20))              AS enqNo,
                    CONVERT(char(10), h.DocDate, 23)          AS enqDate,
                    ISNULL(h.EnqPriority, '')                 AS priority,
                    ISNULL(h.VendorName, '')                  AS customerName,
                    ISNULL(d.PartCode, '')                    AS productCode,
                    ISNULL(d.Description, '')                 AS description,
                    ISNULL(d.Qty, 0)                          AS qty,
                    h.RptStatus                               AS status
                FROM hdr h
                LEFT JOIN dbo.EnqDtl d ON d.MTransNo = h.MTransNo AND d.DeleteStatus = 'N'
                WHERE @EType = 'All' OR h.RptStatus = @EType
                ORDER BY h.DocDate, h.DocNo, d.SLNo");

        // ============================================================
        // QUOTATION REGISTER — QtnMst
        // ============================================================

        [HttpGet("api/QuotationReport/AllReports")]
        public Task<IActionResult> QuotationReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                SELECT * FROM (
                    SELECT
                        CAST(m.DocNo AS varchar(20))          AS qtnNo,
                        CONVERT(char(10), m.DocDate, 23)      AS qtnDate,
                        ISNULL(m.RefNo, '')                   AS customerRef,
                        ISNULL(m.VendorName, '')              AS customerName,
                        ISNULL(m.QtnCategory, '')             AS category,
                        ISNULL(m.SalesExec, '')               AS salesExecutive,
                        ISNULL(m.CPName, '')                  AS contactPerson,
                        COALESCE(NULLIF(m.CPPhone, ''), m.PhoneNo, '') AS phoneNo,
                        ISNULL(m.EmailID, '')                 AS emailId,
                        ISNULL(m.NetValue, 0)                 AS totalValue,
                        CASE
                            WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                            -- Closed: marked closed, or already converted into an OA.
                            WHEN m.DocStatus = 'Y'
                              OR EXISTS (SELECT 1 FROM dbo.OAMst o
                                         WHERE o.TargetTransNo = m.MTransNo AND o.SubscID = m.SubscID
                                           AND o.DeleteStatus = 'N' AND o.DocAmdStatus = 'N') THEN 'Closed'
                            ELSE 'Open'
                        END                                   AS status,
                        m.DocDate AS sortDate, m.DocNo AS sortNo
                    FROM dbo.QtnMst m
                    WHERE " + CommonWhere + @"
                      AND m.DocAmdStatus = 'N'
                ) r
                WHERE @EType = 'All' OR r.status = @EType
                ORDER BY r.sortDate, r.sortNo");

        // ============================================================
        // ORDER ACCEPTANCE REGISTER — OAMst + OADtl (one row per item)
        // ============================================================

        [HttpGet("api/OrderAcceptancePReport/AllReports")]
        public Task<IActionResult> OrderAcceptanceReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                SELECT * FROM (
                    SELECT
                        CAST(m.DocNo AS varchar(20))          AS oaNo,
                        CONVERT(char(10), m.DocDate, 23)      AS oaDate,
                        CONVERT(char(10), COALESCE(d.DeliveryDate, m.DueDate), 23) AS dueDate,
                        ISNULL(m.RefNo, '')                   AS poNo,
                        CONVERT(char(10), m.RefDate, 23)      AS poDate,
                        ISNULL(m.VendorName, '')              AS customerName,
                        ISNULL(d.PartDescription, '')         AS description,
                        ISNULL(d.Qty, 0)                      AS qty,
                        ISNULL(d.Rate, 0)                     AS rate,
                        ISNULL(d.BasicValue, 0)               AS amount,
                        CASE
                            WHEN m.DeleteStatus = 'Y' THEN 'Cancelled'
                            -- Closed: the OA is closed, or this line is fully invoiced.
                            WHEN m.DocStatus = 'Y' OR ISNULL(inv.Qty, 0) >= ISNULL(d.Qty, 0) THEN 'Closed'
                            ELSE 'Open'
                        END                                   AS status,
                        m.DocDate AS sortDate, m.DocNo AS sortNo, d.SlNo AS sortLine
                    FROM dbo.OAMst m
                    JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (
                        SELECT SUM(sd.Qty) AS Qty
                        FROM dbo.STxnDtl sd
                        JOIN dbo.STxnMst sm ON sm.MTransNo = sd.MTransNo AND sm.DeleteStatus = 'N'
                        WHERE sd.LinkNo = d.TransSlNo AND sd.DeleteStatus = 'N'
                    ) inv
                    WHERE " + CommonWhere + @"
                      AND m.DocAmdStatus = 'N'
                ) r
                WHERE @EType = 'All' OR r.status = @EType
                ORDER BY r.sortDate, r.sortNo, r.sortLine");

        // ============================================================
        // PACKING SLIP REGISTER — PackingSlipMst
        // ============================================================

        [HttpGet("api/PackingSlipReport/AllReports")]
        public Task<IActionResult> PackingSlipReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Draft", "Submitted", "Approved" }, @"
                SELECT * FROM (
                    SELECT
                        CAST(m.DocNo AS varchar(20))          AS slipNo,
                        CONVERT(char(10), m.DocDate, 23)      AS slipDate,
                        ISNULL(CAST(COALESCE(m.OANo, m.TargetDocNo) AS varchar(20)), '') AS oaNo,
                        ISNULL(m.VendorName, '')              AS customerName,
                        ISNULL(m.TotalCases, 0)               AS totalCases,
                        ISNULL(m.TotalWeight, 0)              AS totalWeight,
                        -- LockStatus: Y approved, N draft, anything else submitted.
                        CASE m.LockStatus WHEN 'Y' THEN 'Approved'
                                          WHEN 'N' THEN 'Draft'
                                          ELSE 'Submitted' END AS status,
                        m.DocDate AS sortDate, m.DocNo AS sortNo
                    FROM dbo.PackingSlipMst m
                    WHERE " + CommonWhere + @"
                      AND m.DeleteStatus = 'N'
                ) r
                WHERE @EType = 'All' OR r.status = @EType
                ORDER BY r.sortDate, r.sortNo");

        // ============================================================
        // PROFORMA REGISTER — ProformaMst
        // ============================================================

        [HttpGet("api/ProformaReport/AllReports")]
        public Task<IActionResult> ProformaReport([FromQuery] SalesReportFilter f) =>
            RunReport(f, new[] { "All", "Domestic", "Export" }, @"
                SELECT
                    CAST(m.DocNo AS varchar(20))              AS invNo,
                    CONVERT(char(10), m.DocDate, 23)          AS invDate,
                    ISNULL(m.InvType, '')                     AS invType,
                    ISNULL(CAST(m.TargetDocNo AS varchar(20)), '') AS oaNo,
                    ISNULL(m.VendorName, '')                  AS customerName,
                    ISNULL(NULLIF(m.TxnCurrency, ''), 'INR')  AS currency,
                    ISNULL(m.CurrencyAmount, 0)               AS currencyValue,
                    ISNULL(m.NetValue, 0)                     AS netValue,
                    ISNULL(m.AdvanceP, 0)                     AS advancePct,
                    ISNULL(m.Advance, 0)                      AS advanceValue
                FROM dbo.ProformaMst m
                WHERE " + CommonWhere + @"
                  AND m.DeleteStatus = 'N'
                  AND (@EType = 'All' OR m.InvType = @EType)
                ORDER BY m.DocDate, m.DocNo");

        // ============================================================
        // ATTACHMENT VIEWS — every live document that carries a file
        // ============================================================
        //
        // AttFile is the path relative to the API web root (e.g.
        // "Uploads/QTN/12/2026…pdf", written by GrnUploads), so the page loads
        // it as DomainName + "/" + attFile through the static-file pipeline.

        // Enquiry has no attachment table — only the single EnqMst.FileAttach column.
        [HttpGet("api/SalesReports/Attachments/Enq/{subscID}")]
        public Task<IActionResult> EnqAttachments(int subscID) =>
            RunAttachmentList(subscID, @"
                SELECT m.MTransNo AS transSlNo, m.MTransNo AS mTransNo,
                       RTRIM(m.DocSeries) AS docSeries, CAST(m.DocNo AS varchar(20)) AS docNo,
                       ISNULL(m.VendorName, '') AS customerName,
                       ISNULL(NULLIF(m.DocNotes, ''), 'Enquiry attachment') AS attNotes,
                       m.FileAttach AS attFile
                FROM dbo.EnqMst m
                WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                  AND LTRIM(ISNULL(m.FileAttach, '')) <> ''
                ORDER BY m.DocDate DESC, m.DocNo DESC");

        [HttpGet("api/SalesReports/Attachments/Qtn/{subscID}")]
        public Task<IActionResult> QtnAttachments(int subscID) =>
            RunAttachmentList(subscID, @"
                SELECT a.TransSlNo AS transSlNo, m.MTransNo AS mTransNo,
                       RTRIM(m.DocSeries) AS docSeries, CAST(m.DocNo AS varchar(20)) AS docNo,
                       ISNULL(m.VendorName, '') AS customerName,
                       ISNULL(a.AttNotes, '') AS attNotes, a.AttFile AS attFile
                FROM dbo.QtnAttachment a
                JOIN dbo.QtnMst m ON m.MTransNo = a.MTransNo AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                WHERE a.SubscID = @SubscID AND a.DeleteStatus = 'N'
                  AND LTRIM(ISNULL(a.AttFile, '')) <> ''
                ORDER BY m.DocDate DESC, m.DocNo DESC, a.SlNo");

        [HttpGet("api/SalesReports/Attachments/OA/{subscID}")]
        public Task<IActionResult> OAAttachments(int subscID) =>
            RunAttachmentList(subscID, @"
                SELECT a.TransSlNo AS transSlNo, m.MTransNo AS mTransNo,
                       RTRIM(m.DocSeries) AS docSeries, CAST(m.DocNo AS varchar(20)) AS docNo,
                       ISNULL(m.VendorName, '') AS customerName,
                       ISNULL(a.AttNotes, '') AS attNotes, a.AttFile AS attFile
                FROM dbo.OAAttachment a
                JOIN dbo.OAMst m ON m.MTransNo = a.MTransNo AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                WHERE a.SubscID = @SubscID AND a.DeleteStatus = 'N'
                  AND LTRIM(ISNULL(a.AttFile, '')) <> ''
                ORDER BY m.DocDate DESC, m.DocNo DESC, a.SlNo");

        private Task<IActionResult> RunAttachmentList(int subscID, string sql) =>
            subscID <= 0
                ? Task.FromResult(BadRequestResult("Invalid Subscription."))
                : RunQuery(sql, new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });

        // ============================================================
        // SHARED RUNNER
        // ============================================================

        /// <summary>Validates the report filters, then runs the report query.</summary>
        private async Task<IActionResult> RunReport(SalesReportFilter f, string[] types, string sql)
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
