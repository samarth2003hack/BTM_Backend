using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Opening Stock — EMSSoft/Modules/Store/OpeningStock.html (SQL/72_OpeningStock.sql).
    ///
    /// The stock held on the go-live day: one document per count, a line per
    /// part + grade + heat with qty, rate and bin. Approved OWN lines are
    /// 'OPENING' rows of VW_StoreStock (so Store Issue, planning and the
    /// reports see them); approved CUSTOMER lines are received customer bar on
    /// their OA in VW_CustomerMaterialStock. Customer material has no value.
    ///
    ///   Mst/GetList/{subscID}                              documents with line count, qty per unit, value
    ///   Mst/GetFull/{id}/{subscID}                         { header, lines }
    ///   Mst/GetNextDocNo/{subscID}?docSeries&amp;docYear       preview of the next OS No
    ///   Mst/SaveFull                                       header + lines (SP_OpeningStockSave)
    ///   Mst/Approve/{id}?userNo&amp;subscID                    S → Y (ADMIN / MANAGER)
    ///   Mst/Unapprove/{id}?userNo&amp;subscID&amp;reason          Y → S (ADMIN; refused once the stock is used)
    ///   Mst/DeleteData/{id}?userNo&amp;subscID&amp;reason          not when approved
    ///   Dtl/ApprovedKeys/{subscID}?excludeMTransNo&amp;stockType&amp;customerNo&amp;oaYear&amp;oaNo
    ///                                                      lines already on an approved opening
    ///   Lookup/PartGrades/{subscID}                        every active part + grade, heat-traced flag
    ///
    /// Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    [Route("api/[controller]")]
    public class OpeningStockController : EmsControllerBase
    {
        private readonly EMSContext db;
        public OpeningStockController(EMSContext context) => db = context;

        private const string HeaderSelect = @"
            SELECT  m.MTransNo                              AS mTransNo,
                    RTRIM(m.DocSeries)                      AS docSeries,
                    m.DocYear                               AS docYear,
                    m.DocNo                                 AS docNo,
                    CONVERT(char(10), m.AsOnDate, 23)       AS asOnDate,
                    m.StockType                             AS stockType,
                    ISNULL(m.CustomerNo, 0)                 AS customerNo,
                    ISNULL(m.CustomerName, '')              AS customerName,
                    ISNULL(m.OAYear, 0)                     AS oaYear,
                    ISNULL(m.OANo, 0)                       AS oaNo,
                    ISNULL(m.Remarks, '')                   AS remarks,
                    m.LockStatus                            AS lockStatus,
                    m.CreatedBy                             AS createdBy,
                    CONVERT(char(10), m.CreatedOn, 23)      AS createdOn,
                    m.ApprovedBy                            AS approvedBy,
                    CONVERT(char(10), m.ApprovedOn, 23)     AS approvedOn,
                    ISNULL(t.LineCount, 0)                  AS lineCount,
                    ISNULL(t.TotalValue, 0)                 AS totalValue
              FROM  dbo.OpeningStockMst m
             OUTER APPLY (SELECT COUNT(*) AS LineCount, SUM(d.Value) AS TotalValue
                            FROM dbo.OpeningStockDtl d
                           WHERE d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N') t
             WHERE  m.SubscID = @SubscID AND m.DeleteStatus = 'N'";

        // ============================================================
        // READ
        // ============================================================

        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var rows = await db.PmQueryRowsAsync(HeaderSelect + " ORDER BY m.MTransNo DESC", Int("@SubscID", subscID));
                await AttachQtyByUnit(rows, subscID);
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetFull/{id}/{subscID}")]
        public async Task<IActionResult> MstGetFull(int id, int subscID)
        {
            if (id <= 0 || subscID <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var head = await db.PmQueryRowsAsync(HeaderSelect + " AND m.MTransNo = @MTransNo",
                    Int("@SubscID", subscID), Int("@MTransNo", id));
                if (head.Count == 0) return NotFoundResult("Opening stock document not found.");
                await AttachQtyByUnit(head, subscID);
                var lines = await db.PmQueryRowsAsync(@"
                    SELECT  d.TransSlNo AS transSlNo, d.SlNo AS slNo, d.PartNo AS partNo,
                            ISNULL(d.PartCode, '') AS partCode, ISNULL(d.PartName, '') AS partName,
                            ISNULL(d.GradeNo, 0) AS gradeNo, d.Grade AS grade, ISNULL(d.Unit, '') AS unit,
                            d.Qty AS qty, d.Rate AS rate, d.Value AS value,
                            ISNULL(d.BinNo, 0) AS binNo, ISNULL(d.Store, '') AS store, ISNULL(d.Location, '') AS location,
                            ISNULL(d.HeatNo, '') AS heatNo, ISNULL(d.TCNo, '') AS tcNo, ISNULL(d.Remarks, '') AS remarks
                      FROM  dbo.OpeningStockDtl d
                     WHERE  d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'
                     ORDER  BY d.SlNo", Int("@MTransNo", id));
                return OkResult(new { header = head[0], lines });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> MstGetNextDocNo(int subscID, [FromQuery] string? docSeries, [FromQuery] int docYear)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries) || docYear <= 0) return BadRequestResult("Doc Series and Doc Year are required.");
            try
            {
                var rows = await db.PmQueryRowsAsync(@"
                    SELECT ISNULL(MAX(DocNo), 0) + 1 AS nextNo FROM dbo.OpeningStockMst
                     WHERE SubscID = @SubscID AND DocSeries = @DocSeries AND DocYear = @DocYear",
                    Int("@SubscID", subscID), Str("@DocSeries", docSeries.Trim(), 10), Int("@DocYear", docYear));
                return OkResult(Convert.ToInt32(rows[0]["nextNo"]));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Lines of the OTHER approved openings of the same stock type (and the
        /// same OA for customer material) — the page warns before a part +
        /// grade + heat is counted twice.
        /// </summary>
        [HttpGet("Dtl/ApprovedKeys/{subscID}")]
        public async Task<IActionResult> DtlApprovedKeys(int subscID, [FromQuery] int excludeMTransNo = 0,
            [FromQuery] string? stockType = "OWN", [FromQuery] int customerNo = 0, [FromQuery] int oaYear = 0, [FromQuery] int oaNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var rows = await db.PmQueryRowsAsync(@"
                    SELECT  d.PartNo AS partNo, ISNULL(d.PartCode, '') AS partCode, d.Grade AS grade,
                            ISNULL(d.HeatNo, '') AS heatNo,
                            CONCAT(RTRIM(m.DocSeries), ' / ', m.DocNo) AS docLabel,
                            CONVERT(char(10), m.AsOnDate, 23) AS asOnDate
                      FROM  dbo.OpeningStockMst m
                      JOIN  dbo.OpeningStockDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                     WHERE  m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                       AND  m.MTransNo <> @Exclude AND m.StockType = @Type
                       AND  (@Type = 'OWN' OR (m.CustomerNo = @CustomerNo AND m.OAYear = @OAYear AND m.OANo = @OANo))",
                    Int("@SubscID", subscID), Int("@Exclude", excludeMTransNo),
                    Str("@Type", string.Equals(stockType, "CUSTOMER", StringComparison.OrdinalIgnoreCase) ? "CUSTOMER" : "OWN", 10),
                    Int("@CustomerNo", customerNo), Int("@OAYear", oaYear), Int("@OANo", oaNo));
                return OkResult(rows);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Every active part with its grades: the Excel upload checks the file
        /// against it, and it carries the heat-traced flag (PartMaster.MfrLicNo
        /// 'Y') that the Part dropdown does not return.
        /// </summary>
        [HttpGet("Lookup/PartGrades/{subscID}")]
        public async Task<IActionResult> LookupPartGrades(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var rows = await db.PmQueryRowsAsync(@"
                    SELECT  p.MTransNo AS partNo, p.PartCode AS partCode, p.PartName AS partName,
                            ISNULL(p.Category, '') AS category, ISNULL(p.PrimaryUnit, '') AS unit,
                            CAST(CASE WHEN UPPER(ISNULL(p.MfrLicNo, '')) = 'Y' THEN 1 ELSE 0 END AS BIT) AS heatTraced,
                            g.GradeNo AS gradeNo, LTRIM(RTRIM(g.Grade)) AS grade
                      FROM  dbo.PartMaster p
                      JOIN  dbo.MaterialGrade g ON g.PartNo = p.MTransNo AND g.SubscID = p.SubscID
                                               AND g.DeleteStatus = 'N' AND ISNULL(LTRIM(RTRIM(g.Grade)), '') <> ''
                     WHERE  p.SubscID = @SubscID AND p.DeleteStatus = 'N' AND p.LockStatus = 'Y'
                     ORDER  BY p.PartCode, g.Grade", Int("@SubscID", subscID));
                return OkResult(rows);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // WRITE
        // ============================================================

        [HttpPost("Mst/SaveFull")]
        public async Task<IActionResult> MstSaveFull([FromBody] OpeningStockSaveRequest req)
        {
            var h = req?.Header;
            if (h == null) return BadRequestResult("Invalid request.");
            if (h.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            var userNo = h.ModifiedBy > 0 ? h.ModifiedBy : h.CreatedBy;
            if (userNo <= 0) return BadRequestResult("Invalid User.");
            if (h.Status == "Update" && h.MTransNo <= 0) return BadRequestResult("Invalid MTransNo.");
            if (h.Status != "Update") h.MTransNo = 0;
            var lines = req!.Lines ?? new List<OpeningStockLine>();
            if (lines.Count == 0) return BadRequestResult("Add at least one line.");
            if (lines.Count > 5000) return BadRequestResult("At most 5000 lines can be saved on one document. Split the count.");

            try
            {
                var r = await db.CallOpeningStockSaveSP(h, lines, userNo, ClientIp);
                if (r.ID <= 0) return ConflictResult(r.Message);
                return OkResult(new { id = r.ID, docNo = r.DocNo }, r.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(int id, [FromQuery] int userNo, [FromQuery] int subscID) =>
            RunStatus(id, "Approve", null, userNo, subscID);

        [HttpPost("Mst/Unapprove/{id}")]
        public Task<IActionResult> MstUnapprove(int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string? reason) =>
            RunStatus(id, "Unapprove", reason, userNo, subscID);

        [HttpPost("Mst/DeleteData/{id}")]
        public Task<IActionResult> MstDeleteData(int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string? reason) =>
            RunStatus(id, "Delete", reason, userNo, subscID);

        private async Task<IActionResult> RunStatus(int id, string action, string? reason, int userNo, int subscID)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0) return BadRequestResult("Invalid input parameters.");
            try
            {
                var r = await db.CallOpeningStockStatusSP(id, action, reason, userNo, ClientIp, subscID);
                if (r.ID <= 0) return ConflictResult(r.Message);
                return OkResult(new { id = r.ID }, r.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>totalQty = { unit: qty } per document (bar in Kg, forgings in Nos …).</summary>
        private async Task AttachQtyByUnit(List<Dictionary<string, object?>> heads, int subscID)
        {
            if (heads.Count == 0) return;
            var sums = await db.PmQueryRowsAsync(@"
                SELECT d.MTransNo AS mTransNo, ISNULL(NULLIF(d.Unit, ''), '-') AS unit, SUM(d.Qty) AS qty
                  FROM dbo.OpeningStockDtl d
                  JOIN dbo.OpeningStockMst m ON m.MTransNo = d.MTransNo AND m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                 WHERE d.DeleteStatus = 'N'
                 GROUP BY d.MTransNo, ISNULL(NULLIF(d.Unit, ''), '-')", Int("@SubscID", subscID));
            var byDoc = sums.GroupBy(s => Convert.ToInt32(s["mTransNo"]))
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => Convert.ToString(x["unit"]) ?? "-", x => Convert.ToDecimal(x["qty"])));
            foreach (var h in heads)
                h["totalQty"] = byDoc.TryGetValue(Convert.ToInt32(h["mTransNo"]), out var m) ? m : new Dictionary<string, decimal>();
        }

        private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
        private static SqlParameter Str(string name, string value, int size) => new(name, SqlDbType.VarChar, size) { Value = value };
    }
}
