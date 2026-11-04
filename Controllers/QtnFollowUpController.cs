using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Quotation Follow-Up — EMSSoft/Modules/Sales/FollowUp.html.
    ///
    /// Shares the api/Qtn prefix with QtnController so the page reads as one
    /// API: FollowUp/* is the QtnFollowUp child table, Mst/Truncate closes a
    /// quotation nobody will order against (DocStatus 'T').
    ///
    /// Rules (SQL/50_QuotationFollowUp.sql):
    ///   - An OPEN quotation is the live, current revision with DocStatus 'N'
    ///     and no live OA against any of its revisions.
    ///   - Follow-ups belong to the quotation DOCUMENT (SubscID + DocSeries +
    ///     DocYear + DocNo), so an amendment keeps its history.
    ///   - Dates go out as plain "YYYY-MM-DD" so the browser never shifts them.
    /// </summary>
    [Route("api/Qtn")]
    public class QtnFollowUpController : EmsControllerBase
    {
        private readonly EMSContext db;

        public QtnFollowUpController(EMSContext context) => db = context;

        // Every revision of the quotation "c" — the join that makes the log
        // span amendments.
        private const string SameDocument = @"
                      r.SubscID   = c.SubscID
                  AND r.DocSeries = c.DocSeries
                  AND r.DocYear   = c.DocYear
                  AND r.DocNo     = c.DocNo";

        // ============================================================
        // PENDING — the chase list
        // ============================================================

        /// <summary>
        /// Every open quotation with its follow-up roll-up (count, last
        /// follow-up date, and the next date set by the latest entry).
        /// </summary>
        [HttpGet("FollowUp/GetPending/{subscID}")]
        public Task<IActionResult> FollowUpGetPending(int subscID)
        {
            if (subscID <= 0)
                return Task.FromResult(BadRequestResult("Invalid Subscription."));

            return RunQuery(@"
                SELECT
                    c.MTransNo                              AS mTransNo,
                    c.DocSeries                             AS docSeries,
                    c.DocYear                               AS docYear,
                    c.DocNo                                 AS docNo,
                    c.DocAmdNo                              AS docAmdNo,
                    CONVERT(char(10), c.DocDate, 23)        AS docDate,
                    CONVERT(char(10), c.TargetDate, 23)     AS targetDate,
                    c.VendorName                            AS vendorName,
                    c.CPName                                AS cpName,
                    ISNULL(NULLIF(c.CPPhone, ''), c.PhoneNo) AS cpPhone,
                    c.EmailID                               AS emailID,
                    c.SalesExec                             AS salesExec,
                    c.RefNo                                 AS refNo,
                    c.RefProject                            AS refProject,
                    ISNULL(c.NetValue, 0)                   AS netValue,
                    c.TxnCurrency                           AS txnCurrency,
                    c.DocStatus                             AS docStatus,
                    c.LockStatus                            AS lockStatus,
                    ISNULL(s.FUCount, 0)                    AS fuCount,
                    CONVERT(char(10), s.LastFollowUp, 23)   AS lastFollowUp,
                    CONVERT(char(10), n.NextVisitDate, 23)  AS nextFollowUp
                FROM dbo.QtnMst c
                OUTER APPLY (
                    SELECT COUNT(*) AS FUCount, MAX(f.FollowupDate) AS LastFollowUp
                      FROM dbo.QtnFollowUp f
                      JOIN dbo.QtnMst r ON r.MTransNo = f.MTransNo
                     WHERE f.DeleteStatus = 'N' AND " + SameDocument + @"
                ) s
                OUTER APPLY (
                    -- The latest entry's next date; an older one's is superseded.
                    SELECT TOP (1) f.NextVisitDate
                      FROM dbo.QtnFollowUp f
                      JOIN dbo.QtnMst r ON r.MTransNo = f.MTransNo
                     WHERE f.DeleteStatus = 'N' AND " + SameDocument + @"
                     ORDER BY f.FollowupDate DESC, f.TransSlNo DESC
                ) n
                WHERE c.SubscID      = @SubscID
                  AND c.DeleteStatus = 'N'
                  AND c.DocAmdStatus = 'N'
                  AND c.DocStatus    = 'N'
                  AND NOT EXISTS (
                        SELECT 1
                          FROM dbo.OAMst o
                          JOIN dbo.QtnMst r ON r.MTransNo = o.TargetTransNo
                         WHERE o.SubscID = c.SubscID AND o.DeleteStatus = 'N' AND " + SameDocument + @")
                ORDER BY c.MTransNo DESC",
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });
        }

        // ============================================================
        // FOLLOWUP — the log of one quotation
        // ============================================================

        /// <summary>
        /// Every follow-up of the quotation <paramref name="mTransNo"/> belongs
        /// to, across all its revisions, newest first.
        /// </summary>
        [HttpGet("FollowUp/GetList/{mTransNo}/{subscID}")]
        public Task<IActionResult> FollowUpGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0)
                return Task.FromResult(BadRequestResult("Invalid Quotation."));
            if (subscID <= 0)
                return Task.FromResult(BadRequestResult("Invalid Subscription."));

            return RunQuery(@"
                SELECT
                    f.TransSlNo                             AS transSlNo,
                    f.SLNo                                  AS slNo,
                    f.MTransNo                              AS mTransNo,
                    r.DocAmdNo                              AS docAmdNo,
                    CONVERT(char(10), f.FollowupDate, 23)   AS followupDate,
                    f.FollowUpType                          AS followUpType,
                    f.ContactPerson                         AS contactPerson,
                    f.Response                              AS response,
                    CONVERT(char(10), f.NextVisitDate, 23)  AS nextVisitDate,
                    f.FollowupNotes                         AS followupNotes,
                    f.CreatedBy                             AS createdBy,
                    ISNULL(u.UserName, '')                  AS createdByName,
                    f.CreatedOn                             AS createdOn,
                    f.ModifiedOn                            AS modifiedOn
                FROM dbo.QtnMst c
                JOIN dbo.QtnMst r      ON " + SameDocument + @"
                JOIN dbo.QtnFollowUp f ON f.MTransNo = r.MTransNo AND f.DeleteStatus = 'N'
                LEFT JOIN dbo.UserMaster u ON u.MTransNo = f.CreatedBy
                WHERE c.MTransNo = @MTransNo
                  AND c.SubscID  = @SubscID
                ORDER BY f.FollowupDate DESC, f.TransSlNo DESC",
                new SqlParameter("@MTransNo", SqlDbType.Int) { Value = mTransNo },
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });
        }

        [HttpPost("FollowUp/SaveData")]
        public async Task<IActionResult> FollowUpSaveData([FromBody] SP_QtnFollowUp dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (dto.FollowupDate == null)
                return BadRequestResult("Follow-up Date is required.");
            if (string.IsNullOrWhiteSpace(dto.FollowUpType))
                return BadRequestResult("Medium is required.");
            if (string.IsNullOrWhiteSpace(dto.Response))
                return BadRequestResult("Response is required.");
            if (string.IsNullOrWhiteSpace(dto.FollowupNotes))
                return BadRequestResult("Notes / Remarks are required.");
            if (dto.FollowupNotes.Trim().Length > 2000)
                return BadRequestResult("Notes / Remarks cannot exceed 2000 characters.");
            if (dto.ContactPerson != null && dto.ContactPerson.Trim().Length > 100)
                return BadRequestResult("Spoke With cannot exceed 100 characters.");
            if (dto.NextVisitDate != null && dto.NextVisitDate.Value.Date < dto.FollowupDate.Value.Date)
                return BadRequestResult("The next follow-up cannot be before the follow-up itself.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid follow-up.");

            try
            {
                dto.SystemIP = ClientIp;
                // The server clock stamps the audit columns, never the browser's.
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                var result = await db.CallQtnFollowUpSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("FollowUp/DeleteData/{transSlNo}")]
        public async Task<IActionResult> FollowUpDeleteData(
            int transSlNo,
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (transSlNo <= 0 || mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallQtnFollowUpSP(new SP_QtnFollowUp
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
                    SystemIP     = ClientIp,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(),
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // MST — TRUNCATE
        // ============================================================

        /// <summary>
        /// DocStatus 'N' → 'T' on the current revision, keeping the reason.
        /// The quotation and its follow-ups are kept; it just leaves the list.
        /// </summary>
        [HttpPost("Mst/Truncate/{id}")]
        public async Task<IActionResult> MstTruncate(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (reason != null && reason.Trim().Length > 500)
                return BadRequestResult("Reason cannot exceed 500 characters.");

            try
            {
                var result = await db.CallQtnTruncateSP(
                    id, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                    ClientIp, userNo, subscID);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID, DocStatus = "T" }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Runs one read-only query and returns its rows as column-name → value
        /// maps; strings are trimmed (DocSeries is CHAR(10)).
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
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = value is string s ? s.Trim() : value;
                    }
                    rows.Add(row);
                }

                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
