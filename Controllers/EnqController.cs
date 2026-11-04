using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class EnqController : EmsControllerBase
    {
        private readonly EMSContext db;

        public EnqController(EMSContext context) => db = context;

        // ============================================================
        // MST — ENQUIRY HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_EnqMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.DocAmdNo == null)
                return BadRequestResult("Doc Amendment No is required.");
            if (dto.VendorNo == null || dto.VendorNo <= 0)
                return BadRequestResult("Customer is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid Enquiry.");

            try
            {
                // CreatedOn is NOT NULL on all three Enq tables and the procedures
                // insert @CreatedOn verbatim (the column default never fires), so
                // the server stamps the audit times rather than the client clock.
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                dto.SystemIP = ClientIp;
                var result = await db.CallEnqMstSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Preview only — SP_EnqMst allocates the real DocNo at insert
        /// (MAX(DocNo)+1 per DocSeries + DocYear + SubscID, deleted rows included).
        /// </summary>
        [HttpGet("Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> MstGetNextDocNo(
            int subscID,
            [FromQuery] string docSeries = "",
            [FromQuery] int docYear = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries)) return BadRequestResult("Doc Series is required.");
            if (docYear <= 0) return BadRequestResult("Doc Year is required.");

            try
            {
                var series = docSeries.Trim();
                var max = await db.EnqMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/DeleteData/{id}")]
        public async Task<IActionResult> MstDeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                // An enquiry a live quotation was raised from (QtnMst.TargetTransNo)
                // is the quotation's source document — deleting it would orphan it.
                var quoted = await db.QtnMst.AsNoTracking().AnyAsync(q =>
                    q.TargetTransNo == id && q.SubscID == subscID && q.DeleteStatus == "N");
                if (quoted)
                    return ConflictResult("A quotation has been raised against this enquiry, so it cannot be deleted.");

                var why = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason;
                var result = await db.CallEnqMstSP(new SP_EnqMst
                {
                    MTransNo     = id,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = why,
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);

                // SP_EnqMst soft-deletes only the header; retire its lines and
                // follow-ups with it. "Nothing to delete" (ID 0) is not an error here.
                await db.CallEnqDtlSP(new SP_EnqDtl
                {
                    MTransNo = id, ModifiedBy = userNo, ModifiedOn = DateTime.Now,
                    SubscID = subscID, DeleteReason = why, Status = "DeleteAll"
                });
                await db.CallEnqFollowUpSP(new SP_EnqFollowUp
                {
                    MTransNo = id, ModifiedBy = userNo, ModifiedOn = DateTime.Now,
                    SubscID = subscID, DeleteReason = why, Status = "DeleteAll"
                });

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/LockUnlock/{id}")]
        public async Task<IActionResult> MstLockUnlock(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string action = "Lock")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (action != "Lock" && action != "Unlock")
                return BadRequestResult("Action must be 'Lock' or 'Unlock'.");

            try
            {
                var result = await db.CallEnqMstSP(new SP_EnqMst
                {
                    MTransNo   = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    Status     = action
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? salesExec = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.EnqMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(salesExec))
                    query = query.Where(x => x.SalesExec == salesExec.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search)  ||
                        x.CPName!.Contains(search)      ||
                        x.RefNo!.Contains(search)       ||
                        x.DocNotes!.Contains(search)    ||
                        x.EnqRemarks!.Contains(search));

                var data = await query
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .ThenByDescending(x => x.DocAmdNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DocSeries,
                        x.DocYear,
                        x.DocNo,
                        x.DocDate,
                        x.DocAmdNo,
                        x.DocAmdStatus,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.CPName,
                        x.CPPhone,
                        x.RefNo,
                        x.RefDate,
                        x.DueDate,
                        x.SalesExec,
                        x.EnqSource,
                        x.EnqPriority,
                        x.EnqCategory,
                        x.StateName,
                        x.Zone,
                        x.DrvNo,
                        x.DocStatus,
                        x.LeadStatus,
                        x.Nextfollowupdate,
                        x.LockStatus,
                        x.LockCounter,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn,
                        ItemCount = db.EnqDtl.Count(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EnqMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id      &&
                        x.SubscID      == subscID  &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — ENQUIRY ITEM LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_EnqDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Enquiry.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid line item.");

            try
            {
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                dto.SystemIP = ClientIp;
                var result = await db.CallEnqDtlSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Dtl/DeleteData/{transSlNo}")]
        public async Task<IActionResult> DtlDeleteData(
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
                var result = await db.CallEnqDtlSP(new SP_EnqDtl
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Dtl/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> DtlDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallEnqDtlSP(new SP_EnqDtl
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = "DeleteAll"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Enquiry.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EnqDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SLNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SLNo,
                        x.MTransNo,
                        x.PartNo,
                        x.Description,
                        x.ItemRemarks,
                        x.Qty,
                        x.Unit,
                        x.CustPartNo,
                        x.PartCode,
                        x.PartSysCode,
                        x.PartSysDesc,
                        x.AmdNo,
                        x.AmdMode,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Dtl/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> DtlGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EnqDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo    == transSlNo &&
                        x.SubscID      == subscID   &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // FOLLOWUP — ENQUIRY FOLLOW-UP HISTORY
        // ============================================================

        [HttpPost("FollowUp/SaveData")]
        public async Task<IActionResult> FollowUpSaveData([FromBody] SP_EnqFollowUp dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Enquiry.");
            if (dto.NextVisitDate == null)
                return BadRequestResult("Next Visit Date is required.");
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
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                dto.SystemIP = ClientIp;
                var result = await db.CallEnqFollowUpSP(dto);
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
                var result = await db.CallEnqFollowUpSP(new SP_EnqFollowUp
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("FollowUp/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> FollowUpDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallEnqFollowUpSP(new SP_EnqFollowUp
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = "DeleteAll"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("FollowUp/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> FollowUpGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Enquiry.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EnqFollowUp
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SLNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SLNo,
                        x.MTransNo,
                        x.FollowUpType,
                        x.FollowupDate,
                        x.FollowupNotes,
                        x.NextVisitDate,
                        x.EnqNo,
                        x.QtnNo,
                        x.OANo,
                        x.PINo,
                        x.InvNo,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("FollowUp/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> FollowUpGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EnqFollowUp
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo    == transSlNo &&
                        x.SubscID      == subscID   &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
