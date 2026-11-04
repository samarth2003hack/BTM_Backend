using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Sales (Tax) Invoice — bills an approved Order Acceptance.
    /// STxnMst.Target* = the OA; STxnDtl.LinkNo = the OADtl.TransSlNo billed.
    /// Accounting distribution (STxnAcc) is not posted from here.
    /// </summary>
    [Route("api/[controller]")]
    public class SalesInvoiceController : EmsControllerBase
    {
        private readonly EMSContext db;

        private static readonly string[] InvTypes = { "Domestic", "Export" };

        public SalesInvoiceController(EMSContext context) => db = context;

        // ============================================================
        // SOURCE — ORDER ACCEPTANCES OPEN TO INVOICE
        // ============================================================

        /// <summary>
        /// One billed-quantity row per invoice line raised against an OA
        /// document (any revision of DocSeries + DocYear + DocNo), keyed by the
        /// OA line's SlNo so an amended OA still sees what was billed earlier.
        /// </summary>
        private IQueryable<BilledRow> BilledAgainstOa(int subscID) =>
            from s in db.STxnDtl.AsNoTracking()
            join h in db.STxnMst.AsNoTracking() on s.MTransNo equals h.MTransNo
            join d in db.OADtl.AsNoTracking() on s.LinkNo equals d.TransSlNo
            join m in db.OAMst.AsNoTracking() on d.MTransNo equals m.MTransNo
            where s.DeleteStatus == "N" && h.DeleteStatus == "N" && h.SubscID == subscID
            select new BilledRow
            {
                TransSlNo   = s.TransSlNo,
                MTransNo    = s.MTransNo,
                LinkNo      = s.LinkNo,
                OADocSeries = m.DocSeries,
                OADocYear   = m.DocYear,
                OADocNo     = m.DocNo,
                OASlNo      = d.SlNo,
                Qty         = s.Qty,
                ItemRemarks = s.ItemRemarks
            };

        private sealed class BilledRow
        {
            public int TransSlNo { get; set; }
            public int MTransNo { get; set; }
            public int? LinkNo { get; set; }
            public string? OADocSeries { get; set; }
            public int? OADocYear { get; set; }
            public int? OADocNo { get; set; }
            public decimal? OASlNo { get; set; }
            public decimal Qty { get; set; }
            public string? ItemRemarks { get; set; }
        }

        private static string OaKey(string? series, int? year, int? no, decimal? slNo) =>
            $"{(series ?? "").Trim()}|{year}|{no}|{slNo}";

        /// <summary>
        /// Approved, current-revision OAs with at least one line still open
        /// to invoice. Pass includeTransNo to keep an OA in the list even when
        /// fully billed (the invoice being edited bills it).
        /// </summary>
        [HttpGet("Source/OAList/{subscID}")]
        public async Task<IActionResult> SourceOAList(int subscID, [FromQuery] int? includeTransNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var headers = await db.OAMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" &&
                                ((x.DocAmdStatus == "N" && x.LockStatus == "Y") ||
                                 (includeTransNo != null && x.MTransNo == includeTransNo)))
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate, x.DocAmdNo,
                        x.VendorNo, x.VendorCode, x.VendorName, x.RefNo, x.RefDate,
                        x.TaxType, x.TxnCurrency, x.CurrencyRate, x.NetValue, x.LockStatus
                    })
                    .ToListAsync();

                var ids = headers.Select(h => h.MTransNo).ToList();
                var lines = await db.OADtl
                    .AsNoTracking()
                    .Where(d => ids.Contains(d.MTransNo) && d.DeleteStatus == "N")
                    .Select(d => new { d.MTransNo, d.SlNo, d.Qty })
                    .ToListAsync();

                var billed = (await BilledAgainstOa(subscID).ToListAsync())
                    .GroupBy(b => OaKey(b.OADocSeries, b.OADocYear, b.OADocNo, b.OASlNo))
                    .ToDictionary(g => g.Key, g => g.Sum(b => b.Qty));

                var data = headers
                    .Select(h =>
                    {
                        var open = lines
                            .Where(l => l.MTransNo == h.MTransNo)
                            .Sum(l =>
                            {
                                billed.TryGetValue(OaKey(h.DocSeries, h.DocYear, h.DocNo, l.SlNo), out var done);
                                return Math.Max(0, l.Qty - done);
                            });
                        return new
                        {
                            h.MTransNo, DocSeries = h.DocSeries?.Trim(), h.DocYear, h.DocNo, h.DocDate, h.DocAmdNo,
                            h.VendorNo, h.VendorCode, h.VendorName, h.RefNo, h.RefDate,
                            h.TaxType, h.TxnCurrency, h.CurrencyRate, h.NetValue, h.LockStatus,
                            OpenQty = open
                        };
                    })
                    .Where(x => x.OpenQty > 0 || x.MTransNo == includeTransNo)
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One OA's lines with BalQty = OA qty − everything billed on OTHER
        /// non-deleted invoices. With mTransNo (editing), that invoice's own
        /// row for each line comes back as InvData and is not subtracted.
        /// FreightRate is per unit (OA FreightAmt / Qty) and PnFP is the OA
        /// line's P&amp;F figure, both exactly as the legacy GetOADtl sent them.
        /// </summary>
        [HttpGet("Source/OAItems/{oaTransNo}/{subscID}")]
        public async Task<IActionResult> SourceOAItems(int oaTransNo, int subscID, [FromQuery] int mTransNo = 0)
        {
            if (oaTransNo <= 0) return BadRequestResult("Invalid Order Acceptance.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var oa = await db.OAMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == oaTransNo && x.SubscID == subscID && x.DeleteStatus == "N");
                if (oa == null) return NotFoundResult("Order Acceptance not found.");

                var lines = await (
                    from d in db.OADtl.AsNoTracking()
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    where d.MTransNo == oaTransNo && d.DeleteStatus == "N"
                    orderby d.SlNo, d.TransSlNo
                    select new
                    {
                        d.TransSlNo, d.SlNo, d.PartNo, d.PartCode, d.PartDescription,
                        d.MocCode, d.MocDescription, d.ValveCode, d.ValveDescription,
                        d.ItemRemarks, d.CustPartNo, d.Qty, d.Unit, d.Rate, d.DiscP,
                        d.PnFP, d.PnFAmt, d.FreightAmt, d.GSTP,
                        HSNCode = p != null ? p.HSNCode : null
                    })
                    .ToListAsync();

                var series = (oa.DocSeries ?? "").Trim();
                var billed = await BilledAgainstOa(subscID)
                    .Where(b => b.OADocSeries == series && b.OADocYear == oa.DocYear && b.OADocNo == oa.DocNo)
                    .ToListAsync();

                var data = lines.Select(l =>
                {
                    var others = billed
                        .Where(b => b.OASlNo == l.SlNo && b.MTransNo != mTransNo)
                        .Sum(b => b.Qty);
                    var own = mTransNo > 0
                        ? billed.FirstOrDefault(b => b.MTransNo == mTransNo && b.LinkNo == l.TransSlNo)
                          ?? billed.FirstOrDefault(b => b.MTransNo == mTransNo && b.OASlNo == l.SlNo)
                        : null;

                    return new
                    {
                        l.TransSlNo, l.SlNo, l.PartNo, l.PartCode, l.PartDescription,
                        l.MocCode, l.MocDescription, l.ValveCode, l.ValveDescription,
                        l.ItemRemarks, l.CustPartNo, l.HSNCode,
                        l.Qty, l.Unit, l.Rate, l.DiscP,
                        l.PnFP, l.PnFAmt, l.FreightAmt,
                        FreightRate = (l.FreightAmt ?? 0) > 0 && l.Qty > 0
                            ? Math.Round((l.FreightAmt ?? 0) / l.Qty, 2)
                            : 0m,
                        l.GSTP,
                        BilledQty = others,
                        BalQty = Math.Max(0, l.Qty - others),
                        InvData = own == null ? null : new { own.TransSlNo, own.Qty, own.ItemRemarks }
                    };
                }).ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // MST — INVOICE HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_STxnMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (string.IsNullOrWhiteSpace(dto.InvType) || !InvTypes.Contains(dto.InvType.Trim()))
                return BadRequestResult("Invoice Type must be Domestic or Export.");
            if (dto.TargetTransNo == null || dto.TargetTransNo <= 0)
                return BadRequestResult("Order Acceptance is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.Status == "Insert" && (dto.CreatedBy == null || dto.CreatedBy <= 0))
                return BadRequestResult("Invalid User.");
            if (dto.Status == "Update" && (dto.ModifiedBy == null || dto.ModifiedBy <= 0))
                return BadRequestResult("Invalid User.");

            try
            {
                // The customer, reference and target snapshot always come from
                // the OA itself — never from the client.
                var oa = await db.OAMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == dto.TargetTransNo &&
                                              x.SubscID == dto.SubscID &&
                                              x.DeleteStatus == "N");
                if (oa == null)
                    return BadRequestResult("Order Acceptance not found.");
                if (oa.VendorNo == null || oa.VendorNo <= 0)
                    return BadRequestResult("The Order Acceptance has no customer.");

                dto.TargetDocYear = oa.DocYear;
                dto.TargetDocNo   = oa.DocNo;
                dto.TargetAmdNo   = oa.DocAmdNo;
                dto.VendorNo      = oa.VendorNo;
                dto.VendorNoShip  = oa.VendorNoShip;
                dto.VendorCode    = oa.VendorCode;
                dto.VendorName    = oa.VendorName;
                dto.Address       = oa.Address;
                dto.GSTIN         = oa.GSTIN;
                dto.StateName     = oa.StateName;
                dto.PhoneNo       = oa.PhoneNo;
                dto.EmailID       = oa.EmailID;
                dto.RefNo         = oa.RefNo;
                dto.RefDate       = oa.RefDate;
                if (string.IsNullOrWhiteSpace(dto.TaxType))     dto.TaxType     = oa.TaxType;
                if (string.IsNullOrWhiteSpace(dto.TxnCurrency)) dto.TxnCurrency = oa.TxnCurrency;
                if (string.IsNullOrWhiteSpace(dto.SalesRep))    dto.SalesRep    = oa.SalesExec;

                // BankName carries the bank code the page shows; resolve its key.
                if ((dto.BankNo == null || dto.BankNo <= 0) && !string.IsNullOrWhiteSpace(dto.BankName))
                {
                    var code = dto.BankName.Trim();
                    dto.BankNo = await db.BankMaster
                        .AsNoTracking()
                        .Where(b => b.SubscID == dto.SubscID && b.DeleteStatus == "N" && b.BankCode == code)
                        .Select(b => (int?)b.MTransNo)
                        .FirstOrDefaultAsync();
                }

                if (dto.Status == "Insert") dto.CreatedOn ??= DateTime.Now;
                else dto.ModifiedOn ??= DateTime.Now;

                dto.SystemIP = ClientIp;
                var result = await db.CallSTxnMstSP(dto);
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
                var result = await db.CallSTxnMstSP(new SP_STxnMst
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
                var result = await db.CallSTxnMstSP(new SP_STxnMst
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
            [FromQuery] string? invType = null,
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
                var query = db.STxnMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (!string.IsNullOrWhiteSpace(invType))
                    query = query.Where(x => x.InvType == invType.Trim());
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

                var data = await query
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        DocSeries = x.DocSeries!.Trim(),
                        x.DocYear,
                        x.DocNo,
                        x.DocDate,
                        x.InvType,
                        x.TargetTransNo,
                        x.TargetDocYear,
                        x.TargetDocNo,
                        x.TargetAmdNo,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.RefNo,
                        x.TaxType,
                        x.TxnCurrency,
                        x.CurrencyRate,
                        x.CurrencyAmount,
                        x.RoundOff,
                        x.NetValue,
                        x.DocNotes,
                        x.TransportName,
                        x.TransportID,
                        x.BankNo,
                        x.BankName,
                        x.LUTNo,
                        x.PrintType,
                        x.IRNNo,
                        x.EWBillNo,
                        x.DocStatus,
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

        [HttpGet("Mst/GetData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.STxnMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id      &&
                        x.SubscID      == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                data.DocSeries = data.DocSeries?.Trim();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The number the next invoice in a series + year + invoice type would
        /// get — for display only. SP_STxnMst allocates the real DocNo itself
        /// at insert (deleted invoices included, so a number is never reused).
        /// </summary>
        [HttpGet("Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> MstGetNextDocNo(
            int subscID,
            [FromQuery] string docSeries = "",
            [FromQuery] int docYear = 0,
            [FromQuery] string invType = "")
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(docSeries)) return BadRequestResult("Doc Series is required.");
            if (docYear <= 0) return BadRequestResult("Doc Year is required.");
            if (string.IsNullOrWhiteSpace(invType)) return BadRequestResult("Invoice Type is required.");

            try
            {
                var series = docSeries.Trim();
                var type = invType.Trim();
                var max = await db.STxnMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear &&
                                x.InvType == type && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — INVOICE LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_STxnDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Sales Invoice.");
            if (dto.Status == "Update" && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.LinkNo == null || dto.LinkNo <= 0)
                return BadRequestResult("Order Acceptance line is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Inv Qty should not be zero.");
            if (dto.Rate == null || dto.Rate < 0)
                return BadRequestResult("Rate is required.");
            if (dto.BasicValue == null)
                return BadRequestResult("Basic Value is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.Status == "Insert" && (dto.CreatedBy == null || dto.CreatedBy <= 0))
                return BadRequestResult("Invalid User.");
            if (dto.Status == "Update" && (dto.ModifiedBy == null || dto.ModifiedBy <= 0))
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status == "Insert") dto.CreatedOn ??= DateTime.Now;
                else dto.ModifiedOn ??= DateTime.Now;

                dto.SystemIP = ClientIp;
                var result = await db.CallSTxnDtlSP(dto);
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
                var result = await db.CallSTxnDtlSP(new SP_STxnDtl
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
                var result = await db.CallSTxnDtlSP(new SP_STxnDtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Sales Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.STxnDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo, x.LinkNo,
                        x.PartNo, x.PartCode, x.Description, x.ItemRemarks, x.CustPartNo, x.HSNCode,
                        x.Qty, x.Unit, x.Rate, x.BasicValue, x.DiscP, x.DiscAmt,
                        x.PnFP, x.PnFAmt, x.FreightRate, x.FreightAmt,
                        x.TaxableValue, x.GSTP, x.IGSTAmt, x.CGSTAmt, x.SGSTAmt, x.TotalValue,
                        x.LockStatus, x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
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
                var data = await db.STxnDtl
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
        public async Task<IActionResult> TermsSaveData([FromBody] SP_StxnTerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Sales Invoice.");
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
                if (dto.Status == "Insert") dto.CreatedOn ??= DateTime.Now;
                else dto.ModifiedOn ??= DateTime.Now;
                dto.SystemIP = ClientIp;
                var result = await db.CallStxnTermsSP(dto);
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
                var result = await db.CallStxnTermsSP(new SP_StxnTerms
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
                var result = await db.CallStxnTermsSP(new SP_StxnTerms
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Sales Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.StxnTerms
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

        // ============================================================
        // EXTRAS — the answered "Invoice Extras" terms
        // ============================================================

        [HttpPost("Extras/SaveData")]
        public async Task<IActionResult> ExtrasSaveData([FromBody] SP_STxnExtra dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Sales Invoice.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status == "Insert") dto.CreatedOn ??= DateTime.Now;
                else dto.ModifiedOn ??= DateTime.Now;
                dto.SystemIP = ClientIp;
                var result = await db.CallSTxnExtraSP(dto);
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
                var result = await db.CallSTxnExtraSP(new SP_STxnExtra
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
                var result = await db.CallSTxnExtraSP(new SP_STxnExtra
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Sales Invoice.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.STxnExtra
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
    }
}
