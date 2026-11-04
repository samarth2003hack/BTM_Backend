using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Requisition / order / gate pass STATUS reports behind EMSSoft/Modules/PurchaseReports:
    /// Purchase Requisition Report, Requisition Status Report, Purchase Status Report
    /// and RGP Status Report.
    ///
    /// Each answers GET api/{Report}/AllReports with the legacy filter names
    /// (PValue = DocSeries, EType = report type, Customer = requested-by / supplier NAME,
    /// PONo / RGPNo = document number, FromDate, ToDate, SubscID). Blank series = every
    /// series, blank customer = everyone, document number 0/blank = every document.
    ///
    /// The status reports come back FLATTENED - one row per (source line x follow-up
    /// line), parent columns repeated on every row - so the grid can sort, filter and
    /// export them like any other register. A source line with no follow-up still
    /// gives one row with the follow-up columns empty.
    ///
    /// Links used:
    ///   PR -> PO   POMst.TargetType 'PR': POMst.TargetTransNo = PurReqMst.MTransNo
    ///              POMst.TargetType 'PQ': POMst.TargetTransNo = RFQPriceMst.MTransNo,
    ///                                     RFQPriceMst.TargetTransNo = RFQMst.MTransNo,
    ///                                     RFQMst.PRTransNo = PurReqMst.MTransNo
    ///              (same two routes VW_PurReqItemsForPO counts). The requisition is
    ///              matched by DocSeries/DocYear/DocNo so any PR revision counts, and the
    ///              line by PartNo (+ GradeNo when both lines carry one). PO lines do not
    ///              carry the PR line number or its OA, so OA is not matched.
    ///   PO -> GRN  GRNMst.TargetType 'PO', GRNMst.TargetTransNo = POMst.MTransNo,
    ///              GRNDtl.SlNo = PODtl.SlNo. The order is matched by DocSeries/DocYear/
    ///              DocNo so receipts booked against an earlier revision still count
    ///              (an amendment copies SlNo forward).
    ///   RGP -> Inw RGPInwDtl.LinkNo = RGPDtl.TransSlNo (live inward line on a live inward).
    ///
    /// Shared rules: approved (LockStatus 'Y') live documents, current revision
    /// (DocAmdStatus 'N') where the table has one; "Cancelled" = soft-deleted.
    /// Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    public class PurchaseStatusReportsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PurchaseStatusReportsController(EMSContext context) => db = context;

        /// <summary>Query-string filters shared by every status report.</summary>
        public class StatusReportFilter
        {
            public string? PValue { get; set; }
            public string? EType { get; set; }
            public string? Customer { get; set; }
            public DateTime? FromDate { get; set; }
            public DateTime? ToDate { get; set; }
            public int SubscID { get; set; }
            /// <summary>PR / PO number (legacy name for both status reports); 0 = all.</summary>
            public int? PONo { get; set; }
            /// <summary>RGP number (RGP Status Report); 0 = all.</summary>
            public int? RGPNo { get; set; }
        }

        // Requisition line as the legacy reports print it: "Description-Grade".
        private const string PartDesc = @"
            ISNULL(pm.PartName, '')
              + CASE WHEN LTRIM(ISNULL(d.Grade, '')) = '' THEN '' ELSE '-' + RTRIM(d.Grade) END";

        private const string LineDesc = @"
            ISNULL(d.PartDescription, '')
              + CASE WHEN LTRIM(ISNULL(d.Grade, '')) = '' THEN '' ELSE '-' + RTRIM(d.Grade) END";

        /// <summary>
        /// CTE "po": every live current-revision PO line together with the identity of
        /// the requisition it was raised from (directly, or through a supplier quote).
        /// </summary>
        private static string PoLinesCte(bool approvedOnly) => @"
                po AS (
                    SELECT om.MTransNo AS POTransNo, om.DocSeries, om.DocYear, om.DocNo, om.DocDate,
                           om.DueDate, om.VendorName, om.DocStatus,
                           od.TransSlNo, od.SlNo, od.PartNo, od.GradeNo, ISNULL(od.Qty, 0) AS Qty,
                           od.DeleveryDate,
                           px.DocSeries AS PRSeries, px.DocYear AS PRYear, px.DocNo AS PRNo
                    FROM dbo.POMst om
                    JOIN dbo.PODtl od ON od.MTransNo = om.MTransNo AND od.DeleteStatus = 'N'
                    LEFT JOIN dbo.RFQPriceMst qq ON om.TargetType = 'PQ' AND qq.MTransNo = om.TargetTransNo
                    LEFT JOIN dbo.RFQMst rr ON rr.MTransNo = qq.TargetTransNo
                    JOIN dbo.PurReqMst px ON px.MTransNo =
                         CASE WHEN om.TargetType = 'PR' THEN om.TargetTransNo ELSE rr.PRTransNo END
                    WHERE om.SubscID = @SubscID AND om.DeleteStatus = 'N' AND om.DocAmdStatus = 'N'
                      AND om.TargetType IN ('PR', 'PQ')" +
                      (approvedOnly ? @"
                      AND om.LockStatus = 'Y'" : "") + @"
                )";

        // PR line <-> PO line: same requisition (any revision), same part, grade when both have one.
        private const string PrPoMatch = @"
                    p.PRSeries = r.DocSeries AND p.PRYear = r.DocYear AND p.PRNo = r.DocNo
                    AND p.PartNo = r.PartNo
                    AND (ISNULL(r.GradeNo, 0) = 0 OR ISNULL(p.GradeNo, 0) = 0 OR p.GradeNo = r.GradeNo)";

        /// <summary>CTE "grn": every live GRN line with the identity of the PO it receives.</summary>
        private const string GrnLinesCte = @"
                grn AS (
                    SELECT gp.DocSeries AS POSeries, gp.DocYear AS POYear, gp.DocNo AS PONo, gd.SlNo,
                           gm.MTransNo AS GRNTransNo, gm.DocSeries, gm.DocNo, gm.DocDate,
                           gm.RefNo, gm.RefDate, gd.TransSlNo,
                           ISNULL(gd.BillQty, 0) AS BillQty, ISNULL(gd.AccQty, 0) AS AccQty,
                           ISNULL(gd.RejQty, 0) AS RejQty
                    FROM dbo.GRNMst gm
                    JOIN dbo.GRNDtl gd ON gd.MTransNo = gm.MTransNo AND gd.DeleteStatus = 'N'
                    JOIN dbo.POMst gp ON gp.MTransNo = gm.TargetTransNo
                    WHERE gm.TargetType = 'PO' AND gm.DeleteStatus = 'N' AND gm.SubscID = @SubscID
                )";

        // ============================================================
        // PURCHASE REQUISITION REGISTER — PurReqMst + PurReqDtl (one row per PR line)
        //   PO Qty    = ordered on every live current-revision PO raised from the PR
        //               (directly or through a quote), approved or not — the same
        //               count the PO item picker uses.
        //   Open      = approved, not closed (DocStatus <> 'Y') and still to be ordered.
        //   Closed    = approved and closed, or fully ordered.
        //   Cancelled = deleted requisition (current revision).
        //   All       = Open + Closed.
        //   Customer  = Requested By (exact name).
        // ============================================================

        [HttpGet("api/PurchaseRequisitionReport/AllReports")]
        public Task<IActionResult> PurchaseRequisitionReport([FromQuery] StatusReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed", "Cancelled" }, @"
                WITH " + PoLinesCte(approvedOnly: false) + @",
                pr AS (
                    SELECT m.MTransNo, m.DocSeries, m.DocYear, m.DocNo, m.DocDate, m.DueDate,
                           m.ReqBy, m.ReqDept, m.Project, m.DocStatus, m.DeleteStatus,
                           d.TransSlNo, d.SlNo, d.PartNo, d.GradeNo, ISNULL(d.Qty, 0) AS Qty,
                           ISNULL(d.Unit, pm.PrimaryUnit) AS Unit, d.ItemRemarks,
                           pm.PartCode, " + PartDesc + @" AS ItemDesc,
                           d.OAYear, d.OANo
                    FROM dbo.PurReqMst m
                    JOIN dbo.PurReqDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.PartMaster pm ON pm.MTransNo = d.PartNo
                    WHERE m.SubscID = @SubscID
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@Customer = '' OR m.ReqBy = @Customer)
                      AND (@DocNo = 0 OR m.DocNo = @DocNo)
                      AND m.DocAmdStatus = 'N'
                      AND (m.DeleteStatus = 'Y' OR m.LockStatus = 'Y')
                )
                SELECT * FROM (
                    SELECT
                        r.TransSlNo                               AS rowId,
                        r.MTransNo                                AS mTransNo,
                        RTRIM(r.DocSeries)                        AS docSeries,
                        CAST(r.DocNo AS varchar(20))              AS prNo,
                        CONVERT(char(10), r.DocDate, 23)          AS prDate,
                        CONVERT(char(10), r.DueDate, 23)          AS dueDate,
                        ISNULL(r.ReqBy, '')                       AS reqBy,
                        ISNULL(r.ReqDept, '')                     AS reqDept,
                        ISNULL(r.Project, '')                     AS project,
                        ISNULL(r.PartCode, '')                    AS partCode,
                        r.ItemDesc                                AS itemDesc,
                        ISNULL(r.Unit, '')                        AS unit,
                        r.Qty                                     AS qty,
                        ISNULL(o.OrderedQty, 0)                   AS poQty,
                        CASE WHEN r.Qty - ISNULL(o.OrderedQty, 0) > 0
                             THEN r.Qty - ISNULL(o.OrderedQty, 0) ELSE 0 END AS balQty,
                        CASE WHEN ISNULL(r.OANo, 0) > 0
                             THEN CAST(r.OAYear AS varchar(10)) + '-' + CAST(r.OANo AS varchar(20))
                             ELSE '' END                          AS oaNo,
                        ISNULL(r.ItemRemarks, '')                 AS remarks,
                        CASE WHEN r.DeleteStatus = 'Y' THEN 'Cancelled'
                             WHEN RTRIM(r.DocStatus) = 'Y'
                               OR r.Qty - ISNULL(o.OrderedQty, 0) <= 0 THEN 'Closed'
                             ELSE 'Open' END                      AS type,
                        r.DocDate AS sortDate, r.DocNo AS sortNo, r.SlNo AS sortSl
                    FROM pr r
                    OUTER APPLY (
                        SELECT SUM(p.Qty) AS OrderedQty FROM po p WHERE " + PrPoMatch + @"
                    ) o
                ) x
                WHERE (@EType = 'All' AND x.type <> 'Cancelled') OR x.type = @EType
                ORDER BY x.sortDate, x.sortNo, x.sortSl");

        // ============================================================
        // REQUISITION STATUS — PR line -> approved PO lines -> GRN lines
        //   One row per PR line x PO line x GRN line (outer joins).
        //   prBalQty  = PR qty less the PO qty ordered so far (running, in PO order).
        //   grnBalQty = PO qty less the qty received so far (running, in GRN order).
        //   Open      = PR line not fully ordered (and PR not closed), or some PO line
        //               on it still awaiting goods (and that PO not closed).
        //   Closed    = otherwise.  All = both.
        // ============================================================

        [HttpGet("api/RequisitionStatusReport/AllReports")]
        public Task<IActionResult> RequisitionStatusReport([FromQuery] StatusReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed" }, @"
                WITH " + PoLinesCte(approvedOnly: true) + @",
                " + GrnLinesCte + @",
                poRecd AS (
                    SELECT p.*, ISNULL(t.RecdQty, 0) AS RecdQty
                    FROM po p
                    OUTER APPLY (
                        SELECT SUM(g.BillQty) AS RecdQty FROM grn g
                        WHERE g.POSeries = p.DocSeries AND g.POYear = p.DocYear
                          AND g.PONo = p.DocNo AND g.SlNo = p.SlNo
                    ) t
                ),
                pr AS (
                    SELECT m.MTransNo, m.DocSeries, m.DocYear, m.DocNo, m.DocDate, m.DueDate,
                           m.ReqBy, m.ReqDept, m.DocStatus,
                           d.TransSlNo, d.SlNo, d.PartNo, d.GradeNo, ISNULL(d.Qty, 0) AS Qty,
                           ISNULL(d.Unit, pm.PrimaryUnit) AS Unit,
                           pm.PartCode, " + PartDesc + @" AS ItemDesc
                    FROM dbo.PurReqMst m
                    JOIN dbo.PurReqDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.PartMaster pm ON pm.MTransNo = d.PartNo
                    WHERE m.SubscID = @SubscID
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@Customer = '' OR m.ReqBy = @Customer)
                      AND (@DocNo = 0 OR m.DocNo = @DocNo)
                      AND m.DocAmdStatus = 'N' AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                ),
                prPo AS (
                    -- PO lines per PR line, with the PR balance left after each one.
                    SELECT r.TransSlNo AS PRTransSlNo, p.*,
                           r.Qty - SUM(p.Qty) OVER (PARTITION BY r.TransSlNo
                                                    ORDER BY p.DocDate, p.DocNo, p.TransSlNo
                                                    ROWS UNBOUNDED PRECEDING) AS PRBal
                    FROM pr r
                    JOIN poRecd p ON " + PrPoMatch + @"
                ),
                prStat AS (
                    SELECT r.TransSlNo,
                        CASE WHEN (RTRIM(r.DocStatus) <> 'Y' AND r.Qty - ISNULL(SUM(pp.Qty), 0) > 0)
                               OR MAX(CASE WHEN RTRIM(pp.DocStatus) <> 'Y' AND pp.Qty - pp.RecdQty > 0
                                           THEN 1 ELSE 0 END) = 1
                             THEN 'Open' ELSE 'Closed' END AS RptStatus
                    FROM pr r
                    LEFT JOIN prPo pp ON pp.PRTransSlNo = r.TransSlNo
                    GROUP BY r.TransSlNo, r.Qty, r.DocStatus
                )
                SELECT
                    CAST(r.TransSlNo AS varchar(12)) + '-' + ISNULL(CAST(pp.TransSlNo AS varchar(12)), '0')
                      + '-' + ISNULL(CAST(g.TransSlNo AS varchar(12)), '0') AS rowId,
                    r.MTransNo                                AS mTransNo,
                    RTRIM(r.DocSeries)                        AS docSeries,
                    CAST(r.DocNo AS varchar(20))              AS prNo,
                    CONVERT(char(10), r.DocDate, 23)          AS prDate,
                    CONVERT(char(10), r.DueDate, 23)          AS prDueDate,
                    ISNULL(r.ReqBy, '')
                      + CASE WHEN LTRIM(ISNULL(r.ReqDept, '')) = '' THEN ''
                             ELSE ' (' + RTRIM(r.ReqDept) + ')' END AS reqBy,
                    ISNULL(r.PartCode, '')                    AS partCode,
                    r.ItemDesc                                AS itemDesc,
                    ISNULL(r.Unit, '')                        AS unit,
                    r.Qty                                     AS prQty,
                    pp.POTransNo                              AS poTransNo,
                    ISNULL(CAST(pp.DocNo AS varchar(20)), '') AS poNo,
                    CONVERT(char(10), pp.DocDate, 23)         AS poDate,
                    CONVERT(char(10), ISNULL(pp.DeleveryDate, pp.DueDate), 23) AS poDueDate,
                    ISNULL(pp.VendorName, '')                 AS supplier,
                    pp.Qty                                    AS poQty,
                    ISNULL(pp.PRBal, r.Qty)                   AS prBalQty,
                    g.GRNTransNo                              AS grnTransNo,
                    ISNULL(CAST(g.DocNo AS varchar(20)), '')  AS grnNo,
                    CONVERT(char(10), g.DocDate, 23)          AS grnDate,
                    ISNULL(g.RefNo, '')                       AS billNo,
                    CONVERT(char(10), g.RefDate, 23)          AS billDate,
                    g.BillQty                                 AS grnQty,
                    g.AccQty                                  AS accQty,
                    g.RejQty                                  AS rejQty,
                    CASE WHEN pp.TransSlNo IS NULL THEN NULL
                         ELSE pp.Qty - ISNULL(SUM(g.BillQty) OVER (
                                PARTITION BY r.TransSlNo, pp.TransSlNo
                                ORDER BY g.DocDate, g.DocNo, g.TransSlNo
                                ROWS UNBOUNDED PRECEDING), 0) END AS grnBalQty,
                    s.RptStatus                               AS type,
                    r.DocDate AS sortDate, r.DocNo AS sortNo, r.SlNo AS sortSl,
                    pp.DocDate AS sortPODate, pp.DocNo AS sortPONo, pp.TransSlNo AS sortPOSl,
                    g.DocDate AS sortGDate, g.DocNo AS sortGNo, g.TransSlNo AS sortGSl
                FROM pr r
                JOIN prStat s ON s.TransSlNo = r.TransSlNo
                LEFT JOIN prPo pp ON pp.PRTransSlNo = r.TransSlNo
                LEFT JOIN grn g ON g.POSeries = pp.DocSeries AND g.POYear = pp.DocYear
                               AND g.PONo = pp.DocNo AND g.SlNo = pp.SlNo
                WHERE @EType = 'All' OR s.RptStatus = @EType
                ORDER BY sortDate, sortNo, sortSl, sortPODate, sortPONo, sortPOSl,
                         sortGDate, sortGNo, sortGSl");

        // ============================================================
        // PURCHASE STATUS — approved PO line -> GRN lines
        //   One row per PO line x GRN line (outer join).
        //   balQty = PO qty less the qty received so far (running, in GRN order).
        //   Open   = PO not closed (DocStatus <> 'Y') and the line not fully received.
        //   Closed = otherwise.  All = both.  Customer = supplier name.
        // ============================================================

        [HttpGet("api/PurchaseStatusReport/AllReports")]
        public Task<IActionResult> PurchaseStatusReport([FromQuery] StatusReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed" }, @"
                WITH " + GrnLinesCte + @",
                lines AS (
                    SELECT m.MTransNo, m.DocSeries, m.DocYear, m.DocNo, m.DocDate, m.DueDate,
                           m.VendorName, m.DocStatus,
                           d.TransSlNo, d.SlNo, d.PartCode, " + LineDesc + @" AS ItemDesc,
                           d.Unit, ISNULL(d.Qty, 0) AS Qty, d.Rate, d.DeleveryDate, d.ItemRemarks,
                           ISNULL(t.RecdQty, 0) AS RecdQty
                    FROM dbo.POMst m
                    JOIN dbo.PODtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (
                        SELECT SUM(g.BillQty) AS RecdQty FROM grn g
                        WHERE g.POSeries = m.DocSeries AND g.POYear = m.DocYear
                          AND g.PONo = m.DocNo AND g.SlNo = d.SlNo
                    ) t
                    WHERE m.SubscID = @SubscID
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@Customer = '' OR m.VendorName = @Customer)
                      AND (@DocNo = 0 OR m.DocNo = @DocNo)
                      AND m.DocAmdStatus = 'N' AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                )
                SELECT * FROM (
                    SELECT
                        CAST(l.TransSlNo AS varchar(12)) + '-' + ISNULL(CAST(g.TransSlNo AS varchar(12)), '0') AS rowId,
                        l.MTransNo                                AS mTransNo,
                        RTRIM(l.DocSeries)                        AS docSeries,
                        CAST(l.DocNo AS varchar(20))              AS poNo,
                        CONVERT(char(10), l.DocDate, 23)          AS poDate,
                        CONVERT(char(10), ISNULL(l.DeleveryDate, l.DueDate), 23) AS dueDate,
                        ISNULL(l.VendorName, '')                  AS supplier,
                        ISNULL(l.PartCode, '')                    AS partCode,
                        l.ItemDesc                                AS itemDesc,
                        ISNULL(l.Unit, '')                        AS unit,
                        l.Qty                                     AS poQty,
                        g.GRNTransNo                              AS grnTransNo,
                        ISNULL(CAST(g.DocNo AS varchar(20)), '')  AS grnNo,
                        CONVERT(char(10), g.DocDate, 23)          AS grnDate,
                        ISNULL(g.RefNo, '')                       AS billNo,
                        CONVERT(char(10), g.RefDate, 23)          AS billDate,
                        g.BillQty                                 AS grnQty,
                        g.AccQty                                  AS accQty,
                        g.RejQty                                  AS rejQty,
                        l.Qty - ISNULL(SUM(g.BillQty) OVER (
                                PARTITION BY l.TransSlNo
                                ORDER BY g.DocDate, g.DocNo, g.TransSlNo
                                ROWS UNBOUNDED PRECEDING), 0) AS balQty,
                        CASE WHEN RTRIM(l.DocStatus) = 'Y' OR l.Qty - l.RecdQty <= 0
                             THEN 'Closed' ELSE 'Open' END        AS type,
                        l.DocDate AS sortDate, l.DocNo AS sortNo, l.SlNo AS sortSl,
                        g.DocDate AS sortGDate, g.DocNo AS sortGNo, g.TransSlNo AS sortGSl
                    FROM lines l
                    LEFT JOIN grn g ON g.POSeries = l.DocSeries AND g.POYear = l.DocYear
                                   AND g.PONo = l.DocNo AND g.SlNo = l.SlNo
                ) x
                WHERE @EType = 'All' OR x.type = @EType
                ORDER BY x.sortDate, x.sortNo, x.sortSl, x.sortGDate, x.sortGNo, x.sortGSl");

        // ============================================================
        // RGP STATUS — approved RGP (RGPType 'RGP') line -> RGP inward lines
        //   One row per gate pass line x inward line (outer join).
        //   balQty = sent qty less the qty received back so far (running).
        //   Open   = material still out on the line; Closed = all back.
        //   Customer = supplier name.
        // ============================================================

        [HttpGet("api/RGPStatusReport/AllReports")]
        public Task<IActionResult> RGPStatusReport([FromQuery] StatusReportFilter f) =>
            RunReport(f, new[] { "All", "Open", "Closed" }, @"
                WITH ret AS (
                    SELECT i.LinkNo, im.MTransNo AS InwTransNo, im.DocNo, im.DocDate,
                           im.RefNo, im.RefDate, im.DCNo, im.DCDate,
                           i.TransSlNo, ISNULL(i.Qty, 0) AS Qty
                    FROM dbo.RGPInwDtl i
                    JOIN dbo.RGPInwMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                    WHERE i.DeleteStatus = 'N' AND i.LinkNo IS NOT NULL AND im.SubscID = @SubscID
                ),
                lines AS (
                    SELECT m.MTransNo, m.DocSeries, m.DocNo, m.DocDate, m.DueDate, m.VendorName,
                           d.TransSlNo, d.SlNo, d.PartCode, " + LineDesc + @" AS ItemDesc,
                           d.Unit, ISNULL(d.Qty, 0) AS Qty, d.ItemRemarks,
                           ISNULL(t.RetQty, 0) AS RetQty
                    FROM dbo.RGPMst m
                    JOIN dbo.RGPDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    OUTER APPLY (SELECT SUM(r.Qty) AS RetQty FROM ret r WHERE r.LinkNo = d.TransSlNo) t
                    WHERE m.SubscID = @SubscID
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@Customer = '' OR m.VendorName = @Customer)
                      AND (@DocNo = 0 OR m.DocNo = @DocNo)
                      AND m.RGPType = 'RGP' AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                )
                SELECT * FROM (
                    SELECT
                        CAST(l.TransSlNo AS varchar(12)) + '-' + ISNULL(CAST(r.TransSlNo AS varchar(12)), '0') AS rowId,
                        l.MTransNo                                AS mTransNo,
                        RTRIM(ISNULL(l.DocSeries, ''))            AS docSeries,
                        CAST(l.DocNo AS varchar(20))              AS rgpNo,
                        CONVERT(char(10), l.DocDate, 23)          AS rgpDate,
                        CONVERT(char(10), l.DueDate, 23)          AS dueDate,
                        ISNULL(l.VendorName, '')                  AS supplier,
                        ISNULL(l.PartCode, '')                    AS partCode,
                        l.ItemDesc                                AS itemDesc,
                        ISNULL(l.Unit, '')                        AS unit,
                        l.Qty                                     AS rgpQty,
                        r.InwTransNo                              AS inwTransNo,
                        ISNULL(CAST(r.DocNo AS varchar(20)), '')  AS inwNo,
                        CONVERT(char(10), r.DocDate, 23)          AS inwDate,
                        ISNULL(r.RefNo, '')                       AS refNo,
                        CONVERT(char(10), r.RefDate, 23)          AS refDate,
                        ISNULL(r.DCNo, '')                        AS dcNo,
                        CONVERT(char(10), r.DCDate, 23)           AS dcDate,
                        r.Qty                                     AS inwQty,
                        l.Qty - ISNULL(SUM(r.Qty) OVER (
                                PARTITION BY l.TransSlNo
                                ORDER BY r.DocDate, r.DocNo, r.TransSlNo
                                ROWS UNBOUNDED PRECEDING), 0) AS balQty,
                        CASE WHEN l.Qty - l.RetQty > 0 THEN 'Open' ELSE 'Closed' END AS type,
                        l.DocDate AS sortDate, l.DocNo AS sortNo, l.SlNo AS sortSl,
                        r.DocDate AS sortIDate, r.DocNo AS sortINo, r.TransSlNo AS sortISl
                    FROM lines l
                    LEFT JOIN ret r ON r.LinkNo = l.TransSlNo
                ) x
                WHERE @EType = 'All' OR x.type = @EType
                ORDER BY x.sortDate, x.sortNo, x.sortSl, x.sortIDate, x.sortINo, x.sortISl");

        // ============================================================
        // SHARED RUNNER
        // ============================================================

        /// <summary>Validates the report filters, then runs the report query.</summary>
        private async Task<IActionResult> RunReport(StatusReportFilter f, string[] types, string sql)
        {
            if (f.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (f.FromDate == null || f.ToDate == null)
                return BadRequestResult("From Date and To Date are required.");
            if (f.FromDate.Value.Date > f.ToDate.Value.Date)
                return BadRequestResult("From Date cannot be after To Date.");

            var wanted = (f.EType ?? "").Trim();
            if (wanted == "") wanted = "All";
            var type = types.FirstOrDefault(t => string.Equals(t, wanted, StringComparison.OrdinalIgnoreCase));
            if (type == null)
                return BadRequestResult($"Type must be one of: {string.Join(", ", types)}.");

            var docNo = f.PONo ?? f.RGPNo ?? 0;
            if (docNo < 0)
                return BadRequestResult("Document number cannot be negative.");

            return await RunQuery(sql,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = f.SubscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = f.FromDate.Value.Date },
                new SqlParameter("@ToDate", SqlDbType.Date) { Value = f.ToDate.Value.Date },
                new SqlParameter("@Series", SqlDbType.VarChar, 10) { Value = (f.PValue ?? "").Trim() },
                new SqlParameter("@Customer", SqlDbType.VarChar, 100) { Value = (f.Customer ?? "").Trim() },
                new SqlParameter("@EType", SqlDbType.VarChar, 20) { Value = type },
                new SqlParameter("@DocNo", SqlDbType.Int) { Value = docNo });
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
