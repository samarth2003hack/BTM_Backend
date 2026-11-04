using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Outward documents kept in RGPMst / RGPDtl / RGPTerms. Two controllers
    /// share these tables, told apart by RGPMst.RGPType:
    ///
    ///   RGPController  api/RGP   RGPType 'RGP'  returnable gate pass
    ///   DCController   api/DC    RGPType 'DC'   delivery challan (non-returnable)
    ///
    /// Each controller only ever sees and writes its own type: lists and
    /// reads are filtered by it, saves force it, and every child-table call
    /// first checks that the header belongs to it.
    ///
    /// Business rules live in the procedures (SQL 28): DocNo allocated on
    /// Insert per series + year + type, vendor snapshot from SupplierMaster,
    /// part snapshot from PartMaster, Amount / ApproxValue recomputed, and an
    /// approved (LockStatus Y) document frozen. LockStatus: N = draft,
    /// S = submitted, Y = approved (Mst/Approve, S -> Y only).
    /// </summary>
    public abstract class RGPOutwardControllerBase : EmsControllerBase
    {
        protected readonly EMSContext db;

        protected RGPOutwardControllerBase(EMSContext context) => db = context;

        /// <summary>The RGPMst.RGPType this controller owns.</summary>
        protected abstract string RGPType { get; }

        /// <summary>User-facing name of the document, for messages.</summary>
        protected abstract string DocLabel { get; }

        private IQueryable<RGPMst> OwnHeaders(int subscID) =>
            db.RGPMst.Where(x => x.SubscID == subscID &&
                                 x.DeleteStatus == "N" &&
                                 x.RGPType == RGPType);

        private Task<bool> OwnsHeader(int mTransNo, int subscID) =>
            OwnHeaders(subscID).AnyAsync(x => x.MTransNo == mTransNo);

        // ============================================================
        // MST — HEADER
        // ============================================================

        /// <summary>
        /// Insert or update the header. DocNo is allocated by the procedure
        /// on Insert (the client's is a preview), the vendor snapshot is read
        /// from SupplierMaster, ApproxValue is rolled up from the lines, and
        /// LockStatus can only be N (draft) or S (submitted) here.
        /// Replies { id } — the header's MTransNo.
        /// </summary>
        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_RGPMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be Insert or Update.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.VendorNo == null || dto.VendorNo <= 0)
                return BadRequestResult("Supplier is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (dto.Status == "Update" && !await OwnsHeader(dto.MTransNo, dto.SubscID.Value))
                    return NotFoundResult($"{DocLabel} not found.");

                dto.RGPType    = RGPType;
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = DateTime.Now;
                dto.ModifiedBy = dto.ModifiedBy is > 0 ? dto.ModifiedBy : dto.CreatedBy;
                dto.ModifiedOn = DateTime.Now;

                var result = await db.CallRGPMstSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Soft-delete the header with its lines and terms. Refused for an
        /// approved document, and for a gate pass that has had material back.
        /// </summary>
        [HttpPost("Mst/DeleteData/{id}")]
        public Task<IActionResult> MstDeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "") =>
            RunHeaderStatus(id, userNo, subscID, "Delete",
                string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason);

        /// <summary>Approve a submitted document (LockStatus S -> Y).</summary>
        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID) =>
            RunHeaderStatus(id, userNo, subscID, "Approve", null);

        /// <summary>
        /// Legacy lock / unlock. Lock goes straight to approved; Unlock returns
        /// the document to draft, unless material has come back against it.
        /// </summary>
        [HttpPost("Mst/LockUnlock/{id}")]
        public Task<IActionResult> MstLockUnlock(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string action = "Lock")
        {
            if (action != "Lock" && action != "Unlock")
                return Task.FromResult(BadRequestResult("Action must be 'Lock' or 'Unlock'."));
            return RunHeaderStatus(id, userNo, subscID, action, null);
        }

        private async Task<IActionResult> RunHeaderStatus(
            int id, int userNo, int subscID, string status, string? reason)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                if (!await OwnsHeader(id, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var result = await db.CallRGPMstSP(new SP_RGPMst
                {
                    MTransNo     = id,
                    RGPType      = RGPType,
                    ModifiedBy   = userNo,
                    ModifiedOn   = DateTime.Now,
                    SubscID      = subscID,
                    DeleteReason = reason,
                    SystemIP     = ClientIp,
                    Status       = status
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Whole header rows (vendor snapshot included), newest first. With
        /// userNo, a draft (LockStatus N) is shown only to the user who
        /// raised it — as on the legacy screens.
        /// </summary>
        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int? userNo = null,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? lockStatus = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = OwnHeaders(subscID).AsNoTracking();

                if (userNo.HasValue && userNo > 0)
                    query = query.Where(x => x.LockStatus != "N" || x.CreatedBy == userNo);
                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(lockStatus))
                    query = query.Where(x => x.LockStatus == lockStatus.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.VendorCode!.Contains(search) ||
                        x.DocNotes!.Contains(search)   ||
                        x.RefNo!.Contains(search));

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
                var data = await OwnHeaders(subscID)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id);

                if (data == null) return NotFoundResult($"{DocLabel} not found.");
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The DocNo the next document in a series + year would get — display
        /// only. The procedure allocates the real number at insert. Deleted
        /// documents count, as numbers are never reused.
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
                var max = await db.RGPMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear &&
                                x.SubscID == subscID && x.RGPType == RGPType)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — LINE ITEMS
        // ============================================================

        /// <summary>
        /// Insert (TransSlNo 0) or update one line. Part code / description /
        /// HSN come from PartMaster and Amount = Qty x Rate, whatever the
        /// client sent. Refused once the header is approved.
        /// </summary>
        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_RGPDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult($"Invalid {DocLabel}.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.Rate == null || dto.Rate <= 0)
                return BadRequestResult("Rate is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (!await OwnsHeader(dto.MTransNo, dto.SubscID.Value))
                    return NotFoundResult($"{DocLabel} not found.");

                dto.Status     = dto.TransSlNo > 0 ? "Update" : "Insert";
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = DateTime.Now;
                dto.ModifiedBy = dto.ModifiedBy is > 0 ? dto.ModifiedBy : dto.CreatedBy;
                dto.ModifiedOn = DateTime.Now;

                var result = await db.CallRGPDtlSP(dto);
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
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var result = await db.CallRGPDtlSP(new SP_RGPDtl
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
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var result = await db.CallRGPDtlSP(new SP_RGPDtl
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
        /// Lines of one header, with the part's name (PartName) and type
        /// (PartType) joined on for the grid.
        /// </summary>
        [HttpGet("Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult($"Invalid {DocLabel}.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var data = await (
                    from d in db.RGPDtl.AsNoTracking()
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    where d.MTransNo == mTransNo && d.SubscID == subscID && d.DeleteStatus == "N"
                    orderby d.SlNo, d.TransSlNo
                    select new
                    {
                        d.TransSlNo,
                        d.SlNo,
                        d.MTransNo,
                        d.PartNo,
                        d.PartCode,
                        d.PartDescription,
                        PartName = d.PartDescription,
                        PartType = p != null ? p.PartType : null,
                        d.ItemRemarks,
                        d.HSNCode,
                        d.Qty,
                        d.Unit,
                        d.Rate,
                        d.Amount,
                        d.GradeNo,
                        d.Grade,
                        d.LockStatus,
                        d.CreatedBy,
                        d.CreatedOn,
                        d.ModifiedBy,
                        d.ModifiedOn
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
                var heads = OwnHeaders(subscID);
                var data = await db.RGPDtl
                    .AsNoTracking()
                    .Where(x => x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Where(x => heads.Any(h => h.MTransNo == x.MTransNo))
                    .FirstOrDefaultAsync();

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // TERMS
        // ============================================================

        [HttpPost("Terms/SaveData")]
        public async Task<IActionResult> TermsSaveData([FromBody] SP_RGPTerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult($"Invalid {DocLabel}.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequestResult("Title is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                if (!await OwnsHeader(dto.MTransNo, dto.SubscID.Value))
                    return NotFoundResult($"{DocLabel} not found.");

                dto.Status     = dto.TransSlNo > 0 ? "Update" : "Insert";
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = DateTime.Now;
                dto.ModifiedBy = dto.ModifiedBy is > 0 ? dto.ModifiedBy : dto.CreatedBy;
                dto.ModifiedOn = DateTime.Now;

                var result = await db.CallRGPTermsSP(dto);
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
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var result = await db.CallRGPTermsSP(new SP_RGPTerms
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
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var result = await db.CallRGPTermsSP(new SP_RGPTerms
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
            if (mTransNo <= 0) return BadRequestResult($"Invalid {DocLabel}.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                if (!await OwnsHeader(mTransNo, subscID))
                    return NotFoundResult($"{DocLabel} not found.");

                var data = await db.RGPTerms
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID  == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .ThenBy(x => x.TransSlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.Title,
                        x.Description,
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

        [HttpGet("Terms/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> TermsGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var heads = OwnHeaders(subscID);
                var data = await db.RGPTerms
                    .AsNoTracking()
                    .Where(x => x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Where(x => heads.Any(h => h.MTransNo == x.MTransNo))
                    .FirstOrDefaultAsync();

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }

    /// <summary>
    /// Returnable Gate Pass: the outward document (RGPType 'RGP', endpoints
    /// inherited from <see cref="RGPOutwardControllerBase"/>) plus the inward
    /// that receives the material back (RGPInwMst / RGPInwDtl, Inw/*).
    ///
    /// HOW THE INWARD SCREEN USES THE ENDPOINTS
    ///   Inw/Source/RGPList             approved gate passes with a balance
    ///   Mst/GetData/{rgp}              the chosen gate pass's supplier
    ///   Inw/Source/RGPLines/{rgp}      its lines with RGPQty and BalQty
    ///   Inw/Mst/SaveData               header (TargetTransNo = the gate pass)
    ///   Inw/Dtl/SaveData               one call per received line (LinkNo)
    ///   Inw/Dtl/GetWithBalance/{inw}   an inward's lines joined onto every
    ///                                  gate pass line still open, for edit
    /// A delivery challan is non-returnable and never appears as a source.
    /// </summary>
    [Route("api/[controller]")]
    public class RGPController : RGPOutwardControllerBase
    {
        public RGPController(EMSContext context) : base(context) { }

        protected override string RGPType => "RGP";
        protected override string DocLabel => "Returnable gate pass";

        // Inward lines that count against a gate pass balance: both the line
        // and its inward header are live.
        private IQueryable<RGPInwDtl> LiveInwardLines() =>
            db.RGPInwDtl.Where(d => d.DeleteStatus == "N" && d.LinkNo != null &&
                                    db.RGPInwMst.Any(m => m.MTransNo == d.MTransNo && m.DeleteStatus == "N"));

        /// <summary>Qty received per gate pass line, optionally ignoring one inward.</summary>
        private async Task<Dictionary<int, decimal>> ReceivedByLine(List<int> linkNos, int excludeInward = 0)
        {
            if (linkNos.Count == 0) return new Dictionary<int, decimal>();

            var rows = await LiveInwardLines()
                .AsNoTracking()
                .Where(d => linkNos.Contains(d.LinkNo!.Value) && d.MTransNo != excludeInward)
                .GroupBy(d => d.LinkNo!.Value)
                .Select(g => new { LinkNo = g.Key, Qty = g.Sum(x => x.Qty) })
                .ToListAsync();

            return rows.ToDictionary(r => r.LinkNo, r => r.Qty);
        }

        // ============================================================
        // INW SOURCE — the gate passes that can be received against
        // ============================================================

        /// <summary>
        /// Approved returnable gate passes with at least one line not yet
        /// fully back. Delivery challans never appear.
        /// </summary>
        [HttpGet("Inw/Source/RGPList/{subscID}")]
        public async Task<IActionResult> InwSourceRGPList(int subscID, [FromQuery] int? vendorNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var live = LiveInwardLines();

                var query = db.RGPMst
                    .AsNoTracking()
                    .Where(m => m.SubscID == subscID && m.DeleteStatus == "N" &&
                                m.RGPType == "RGP" && m.LockStatus == "Y")
                    .Where(m => db.RGPDtl.Any(d =>
                        d.MTransNo == m.MTransNo && d.DeleteStatus == "N" &&
                        d.Qty > (live.Where(i => i.LinkNo == d.TransSlNo).Sum(i => (decimal?)i.Qty) ?? 0)));

                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(m => m.VendorNo == vendorNo);

                var data = await query
                    .OrderByDescending(m => m.DocYear)
                    .ThenByDescending(m => m.DocNo)
                    .Select(m => new
                    {
                        m.MTransNo,
                        m.DocSeries,
                        m.DocYear,
                        m.DocNo,
                        m.DocDate,
                        m.DueDate,
                        m.VendorNo,
                        m.VendorCode,
                        m.VendorName
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One gate pass's lines with what is still out: LinkNo is the line's
        /// TransSlNo, Qty / RGPQty what went out, RecQty what has come back on
        /// live inwards, BalQty the difference. Only lines with a balance.
        /// </summary>
        [HttpGet("Inw/Source/RGPLines/{rgpTransNo}/{subscID}")]
        public async Task<IActionResult> InwSourceRGPLines(int rgpTransNo, int subscID)
        {
            if (rgpTransNo <= 0) return BadRequestResult("Invalid returnable gate pass.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var rgp = await db.RGPMst.AsNoTracking().FirstOrDefaultAsync(m =>
                    m.MTransNo == rgpTransNo && m.SubscID == subscID && m.DeleteStatus == "N");
                if (rgp == null) return NotFoundResult("Returnable gate pass not found.");
                if (rgp.RGPType != "RGP")
                    return ConflictResult("A delivery challan is not returnable; only a returnable gate pass can be received back.");

                var lines = await db.RGPDtl.AsNoTracking()
                    .Where(d => d.MTransNo == rgpTransNo && d.DeleteStatus == "N")
                    .OrderBy(d => d.SlNo).ThenBy(d => d.TransSlNo)
                    .ToListAsync();

                var received = await ReceivedByLine(lines.Select(l => l.TransSlNo).ToList());

                var data = lines
                    .Select(d =>
                    {
                        var rec = received.TryGetValue(d.TransSlNo, out var q) ? q : 0m;
                        return new
                        {
                            LinkNo      = d.TransSlNo,
                            d.SlNo,
                            d.PartNo,
                            d.PartCode,
                            PartName    = d.PartDescription,
                            d.Grade,
                            d.Unit,
                            d.ItemRemarks,
                            d.Qty,
                            RGPQty      = d.Qty,
                            d.Rate,
                            RecQty      = rec,
                            BalQty      = d.Qty - rec
                        };
                    })
                    .Where(r => r.BalQty > 0)
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // INW MST — INWARD HEADER
        // ============================================================

        /// <summary>
        /// Insert or update an inward. TargetTransNo (the gate pass) is
        /// required on Insert and never changes afterwards; the procedure
        /// stamps the gate pass's year / number, the supplier and a fresh
        /// SupplierMaster snapshot, and allocates DocNo on Insert.
        /// Replies { id } — the inward's MTransNo.
        /// </summary>
        [HttpPost("Inw/Mst/SaveData")]
        public async Task<IActionResult> InwMstSaveData([FromBody] SP_RGPInwMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be Insert or Update.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");
            if (dto.Status == "Insert" && (dto.TargetTransNo == null || dto.TargetTransNo <= 0))
                return BadRequestResult("Select the returnable gate pass being received.");
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
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = DateTime.Now;
                dto.ModifiedBy = dto.ModifiedBy is > 0 ? dto.ModifiedBy : dto.CreatedBy;
                dto.ModifiedOn = DateTime.Now;
                dto.TargetType = "RGP";

                var result = await db.CallRGPInwMstSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Soft-delete an inward and its lines; the quantities go back onto the gate pass.</summary>
        [HttpPost("Inw/Mst/DeleteData/{id}")]
        public async Task<IActionResult> InwMstDeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallRGPInwMstSP(new SP_RGPInwMst
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

        [HttpPost("Inw/Mst/LockUnlock/{id}")]
        public async Task<IActionResult> InwMstLockUnlock(
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
                var result = await db.CallRGPInwMstSP(new SP_RGPInwMst
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

        // Header row as the inward screen reads it: the whole snapshot plus
        // the total quantity received on its live lines.
        private IQueryable<object> InwardRows(IQueryable<RGPInwMst> query) =>
            query.Select(x => new
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
                x.TargetType,
                x.VendorNo,
                x.VendorCode,
                x.VendorName,
                x.Address,
                x.GSTIN,
                x.StateName,
                x.PhoneNo,
                x.EmailID,
                x.RefNo,
                x.RefDate,
                x.InwardNo,
                x.InwardDate,
                x.LRNo,
                x.LRDate,
                x.DCNo,
                x.DCDate,
                x.DocNotes,
                x.DocStatus,
                x.LockStatus,
                x.LockCounter,
                TotalQty = db.RGPInwDtl
                    .Where(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
                    .Sum(d => (decimal?)d.Qty) ?? 0,
                x.CreatedBy,
                x.CreatedOn,
                x.ModifiedBy,
                x.ModifiedOn
            });

        [HttpGet("Inw/Mst/GetList/{subscID}")]
        public async Task<IActionResult> InwMstGetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] int? targetTransNo = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.RGPInwMst
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
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search) ||
                        x.VendorCode!.Contains(search) ||
                        x.InwardNo!.Contains(search)   ||
                        x.RefNo!.Contains(search)      ||
                        x.DocNotes!.Contains(search));

                var data = await InwardRows(query
                        .OrderByDescending(x => x.DocYear)
                        .ThenByDescending(x => x.DocNo))
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Inw/Mst/GetData/{id}/{subscID}")]
        public async Task<IActionResult> InwMstGetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await InwardRows(db.RGPInwMst
                        .AsNoTracking()
                        .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N"))
                    .FirstOrDefaultAsync();

                if (data == null) return NotFoundResult("RGP inward not found.");
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The DocNo the next inward in a series + year would get — display
        /// only; SP_RGPInwMst allocates the real one at insert.
        /// </summary>
        [HttpGet("Inw/Mst/GetNextDocNo/{subscID}")]
        public async Task<IActionResult> InwMstGetNextDocNo(
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
                var max = await db.RGPInwMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Inw/Mst/GetByRGP/{targetTransNo}/{subscID}")]
        public async Task<IActionResult> InwMstGetByRGP(int targetTransNo, int subscID)
        {
            if (targetTransNo <= 0) return BadRequestResult("Invalid RGP TransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await InwardRows(db.RGPInwMst
                        .AsNoTracking()
                        .Where(x => x.TargetTransNo == targetTransNo &&
                                    x.SubscID       == subscID       &&
                                    x.DeleteStatus  == "N")
                        .OrderByDescending(x => x.DocYear)
                        .ThenByDescending(x => x.DocNo))
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // INW DTL — INWARD LINES
        // ============================================================

        /// <summary>
        /// Insert (TransSlNo 0) or update one received line. LinkNo names the
        /// gate pass line; part, grade, unit, rate and RGPQty are taken off it
        /// by the procedure, which also refuses a Qty above the balance still
        /// out. Only Qty and ItemRemarks are the client's.
        /// </summary>
        [HttpPost("Inw/Dtl/SaveData")]
        public async Task<IActionResult> InwDtlSaveData([FromBody] SP_RGPInwDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid RGP Inward.");
            if (dto.TransSlNo <= 0 && (dto.LinkNo == null || dto.LinkNo <= 0))
                return BadRequestResult("The gate pass line (LinkNo) is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Inw Qty must be greater than zero.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.Status     = dto.TransSlNo > 0 ? "Update" : "Insert";
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = DateTime.Now;
                dto.ModifiedBy = dto.ModifiedBy is > 0 ? dto.ModifiedBy : dto.CreatedBy;
                dto.ModifiedOn = DateTime.Now;

                var result = await db.CallRGPInwDtlLinkedSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Inw/Dtl/DeleteData/{transSlNo}")]
        public async Task<IActionResult> InwDtlDeleteData(
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
                var result = await db.CallRGPInwDtlLinkedSP(new SP_RGPInwDtl
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

        [HttpPost("Inw/Dtl/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> InwDtlDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallRGPInwDtlLinkedSP(new SP_RGPInwDtl
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

        [HttpGet("Inw/Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> InwDtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid RGP Inward.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.RGPInwDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID  == subscID  &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.SlNo,
                        x.MTransNo,
                        x.LinkNo,
                        x.PartNo,
                        x.PartCode,
                        x.PartDescription,
                        x.ItemRemarks,
                        x.HSNCode,
                        x.RGPQty,
                        x.Qty,
                        x.Unit,
                        x.Rate,
                        x.GradeNo,
                        x.Grade,
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

        /// <summary>
        /// An inward's lines joined onto its gate pass: every gate pass line
        /// that is still open or is on this inward. InwQty / TransSlNo /
        /// ItemRemarks are this inward's own (0 when the line was skipped);
        /// BalQty excludes this inward's own receipt, so re-keying it never
        /// reads as an over-receipt.
        /// </summary>
        [HttpGet("Inw/Dtl/GetWithBalance/{mTransNo}/{subscID}")]
        public async Task<IActionResult> InwDtlGetWithBalance(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid RGP Inward.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var inward = await db.RGPInwMst.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");
                if (inward == null) return NotFoundResult("RGP inward not found.");

                var rgpLines = await db.RGPDtl.AsNoTracking()
                    .Where(d => d.MTransNo == inward.TargetTransNo && d.DeleteStatus == "N")
                    .OrderBy(d => d.SlNo).ThenBy(d => d.TransSlNo)
                    .ToListAsync();

                var own = await db.RGPInwDtl.AsNoTracking()
                    .Where(d => d.MTransNo == mTransNo && d.DeleteStatus == "N")
                    .ToListAsync();

                var received = await ReceivedByLine(rgpLines.Select(l => l.TransSlNo).ToList(), mTransNo);

                var data = rgpLines
                    .Select(d =>
                    {
                        var mine = own.FirstOrDefault(o => o.LinkNo == d.TransSlNo);
                        var rec = received.TryGetValue(d.TransSlNo, out var q) ? q : 0m;
                        return new
                        {
                            TransSlNo   = mine?.TransSlNo ?? 0,
                            LinkNo      = d.TransSlNo,
                            d.PartNo,
                            d.PartCode,
                            PartName    = d.PartDescription,
                            d.Grade,
                            RGPQty      = d.Qty,
                            BalQty      = d.Qty - rec,
                            InwQty      = mine?.Qty ?? 0m,
                            d.Unit,
                            d.Rate,
                            ItemRemarks = mine != null ? mine.ItemRemarks : d.ItemRemarks
                        };
                    })
                    .Where(r => r.BalQty > 0 || r.TransSlNo > 0)
                    .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("Inw/Dtl/GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> InwDtlGetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0) return BadRequestResult("Invalid TransSlNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.RGPInwDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo &&
                        x.SubscID   == subscID   &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
