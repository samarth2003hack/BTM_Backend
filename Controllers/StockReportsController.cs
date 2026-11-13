using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Stock Reports — EMSSoft/Modules/PurchaseReports/StockReports.html. Read only.
    ///
    /// Own stock = VW_StoreStock (GRN, approved opening stock, production,
    /// issue, return, dispatch — SQL 67 / 72 / 73). Customer material = the
    /// dated movements behind VW_CustomerMaterialStock. Rules every report keeps:
    ///   • an approved opening-stock line is a BALANCE at the start of its As On
    ///     date: it counts in Opening, never as a receipt;
    ///   • Closing = Opening + Receipt + Return − Issue − Dispatch;
    ///   • Rate = weighted average cost of the receipts that carry a rate
    ///     (GRN accepted qty, approved opening stock) up to the report date,
    ///     else Part Master cost price; Value = Closing × Rate.
    ///
    ///   Summary?FromDate&amp;ToDate&amp;PartNo&amp;Grade&amp;Category&amp;Show&amp;SubscID
    ///   Ledger?FromDate&amp;ToDate&amp;PartNo&amp;Grade&amp;SubscID
    ///   Reorder?AsOn&amp;Category&amp;Status&amp;SubscID
    ///   Ageing?AsOn&amp;PartNo&amp;Grade&amp;Category&amp;SubscID          (FIFO)
    ///   HeatWise?AsOn&amp;PartNo&amp;Grade&amp;ShowZero&amp;SubscID
    ///   CustomerMaterial?FromDate&amp;ToDate&amp;CustomerNo&amp;SubscID
    /// Qty in the part's primary unit; qty and ₹ rounded to 2 decimals.
    /// </summary>
    [Route("api/[controller]")]
    public class StockReportsController : EmsControllerBase
    {
        private readonly EMSContext db;
        public StockReportsController(EMSContext context) => db = context;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string Today => DateTime.Today.ToString("yyyy-MM-dd", Inv);

        // ============================================================
        // Endpoints
        // ============================================================

        [HttpGet("Summary")]
        public async Task<IActionResult> Summary([FromQuery] string? FromDate, [FromQuery] string? ToDate, [FromQuery] int PartNo,
            [FromQuery] string? Grade, [FromQuery] string? Category, [FromQuery] string? Show, [FromQuery] int SubscID)
        {
            var bad = Window(FromDate, ToDate, SubscID, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var items = await Items(SubscID, PartNo, Grade, Category);
                var moves = await Moves(SubscID, to, PartNo);
                var rates = await Rates(SubscID, to);
                var nonMovingFrom = DateTime.Parse(to, Inv).AddDays(-90).ToString("yyyy-MM-dd", Inv);
                var show = string.IsNullOrWhiteSpace(Show) ? "All" : Show.Trim();
                var byKey = moves.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.ToList());

                var rows = new List<object>();
                foreach (var it in await AllKeys(items, moves, Grade, Category))
                {
                    double opening = 0, receipt = 0, ret = 0, issue = 0, dispatch = 0;
                    string last = "", lastOut = "";
                    var moved = false;
                    if (byKey.TryGetValue(it.Key, out var list))
                        foreach (var m in list)
                        {
                            if (string.CompareOrdinal(m.Date, last) > 0) last = m.Date;
                            if (m.Out > 0 && string.CompareOrdinal(m.Date, lastOut) > 0) lastOut = m.Date;
                            if (string.CompareOrdinal(m.Date, from) < 0 || m.Type == "OPENING") { opening += m.In - m.Out; continue; }
                            moved = true;
                            switch (m.Type)
                            {
                                case "GRN": case "PRODUCTION": receipt += m.In; break;
                                case "RETURN": ret += m.In; break;
                                case "SALES": dispatch += m.Out; break;
                                default: issue += m.Out; receipt += m.In; break;   // ISSUE, and anything new
                            }
                        }
                    var closing = R2(opening + receipt + ret - issue - dispatch);
                    var rate = RateOf(rates, it);
                    var keep = show switch
                    {
                        "Stock" => closing > 0,
                        "Movement" => moved,
                        "Zero" => closing == 0,
                        "Negative" => closing < 0,
                        "NonMoving" => closing > 0 && (lastOut == "" || string.CompareOrdinal(lastOut, nonMovingFrom) < 0),
                        _ => true
                    };
                    if (!keep) continue;
                    rows.Add(new
                    {
                        partNo = it.PartNo, partCode = it.PartCode, partName = it.PartName, category = it.Category,
                        grade = it.Grade, unit = it.Unit,
                        opening = R2(opening), receipt = R2(receipt), issue = R2(issue), @return = R2(ret), dispatch = R2(dispatch),
                        closing, rate, value = R2(closing * rate), lastMovement = last, lastOut
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Ledger")]
        public async Task<IActionResult> Ledger([FromQuery] string? FromDate, [FromQuery] string? ToDate, [FromQuery] int PartNo,
            [FromQuery] string? Grade, [FromQuery] int SubscID)
        {
            var bad = Window(FromDate, ToDate, SubscID, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            if (PartNo <= 0) return BadRequestResult("Select the part whose ledger you want.");
            try
            {
                var rows = await db.PmQueryRowsAsync(@"
                    SELECT  s.TxnType AS txnType, CONVERT(char(10), s.DocDate, 23) AS date,
                            LTRIM(RTRIM(ISNULL(s.Grade, ''))) AS grade,
                            ISNULL(s.InQty, 0) AS inQty, ISNULL(s.OutQty, 0) AS outQty,
                            CASE WHEN s.TxnType = 'PRODUCTION' THEN CONCAT('INSP ', s.DocYear, '/', s.DocNo)
                                 ELSE CONCAT(s.TxnType, ' ', RTRIM(ISNULL(s.DocSeries, '')), '/', s.DocNo) END AS docNo,
                            ISNULL(r.ref, '') AS reference, ISNULL(h.heat, '') AS heatNo,
                            ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName, ISNULL(p.PrimaryUnit, '') AS unit
                      FROM  dbo.VW_StoreStock s
                      LEFT  JOIN dbo.PartMaster p ON p.MTransNo = s.PartNo
                     OUTER  APPLY (SELECT CASE s.TxnType
                                WHEN 'GRN'     THEN (SELECT TOP (1) g.VendorName FROM dbo.GRNMst g WHERE g.MTransNo = s.RefTransNo)
                                WHEN 'SALES'   THEN (SELECT TOP (1) v.VendorName FROM dbo.STxnMst v WHERE v.MTransNo = s.RefTransNo)
                                WHEN 'ISSUE'   THEN (SELECT TOP (1) CONCAT(ISNULL(im.TargetType, 'Issue'), ' ',
                                                        ISNULL(jc.JobCardNo, CONCAT(im.TargetDocYear, '-', im.TargetDocNo)),
                                                        CASE WHEN ISNULL(im.Department, '') <> '' THEN ' — ' + im.Department ELSE '' END)
                                                       FROM dbo.StoreIssueMst im
                                                       LEFT JOIN dbo.JobCardMst jc ON jc.MTransNo = im.TargetTransNo AND im.TargetType = 'Job Card'
                                                      WHERE im.MTransNo = s.RefTransNo)
                                WHEN 'RETURN'  THEN (SELECT TOP (1) CONCAT(ISNULL(NULLIF(rd.ReturnType, ''), 'Return'), ' back from the shop')
                                                       FROM dbo.StoreReturnsDtl rd WHERE rd.TransSlNo = s.RefTransSlNo)
                                WHEN 'OPENING' THEN (SELECT TOP (1) CONCAT('Opening stock', ISNULL(' — ' + o.Remarks, ''))
                                                       FROM dbo.OpeningStockMst o WHERE o.MTransNo = s.RefTransNo)
                                WHEN 'PRODUCTION' THEN (SELECT TOP (1) CONCAT('Inspection OK — ', j.JobCardNo)
                                                       FROM dbo.JobCardInspection i JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo
                                                      WHERE i.MTransNo = s.RefTransNo)
                                END AS ref) r
                     OUTER  APPLY (SELECT CASE s.TxnType
                                WHEN 'ISSUE'   THEN (SELECT TOP (1) d.HeatNo FROM dbo.StoreIssueDtl d WHERE d.TransSlNo = s.RefTransSlNo)
                                WHEN 'RETURN'  THEN (SELECT TOP (1) d.HeatNo FROM dbo.StoreReturnsDtl rd
                                                       JOIN dbo.StoreIssueDtl d ON d.TransSlNo = rd.LinkNo WHERE rd.TransSlNo = s.RefTransSlNo)
                                WHEN 'OPENING' THEN (SELECT TOP (1) d.HeatNo FROM dbo.OpeningStockDtl d WHERE d.TransSlNo = s.RefTransSlNo)
                                WHEN 'GRN'     THEN (SELECT STRING_AGG(hd.HeatNo, ', ') FROM dbo.HeatNoDtl hd
                                                       JOIN dbo.HeatNoMst hm ON hm.MTransNo = hd.MTransNo AND hm.DeleteStatus = 'N'
                                                      WHERE hm.TargetTransNo = s.RefTransNo AND hd.PartNo = s.PartNo AND hd.DeleteStatus = 'N')
                                END AS heat) h
                     WHERE  s.SubscID = @SubscID AND s.PartNo = @PartNo AND s.DocDate <= @To
                       AND  (@Grade = '' OR LTRIM(RTRIM(ISNULL(s.Grade, ''))) = @Grade)
                     ORDER  BY s.DocDate,
                            CASE s.TxnType WHEN 'OPENING' THEN 0 WHEN 'GRN' THEN 1 WHEN 'PRODUCTION' THEN 1 WHEN 'RETURN' THEN 1 ELSE 2 END,
                            s.RefTransNo, s.RefTransSlNo",
                    Int("@SubscID", SubscID), Int("@PartNo", PartNo), Str("@To", to, 10), Str("@Grade", (Grade ?? "").Trim(), 50));

                var partGrades = await Items(SubscID, PartNo, Grade, null);
                var part = partGrades.FirstOrDefault();
                var grades = rows.Select(r => S(r, "grade")).Union(partGrades.Select(i => i.Grade))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
                var labels = new Dictionary<string, string>
                {
                    ["OPENING"] = "Opening Stock", ["GRN"] = "GRN", ["PRODUCTION"] = "Production",
                    ["ISSUE"] = "Issue", ["RETURN"] = "Return", ["SALES"] = "Dispatch"
                };

                var output = new List<object>();
                foreach (var g in grades)
                {
                    var mine = rows.Where(r => string.Equals(S(r, "grade"), g, StringComparison.OrdinalIgnoreCase)).ToList();
                    var first = mine.FirstOrDefault();
                    string code = first != null ? S(first, "partCode") : part?.PartCode ?? "",
                           name = first != null ? S(first, "partName") : part?.PartName ?? "",
                           unit = first != null ? S(first, "unit") : part?.Unit ?? "";
                    double bal = mine.Where(r => string.CompareOrdinal(S(r, "date"), from) < 0).Sum(r => D(r, "inQty") - D(r, "outQty"));
                    bal = R2(bal);
                    output.Add(new
                    {
                        partCode = code, partName = name, grade = g, unit, date = from, txnType = "Opening Balance",
                        docNo = "", reference = "Balance brought forward", heatNo = "",
                        inQty = (double?)null, outQty = (double?)null, balance = bal, isOpening = true
                    });
                    foreach (var r in mine.Where(r => string.CompareOrdinal(S(r, "date"), from) >= 0))
                    {
                        double i = D(r, "inQty"), o = D(r, "outQty");
                        bal = R2(bal + i - o);
                        output.Add(new
                        {
                            partCode = code, partName = name, grade = g, unit, date = S(r, "date"),
                            txnType = labels.TryGetValue(S(r, "txnType"), out var l) ? l : S(r, "txnType"),
                            docNo = S(r, "docNo"), reference = S(r, "reference"), heatNo = S(r, "heatNo"),
                            inQty = i > 0 ? R2(i) : (double?)null, outQty = o > 0 ? R2(o) : (double?)null,
                            balance = bal, isOpening = false
                        });
                    }
                }
                return OkResult(output, output.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Reorder")]
        public async Task<IActionResult> Reorder([FromQuery] string? AsOn, [FromQuery] string? Category, [FromQuery] string? Status, [FromQuery] int SubscID)
        {
            var bad = AsOnOf(AsOn, SubscID, out var asOn);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var items = await Items(SubscID, 0, null, Category);
                var bal = Balances(await Moves(SubscID, asOn, 0));
                var rates = await Rates(SubscID, asOn);
                var want = (Status ?? "").Trim();
                var rows = new List<object>();
                foreach (var it in items)
                {
                    var stock = R2(bal.TryGetValue(it.Key, out var b) ? b : 0);
                    string status;
                    if (it.Min == 0 && it.Max == 0 && it.Safety == 0) status = "No level set";
                    else if (it.Safety > 0 && stock < it.Safety) status = "Below Safety";
                    else if (it.Min > 0 && stock < it.Min) status = "Below Min";
                    else if (it.Max > 0 && stock > it.Max) status = "Above Max";
                    else status = "OK";
                    var code = status switch { "Below Safety" => "BelowSafety", "Below Min" => "BelowMin", "Above Max" => "AboveMax", "No level set" => "NoLevel", _ => "OK" };
                    if (want != "" && want != code) continue;
                    double suggested = 0;
                    if (it.Min > 0 && stock < it.Min && it.Max > 0)
                    {
                        var need = it.Max - stock;
                        suggested = it.Moq > 0 ? Math.Ceiling(need / it.Moq) * it.Moq : need;
                    }
                    var rate = RateOf(rates, it);
                    rows.Add(new
                    {
                        partCode = it.PartCode, partName = it.PartName, category = it.Category, grade = it.Grade, unit = it.Unit,
                        stock, safetyStock = it.Safety, minStock = it.Min, maxStock = it.Max, moq = it.Moq,
                        leadTimeDays = it.LeadDays, status, suggestedQty = R2(suggested), rate, suggestedValue = R2(suggested * rate)
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Ageing")]
        public async Task<IActionResult> Ageing([FromQuery] string? AsOn, [FromQuery] int PartNo, [FromQuery] string? Grade,
            [FromQuery] string? Category, [FromQuery] int SubscID)
        {
            var bad = AsOnOf(AsOn, SubscID, out var asOn);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                var items = await Items(SubscID, PartNo, Grade, Category);
                var moves = await Moves(SubscID, asOn, PartNo);
                var rates = await Rates(SubscID, asOn);
                var byKey = moves.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.ToList());
                var day = DateTime.Parse(asOn, Inv);
                var rows = new List<object>();
                foreach (var it in await AllKeys(items, moves, Grade, Category))
                {
                    if (!byKey.TryGetValue(it.Key, out var list)) continue;
                    var stock = R2(list.Sum(m => m.In - m.Out));
                    if (stock <= 0) continue;
                    double[] b = new double[5];
                    var left = stock;
                    var oldest = "";
                    foreach (var m in list.Where(m => m.In > 0).OrderByDescending(m => m.Date, StringComparer.Ordinal))
                    {
                        if (left <= 0) break;
                        var take = Math.Min(left, m.In);
                        var age = (day - DateTime.Parse(m.Date, Inv)).TotalDays;
                        b[age <= 30 ? 0 : age <= 60 ? 1 : age <= 90 ? 2 : age <= 180 ? 3 : 4] += take;
                        oldest = m.Date;
                        left = Math.Round(left - take, 4);
                    }
                    var rate = RateOf(rates, it);
                    rows.Add(new
                    {
                        partCode = it.PartCode, partName = it.PartName, category = it.Category, grade = it.Grade, unit = it.Unit,
                        stock, age0_30 = R2(b[0]), age31_60 = R2(b[1]), age61_90 = R2(b[2]), age91_180 = R2(b[3]), age181Plus = R2(b[4]),
                        oldestReceipt = oldest, rate, value = R2(stock * rate)
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("HeatWise")]
        public async Task<IActionResult> HeatWise([FromQuery] string? AsOn, [FromQuery] int PartNo, [FromQuery] string? Grade,
            [FromQuery] bool ShowZero, [FromQuery] int SubscID)
        {
            var bad = AsOnOf(AsOn, SubscID, out var asOn);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                // Heats received: approved Heat No entries (GRN) and approved own opening lines.
                var rows = await db.PmQueryRowsAsync(@"
                    WITH rec AS (
                        SELECT d.PartNo, LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(d.Grade)), ''), ISNULL(m.Grade, '')))) AS Grade,
                               UPPER(LTRIM(RTRIM(d.HeatNo))) AS HeatNo, ISNULL(d.TCNo, '') AS TCNo, 'GRN' AS Source,
                               CONCAT('GRN ', RTRIM(ISNULL(g.DocSeries, '')), '/', g.DocNo) AS RefNo, g.DocDate AS RefDate, d.Qty
                          FROM dbo.HeatNoDtl d
                          JOIN dbo.HeatNoMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                          LEFT JOIN dbo.GRNMst g ON g.MTransNo = m.TargetTransNo
                         WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND LTRIM(RTRIM(ISNULL(d.HeatNo, ''))) <> ''
                           AND ISNULL(g.DocDate, m.DocDate) <= @AsOn
                        UNION ALL
                        SELECT d.PartNo, LTRIM(RTRIM(d.Grade)), UPPER(LTRIM(RTRIM(d.HeatNo))), ISNULL(d.TCNo, ''), 'Opening',
                               CONCAT('OS ', RTRIM(o.DocSeries), '/', o.DocNo), o.AsOnDate, d.Qty
                          FROM dbo.OpeningStockDtl d
                          JOIN dbo.OpeningStockMst o ON o.MTransNo = d.MTransNo AND o.DeleteStatus = 'N'
                                                   AND o.LockStatus = 'Y' AND o.StockType = 'OWN'
                         WHERE o.SubscID = @SubscID AND d.DeleteStatus = 'N' AND LTRIM(RTRIM(ISNULL(d.HeatNo, ''))) <> ''
                           AND o.AsOnDate <= @AsOn
                    ),
                    heats AS (
                        SELECT PartNo, MAX(Grade) AS Grade, HeatNo, MAX(TCNo) AS TCNo,
                               MIN(Source) AS Source, MIN(RefNo) AS RefNo, MIN(RefDate) AS RefDate, SUM(Qty) AS Received
                          FROM rec GROUP BY PartNo, HeatNo
                    ),
                    iss AS (
                        SELECT i.PartNo, UPPER(LTRIM(RTRIM(i.HeatNo))) AS HeatNo, SUM(i.Qty) AS Qty
                          FROM dbo.StoreIssueDtl i
                          JOIN dbo.StoreIssueMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                         WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND ISNULL(i.MaterialSource, 'O') <> 'C'
                           AND LTRIM(RTRIM(ISNULL(i.HeatNo, ''))) <> '' AND im.DocDate <= @AsOn
                         GROUP BY i.PartNo, UPPER(LTRIM(RTRIM(i.HeatNo)))
                    ),
                    ret AS (
                        SELECT i.PartNo, UPPER(LTRIM(RTRIM(i.HeatNo))) AS HeatNo, SUM(r.RTNQty) AS Qty
                          FROM dbo.StoreReturnsDtl r
                          JOIN dbo.StoreReturnsMst rm ON rm.MTransNo = r.MTransNo AND rm.DeleteStatus = 'N'
                          JOIN dbo.StoreIssueDtl i ON i.TransSlNo = r.LinkNo AND ISNULL(i.MaterialSource, 'O') <> 'C'
                         WHERE r.SubscID = @SubscID AND r.DeleteStatus = 'N' AND LTRIM(RTRIM(ISNULL(i.HeatNo, ''))) <> ''
                           AND rm.DocDate <= @AsOn
                         GROUP BY i.PartNo, UPPER(LTRIM(RTRIM(i.HeatNo)))
                    )
                    SELECT  ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName, h.Grade AS grade,
                            ISNULL(p.PrimaryUnit, '') AS unit, h.HeatNo AS heatNo, h.TCNo AS tcNo, h.Source AS source,
                            h.RefNo AS refNo, CONVERT(char(10), h.RefDate, 23) AS refDate,
                            h.Received AS received, ISNULL(iss.Qty, 0) AS issued, ISNULL(ret.Qty, 0) AS returned,
                            h.Received - ISNULL(iss.Qty, 0) + ISNULL(ret.Qty, 0) AS balance
                      FROM  heats h
                      LEFT  JOIN iss ON iss.PartNo = h.PartNo AND iss.HeatNo = h.HeatNo
                      LEFT  JOIN ret ON ret.PartNo = h.PartNo AND ret.HeatNo = h.HeatNo
                      LEFT  JOIN dbo.PartMaster p ON p.MTransNo = h.PartNo
                     WHERE  (@PartNo = 0 OR h.PartNo = @PartNo)
                       AND  (@Grade = '' OR h.Grade = @Grade)
                     ORDER  BY p.PartCode, h.RefDate, h.HeatNo",
                    Int("@SubscID", SubscID), Str("@AsOn", asOn, 10), Int("@PartNo", PartNo), Str("@Grade", (Grade ?? "").Trim(), 50));

                var output = rows
                    .Select(r => new
                    {
                        partCode = S(r, "partCode"), partName = S(r, "partName"), grade = S(r, "grade"), unit = S(r, "unit"),
                        heatNo = S(r, "heatNo"), tcNo = S(r, "tcNo"), source = S(r, "source"), refNo = S(r, "refNo"),
                        refDate = S(r, "refDate"), received = R2(D(r, "received")), issued = R2(D(r, "issued")),
                        returned = R2(D(r, "returned")), balance = R2(D(r, "balance"))
                    })
                    .Where(r => ShowZero || r.balance != 0)
                    .ToList();
                return OkResult(output, output.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("CustomerMaterial")]
        public async Task<IActionResult> CustomerMaterial([FromQuery] string? FromDate, [FromQuery] string? ToDate,
            [FromQuery] int CustomerNo, [FromQuery] int SubscID)
        {
            var bad = Window(FromDate, ToDate, SubscID, out var from, out var to);
            if (bad != null) return BadRequestResult(bad);
            try
            {
                // The dated movements behind VW_CustomerMaterialStock.
                var rows = await db.PmQueryRowsAsync(@"
                    WITH oa AS (
                        SELECT m.SubscID, m.DocYear AS OAYear, m.DocNo AS OANo, MAX(m.VendorNo) AS CustomerNo
                          FROM dbo.OAMst m WHERE m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                         GROUP BY m.SubscID, m.DocYear, m.DocNo
                    ),
                    mv AS (
                        SELECT 'OPENING' AS T, o.CustomerNo, d.PartNo, LTRIM(RTRIM(d.Grade)) AS Grade, d.Qty, o.AsOnDate AS Dt
                          FROM dbo.OpeningStockMst o
                          JOIN dbo.OpeningStockDtl d ON d.MTransNo = o.MTransNo AND d.DeleteStatus = 'N'
                         WHERE o.SubscID = @SubscID AND o.DeleteStatus = 'N' AND o.LockStatus = 'Y' AND o.StockType = 'CUSTOMER'
                        UNION ALL
                        SELECT CASE m.DocType WHEN 'I' THEN 'RECEIVED' ELSE 'TOCUSTOMER' END, m.CustomerNo, d.PartNo,
                               LTRIM(RTRIM(ISNULL(d.Grade, ''))), d.Qty, CAST(m.InwardDate AS DATE)
                          FROM dbo.CustMatInwardMst m
                          JOIN dbo.CustMatInwardDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                         WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocType IN ('I', 'R')
                        UNION ALL
                        SELECT 'ISSUED', oa.CustomerNo, i.PartNo, LTRIM(RTRIM(ISNULL(i.Grade, ''))), i.Qty, CAST(im.DocDate AS DATE)
                          FROM dbo.StoreIssueDtl i
                          JOIN dbo.StoreIssueMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                          JOIN oa ON oa.SubscID = im.SubscID AND oa.OAYear = i.OAYear AND oa.OANo = i.OANo
                         WHERE im.SubscID = @SubscID AND i.DeleteStatus = 'N' AND i.MaterialSource = 'C'
                        UNION ALL
                        SELECT 'SHOPRETURN', oa.CustomerNo, i.PartNo, LTRIM(RTRIM(ISNULL(i.Grade, ''))), r.RTNQty, CAST(rm.DocDate AS DATE)
                          FROM dbo.StoreReturnsDtl r
                          JOIN dbo.StoreReturnsMst rm ON rm.MTransNo = r.MTransNo AND rm.DeleteStatus = 'N'
                          JOIN dbo.StoreIssueDtl i ON i.TransSlNo = r.LinkNo AND i.MaterialSource = 'C'
                          JOIN oa ON oa.SubscID = rm.SubscID AND oa.OAYear = i.OAYear AND oa.OANo = i.OANo
                         WHERE rm.SubscID = @SubscID AND r.DeleteStatus = 'N'
                    )
                    SELECT  mv.T AS t, mv.CustomerNo AS customerNo, ISNULL(c.CustomerCode, '') AS customerCode,
                            ISNULL(c.CustomerName, '') AS customerName, mv.PartNo AS partNo,
                            ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                            mv.Grade AS grade, ISNULL(NULLIF(p.PrimaryUnit, ''), 'Kg') AS unit,
                            mv.Qty AS qty, CONVERT(char(10), mv.Dt, 23) AS dt
                      FROM  mv
                      LEFT  JOIN dbo.CustomerMaster c ON c.MTransNo = mv.CustomerNo
                      LEFT  JOIN dbo.PartMaster p ON p.MTransNo = mv.PartNo
                     WHERE  mv.Dt <= @To AND (@CustomerNo = 0 OR mv.CustomerNo = @CustomerNo)
                     ORDER  BY c.CustomerName, p.PartCode, mv.Grade",
                    Int("@SubscID", SubscID), Str("@To", to, 10), Int("@CustomerNo", CustomerNo));

                var output = rows
                    .GroupBy(r => I(r, "customerNo") + "|" + I(r, "partNo") + "|" + S(r, "grade").ToUpperInvariant())
                    .Select(g =>
                    {
                        double opening = 0, received = 0, issued = 0, back = 0, toCust = 0;
                        foreach (var r in g)
                        {
                            var t = S(r, "t");
                            var q = D(r, "qty");
                            var sign = t is "ISSUED" or "TOCUSTOMER" ? -1 : 1;
                            if (string.CompareOrdinal(S(r, "dt"), from) < 0 || t == "OPENING") { opening += sign * q; continue; }
                            if (t == "RECEIVED") received += q;
                            else if (t == "ISSUED") issued += q;
                            else if (t == "SHOPRETURN") back += q;
                            else if (t == "TOCUSTOMER") toCust += q;
                        }
                        var f = g.First();
                        return new
                        {
                            customerCode = S(f, "customerCode"), customerName = S(f, "customerName"),
                            partCode = S(f, "partCode"), partName = S(f, "partName"), grade = S(f, "grade"), unit = S(f, "unit"),
                            opening = R2(opening), received = R2(received), issued = R2(issued),
                            returnedFromShop = R2(back), returnedToCustomer = R2(toCust),
                            balance = R2(opening + received - issued + back - toCust)
                        };
                    })
                    .ToList();
                return OkResult(output, output.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // Shared reads
        // ============================================================

        private sealed class Item
        {
            public int PartNo; public string PartCode = "", PartName = "", Category = "", Grade = "", Unit = "";
            public double Min, Max, Moq, Safety, CostPrice; public int LeadDays;
            public string Key => PartNo + "|" + Grade.ToUpperInvariant();
        }

        private sealed class Move
        {
            public string Type = "", Date = "", Grade = ""; public int PartNo; public double In, Out;
            public string Key => PartNo + "|" + Grade.ToUpperInvariant();
        }

        /// <summary>Every part + grade of the masters (filtered), with its stock levels.</summary>
        private async Task<List<Item>> Items(int subscID, int partNo, string? grade, string? category)
        {
            var rows = await db.PmQueryRowsAsync(@"
                SELECT  p.MTransNo AS partNo, ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                        ISNULL(p.Category, '') AS category, ISNULL(p.PrimaryUnit, '') AS unit, ISNULL(p.CostPrice, 0) AS costPrice,
                        LTRIM(RTRIM(g.Grade)) AS grade, ISNULL(g.MinStock, 0) AS minStock, ISNULL(g.MaxStock, 0) AS maxStock,
                        ISNULL(g.MOQ, 0) AS moq, ISNULL(g.SafetyStock, 0) AS safetyStock, ISNULL(g.LeadTimeDays, 0) AS leadTimeDays
                  FROM  dbo.PartMaster p
                  JOIN  dbo.MaterialGrade g ON g.PartNo = p.MTransNo AND g.SubscID = p.SubscID AND g.DeleteStatus = 'N'
                                           AND ISNULL(LTRIM(RTRIM(g.Grade)), '') <> ''
                 WHERE  p.SubscID = @SubscID AND p.DeleteStatus = 'N'
                   AND  (@PartNo = 0 OR p.MTransNo = @PartNo)
                   AND  (@Grade = '' OR LTRIM(RTRIM(g.Grade)) = @Grade)
                   AND  (@Category = '' OR p.Category = @Category)
                 ORDER  BY p.PartCode, g.Grade",
                Int("@SubscID", subscID), Int("@PartNo", partNo), Str("@Grade", (grade ?? "").Trim(), 50), Str("@Category", (category ?? "").Trim(), 100));
            return rows.Select(r => new Item
            {
                PartNo = I(r, "partNo"), PartCode = S(r, "partCode"), PartName = S(r, "partName"), Category = S(r, "category"),
                Unit = S(r, "unit"), CostPrice = D(r, "costPrice"), Grade = S(r, "grade"),
                Min = D(r, "minStock"), Max = D(r, "maxStock"), Moq = D(r, "moq"), Safety = D(r, "safetyStock"), LeadDays = I(r, "leadTimeDays")
            }).ToList();
        }

        /// <summary>
        /// The master's part + grades, plus any part + grade that has stock
        /// movements but is not (or no longer) a grade in the master.
        /// </summary>
        private async Task<List<Item>> AllKeys(List<Item> items, List<Move> moves, string? grade, string? category)
        {
            var keys = items.Select(i => i.Key).ToHashSet();
            var extra = moves.Where(m => !keys.Contains(m.Key)).Select(m => (m.PartNo, m.Grade)).Distinct().ToList();
            if (extra.Count == 0) return items;
            var parts = (await db.PmQueryRowsAsync(@"
                SELECT MTransNo AS partNo, ISNULL(PartCode, '') AS partCode, ISNULL(PartName, '') AS partName,
                       ISNULL(Category, '') AS category, ISNULL(PrimaryUnit, '') AS unit, ISNULL(CostPrice, 0) AS costPrice
                  FROM dbo.PartMaster"))
                .ToDictionary(r => I(r, "partNo"));
            var all = new List<Item>(items);
            foreach (var (pNo, g) in extra)
            {
                if (!parts.TryGetValue(pNo, out var p)) continue;
                if (!string.IsNullOrWhiteSpace(grade) && !string.Equals(g, grade.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(category) && S(p, "category") != category.Trim()) continue;
                all.Add(new Item
                {
                    PartNo = pNo, PartCode = S(p, "partCode"), PartName = S(p, "partName"), Category = S(p, "category"),
                    Unit = S(p, "unit"), CostPrice = D(p, "costPrice"), Grade = g
                });
            }
            return all.OrderBy(i => i.PartCode, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Grade, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Every own-stock movement up to `to` (optionally one part).</summary>
        private async Task<List<Move>> Moves(int subscID, string to, int partNo)
        {
            var rows = await db.PmQueryRowsAsync(@"
                SELECT TxnType AS t, CONVERT(char(10), DocDate, 23) AS d, PartNo AS p, LTRIM(RTRIM(ISNULL(Grade, ''))) AS g,
                       ISNULL(InQty, 0) AS i, ISNULL(OutQty, 0) AS o
                  FROM dbo.VW_StoreStock
                 WHERE SubscID = @SubscID AND DocDate <= @To AND (@PartNo = 0 OR PartNo = @PartNo)",
                Int("@SubscID", subscID), Str("@To", to, 10), Int("@PartNo", partNo));
            return rows.Select(r => new Move
            {
                Type = S(r, "t"), Date = S(r, "d"), PartNo = I(r, "p"), Grade = S(r, "g"), In = D(r, "i"), Out = D(r, "o")
            }).ToList();
        }

        private static Dictionary<string, double> Balances(List<Move> moves) =>
            moves.GroupBy(m => m.Key).ToDictionary(g => g.Key, g => g.Sum(m => m.In - m.Out));

        /// <summary>Weighted average cost per part + grade up to `asOf`: GRN accepted qty and approved own opening stock.</summary>
        private async Task<Dictionary<string, double>> Rates(int subscID, string asOf)
        {
            var rows = await db.PmQueryRowsAsync(@"
                SELECT r.PartNo AS p, r.Grade AS g,
                       CASE WHEN SUM(r.Q) > 0 THEN SUM(r.Q * r.Rate) / SUM(r.Q) ELSE 0 END AS rate
                  FROM (
                        SELECT d.PartNo, LTRIM(RTRIM(ISNULL(d.Grade, ''))) AS Grade,
                               ISNULL(d.AccQty, 0) + ISNULL(d.AODQty, 0) AS Q, ISNULL(d.Rate, 0) AS Rate
                          FROM dbo.GRNDtl d
                          JOIN dbo.GRNMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                                           AND m.GRNType IN ('Q', 'C')
                         WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND ISNULL(d.Rate, 0) > 0 AND m.DocDate <= @AsOf
                        UNION ALL
                        SELECT d.PartNo, LTRIM(RTRIM(d.Grade)), d.Qty, d.Rate
                          FROM dbo.OpeningStockDtl d
                          JOIN dbo.OpeningStockMst o ON o.MTransNo = d.MTransNo AND o.DeleteStatus = 'N'
                                                   AND o.LockStatus = 'Y' AND o.StockType = 'OWN'
                         WHERE o.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.Rate > 0 AND o.AsOnDate <= @AsOf
                       ) r
                 GROUP BY r.PartNo, r.Grade",
                Int("@SubscID", subscID), Str("@AsOf", asOf, 10));
            return rows.ToDictionary(r => I(r, "p") + "|" + S(r, "g").ToUpperInvariant(), r => D(r, "rate"));
        }

        private static double RateOf(Dictionary<string, double> rates, Item it) =>
            R2(rates.TryGetValue(it.Key, out var r) && r > 0 ? r : it.CostPrice);

        // ============================================================
        // Helpers
        // ============================================================

        private static string? Window(string? fromDate, string? toDate, int subscID, out string from, out string to)
        {
            from = DateOnly(fromDate); to = DateOnly(toDate);
            if (subscID <= 0) return "Invalid Subscription.";
            if (from == "" || to == "") return "From Date and To Date are required.";
            if (string.CompareOrdinal(from, to) > 0) return "From Date cannot be after To Date.";
            return null;
        }

        private static string? AsOnOf(string? asOn, int subscID, out string value)
        {
            value = DateOnly(asOn);
            if (subscID <= 0) return "Invalid Subscription.";
            if (value == "") value = Today;
            return null;
        }

        private static string DateOnly(string? v)
        {
            var s = (v ?? "").Split('T')[0].Trim();
            return DateTime.TryParseExact(s, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", Inv) : "";
        }

        private static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        private static string S(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, Inv)!.Trim() : "";
        private static double D(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToDouble(v, Inv) : 0;
        private static int I(Dictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToInt32(v, Inv) : 0;
        private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
        private static SqlParameter Str(string name, string value, int size) => new(name, SqlDbType.VarChar, size) { Value = value };
    }
}
