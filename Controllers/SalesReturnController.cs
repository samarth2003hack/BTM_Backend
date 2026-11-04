using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class SalesReturnController : EmsControllerBase
    {
        private readonly EMSContext db;

        public SalesReturnController(EMSContext context) => db = context;

        // ============================================================
        // MST — SALES RETURN HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_SalesReturnMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.TargetTransNo == null || dto.TargetTransNo <= 0)
                return BadRequestResult("Invoice reference is required.");
            if (dto.VendorNo == null || dto.VendorNo <= 0)
                return BadRequestResult("Customer is required.");
            if (string.IsNullOrWhiteSpace(dto.VendorName))
                return BadRequestResult("Customer Name is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallSalesReturnMstSP(dto);
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
                var result = await db.CallSalesReturnMstSP(new SP_SalesReturnMst
                {
                    MTransNo = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "Delete"
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
            [FromQuery] string action = "Submit")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (action != "Draft" && action != "Submit" &&
                action != "Approve" && action != "Reject")
                return BadRequestResult("Action must be 'Draft', 'Submit', 'Approve' or 'Reject'.");

            try
            {
                var result = await db.CallSalesReturnMstSP(new SP_SalesReturnMst
                {
                    MTransNo = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID = subscID,
                    Status = action
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
            [FromQuery] string? lockStatus = null,
            [FromQuery] int? targetTransNo = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.SalesReturnMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(lockStatus))
                    query = query.Where(x => x.LockStatus == lockStatus.Trim());
                if (targetTransNo.HasValue && targetTransNo > 0)
                    query = query.Where(x => x.TargetTransNo == targetTransNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.RefNo!.Contains(search) ||
                        x.DocNotes!.Contains(search));

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
                        x.TargetTransNo,
                        x.TargetDocYear,
                        x.TargetDocNo,
                        x.VendorNo,
                        x.VendorName,
                        x.TxnGSTIN,
                        x.TaxType,
                        x.RefNo,
                        x.RefDate,
                        x.InvValue,
                        x.TaxableValue,
                        x.NetValue,
                        x.TxnCurrency,
                        x.LockStatus,
                        x.LockCounter,
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
                var data = await db.SalesReturnMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id &&
                        x.SubscID == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Mst/GetByInvoice/{targetTransNo}/{subscID}")]
        public async Task<IActionResult> MstGetByInvoice(int targetTransNo, int subscID)
        {
            if (targetTransNo <= 0) return BadRequestResult("Invalid Invoice TransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.SalesReturnMst
                    .AsNoTracking()
                    .Where(x => x.TargetTransNo == targetTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DocSeries,
                        x.DocYear,
                        x.DocNo,
                        x.DocDate,
                        x.TargetTransNo,
                        x.VendorNo,
                        x.VendorName,
                        x.InvValue,
                        x.NetValue,
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

        // ============================================================
        // MST — GET NEXT DOC NO (used by form on series selection)
        // ============================================================

        [HttpGet("Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> MstGetNextDocNo(
            int subscID,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? docYear = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries))
                return BadRequestResult("Doc Series is required.");
            if (docYear == null || docYear <= 0)
                return BadRequestResult("Doc Year is required.");

            try
            {
                var maxDocNo = await db.SalesReturnMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == docSeries.Trim() &&
                                x.DocYear == docYear &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(new { NextDocNo = maxDocNo + 1 });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — INVOICE LIST (dropdown: invoices available to return)
        // ============================================================

        [HttpGet("Source/InvList/{subscID}")]
        public async Task<IActionResult> SourceInvList(
            int subscID,
            [FromQuery] int? vendorNo = null,
            [FromQuery] int? docYear = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.STxnMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y");   // only locked (approved) invoices

                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search));

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
                        x.VendorNo,
                        x.VendorName,
                        x.TaxType,
                        x.NetValue
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No invoices found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — INVOICE ITEMS (lines of selected invoice)
        // ============================================================

        /// <summary>
        /// Lines of one invoice, for the Sales Return page.
        /// Part information comes from three places, in this order:
        ///   1. the invoice line itself   (STxnDtl.Description / PartCode / HSNCode ...)
        ///   2. the Order Acceptance line (OADtl via STxnDtl.LinkNo) — part description,
        ///      MOC and valve info, which the invoice line does not store
        ///   3. PartMaster                (PartName, PartCode, HSNCode) as the last fallback
        /// The description is returned as PartDescription AND Description (the page
        /// reads partDescription -> description -> partName), so it always arrives.
        /// </summary>
        [HttpGet("Source/InvItems/{invTransNo}/{subscID}")]
        public async Task<IActionResult> SourceInvItems(
            int invTransNo,
            int subscID,
            [FromQuery] int? mTransNo = null)
        {
            if (invTransNo <= 0) return BadRequestResult("Invalid Invoice TransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                // Invoice lines, each left-joined to its OA line and to PartMaster.
                var raw = await (
                    from d in db.STxnDtl.AsNoTracking()
                    join o in db.OADtl.AsNoTracking() on d.LinkNo equals o.TransSlNo into oj
                    from o in oj.DefaultIfEmpty()
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    where d.MTransNo == invTransNo &&
                          d.SubscID == subscID &&
                          d.DeleteStatus == "N"
                    orderby d.SlNo
                    select new
                    {
                        d.TransSlNo,
                        d.SlNo,
                        d.MTransNo,
                        d.PartNo,
                        d.PartCode,
                        d.Description,
                        d.ItemRemarks,
                        d.CustPartNo,
                        d.HSNCode,
                        d.Qty,
                        d.Unit,
                        d.Rate,
                        d.GSTP,
                        d.TotalValue,
                        OaPartDescription = o != null ? o.PartDescription : null,
                        OaMocCode = o != null ? o.MocCode : null,
                        OaMocDescription = o != null ? o.MocDescription : null,
                        OaValveCode = o != null ? o.ValveCode : null,
                        OaValveDesc = o != null ? o.ValveDescription : null,
                        MasterPartCode = p != null ? p.PartCode : null,
                        MasterPartName = p != null ? p.PartName : null,
                        MasterHSNCode = p != null ? p.HSNCode : null
                    })
                    .ToListAsync();

                if (!raw.Any())
                    return NotFoundResult();

                // First non-blank value wins (done in memory — no SQL translation risk).
                static string? First(params string?[] values) =>
                    values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

                var invLines = raw.Select(x =>
                {
                    var description = First(x.Description, x.OaPartDescription, x.MasterPartName);
                    return new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.PartNo,
                        PartCode = First(x.PartCode, x.MasterPartCode),
                        PartDescription = description,   // page: pick("partDescription")
                        Description = description,   // page: pick("description")
                        PartName = x.MasterPartName, // page: pick("partName")
                        MocCode = First(x.OaMocCode),
                        MocDescription = First(x.OaMocDescription), // page: pick("mocDescription")
                        ValveCode = First(x.OaValveCode),
                        ValveDescription = First(x.OaValveDesc),
                        HSNCode = First(x.HSNCode, x.MasterHSNCode),
                        x.CustPartNo,
                        x.ItemRemarks,
                        x.Qty,
                        x.Unit,
                        x.Rate,
                        x.GSTP,
                        x.TotalValue
                    };
                }).ToList();

                // If editing an existing return — get already-returned qtys
                // so frontend can enforce ReturnQty <= (Qty - AlreadyReturned)
                if (mTransNo.HasValue && mTransNo > 0)
                {
                    var returned = await db.SalesReturnDtl
                        .AsNoTracking()
                        .Where(x => x.InvTransNo == invTransNo &&
                                    x.SubscID == subscID &&
                                    x.DeleteStatus == "N" &&
                                    x.MTransNo != mTransNo)   // exclude current CN
                        .GroupBy(x => x.SlNo)
                        .Select(g => new { SlNo = g.Key, ReturnedQty = g.Sum(x => x.ReturnQty) })
                        .ToListAsync();

                    var result = invLines.Select(line =>
                    {
                        var ret = returned.FirstOrDefault(r => r.SlNo == line.SlNo);
                        return new
                        {
                            line.TransSlNo,
                            line.SlNo,
                            line.MTransNo,
                            line.PartNo,
                            line.PartCode,
                            line.PartDescription,
                            line.Description,
                            line.PartName,
                            line.MocCode,
                            line.MocDescription,
                            line.ValveCode,
                            line.ValveDescription,
                            line.HSNCode,
                            line.CustPartNo,
                            line.ItemRemarks,
                            line.Qty,
                            AlreadyReturnedQty = ret?.ReturnedQty ?? 0,
                            line.Unit,
                            line.Rate,
                            line.GSTP,
                            line.TotalValue
                        };
                    }).ToList();

                    return OkResult(result);
                }

                return OkResult(invLines);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — SALES RETURN LINE ITEMS
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_SalesReturnDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Sales Return.");
            if (dto.InvTransNo == null || dto.InvTransNo <= 0)
                return BadRequestResult("Invoice reference is required.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.SlNo == null || dto.SlNo <= 0)
                return BadRequestResult("Invoice Sl No is required.");
            if (dto.ReturnQty == null || dto.ReturnQty <= 0)
                return BadRequestResult("Return Quantity is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallSalesReturnDtlSP(dto);
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
                var result = await db.CallSalesReturnDtlSP(new SP_SalesReturnDtl
                {
                    TransSlNo = transSlNo,
                    MTransNo = mTransNo,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "Delete"
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
                var result = await db.CallSalesReturnDtlSP(new SP_SalesReturnDtl
                {
                    MTransNo = mTransNo,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "DeleteAll"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Sales Return.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.SalesReturnDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.InvTransNo,
                        x.LinkNo,
                        x.PartNo,
                        x.PartCode,
                        x.Description,
                        x.MocDescription,
                        x.Qty,
                        x.ReturnQty,
                        x.Unit,
                        x.Rate,
                        x.GSTP,
                        x.TotalValue,
                        x.ItemRemarks,
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
                var data = await db.SalesReturnDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo &&
                        x.SubscID == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
