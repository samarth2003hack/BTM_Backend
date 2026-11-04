using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Purchase order — header, lines, terms, user fields, and the source
    /// picker the screen opens with.
    ///
    /// HOW THE SCREEN USES THE SOURCE ENDPOINTS
    ///
    ///   1. Source/PurReqList        the approved requisitions. Every row
    ///                               carries SourceMode:
    ///                                 'Quote' — an approved supplier quote
    ///                                           exists; show the RFQ No.
    ///                                           dropdown
    ///                                 'Items' — no approved quote; open the
    ///                                           item picker
    ///
    ///   2a. SourceMode 'Quote'
    ///       Source/QuoteList/{prTransNo}     only APPROVED quotes appear
    ///       Source/QuoteItems/{quoteTransNo} optional preview of what will
    ///                                        be ordered
    ///       Mst/GenerateFromQuote            builds the whole order in one
    ///                                        call — header, lines, rates and
    ///                                        terms — and returns it ready to
    ///                                        display. No item picker: there
    ///                                        is nothing left to choose.
    ///
    ///   2b. SourceMode 'Items'
    ///       Source/PurReqItems/{prTransNo}   the popup's rows, already net of
    ///                                        anything ordered before
    ///       Mst/SaveData  (TargetType 'PR')  then Dtl/SaveData per item
    ///
    /// Everything else follows the house pattern: writes go through the SP_*
    /// procedures, reads come off the DbSets, and every reply is the standard
    /// { success, message, data, statusCode } envelope.
    /// </summary>
    [Route("api/[controller]")]
    public class PurchaseOrderController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PurchaseOrderController(EMSContext context) => db = context;

        // ============================================================
        // SOURCE — requisition, then quote or items
        // ============================================================

        /// <summary>
        /// Approved purchase requisitions available to order against, each
        /// flagged with whether it has an approved quote behind it.
        /// </summary>
        [HttpGet("Source/PurReqList/{subscID}")]
        public async Task<IActionResult> SourcePurReqList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? sourceMode = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PurReqForPO.AsNoTracking().Where(x => x.SubscID == subscID);

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.PRDocYear == docYear);

                // 'Quote' or 'Items' — lets the screen show one tab at a time.
                if (!string.IsNullOrWhiteSpace(sourceMode))
                    query = query.Where(x => x.SourceMode == sourceMode.Trim());

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.ReqBy!.Contains(search)   ||
                        x.ReqDept!.Contains(search) ||
                        x.Project!.Contains(search) ||
                        x.DocNotes!.Contains(search));

                var data = await query
                    .OrderByDescending(x => x.PRDocYear)
                    .ThenByDescending(x => x.PRDocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The approved supplier quotes a requisition may be ordered from —
        /// the RFQ No. dropdown. A quote already turned into an order still
        /// appears, with POTransNo set, so the buyer can see what happened
        /// rather than wonder where it went.
        /// </summary>
        [HttpGet("Source/QuoteList/{prTransNo}/{subscID}")]
        public async Task<IActionResult> SourceQuoteList(int prTransNo, int subscID)
        {
            if (prTransNo <= 0) return BadRequestResult("Invalid Purchase Requisition.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QuoteForPO
                    .AsNoTracking()
                    .Where(x => x.PRTransNo == prTransNo && x.SubscID == subscID)
                    .OrderBy(x => x.VendorName)
                    .ThenByDescending(x => x.QuoteDocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0
                    ? "Data found."
                    : "This requisition has no approved supplier quote. Use the item picker instead.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// What an approved quote would turn into — read-only preview, so the
        /// buyer can check the lines before generating the order.
        /// </summary>
        [HttpGet("Source/QuoteItems/{quoteTransNo}/{subscID}")]
        public async Task<IActionResult> SourceQuoteItems(int quoteTransNo, int subscID)
        {
            if (quoteTransNo <= 0) return BadRequestResult("Invalid Quote.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await (
                    from d in db.RFQPriceDtl.AsNoTracking()
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    where d.MTransNo == quoteTransNo && d.SubscID == subscID && d.DeleteStatus == "N"
                    orderby d.SLNo
                    select new
                    {
                        d.TransSlNo,
                        d.SLNo,
                        d.PartNo,
                        p.PartCode,
                        PartDescription = p.PartName,
                        p.HSNCode,
                        GSTP = p.TaxP,
                        d.ItemRemarks,
                        d.Qty,
                        Unit = d.Unit ?? p.PrimaryUnit,
                        p.PrimaryUnit,
                        p.SecondaryUnit,
                        p.ConversionRatio,
                        p.TxnUnit,
                        d.GradeNo,
                        d.Grade,
                        d.Rate,
                        LineValue = d.Qty * d.Rate
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The terms agreed with the supplier on an approved quote — what a PO
        /// raised against that quote must carry, rather than the supplier
        /// master's standing terms. Falls back to the RFQ's own terms when the
        /// price update recorded none.
        /// </summary>
        [HttpGet("Source/QuoteTerms/{quoteTransNo}/{subscID}")]
        public async Task<IActionResult> SourceQuoteTerms(int quoteTransNo, int subscID)
        {
            if (quoteTransNo <= 0) return BadRequestResult("Invalid Quote.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.RFQPriceTerms
                    .AsNoTracking()
                    .Where(x => x.MTransNo == quoteTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new { x.SlNo, x.Title, x.Description, Source = "Quote" })
                    .ToListAsync();

                if (data.Count == 0)
                {
                    var rfqTransNo = await db.RFQPriceMst
                        .AsNoTracking()
                        .Where(x => x.MTransNo == quoteTransNo && x.SubscID == subscID)
                        .Select(x => x.TargetTransNo)
                        .FirstOrDefaultAsync();

                    if (rfqTransNo > 0)
                        data = await db.RFQTerms
                            .AsNoTracking()
                            .Where(x => x.MTransNo == rfqTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                            .OrderBy(x => x.SlNo)
                            .Select(x => new { x.SlNo, x.Title, x.Description, Source = "RFQ" })
                            .ToListAsync();
                }

                return OkResult(data, data.Count > 0 ? "Data found." : "No terms recorded on this quote.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The item picker's rows: requisition lines with everything already
        /// ordered — by either route — subtracted. Pass onlyPending=false to
        /// see fully-ordered lines too.
        /// </summary>
        [HttpGet("Source/PurReqItems/{prTransNo}/{subscID}")]
        public async Task<IActionResult> SourcePurReqItems(
            int prTransNo,
            int subscID,
            [FromQuery] bool onlyPending = true)
        {
            if (prTransNo <= 0) return BadRequestResult("Invalid Purchase Requisition.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PurReqItemsForPO
                    .AsNoTracking()
                    .Where(x => x.PRTransNo == prTransNo && x.SubscID == subscID);

                if (onlyPending)
                    query = query.Where(x => x.BalanceQty > 0);

                var data = await query.OrderBy(x => x.PRSlNo).ToListAsync();

                return OkResult(data, data.Count > 0
                    ? "Data found."
                    : "Nothing left to order on this requisition.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // MST — HEADER
        // ============================================================

        /// <summary>
        /// Generate a whole purchase order from an approved supplier quote —
        /// the "no item picker" path. Returns the new order, fully loaded, so
        /// the screen can display it without a second round trip.
        /// </summary>
        [HttpPost("Mst/GenerateFromQuote")]
        public async Task<IActionResult> MstGenerateFromQuote([FromBody] SP_POGenerateFromQuote dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.QuoteTransNo <= 0)
                return BadRequestResult("Select an approved supplier quote.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallPOGenerateFromQuoteSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);

                var order = await LoadFullOrder(result.ID, dto.SubscID.Value);
                return OkResult(order, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_POMst dto)
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
                return BadRequestResult("Supplier is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            // 'PQ' and 'PR' both name a source document, so one must be given.
            var target = (dto.TargetType ?? "Direct").Trim();
            if (target != "PQ" && target != "PR" && target != "Direct" && target != "Pattern")
                return BadRequestResult("Target Type must be PQ, PR, Direct or Pattern.");
            if ((target == "PQ" || target == "PR") && (dto.TargetTransNo == null || dto.TargetTransNo <= 0))
                return BadRequestResult($"Target Type '{target}' needs the source document to be selected.");

            dto.TargetType = target;

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallPOMstSP(dto);
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
                var result = await db.CallPOMstSP(new SP_POMst
                {
                    MTransNo     = id,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    SystemIP     = ClientIp,
                    Status       = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Lock approves the order — and only an approved, current revision
        /// can be received against. Unlock is refused once goods have arrived;
        /// amend it instead.
        /// </summary>
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
                var result = await db.CallPOMstSP(new SP_POMst
                {
                    MTransNo   = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = action
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // Approve a draft / rejected purchase order → LockStatus 'Y', DocStatus 'A'.
        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID) =>
            RunApproval(id, userNo, subscID, "Approve", null);

        // Reject a draft purchase order → LockStatus 'R' + RejectReason.
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
                var result = await db.CallPOApprovalSP(new SP_SalesApproval
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

        /// <summary>
        /// Open the next revision of an order as a copy of the current one.
        /// Returns the new revision, fully loaded.
        /// </summary>
        [HttpPost("Mst/Amend/{id}")]
        public async Task<IActionResult> MstAmend(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string notes = "",
            [FromQuery] DateTime? docDate = null)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallPOMstSP(new SP_POMst
                {
                    MTransNo    = id,
                    DocDate     = docDate,
                    DocAmdNotes = notes,
                    ModifiedBy  = userNo,
                    ModifiedOn  = DateTime.Now,
                    SubscID     = subscID,
                    SystemIP    = ClientIp,
                    Status      = "Amend"
                });
                if (result.ID == 0) return ConflictResult(result.Message);

                var order = await LoadFullOrder(result.ID, subscID);
                return OkResult(order, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? targetType = null,
            [FromQuery] string? lockStatus = null,
            [FromQuery] bool currentOnly = true,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.POMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // Superseded revisions are history; the list shows the live one
                // unless the caller asks for the whole chain.
                if (currentOnly)
                    query = query.Where(x => x.DocAmdStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(targetType))
                    query = query.Where(x => x.TargetType == targetType.Trim());
                if (!string.IsNullOrWhiteSpace(lockStatus))
                    query = query.Where(x => x.LockStatus == lockStatus.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.VendorCode!.Contains(search) ||
                        x.RefNo!.Contains(search)      ||
                        x.PurSubject!.Contains(search) ||
                        x.DocNotes!.Contains(search));

                // Whole rows, not a projection: the order screen paints its form
                // from the list row before the full read lands, so the list has
                // to carry the snapshot (address, contact, notes, rate) too.
                var data = await query
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .ThenByDescending(x => x.DocAmdNo)
                    .ToListAsync();

                await AttachSource(data);
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
                var data = await db.POMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                await AttachSource(new[] { data });
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The number the next first issue in a series + year would get — for
        /// display only. SP_POMst allocates the real DocNo itself at insert, so
        /// two buyers previewing the same number never collide.
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
                var max = await db.POMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Header, lines, terms and user fields in one read — what the order
        /// screen needs to open a document.
        /// </summary>
        [HttpGet("Mst/GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetFullData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var order = await LoadFullOrder(id, subscID);
                if (order == null) return NotFoundResult();
                return OkResult(order);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Every revision of one order number, oldest first.</summary>
        [HttpGet("Mst/GetAmendments/{docSeries}/{docYear}/{docNo}/{subscID}")]
        public async Task<IActionResult> MstGetAmendments(
            string docSeries, int docYear, int docNo, int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.POMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == docSeries && x.DocYear == docYear
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
        // DTL — LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_PODtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Purchase Order.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.Rate == null || dto.Rate < 0)
                return BadRequestResult("Rate is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallPODtlSP(dto);
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
                var result = await db.CallPODtlSP(new SP_PODtl
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    SystemIP     = ClientIp,
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
                var result = await db.CallPODtlSP(new SP_PODtl
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    SystemIP     = ClientIp,
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Purchase Order.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadLines(mTransNo, subscID);
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
                var data = await db.PODtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // TERMS
        // ============================================================

        [HttpPost("Terms/SaveData")]
        public Task<IActionResult> TermsSaveData([FromBody] SP_POLine dto) =>
            SaveChildLine(dto, db.CallPOTermsSP, "Term");

        [HttpPost("Terms/DeleteData/{transSlNo}")]
        public Task<IActionResult> TermsDeleteData(
            int transSlNo,
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            DeleteChildLine(db.CallPOTermsSP, transSlNo, mTransNo, userNo, subscID, reason, "Delete");

        [HttpPost("Terms/DeleteAll/{mTransNo}")]
        public Task<IActionResult> TermsDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            DeleteChildLine(db.CallPOTermsSP, 0, mTransNo, userNo, subscID, reason, "DeleteAll");

        [HttpGet("Terms/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> TermsGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Purchase Order.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadTerms(mTransNo, subscID);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // EXTRAS — user-defined fields
        // ============================================================

        [HttpPost("Extras/SaveData")]
        public Task<IActionResult> ExtrasSaveData([FromBody] SP_POLine dto) =>
            SaveChildLine(dto, db.CallPOExtrasSP, "Field");

        [HttpPost("Extras/DeleteData/{transSlNo}")]
        public Task<IActionResult> ExtrasDeleteData(
            int transSlNo,
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            DeleteChildLine(db.CallPOExtrasSP, transSlNo, mTransNo, userNo, subscID, reason, "Delete");

        [HttpPost("Extras/DeleteAll/{mTransNo}")]
        public Task<IActionResult> ExtrasDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            DeleteChildLine(db.CallPOExtrasSP, 0, mTransNo, userNo, subscID, reason, "DeleteAll");

        [HttpGet("Extras/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> ExtrasGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Purchase Order.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadExtras(mTransNo, subscID);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // Shared internals
        // ============================================================

        /// <summary>Terms and Extras take the same shape and the same rules,
        /// so their save and delete endpoints share one body each.</summary>
        private async Task<IActionResult> SaveChildLine(
            SP_POLine dto,
            Func<SP_POLine, Task<(int ID, string Message)>> call,
            string what)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Purchase Order.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult($"{what} title is required.");
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequestResult($"{what} description is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await call(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private async Task<IActionResult> DeleteChildLine(
            Func<SP_POLine, Task<(int ID, string Message)>> call,
            int transSlNo, int mTransNo, int userNo, int subscID, string reason, string status)
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (status == "Delete" && transSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");

            try
            {
                var result = await call(new SP_POLine
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    SystemIP     = ClientIp,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status       = status
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        private async Task<object?> LoadFullOrder(int mTransNo, int subscID)
        {
            var header = await db.POMst
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");

            if (header == null) return null;
            await AttachSource(new[] { header });

            return new
            {
                Header = header,
                Lines  = await LoadLines(mTransNo, subscID),
                Terms  = await LoadTerms(mTransNo, subscID),
                Extras = await LoadExtras(mTransNo, subscID)
            };
        }

        /// <summary>
        /// Fills PRTransNo / RFQTransNo on each header. A 'PR' order names its
        /// requisition directly; a 'PQ' order names a quote, so the requisition
        /// is two hops away (quote -> RFQ -> PR) and is resolved in one query
        /// for the whole batch.
        /// </summary>
        private async Task AttachSource(IReadOnlyCollection<POMst> headers)
        {
            var quoteIds = headers
                .Where(h => h.TargetType == "PQ" && h.TargetTransNo > 0)
                .Select(h => h.TargetTransNo!.Value)
                .Distinct()
                .ToList();

            var viaQuote = quoteIds.Count == 0
                ? new Dictionary<int, (int? RFQ, int? PR)>()
                : await (
                    from q in db.RFQPriceMst.AsNoTracking()
                    join r in db.RFQMst.AsNoTracking() on q.TargetTransNo equals (int?)r.MTransNo
                    where quoteIds.Contains(q.MTransNo)
                    select new { q.MTransNo, RFQ = (int?)r.MTransNo, r.PRTransNo })
                  .ToDictionaryAsync(x => x.MTransNo, x => (RFQ: x.RFQ, PR: x.PRTransNo));

            foreach (var h in headers)
            {
                if (h.TargetType == "PR")
                {
                    h.PRTransNo = h.TargetTransNo;
                }
                else if (h.TargetType == "PQ" && h.TargetTransNo.HasValue &&
                         viaQuote.TryGetValue(h.TargetTransNo.Value, out var src))
                {
                    h.RFQTransNo = src.RFQ;
                    h.PRTransNo  = src.PR;
                }
            }
        }

        private Task<List<PODtl>> LoadLines(int mTransNo, int subscID) =>
            db.PODtl
              .AsNoTracking()
              .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
              .OrderBy(x => x.SlNo)
              .ToListAsync();

        private Task<List<POTerms>> LoadTerms(int mTransNo, int subscID) =>
            db.POTerms
              .AsNoTracking()
              .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
              .OrderBy(x => x.SlNo)
              .ToListAsync();

        private Task<List<POExtras>> LoadExtras(int mTransNo, int subscID) =>
            db.POExtras
              .AsNoTracking()
              .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
              .OrderBy(x => x.SlNo)
              .ToListAsync();
    }
}
