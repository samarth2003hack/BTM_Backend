using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// OA Status Report — two-step drill-down behind
    /// EMSSoft/Modules/SalesReports/OAStatusReport.html.
    ///
    ///   GET api/OAStatusReport/GetOAList/{subscID}
    ///       Live OAs (current revision, not deleted) for the OA picker.
    ///   GET api/OAStatusReport/ProductionStatus?OAYear=&OANo=&SubscID=
    ///       One row per OA line; stages CONVBOM, ISSUE, JC, PROD, INSP, PACK, INV.
    ///       This is the only endpoint OAStatusReport.html calls now.
    ///   GET api/OAStatusReport/MaterialStatus?OAYear=&OANo=&SubscID=
    ///       One row per OA line x ConvBOM raw-material line; stages PR, PO, GRN, QA, ISSUE.
    ///       NOT used by the current page (the Material report was removed from the design);
    ///       kept so it can be brought back without rewriting. Safe to delete.
    ///
    /// HOW AN OA LINE IS LINKED (taken from the project SQL — do not change):
    ///   OAYear = OAMst.DocYear,  OANo = OAMst.DocNo,  OAID = OADtl.SlNo (the LINE NUMBER
    ///   inside the OA, not OADtl.TransSlNo). So every downstream lookup matches the
    ///   three columns TOGETHER; matching OAID alone would mix line 1 of every OA.
    ///   Tables carrying that triple: MRNDtl, StoreIssueDtl, PurReqDtl, PODtl, GRNDtl,
    ///   JobCardMst. PackingSlipMst carries OAYear/OANo and PackingSlipDtl carries OAID.
    ///   The one exception is the invoice: STxnDtl.LinkNo = OADtl.TransSlNo.
    ///
    /// BOM: ConvBOM is the only BOM table (FPartNo = finished part, PartNo = raw material
    /// or bought-out part, one level). The material report therefore lists, for each
    /// OA line, the ConvBOM parts and follows each part through PR / PO / GRN / QA / Issue
    /// for that OA line.
    ///
    /// Stage object: { status, docs:[{no,date,qty,note}], qty, bal }
    ///   status Done = qty >= required; Partial = 0 < qty < required; Pending = 0; NA = no BOM.
    /// </summary>
    [Route("api/[controller]")]
    public class OAStatusReportController : EmsControllerBase
    {
        private readonly EMSContext db;
        public OAStatusReportController(EMSContext context) => db = context;

        // ============================================================
        // STEP 1 — OA list for the picker
        // ============================================================

        [HttpGet("GetOAList/{subscID}")]
        public async Task<IActionResult> GetOAList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            const string sql = @"
                SELECT TOP (500)
                    m.DocYear                                   AS oaYear,
                    m.DocNo                                     AS oaNo,
                    RTRIM(ISNULL(m.DocSeries, ''))
                        + CASE WHEN ISNULL(m.DocAmdNo, 0) > 0
                               THEN ' (A' + CAST(m.DocAmdNo AS varchar(5)) + ')'
                               ELSE '' END
                        + ' / ' + CAST(m.DocNo AS varchar(20))  AS oaLabel,
                    CONVERT(char(10), m.DocDate, 23)            AS oaDate,
                    ISNULL(m.VendorName, '')                    AS customerName,
                    ISNULL(m.RefNo, '')                         AS poNo,
                    ISNULL(CONVERT(char(10), m.RefDate, 23), '') AS poDate,
                    ISNULL(CONVERT(char(10), m.DueDate, 23), '') AS dueDate
                FROM dbo.OAMst m
                WHERE m.SubscID = @SubscID
                  AND m.DeleteStatus = 'N'
                  AND m.DocAmdStatus = 'N'
                ORDER BY m.DocYear DESC, m.DocNo DESC";

            try
            {
                var con = await OpenAsync();
                var rows = await QueryRows(con, sql, P("@SubscID", subscID));
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // STEP 2a — Production status of one OA
        // ============================================================

        [HttpGet("ProductionStatus")]
        public async Task<IActionResult> ProductionStatus(
            [FromQuery] int oaYear, [FromQuery] int oaNo, [FromQuery] int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (oaYear <= 0 || oaNo <= 0) return BadRequestResult("Invalid OA Year / No.");

            try
            {
                var con  = await OpenAsync();
                var head = await QueryOAHead(con, oaYear, oaNo, subscID);
                if (head == null) return NotFoundResult("OA not found.");

                var lines = await QueryRows(con, @"
                    SELECT CAST(d.SlNo AS int)                          AS oaId,
                           d.TransSlNo                                  AS transSlNo,
                           d.PartNo                                     AS partNo,
                           ISNULL(p.PartCode, '')                       AS partCode,
                           COALESCE(NULLIF(LTRIM(RTRIM(d.PartDescription)), ''),
                                    p.PartName, '')                     AS partDescription,
                           ISNULL(d.MocDescription, '')                 AS moc,
                           ISNULL(d.Qty, 0)                             AS oaQty,
                           COALESCE(NULLIF(LTRIM(RTRIM(d.Unit)), ''),
                                    p.PrimaryUnit, '')                  AS unit
                    FROM dbo.OADtl d
                    JOIN dbo.OAMst m ON m.MTransNo = d.MTransNo
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    WHERE m.SubscID = @SubscID AND m.DocYear = @Year AND m.DocNo = @No
                      AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND d.DeleteStatus = 'N'
                    ORDER BY d.SlNo",
                    P("@SubscID", subscID), P("@Year", oaYear), P("@No", oaNo));

                foreach (var line in lines)
                {
                    var k = new OAKey(subscID, oaYear, oaNo,
                        Convert.ToInt32(line["oaId"]), Convert.ToInt32(line["transSlNo"]),
                        Convert.ToInt32(line["partNo"] ?? 0), Dec(line["oaQty"]));

                    line["stages"] = new Dictionary<string, object>
                    {
                        ["CONVBOM"] = await StageConvBOM(con, k),
                        ["ISSUE"]   = await StageIssue(con, k),
                        ["JC"]      = await StageJC(con, k),
                        ["PROD"]    = await StageProd(con, k),
                        ["INSP"]    = await StageInsp(con, k),
                        ["PACK"]    = await StagePack(con, k),
                        ["INV"]     = await StageInv(con, k),
                    };
                    line.Remove("transSlNo");
                    line.Remove("partNo");
                }

                var stageDefs = new[]
                {
                    ("CONVBOM", "ConvBOM"), ("ISSUE", "Store Issue"),
                    ("JC", "Job Card"), ("PROD", "Production"), ("INSP", "Inspection"),
                    ("PACK", "Packing Slip"), ("INV", "Invoice")
                };

                return OkResult(new
                {
                    ok     = true,
                    head,
                    stages = stageDefs.Select(s => new { key = s.Item1, title = s.Item2 }).ToList(),
                    rows   = lines
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // STEP 2b — Material status of one OA
        // ============================================================

        [HttpGet("MaterialStatus")]
        public async Task<IActionResult> MaterialStatus(
            [FromQuery] int oaYear, [FromQuery] int oaNo, [FromQuery] int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (oaYear <= 0 || oaNo <= 0) return BadRequestResult("Invalid OA Year / No.");

            try
            {
                var con  = await OpenAsync();
                var head = await QueryOAHead(con, oaYear, oaNo, subscID);
                if (head == null) return NotFoundResult("OA not found.");

                // One row per OA line x ConvBOM line (FPartNo = the OA part).
                var rows = await QueryRows(con, @"
                    SELECT CAST(d.SlNo AS int)                           AS oaId,
                           d.TransSlNo                                   AS transSlNo,
                           ISNULL(p.PartCode, '')
                             + CASE WHEN COALESCE(NULLIF(LTRIM(RTRIM(d.PartDescription)), ''), p.PartName, '') = ''
                                    THEN ''
                                    ELSE ' - ' + COALESCE(NULLIF(LTRIM(RTRIM(d.PartDescription)), ''), p.PartName)
                               END                                       AS oaPart,
                           ISNULL(d.Qty, 0)                              AS oaQty,
                           ISNULL(b.MTransNo, 0)                         AS bomId,
                           CASE WHEN b.MTransNo IS NULL THEN ''
                                ELSE 'BOM-' + CAST(b.MTransNo AS varchar(20)) END AS bomNo,
                           CASE UPPER(ISNULL(rp.PartType, ''))
                                WHEN 'RAW MATERIAL' THEN 'Raw Material'
                                WHEN 'BOUGHTOUT'    THEN 'Bought-out'
                                WHEN 'COMPONENT'    THEN 'Component'
                                WHEN 'CONSUMABLE'   THEN 'Consumable'
                                WHEN 'DIE'          THEN 'Die'
                                ELSE ISNULL(rp.PartType, '') END         AS mtrlType,
                           ISNULL(rp.PartCode, '')                       AS partCode,
                           ISNULL(rp.PartName, '')                       AS partDescription,
                           ISNULL(b.Grade, '')                           AS grade,
                           ISNULL(b.Qty, 0)                              AS bomQty,
                           ISNULL(b.Qty, 0) * ISNULL(d.Qty, 0)           AS reqQty,
                           'same part'                                   AS rmPart,
                           ISNULL(b.Qty, 0) * ISNULL(d.Qty, 0)           AS reqRMQty,
                           COALESCE(NULLIF(LTRIM(RTRIM(b.Unit)), ''),
                                    rp.PrimaryUnit, '')                  AS rmUnit,
                           ISNULL(b.PartNo, 0)                           AS rmPartNo
                    FROM dbo.OADtl d
                    JOIN dbo.OAMst m ON m.MTransNo = d.MTransNo
                    LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                    LEFT JOIN dbo.ConvBOM b    ON b.FPartNo = d.PartNo AND b.SubscID = @SubscID
                                              AND b.DeleteStatus = 'N' AND b.LockStatus = 'Y'
                    LEFT JOIN dbo.PartMaster rp ON rp.MTransNo = b.PartNo
                    WHERE m.SubscID = @SubscID AND m.DocYear = @Year AND m.DocNo = @No
                      AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                      AND d.DeleteStatus = 'N'
                    ORDER BY d.SlNo, b.MTransNo",
                    P("@SubscID", subscID), P("@Year", oaYear), P("@No", oaNo));

                var noBOM = new List<string>();
                var stageKeys = new[] { "PR", "PO", "GRN", "QA", "ISSUE" };

                foreach (var row in rows)
                {
                    var bomId    = Convert.ToInt32(row["bomId"]);
                    var rmPartNo = Convert.ToInt32(row["rmPartNo"]);
                    var req      = Dec(row["reqRMQty"]);
                    var k = new OAKey(subscID, oaYear, oaNo,
                        Convert.ToInt32(row["oaId"]), Convert.ToInt32(row["transSlNo"]),
                        rmPartNo, req);

                    if (bomId == 0)
                    {
                        var label = row["oaPart"]?.ToString() ?? "";
                        if (label != "" && !noBOM.Contains(label)) noBOM.Add(label);
                        row["stages"] = stageKeys.ToDictionary(s => s, s => StageNA());
                    }
                    else
                    {
                        row["stages"] = new Dictionary<string, object>
                        {
                            ["PR"]    = await MtlStage(con, k, "PurReqDtl", "PurReqMst", "pd.Qty", "pm.DocAmdStatus = 'N'", ""),
                            ["PO"]    = await MtlStage(con, k, "PODtl",     "POMst",     "pd.Qty", "pm.DocAmdStatus = 'N'", ""),
                            ["GRN"]   = await MtlGRN(con, k, qaOnly: false),
                            ["QA"]    = await MtlGRN(con, k, qaOnly: true),
                            ["ISSUE"] = await MtlIssue(con, k),
                        };
                    }
                    row.Remove("transSlNo");
                    row.Remove("bomId");
                    row.Remove("rmPartNo");
                }

                var stageDefs = new[]
                {
                    ("PR", "Requisition"), ("PO", "Purchase Order"), ("GRN", "GRN"),
                    ("QA", "GRN QA"), ("ISSUE", "Store Issue")
                };

                return OkResult(new
                {
                    ok     = true,
                    head,
                    stages = stageDefs.Select(s => new { key = s.Item1, title = s.Item2 }).ToList(),
                    rows,
                    noBOM
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // PRODUCTION STAGES
        // ============================================================

        // ConvBOM exists for the OA part (active). Counts as the full OA qty.
        private async Task<object> StageConvBOM(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT 'ConvBOM-' + CAST(b.MTransNo AS varchar(20)) AS no,
                       CONVERT(char(10), b.CreatedOn, 23)         AS date,
                       CAST(@Qty AS numeric(18, 3))               AS qty,
                       ''                                         AS note
                FROM dbo.ConvBOM b
                WHERE b.SubscID = @SubscID AND b.FPartNo = @PartNo
                  AND b.DeleteStatus = 'N' AND b.LockStatus = 'Y'
                ORDER BY b.MTransNo",
                P("@SubscID", k.SubscID), P("@PartNo", k.PartNo), PD("@Qty", k.Qty));
            return MakeStage(k.Qty, docs.Count == 0 ? 0m : k.Qty, docs);
        }

        // Store Issue against those MRN lines, in FG-equivalent qty: the limiting MRN line
        // decides how much of the FG qty is fully issued (issued / MRN line qty x FG qty).
        // Document rows show the raw RM qty actually issued.
        private async Task<object> StageIssue(DbConnection con, OAKey k)
        {
            var mrnLines = await QueryRows(con, @"
                SELECT d.TransSlNo                                AS lineId,
                       d.Qty                                      AS lineQty,
                       COALESCE(NULLIF(d.ConvQty, 0), d.Qty)      AS fgQty
                FROM dbo.MRNDtl d
                JOIN dbo.MRNMst m ON m.MTransNo = d.MTransNo
                WHERE m.SubscID = @SubscID AND m.MRNType = 'PROD'
                  AND m.DeleteStatus = 'N' AND d.DeleteStatus = 'N'
                  AND d.OAYear = @Year AND d.OANo = @No AND d.OAID = @Line",
                K(k));
            if (mrnLines.Count == 0) return MakeStage(k.Qty, 0m, new());

            var issues = await QueryRows(con, @"
                SELECT RTRIM(ISNULL(sm.DocSeries, '')) + '-' + CAST(sm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), sm.DocDate, 23)                                     AS date,
                       d.TransSlNo                                                           AS lineId,
                       SUM(sd.Qty)                                                           AS qty
                FROM dbo.MRNDtl d
                JOIN dbo.MRNMst m ON m.MTransNo = d.MTransNo
                JOIN dbo.StoreIssueDtl sd ON sd.LinkNo = d.TransSlNo AND sd.DeleteStatus = 'N'
                JOIN dbo.StoreIssueMst sm ON sm.MTransNo = sd.MTransNo
                                         AND sm.TargetTransNo = m.MTransNo
                                         AND sm.DeleteStatus = 'N'
                                         AND ISNULL(sm.TargetType, '') <> 'Job Card'
                WHERE m.SubscID = @SubscID AND m.MRNType = 'PROD'
                  AND m.DeleteStatus = 'N' AND d.DeleteStatus = 'N'
                  AND d.OAYear = @Year AND d.OANo = @No AND d.OAID = @Line
                GROUP BY sm.MTransNo, sm.DocSeries, sm.DocNo, sm.DocDate, d.TransSlNo
                ORDER BY sm.DocDate, sm.DocNo",
                K(k));

            var issuedByLine = issues
                .GroupBy(r => Convert.ToInt64(r["lineId"]))
                .ToDictionary(g => g.Key, g => g.Sum(r => Dec(r["qty"])));

            decimal fraction = 1m, fgQty = 0m;
            foreach (var l in mrnLines)
            {
                var lineQty = Dec(l["lineQty"]);
                fgQty = Math.Max(fgQty, Dec(l["fgQty"]));
                issuedByLine.TryGetValue(Convert.ToInt64(l["lineId"]), out var issued);
                var f = lineQty > 0 ? Math.Min(1m, issued / lineQty) : 0m;
                fraction = Math.Min(fraction, f);
            }

            var docs = issues
                .GroupBy(r => (No: r["no"]?.ToString() ?? "", Date: r["date"]?.ToString() ?? ""))
                .Select(g => new Dictionary<string, object?>
                {
                    ["no"] = g.Key.No, ["date"] = g.Key.Date,
                    ["qty"] = g.Sum(r => Dec(r["qty"])), ["note"] = "RM qty"
                }).ToList();

            return MakeStage(k.Qty, fraction * fgQty, docs);
        }

        private async Task<object> StageJC(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT j.JobCardNo                          AS no,
                       CONVERT(char(10), j.JobDate, 23)     AS date,
                       j.Qty                                AS qty,
                       ''                                   AS note
                FROM dbo.JobCardMst j
                WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N'
                  AND j.OAYear = @Year AND j.OANo = @No AND j.OAID = @Line
                ORDER BY j.JobDate, j.MTransNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        // Output of each job card = what its LAST route step processed.
        private async Task<object> StageProd(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT j.JobCardNo                                         AS no,
                       ISNULL(CONVERT(char(10), le.LastEntryDate, 23),
                              CONVERT(char(10), j.JobDate, 23))            AS date,
                       v.ProcessedQty                                      AS qty,
                       ''                                                  AS note
                FROM dbo.JobCardMst j
                JOIN dbo.VW_JobCardStep v ON v.JobCardTransNo = j.MTransNo AND v.IsLast = 1
                OUTER APPLY (
                    SELECT MAX(x.EntryDate) AS LastEntryDate
                    FROM dbo.JobCardEntry x
                    WHERE x.MTransNo = j.MTransNo AND x.StepSlNo = v.SlNo AND x.DeleteStatus = 'N'
                ) le
                WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N'
                  AND j.OAYear = @Year AND j.OANo = @No AND j.OAID = @Line
                  AND v.ProcessedQty > 0
                ORDER BY j.JobDate, j.MTransNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        // Inspection counts the OK qty; the note carries rework / rejected.
        private async Task<object> StageInsp(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT i.InspectionNo                                       AS no,
                       CONVERT(char(10), i.InspDate, 23)                    AS date,
                       i.OKQty                                              AS qty,
                       CASE WHEN i.ReworkQty + i.RejectedQty = 0 THEN ''
                            ELSE 'Rework ' + CAST(CAST(i.ReworkQty AS float) AS varchar(20))
                               + ' / Rejected ' + CAST(CAST(i.RejectedQty AS float) AS varchar(20))
                       END                                                  AS note
                FROM dbo.JobCardInspection i
                JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo
                WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N' AND i.DeleteStatus = 'N'
                  AND j.OAYear = @Year AND j.OANo = @No AND j.OAID = @Line
                ORDER BY i.InspDate, i.MTransNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        private async Task<object> StagePack(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT RTRIM(ISNULL(pm.DocSeries, '')) + '-' + CAST(pm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), pm.DocDate, 23)                                     AS date,
                       SUM(pd.Qty)                                                           AS qty,
                       ''                                                                    AS note
                FROM dbo.PackingSlipDtl pd
                JOIN dbo.PackingSlipMst pm ON pm.MTransNo = pd.MTransNo
                WHERE pm.SubscID = @SubscID AND pm.DeleteStatus = 'N' AND pd.DeleteStatus = 'N'
                  AND pm.OAYear = @Year AND pm.OANo = @No AND pd.OAID = @Line
                GROUP BY pm.MTransNo, pm.DocSeries, pm.DocNo, pm.DocDate
                ORDER BY pm.DocDate, pm.DocNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        // Invoice lines point at the OA line by its TransSlNo (STxnDtl.LinkNo).
        private async Task<object> StageInv(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT RTRIM(ISNULL(sm.DocSeries, '')) + '-' + CAST(sm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), sm.DocDate, 23)                                     AS date,
                       SUM(sd.Qty)                                                           AS qty,
                       ''                                                                    AS note
                FROM dbo.STxnDtl sd
                JOIN dbo.STxnMst sm ON sm.MTransNo = sd.MTransNo
                WHERE sm.SubscID = @SubscID AND sm.DeleteStatus = 'N' AND sd.DeleteStatus = 'N'
                  AND sd.LinkNo = @Trans
                GROUP BY sm.MTransNo, sm.DocSeries, sm.DocNo, sm.DocDate
                ORDER BY sm.DocDate, sm.DocNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        // ============================================================
        // MATERIAL STAGES (k.PartNo = the ConvBOM raw-material / bought-out part)
        // ============================================================

        // PR and PO share a shape: lines tagged with the OA triple for this part.
        private async Task<object> MtlStage(DbConnection con, OAKey k,
            string dtlTable, string mstTable, string qtyExpr, string mstExtra, string dtlExtra)
        {
            var sql = $@"
                SELECT RTRIM(ISNULL(pm.DocSeries, '')) + '-' + CAST(pm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), pm.DocDate, 23)                                     AS date,
                       SUM({qtyExpr})                                                        AS qty,
                       ''                                                                    AS note
                FROM dbo.{dtlTable} pd
                JOIN dbo.{mstTable} pm ON pm.MTransNo = pd.MTransNo
                WHERE pm.SubscID = @SubscID AND pm.DeleteStatus = 'N' AND pd.DeleteStatus = 'N'
                  AND {mstExtra}
                  AND pd.OAYear = @Year AND pd.OANo = @No AND pd.OAID = @Line
                  AND pd.PartNo = @PartNo
                GROUP BY pm.MTransNo, pm.DocSeries, pm.DocNo, pm.DocDate
                ORDER BY pm.DocDate, pm.DocNo";
            return MakeStage(k.Qty, await QueryRows(con, sql, K(k)));
        }

        // GRN stage counts received qty; QA stage counts accepted qty of GRNs already
        // inspected (GRNType <> 'N', the same rule the GRN report uses for QA Pending).
        private async Task<object> MtlGRN(DbConnection con, OAKey k, bool qaOnly)
        {
            var qty   = qaOnly ? "gd.AccQty" : "gd.RecQty";
            var extra = qaOnly ? "AND ISNULL(gm.GRNType, 'N') <> 'N'" : "";
            var sql = $@"
                SELECT RTRIM(ISNULL(gm.DocSeries, '')) + '-' + CAST(gm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), gm.DocDate, 23)                                     AS date,
                       SUM({qty})                                                            AS qty,
                       ''                                                                    AS note
                FROM dbo.GRNDtl gd
                JOIN dbo.GRNMst gm ON gm.MTransNo = gd.MTransNo
                WHERE gm.SubscID = @SubscID AND gm.DeleteStatus = 'N' AND gd.DeleteStatus = 'N'
                  {extra}
                  AND gd.OAYear = @Year AND gd.OANo = @No AND gd.OAID = @Line
                  AND gd.PartNo = @PartNo
                GROUP BY gm.MTransNo, gm.DocSeries, gm.DocNo, gm.DocDate
                ORDER BY gm.DocDate, gm.DocNo";
            return MakeStage(k.Qty, await QueryRows(con, sql, K(k)));
        }

        // Issues of this part tagged to the OA line (against an MRN or a job card).
        private async Task<object> MtlIssue(DbConnection con, OAKey k)
        {
            var docs = await QueryRows(con, @"
                SELECT RTRIM(ISNULL(sm.DocSeries, '')) + '-' + CAST(sm.DocNo AS varchar(20)) AS no,
                       CONVERT(char(10), sm.DocDate, 23)                                     AS date,
                       SUM(sd.Qty)                                                           AS qty,
                       ''                                                                    AS note
                FROM dbo.StoreIssueDtl sd
                JOIN dbo.StoreIssueMst sm ON sm.MTransNo = sd.MTransNo
                WHERE sm.SubscID = @SubscID AND sm.DeleteStatus = 'N' AND sd.DeleteStatus = 'N'
                  AND sd.OAYear = @Year AND sd.OANo = @No AND sd.OAID = @Line
                  AND sd.PartNo = @PartNo
                GROUP BY sm.MTransNo, sm.DocSeries, sm.DocNo, sm.DocDate
                ORDER BY sm.DocDate, sm.DocNo",
                K(k));
            return MakeStage(k.Qty, docs);
        }

        // ============================================================
        // SHARED HELPERS
        // ============================================================

        /// <summary>Identifies one OA line and what is required of it.</summary>
        private sealed record OAKey(int SubscID, int Year, int No, int Line, int Trans, int PartNo, decimal Qty);

        // Fresh parameters for every query (a SqlParameter can only belong to one command).
        private static SqlParameter[] K(OAKey k) => new[]
        {
            P("@SubscID", k.SubscID), P("@Year", k.Year), P("@No", k.No),
            P("@Line", k.Line), P("@Trans", k.Trans), P("@PartNo", k.PartNo)
        };

        private async Task<DbConnection> OpenAsync()
        {
            var con = db.Database.GetDbConnection();
            if (con.State != ConnectionState.Open) await con.OpenAsync();
            return con;
        }

        private async Task<Dictionary<string, object?>?> QueryOAHead(
            DbConnection con, int oaYear, int oaNo, int subscID)
        {
            var rows = await QueryRows(con, @"
                SELECT TOP (1)
                    m.DocYear                                   AS oaYear,
                    m.DocNo                                     AS oaNo,
                    RTRIM(ISNULL(m.DocSeries, ''))
                        + CASE WHEN ISNULL(m.DocAmdNo, 0) > 0
                               THEN ' (A' + CAST(m.DocAmdNo AS varchar(5)) + ')'
                               ELSE '' END
                        + ' / ' + CAST(m.DocNo AS varchar(20))  AS oaLabel,
                    CONVERT(char(10), m.DocDate, 23)            AS oaDate,
                    ISNULL(m.VendorName, '')                    AS customerName,
                    ISNULL(m.RefNo, '')                         AS poNo,
                    ISNULL(CONVERT(char(10), m.RefDate, 23), '') AS poDate,
                    ISNULL(CONVERT(char(10), m.DueDate, 23), '') AS dueDate
                FROM dbo.OAMst m
                WHERE m.SubscID = @SubscID AND m.DocYear = @Year AND m.DocNo = @No
                  AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                ORDER BY m.MTransNo DESC",
                P("@SubscID", subscID), P("@Year", oaYear), P("@No", oaNo));
            return rows.Count > 0 ? rows[0] : null;
        }

        private static async Task<List<Dictionary<string, object?>>> QueryRows(
            DbConnection con, string sql, params SqlParameter[] p)
        {
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(p);
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

        private static decimal Dec(object? v) => v == null ? 0m : Convert.ToDecimal(v);

        // Stage from its documents: qty = sum of document qty.
        private static object MakeStage(decimal req, List<Dictionary<string, object?>> docs) =>
            MakeStage(req, docs.Sum(d => Dec(d["qty"])), docs);

        // Stage with an explicit achieved qty (documents are shown for reference only).
        private static object MakeStage(decimal req, decimal qty, List<Dictionary<string, object?>> docs)
        {
            var status = qty <= 0 ? "Pending" : (qty + 0.0005m >= req ? "Done" : "Partial");
            return new
            {
                status,
                docs = docs.Select(d => new
                {
                    no   = d["no"]?.ToString() ?? "",
                    date = d["date"]?.ToString() ?? "",
                    qty  = Dec(d["qty"]),
                    note = d["note"]?.ToString() ?? ""
                }).ToList(),
                qty,
                bal = Math.Max(0m, req - qty)
            };
        }

        private static object StageNA() =>
            new { status = "NA", docs = Array.Empty<object>(), qty = (decimal?)null, bal = (decimal?)null };

        private static SqlParameter P(string name, int val) =>
            new(name, SqlDbType.Int) { Value = val };

        private static SqlParameter PD(string name, decimal val) =>
            new(name, SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = val };
    }
}
