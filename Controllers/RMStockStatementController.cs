using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// PurchaseReports/RMStockStatement.html — the raw-material bar register
    /// for a date range, by RM part + grade, from VW_StoreStock:
    ///   opening  = in − out before FromDate
    ///   receipt  = GRN in + store returns in the range
    ///   issued   = store issues (+ sales) in the range
    ///   endPiece = returns of type "End Piece" in the range (part of receipt;
    ///              shown so re-usable offcut is visible)
    ///   closing  = opening + receipt − issued
    ///   rate     = weighted GRN rate of the part + grade; value = closing × rate
    /// plus what live, approved plans need of each bar in the 7 days from today
    /// and what is left free after it.
    ///
    ///   GET AllReports?Grade=&MaterialClass=&Show=All|Stock|Movement|Short&FromDate=&ToDate=&SubscID=
    /// </summary>
    [Route("api/[controller]")]
    public class RMStockStatementController : PlanningControllerBase
    {
        public RMStockStatementController(EMSContext context) : base(context) { }

        [HttpGet("AllReports")]
        public async Task<IActionResult> AllReports([FromQuery] string? Grade, [FromQuery] string? MaterialClass, [FromQuery] string? Show,
            [FromQuery] string? FromDate, [FromQuery] string? ToDate, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var from = string.IsNullOrWhiteSpace(FromDate) ? DateTime.Today.ToString("yyyy-MM-01", Inv) : FromDate.Split('T')[0];
                var to = string.IsNullOrWhiteSpace(ToDate) ? Today : ToDate.Split('T')[0];
                var ledger = await Query(@"
                    SELECT s.PartNo AS partNo, LTRIM(RTRIM(ISNULL(s.Grade, ''))) AS grade,
                           ISNULL(p.PartCode, '') AS partCode, ISNULL(p.PartName, '') AS partName,
                           SUM(CASE WHEN s.DocDate < @From THEN ISNULL(s.InQty, 0) - ISNULL(s.OutQty, 0) ELSE 0 END) AS opening,
                           SUM(CASE WHEN s.DocDate BETWEEN @From AND @To AND s.TxnType IN ('GRN', 'RETURN') THEN ISNULL(s.InQty, 0) ELSE 0 END) AS receipt,
                           SUM(CASE WHEN s.DocDate BETWEEN @From AND @To THEN ISNULL(s.OutQty, 0) ELSE 0 END) AS issued,
                           SUM(CASE WHEN s.DocDate BETWEEN @From AND @To AND s.TxnType = 'RETURN' AND rt.ReturnType = 'End Piece'
                                    THEN ISNULL(s.InQty, 0) ELSE 0 END) AS endPiece,
                           STRING_AGG(CASE WHEN s.DocDate BETWEEN @From AND @To AND s.TxnType = 'GRN'
                                           THEN CONCAT(RTRIM(s.DocSeries), '/', s.DocNo) END, ', ') AS grnRefs
                      FROM dbo.VW_StoreStock s
                      LEFT JOIN dbo.PartMaster p ON p.MTransNo = s.PartNo
                      OUTER APPLY (SELECT TOP (1) r.ReturnType FROM dbo.StoreReturnsDtl r
                                    WHERE s.TxnType = 'RETURN' AND r.TransSlNo = s.RefTransSlNo) rt
                     WHERE s.SubscID = @SubscID AND s.DocDate <= @To
                     GROUP BY s.PartNo, LTRIM(RTRIM(ISNULL(s.Grade, ''))), p.PartCode, p.PartName",
                    Int("@SubscID", SubscID), Str("@From", from, 10), Str("@To", to, 10));
                var rates = (await Query(@"
                    SELECT d.PartNo AS partNo, LTRIM(RTRIM(ISNULL(d.Grade, ''))) AS grade,
                           CASE WHEN SUM(ISNULL(d.AccQty, 0)) > 0 THEN SUM(ISNULL(d.Rate, 0) * ISNULL(d.AccQty, 0)) / SUM(ISNULL(d.AccQty, 0)) ELSE MAX(ISNULL(d.Rate, 0)) END AS rate
                      FROM dbo.GRNDtl d WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N'
                     GROUP BY d.PartNo, LTRIM(RTRIM(ISNULL(d.Grade, '')))", Int("@SubscID", SubscID)))
                    .ToDictionary(r => I(r, "partNo") + "|" + S(r, "grade"), r => Dbl(r, "rate"));
                var jobCards = (await Query(@"
                    SELECT j.RMPartNo AS partNo, LTRIM(RTRIM(j.RMGrade)) AS grade, STRING_AGG(j.JobCardNo, ', ') AS jcs
                      FROM dbo.StoreIssueMst m
                      JOIN dbo.JobCardMst j ON j.MTransNo = m.TargetTransNo AND m.TargetType = 'Job Card'
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocDate BETWEEN @From AND @To
                     GROUP BY j.RMPartNo, LTRIM(RTRIM(j.RMGrade))", Int("@SubscID", SubscID), Str("@From", from, 10), Str("@To", to, 10)))
                    .ToDictionary(r => I(r, "partNo") + "|" + S(r, "grade"), r => S(r, "jcs"));

                // What live, approved plans need of each bar in the next 7 days.
                var data = await LoadPlanningData(SubscID);
                var need = new Dictionary<string, double>();
                var win = (From: Today, To: DateTime.Today.AddDays(6).ToString("yyyy-MM-dd", Inv));
                foreach (var p in (await LoadPlans(SubscID)).Where(p => p.PlanStatus != "Draft" && IsLivePlan(p)))
                    foreach (var x in await LineFacts(p, data))
                    {
                        if (x.CustomerMaterial || x.Bar.RMPartNo <= 0) continue;
                        var q = SumBetween(x.Planned, win.From, win.To);
                        if (q > 0) need[x.BarKey] = R2((need.TryGetValue(x.BarKey, out var n) ? n : 0) + q * x.CutWt);
                    }
                var dias = data.ConvBom.Where(b => D(b, "barDia") > 0)
                    .GroupBy(b => I(b, "partNo") + "|" + S(b, "grade")).ToDictionary(g => g.Key, g => Dbl(g.First(), "barDia"));

                var rows = new List<object>();
                foreach (var r in ledger)
                {
                    var key = I(r, "partNo") + "|" + S(r, "grade");
                    double opening = R2(Dbl(r, "opening")), receipt = R2(Dbl(r, "receipt")), issued = R2(Dbl(r, "issued"));
                    var closing = R2(opening + receipt - issued);
                    var rate = R2(rates.TryGetValue(key, out var rt) ? rt : 0);
                    var planKg = need.TryGetValue(key, out var nk) ? nk : 0;
                    var dia = dias.TryGetValue(key, out var dv) ? dv : 0;
                    var grade = S(r, "grade");
                    var mClass = MaterialClassOf(grade);
                    if (!string.IsNullOrWhiteSpace(Grade) && !string.Equals(grade, Grade.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrWhiteSpace(MaterialClass) && mClass != MaterialClass) continue;
                    var endPiece = R2(Dbl(r, "endPiece"));
                    if (Show == "Stock" && !(closing > 0)) continue;
                    if (Show == "Movement" && receipt == 0 && issued == 0) continue;
                    if (Show == "Short" && !(closing - planKg < 0)) continue;
                    rows.Add(new
                    {
                        partNo = I(r, "partNo"), partCode = S(r, "partCode"), partName = S(r, "partName"), grade, materialClass = mClass,
                        dia, diaLabel = dia > 0 ? dia.ToString("0.##", Inv) : S(r, "partCode"),
                        opening, receipt, issued, endPiece, closing, rate, value = R2(closing * rate),
                        jobCards = jobCards.TryGetValue(key, out var jc) ? jc : "", grnRefs = S(r, "grnRefs"),
                        planNext7Kg = planKg, freeAfterPlan = R2(closing - planKg),
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
