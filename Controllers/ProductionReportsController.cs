using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Production Reports — the read-only registers behind EMSSoft/Modules/ProductionReports:
    /// Job Card Report, Job Card Inspection Report and MRN Report.
    ///
    /// Each answers GET api/{Report}/AllReports with the legacy filter names
    /// (EType = report type, FromDate, ToDate, SubscID) plus
    ///   PartNo  = PartMaster.MTransNo of the finished part (job card reports); 0 = every part
    ///   MRNType = All / Production MRN / General MRN (or PROD / GEN)      (MRN report)
    ///   PValue  = DocSeries; blank = every series                          (MRN report)
    /// and returns a flat row list whose camelCase keys are exactly the column keys of the
    /// matching page. The row shape depends on the type — see each report below.
    ///
    /// Shared rules:
    ///   - Job card progress, status and inspection / RM roll-ups are read from
    ///     VW_JobCardStep and VW_JobCardSummary (SQL 53) — never re-derived here.
    ///   - OA = the live current revision (DeleteStatus 'N', DocAmdStatus 'N') of
    ///     OAMst DocYear/DocNo = JobCardMst OAYear/OANo; VendorName is the customer.
    ///   - MRN issued qty = Σ live Store Issue lines drawing on the MRN line
    ///     (StoreIssueDtl.LinkNo = MRNDtl.TransSlNo, StoreIssueMst.TargetTransNo =
    ///     MRNMst.MTransNo) — the rule VW_StoreIssueMRNLine (SQL 44-46) uses.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    ///
    /// The SQL lives here rather than in stored procedures so the reports need no
    /// DB deployment beyond the production tables themselves.
    /// </summary>
    public class ProductionReportsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ProductionReportsController(EMSContext context) => db = context;

        /// <summary>Query-string filters shared by every production report.</summary>
        public class ProductionReportFilter
        {
            public string? EType { get; set; }
            public DateTime? FromDate { get; set; }
            public DateTime? ToDate { get; set; }
            public int SubscID { get; set; }
            /// <summary>Finished part (PartMaster.MTransNo); 0 / blank = every part.</summary>
            public int? PartNo { get; set; }
            /// <summary>MRN type: All / Production MRN / General MRN (PROD / GEN also accepted).</summary>
            public string? MRNType { get; set; }
            /// <summary>DocSeries (MRN report); blank = every series.</summary>
            public string? PValue { get; set; }
        }

        // OA reference as the production screens print it: "2627-15 / 2" (year-no / line).
        private const string OARef = @"
            CASE WHEN ISNULL(j.OANo, 0) > 0
                 THEN CAST(j.OAYear AS varchar(10)) + '-' + CAST(j.OANo AS varchar(20))
                      + ' / ' + CAST(j.OAID AS varchar(10))
                 ELSE '' END";

        /// <summary>
        /// CTE "jc": every live job card of the subscription whose JobDate falls in the
        /// period (and of the chosen part), with its part, route, OA customer, raw
        /// material and its VW_JobCardSummary roll-up.
        /// </summary>
        private const string JobCardCte = @"
                jc AS (
                    SELECT j.MTransNo, j.JobCardNo, j.JobDate, j.DocYear, j.DocNo,
                           j.OAYear, j.OANo, j.OAID, j.PartNo, j.Grade, j.Qty,
                           j.RMGrade, j.RMQty, j.RMUnit, j.Operator, j.Shift, j.Remarks,
                           " + OARef + @"                      AS OARef,
                           ISNULL(oa.VendorName, '')            AS Customer,
                           ISNULL(p.PartCode, '')               AS PartCode,
                           ISNULL(p.PartName, '')               AS PartName,
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
                    LEFT JOIN dbo.PartMaster p        ON p.MTransNo  = j.PartNo
                    LEFT JOIN dbo.PartMaster rm       ON rm.MTransNo = j.RMPartNo
                    LEFT JOIN dbo.RoutingChartMst r   ON r.MTransNo  = j.RouteNo
                    OUTER APPLY (
                        SELECT TOP (1) o.VendorName
                        FROM dbo.OAMst o
                        WHERE o.SubscID = j.SubscID AND o.DocYear = j.OAYear AND o.DocNo = j.OANo
                          AND o.DeleteStatus = 'N' AND o.DocAmdStatus = 'N'
                        ORDER BY o.MTransNo DESC
                    ) oa
                    WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N'
                      AND j.JobDate BETWEEN @FromDate AND @ToDate
                      AND (@PartNo = 0 OR j.PartNo = @PartNo)
                )";

        // ============================================================
        // JOB CARD REPORT — JobCardMst + VW_JobCardSummary / VW_JobCardStep
        //   All / Pending / Open / In Progress / Completed
        //       one row per live job card (JobDate in the period) with its
        //       progress, RM issue and inspection roll-up.
        //       Pending = Open + In Progress with balance qty still to make.
        //   Operation Wise
        //       one row per job card x route step (VW_JobCardStep), so the
        //       steps not yet started are visible too.
        //   Production Entries
        //       one row per live JobCardEntry whose EntryDate falls in the
        //       period (a production register is read by the day the work was
        //       done), on a live job card. Run (min) = End - Start, wrapping
        //       past midnight; Setting / Idle are shown as recorded.
        // ============================================================

        
        // ============================================================
        // JOB CARD INSPECTION REPORT — JobCardInspection + VW_JobCardSummary
        //   All                one row per live inspection whose InspDate falls in
        //                      the period, on a live job card.
        //   Job Card Wise      one row per live job card whose JobDate falls in the
        //                      period: what reached the inspection step, what was
        //                      inspected, the OK / rework / rejected split and what
        //                      still awaits inspection (VW_JobCardSummary).
        //   Pending Inspection Job Card Wise limited to AwaitingInspectionQty > 0.
        //   OK % = OK / Inspected; Rejection % = Rejected / Inspected (0 when nothing
        //   was inspected).
        // ============================================================

        private static readonly string[] InspectionTypes = { "All", "Job Card Wise", "Pending Inspection" };

        [HttpGet("api/JobCardInspectionReport/AllReports")]
        public Task<IActionResult> JobCardInspectionReport([FromQuery] ProductionReportFilter f) =>
            RunReport(f, InspectionTypes, type => type == "All" ? @"
                SELECT
                    i.MTransNo                                AS rowId,
                    i.MTransNo                                AS mTransNo,
                    i.InspectionNo                            AS inspectionNo,
                    CONVERT(char(10), i.InspDate, 23)         AS inspDate,
                    j.JobCardNo                               AS jobCardNo,
                    CONVERT(char(10), j.JobDate, 23)          AS jobDate,
                    ISNULL(p.PartCode, '')                    AS partCode,
                    ISNULL(p.PartName, '')                    AS partName,
                    ISNULL(j.Grade, '')                       AS grade,
                    ISNULL(i.InspectedBy, '')                 AS inspectedBy,
                    i.InspectedQty                            AS inspectedQty,
                    i.OKQty                                   AS okQty,
                    i.ReworkQty                               AS reworkQty,
                    i.RejectedQty                             AS rejectedQty,
                    CAST(CASE WHEN i.InspectedQty > 0 THEN i.OKQty * 100.0 / i.InspectedQty ELSE 0 END
                         AS numeric(9, 2))                    AS okPct,
                    ISNULL(i.Remarks, '')                     AS remarks,
                    i.InspDate AS sortDate, i.DocYear AS sortYear, i.DocNo AS sortNo
                FROM dbo.JobCardInspection i
                JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'
                LEFT JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo
                WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N'
                  AND i.InspDate BETWEEN @FromDate AND @ToDate
                  AND (@PartNo = 0 OR j.PartNo = @PartNo)
                ORDER BY sortDate, sortYear, sortNo" : @"
                WITH " + JobCardCte + @"
                SELECT
                    c.MTransNo                                AS rowId,
                    c.MTransNo                                AS mTransNo,
                    c.JobCardNo                               AS jobCardNo,
                    CONVERT(char(10), c.JobDate, 23)          AS jobDate,
                    c.OARef                                   AS oaNo,
                    c.Customer                                AS customer,
                    c.PartCode                                AS partCode,
                    c.PartName                                AS partName,
                    ISNULL(c.Grade, '')                       AS grade,
                    c.Qty                                     AS qty,
                    c.InspectionStep                          AS inspectionStep,
                    c.InspectionEligibleQty                   AS eligibleQty,
                    c.InspectedQty                            AS inspectedQty,
                    c.OKQty                                   AS okQty,
                    c.ReworkQty                               AS reworkQty,
                    c.RejectedQty                             AS rejectedQty,
                    c.AwaitingInspectionQty                   AS awaitingQty,
                    CAST(CASE WHEN c.InspectedQty > 0 THEN c.RejectedQty * 100.0 / c.InspectedQty ELSE 0 END
                         AS numeric(9, 2))                    AS rejectionPct,
                    c.InspectionCount                         AS inspectionCount,
                    CONVERT(char(10), c.LastInspDate, 23)     AS lastInspDate,
                    c.JobStatus                               AS jobStatus,
                    c.JobDate AS sortDate, c.DocYear AS sortYear, c.DocNo AS sortNo
                FROM jc c
                WHERE @EType = 'Job Card Wise' OR c.AwaitingInspectionQty > 0
                ORDER BY sortDate, sortYear, sortNo");

        // ============================================================
        // MRN REPORT — MRNMst + MRNDtl, every MRN type
        //   All      one row per live MRN line (DocDate in the period).
        //   Pending  approved (LockStatus 'Y') lines with a balance still to issue.
        //   Issued   approved lines issued in full.
        //   Summary  one row per live MRN with its line count and qty totals.
        //   Approval: Y Approved, S Submitted, R Rejected, otherwise Draft — only an
        //   approved MRN can be issued against (Store Issue reads LockStatus 'Y').
        //   Line status: Not Approved / Pending / Partly Issued / Issued.
        //   Balance = MRN qty - issued, never below 0.
        // ============================================================

        private static readonly string[] MRNTypes = { "All", "Pending", "Issued", "Summary" };

        private const string MRNLinesCte = @"
                ln AS (
                    SELECT m.MTransNo, m.MRNType, m.DocSeries, m.DocYear, m.DocNo, m.DocDate,
                           m.Requiredby, m.Department, m.LockStatus,
                           d.TransSlNo, d.SlNo, d.PartNo, d.OAYear, d.OANo, d.OAID, d.ItemRemarks,
                           ISNULL(p.PartCode, '')                                            AS PartCode,
                           COALESCE(NULLIF(LTRIM(RTRIM(d.Description)), ''), p.PartName, '') AS PartName,
                           ISNULL(LTRIM(RTRIM(d.Grade)), '')                                 AS Grade,
                           COALESCE(NULLIF(LTRIM(RTRIM(d.Unit)), ''), p.PrimaryUnit, '')     AS Unit,
                           ISNULL(d.Qty, 0)                                                  AS Qty,
                           ISNULL(i.IssuedQty, 0)                                            AS IssuedQty,
                           CASE WHEN ISNULL(d.Qty, 0) - ISNULL(i.IssuedQty, 0) > 0
                                THEN ISNULL(d.Qty, 0) - ISNULL(i.IssuedQty, 0) ELSE 0 END    AS BalQty,
                           CASE m.MRNType WHEN 'PROD' THEN 'Production MRN'
                                          WHEN 'GEN'  THEN 'General MRN'
                                          ELSE ISNULL(m.MRNType, '') END                     AS TypeName,
                           CASE m.LockStatus WHEN 'Y' THEN 'Approved'
                                             WHEN 'S' THEN 'Submitted'
                                             WHEN 'R' THEN 'Rejected'
                                             ELSE 'Draft' END                                AS Approval
                    FROM dbo.MRNMst m
                    JOIN dbo.MRNDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    OUTER APPLY (
                        SELECT SUM(sd.Qty) AS IssuedQty
                        FROM dbo.StoreIssueDtl sd
                        JOIN dbo.StoreIssueMst sm ON sm.MTransNo = sd.MTransNo
                        WHERE sd.LinkNo = d.TransSlNo
                          AND sm.TargetTransNo = m.MTransNo
                          AND ISNULL(sm.TargetType, '') <> 'Job Card'   -- SQL 56: job card ids can equal MRN ids
                          AND sd.DeleteStatus = 'N' AND sm.DeleteStatus = 'N'
                    ) i
                    WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                      AND m.DocDate BETWEEN @FromDate AND @ToDate
                      AND (@Series = '' OR RTRIM(m.DocSeries) = @Series)
                      AND (@MRNType = '' OR m.MRNType = @MRNType)
                )";

        [HttpGet("api/MRNReport/AllReports")]
        public Task<IActionResult> MRNReport([FromQuery] ProductionReportFilter f)
        {
            var mrnType = (f.MRNType ?? "").Trim().ToUpperInvariant() switch
            {
                "" or "ALL" => "",
                "PRODUCTION MRN" or "PROD" => "PROD",
                "GENERAL MRN" or "GEN" => "GEN",
                _ => null
            };
            if (mrnType == null)
                return Task.FromResult(BadRequestResult("MRN Type must be one of: All, Production MRN, General MRN."));

            return RunReport(f, MRNTypes, type => type == "Summary" ? @"
                WITH " + MRNLinesCte + @",
                doc AS (
                    SELECT MTransNo, MRNType, TypeName, DocSeries, DocYear, DocNo, DocDate,
                           Requiredby, Department, LockStatus, Approval,
                           COUNT(*) AS LineCount, SUM(Qty) AS Qty, SUM(IssuedQty) AS IssuedQty,
                           SUM(BalQty) AS BalQty,
                           SUM(CASE WHEN IssuedQty > 0 THEN 1 ELSE 0 END) AS IssuedLines
                    FROM ln
                    GROUP BY MTransNo, MRNType, TypeName, DocSeries, DocYear, DocNo, DocDate,
                             Requiredby, Department, LockStatus, Approval
                )
                SELECT
                    d.MTransNo                                AS rowId,
                    d.MTransNo                                AS mTransNo,
                    d.TypeName                                AS mrnType,
                    RTRIM(ISNULL(d.DocSeries, ''))            AS docSeries,
                    CAST(d.DocNo AS varchar(20))              AS mrnNo,
                    CONVERT(char(10), d.DocDate, 23)          AS mrnDate,
                    ISNULL(d.Requiredby, '')                  AS requiredBy,
                    ISNULL(d.Department, '')                  AS department,
                    d.LineCount                               AS lineCount,
                    d.Qty                                     AS mrnQty,
                    d.IssuedQty                               AS issuedQty,
                    d.BalQty                                  AS balanceQty,
                    d.Approval                                AS approval,
                    CASE WHEN d.LockStatus <> 'Y' THEN 'Not Approved'
                         WHEN d.BalQty <= 0       THEN 'Issued'
                         WHEN d.IssuedLines > 0   THEN 'Partly Issued'
                         ELSE 'Pending' END                   AS status,
                    d.DocDate AS sortDate, d.MRNType AS sortType, d.DocNo AS sortNo
                FROM doc d
                ORDER BY sortDate, sortType, sortNo" : @"
                WITH " + MRNLinesCte + @"
                SELECT * FROM (
                    SELECT
                        l.TransSlNo                               AS rowId,
                        l.MTransNo                                AS mTransNo,
                        l.TypeName                                AS mrnType,
                        RTRIM(ISNULL(l.DocSeries, ''))            AS docSeries,
                        CAST(l.DocNo AS varchar(20))              AS mrnNo,
                        CONVERT(char(10), l.DocDate, 23)          AS mrnDate,
                        ISNULL(l.Requiredby, '')                  AS requiredBy,
                        ISNULL(l.Department, '')                  AS department,
                        l.PartCode                                AS partCode,
                        l.PartName                                AS partName,
                        l.Grade                                   AS grade,
                        CASE WHEN ISNULL(l.OANo, 0) > 0
                             THEN CAST(l.OAYear AS varchar(10)) + '-' + CAST(l.OANo AS varchar(20))
                                  + CASE WHEN ISNULL(l.OAID, 0) > 0 THEN ' / ' + CAST(l.OAID AS varchar(10)) ELSE '' END
                             ELSE '' END                          AS oaNo,
                        l.Unit                                    AS unit,
                        l.Qty                                     AS mrnQty,
                        l.IssuedQty                               AS issuedQty,
                        l.BalQty                                  AS balanceQty,
                        l.Approval                                AS approval,
                        CASE WHEN l.LockStatus <> 'Y' THEN 'Not Approved'
                             WHEN l.BalQty <= 0       THEN 'Issued'
                             WHEN l.IssuedQty > 0     THEN 'Partly Issued'
                             ELSE 'Pending' END                   AS status,
                        ISNULL(l.ItemRemarks, '')                 AS remarks,
                        l.DocDate AS sortDate, l.MRNType AS sortType, l.DocNo AS sortNo, l.SlNo AS sortSl
                    FROM ln l
                ) x
                WHERE @EType = 'All'
                   OR (@EType = 'Pending' AND x.status IN ('Pending', 'Partly Issued'))
                   OR (@EType = 'Issued'  AND x.status = 'Issued')
                ORDER BY x.sortDate, x.sortType, x.sortNo, x.sortSl",
                new SqlParameter("@MRNType", SqlDbType.VarChar, 30) { Value = mrnType },
                new SqlParameter("@Series", SqlDbType.VarChar, 10) { Value = (f.PValue ?? "").Trim() });
        }

        // ============================================================
        // SHARED RUNNER
        // ============================================================

        /// <summary>
        /// Validates the shared filters (subscription, period, part, type), then runs
        /// the query built for the resolved type. Extra parameters are passed through.
        /// </summary>
        private async Task<IActionResult> RunReport(ProductionReportFilter f, string[] types,
            Func<string, string> buildSql, params SqlParameter[] extra)
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

            var partNo = f.PartNo ?? 0;
            if (partNo < 0)
                return BadRequestResult("Invalid Part.");

            var parameters = new List<SqlParameter>
            {
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = f.SubscID },
                new SqlParameter("@FromDate", SqlDbType.Date) { Value = f.FromDate.Value.Date },
                new SqlParameter("@ToDate", SqlDbType.Date) { Value = f.ToDate.Value.Date },
                new SqlParameter("@EType", SqlDbType.VarChar, 30) { Value = type },
                new SqlParameter("@PartNo", SqlDbType.Int) { Value = partNo }
            };
            parameters.AddRange(extra);

            return await RunQuery(buildSql(type), parameters.ToArray());
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
