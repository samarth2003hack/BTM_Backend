using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Proforma Invoice — an advance invoice raised against an Order Acceptance.
    ///
    ///   ProformaMst.TargetTransNo = OAMst.MTransNo (Target* = the OA)
    ///   ProformaDtl.LinkNo        = OADtl.TransSlNo (the OA line being billed)
    ///
    /// DocNo is numbered per DocSeries + DocYear + InvType; SP_ProformaMst
    /// allocates it on a first issue, so GetNextDocNo is a preview only.
    /// SP_ProformaDtl refuses a line that would bill more than the OA balance.
    /// </summary>
    [Route("api/[controller]")]
    public class ProformaController : EmsControllerBase
    {
        private readonly EMSContext db;

        private static readonly string[] InvTypes = { "Domestic", "Export" };

        public ProformaController(EMSContext context) => db = context;

        // ============================================================
        // MST — PROFORMA HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_ProformaMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            var isUpdate = dto.Status == "Update";
            if (!isUpdate && dto.Status != "Insert")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (isUpdate && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("INV Date is required.");
            if (string.IsNullOrWhiteSpace(dto.InvType) ||
                !InvTypes.Contains(dto.InvType.Trim(), StringComparer.OrdinalIgnoreCase))
                return BadRequestResult("Invoice Type must be Domestic or Export.");
            if (dto.TargetTransNo == null || dto.TargetTransNo <= 0)
                return BadRequestResult("Select the OA No.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (isUpdate ? (dto.ModifiedBy ?? dto.CreatedBy ?? 0) <= 0 : (dto.CreatedBy ?? 0) <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.AdvanceP < 0 || dto.AdvanceP > 100)
                return BadRequestResult("Advance % must be between 0 and 100.");

            try
            {
                // The customer is always the OA's — snapshot it server-side so a
                // client can never bill one customer's OA to another.
                var oa = await db.OAMst.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.MTransNo     == dto.TargetTransNo &&
                    x.SubscID      == dto.SubscID       &&
                    x.DeleteStatus == "N");
                if (oa == null)
                    return BadRequestResult("Order Acceptance not found.");
                if (oa.VendorNo == null || oa.VendorNo <= 0)
                    return BadRequestResult("The Order Acceptance has no customer.");

                dto.InvType       = InvTypes.First(t => t.Equals(dto.InvType!.Trim(), StringComparison.OrdinalIgnoreCase));
                dto.AmdNo       ??= 0;
                dto.TargetDocYear = oa.DocYear;
                dto.TargetDocNo   = oa.DocNo;
                dto.TargetAmdNo   = oa.DocAmdNo;
                dto.VendorNo      = oa.VendorNo;
                dto.VendorNoShip ??= oa.VendorNoShip;
                dto.VendorCode    = oa.VendorCode;
                dto.VendorName    = oa.VendorName;
                dto.Address       = oa.Address;
                dto.GSTIN         = oa.GSTIN;
                dto.StateName     = oa.StateName;
                dto.PhoneNo       = oa.PhoneNo;
                dto.EmailID       = oa.EmailID;
                dto.RefNo         = oa.RefNo;
                dto.RefDate       = oa.RefDate;
                if (string.IsNullOrWhiteSpace(dto.SalesRep))    dto.SalesRep    = oa.SalesExec;
                if (string.IsNullOrWhiteSpace(dto.TaxType))     dto.TaxType     = oa.TaxType;
                if (string.IsNullOrWhiteSpace(dto.TxnCurrency)) dto.TxnCurrency = oa.TxnCurrency;
                if (dto.CurrencyRate == null || dto.CurrencyRate <= 0) dto.CurrencyRate = oa.CurrencyRate ?? 1;

                if (NoGstSplit(dto.IGSTAmt, dto.CGSTAmt, dto.SGSTAmt) && (dto.GSTAmt ?? 0) != 0)
                {
                    var intra = await IsIntraStateAsync(dto.SubscID.Value, dto.GSTIN, dto.StateName, dto.TaxType, dto.InvType);
                    (dto.IGSTAmt, dto.CGSTAmt, dto.SGSTAmt) = SplitGst(dto.GSTAmt!.Value, intra);
                }

                if (isUpdate)
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                else
                {
                    dto.MTransNo  = 0;
                    dto.CreatedOn = DateTime.Now;
                }

                dto.SystemIP = ClientIp;
                var result = await db.CallProformaMstSP(dto);
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
                var result = await db.CallProformaMstSP(new SP_ProformaMst
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
                var result = await db.CallProformaMstSP(new SP_ProformaMst
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

        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] string? invType = null,
            [FromQuery] int? docNo = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] int? targetTransNo = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.ProformaMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (!string.IsNullOrWhiteSpace(invType))
                    query = query.Where(x => x.InvType == invType.Trim());
                if (docNo.HasValue && docNo > 0)
                    query = query.Where(x => x.DocNo == docNo);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (targetTransNo.HasValue && targetTransNo > 0)
                    query = query.Where(x => x.TargetTransNo == targetTransNo);
                if (fromDate.HasValue)
                    query = query.Where(x => x.DocDate >= fromDate.Value.Date);
                if (toDate.HasValue)
                    query = query.Where(x => x.DocDate <= toDate.Value.Date);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.VendorCode!.Contains(search) ||
                        x.RefNo!.Contains(search)      ||
                        x.DocNotes!.Contains(search));

                var rows = await query
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.DocSeries,
                        x.DocYear,
                        x.DocNo,
                        x.DocDate,
                        x.InvType,
                        x.AmdNo,
                        x.TargetTransNo,
                        x.TargetDocYear,
                        x.TargetDocNo,
                        x.TargetAmdNo,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.RefNo,
                        x.RefDate,
                        x.TaxType,
                        x.TxnCurrency,
                        x.CurrencyRate,
                        x.CurrencyAmount,
                        x.DocNotes,
                        x.RoundOff,
                        x.NetValue,
                        x.AdvanceP,
                        x.Advance,
                        x.DocStatus,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                // DocSeries is CHAR(10) — hand it back without the padding.
                var data = rows.Select(x => new
                {
                    x.MTransNo, DocSeries = x.DocSeries?.Trim(), x.DocYear, x.DocNo, x.DocDate,
                    x.InvType, x.AmdNo, x.TargetTransNo, x.TargetDocYear, x.TargetDocNo, x.TargetAmdNo,
                    x.VendorNo, x.VendorCode, x.VendorName, x.RefNo, x.RefDate, x.TaxType,
                    x.TxnCurrency, x.CurrencyRate, x.CurrencyAmount, x.DocNotes,
                    x.RoundOff, x.NetValue, x.AdvanceP, x.Advance,
                    x.DocStatus, x.LockStatus, x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                }).ToList();

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
                var data = await db.ProformaMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id      &&
                        x.SubscID      == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                data.DocSeries = data.DocSeries?.Trim();
                data.InvTag    = data.InvTag?.Trim();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Preview of the next DocNo in a series + year + invoice type.
        /// Display only — SP_ProformaMst allocates the real number at insert.
        /// </summary>
        [HttpGet("Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> MstGetNextDocNo(
            int subscID,
            [FromQuery] string docSeries = "",
            [FromQuery] int docYear = 0,
            [FromQuery] string invType = "Domestic")
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries)) return BadRequestResult("Doc Series is required.");
            if (docYear <= 0) return BadRequestResult("Doc Year is required.");
            if (string.IsNullOrWhiteSpace(invType)) return BadRequestResult("Invoice Type is required.");

            try
            {
                var series = docSeries.Trim();
                var type   = invType.Trim();
                // Same scope as the SP: deleted rows count, so a number is never reused.
                var max = await db.ProformaMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear &&
                                x.InvType   == type   && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — PROFORMA LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_ProformaDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            var isUpdate = dto.Status == "Update";
            if (!isUpdate && dto.Status != "Insert")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Proforma Invoice.");
            if (isUpdate && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Inv Qty should not be zero.");
            if (dto.Rate == null || dto.Rate < 0)
                return BadRequestResult("Rate is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (isUpdate ? (dto.ModifiedBy ?? dto.CreatedBy ?? 0) <= 0 : (dto.CreatedBy ?? 0) <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                var mst = await db.ProformaMst.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.MTransNo == dto.MTransNo && x.SubscID == dto.SubscID && x.DeleteStatus == "N");
                if (mst == null)
                    return BadRequestResult("Proforma Invoice not found.");

                // A billed line always comes from the proforma's own OA; its
                // identity (part, code, description) is taken from the OA line.
                if (dto.LinkNo != null && dto.LinkNo > 0)
                {
                    var line = await db.OADtl.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TransSlNo == dto.LinkNo && x.DeleteStatus == "N");
                    if (line == null || line.MTransNo != mst.TargetTransNo)
                        return BadRequestResult("The line does not belong to this proforma's Order Acceptance.");

                    dto.PartNo = line.PartNo;
                    if (string.IsNullOrWhiteSpace(dto.PartCode))    dto.PartCode    = line.PartCode;
                    if (string.IsNullOrWhiteSpace(dto.Description)) dto.Description = Truncate(OaLineDescription(line), 250);
                    if (string.IsNullOrWhiteSpace(dto.CustPartNo))  dto.CustPartNo  = Truncate(line.CustPartNo, 30);
                    if (string.IsNullOrWhiteSpace(dto.Unit))        dto.Unit        = line.Unit;
                    dto.DeleveryDate ??= line.DeliveryDate;
                }
                if (dto.PartNo == null || dto.PartNo <= 0)
                    return BadRequestResult("Part No is required.");

                if (string.IsNullOrWhiteSpace(dto.HSNCode))
                {
                    var hsn = await db.PartMaster.AsNoTracking()
                        .Where(x => x.MTransNo == dto.PartNo)
                        .Select(x => x.HSNCode)
                        .FirstOrDefaultAsync();
                    dto.HSNCode = Truncate(hsn, 10);
                }

                dto.BasicValue    ??= Math.Round((dto.Qty ?? 0) * (dto.Rate ?? 0), 2);
                dto.DTargetDocNo  ??= mst.TargetDocNo;
                dto.DTargetDocYear ??= mst.TargetDocYear;
                dto.VendorNo      ??= mst.VendorNo;

                if (NoGstSplit(dto.IGSTAmt, dto.CGSTAmt, dto.SGSTAmt) && (dto.GSTAmt ?? 0) != 0)
                {
                    var intra = await IsIntraStateAsync(dto.SubscID.Value, mst.GSTIN, mst.StateName, mst.TaxType, mst.InvType);
                    (dto.IGSTAmt, dto.CGSTAmt, dto.SGSTAmt) = SplitGst(dto.GSTAmt!.Value, intra);
                }

                if (isUpdate)
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn   = DateTime.Now;
                }
                else
                {
                    dto.TransSlNo = 0;
                    dto.CreatedOn = DateTime.Now;
                }

                dto.SystemIP = ClientIp;
                var result = await db.CallProformaDtlSP(dto);
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
                var result = await db.CallProformaDtlSP(new SP_ProformaDtl
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
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
                var result = await db.CallProformaDtlSP(new SP_ProformaDtl
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    SystemIP     = ClientIp,
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Proforma Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.ProformaDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
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
                var data = await db.ProformaDtl
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
        public async Task<IActionResult> TermsSaveData([FromBody] SP_ProformaTerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Proforma Invoice.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (string.IsNullOrWhiteSpace(dto.Description))
                return BadRequestResult("Description is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if ((dto.Status == "Update" ? dto.ModifiedBy ?? dto.CreatedBy : dto.CreatedBy) is not > 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status == "Update") { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                else dto.CreatedOn = DateTime.Now;
                dto.SystemIP = ClientIp;
                var result = await db.CallProformaTermsSP(dto);
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
                var result = await db.CallProformaTermsSP(new SP_ProformaTerms
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
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
                var result = await db.CallProformaTermsSP(new SP_ProformaTerms
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    SystemIP     = ClientIp,
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Proforma Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.ProformaTerms
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
                var data = await db.ProformaTerms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // EXTRAS (table ProformaExtra)
        // ============================================================

        [HttpPost("Extras/SaveData")]
        public async Task<IActionResult> ExtrasSaveData([FromBody] SP_ProformaExtra dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Proforma Invoice.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if ((dto.Status == "Update" ? dto.ModifiedBy ?? dto.CreatedBy : dto.CreatedBy) is not > 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status == "Update") { dto.ModifiedBy ??= dto.CreatedBy; dto.ModifiedOn = DateTime.Now; }
                else dto.CreatedOn = DateTime.Now;
                dto.SystemIP = ClientIp;
                var result = await db.CallProformaExtraSP(dto);
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
                var result = await db.CallProformaExtraSP(new SP_ProformaExtra
                {
                    TransSlNo    = transSlNo,
                    MTransNo     = mTransNo,
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
                var result = await db.CallProformaExtraSP(new SP_ProformaExtra
                {
                    MTransNo     = mTransNo,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    SystemIP     = ClientIp,
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Proforma Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.ProformaExtra
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
                var data = await db.ProformaExtra
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — upstream Order Acceptance pickers
        // ============================================================

        /// <summary>
        /// Current-revision, non-deleted OAs that still have quantity left to
        /// proforma. `includeTransNo` keeps one OA in the list even when it is
        /// fully billed (the OA of the proforma being edited).
        /// </summary>
        [HttpGet("Source/OAList/{subscID}")]
        public async Task<IActionResult> SourceOAList(int subscID, [FromQuery] int? includeTransNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var oas = await db.OAMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" &&
                                (x.DocAmdStatus == null || x.DocAmdStatus == "N"))
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocAmdNo, x.DocDate,
                        x.VendorNo, x.VendorCode, x.VendorName, x.RefNo, x.RefDate,
                        x.TaxType, x.TxnCurrency, x.CurrencyRate, x.NetValue
                    })
                    .ToListAsync();

                if (oas.Count == 0)
                    return OkResult(Array.Empty<object>(), "No records found.");

                var oaIds = oas.Select(x => x.MTransNo).ToList();

                var ordered = await db.OADtl
                    .AsNoTracking()
                    .Where(x => oaIds.Contains(x.MTransNo) && x.DeleteStatus == "N")
                    .Select(x => new { x.MTransNo, x.TransSlNo, x.Qty })
                    .ToListAsync();

                var billed = await BilledByLinkAsync(ordered.Select(x => x.TransSlNo).ToList(), subscID, null);

                var balance = ordered
                    .GroupBy(x => x.MTransNo)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(l => Math.Max(0, l.Qty - billed.GetValueOrDefault(l.TransSlNo))));

                var data = oas
                    .Select(x => new
                    {
                        x.MTransNo, DocSeries = x.DocSeries?.Trim(), x.DocYear, x.DocNo, x.DocAmdNo, x.DocDate,
                        x.VendorNo, x.VendorCode, x.VendorName, x.RefNo, x.RefDate,
                        x.TaxType, x.TxnCurrency, x.CurrencyRate, x.NetValue,
                        BalQty = balance.GetValueOrDefault(x.MTransNo)
                    })
                    .Where(x => x.BalQty > 0 || x.MTransNo == includeTransNo)
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The lines of one OA with the balance still open to proforma:
        /// balQty = OA qty − qty on other live proformas. With `mTransNo` (edit)
        /// that proforma's own rows are left out of the "already billed" sum and
        /// merged in as `invData { transSlNo, qty, itemRemarks }`.
        /// </summary>
        [HttpGet("Source/OAItems/{oaTransNo}/{subscID}")]
        public async Task<IActionResult> SourceOAItems(int oaTransNo, int subscID, [FromQuery] int? mTransNo = null)
        {
            if (oaTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var lines = await db.OADtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == oaTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .ToListAsync();

                if (lines.Count == 0)
                    return OkResult(Array.Empty<object>(), "No records found.");

                var linkIds  = lines.Select(x => x.TransSlNo).ToList();
                var ownId    = mTransNo.GetValueOrDefault();
                var billed   = await BilledByLinkAsync(linkIds, subscID, ownId > 0 ? ownId : null);

                var own = ownId > 0
                    ? await db.ProformaDtl
                        .AsNoTracking()
                        .Where(x => x.MTransNo == ownId && x.SubscID == subscID &&
                                    x.DeleteStatus == "N" && x.LinkNo != null && linkIds.Contains(x.LinkNo.Value))
                        .Select(x => new { x.TransSlNo, x.LinkNo, x.Qty, x.ItemRemarks })
                        .ToListAsync()
                    : new();

                var partIds = lines.Select(x => x.PartNo).Distinct().ToList();
                var hsn = await db.PartMaster
                    .AsNoTracking()
                    .Where(x => partIds.Contains(x.MTransNo))
                    .Select(x => new { x.MTransNo, x.HSNCode })
                    .ToDictionaryAsync(x => x.MTransNo, x => x.HSNCode);

                var data = lines.Select(x =>
                {
                    var mine = own.FirstOrDefault(o => o.LinkNo == x.TransSlNo);
                    return new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.PartNo,
                        x.PartCode,
                        x.ValveCode,
                        x.PartDescription,
                        x.MocDescription,
                        x.CustPartNo,
                        HSNCode = hsn.GetValueOrDefault(x.PartNo),
                        x.Qty,
                        BalQty = Math.Max(0, x.Qty - billed.GetValueOrDefault(x.TransSlNo)),
                        x.Unit,
                        x.Rate,
                        x.DiscP,
                        x.PnFP,
                        x.FreightRate,
                        x.FreightAmt,
                        x.GSTP,
                        x.ItemRemarks,
                        x.DeliveryDate,
                        InvData = mine == null
                            ? null
                            : new { mine.TransSlNo, mine.Qty, mine.ItemRemarks }
                    };
                }).ToList();

                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>Qty already on live proformas per OADtl.TransSlNo, optionally excluding one proforma.</summary>
        private async Task<Dictionary<int, decimal>> BilledByLinkAsync(List<int> linkIds, int subscID, int? excludeMTransNo)
        {
            if (linkIds.Count == 0) return new();

            var q = from d in db.ProformaDtl.AsNoTracking()
                    join m in db.ProformaMst.AsNoTracking() on d.MTransNo equals m.MTransNo
                    where d.DeleteStatus == "N" && m.DeleteStatus == "N" &&
                          d.SubscID == subscID && d.LinkNo != null && linkIds.Contains(d.LinkNo.Value)
                    select new { d.MTransNo, LinkNo = d.LinkNo!.Value, d.Qty };

            if (excludeMTransNo.HasValue)
                q = q.Where(x => x.MTransNo != excludeMTransNo.Value);

            return await q
                .GroupBy(x => x.LinkNo)
                .Select(g => new { g.Key, Qty = g.Sum(x => x.Qty) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);
        }

        private static bool NoGstSplit(decimal? igst, decimal? cgst, decimal? sgst) =>
            (igst ?? 0) == 0 && (cgst ?? 0) == 0 && (sgst ?? 0) == 0;

        /// <summary>
        /// Place of supply: CGST + SGST when the customer is in the company's
        /// state, IGST otherwise. Export / SEZ always attract IGST. States are
        /// compared by GSTIN state code first, then by state name; when neither
        /// side can be determined the supply is treated as intra-state.
        /// </summary>
        private async Task<bool> IsIntraStateAsync(int subscID, string? gstin, string? stateName, string? taxType, string? invType)
        {
            var tax = (taxType ?? "").Trim().ToUpperInvariant();
            if (string.Equals(invType, "Export", StringComparison.OrdinalIgnoreCase) ||
                tax == "EXPORT" || tax == "SEZ" || tax == "IGST")
                return false;

            var comp = await db.Company.AsNoTracking()
                .Where(x => x.SubscID == subscID && x.DeleteStatus != "Y")
                .OrderBy(x => x.MTransNo)
                .Select(x => new { x.GSTIN, x.State })
                .FirstOrDefaultAsync();
            if (comp == null) return true;

            static string? Code(string? g) =>
                g != null && g.Trim().Length >= 2 && char.IsDigit(g.Trim()[0]) && char.IsDigit(g.Trim()[1])
                    ? g.Trim().Substring(0, 2) : null;

            var a = Code(comp.GSTIN);
            var b = Code(gstin);
            if (a != null && b != null) return a == b;

            if (!string.IsNullOrWhiteSpace(comp.State) && !string.IsNullOrWhiteSpace(stateName))
                return string.Equals(comp.State.Trim(), stateName.Trim(), StringComparison.OrdinalIgnoreCase);

            return true;
        }

        private static (decimal? igst, decimal? cgst, decimal? sgst) SplitGst(decimal gst, bool intraState)
        {
            if (!intraState) return (Math.Round(gst, 2), 0m, 0m);
            var cgst = Math.Round(gst / 2, 2);
            return (0m, cgst, Math.Round(gst - cgst, 2));
        }

        private static string? OaLineDescription(OADtl line)
        {
            var parts = new[] { line.ValveCode, line.PartDescription, line.MocDescription }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim());
            var text = string.Join(" / ", parts);
            return text.Length == 0 ? null : text;
        }

        private static string? Truncate(string? s, int max) =>
            s == null ? null : (s.Length <= max ? s : s.Substring(0, max));
    }
}
