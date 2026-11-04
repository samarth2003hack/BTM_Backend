using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Packing Slip — packs the lines of one Order Acceptance into cases.
    ///
    /// Writes go through SP_PackingSlipMst / SP_PackingSlipDtl. The header's
    /// customer + OA block and each line's part / MOC / unit are snapshotted
    /// here from the OA at save time, so the client only has to name the OA
    /// (TargetTransNo) and the OA line (OAID = OADtl.TransSlNo).
    ///
    /// Balance of an OA line = OADtl.Qty − qty on every non-deleted
    /// PackingSlipDtl row of every non-deleted slip. The Source endpoints
    /// take an optional mTransNo so the slip being edited does not count
    /// against itself.
    /// </summary>
    [Route("api/[controller]")]
    public class PackingSlipController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PackingSlipController(EMSContext context) => db = context;

        private static readonly string[] LockValues = { "S", "N", "Y" };

        // ============================================================
        // MST — PACKING SLIP HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_PackingSlipMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid Packing Slip.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.TargetTransNo == null || dto.TargetTransNo <= 0)
                return BadRequestResult("Order Acceptance is required.");
            if (dto.TotalCases != null && dto.TotalCases < 0)
                return BadRequestResult("Total Cases cannot be negative.");
            if (dto.TotalWeight != null && dto.TotalWeight < 0)
                return BadRequestResult("Total Weight cannot be negative.");
            if (!string.IsNullOrWhiteSpace(dto.LockStatus) && !LockValues.Contains(dto.LockStatus.Trim()))
                return BadRequestResult("Lock Status must be 'S', 'N' or 'Y'.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                var oa = await db.OAMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == dto.TargetTransNo &&
                        x.SubscID      == dto.SubscID       &&
                        x.DeleteStatus == "N");
                if (oa == null)
                    return BadRequestResult("Order Acceptance not found.");
                if (oa.VendorNo == null || oa.VendorNo <= 0)
                    return BadRequestResult("The Order Acceptance has no customer.");

                // A superseded OA revision may stay on a slip that already
                // points at it, but a new pick must be the current revision.
                if (oa.DocAmdStatus == "Y")
                {
                    int? existingTarget = null;
                    if (dto.Status == "Update")
                        existingTarget = await db.PackingSlipMst
                            .AsNoTracking()
                            .Where(x => x.MTransNo == dto.MTransNo && x.SubscID == dto.SubscID)
                            .Select(x => x.TargetTransNo)
                            .FirstOrDefaultAsync();
                    if (existingTarget != dto.TargetTransNo)
                        return ConflictResult("This Order Acceptance has been amended — pick its current revision.");
                }

                // Snapshot of the OA header.
                dto.TargetDocYear = oa.DocYear;
                dto.TargetDocNo   = oa.DocNo;
                dto.TargetAmdNo   = oa.DocAmdNo;
                dto.OAYear        = oa.DocYear;
                dto.OANo          = oa.DocNo;
                dto.OADate        = oa.DocDate;
                dto.VendorNo      = oa.VendorNo;
                dto.VendorCode    = oa.VendorCode ?? string.Empty;
                dto.VendorName    = oa.VendorName ?? string.Empty;
                dto.Address       = oa.Address    ?? string.Empty;
                dto.GSTIN         = oa.GSTIN      ?? string.Empty;
                dto.StateName     = oa.StateName;
                dto.PhoneNo       = oa.PhoneNo;
                dto.EmailID       = oa.EmailID;
                dto.RefNo         = oa.RefNo;       // customer PO
                dto.RefDate       = oa.RefDate;

                dto.LockStatus = string.IsNullOrWhiteSpace(dto.LockStatus) ? "S" : dto.LockStatus.Trim();
                dto.SystemIP   = ClientIp;
                if (dto.Status == "Insert")
                {
                    dto.MTransNo  = 0;
                    dto.CreatedOn = DateTime.Now;
                }
                else
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn  = DateTime.Now;
                }

                var result = await db.CallPackingSlipMstSP(dto);
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
                var result = await db.CallPackingSlipMstSP(new SP_PackingSlipMst
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
                var result = await db.CallPackingSlipMstSP(new SP_PackingSlipMst
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
            [FromQuery] int? targetTransNo = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PackingSlipMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
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
                        x.TargetAmdNo,
                        x.OAYear,
                        x.OANo,
                        x.OADate,
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.RefNo,
                        x.RefDate,
                        x.DocNotes,
                        x.TotalCases,
                        x.TotalWeight,
                        x.DocStatus,
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
                var data = await db.PackingSlipMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id      &&
                        x.SubscID      == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The number the next slip in a series + year would get — display
        /// only. SP_PackingSlipMst allocates the real DocNo at insert.
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
                var max = await db.PackingSlipMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — PACKED CASES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_PackingSlipDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Packing Slip.");
            if (dto.Status == "Update" && dto.TransSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");
            if (dto.OAID == null || dto.OAID <= 0)
                return BadRequestResult("OA ID is required.");
            if (dto.CaseNo == null || dto.CaseNo <= 0)
                return BadRequestResult("Case No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.NetWeight != null && dto.NetWeight < 0)
                return BadRequestResult("Net Weight cannot be negative.");
            if (dto.GrossWeight != null && dto.GrossWeight < 0)
                return BadRequestResult("Gross Weight cannot be negative.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                // Snapshot of the OA line (and the part's HSN) — never trusted
                // from the client.
                var line = await (
                    from l in db.OADtl.AsNoTracking()
                    join pm in db.PartMaster.AsNoTracking() on l.PartNo equals pm.MTransNo into pj
                    from pm in pj.DefaultIfEmpty()
                    where l.TransSlNo == dto.OAID && l.SubscID == dto.SubscID && l.DeleteStatus == "N"
                    select new
                    {
                        l.PartNo,
                        PartCode        = l.PartCode ?? pm.PartCode,
                        PartDescription = l.PartDescription ?? pm.PartName,
                        Moc             = l.MocDescription ?? l.MocShort,
                        l.ItemRemarks,
                        l.Unit,
                        HSNCode         = pm.HSNCode
                    }).FirstOrDefaultAsync();
                if (line == null)
                    return BadRequestResult("OA line not found.");

                dto.PartNo          = line.PartNo;
                dto.PartCode        = line.PartCode;
                dto.PartDescription = line.PartDescription;
                dto.MOC             = line.Moc;
                dto.OARemarks       = line.ItemRemarks;
                dto.Unit            = line.Unit;
                dto.HSNCode         = line.HSNCode;

                dto.SystemIP = ClientIp;
                if (dto.Status == "Insert")
                {
                    dto.TransSlNo = 0;
                    dto.CreatedOn = DateTime.Now;
                }
                else
                {
                    dto.ModifiedBy ??= dto.CreatedBy;
                    dto.ModifiedOn  = DateTime.Now;
                }

                var result = await db.CallPackingSlipDtlSP(dto);
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
                var result = await db.CallPackingSlipDtlSP(new SP_PackingSlipDtl
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
                var result = await db.CallPackingSlipDtlSP(new SP_PackingSlipDtl
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

        /// <summary>
        /// The slip's cases, each carrying the display fields of its OA line
        /// and balQty = OA qty − qty packed on OTHER slips (this slip's own
        /// rows are not subtracted, so the page can re-check its own split).
        /// </summary>
        [HttpGet("Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Packing Slip.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var rows = await db.PackingSlipDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo     == mTransNo &&
                                x.SubscID      == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .ToListAsync();

                var oaIds = rows.Select(r => r.OAID).Distinct().ToList();
                var oaQty = await db.OADtl
                    .AsNoTracking()
                    .Where(l => oaIds.Contains(l.TransSlNo) && l.SubscID == subscID && l.DeleteStatus == "N")
                    .Select(l => new { l.TransSlNo, l.Qty })
                    .ToDictionaryAsync(l => l.TransSlNo, l => l.Qty);
                var packed = await PackedByOaLine(subscID, oaIds, mTransNo);

                var data = rows.Select(x => new
                {
                    x.TransSlNo,
                    x.SlNo,
                    x.MTransNo,
                    x.OAID,
                    x.CaseNo,
                    x.PartNo,
                    x.PartCode,
                    x.PartDescription,
                    PartName  = PartLabel(x.PartCode, x.PartDescription),
                    x.MOC,
                    Grade     = x.MOC,
                    x.OARemarks,
                    x.ItemRemarks,
                    x.HSNCode,
                    x.Qty,
                    x.Unit,
                    x.CaseSize,
                    x.NetWeight,
                    x.GrossWeight,
                    x.ValveSlNo,
                    x.BoxType,
                    OAQty     = oaQty.TryGetValue(x.OAID, out var q) ? q : 0m,
                    BalQty    = (oaQty.TryGetValue(x.OAID, out var q2) ? q2 : 0m)
                                - (packed.TryGetValue(x.OAID, out var p) ? p : 0m),
                    x.LockStatus,
                    x.CreatedBy,
                    x.CreatedOn,
                    x.ModifiedBy,
                    x.ModifiedOn
                }).ToList();

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
                var data = await db.PackingSlipDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — ORDER ACCEPTANCES TO PACK
        // ============================================================

        /// <summary>
        /// Current-revision, non-deleted OAs that still have qty left to pack.
        /// Pass mTransNo when editing so that slip's own cases are not counted.
        /// </summary>
        [HttpGet("Source/OAList/{subscID}")]
        public async Task<IActionResult> SourceOAList(int subscID, [FromQuery] int mTransNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var headers = await db.OAMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.DocAmdStatus == "N")
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
                        x.DocStatus,
                        x.LockStatus
                    })
                    .ToListAsync();

                var oaNos = headers.Select(h => h.MTransNo).ToList();
                var lines = await db.OADtl
                    .AsNoTracking()
                    .Where(l => oaNos.Contains(l.MTransNo) && l.SubscID == subscID && l.DeleteStatus == "N")
                    .Select(l => new { l.TransSlNo, l.MTransNo, l.Qty })
                    .ToListAsync();
                var packed = await PackedByOaLine(subscID, lines.Select(l => l.TransSlNo).ToList(), mTransNo);

                var balByOa = lines
                    .GroupBy(l => l.MTransNo)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(l => Math.Max(0m, l.Qty - (packed.TryGetValue(l.TransSlNo, out var p) ? p : 0m))));

                var data = headers
                    .Where(h => balByOa.TryGetValue(h.MTransNo, out var b) && b > 0)
                    .OrderByDescending(h => h.DocYear)
                    .ThenByDescending(h => h.DocNo)
                    .Select(h => new
                    {
                        h.MTransNo,
                        h.DocSeries,
                        h.DocYear,
                        h.DocNo,
                        h.DocAmdNo,
                        OAYear = h.DocYear,
                        OANo   = h.DocNo,
                        OADate = h.DocDate,
                        h.VendorNo,
                        h.VendorCode,
                        h.VendorName,
                        h.RefNo,
                        h.RefDate,
                        h.DocStatus,
                        h.LockStatus,
                        BalQty = balByOa[h.MTransNo]
                    })
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The lines of one OA with what is left to pack on each. Lines with
        /// nothing left are omitted. Pass mTransNo when editing so that slip's
        /// own cases are not counted.
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
                    .Where(x => x.MTransNo == oaTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => new { x.DocYear, x.DocNo })
                    .FirstOrDefaultAsync();
                if (oa == null) return NotFoundResult();

                var lines = await (
                    from l in db.OADtl.AsNoTracking()
                    join pm in db.PartMaster.AsNoTracking() on l.PartNo equals pm.MTransNo into pj
                    from pm in pj.DefaultIfEmpty()
                    where l.MTransNo == oaTransNo && l.SubscID == subscID && l.DeleteStatus == "N"
                    orderby l.SlNo
                    select new
                    {
                        l.TransSlNo,
                        l.SlNo,
                        l.PartNo,
                        PartCode        = l.PartCode ?? pm.PartCode,
                        PartDescription = l.PartDescription ?? pm.PartName,
                        Moc             = l.MocDescription ?? l.MocShort,
                        l.ItemRemarks,
                        l.Qty,
                        l.Unit,
                        HSNCode         = pm.HSNCode
                    }).ToListAsync();

                var packed = await PackedByOaLine(subscID, lines.Select(l => l.TransSlNo).ToList(), mTransNo);

                var data = lines
                    .Select(l =>
                    {
                        var done = packed.TryGetValue(l.TransSlNo, out var p) ? p : 0m;
                        return new
                        {
                            OAID       = l.TransSlNo,
                            l.SlNo,
                            OAYear     = oa.DocYear,
                            OANo       = oa.DocNo,
                            l.PartNo,
                            l.PartCode,
                            l.PartDescription,
                            PartName   = PartLabel(l.PartCode, l.PartDescription),
                            MocDescription = l.Moc,
                            Grade      = l.Moc,
                            OARemarks  = l.ItemRemarks,
                            l.HSNCode,
                            l.Unit,
                            OAQty      = l.Qty,
                            PackedQty  = done,
                            BalQty     = l.Qty - done
                        };
                    })
                    .Where(l => l.BalQty > 0)
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>
        /// Qty already packed per OA line (OADtl.TransSlNo) on non-deleted
        /// rows of non-deleted slips, leaving out slip <paramref name="excludeMTransNo"/>.
        /// </summary>
        private async Task<Dictionary<int, decimal>> PackedByOaLine(int subscID, List<int> oaIds, int excludeMTransNo)
        {
            if (oaIds.Count == 0) return new Dictionary<int, decimal>();

            return await (
                from d in db.PackingSlipDtl.AsNoTracking()
                join m in db.PackingSlipMst.AsNoTracking() on d.MTransNo equals m.MTransNo
                where oaIds.Contains(d.OAID)
                   && d.SubscID      == subscID
                   && d.DeleteStatus == "N"
                   && m.DeleteStatus == "N"
                   && d.MTransNo     != excludeMTransNo
                group d by d.OAID into g
                select new { OAID = g.Key, Qty = g.Sum(x => x.Qty) })
                .ToDictionaryAsync(x => x.OAID, x => x.Qty);
        }

        private static string PartLabel(string? code, string? description)
        {
            code = code?.Trim();
            description = description?.Trim();
            if (string.IsNullOrEmpty(code)) return description ?? string.Empty;
            if (string.IsNullOrEmpty(description)) return code;
            return code + " — " + description;
        }
    }
}
