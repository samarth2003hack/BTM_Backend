using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Customer Material Inward — EMSSoft/Modules/Store/CustomerMaterialInward.html
    /// (SQL/60_CustomerMaterialInward.sql).
    ///
    /// Job work: the customer's own steel bar arrives on the customer's DC and
    /// is recorded against the customer's OA. It is never our stock — it is
    /// the second owner in the one stock ledger (VW_CustomerMaterialStock).
    ///
    ///     GetList/{subscID}                    every live inward (header + line roll-up)
    ///     GetFullData/{id}/{subscID}           header + lines
    ///     GetMaxNo?docDate&amp;subscID              preview of the next CMI-&lt;FY&gt;-&lt;no&gt;
    ///     Mst/SaveData                         header, Status 'Insert' | 'Update' → "OK^id"
    ///     Dtl/SaveData                         lines JSON, replace-all → "OK^id"
    ///     DeleteData/{id}?userNo&amp;subscID&amp;reason soft delete, reason required
    ///     Balance/{subscID}?customerNo&amp;oaYear&amp;oaNo  customer-owned balance
    ///     BalanceByOA/{subscID}                balance per OA x RM part x grade x dia (planning)
    ///     Lookup/OAList/{subscID}?customerNo   OAs that can receive customer bar
    ///     Lookup/OALines/{subscID}?oaYear&amp;oaNo the lines of one OA
    ///
    /// Parts and grades come from the existing PartMaster/GetDropdown and
    /// MaterialGrade/GetDropdown endpoints.
    ///
    /// OADtl.CustomerMaterial ('Y'/'N', SQL 58) may not exist on every server:
    /// it is read only when COL_LENGTH says so. With it, the OA picker offers
    /// the OAs that have a customer-material line (not rejected); without it,
    /// every approved OA (LockStatus 'Y').
    /// Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    [Route("api/[controller]")]
    public class CustomerMaterialInwardController : EmsControllerBase
    {
        private readonly EMSContext db;
        public CustomerMaterialInwardController(EMSContext context) => db = context;

        // One row per live inward, with its line count and total kg.
        private const string InwardSelect = @"
            SELECT  m.MTransNo                              AS mTransNo,
                    m.DocType                               AS docType,
                    m.InwardNo                              AS inwardNo,
                    m.DocYear                               AS docYear,
                    m.DocNo                                 AS docNo,
                    CONVERT(char(10), m.InwardDate, 23)     AS inwardDate,
                    m.CustomerNo                            AS customerNo,
                    m.CustomerName                          AS customerName,
                    m.OAYear                                AS oaYear,
                    m.OANo                                  AS oaNo,
                    CONCAT(m.OAYear, '-', m.OANo)           AS oaLabel,
                    m.DCNo                                  AS dcNo,
                    CONVERT(char(10), m.DCDate, 23)         AS dcDate,
                    m.ReceivedBy                            AS receivedBy,
                    m.Remarks                               AS remarks,
                    m.DocStatus                             AS docStatus,
                    ISNULL(t.LineCount, 0)                  AS lineCount,
                    ISNULL(t.TotalQty, 0)                   AS totalQty,
                    CONVERT(char(10), m.CreatedOn, 23)      AS createdOn
              FROM  dbo.CustMatInwardMst m
             OUTER APPLY (SELECT COUNT(*) AS LineCount, SUM(d.Qty) AS TotalQty
                            FROM dbo.CustMatInwardDtl d
                           WHERE d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N') t
             WHERE  m.DeleteStatus = 'N'";

        private const string LineSelect = @"
            SELECT  d.TransSlNo                 AS transSlNo,
                    d.SlNo                      AS slNo,
                    d.PartNo                    AS partNo,
                    p.PartCode                  AS partCode,
                    p.PartName                  AS partName,
                    d.GradeNo                   AS gradeNo,
                    d.Grade                     AS grade,
                    d.BarDia                    AS barDia,
                    d.Qty                       AS qty,
                    d.Unit                      AS unit,
                    d.HeatNo                    AS heatNo,
                    d.TCNo                      AS tcNo,
                    d.OAID                      AS oaID,
                    d.OALinkNo                  AS oaLinkNo,
                    od.PartCode                 AS oaPartCode,
                    od.PartDescription          AS oaPartName,
                    d.Remarks                   AS remarks
              FROM  dbo.CustMatInwardDtl d
              LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
              LEFT JOIN dbo.OADtl od     ON od.TransSlNo = d.OALinkNo
             WHERE  d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'
             ORDER  BY d.SlNo";

        private const string BalanceSelect = @"
            SELECT  v.CustomerNo        AS customerNo,
                    v.CustomerName      AS customerName,
                    v.OAYear            AS oaYear,
                    v.OANo              AS oaNo,
                    CONCAT(v.OAYear, '-', v.OANo) AS oaLabel,
                    v.PartNo            AS partNo,
                    v.PartCode          AS partCode,
                    v.PartName          AS partName,
                    v.GradeNo           AS gradeNo,
                    v.Grade             AS grade,
                    v.BarDia            AS barDia,
                    v.Unit              AS unit,
                    v.ReceivedQty       AS receivedQty,
                    v.IssuedQty         AS issuedQty,
                    v.ReturnedQty       AS returnedQty,
                    v.BalanceQty        AS balanceQty,
                    v.InwardCount       AS inwardCount,
                    CONVERT(char(10), v.LastInwardDate, 23) AS lastInwardDate
              FROM  dbo.VW_CustomerMaterialStock v
             WHERE  v.SubscID = @SubscID";

        // ============================================================
        // READ
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return Task.FromResult(BadRequestResult("Invalid Subscription."));
            return RunQuery(InwardSelect + " AND m.SubscID = @SubscID ORDER BY m.MTransNo DESC",
                Int("@SubscID", subscID));
        }

        [HttpGet("GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> GetFullData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid inward.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var header = (await db.PmQueryRowsAsync(
                    InwardSelect + " AND m.MTransNo = @MTransNo AND m.SubscID = @SubscID",
                    Int("@MTransNo", id), Int("@SubscID", subscID))).FirstOrDefault();
                if (header == null) return NotFoundResult("Customer material inward not found.");

                header["lines"] = await db.PmQueryRowsAsync(LineSelect, Int("@MTransNo", id));
                return OkResult(header);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Preview only — SP_CustMatInwardMst allocates the number under a lock.</summary>
        [HttpGet("GetMaxNo")]
        public async Task<IActionResult> GetMaxNo([FromQuery] DateTime? docDate, [FromQuery] int subscID, [FromQuery] string? docType = "I")
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            var type = string.Equals(docType, "R", StringComparison.OrdinalIgnoreCase) ? "R" : "I";
            try
            {
                var rows = await db.PmQueryRowsAsync(@"
                    DECLARE @Year INT = dbo.FN_FYCode(@Date);
                    SELECT CONCAT(CASE WHEN @Type = 'R' THEN 'CMR-' ELSE 'CMI-' END, @Year, '-',
                                  RIGHT('00000' + CAST(ISNULL(MAX(DocNo), 0) + 1 AS VARCHAR(10)), 5)) AS no
                      FROM dbo.CustMatInwardMst WHERE SubscID = @SubscID AND DocYear = @Year AND DocType = @Type",
                    new SqlParameter("@Date", SqlDbType.Date) { Value = (docDate ?? DateTime.Today).Date },
                    Int("@SubscID", subscID),
                    new SqlParameter("@Type", SqlDbType.Char, 1) { Value = type });
                return OkResult(rows.Count > 0 ? Convert.ToString(rows[0]["no"]) : "");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // BALANCE — VW_CustomerMaterialStock (customer-owned, never our stock)
        // ============================================================

        [HttpGet("Balance/{subscID}")]
        public Task<IActionResult> Balance(
            int subscID,
            [FromQuery] int customerNo = 0,
            [FromQuery] int oaYear = 0,
            [FromQuery] int oaNo = 0)
        {
            if (subscID <= 0) return Task.FromResult(BadRequestResult("Invalid Subscription."));
            return RunQuery(BalanceSelect + @"
                   AND (@CustomerNo = 0 OR v.CustomerNo = @CustomerNo)
                   AND (@OAYear = 0 OR v.OAYear = @OAYear)
                   AND (@OANo = 0 OR v.OANo = @OANo)
                 ORDER BY v.CustomerName, v.OAYear DESC, v.OANo DESC, v.PartCode, v.Grade, v.BarDia",
                Int("@SubscID", subscID), Int("@CustomerNo", customerNo),
                Int("@OAYear", oaYear), Int("@OANo", oaNo));
        }

        /// <summary>
        /// Customer bar on hand per OA x RM part x grade x dia — what the RM
        /// Requirement report uses, instead of our stock, for OA lines flagged
        /// CustomerMaterial. Rows with a zero balance are left out.
        /// </summary>
        [HttpGet("BalanceByOA/{subscID}")]
        public Task<IActionResult> BalanceByOA(int subscID)
        {
            if (subscID <= 0) return Task.FromResult(BadRequestResult("Invalid Subscription."));
            return RunQuery(@"
                SELECT  v.OAYear        AS oaYear,
                        v.OANo          AS oaNo,
                        v.CustomerNo    AS customerNo,
                        v.PartNo        AS partNo,
                        v.PartCode      AS partCode,
                        v.GradeNo       AS gradeNo,
                        v.Grade         AS grade,
                        v.BarDia        AS barDia,
                        v.Unit          AS unit,
                        v.BalanceQty    AS balanceKg
                  FROM  dbo.VW_CustomerMaterialStock v
                 WHERE  v.SubscID = @SubscID AND v.BalanceQty <> 0
                 ORDER  BY v.OAYear, v.OANo, v.PartCode, v.Grade, v.BarDia",
                Int("@SubscID", subscID));
        }

        // ============================================================
        // LOOKUPS — the OA side (parts / grades: PartMaster, MaterialGrade)
        // ============================================================

        /// <summary>
        /// OAs (live, current revision) that can receive customer bar, newest
        /// first: with OADtl.CustomerMaterial — those with a 'Y' line, not
        /// rejected; without the column — every approved OA.
        /// </summary>
        [HttpGet("Lookup/OAList/{subscID}")]
        public async Task<IActionResult> LookupOAList(int subscID, [FromQuery] int customerNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var hasFlag = await HasCustomerMaterialColumn();
                var rows = await db.PmQueryRowsAsync(OAListSql(hasFlag) + @"
                       AND (@CustomerNo = 0 OR m.VendorNo = @CustomerNo)
                     ORDER BY m.DocYear DESC, m.DocNo DESC",
                    Int("@SubscID", subscID), Int("@CustomerNo", customerNo));
                return OkResult(rows, hasFlag
                    ? "OAs with customer-material lines."
                    : "Approved OAs (customer-material flag not installed).");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Lookup/OALines/{subscID}")]
        public async Task<IActionResult> LookupOALines(int subscID, [FromQuery] int oaYear, [FromQuery] int oaNo)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (oaYear <= 0 || oaNo <= 0) return BadRequestResult("OA Year and OA No are required.");
            try
            {
                var flag = await HasCustomerMaterialColumn() ? "d.CustomerMaterial" : "CAST(NULL AS CHAR(1))";
                var rows = await db.PmQueryRowsAsync($@"
                    SELECT  CAST(d.SlNo AS INT)     AS oaID,
                            d.TransSlNo             AS oaLinkNo,
                            d.PartNo                AS partNo,
                            d.PartCode              AS partCode,
                            d.PartDescription       AS partName,
                            d.MocCode               AS grade,
                            d.Qty                   AS qty,
                            d.Unit                  AS unit,
                            {flag}                  AS customerMaterial
                      FROM  dbo.OAMst m
                      JOIN  dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                     WHERE  m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND  m.DocYear = @OAYear AND m.DocNo = @OANo
                     ORDER  BY d.SlNo",
                    Int("@SubscID", subscID), Int("@OAYear", oaYear), Int("@OANo", oaNo));
                return OkResult(rows);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SAVE / DELETE
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] CustMatInwardSaveRequest r)
        {
            if (r == null) return BadRequestResult("Invalid data.");
            if (r.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (r.UserNo <= 0) return BadRequestResult("Invalid user.");
            var isUpdate = string.Equals(r.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (!isUpdate && !string.Equals(r.Status, "Insert", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Status must be Insert or Update.");
            if (isUpdate && r.MTransNo <= 0) return BadRequestResult("Invalid inward.");
            var docType = string.Equals(r.DocType, "R", StringComparison.OrdinalIgnoreCase) ? "R" : "I";
            if (r.InwardDate == null) return BadRequestResult(docType == "R" ? "Return Date is required." : "Inward Date is required.");
            if (r.OAYear <= 0 || r.OANo <= 0) return BadRequestResult("Select the Order Acceptance.");
            if (string.IsNullOrWhiteSpace(r.DCNo)) return BadRequestResult(docType == "R" ? "Our DC No is required." : "Customer DC No is required.");
            if (r.DCDate == null) return BadRequestResult("DC Date is required.");

            try
            {
                // A new inward goes only against an OA the picker would offer.
                if (!isUpdate)
                {
                    var hasFlag = await HasCustomerMaterialColumn();
                    var ok = await db.PmQueryRowsAsync(OAListSql(hasFlag) +
                        " AND m.DocYear = @OAYear AND m.DocNo = @OANo",
                        Int("@SubscID", r.SubscID), Int("@OAYear", r.OAYear), Int("@OANo", r.OANo));
                    if (ok.Count == 0)
                        return ConflictResult(hasFlag
                            ? $"OA {r.OAYear}-{r.OANo} has no line marked for customer material."
                            : $"OA {r.OAYear}-{r.OANo} is not an approved order acceptance.");
                }

                var result = await db.CallCustMatInwardMstSP(new SP_CustMatInwardMst
                {
                    Status = isUpdate ? "Update" : "Insert",
                    MTransNo = isUpdate ? r.MTransNo : 0,
                    InwardDate = r.InwardDate.Value.Date,
                    CustomerNo = r.CustomerNo,
                    OAYear = r.OAYear,
                    OANo = r.OANo,
                    DCNo = Cut(r.DCNo, 30),
                    DCDate = r.DCDate.Value.Date,
                    ReceivedBy = Cut(r.ReceivedBy, 100),
                    Remarks = Cut(r.Remarks, 250),
                    DocStatus = string.IsNullOrWhiteSpace(r.DocStatus) ? (docType == "R" ? "Returned" : "Received") : r.DocStatus.Trim(),
                    DocType = docType,
                    SystemIP = ClientIp,
                    UserNo = r.UserNo,
                    SubscID = r.SubscID,
                });

                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", isUpdate ? "Record Edited Successfully" : "Record Saved Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] CustMatInwardLinesRequest r)
        {
            if (r == null) return BadRequestResult("Invalid data.");
            if (r.MTransNo <= 0) return BadRequestResult("Invalid inward.");
            if (r.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (r.UserNo <= 0) return BadRequestResult("Invalid user.");
            if (r.Lines == null || r.Lines.Count == 0) return BadRequestResult("Add at least one material line.");

            try
            {
                var json = JsonSerializer.Serialize(r.Lines.Select(l => new
                {
                    partNo  = l.PartNo,
                    gradeNo = l.GradeNo,
                    grade   = (l.Grade ?? "").Trim(),
                    barDia  = l.BarDia,
                    qty     = l.Qty,
                    unit    = (l.Unit ?? "").Trim(),
                    heatNo  = (l.HeatNo ?? "").Trim(),
                    tcNo    = (l.TCNo ?? "").Trim(),
                    oaID    = l.OAID,
                    remarks = (l.Remarks ?? "").Trim()
                }));

                var result = await db.CallCustMatInwardDtlSaveSP(r.MTransNo, json, r.UserNo, r.SubscID);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string? reason = null)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0) return BadRequestResult("Invalid input parameters.");
            if (string.IsNullOrWhiteSpace(reason)) return BadRequestResult("Enter the reason for deleting.");

            try
            {
                var result = await db.CallCustMatInwardMstSP(new SP_CustMatInwardMst
                {
                    Status = "Delete",
                    MTransNo = id,
                    InwardDate = DateTime.Today,
                    UserNo = userNo,
                    DeleteReason = Cut(reason, 250),
                    SystemIP = ClientIp,
                    SubscID = subscID,
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", "Record Deleted Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // Shared internals
        // ============================================================

        /// <summary>
        /// SELECT … FROM/WHERE of the OAs a customer-material inward may go
        /// against (filter on alias m; @SubscID required). The rule is
        /// shared by the picker and the insert check.
        /// </summary>
        private static string OAListSql(bool hasFlag)
        {
            var cmCount = hasFlag
                ? "(SELECT COUNT(*) FROM dbo.OADtl x WHERE x.MTransNo = m.MTransNo AND x.DeleteStatus = 'N' AND x.CustomerMaterial = 'Y')"
                : "CAST(0 AS INT)";
            var rule = hasFlag
                ? @" AND ISNULL(m.LockStatus, 'N') <> 'R'
                     AND EXISTS (SELECT 1 FROM dbo.OADtl x WHERE x.MTransNo = m.MTransNo
                                    AND x.DeleteStatus = 'N' AND x.CustomerMaterial = 'Y')"
                : " AND m.LockStatus = 'Y'";
            return $@"
                SELECT  m.MTransNo                          AS oaTransNo,
                        m.DocYear                           AS oaYear,
                        m.DocNo                             AS oaNo,
                        CONCAT(m.DocYear, '-', m.DocNo)     AS oaLabel,
                        CONVERT(char(10), m.DocDate, 23)    AS oaDate,
                        m.VendorNo                          AS customerNo,
                        m.VendorName                        AS customerName,
                        m.RefNo                             AS customerPONo,
                        m.LockStatus                        AS lockStatus,
                        (SELECT COUNT(*) FROM dbo.OADtl x WHERE x.MTransNo = m.MTransNo AND x.DeleteStatus = 'N') AS lineCount,
                        {cmCount}                           AS customerMaterialLines
                  FROM  dbo.OAMst m
                 WHERE  m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'{rule}";
        }

        /// <summary>True once SQL 58 has added OADtl.CustomerMaterial.</summary>
        private async Task<bool> HasCustomerMaterialColumn()
        {
            var rows = await db.PmQueryRowsAsync(
                "SELECT CASE WHEN COL_LENGTH('dbo.OADtl', 'CustomerMaterial') IS NULL THEN 0 ELSE 1 END AS ok");
            return rows.Count > 0 && Convert.ToInt32(rows[0]["ok"]) == 1;
        }

        /// <summary>Read wrapped in the response envelope.</summary>
        private async Task<IActionResult> RunQuery(string sql, params SqlParameter[] parameters)
        {
            try
            {
                return OkResult(await db.PmQueryRowsAsync(sql, parameters));
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        private static SqlParameter Int(string name, int value) =>
            new(name, SqlDbType.Int) { Value = value };

        private static string? Cut(string? value, int max)
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return null;
            return v.Length > max ? v[..max] : v;
        }
    }
}
