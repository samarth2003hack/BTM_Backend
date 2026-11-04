using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class QtnController : EmsControllerBase
    {
        private readonly EMSContext db;

        public QtnController(EMSContext context) => db = context;

        // ============================================================
        // MST — QUOTATION HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_QtnMst dto)
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
            if (string.IsNullOrWhiteSpace(dto.VendorCode))
                return BadRequestResult("Customer Code is required.");
            if (string.IsNullOrWhiteSpace(dto.VendorName))
                return BadRequestResult("Customer Name is required.");
            if (string.IsNullOrWhiteSpace(dto.Address))
                return BadRequestResult("Address is required.");
            if (string.IsNullOrWhiteSpace(dto.TaxType))
                return BadRequestResult("Tax Type is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            // An amendment revises an existing number, so it has to carry it.
            if (dto.Status == "Insert" && dto.DocAmdNo > 0 && (dto.DocNo == null || dto.DocNo <= 0))
                return BadRequestResult("An amendment needs the Doc No it revises.");

            // GSTIN is NOT NULL on QtnMst but an unregistered / export customer
            // has none, so a blank one is stored as ''.
            dto.GSTIN ??= "";

            try
            {
                dto.SystemIP = ClientIp;
                // The procedures write CreatedOn / ModifiedOn as given, so the
                // server clock stamps them — never the browser's.
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQtnMstSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
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
                var result = await db.CallQtnMstSP(new SP_QtnMst
                {
                    MTransNo     = id,
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
                var result = await db.CallQtnMstSP(new SP_QtnMst
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

        // Approve a draft / submitted / rejected quotation → LockStatus 'Y'.
        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID) =>
            RunApproval(id, userNo, subscID, "Approve", null);

        // Reject a draft / submitted quotation → LockStatus 'R' + RejectReason.
        [HttpPost("Mst/Reject/{id}")]
        public Task<IActionResult> MstReject(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            RunApproval(id, userNo, subscID, "Reject", reason);

        private async Task<IActionResult> RunApproval(
            int id, int userNo, int subscID, string status, string? reason)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (status == "Reject" && string.IsNullOrWhiteSpace(reason))
                return BadRequestResult("Rejection reason is required.");
            if (reason != null && reason.Trim().Length > 250)
                return BadRequestResult("Rejection reason cannot exceed 250 characters.");

            try
            {
                var result = await db.CallQtnApprovalSP(new SP_SalesApproval
                {
                    MTransNo     = id,
                    RejectReason = reason?.Trim(),
                    SystemIP     = ClientIp,
                    ModifiedBy   = userNo,
                    SubscID      = subscID,
                    Status       = status
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID, LockStatus = status == "Approve" ? "Y" : "R" },
                                result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] int? targetTransNo = null,
            [FromQuery] string? search = null,
            [FromQuery] bool currentOnly = false)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.QtnMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // currentOnly: one row per quotation — its live revision.
                if (currentOnly)
                    query = query.Where(x => x.DocAmdStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (targetTransNo.HasValue && targetTransNo > 0)
                    query = query.Where(x => x.TargetTransNo == targetTransNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search)  ||
                        x.VendorCode!.Contains(search)  ||
                        x.RefNo!.Contains(search)        ||
                        x.SalesExec!.Contains(search)    ||
                        x.DocNotes!.Contains(search));

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
                        x.TargetTransNo,
                        x.TargetDocYear,
                        x.TargetDocNo,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.RefNo,
                        x.RefDate,
                        x.SalesExec,
                        x.TargetDate,
                        x.TaxType,
                        x.NetValue,
                        x.DocStatus,
                        x.LockStatus,
                        x.LockCounter,
                        x.RejectReason,
                        x.RejectedBy,
                        x.RejectedOn,
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

        [HttpGet("Mst/GetData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnMst
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

        [HttpGet("Mst/GetByEnq/{targetTransNo}/{subscID}")]
        public async Task<IActionResult> MstGetByEnq(int targetTransNo, int subscID)
        {
            if (targetTransNo <= 0) return BadRequestResult("Invalid Enquiry TransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnMst
                    .AsNoTracking()
                    .Where(x => x.TargetTransNo == targetTransNo &&
                                x.SubscID       == subscID       &&
                                x.DeleteStatus  == "N")
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
                        x.TargetTransNo,
                        x.VendorNo,
                        x.VendorName,
                        x.TaxType,
                        x.NetValue,
                        x.DocStatus,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                if (!data.Any()) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Preview of the next QTN No for a series + year. Display only:
        /// SP_QtnMst allocates the real number at insert (MAX + 1 over every
        /// row, deleted ones included), so this mirrors that exactly.
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
                var max = await db.QtnMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Every live revision of one quotation, oldest first — the revision
        /// history. DocAmdStatus 'N' marks the current one.
        /// </summary>
        [HttpGet("Mst/GetAmendments/{docSeries}/{docYear}/{docNo}/{subscID}")]
        public async Task<IActionResult> MstGetAmendments(
            string docSeries, int docYear, int docNo, int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries) || docYear <= 0 || docNo <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var series = docSeries.Trim();
                var data = await db.QtnMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear
                             && x.DocNo == docNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.DocAmdNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocAmdNo, x.DocAmdStatus, x.DocAmdNotes,
                        x.DocDate, x.NetValue, x.LockStatus,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                if (data.Count == 0) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — UPSTREAM ENQUIRIES (read-only pickers)
        // ============================================================

        /// <summary>
        /// Enquiries a quotation can be raised against: the current revision
        /// of every live enquiry, newest first. `quoted` flags the ones that
        /// already have a live quotation; pendingOnly=true leaves those out
        /// (except includeTransNo, so an edited quotation still finds its own).
        /// </summary>
        [HttpGet("Source/EnqList/{subscID}")]
        public async Task<IActionResult> SourceEnqList(
            int subscID,
            [FromQuery] int? vendorNo = null,
            [FromQuery] bool pendingOnly = false,
            [FromQuery] int? includeTransNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var quoted = db.QtnMst
                    .Where(q => q.SubscID == subscID && q.DeleteStatus == "N" && q.TargetTransNo != null)
                    .Select(q => q.TargetTransNo);

                var query = db.EnqMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" &&
                                (x.DocAmdStatus == null || x.DocAmdStatus == "N"));

                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (pendingOnly)
                    query = query.Where(x => !quoted.Contains(x.MTransNo) || x.MTransNo == includeTransNo);

                var data = await query
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DocSeries,
                        x.DocYear,
                        x.DocNo,
                        x.DocDate,
                        x.DocAmdNo,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.RefNo,
                        x.SalesExec,
                        x.DocStatus,
                        x.LockStatus,
                        Quoted = quoted.Contains(x.MTransNo)
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One enquiry's lines, with the product master's name, unit and tax
        /// rate — what "pull from enquiry" copies onto the quotation. The
        /// enquiry carries no price, so each line still needs a rate.
        /// </summary>
        [HttpGet("Source/EnqItems/{enqTransNo}/{subscID}")]
        public async Task<IActionResult> SourceEnqItems(int enqTransNo, int subscID)
        {
            if (enqTransNo <= 0) return BadRequestResult("Invalid Enquiry.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await (
                    from d in db.EnqDtl.AsNoTracking()
                    where d.MTransNo == enqTransNo && d.SubscID == subscID && d.DeleteStatus == "N"
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    orderby d.SLNo
                    select new
                    {
                        d.TransSlNo,
                        d.SLNo,
                        d.MTransNo,
                        d.PartNo,
                        PartCode    = d.PartCode ?? (p != null ? p.PartCode : null),
                        PartName    = p != null ? p.PartName : null,
                        d.Description,
                        d.ItemRemarks,
                        d.Qty,
                        Unit        = d.Unit ?? (p != null ? p.PrimaryUnit : null),
                        d.CustPartNo,
                        d.PartSysCode,
                        d.PartSysDesc,
                        TaxP        = p != null ? p.TaxP : null
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — QUOTATION LINE ITEMS
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_QtnDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.ConvQty == null || dto.ConvQty <= 0)
                return BadRequestResult("Conv Qty is required.");
            if (dto.Rate == null || dto.Rate <= 0)
                return BadRequestResult("Rate is required.");
            if (dto.BasicValue == null)
                return BadRequestResult("Basic Value is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status != "Insert" && dto.Status != "Update")
                    return BadRequestResult("Status must be 'Insert' or 'Update'.");
                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQtnDtlSP(dto);
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
                var result = await db.CallQtnDtlSP(new SP_QtnDtl
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
                var result = await db.CallQtnDtlSP(new SP_QtnDtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnDtl
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
                        x.PartCode,
                        x.PartDescription,
                        x.ItemRemarks,
                        x.CustPartNo,
                        x.DeliveryDate,
                        x.Qty,
                        x.ConvQty,
                        x.Unit,
                        x.Rate,
                        x.BasicValue,
                        x.DiscP,
                        x.DiscAmt,
                        x.TaxableValue,
                        x.GSTP,
                        x.IGSTAmt,
                        x.CGSTAmt,
                        x.SGSTAmt,
                        x.TotalValue,
                        // What the quotation screen reopens a line with: the
                        // grade (MocCode), P&F % and freight, the free-text
                        // fields and the enquiry line it was pulled from.
                        x.LinkRefYear,
                        x.LinkRefNo,
                        x.LinkRefId,
                        x.TxnUnit,
                        x.PnFP,
                        x.PnFAmt,
                        x.FreightRate,
                        x.FreightAmt,
                        x.LinkNo,
                        x.MocSysCode,
                        x.MocSysDesc,
                        x.MocCode,
                        x.MocDescription,
                        x.ValveCode,
                        x.ValveDescription,
                        x.MOCShort,
                        x.Deviation,
                        x.SpecialReq,
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
                var data = await db.QtnDtl
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
        // TERMS
        // ============================================================

        [HttpPost("Terms/SaveData")]
        public async Task<IActionResult> TermsSaveData([FromBody] SP_QtnTerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequestResult("Description is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status != "Insert" && dto.Status != "Update")
                    return BadRequestResult("Status must be 'Insert' or 'Update'.");
                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQtnTermsSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Terms/DeleteData/{transSlNo}")]
        public async Task<IActionResult> TermsDeleteData(
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
                var result = await db.CallQtnTermsSP(new SP_QtnTerms
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

        [HttpPost("Terms/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> TermsDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallQtnTermsSP(new SP_QtnTerms
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

        [HttpGet("Terms/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> TermsGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnTerms
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo,
                        x.Title, x.Description, x.LockStatus,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Terms/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> TermsGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnTerms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // EXTRAS
        // ============================================================

        [HttpPost("Extras/SaveData")]
        public async Task<IActionResult> ExtrasSaveData([FromBody] SP_QtnExtras dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status != "Insert" && dto.Status != "Update")
                    return BadRequestResult("Status must be 'Insert' or 'Update'.");
                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQtnExtrasSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Extras/DeleteData/{transSlNo}")]
        public async Task<IActionResult> ExtrasDeleteData(
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
                var result = await db.CallQtnExtrasSP(new SP_QtnExtras
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

        [HttpPost("Extras/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> ExtrasDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallQtnExtrasSP(new SP_QtnExtras
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

        [HttpGet("Extras/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> ExtrasGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnExtras
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo,
                        x.Title, x.Description, x.LockStatus,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Extras/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> ExtrasGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnExtras
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // NOTES
        // ============================================================

        [HttpPost("Notes/SaveData")]
        public async Task<IActionResult> NotesSaveData([FromBody] SP_QTNNotes dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (string.IsNullOrWhiteSpace(dto.Notes))
                return BadRequestResult("Notes is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status != "Insert" && dto.Status != "Update")
                    return BadRequestResult("Status must be 'Insert' or 'Update'.");
                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQTNNotesSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Notes/DeleteData/{transSlNo}")]
        public async Task<IActionResult> NotesDeleteData(
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
                var result = await db.CallQTNNotesSP(new SP_QTNNotes
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

        [HttpPost("Notes/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> NotesDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallQTNNotesSP(new SP_QTNNotes
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

        [HttpGet("Notes/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> NotesGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QTNNotes
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo,
                        x.Notes, x.LockStatus,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Notes/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> NotesGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QTNNotes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // ATTACHMENT
        // ============================================================

        [HttpPost("Attachment/SaveData")]
        public async Task<IActionResult> AttachmentSaveData([FromBody] SP_QtnAttachment dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Quotation.");
            if (string.IsNullOrWhiteSpace(dto.AttFile))
                return BadRequestResult("Attachment file is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status != "Insert" && dto.Status != "Update")
                    return BadRequestResult("Status must be 'Insert' or 'Update'.");
                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }
                var result = await db.CallQtnAttachmentSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Multipart upload: the bytes go to wwwroot/Uploads/QTN/{MTransNo}/
        /// under a generated name (GrnUploads enforces the extension allowlist
        /// and size cap), and only that relative path is stored in AttFile.
        /// </summary>
        [HttpPost("Attachment/Upload")]
        [RequestSizeLimit(30_000_000)]
        public async Task<IActionResult> AttachmentUpload(
            [FromForm] IFormFile? file,
            [FromForm] int mTransNo,
            [FromForm] int userNo,
            [FromForm] int subscID,
            [FromForm] string attNotes = "")
        {
            if (file == null || file.Length == 0)
                return BadRequestResult("No file was uploaded.");
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var owner = await db.QtnMst.AsNoTracking()
                    .AnyAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");
                if (!owner) return NotFoundResult();

                var stored = await GrnUploads.SaveAsync(HttpContext, "QTN", mTransNo, file);
                if (stored.Error != null) return BadRequestResult(stored.Error);

                var result = await db.CallQtnAttachmentSP(new SP_QtnAttachment
                {
                    MTransNo  = mTransNo,
                    // The original name is kept for display when no description is given.
                    AttNotes  = string.IsNullOrWhiteSpace(attNotes) ? Path.GetFileName(file.FileName) : attNotes,
                    AttFile   = stored.RelativePath,
                    CreatedBy = userNo,
                    CreatedOn = DateTime.Now,
                    SubscID   = subscID,
                    SystemIP  = ClientIp,
                    Status    = "Insert"
                });

                if (result.ID == 0)
                {
                    // The row was refused, so the file on disk is an orphan.
                    GrnUploads.TryDelete(stored.FullPath);
                    return ConflictResult(result.Message);
                }

                return OkResult(new { ID = result.ID, AttFile = stored.RelativePath }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Attachment/DeleteData/{transSlNo}")]
        public async Task<IActionResult> AttachmentDeleteData(
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
                var result = await db.CallQtnAttachmentSP(new SP_QtnAttachment
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

        [HttpPost("Attachment/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> AttachmentDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallQtnAttachmentSP(new SP_QtnAttachment
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

        [HttpGet("Attachment/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> AttachmentGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnAttachment
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo,
                        x.AttNotes, x.AttFile, x.LockStatus,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Attachment/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> AttachmentGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnAttachment
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
