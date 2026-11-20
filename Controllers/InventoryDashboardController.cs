using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static EMSAPI.Models.Dash;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Inventory Dashboard — EMSSoft/Modules/Store/InventoryDashboard.html.
    ///
    ///     GET api/InventoryDashboard/GetSummary?SubscID=&amp;Refresh=    stock as on today
    ///     GET api/InventoryDashboard/GetScrap?SubscID=&amp;Refresh=      scrap: this month, last month, 6 months
    ///
    /// Two calls so the page draws its stock cards without waiting for the
    /// scrap history (six months of job card entries); the page fires both
    /// at once. Both are cached for a few minutes (Models/DashboardSupport.cs).
    ///
    /// Stock follows Stock Reports (StockReportsController):
    ///   stock  = VW_StoreStock in − out per part + grade (summed in SQL)
    ///   rate   = weighted average of GRN accepted (+AOD) qty and approved own
    ///            opening stock, else PartMaster.CostPrice; value = stock × rate
    ///   levels = MaterialGrade Min / Safety / Max / MOQ (the Reorder report)
    /// Kind of item: finished goods = a part made on a job card or the FG of a
    /// Conversion BOM; else PartMaster.PartType (RAW MATERIAL, CONSUMABLE,
    /// DIE; anything else = bought-out).
    /// Non-moving: stock on hand with no issue for 90+ days — counted from the
    /// last issue, or from the first receipt when the item was never issued
    /// (Stock Reports' filter also counts never-issued stock received recently).
    /// Dies: DieMst status; used = live JobCardEntry qty on the die + opening
    /// (Die Master's rule).
    /// </summary>
    [Route("api/InventoryDashboard")]
    public class InventoryDashboardController : EmsControllerBase
    {
        private readonly EMSContext db;
        private readonly DashboardCache cache;

        public InventoryDashboardController(EMSContext context, DashboardCache cache)
        {
            db = context;
            this.cache = cache;
        }

        private const int NonMovingDays = 90;
        private const double DieWatchPct = 80;

        [HttpGet("GetSummary")]
        public async Task<IActionResult> GetSummary([FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await cache.GetOrBuild("Inventory", DashboardCache.KeyOf(SubscID), Refresh, t => BuildStock(SubscID, t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetScrap")]
        public async Task<IActionResult> GetScrap([FromQuery] int SubscID, [FromQuery] bool Refresh = false)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await cache.GetOrBuild("InventoryScrap", DashboardCache.KeyOf(SubscID), Refresh, t => BuildScrap(SubscID, t));
                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ================================================================
        // STOCK
        // ================================================================

        private sealed class Item
        {
            public int PartNo; public string Code = "", Name = "", Unit = "", Grade = "", Kind = "";
            public double Min, Max, Moq, Safety, Cost, Rate, Stock;
            public DateTime? LastOut, FirstIn, LastMove;
            public bool InMaster;
            public double Value => Math.Max(0, Stock) * Rate;
        }

        private async Task<Dictionary<string, object?>> BuildStock(int subscID, DashTimer t)
        {
            var con = db.Database.GetDbConnection();
            var today = DateTime.Today;
            SqlParameter S() => PInt("@SubscID", subscID);

            var parts = (await t.Time("parts", () => Rows(con, @"
                SELECT p.MTransNo AS partNo, ISNULL(p.PartCode, '') AS code, ISNULL(p.PartName, '') AS name,
                       ISNULL(p.PrimaryUnit, '') AS unit, ISNULL(p.CostPrice, 0) AS cost, UPPER(LTRIM(RTRIM(ISNULL(p.PartType, '')))) AS partType,
                       CASE WHEN EXISTS (SELECT 1 FROM dbo.ConvBOM b WHERE b.FPartNo = p.MTransNo AND b.DeleteStatus = 'N')
                              OR EXISTS (SELECT 1 FROM dbo.JobCardMst j WHERE j.PartNo = p.MTransNo AND j.DeleteStatus = 'N')
                            THEN 1 ELSE 0 END AS isFg
                  FROM dbo.PartMaster p WHERE p.SubscID = @SubscID", S())))
                .ToDictionary(r => Int(r, "partNo"));

            string KindOf(Dictionary<string, object?> p) =>
                Int(p, "isFg") == 1 ? "Finished goods"
                : Str(p, "partType") switch { "RAW MATERIAL" => "Raw material", "CONSUMABLE" => "Consumable", "DIE" => "Die", _ => "Bought-out" };

            // Master part + grades with their stock levels (the Reorder report's list).
            var items = new Dictionary<string, Item>();
            foreach (var r in await t.Time("levels", () => Rows(con, @"
                SELECT g.PartNo AS partNo, LTRIM(RTRIM(g.Grade)) AS grade, ISNULL(g.MinStock, 0) AS minStock, ISNULL(g.MaxStock, 0) AS maxStock,
                       ISNULL(g.MOQ, 0) AS moq, ISNULL(g.SafetyStock, 0) AS safety
                  FROM dbo.MaterialGrade g
                  JOIN dbo.PartMaster p ON p.MTransNo = g.PartNo AND p.SubscID = g.SubscID AND p.DeleteStatus = 'N'
                 WHERE g.SubscID = @SubscID AND g.DeleteStatus = 'N' AND ISNULL(LTRIM(RTRIM(g.Grade)), '') <> ''", S())))
            {
                if (!parts.TryGetValue(Int(r, "partNo"), out var p)) continue;
                var it = NewItem(Int(r, "partNo"), Str(r, "grade"), p, KindOf(p));
                it.Min = Dbl(r, "minStock"); it.Max = Dbl(r, "maxStock"); it.Moq = Dbl(r, "moq"); it.Safety = Dbl(r, "safety");
                it.InMaster = true;
                items[Key(it.PartNo, it.Grade)] = it;
            }

            // Balances, summed by the server — not every movement pulled across.
            foreach (var r in await t.Time("stock", () => Rows(con, @"
                SELECT PartNo AS partNo, LTRIM(RTRIM(ISNULL(Grade, ''))) AS grade,
                       SUM(ISNULL(InQty, 0) - ISNULL(OutQty, 0)) AS bal,
                       MAX(CASE WHEN ISNULL(OutQty, 0) > 0 THEN CAST(DocDate AS date) END) AS lastOut,
                       MIN(CASE WHEN ISNULL(InQty, 0) > 0 THEN CAST(DocDate AS date) END) AS firstIn,
                       MAX(CAST(DocDate AS date)) AS lastMove
                  FROM dbo.VW_StoreStock
                 WHERE SubscID = @SubscID AND DocDate < DATEADD(day, 1, CAST(GETDATE() AS date))
                 GROUP BY PartNo, LTRIM(RTRIM(ISNULL(Grade, '')))", S())))
            {
                var key = Key(Int(r, "partNo"), Str(r, "grade"));
                if (!items.TryGetValue(key, out var it))
                {
                    if (!parts.TryGetValue(Int(r, "partNo"), out var p)) continue;
                    it = NewItem(Int(r, "partNo"), Str(r, "grade"), p, KindOf(p));
                    items[key] = it;
                }
                it.Stock += Dbl(r, "bal");
                it.LastOut = Max(it.LastOut, Date(r, "lastOut"));
                it.FirstIn = Min(it.FirstIn, Date(r, "firstIn"));
                it.LastMove = Max(it.LastMove, Date(r, "lastMove"));
            }

            var rates = (await t.Time("rates", () => Rows(con, @"
                SELECT r.PartNo AS p, UPPER(r.Grade) AS g,
                       CASE WHEN SUM(r.Q) > 0 THEN SUM(r.Q * r.Rate) / SUM(r.Q) ELSE 0 END AS rate
                  FROM (SELECT d.PartNo, LTRIM(RTRIM(ISNULL(d.Grade, ''))) AS Grade,
                               ISNULL(d.AccQty, 0) + ISNULL(d.AODQty, 0) AS Q, ISNULL(d.Rate, 0) AS Rate
                          FROM dbo.GRNDtl d
                          JOIN dbo.GRNMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y' AND m.GRNType IN ('Q', 'C')
                         WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND ISNULL(d.Rate, 0) > 0
                        UNION ALL
                        SELECT d.PartNo, LTRIM(RTRIM(d.Grade)), d.Qty, d.Rate
                          FROM dbo.OpeningStockDtl d
                          JOIN dbo.OpeningStockMst o ON o.MTransNo = d.MTransNo AND o.DeleteStatus = 'N' AND o.LockStatus = 'Y' AND o.StockType = 'OWN'
                         WHERE o.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.Rate > 0) r
                 GROUP BY r.PartNo, UPPER(r.Grade)", S())))
                .ToDictionary(r => Int(r, "p") + "|" + Str(r, "g"), r => Dbl(r, "rate"));
            foreach (var it in items.Values)
            {
                it.Stock = R2(it.Stock);
                it.Rate = R2(rates.TryGetValue(Key(it.PartNo, it.Grade), out var rt) && rt > 0 ? rt : it.Cost);
            }

            var onHand = items.Values.Where(i => i.Stock > 0).ToList();
            var rm = onHand.Where(i => i.Kind == "Raw material").ToList();
            var fg = onHand.Where(i => i.Kind == "Finished goods").ToList();

            var typeOrder = new[] { "Raw material", "Consumable", "Bought-out", "Die", "Finished goods" };
            var stockByType = typeOrder.Select(k => (k, list: onHand.Where(i => i.Kind == k).ToList()))
                .Where(x => x.k != "Die" || x.list.Count > 0)
                .Select(x => new { type = x.k, items = x.list.Count, qty = R2(x.list.Sum(i => i.Stock)), value = Math.Round(x.list.Sum(i => i.Value)) })
                .ToList();

            var rmByGrade = TopWithOthers(rm.GroupBy(i => i.Grade == "" ? "No grade" : i.Grade).Select(g => new Dictionary<string, object?>
            {
                ["grade"] = g.Key, ["kg"] = R2(g.Sum(i => i.Stock)), ["value"] = Math.Round(g.Sum(i => i.Value)),
            }), r => Dbl(r, "kg"), rest => new Dictionary<string, object?>
            {
                ["grade"] = "Others (" + rest.Count + ")", ["kg"] = R2(SumOf(rest, "kg")), ["value"] = SumOf(rest, "value"),
            });

            // Reorder rule: below safety first, then below min; order up to Max in MOQ multiples.
            var low = items.Values.Where(i => i.InMaster).Select(i =>
            {
                string status = i.Min == 0 && i.Max == 0 && i.Safety == 0 ? "No level set"
                    : i.Safety > 0 && i.Stock < i.Safety ? "Below Safety"
                    : i.Min > 0 && i.Stock < i.Min ? "Below Min" : "OK";
                double sug = 0;
                if (i.Min > 0 && i.Stock < i.Min && i.Max > 0)
                {
                    var need = i.Max - i.Stock;
                    sug = i.Moq > 0 ? Math.Ceiling(need / i.Moq) * i.Moq : need;
                }
                return (i, status, sug);
            }).Where(x => x.status == "Below Safety" || x.status == "Below Min")
              .OrderBy(x => x.status == "Below Safety" ? 0 : 1).ThenBy(x => x.i.Code, StringComparer.OrdinalIgnoreCase)
              .Select(x => new
              {
                  partCode = x.i.Code, partName = x.i.Name, grade = x.i.Grade, unit = x.i.Unit, stock = x.i.Stock,
                  safetyStock = x.i.Safety, minStock = x.i.Min, maxStock = x.i.Max, status = x.status,
                  suggestedQty = R2(x.sug), suggestedValue = Math.Round(x.sug * x.i.Rate),
              }).ToList();

            var idleFrom = today.AddDays(-NonMovingDays);
            var nonMoving = onHand.Select(i => (i, since: i.LastOut ?? i.FirstIn))
                .Where(x => x.since != null && x.since < idleFrom)
                .OrderByDescending(x => x.i.Value)
                .Select(x => new
                {
                    partCode = x.i.Code, partName = x.i.Name + (x.i.Grade != "" ? " · " + x.i.Grade : ""), type = x.i.Kind,
                    unit = x.i.Unit, stock = x.i.Stock, value = Math.Round(x.i.Value),
                    lastOut = x.i.LastOut != null ? Iso(x.i.LastOut.Value) : "", daysIdle = (today - x.since!.Value).Days,
                }).ToList();

            // ── dies ─────────────────────────────────────────────────────
            var dies = await t.Time("dies", () => Rows(con, @"
                SELECT d.MTransNo AS id, ISNULL(NULLIF(p.PartCode, ''), 'Die ' + CAST(d.MTransNo AS varchar(10))) AS dieNo,
                       ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                       ISNULL(d.DieStatus, 'Ready') AS status, ISNULL(d.MaxQty, 0) AS maxQty,
                       ISNULL(d.OpeningQty, 0) + ISNULL(u.q, 0) AS processed
                  FROM dbo.DieMst d
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
                  OUTER APPLY (SELECT SUM(e.Qty) AS q FROM dbo.JobCardEntry e WHERE e.DieNo = d.MTransNo AND e.DeleteStatus = 'N') u
                 WHERE d.SubscID = @SubscID AND ISNULL(d.DeleteStatus, 'N') = 'N'", S()));
            var health = dies.Select(d => new
            {
                dieNo = Str(d, "dieNo"), partCode = Str(d, "partCode"), partName = Str(d, "partName"),
                usedPct = Pct(Dbl(d, "processed"), Dbl(d, "maxQty")), remainingQty = Math.Max(0, Dbl(d, "maxQty") - Dbl(d, "processed")),
                status = Str(d, "status"),
            }).Where(d => d.status != "Scrapped" && (d.usedPct >= DieWatchPct || d.status != "Ready"))
              .OrderBy(d => d.status == "Ready" ? 1 : 0).ThenByDescending(d => d.usedPct).ToList();

            // ── work in progress ─────────────────────────────────────────
            var wipHead = (await t.Time("wip", () => Rows(con, @"
                SELECT COUNT(*) AS jobCards, ISNULL(SUM(s.BalanceQty), 0) AS pieces, ISNULL(SUM(s.RMIssuedQty), 0) AS barKg
                  FROM dbo.VW_JobCardSummary s
                  JOIN dbo.JobCardMst j ON j.MTransNo = s.JobCardTransNo AND j.DeleteStatus = 'N'
                 WHERE s.SubscID = @SubscID AND s.JobStatus = 'In Progress'", S()))).FirstOrDefault();
            var wipOps = await t.Time("wip by operation", () => Rows(con, WipByOperationSql, S(), PInt("@MachineNo", 0)));

            var totals = new Dictionary<string, int>();
            return new Dictionary<string, object?>
            {
                ["kpi"] = new
                {
                    rmKg = R2(rm.Sum(i => i.Stock)), rmValue = Math.Round(rm.Sum(i => i.Value)),
                    fgPieces = R2(fg.Sum(i => i.Stock)), fgValue = Math.Round(fg.Sum(i => i.Value)),
                    lowStockItems = low.Count, belowSafety = low.Count(x => x.status == "Below Safety"),
                    nonMovingValue = nonMoving.Sum(x => x.value), nonMovingItems = nonMoving.Count,
                },
                ["stockByType"] = stockByType,
                ["rmByGrade"] = rmByGrade,
                ["fgStock"] = Limit(totals, "fgStock", fg.OrderByDescending(i => i.Value).Select(i => new
                {
                    partCode = i.Code, partName = i.Name, grade = i.Grade, unit = i.Unit, stock = i.Stock, rate = i.Rate,
                    value = Math.Round(i.Value), lastMovement = i.LastMove != null ? Iso(i.LastMove.Value) : "",
                })),
                ["dies"] = new
                {
                    ready = dies.Count(d => Str(d, "status") == "Ready"), inRepair = dies.Count(d => Str(d, "status") == "In Repair"),
                    scrapped = dies.Count(d => Str(d, "status") == "Scrapped"), total = dies.Count,
                    health = Limit(totals, "dieHealth", health),
                },
                ["wip"] = new
                {
                    jobCards = Int(wipHead, "jobCards"), pieces = Dbl(wipHead, "pieces"), barKg = R2(Dbl(wipHead, "barKg")),
                    byOperation = wipOps.Select(r => new { operation = Str(r, "operation"), jobCards = Int(r, "jobCards"), qty = Dbl(r, "qty") }).ToList(),
                },
                ["lowStock"] = Limit(totals, "lowStock", low),
                ["nonMoving"] = Limit(totals, "nonMoving", nonMoving),
                ["listTotals"] = totals,
            };
        }

        private static Item NewItem(int partNo, string grade, Dictionary<string, object?> p, string kind) => new()
        {
            PartNo = partNo, Grade = grade, Code = Str(p, "code"), Name = Str(p, "name"), Unit = Str(p, "unit"),
            Cost = Dbl(p, "cost"), Kind = kind,
        };

        private static string Key(int partNo, string grade) => partNo + "|" + (grade ?? "").Trim().ToUpperInvariant();
        private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a > b ? a : b);
        private static DateTime? Min(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : (a < b ? a : b);

        // ================================================================
        // SCRAP
        // ================================================================
        //   forging scrap   = forged pcs × (cut weight − forged weight)     flash, burr, scale
        //   rejection scrap = rejected pcs × forged weight                  (their flash is already above)
        //   bar-end scrap   = Issue Return lines, ReturnType 'Scrap'
        //   end pieces      = ReturnType 'End Piece' — re-usable, reported apart
        // cut weight = ConvBOM.Qty of the part (its grade, else the blank-grade
        // row); forged weight = PartMaster.PartWeight. A part whose weights are
        // missing, or whose cut weight is not more than its forged weight, is
        // left out of forging / rejection scrap and listed in missingWeight.

        private sealed class Agg { public double Forged, Rejected, ForgingKg, RejectionKg, BarEndKg, EndPieceKg, BarUsedKg; }

        private async Task<Dictionary<string, object?>> BuildScrap(int subscID, DashTimer t)
        {
            var con = db.Database.GetDbConnection();
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var trendStart = monthStart.AddMonths(-5);
            var prevStart = monthStart.AddMonths(-1);
            var prevEnd = new DateTime(prevStart.Year, prevStart.Month, Math.Min(today.Day, DateTime.DaysInMonth(prevStart.Year, prevStart.Month)));
            SqlParameter[] P() => new[]
            {
                PInt("@SubscID", subscID), PDate("@From", trendStart), PDate("@To", today), PDate("@MonthStart", monthStart),
                PDate("@PrevFrom", prevStart), PDate("@PrevTo", prevEnd),
            };
            const string Periods = @"
                       DATEFROMPARTS(YEAR({0}), MONTH({0}), 1) AS mon,
                       SUM(CASE WHEN {0} >= @MonthStart THEN {1} ELSE 0 END) AS cur,
                       SUM(CASE WHEN {0} BETWEEN @PrevFrom AND @PrevTo THEN {1} ELSE 0 END) AS prev,
                       SUM({1}) AS total";

            var forged = await t.Time("forged", () => Rows(con, @"
                SELECT j.PartNo AS partNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))) AS grade,
                       LTRIM(RTRIM(ISNULL(NULLIF(j.RMGrade, ''), j.Grade))) AS rmGrade," + string.Format(Periods, "e.EntryDate", "e.Qty") +
                ForgedEntriesFrom + @" AND e.EntryDate BETWEEN @From AND @To
                 GROUP BY j.PartNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))), LTRIM(RTRIM(ISNULL(NULLIF(j.RMGrade, ''), j.Grade))),
                          DATEFROMPARTS(YEAR(e.EntryDate), MONTH(e.EntryDate), 1)", P()));

            var rejected = await t.Time("rejected", () => Rows(con, @"
                SELECT j.PartNo AS partNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))) AS grade,
                       LTRIM(RTRIM(ISNULL(NULLIF(j.RMGrade, ''), j.Grade))) AS rmGrade," + string.Format(Periods, "i.InspDate", "ISNULL(i.RejectedQty, 0)") + @"
                  FROM dbo.JobCardInspection i
                  JOIN dbo.JobCardMst j ON j.MTransNo = i.JobCardTransNo AND j.DeleteStatus = 'N'
                 WHERE i.SubscID = @SubscID AND i.DeleteStatus = 'N' AND ISNULL(i.RejectedQty, 0) > 0
                   AND i.InspDate BETWEEN @From AND @To
                 GROUP BY j.PartNo, LTRIM(RTRIM(ISNULL(j.Grade, ''))), LTRIM(RTRIM(ISNULL(NULLIF(j.RMGrade, ''), j.Grade))),
                          DATEFROMPARTS(YEAR(i.InspDate), MONTH(i.InspDate), 1)", P()));

            // Issue Return lines: scrap and end pieces, with the forged part when the
            // bar was issued to a job card.
            var returns = await t.Time("returns", () => Rows(con, @"
                SELECT d.ReturnType AS returnType, LTRIM(RTRIM(ISNULL(d.Grade, ''))) AS grade, ISNULL(jc.PartNo, 0) AS partNo,
                       LTRIM(RTRIM(ISNULL(jc.Grade, ''))) AS fgGrade," + string.Format(Periods, "m.DocDate", "ISNULL(d.RTNQty, 0)") + @"
                  FROM dbo.StoreReturnsDtl d
                  JOIN dbo.StoreReturnsMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N'
                  LEFT JOIN dbo.StoreIssueDtl idl ON idl.TransSlNo = d.LinkNo
                  LEFT JOIN dbo.StoreIssueMst im ON im.MTransNo = idl.MTransNo AND im.TargetType = 'Job Card'
                  LEFT JOIN dbo.JobCardMst jc ON jc.MTransNo = im.TargetTransNo AND jc.DeleteStatus = 'N'
                 WHERE m.SubscID = @SubscID AND ISNULL(d.DeleteStatus, 'N') = 'N' AND d.ReturnType IN ('Scrap', 'End Piece')
                   AND m.DocDate BETWEEN @From AND @To
                 GROUP BY d.ReturnType, LTRIM(RTRIM(ISNULL(d.Grade, ''))), ISNULL(jc.PartNo, 0), LTRIM(RTRIM(ISNULL(jc.Grade, ''))),
                          DATEFROMPARTS(YEAR(m.DocDate), MONTH(m.DocDate), 1)", P()));

            // Weights of the parts involved.
            var partNos = forged.Concat(rejected).Concat(returns).Select(r => Int(r, "partNo")).Where(n => n > 0).Distinct().ToList();
            var partInfo = new Dictionary<int, (string Code, string Name, double ForgedWt)>();
            var cutRows = new List<Dictionary<string, object?>>();
            if (partNos.Count > 0)
            {
                var ids = string.Join(",", partNos);   // integers only
                foreach (var r in await Rows(con, $"SELECT MTransNo AS partNo, ISNULL(PartCode, '') AS code, ISNULL(PartName, '') AS name, ISNULL(PartWeight, 0) AS wt FROM dbo.PartMaster WHERE MTransNo IN ({ids})"))
                    partInfo[Int(r, "partNo")] = (Str(r, "code"), Str(r, "name"), Dbl(r, "wt"));
                cutRows = await Rows(con, $@"SELECT FPartNo AS partNo, LTRIM(RTRIM(ISNULL(FGrade, ''))) AS grade, ISNULL(Qty, 0) AS cutWt
                                              FROM dbo.ConvBOM WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND ISNULL(LockStatus, 'Y') <> 'N'
                                               AND FPartNo IN ({ids}) ORDER BY MTransNo", PInt("@SubscID", subscID));
            }
            double CutOf(int partNo, string grade)
            {
                var mine = cutRows.Where(r => Int(r, "partNo") == partNo).ToList();
                var pick = mine.FirstOrDefault(r => string.Equals(Str(r, "grade"), grade, StringComparison.OrdinalIgnoreCase))
                        ?? mine.FirstOrDefault(r => Str(r, "grade") == "");
                return Dbl(pick, "cutWt");
            }
            string? WeightProblem(int partNo, string grade)
            {
                var fw = partInfo.TryGetValue(partNo, out var pi) ? pi.ForgedWt : 0;
                var cw = CutOf(partNo, grade);
                if (fw <= 0) return "No forged weight on Part Master";
                if (cw <= 0) return "No cut weight on Conversion BOM";
                if (cw <= fw) return "Cut weight is not more than forged weight";
                return null;
            }

            var cur = new Agg(); var prev = new Agg();
            var months = new Dictionary<string, Agg>();
            var byGrade = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var byPart = new Dictionary<(int, string), Agg>();
            var missing = new Dictionary<(int, string), (string Reason, double Forged)>();
            Agg MonthOf(Dictionary<string, object?> r) =>
                months.TryGetValue(MonthKey(r), out var a) ? a : months[MonthKey(r)] = new Agg();
            Agg PartOf(int partNo, string grade) =>
                byPart.TryGetValue((partNo, grade), out var a) ? a : byPart[(partNo, grade)] = new Agg();
            void AddGrade(string grade, double kg) { if (kg > 0) byGrade[grade == "" ? "No grade" : grade] = (byGrade.TryGetValue(grade == "" ? "No grade" : grade, out var v) ? v : 0) + kg; }

            foreach (var r in forged)
            {
                int pn = Int(r, "partNo"); string g = Str(r, "grade");
                var problem = WeightProblem(pn, g);
                double cut = CutOf(pn, g), fw = partInfo.TryGetValue(pn, out var pi) ? pi.ForgedWt : 0;
                double flash = problem == null ? cut - fw : 0, bar = problem == null ? cut : 0;
                var m = MonthOf(r);
                m.Forged += Dbl(r, "total"); m.ForgingKg += Dbl(r, "total") * flash; m.BarUsedKg += Dbl(r, "total") * bar;
                cur.Forged += Dbl(r, "cur"); cur.ForgingKg += Dbl(r, "cur") * flash; cur.BarUsedKg += Dbl(r, "cur") * bar;
                prev.Forged += Dbl(r, "prev"); prev.ForgingKg += Dbl(r, "prev") * flash; prev.BarUsedKg += Dbl(r, "prev") * bar;
                if (Dbl(r, "cur") <= 0) continue;
                var pa = PartOf(pn, g);
                pa.Forged += Dbl(r, "cur"); pa.ForgingKg += Dbl(r, "cur") * flash; pa.BarUsedKg += Dbl(r, "cur") * bar;
                AddGrade(Str(r, "rmGrade"), Dbl(r, "cur") * flash);
                if (problem != null)
                    missing[(pn, g)] = (problem, (missing.TryGetValue((pn, g), out var mm) ? mm.Forged : 0) + Dbl(r, "cur"));
            }
            foreach (var r in rejected)
            {
                int pn = Int(r, "partNo"); string g = Str(r, "grade");
                double fw = WeightProblem(pn, g) == null && partInfo.TryGetValue(pn, out var pi) ? pi.ForgedWt : 0;
                var m = MonthOf(r);
                m.Rejected += Dbl(r, "total"); m.RejectionKg += Dbl(r, "total") * fw;
                cur.Rejected += Dbl(r, "cur"); cur.RejectionKg += Dbl(r, "cur") * fw;
                prev.Rejected += Dbl(r, "prev"); prev.RejectionKg += Dbl(r, "prev") * fw;
                if (Dbl(r, "cur") <= 0) continue;
                var pa = PartOf(pn, g);
                pa.Rejected += Dbl(r, "cur"); pa.RejectionKg += Dbl(r, "cur") * fw;
                AddGrade(Str(r, "rmGrade"), Dbl(r, "cur") * fw);
            }
            foreach (var r in returns)
            {
                var scrap = Str(r, "returnType") == "Scrap";
                var m = MonthOf(r);
                if (scrap)
                {
                    m.BarEndKg += Dbl(r, "total"); cur.BarEndKg += Dbl(r, "cur"); prev.BarEndKg += Dbl(r, "prev");
                    AddGrade(Str(r, "grade"), Dbl(r, "cur"));
                    if (Int(r, "partNo") > 0 && Dbl(r, "cur") > 0) PartOf(Int(r, "partNo"), Str(r, "fgGrade")).BarEndKg += Dbl(r, "cur");
                }
                else
                {
                    m.EndPieceKg += Dbl(r, "total"); cur.EndPieceKg += Dbl(r, "cur"); prev.EndPieceKg += Dbl(r, "prev");
                }
            }

            object Finish(Agg a)
            {
                var total = a.ForgingKg + a.RejectionKg + a.BarEndKg;
                return new
                {
                    forgingKg = Math.Round(a.ForgingKg), rejectionKg = Math.Round(a.RejectionKg), barEndKg = Math.Round(a.BarEndKg),
                    endPieceKg = Math.Round(a.EndPieceKg), totalKg = Math.Round(total), barUsedKg = Math.Round(a.BarUsedKg),
                    forgedQty = a.Forged, rejectedQty = a.Rejected, scrapPct = Pct(total, a.BarUsedKg),
                };
            }

            var trend = Enumerable.Range(0, 6).Select(i =>
            {
                var md = trendStart.AddMonths(i);
                var a = months.TryGetValue(md.ToString("yyyy-MM", Inv), out var x) ? x : new Agg();
                var total = a.ForgingKg + a.RejectionKg + a.BarEndKg;
                return new
                {
                    label = md.ToString("MMM yy", Inv), forgingKg = Math.Round(a.ForgingKg), rejectionKg = Math.Round(a.RejectionKg),
                    barEndKg = Math.Round(a.BarEndKg), totalKg = Math.Round(total), scrapPct = Pct(total, a.BarUsedKg),
                };
            }).ToList();

            var partRows = byPart.Select(kv =>
            {
                var (pn, g) = kv.Key;
                var a = kv.Value;
                var ok = WeightProblem(pn, g) == null;
                var pi = partInfo.TryGetValue(pn, out var x) ? x : ("", "", 0);
                var total = a.ForgingKg + a.RejectionKg + a.BarEndKg;
                return new
                {
                    partCode = pi.Item1, partName = pi.Item2, grade = g, cutWt = CutOf(pn, g) > 0 ? CutOf(pn, g) : (double?)null,
                    forgedWt = pi.Item3 > 0 ? pi.Item3 : (double?)null, forgedQty = a.Forged, rejectedQty = a.Rejected,
                    forgingKg = Math.Round(a.ForgingKg), rejectionKg = Math.Round(a.RejectionKg), barEndKg = Math.Round(a.BarEndKg),
                    totalKg = Math.Round(total), scrapPct = ok && a.BarUsedKg > 0 ? Pct(total, a.BarUsedKg) : (double?)null,
                    weightProblem = ok ? "" : WeightProblem(pn, g),
                };
            }).OrderByDescending(p => p.totalKg).ThenByDescending(p => p.forgedQty).ToList();

            var totals = new Dictionary<string, int>();
            return new Dictionary<string, object?>
            {
                ["fromDate"] = Iso(monthStart), ["toDate"] = Iso(today), ["prevFromDate"] = Iso(prevStart), ["prevToDate"] = Iso(prevEnd),
                ["thisMonth"] = Finish(cur),
                ["lastMonthSamePeriod"] = Finish(prev),
                ["trend"] = trend,
                ["byGrade"] = TopWithOthers(byGrade.Select(kv => new Dictionary<string, object?> { ["grade"] = kv.Key, ["kg"] = Math.Round(kv.Value) }),
                    r => Dbl(r, "kg"), rest => new Dictionary<string, object?> { ["grade"] = "Others (" + rest.Count + ")", ["kg"] = SumOf(rest, "kg") }),
                ["byPart"] = Limit(totals, "byPart", partRows),
                ["missingWeight"] = missing.Select(kv => new
                {
                    partCode = partInfo.TryGetValue(kv.Key.Item1, out var pi) ? pi.Code : "", partName = pi.Name ?? "",
                    grade = kv.Key.Item2, forgedQty = kv.Value.Forged, reason = kv.Value.Reason,
                }).OrderByDescending(x => x.forgedQty).ToList(),
                ["listTotals"] = totals,
            };
        }

        private static string MonthKey(Dictionary<string, object?> r) =>
            Date(r, "mon") is DateTime d ? d.ToString("yyyy-MM", Inv) : "";
    }
}
