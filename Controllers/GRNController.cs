using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Goods Receipt Note — the inward document itself: header, lines, QR
    /// labels and attachments.
    ///
    /// What happens to a GRN after it is received belongs to other
    /// controllers, because each stage owns its own columns:
    ///     StoreAllocation    puts the lines away into bins
    ///     HeatNo             records heat numbers and test certificates
    ///     InwardInspection   splits the received quantity into accepted,
    ///                        rejected, rework and accepted-on-deviation,
    ///                        then posts and later closes it
    ///
    /// HOW THE SCREEN USES THIS
    ///
    ///   1. Source/POList/{subscID}          purchase orders with something
    ///                                       still to receive
    ///   2. Source/POLines/{poTransNo}       those lines, with BalanceQty —
    ///                                       the most a new receipt may bill
    ///   3. Mst/SaveData (TargetType 'PO')   opens the GRN; the vendor block
    ///                                       and tax type come off the order
    ///   4. Dtl/SaveData per line            POTransSlNo names the order line,
    ///                                       and the rate comes from it
    ///   5. Mst/LockUnlock 'Lock'            confirms the receipt
    ///
    /// A GRN with no purchase order behind it uses TargetType 'Direct' and
    /// supplies its own supplier and rates.
    /// </summary>
    [Route("api/[controller]")]
    public class GRNController : EmsControllerBase
    {
        private readonly EMSContext db;

        public GRNController(EMSContext context) => db = context;

        // ============================================================
        // SOURCE — which purchase orders are still open
        // ============================================================

        /// <summary>
        /// Purchase orders with a balance left to receive. Built from
        /// VW_POPendingGRN, so only current, approved revisions appear.
        /// </summary>
        [HttpGet("Source/POList/{subscID}")]
        public async Task<IActionResult> SourcePOList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.POPendingGRN
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.BalanceQty > 0);

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.PODocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.VendorName!.Contains(search));

                // One row per order, not per line: this is the order picker.
                var data = await query
                    .GroupBy(x => new
                    {
                        x.POTransNo, x.PODocSeries, x.PODocYear, x.PODocNo,
                        x.PODocDate, x.PODocAmdNo, x.VendorNo, x.VendorCode,
                        x.VendorName, x.TargetType, x.TaxType, x.TxnCurrency
                    })
                    .Select(g => new
                    {
                        g.Key.POTransNo,
                        g.Key.PODocSeries,
                        g.Key.PODocYear,
                        g.Key.PODocNo,
                        g.Key.PODocDate,
                        g.Key.PODocAmdNo,
                        g.Key.VendorNo,
                        g.Key.VendorCode,
                        g.Key.VendorName,
                        g.Key.TargetType,
                        g.Key.TaxType,
                        g.Key.TxnCurrency,
                        PendingLines = g.Count(),
                        PendingQty   = g.Sum(x => x.BalanceQty)
                    })
                    .OrderByDescending(x => x.PODocYear)
                    .ThenByDescending(x => x.PODocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No purchase order is pending receipt.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The lines of one purchase order with their outstanding balance.
        ///
        /// Pass the GRN being edited as excludeGRN so its own quantities are
        /// added back into the balance — otherwise reopening a saved GRN would
        /// show the lines it already booked as unavailable.
        /// </summary>
        [HttpGet("Source/POLines/{poTransNo}/{subscID}")]
        public async Task<IActionResult> SourcePOLines(
            int poTransNo,
            int subscID,
            [FromQuery] int excludeGRN = 0,
            [FromQuery] bool onlyPending = true)
        {
            if (poTransNo <= 0) return BadRequestResult("Invalid Purchase Order.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var lines = await db.POPendingGRN
                    .AsNoTracking()
                    .Where(x => x.POTransNo == poTransNo && x.SubscID == subscID)
                    .OrderBy(x => x.LinkNo)
                    .ToListAsync();

                // What the GRN under edit has already booked, by PO line.
                var mine = new Dictionary<decimal, (decimal Bill, decimal Rec, int TransSlNo)>();

                if (excludeGRN > 0)
                {
                    var own = await db.GRNDtl
                        .AsNoTracking()
                        .Where(x => x.MTransNo == excludeGRN && x.SubscID == subscID && x.DeleteStatus == "N")
                        .Select(x => new { x.SlNo, x.BillQty, x.RecQty, x.TransSlNo })
                        .ToListAsync();

                    foreach (var o in own)
                        mine[o.SlNo ?? 0m] = (o.BillQty, o.RecQty, o.TransSlNo);
                }

                var data = lines.Select(x =>
                {
                    var key = x.LinkNo ?? 0m;
                    mine.TryGetValue(key, out var booked);

                    return new
                    {
                        x.POTransNo, x.POTransSlNo, x.LinkNo,
                        x.PartNo, x.PartCode, x.PartDescription, x.ItemRemarks,
                        x.HSNCode, x.GradeNo, x.Grade, x.MtrlStd,
                        x.Unit, x.Unit2, x.ConvRatio,
                        x.Rate, x.DiscP, x.PnFP, x.GSTP, x.FreightRate,
                        x.DeleveryDate, x.OAYear, x.OANo, x.OAID,
                        x.POQty,
                        x.BilledQty,
                        // Balance as this GRN sees it: what is outstanding,
                        // plus whatever this GRN itself has already taken.
                        BalanceQty   = x.BalanceQty + booked.Bill,
                        GRNTransSlNo = booked.TransSlNo == 0 ? (int?)null : booked.TransSlNo,
                        GRNBillQty   = booked.Bill,
                        GRNRecQty    = booked.Rec
                    };
                });

                if (onlyPending)
                    data = data.Where(x => x.BalanceQty > 0 || x.GRNTransSlNo != null);

                var result = data.ToList();
                return OkResult(result, result.Count > 0 ? "Data found." : "Nothing left to receive on this order.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // MST — HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_GRNMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
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

            var target = (dto.TargetType ?? "Direct").Trim();
            if (target != "PO" && target != "Direct")
                return BadRequestResult("Target Type must be 'PO' or 'Direct'.");
            if (target == "PO" && (dto.TargetTransNo == null || dto.TargetTransNo <= 0))
                return BadRequestResult("Select the purchase order to receive against.");
            // On a PO-backed GRN the supplier comes off the order, so it is
            // only required on a direct receipt.
            if (target == "Direct" && (dto.VendorNo == null || dto.VendorNo <= 0))
                return BadRequestResult("Supplier is required on a direct GRN.");

            dto.TargetType = target;

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallGRNMstSP(dto);
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
                var result = await db.CallGRNMstSP(new SP_GRNMst
                {
                    MTransNo     = id,
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

        /// <summary>Lock confirms the receipt; Unlock is refused once the GRN
        /// has been inspected.</summary>
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
                var result = await db.CallGRNMstSP(new SP_GRNMst
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

        /// <summary>
        /// The receipt worklist. Reads VW_GRNInwardStatus, so the stage and
        /// the totals shown here are the same ones the inspection and closure
        /// screens see.
        /// </summary>
        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? grnType = null,
            [FromQuery] string? targetType = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus.AsNoTracking().Where(x => x.SubscID == subscID);

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(grnType))
                    query = query.Where(x => x.GRNType == grnType.Trim());
                if (!string.IsNullOrWhiteSpace(targetType))
                    query = query.Where(x => x.TargetType == targetType.Trim());
                if (fromDate.HasValue)
                    query = query.Where(x => x.DocDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    query = query.Where(x => x.DocDate <= toDate.Value.Date);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.RefNo!.Contains(search)      ||
                        x.DCNo!.Contains(search)       ||
                        x.InwardNo!.Contains(search));

                var data = await query
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
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
                var data = await db.GRNMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Header, lines, non-conformance, labels and attachments in
        /// one read — what the GRN screen needs to open a document.</summary>
        [HttpGet("Mst/GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetFullData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var header = await db.GRNMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (header == null) return NotFoundResult();

                return OkResult(new
                {
                    Header = header,
                    Status = await db.GRNInwardStatus.AsNoTracking()
                                     .FirstOrDefaultAsync(x => x.MTransNo == id && x.SubscID == subscID),
                    Lines = await db.GRNDtl.AsNoTracking()
                                    .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N")
                                    .OrderBy(x => x.SlNo).ToListAsync(),
                    NonConformance = await db.GRNNCDtl.AsNoTracking()
                                    .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N")
                                    .OrderBy(x => x.SlNo).ToListAsync(),
                    Labels = await db.GRNQRCode.AsNoTracking()
                                    .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N")
                                    .OrderBy(x => x.GRNSlNo).ThenBy(x => x.LinkNo).ToListAsync(),
                    Attachments = await db.GRNAttachment.AsNoTracking()
                                    .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N")
                                    .OrderBy(x => x.SlNo).ToListAsync()
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Every GRN raised against one purchase order.</summary>
        [HttpGet("Mst/GetByPO/{poTransNo}/{subscID}")]
        public async Task<IActionResult> MstGetByPO(int poTransNo, int subscID)
        {
            if (poTransNo <= 0) return BadRequestResult("Invalid Purchase Order.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.TargetType == "PO" && x.TargetTransNo == poTransNo && x.SubscID == subscID)
                    .OrderBy(x => x.DocYear).ThenBy(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "Nothing has been received against this order yet.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GRN SCREEN (EMSSoft/Modules/Purchase/GRN.html)
        // ============================================================

        /// <summary>
        /// The number the next GRN in a series + year would get — for display
        /// only. SP_GRNMst allocates the real DocNo itself at insert (same
        /// MAX + 1 rule, deleted rows included), so two receivers previewing
        /// the same number never collide.
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
                var max = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Legacy GetGRNNo (AquaFlow GRNController) — GRNs not yet inspected,
        /// for callers still on the old URL (api/GRN/GetGRNNo?SubscID=1).
        /// GRNQC.html uses InwardInspection/Worklist/Pending, which also
        /// requires the GRN to be confirmed and to have lines.
        /// </summary>
        [HttpGet("GetGRNNo")]
        public Task<IActionResult> GetGRNNo([FromQuery] int subscID) => GRNNoList(subscID, inspected: false);

        /// <summary>Legacy GetGRNNoST — inspected GRNs (GRNType 'Q'), the
        /// store allocation picker.</summary>
        [HttpGet("GetGRNNoST")]
        public Task<IActionResult> GetGRNNoST([FromQuery] int subscID) => GRNNoList(subscID, inspected: true);

        private async Task<IActionResult> GRNNoList(int subscID, bool inspected)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var grnType = inspected ? "Q" : "N";
                var data = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.GRNType == grnType)
                    .OrderBy(x => x.DocYear).ThenBy(x => x.DocNo)
                    .Select(x => new { x.MTransNo, x.VendorName, x.DocYear, x.DocNo, x.DocDate, x.LockStatus })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The GRN register the entry screen lists: live headers with the
        /// currency and exchange rate, which VW_GRNInwardStatus does not carry.
        /// </summary>
        [HttpGet("Mst/GetRegister/{subscID}")]
        public async Task<IActionResult> MstGetRegister(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());

                var data = await query
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.TargetType, x.TargetTransNo, x.TargetDocYear, x.TargetDocNo,
                        x.VendorNo, x.VendorCode, x.VendorName,
                        x.RefNo, x.RefDate, x.DCNo, x.DCDate,
                        x.InwardNo, x.InwardDate, x.LRNo, x.LRDate,
                        x.BillValue, x.RoundOff, x.NetValue, x.DebitValue, x.CreditValue,
                        x.TxnCurrency, x.CurrencyRate, x.TaxType, x.DocNotes,
                        x.GRNType, x.LockStatus, x.CreatedBy, x.CreatedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Header + lines + dropped lines in ONE transaction, then (by default)
        /// Lock — so the receipt is either saved whole and confirmed, or not at
        /// all. Every rule stays in the procedures: the PO snapshot, DocNo
        /// allocation, the rate taken from the PO line, BillQty ≤ PO balance
        /// and the header totals are all SP_GRNMst / SP_GRNDtl's; the first
        /// refusal rolls everything back and comes back as 409 with its wording.
        /// </summary>
        [HttpPost("Mst/SaveFull")]
        public async Task<IActionResult> MstSaveFull([FromBody] GRNSaveFullRequest req)
        {
            if (req?.Header == null)
                return BadRequestResult("Invalid request.");

            var h = req.Header;
            var status = (h.Status ?? "").Trim();
            if (status != "Insert" && status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (status == "Update" && h.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (string.IsNullOrWhiteSpace(h.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (h.DocYear == null || h.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (h.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (h.SubscID == null || h.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var userNo = (h.CreatedBy ?? 0) > 0 ? h.CreatedBy!.Value : (h.ModifiedBy ?? 0);
            if (userNo <= 0)
                return BadRequestResult("Invalid User.");

            var target = (h.TargetType ?? "Direct").Trim();
            if (target != "PO" && target != "Direct")
                return BadRequestResult("Target Type must be 'PO' or 'Direct'.");
            if (target == "PO" && (h.TargetTransNo == null || h.TargetTransNo <= 0))
                return BadRequestResult("Select the purchase order to receive against.");
            if (target == "Direct" && (h.VendorNo == null || h.VendorNo <= 0))
                return BadRequestResult("Supplier is required on a direct GRN.");

            var lines = req.Lines ?? new List<SP_GRNDtl>();
            if (lines.Count == 0)
                return BadRequestResult("Add at least one received line.");
            foreach (var l in lines)
            {
                if (l == null || l.PartNo == null || l.PartNo <= 0)
                    return BadRequestResult("Part No is required on every line.");
                if ((l.BillQty ?? 0) <= 0 && (l.RecQty ?? 0) <= 0)
                    return BadRequestResult("Enter a billed or a received quantity on every line.");
            }

            // Stamped here, never taken from the client.
            var now      = DateTime.Now;
            var subscID  = h.SubscID!.Value;
            var clientIp = ClientIp;

            h.Status     = status;
            h.TargetType = target;
            h.SystemIP   = clientIp;
            h.CreatedBy  = userNo;
            h.ModifiedBy = userNo;
            h.CreatedOn  = now;
            h.ModifiedOn = now;
            h.LockStatus = null;               // Lock is its own step below
            if (status == "Insert") { h.MTransNo = 0; h.DocNo = 0; }

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var mst = await db.CallGRNMstSP(h);
                if (mst.ID == 0) return await RollbackConflict(tx, mst.Message);
                var id = mst.ID;

                // Drops first, so what they free is available to the saves.
                if (status == "Update")
                {
                    foreach (var slno in (req.DeletedLines ?? new List<int>()).Where(x => x > 0).Distinct())
                    {
                        var del = await db.CallGRNDtlSP(new SP_GRNDtl
                        {
                            TransSlNo    = slno,
                            MTransNo     = id,
                            ModifiedBy   = userNo,
                            ModifiedOn   = now,
                            SubscID      = subscID,
                            SystemIP     = clientIp,
                            DeleteReason = "Removed on GRN edit",
                            Status       = "Delete"
                        });
                        if (del.ID == 0) return await RollbackConflict(tx, del.Message);
                    }
                }

                foreach (var l in lines)
                {
                    if (status == "Insert") l.TransSlNo = 0;
                    l.MTransNo     = id;
                    l.SubscID      = subscID;
                    l.SystemIP     = clientIp;
                    l.CreatedBy    = userNo;
                    l.ModifiedBy   = userNo;
                    l.CreatedOn    = now;
                    l.ModifiedOn   = now;
                    l.DeleteReason = null;
                    l.Status       = l.TransSlNo > 0 ? "Update" : "Insert";

                    var dtl = await db.CallGRNDtlSP(l);
                    if (dtl.ID == 0) return await RollbackConflict(tx, dtl.Message);
                }

                if (req.Confirm)
                {
                    var lk = await db.CallGRNMstSP(new SP_GRNMst
                    {
                        MTransNo   = id,
                        ModifiedBy = userNo,
                        ModifiedOn = now,
                        SubscID    = subscID,
                        SystemIP   = clientIp,
                        Status     = "Lock"
                    });
                    if (lk.ID == 0) return await RollbackConflict(tx, lk.Message);
                }

                await tx.CommitAsync();

                var saved = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.SubscID == subscID)
                    .Select(x => new { x.DocSeries, x.DocYear, x.DocNo, x.NetValue, x.LockStatus })
                    .FirstOrDefaultAsync();

                return OkResult(new
                {
                    ID         = id,
                    DocSeries  = saved?.DocSeries,
                    DocYear    = saved?.DocYear,
                    DocNo      = saved?.DocNo,
                    NetValue   = saved?.NetValue,
                    LockStatus = saved?.LockStatus
                }, mst.Message);
            }
            catch (Exception ex)
            {
                await SafeRollback(tx);
                return ErrorResult(ex);
            }
        }

        private async Task<IActionResult> RollbackConflict(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, string message)
        {
            await SafeRollback(tx);
            return ConflictResult(message);
        }

        // A procedure's CATCH block may already have rolled the transaction
        // back on the server; a second rollback then throws, and is harmless.
        private static async Task SafeRollback(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
        {
            try { await tx.RollbackAsync(); } catch { /* already rolled back */ }
        }

        // ============================================================
        // DTL — RECEIPT LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_GRNDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid GRN.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if ((dto.BillQty ?? 0) <= 0 && (dto.RecQty ?? 0) <= 0)
                return BadRequestResult("Enter a billed or a received quantity.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallGRNDtlSP(dto);
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
                var result = await db.CallGRNDtlSP(new SP_GRNDtl
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
                var result = await db.CallGRNDtlSP(new SP_GRNDtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
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
                var data = await db.GRNDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // QR LABELS
        // ============================================================

        /// <summary>
        /// Print labels for one receipt line. Each label is numbered from the
        /// last one already issued on that line, so a reprint of extra labels
        /// continues the sequence rather than restarting it.
        /// </summary>
        [HttpPost("QRCode/Generate")]
        public async Task<IActionResult> QRCodeGenerate([FromBody] SP_GRNQRCode dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid GRN.");
            if (dto.GRNSlNo <= 0)
                return BadRequestResult("Select the receipt line to label.");
            if (dto.LQty <= 0)
                return BadRequestResult("Label count must be greater than zero.");
            if (dto.LQty > 1000)
                return BadRequestResult("Generate at most 1000 labels at a time.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                dto.Status   = "Generate";
                var result = await db.CallGRNQRCodeSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("QRCode/DeleteData/{transSlNo}")]
        public async Task<IActionResult> QRCodeDeleteData(
            int transSlNo,
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (transSlNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNQRCodeSP(new SP_GRNQRCode
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

        [HttpPost("QRCode/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> QRCodeDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNQRCodeSP(new SP_GRNQRCode
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

        [HttpGet("QRCode/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> QRCodeGetList(
            int mTransNo,
            int subscID,
            [FromQuery] int grnSlNo = 0)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNQRCode
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (grnSlNo > 0)
                    query = query.Where(x => x.GRNSlNo == grnSlNo);

                var data = await query
                    .OrderBy(x => x.GRNSlNo).ThenBy(x => x.LinkNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // ATTACHMENTS
        //
        // The bytes are written to wwwroot/Uploads/GRN/{MTransNo}/ and only
        // the relative path is stored on the row.
        // ============================================================

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
                var stored = await GrnUploads.SaveAsync(HttpContext, "GRN", mTransNo, file);
                if (stored.Error != null) return BadRequestResult(stored.Error);

                var result = await db.CallGRNAttachmentSP(new SP_GRNAttachment
                {
                    MTransNo  = mTransNo,
                    AttNotes  = attNotes,
                    AttFile   = stored.RelativePath,
                    CreatedBy = userNo,
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
            if (transSlNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNAttachmentSP(new SP_GRNAttachment
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

                // The row is soft-deleted, so the file stays on disk: the audit
                // trail can still be followed back to what was attached.
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Attachment/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> AttachmentGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GRNAttachment
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
