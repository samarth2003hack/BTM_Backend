using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class OAController : EmsControllerBase
    {
        private readonly EMSContext db;

        public OAController(EMSContext context) => db = context;

        //=========================================
        // DOWNSTREAM GUARD
        //=========================================

        /// <summary>
        /// Names the live documents that bill or pack against this OA (or, when
        /// transSlNo is given, against that one OA line), or null when none do.
        /// Proforma, Packing Slip and Sales Invoice all link to OADtl.TransSlNo,
        /// so deleting a line — or the whole OA — under them would orphan their
        /// rows and corrupt the balances their pickers compute.
        /// </summary>
        private async Task<string?> FindDownstreamUse(int mTransNo, int subscID, int? transSlNo = null)
        {
            var used = new List<string>();

            if (transSlNo == null)
            {
                if (await db.ProformaMst.AnyAsync(x => x.TargetTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Proforma Invoice");
                if (await db.PackingSlipMst.AnyAsync(x => x.TargetTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Packing Slip");
                if (await db.STxnMst.AnyAsync(x => x.TargetTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Sales Invoice");
            }
            else
            {
                if (await db.ProformaDtl.AnyAsync(x => x.LinkNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Proforma Invoice");
                if (await db.PackingSlipDtl.AnyAsync(x => x.OAID == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Packing Slip");
                if (await db.STxnDtl.AnyAsync(x => x.LinkNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N"))
                    used.Add("Sales Invoice");
            }

            return used.Count == 0 ? null : string.Join(", ", used);
        }

        // ============================================================
        // MST — ORDER ACCEPTANCE HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_OAMst dto)
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
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                // The customer block is a snapshot taken at save time. Whatever
                // the page did not send is filled from the customer master, so
                // the order never depends on the screen having fetched it.
                var customer = await db.CustomerMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo     == dto.VendorNo &&
                                              x.SubscID      == dto.SubscID  &&
                                              x.DeleteStatus == "N");
                if (customer != null)
                {
                    if (string.IsNullOrWhiteSpace(dto.VendorCode)) dto.VendorCode = customer.CustomerCode;
                    if (string.IsNullOrWhiteSpace(dto.VendorName)) dto.VendorName = customer.CustomerName;
                    if (string.IsNullOrWhiteSpace(dto.Address))    dto.Address    = customer.CustomerAddress;
                    if (string.IsNullOrWhiteSpace(dto.GSTIN))      dto.GSTIN      = customer.GSTIN;
                    if (string.IsNullOrWhiteSpace(dto.StateName))  dto.StateName  = customer.StateName;
                    if (string.IsNullOrWhiteSpace(dto.PhoneNo))    dto.PhoneNo    = customer.PhoneNo ?? customer.MobileNo;
                    if (string.IsNullOrWhiteSpace(dto.EmailID))    dto.EmailID    = customer.EmailID;
                    if (string.IsNullOrWhiteSpace(dto.TaxType))    dto.TaxType    = customer.TaxType;
                    if (string.IsNullOrWhiteSpace(dto.SalesExec))  dto.SalesExec  = customer.SalesExec;
                }

                if (string.IsNullOrWhiteSpace(dto.VendorCode))
                    return BadRequestResult("Customer Code is required.");
                if (string.IsNullOrWhiteSpace(dto.VendorName))
                    return BadRequestResult("Customer Name is required.");
                if (string.IsNullOrWhiteSpace(dto.Address))
                    return BadRequestResult("Address is required.");
                if (string.IsNullOrWhiteSpace(dto.TaxType))
                    return BadRequestResult("Tax Type is required.");
                // An unregistered customer has no GSTIN; the column is NOT NULL,
                // so it is stored blank rather than refusing the order.
                dto.GSTIN ??= string.Empty;

                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                var result = await db.CallOAMstSP(dto);
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
                var usedBy = await FindDownstreamUse(id, subscID);
                if (usedBy != null)
                    return ConflictResult($"This Order Acceptance cannot be deleted - it is used in: {usedBy}.");

                var result = await db.CallOAMstSP(new SP_OAMst
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
                var result = await db.CallOAMstSP(new SP_OAMst
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

        // Approve a draft / submitted / rejected order acceptance → LockStatus 'Y'.
        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID) =>
            RunApproval(id, userNo, subscID, "Approve", null);

        // Reject a draft / submitted order acceptance → LockStatus 'R' + RejectReason.
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
                var result = await db.CallOAApprovalSP(new SP_SalesApproval
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

        // ============================================================
        // LOOKUP — OA Year → OA No → OA ID cascade for purchase lines
        // Only the live, current revision: OAMst.DeleteStatus 'N' and
        // DocAmdStatus 'N', OADtl.DeleteStatus 'N'. OA ID = OADtl.SlNo.
        // The same rule is enforced on save by dbo.FN_OALinkCheck (SQL/36).
        // ============================================================

        private IQueryable<OAMst> LiveOAs(int subscID) =>
            db.OAMst.AsNoTracking().Where(m =>
                m.SubscID == subscID && m.DeleteStatus == "N" && m.DocAmdStatus == "N");

        [HttpGet("Lookup/Years/{subscID}")]
        public async Task<IActionResult> LookupYears(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await LiveOAs(subscID)
                    .Where(m => m.DocYear != null)
                    .Select(m => m.DocYear!.Value)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .Select(y => new { OAYear = y })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Lookup/Numbers/{subscID}")]
        public async Task<IActionResult> LookupNumbers(int subscID, [FromQuery] int oaYear)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (oaYear <= 0) return BadRequestResult("OA Year is required.");
            try
            {
                var data = await LiveOAs(subscID)
                    .Where(m => m.DocYear == oaYear)
                    .OrderByDescending(m => m.DocNo)
                    .Select(m => new
                    {
                        m.MTransNo,
                        OAYear   = m.DocYear,
                        OANo     = m.DocNo,
                        m.DocSeries,
                        m.DocDate,
                        Customer = m.VendorName
                    })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Lookup/Lines/{subscID}")]
        public async Task<IActionResult> LookupLines(
            int subscID, [FromQuery] int oaYear, [FromQuery] int oaNo)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (oaYear <= 0 || oaNo <= 0) return BadRequestResult("OA Year and OA No are required.");
            try
            {
                var data = await (
                    from m in LiveOAs(subscID)
                    join d in db.OADtl.AsNoTracking() on m.MTransNo equals d.MTransNo
                    where m.DocYear == oaYear && m.DocNo == oaNo && d.DeleteStatus == "N"
                    orderby d.SlNo
                    select new
                    {
                        OAID = (int)(d.SlNo ?? 0),
                        d.TransSlNo,
                        OATransNo = m.MTransNo,
                        d.PartNo,
                        d.PartCode,
                        d.PartDescription,
                        d.Qty,
                        d.Unit
                    })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
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
            [FromQuery] string? oaType = null,
            [FromQuery] string? search = null,
            [FromQuery] bool currentOnly = false)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.OAMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // DocAmdStatus 'N' is the current revision; 'Y' is superseded.
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
                if (!string.IsNullOrWhiteSpace(oaType))
                    query = query.Where(x => x.OAType == oaType.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search)  ||
                        x.VendorCode!.Contains(search)  ||
                        x.RefNo!.Contains(search)        ||
                        x.OtherRefNo!.Contains(search)   ||
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
                        x.TargetAmdNo,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.VendorNoShip,
                        x.RefNo,
                        x.OtherRefNo,
                        x.RefDate,
                        x.DueDate,
                        x.TargetDate,
                        x.SalesExec,
                        x.TaxType,
                        x.TxnCurrency,
                        x.CurrencyRate,
                        x.NetValue,
                        x.OAType,
                        x.DocStatus,
                        x.DocRegion,
                        x.DocCategory,
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
                var data = await db.OAMst
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

        [HttpGet("Mst/GetByQtn/{targetTransNo}/{subscID}")]
        public async Task<IActionResult> MstGetByQtn(int targetTransNo, int subscID)
        {
            if (targetTransNo <= 0) return BadRequestResult("Invalid Quotation TransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OAMst
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
                        x.OAType,
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
        /// Preview of the next OA No for a series + year. Display only —
        /// SP_OAMst allocates the real number at insert, so a preview can never
        /// be double-booked.
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
                var max = await db.OAMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — UPSTREAM QUOTATIONS (read-only pickers)
        //
        // Read straight off QtnMst / QtnDtl so this screen does not depend on
        // the quotation controller's shapes.
        // ============================================================

        /// <summary>
        /// Quotations an order can be accepted against: the current revision
        /// (DocAmdStatus 'N') of every live quotation, newest first.
        /// </summary>
        [HttpGet("Source/QtnList/{subscID}")]
        public async Task<IActionResult> SourceQtnList(
            int subscID,
            [FromQuery] int? vendorNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.QtnMst
                    .AsNoTracking()
                    .Where(x => x.SubscID      == subscID &&
                                x.DeleteStatus == "N"     &&
                                x.DocAmdStatus == "N");

                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);

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
                        x.RefDate,
                        x.SalesExec,
                        x.TaxType,
                        x.TxnCurrency,
                        x.CurrencyRate,
                        x.NetValue,
                        x.LockStatus
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The live lines of one quotation, for the OA line picker.</summary>
        [HttpGet("Source/QtnItems/{qtnTransNo}/{subscID}")]
        public async Task<IActionResult> SourceQtnItems(int qtnTransNo, int subscID)
        {
            if (qtnTransNo <= 0) return BadRequestResult("Invalid Quotation.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.QtnDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == qtnTransNo &&
                                x.SubscID      == subscID    &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SLNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        SlNo = x.SLNo,
                        x.MTransNo,
                        x.PartNo,
                        x.PartCode,
                        x.PartDescription,
                        x.ItemRemarks,
                        x.CustPartNo,
                        x.DeliveryDate,
                        x.Qty,
                        x.ConvQty,
                        x.TxnUnit,
                        x.Unit,
                        x.Rate,
                        x.BasicValue,
                        x.DiscP,
                        x.DiscAmt,
                        x.PnFP,
                        x.PnFAmt,
                        x.FreightRate,
                        x.FreightAmt,
                        x.TaxableValue,
                        x.GSTP,
                        x.TotalValue,
                        x.MocCode,
                        x.MocDescription,
                        x.Deviation,
                        x.SpecialReq
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — ORDER ACCEPTANCE LINE ITEMS
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_OADtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Order Acceptance.");
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
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            // An edit keeps the row (and its TransSlNo, which downstream
            // documents link to) — so an Update must name the row it changes.
            if (dto.Status == "Update" && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");

            try
            {
                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await db.CallOADtlSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);

                // Planning fields of the line (SQL 58): committed date
                // (null = delivery date), priority, customer-supplied material.
                var plan = await db.CallOADtlPlanFieldsSP(result.ID, dto.CommittedDate,
                    string.IsNullOrWhiteSpace(dto.Priority) ? null : dto.Priority.Trim(),
                    string.IsNullOrWhiteSpace(dto.CustomerMaterial) ? null : dto.CustomerMaterial.Trim().ToUpperInvariant(),
                    dto.ModifiedBy ?? dto.CreatedBy ?? 0, dto.SubscID ?? 0);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                await tx.CommitAsync();
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
                var usedBy = await FindDownstreamUse(mTransNo, subscID, transSlNo);
                if (usedBy != null)
                    return ConflictResult($"This line cannot be removed - it is used in: {usedBy}.");

                var result = await db.CallOADtlSP(new SP_OADtl
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
                var usedBy = await FindDownstreamUse(mTransNo, subscID);
                if (usedBy != null)
                    return ConflictResult($"These lines cannot be removed - the Order Acceptance is used in: {usedBy}.");

                var result = await db.CallOADtlSP(new SP_OADtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OADtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.PartNo,
                        x.PartCode,
                        x.PartDescription,
                        x.ItemRemarks,
                        x.CustPartNo,
                        x.DeliveryDate,
                        x.LinkRefYear,
                        x.LinkRefNo,
                        x.LinkRefId,
                        x.LinkNo,
                        x.Qty,
                        x.ConvQty,
                        x.TxnUnit,
                        x.Unit,
                        x.Rate,
                        x.BasicValue,
                        x.DiscP,
                        x.DiscAmt,
                        x.PnFP,
                        x.PnFAmt,
                        x.FreightRate,
                        x.FreightAmt,
                        x.TaxableValue,
                        x.GSTP,
                        x.IGSTAmt,
                        x.CGSTAmt,
                        x.SGSTAmt,
                        x.TotalValue,
                        x.MocSysCode,
                        x.MocSysDesc,
                        x.MocCode,
                        x.MocDescription,
                        x.MocShort,
                        x.ValveCode,
                        x.ValveDescription,
                        x.Deviation,
                        x.SpecialReq,
                        x.FSlNoValve,
                        x.TSlNoValve,
                        x.ListRate,
                        x.POID,
                        x.AmdNo,
                        x.AmdMode,
                        x.CommittedDate,
                        Priority = x.Priority ?? "Normal",
                        CustomerMaterial = x.CustomerMaterial ?? "N",
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
                var data = await db.OADtl
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
        public async Task<IActionResult> TermsSaveData([FromBody] SP_OATerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Order Acceptance.");
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
                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                var result = await db.CallOATermsSP(dto);
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
                var result = await db.CallOATermsSP(new SP_OATerms
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
                var result = await db.CallOATermsSP(new SP_OATerms
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OATerms
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
                var data = await db.OATerms
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
        public async Task<IActionResult> ExtrasSaveData([FromBody] SP_OAExtras dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Order Acceptance.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                var result = await db.CallOAExtrasSP(dto);
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
                var result = await db.CallOAExtrasSP(new SP_OAExtras
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
                var result = await db.CallOAExtrasSP(new SP_OAExtras
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OAExtras
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
                var data = await db.OAExtras
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
        public async Task<IActionResult> NotesSaveData([FromBody] SP_OANotes dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Order Acceptance.");
            if (string.IsNullOrWhiteSpace(dto.Notes))
                return BadRequestResult("Notes is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                var result = await db.CallOANotesSP(dto);
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
                var result = await db.CallOANotesSP(new SP_OANotes
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
                var result = await db.CallOANotesSP(new SP_OANotes
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OANotes
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
                var data = await db.OANotes
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
        public async Task<IActionResult> AttachmentSaveData([FromBody] SP_OAAttachment dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Order Acceptance.");
            if (string.IsNullOrWhiteSpace(dto.AttFile))
                return BadRequestResult("Attachment file is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP  = ClientIp;
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                if (dto.Status == "Update")
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                var result = await db.CallOAAttachmentSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // The bytes are written to wwwroot/Uploads/OA/{MTransNo}/ (same rules
        // as the GRN uploads: allowlisted extension, generated name) and only
        // the relative path is stored on the row.
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
                var owner = await db.OAMst
                    .AsNoTracking()
                    .AnyAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");
                if (!owner) return NotFoundResult();

                var stored = await GrnUploads.SaveAsync(HttpContext, "OA", mTransNo, file);
                if (stored.Error != null) return BadRequestResult(stored.Error);

                var result = await db.CallOAAttachmentSP(new SP_OAAttachment
                {
                    MTransNo  = mTransNo,
                    // The original name is kept for display only.
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
                var result = await db.CallOAAttachmentSP(new SP_OAAttachment
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
                var result = await db.CallOAAttachmentSP(new SP_OAAttachment
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.OAAttachment
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
                var data = await db.OAAttachment
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
