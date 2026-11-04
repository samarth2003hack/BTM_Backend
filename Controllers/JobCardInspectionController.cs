using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Job Card Inspection — EMSSoft/Modules/Production/JobCardInspection.html
    /// (and the same page under Modules/Quality).
    ///
    ///     GetList/{subscID}                          every live inspection
    ///     GetData/{id}/{subscID}                     one inspection
    ///     GetByJobCard?MTransNo                      a job card's inspection history
    ///     GetMaxNo?DocDate&amp;SubscID                   preview of the next INS-&lt;FY&gt;-&lt;no&gt;
    ///     SaveData                                   Status 'Insert' | 'Update'
    ///     DeleteData/{id}?userNo&amp;subscID&amp;reason      soft delete (HTTP DELETE)
    ///
    /// A job card is inspected at its route's last "Inspection" step (its last
    /// step when the route has none). The pieces inspected across all its
    /// inspections may never exceed what reached that step; each inspection
    /// splits into OK + Rework + Rejected = Inspected. SP_JobCardInspection
    /// re-checks the cap under a lock. Rejected pieces carry a reason from
    /// Other Master 'Rejection Reason' (SQL 58) — required whenever that list
    /// has rows for the subscription.
    /// </summary>
    [Route("api/[controller]")]
    public class JobCardInspectionController : JobCardControllerBase
    {
        public JobCardInspectionController(EMSContext context) : base(context) { }

        private const string InspectionSelect = @"
            SELECT  x.MTransNo                          AS mTransNo,
                    x.InspectionNo                      AS inspectionNo,
                    CONVERT(char(10), x.InspDate, 23)   AS inspDate,
                    x.JobCardTransNo                    AS jobCardNo,
                    j.JobCardNo                         AS jobCardCode,
                    j.Qty                               AS jobQty,
                    j.PartNo                            AS partNo,
                    p.PartCode                          AS partCode,
                    p.PartName                          AS partName,
                    j.Grade                             AS grade,
                    x.InspectedBy                       AS inspectedBy,
                    x.InspectedQty                      AS inspectedQty,
                    x.OKQty                             AS okQty,
                    x.ReworkQty                         AS reworkQty,
                    x.RejectedQty                       AS rejectedQty,
                    ISNULL(x.RejectionReason, '')       AS rejectionReason,
                    x.Remarks                           AS remarks,
                    CONVERT(char(10), x.CreatedOn, 23)  AS createdOn
              FROM  dbo.JobCardInspection x
              JOIN  dbo.JobCardMst j ON j.MTransNo = x.JobCardTransNo
              LEFT JOIN dbo.PartMaster p ON p.MTransNo = j.PartNo
             WHERE  x.DeleteStatus = 'N'";

        [HttpGet("GetList/{subscID}")]
        public Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0) return Task.FromResult(BadRequestResult("Invalid Subscription."));
            return RunQuery(InspectionSelect + " AND x.SubscID = @S ORDER BY x.MTransNo DESC", Int("@S", subscID));
        }

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            try
            {
                var row = await QueryOne(InspectionSelect + " AND x.MTransNo = @M AND x.SubscID = @S",
                    Int("@M", id), Int("@S", subscID));
                return row == null ? NotFoundResult("Inspection not found.") : OkResult(row);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetByJobCard")]
        public Task<IActionResult> GetByJobCard([FromQuery] int MTransNo) =>
            RunQuery(InspectionSelect + " AND x.JobCardTransNo = @M ORDER BY x.InspDate, x.MTransNo",
                Int("@M", MTransNo));

        /// <summary>Preview only — the save allocates the number under a lock.</summary>
        [HttpGet("GetMaxNo")]
        public async Task<IActionResult> GetMaxNo([FromQuery] DateTime? DocDate, [FromQuery] int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var row = await QueryOne(@"
                    DECLARE @Year INT = dbo.FN_FYCode(@Date);
                    SELECT CONCAT('INS-', @Year, '-', RIGHT('0000' + CAST(ISNULL(MAX(DocNo), 0) + 1 AS VARCHAR(10)), 4)) AS No
                      FROM dbo.JobCardInspection WHERE SubscID = @SubscID AND DocYear = @Year",
                    new SqlParameter("@Date", SqlDbType.Date) { Value = (DocDate ?? DateTime.Today).Date },
                    Int("@SubscID", SubscID));
                return OkResult(S(row, "No"));
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] JobCardInspectionSaveRequest r)
        {
            if (r == null) return BadRequestResult("Invalid data.");
            if (r.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (r.CreatedBy <= 0) return BadRequestResult("Invalid user.");
            var isUpdate = string.Equals(r.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (!isUpdate && !string.Equals(r.Status, "Insert", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Status must be Insert or Update.");
            if (isUpdate && r.MTransNo <= 0) return BadRequestResult("Invalid inspection.");

            if (r.JobCardNo <= 0) return ConflictResult("Select a job card.");
            if (r.InspDate == null) return ConflictResult("Inspected Date is required.");
            if (r.InspDate.Value.Date > DateTime.Today) return ConflictResult("Inspected Date cannot be in the future.");
            if (string.IsNullOrWhiteSpace(r.InspectedBy)) return ConflictResult("Select who inspected.");
            if (r.InspectedQty <= 0) return ConflictResult("Inspected Quantity must be greater than zero.");
            if (r.OKQty < 0 || r.ReworkQty < 0 || r.RejectedQty < 0) return ConflictResult("Quantities cannot be negative.");
            if (r.OKQty + r.ReworkQty + r.RejectedQty != r.InspectedQty)
                return ConflictResult(
                    $"OK + Rework + Rejected ({Q(r.OKQty + r.ReworkQty + r.RejectedQty)}) must equal the Inspected Quantity ({Q(r.InspectedQty)}).");

            var reason = r.RejectedQty > 0 ? (r.RejectionReason ?? "").Trim() : "";
            if (reason.Length > 100) reason = reason[..100];

            try
            {
                if (r.RejectedQty > 0 && reason == "")
                {
                    var reasons = await QueryOne(@"
                        SELECT COUNT(*) AS N FROM dbo.OtherMaster
                         WHERE MasterType = 'Rejection Reason' AND SubscID = @S
                           AND DeleteStatus = 'N' AND LockStatus = 'Y'", Int("@S", r.SubscID));
                    if (I(reasons, "N") > 0) return ConflictResult("Select the Rejection Reason for the rejected pieces.");
                }

                // A friendlier refusal than the SP's when the cap is exceeded.
                var jc = await JobCardRow(r.JobCardNo);
                if (jc == null || I(jc, "mTransNo") == 0) return ConflictResult("Select a job card.");
                var already = D(await QueryOne(@"
                    SELECT ISNULL(SUM(InspectedQty), 0) AS Q FROM dbo.JobCardInspection
                     WHERE JobCardTransNo = @J AND DeleteStatus = 'N' AND MTransNo <> @M",
                    Int("@J", r.JobCardNo), Int("@M", isUpdate ? r.MTransNo : 0)), "Q");
                var eligible = D(jc, "inspectionEligibleQty");
                if (already + r.InspectedQty > eligible)
                    return ConflictResult(
                        $"Only {Q(Math.Max(0, eligible - already))} pieces of {S(jc, "jobCardNo")} are awaiting inspection " +
                        $"({Q(eligible)} processed at step {S(jc, "inspectionStepLabel")}, {Q(already)} already inspected).");

                var result = await db.CallJobCardInspectionSP(new SP_JobCardInspection
                {
                    Status = isUpdate ? "Update" : "Insert",
                    MTransNo = isUpdate ? r.MTransNo : 0,
                    InspDate = r.InspDate.Value.Date,
                    JobCardTransNo = r.JobCardNo,
                    InspectedBy = r.InspectedBy!.Trim(),
                    InspectedQty = r.InspectedQty,
                    OKQty = r.OKQty,
                    ReworkQty = r.ReworkQty,
                    RejectedQty = r.RejectedQty,
                    Remarks = string.IsNullOrWhiteSpace(r.Remarks) ? null : r.Remarks.Trim(),
                    SystemIP = ClientIp,
                    UserNo = r.CreatedBy,
                    SubscID = r.SubscID,
                });
                if (result.ID == 0) return ConflictResult(result.Message);

                // Rejection reason (SQL 58). Not wrapped in one transaction with the
                // save: SP_JobCardInspection rolls back its own transaction when it
                // refuses, which would abort an outer one. This is a one-row update
                // of the row just written.
                var plan = await db.CallJobCardInspectionPlanFieldsSP(result.ID, reason, r.CreatedBy, r.SubscID);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                return OkResult($"OK^{result.ID}", isUpdate ? "Record Edited Successfully" : "Record Saved Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpDelete("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string? reason)
        {
            if (id <= 0) return BadRequestResult("Invalid inspection.");
            if (userNo <= 0 || subscID <= 0) return BadRequestResult("Invalid user.");
            if (string.IsNullOrWhiteSpace(reason)) return BadRequestResult("Enter the reason for deleting.");
            try
            {
                var result = await db.CallJobCardInspectionSP(new SP_JobCardInspection
                {
                    Status = "Delete",
                    MTransNo = id,
                    InspDate = DateTime.Today,
                    UserNo = userNo,
                    SubscID = subscID,
                    SystemIP = ClientIp,
                    DeleteReason = reason.Trim().Length > 250 ? reason.Trim()[..250] : reason.Trim(),
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", "Record Deleted Successfully");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
