using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Inward inspection — both screens, because they are two ends of one
    /// process and share a worklist.
    ///
    /// INWARD INSPECTION (PO)   GRNType 'N' -> 'Q'
    ///     Worklist/Pending                 GRNs received but not inspected
    ///     Lines/GetList/{grn}              the receipt lines to judge
    ///     Lines/SaveData                   accepted / rejected / rework /
    ///                                      accepted-on-deviation for one line
    ///     NC/SaveData                      a defect against a line
    ///     Post/{grn}                       allocate the inspection number and
    ///                                      stamp the GRN
    ///     SaveData                         the whole inspection (lines + NC +
    ///                                      post, reopening first on an edit)
    ///                                      in one transaction — GRNQC.html
    ///     Withdraw/{grn}                   reopen + clear lines + drop NC
    ///     InspectionNo, HeatCheck          form helpers for GRNQC.html
    ///
    /// INWARD INSPECTION (CLOSURE)   GRNType 'Q' -> 'C'
    ///     Worklist/Closure                 inspected GRNs carrying
    ///                                      non-conformance
    ///     Closure/Close/{grn}              remarks and evidence, then closed
    ///
    /// The rules live in the procedures, not here, so they hold however the
    /// endpoint is called:
    ///   * a line's Acc + Rej + Rew + AOD must equal its RecQty;
    ///   * posting needs every non-zero Rej / Rew / AOD quantity backed by
    ///     non-conformance lines of that type whose NCQty adds up to it;
    ///   * closure needs an inspected GRN that actually has non-conformance,
    ///     and remarks.
    /// Anything refused comes back as a 409 with the procedure's own wording.
    /// </summary>
    [Route("api/[controller]")]
    public class InwardInspectionController : EmsControllerBase
    {
        private readonly EMSContext db;

        public InwardInspectionController(EMSContext context) => db = context;

        // ============================================================
        // WORKLISTS
        // ============================================================

        /// <summary>
        /// GRNs waiting to be inspected. Confirmed receipts only by default —
        /// an unconfirmed GRN is still being keyed — pass includeDraft=true to
        /// see those too.
        /// </summary>
        [HttpGet("Worklist/Pending/{subscID}")]
        public async Task<IActionResult> WorklistPending(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] bool includeDraft = false,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.GRNType == "N" && x.LineCount > 0);

                if (!includeDraft)
                    query = query.Where(x => x.LockStatus == "Y");
                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.VendorName!.Contains(search) || x.RefNo!.Contains(search));

                var data = await query
                    .OrderBy(x => x.DocDate).ThenBy(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "Nothing is waiting for inspection.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>GRNs already inspected — what the inspection screen shows
        /// when you want to look back at a posted one.</summary>
        [HttpGet("Worklist/Inspected/{subscID}")]
        public async Task<IActionResult> WorklistInspected(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && (x.GRNType == "Q" || x.GRNType == "C"));

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);

                var data = await query
                    .OrderByDescending(x => x.QCDate).ThenByDescending(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The closure screen's worklist: inspected GRNs that carry
        /// non-conformance and have not been closed. Pass includeClosed=true
        /// to review ones already dealt with.
        /// </summary>
        [HttpGet("Worklist/Closure/{subscID}")]
        public async Task<IActionResult> WorklistClosure(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] bool includeClosed = false)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.NCLines > 0);

                query = includeClosed
                    ? query.Where(x => x.GRNType == "Q" || x.GRNType == "C")
                    : query.Where(x => x.GRNType == "Q");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);

                var data = await query
                    .OrderBy(x => x.GRNType).ThenBy(x => x.QCDate).ThenBy(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "Nothing is waiting for closure.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One GRN's VW_GRNInwardStatus row — the header the
        /// inspection screen shows in View / Edit, whatever its stage.</summary>
        [HttpGet("GetData/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetData(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var status = await db.GRNInwardStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID);

                if (status == null) return NotFoundResult("GRN not found.");
                return OkResult(status, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // INSPECTION LINES
        // ============================================================

        /// <summary>
        /// The receipt lines of one GRN as the inspector sees them: the
        /// quantities, the verdict so far, and how much non-conformance has
        /// been detailed against each type — which is what posting will check.
        /// </summary>
        [HttpGet("Lines/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> LinesGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var lines = await db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.PartNo, x.PartCode, x.PartDescription,
                        x.GradeNo, x.Grade, x.Unit, x.HSNCode,
                        x.BillQty, x.RecQty, x.AccQty, x.RejQty, x.RewQty, x.AODQty,
                        x.InspRemarks, x.RejReason, x.Store, x.Location, x.ItemRemarks,
                        x.OAYear, x.OANo, x.OAID,
                        // PartMaster.MfrLicNo = 'Y' is the legacy "heat no
                        // required" flag; SP_GRNNCDtl enforces the same one.
                        MfrLicNo = db.PartMaster
                            .Where(p => p.MTransNo == x.PartNo)
                            .Select(p => p.MfrLicNo)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                if (lines.Count == 0)
                    return OkResult(lines, "No records found.");

                // The NC detailed per line and type, so the screen can show
                // "5 rejected, 3 explained" before the inspector tries to post.
                var ids = lines.Select(l => l.TransSlNo).ToList();

                var nc = await db.GRNNCDtl
                    .AsNoTracking()
                    .Where(x => x.GRNTransSlNo != null && ids.Contains(x.GRNTransSlNo.Value)
                             && x.SubscID == subscID && x.DeleteStatus == "N")
                    .GroupBy(x => new { x.GRNTransSlNo, x.NCType })
                    .Select(g => new { g.Key.GRNTransSlNo, g.Key.NCType, Qty = g.Sum(x => x.NCQty) })
                    .ToListAsync();

                var data = lines.Select(l => new
                {
                    l.TransSlNo, l.SlNo, l.PartNo, l.PartCode, l.PartDescription,
                    l.GradeNo, l.Grade, l.Unit, l.HSNCode,
                    l.BillQty, l.RecQty, l.AccQty, l.RejQty, l.RewQty, l.AODQty,
                    l.InspRemarks, l.RejReason, l.Store, l.Location, l.ItemRemarks,
                    l.OAYear, l.OANo, l.OAID,
                    HeatYN = (l.MfrLicNo ?? "").Trim().ToUpper() == "Y" ? "Y" : "N",
                    NCRejectQty = nc.Where(n => n.GRNTransSlNo == l.TransSlNo && n.NCType == "Reject").Sum(n => n.Qty),
                    NCReworkQty = nc.Where(n => n.GRNTransSlNo == l.TransSlNo && n.NCType == "Rework").Sum(n => n.Qty),
                    NCAODQty    = nc.Where(n => n.GRNTransSlNo == l.TransSlNo && n.NCType == "AOD").Sum(n => n.Qty),
                    Inspected   = l.AccQty + l.RejQty + l.RewQty + l.AODQty == l.RecQty && l.RecQty > 0
                }).ToList();

                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Record the verdict on one receipt line. The four quantities must
        /// add up to what was received; the procedure refuses otherwise.
        /// </summary>
        [HttpPost("Lines/SaveData")]
        public async Task<IActionResult> LinesSaveData([FromBody] SP_GRNInspDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.TransSlNo <= 0)
                return BadRequestResult("Invalid receipt line.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid GRN.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.ModifiedBy == null || dto.ModifiedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.Status != "Save" && dto.Status != "Clear")
                return BadRequestResult("Status must be 'Save' or 'Clear'.");

            try
            {
                dto.SystemIP   = ClientIp;
                dto.ModifiedOn = DateTime.Now;
                var result = await db.CallGRNInspDtlSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // NON-CONFORMANCE
        // ============================================================

        [HttpPost("NC/SaveData")]
        public async Task<IActionResult> NCSaveData([FromBody] SP_GRNNCDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.GRNTransSlNo <= 0)
                return BadRequestResult("Invalid receipt line.");
            if (string.IsNullOrWhiteSpace(dto.NCType))
                return BadRequestResult("NC Type is required.");
            if (dto.NCType != "Reject" && dto.NCType != "Rework" && dto.NCType != "AOD")
                return BadRequestResult("NC Type must be Reject, Rework or AOD.");
            if (dto.NCQty == null || dto.NCQty <= 0)
                return BadRequestResult("NC quantity is required.");
            if (string.IsNullOrWhiteSpace(dto.NCRemarks))
                return BadRequestResult("Describe the non-conformance.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallGRNNCDtlSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("NC/DeleteData/{transSlNo}")]
        public async Task<IActionResult> NCDeleteData(
            int transSlNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (transSlNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNNCDtlSP(new SP_GRNNCDtl
                {
                    TransSlNo    = transSlNo,
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

        /// <summary>Clears every non-conformance line on the whole GRN.
        /// Note that mTransNo here is the GRN, not a receipt line.</summary>
        [HttpPost("NC/DeleteAll/{mTransNo}")]
        public async Task<IActionResult> NCDeleteAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNNCDtlSP(new SP_GRNNCDtl
                {
                    GRNTransSlNo = mTransNo,   // the DeleteAll branch reads the GRN here
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

        /// <summary>Non-conformance on one receipt line, optionally of one type.</summary>
        [HttpGet("NC/GetList/{grnTransSlNo}/{subscID}")]
        public async Task<IActionResult> NCGetList(
            int grnTransSlNo,
            int subscID,
            [FromQuery] string? ncType = null)
        {
            if (grnTransSlNo <= 0) return BadRequestResult("Invalid receipt line.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNNCDtl
                    .AsNoTracking()
                    .Where(x => x.GRNTransSlNo == grnTransSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(ncType))
                    query = query.Where(x => x.NCType == ncType.Trim());

                var data = await query.OrderBy(x => x.SlNo).ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Every non-conformance on a GRN — the closure screen's list.</summary>
        [HttpGet("NC/GetByGRN/{mTransNo}/{subscID}")]
        public async Task<IActionResult> NCGetByGRN(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await (
                    from n in db.GRNNCDtl.AsNoTracking()
                    join d in db.GRNDtl.AsNoTracking() on n.GRNTransSlNo equals d.TransSlNo into dj
                    from d in dj.DefaultIfEmpty()
                    where n.MTransNo == mTransNo && n.SubscID == subscID && n.DeleteStatus == "N"
                    orderby n.SlNo
                    select new
                    {
                        n.TransSlNo, n.SlNo, n.MTransNo, n.GRNTransSlNo,
                        GRNLineNo = d.SlNo,
                        n.PartNo, PartCode = d.PartCode, PartDescription = d.PartDescription,
                        n.Grade, n.NCType, n.HeatNo, n.NCRemarks, n.NCQty, n.Unit,
                        n.CreatedBy, n.CreatedOn, n.ModifiedBy, n.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No non-conformance on this GRN.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // POST / REOPEN THE INSPECTION
        // ============================================================

        /// <summary>
        /// Post the inspection: allocates the inspection number, stamps the QC
        /// date and verifier, and moves the GRN to 'Q'.
        ///
        /// This is where the whole document is checked — every line inspected,
        /// and every non-conforming quantity backed by NC lines that add up to
        /// it. A refusal names the line and the shortfall.
        /// </summary>
        [HttpPost("Post/{mTransNo}")]
        public async Task<IActionResult> Post(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string qcVerifyBy = "",
            [FromQuery] DateTime? qcDate = null)
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (string.IsNullOrWhiteSpace(qcVerifyBy))
                return BadRequestResult("Who verified the inspection?");

            try
            {
                var result = await db.CallGRNInspPostSP(new SP_GRNInspPost
                {
                    MTransNo   = mTransNo,
                    QCDate     = AsDate(qcDate),
                    QCverifyBy = qcVerifyBy,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "Post"
                });
                if (result.ID == 0) return ConflictResult(result.Message);

                var status = await db.GRNInwardStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID);

                return OkResult(status, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Take an inspected GRN back to 'N' so the verdict can be redone.
        /// Refused once the non-conformance has been closed — reopen the
        /// closure first.
        /// </summary>
        [HttpPost("Reopen/{mTransNo}")]
        public async Task<IActionResult> Reopen(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID)
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNInspPostSP(new SP_GRNInspPost
                {
                    MTransNo   = mTransNo,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "Reopen"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // INSPECTION SCREEN SUPPORT (GRNQC.html)
        // ============================================================

        /// <summary>
        /// The Inspection No the form shows: the GRN's own number when it has
        /// one (inspected before, or reopened), otherwise the next number in
        /// its series. Only a preview — SP_GRNInspPost allocates the real one
        /// when the inspection is posted.
        /// </summary>
        [HttpGet("InspectionNo/{mTransNo}/{subscID}")]
        public async Task<IActionResult> InspectionNo(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var grn = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => new { x.DocSeries, x.DocYear, x.InspNo })
                    .FirstOrDefaultAsync();

                if (grn == null) return NotFoundResult("GRN not found.");

                if (!string.IsNullOrWhiteSpace(grn.InspNo))
                    return OkResult(grn.InspNo.Trim(), "Inspection number on file.");

                var taken = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == grn.DocSeries && x.DocYear == grn.DocYear
                             && x.SubscID == subscID && x.DeleteStatus == "N" && x.InspNo != null)
                    .Select(x => x.InspNo!)
                    .ToListAsync();

                var next = taken
                    .Select(s => int.TryParse(s.Trim(), out var n) ? n : 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1;

                return OkResult(next.ToString(), "Next inspection number; it is allocated when the inspection is posted.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Was this heat number received on this GRN for the part and grade?
        /// The legacy CheckHeatNo — the NC pop-up asks before it accepts a row
        /// on a heat-traced part. SP_GRNNCDtl applies the same rule on save.
        /// (api/HeatNo/Dtl/CheckHeatNo answers a different question: whether
        /// the number is already on file for the part anywhere.)
        /// </summary>
        [HttpGet("HeatCheck/{mTransNo}/{subscID}")]
        public async Task<IActionResult> HeatCheck(
            int mTransNo,
            int subscID,
            [FromQuery] int partNo = 0,
            [FromQuery] string grade = "",
            [FromQuery] string heatNo = "")
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (partNo <= 0) return BadRequestResult("Part No is required.");
            if (string.IsNullOrWhiteSpace(heatNo)) return BadRequestResult("Heat No is required.");

            try
            {
                var heat = heatNo.Trim();
                var g = (grade ?? "").Trim();

                var found = await (
                    from d in db.HeatNoDtl.AsNoTracking()
                    join m in db.HeatNoMst.AsNoTracking() on d.MTransNo equals m.MTransNo
                    where m.TargetTransNo == mTransNo && m.SubscID == subscID
                       && m.DeleteStatus == "N" && d.DeleteStatus == "N"
                       && d.PartNo == partNo && d.HeatNo == heat
                       && (d.Grade ?? "").Trim() == g
                    select d.TransSlNo)
                    .AnyAsync();

                return OkResult(
                    new { Found = found, HeatNo = heat },
                    found
                        ? "Heat number received on this GRN."
                        : $"Heat number {heat} was not received on this GRN for this part.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Submit a whole inspection in one transaction: reopen (when the GRN
        /// is already inspected — the Edit case), NC deletes, NC updates and
        /// inserts, every line's verdict, then Post. The procedures keep every
        /// rule; any refusal rolls the lot back and comes back as a 409 with
        /// the procedure's wording, leaving the GRN exactly as it was.
        /// Returns the refreshed VW_GRNInwardStatus row.
        /// </summary>
        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] InwardInspectionSave dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            if (string.IsNullOrWhiteSpace(dto.QCverifyBy))
                return BadRequestResult("Who verified the inspection?");

            dto.Lines ??= new();
            dto.NC ??= new();
            dto.DeletedNC ??= new();

            if (dto.Lines.Count == 0)
                return BadRequestResult("Send the verdict for the receipt lines.");

            foreach (var n in dto.NC)
            {
                if (n.NCType != "Reject" && n.NCType != "Rework" && n.NCType != "AOD")
                    return BadRequestResult("NC Type must be Reject, Rework or AOD.");
                if (n.NCQty <= 0)
                    return BadRequestResult("NC quantity is required.");
                if (string.IsNullOrWhiteSpace(n.NCRemarks))
                    return BadRequestResult("Describe the non-conformance.");
            }

            try
            {
                var grnType = await db.GRNMst
                    .AsNoTracking()
                    .Where(x => x.MTransNo == dto.MTransNo && x.SubscID == dto.SubscID && x.DeleteStatus == "N")
                    .Select(x => x.GRNType)
                    .FirstOrDefaultAsync();

                if (grnType == null) return NotFoundResult("GRN not found.");

                // Everything addressed must belong to this GRN.
                var lineIds = await db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == dto.MTransNo && x.SubscID == dto.SubscID && x.DeleteStatus == "N")
                    .Select(x => x.TransSlNo)
                    .ToListAsync();

                if (dto.Lines.Any(l => !lineIds.Contains(l.TransSlNo)) ||
                    dto.NC.Any(n => !lineIds.Contains(n.GRNTransSlNo)))
                    return BadRequestResult("A receipt line sent is not on this GRN.");

                var ncIds = dto.DeletedNC
                    .Concat(dto.NC.Where(n => n.TransSlNo > 0).Select(n => n.TransSlNo))
                    .Distinct()
                    .ToList();

                if (ncIds.Count > 0)
                {
                    var own = await db.GRNNCDtl
                        .AsNoTracking()
                        .CountAsync(x => ncIds.Contains(x.TransSlNo) && x.MTransNo == dto.MTransNo
                                      && x.SubscID == dto.SubscID && x.DeleteStatus == "N");
                    if (own != ncIds.Count)
                        return BadRequestResult("A non-conformance row sent is not on this GRN, or has already been removed.");
                }

                var now = DateTime.Now;
                var ip  = ClientIp;

                await using var tx = await db.Database.BeginTransactionAsync();
                (int ID, string Message) r;

                // Edit: take the posted inspection back to 'N' first. A closed
                // GRN is refused here with the procedure's own wording.
                if (grnType == "Q" || grnType == "C")
                {
                    r = await db.CallGRNInspPostSP(new SP_GRNInspPost
                    {
                        MTransNo = dto.MTransNo, ModifiedBy = dto.UserNo, ModifiedOn = now,
                        SubscID = dto.SubscID, SystemIP = ip, Status = "Reopen"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                foreach (var id in dto.DeletedNC.Distinct())
                {
                    r = await db.CallGRNNCDtlSP(new SP_GRNNCDtl
                    {
                        TransSlNo = id, ModifiedBy = dto.UserNo, ModifiedOn = now,
                        SubscID = dto.SubscID, SystemIP = ip,
                        DeleteReason = "Removed on inward inspection", Status = "Delete"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                // Updates before inserts, so a lowered quantity frees room on
                // the line before a new row claims it.
                foreach (var n in dto.NC.OrderByDescending(n => n.TransSlNo > 0))
                {
                    var update = n.TransSlNo > 0;
                    r = await db.CallGRNNCDtlSP(new SP_GRNNCDtl
                    {
                        TransSlNo    = n.TransSlNo,
                        GRNTransSlNo = n.GRNTransSlNo,
                        NCType       = n.NCType,
                        HeatNo       = n.HeatNo,
                        NCRemarks    = n.NCRemarks,
                        NCQty        = n.NCQty,
                        CreatedBy    = dto.UserNo,
                        CreatedOn    = update ? null : now,
                        ModifiedBy   = dto.UserNo,
                        ModifiedOn   = now,
                        SubscID      = dto.SubscID,
                        SystemIP     = ip,
                        Status       = update ? "Update" : "Insert"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                foreach (var l in dto.Lines)
                {
                    r = await db.CallGRNInspDtlSP(new SP_GRNInspDtl
                    {
                        TransSlNo   = l.TransSlNo,
                        MTransNo    = dto.MTransNo,
                        AccQty      = l.AccQty,
                        RejQty      = l.RejQty,
                        RewQty      = l.RewQty,
                        AODQty      = l.AODQty,
                        InspRemarks = l.InspRemarks,
                        ModifiedBy  = dto.UserNo,
                        ModifiedOn  = now,
                        SubscID     = dto.SubscID,
                        SystemIP    = ip,
                        Status      = "Save"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                r = await db.CallGRNInspPostSP(new SP_GRNInspPost
                {
                    MTransNo   = dto.MTransNo,
                    QCDate     = AsDate(dto.QCDate),
                    QCverifyBy = dto.QCverifyBy,
                    ModifiedBy = dto.UserNo,
                    ModifiedOn = now,
                    SubscID    = dto.SubscID,
                    SystemIP   = ip,
                    Status     = "Post"
                });
                if (r.ID == 0) return await Refuse(tx, r.Message);

                await tx.CommitAsync();

                var status = await db.GRNInwardStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == dto.MTransNo && x.SubscID == dto.SubscID);

                return OkResult(status, r.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Withdraw a posted inspection — the inspection screen's Delete. In
        /// one transaction: reopen to 'N', clear every line's verdict, and
        /// remove the non-conformance rows. The GRN itself stays and goes back
        /// on the pending list (keeping its Inspection No for the re-post).
        /// Refused once the non-conformance has been closed.
        /// </summary>
        [HttpPost("Withdraw/{mTransNo}")]
        public async Task<IActionResult> Withdraw(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var lineIds = await db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => x.TransSlNo)
                    .ToListAsync();

                var hasNc = await db.GRNNCDtl
                    .AsNoTracking()
                    .AnyAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");

                var now = DateTime.Now;
                var ip  = ClientIp;
                var why = string.IsNullOrWhiteSpace(reason) ? "Inspection withdrawn" : reason.Trim();

                await using var tx = await db.Database.BeginTransactionAsync();

                var r = await db.CallGRNInspPostSP(new SP_GRNInspPost
                {
                    MTransNo = mTransNo, ModifiedBy = userNo, ModifiedOn = now,
                    SubscID = subscID, SystemIP = ip, Status = "Reopen"
                });
                if (r.ID == 0) return await Refuse(tx, r.Message);

                foreach (var id in lineIds)
                {
                    r = await db.CallGRNInspDtlSP(new SP_GRNInspDtl
                    {
                        TransSlNo = id, MTransNo = mTransNo, ModifiedBy = userNo, ModifiedOn = now,
                        SubscID = subscID, SystemIP = ip, Status = "Clear"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                if (hasNc)
                {
                    r = await db.CallGRNNCDtlSP(new SP_GRNNCDtl
                    {
                        GRNTransSlNo = mTransNo,   // the DeleteAll branch reads the GRN here
                        ModifiedBy = userNo, ModifiedOn = now, SubscID = subscID,
                        SystemIP = ip, DeleteReason = why, Status = "DeleteAll"
                    });
                    if (r.ID == 0) return await Refuse(tx, r.Message);
                }

                await tx.CommitAsync();
                return OkResult(new { ID = mTransNo }, "Inspection withdrawn. The GRN is back on the pending list.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Roll back and answer 409 with the procedure's wording.</summary>
        private async Task<IActionResult> Refuse(IDbContextTransaction tx, string message)
        {
            try { await tx.RollbackAsync(); }
            catch { /* already rolled back by the procedure's CATCH */ }
            return ConflictResult(message);
        }

        /// <summary>
        /// A calendar date for a SQL DATE parameter. A zoned timestamp (the
        /// "…T18:30:00Z" a browser writes for local midnight in India) is read
        /// in server-local time first, so it cannot land on the previous day.
        /// </summary>
        private static DateTime? AsDate(DateTime? value)
        {
            if (value == null) return null;
            var v = value.Value;
            return (v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v).Date;
        }

        // ============================================================
        // CLOSURE
        // ============================================================

        /// <summary>
        /// Close the non-conformance, with remarks and an optional piece of
        /// evidence. Send it as multipart/form-data when there is a file;
        /// the file is written under wwwroot/Uploads/GRNNC/{mTransNo}/ and
        /// only its path is stored.
        /// </summary>
        /// <remarks>
        /// <c>reclose=true</c> replaces the closure on a GRN that is already
        /// closed (the closure screen's "Update Closure"): Closure/Reopen and
        /// Close run in one transaction, so a refused close leaves the old
        /// closure in place rather than a half-reopened GRN.
        /// </remarks>
        [HttpPost("Closure/Close/{mTransNo}")]
        [RequestSizeLimit(30_000_000)]
        public async Task<IActionResult> ClosureClose(
            int mTransNo,
            [FromForm] int userNo,
            [FromForm] int subscID,
            [FromForm] string remarks = "",
            [FromForm] DateTime? closeDate = null,
            [FromForm] bool reclose = false,
            [FromForm] IFormFile? file = null)
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (string.IsNullOrWhiteSpace(remarks))
                return BadRequestResult("Closure remarks are required.");

            GrnUploads.Stored? stored = null;

            try
            {
                if (file != null && file.Length > 0)
                {
                    stored = await GrnUploads.SaveAsync(HttpContext, "GRNNC", mTransNo, file);
                    if (stored.Error != null) return BadRequestResult(stored.Error);
                }

                await using var tx = await db.Database.BeginTransactionAsync();

                // The document the closure will carry: the new upload, or —
                // on an "Update Closure" without one — the document already
                // on file, which Reopen would otherwise clear.
                var attachment = stored?.RelativePath;

                if (reclose)
                {
                    var current = await db.GRNMst.AsNoTracking()
                        .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                        .Select(x => new { x.GRNType, x.NCCloseAttachment })
                        .FirstOrDefaultAsync();

                    if (current?.GRNType == "C")
                    {
                        if (attachment == null && !string.IsNullOrWhiteSpace(current.NCCloseAttachment))
                            attachment = current.NCCloseAttachment.Trim();

                        var reopened = await db.CallGRNNCCloseSP(new SP_GRNNCClose
                        {
                            MTransNo   = mTransNo,
                            ModifiedBy = userNo,
                            ModifiedOn = DateTime.Now,
                            SubscID    = subscID,
                            SystemIP   = ClientIp,
                            Status     = "Reopen"
                        });
                        if (reopened.ID == 0)
                        {
                            GrnUploads.TryDelete(stored?.FullPath);
                            return await Refuse(tx, reopened.Message);
                        }
                    }
                }

                var result = await db.CallGRNNCCloseSP(new SP_GRNNCClose
                {
                    MTransNo          = mTransNo,
                    NCCloseDate       = AsDate(closeDate),
                    NCRemarks         = remarks,
                    NCCloseAttachment = attachment,
                    ModifiedBy        = userNo,
                    ModifiedOn        = DateTime.Now,
                    SubscID           = subscID,
                    SystemIP          = ClientIp,
                    Status            = "Close"
                });

                if (result.ID == 0)
                {
                    GrnUploads.TryDelete(stored?.FullPath);
                    return await Refuse(tx, result.Message);
                }

                await tx.CommitAsync();

                var status = await db.GRNInwardStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID);

                return OkResult(status, result.Message);
            }
            catch (Exception ex)
            {
                GrnUploads.TryDelete(stored?.FullPath);
                return ErrorResult(ex);
            }
        }

        [HttpPost("Closure/Reopen/{mTransNo}")]
        public async Task<IActionResult> ClosureReopen(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID)
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNNCCloseSP(new SP_GRNNCClose
                {
                    MTransNo   = mTransNo,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "Reopen"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The closure screen's read: the GRN's stage, its
        /// non-conformance and the closure block, in one call.</summary>
        [HttpGet("Closure/GetData/{mTransNo}/{subscID}")]
        public async Task<IActionResult> ClosureGetData(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var status = await db.GRNInwardStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID);

                if (status == null) return NotFoundResult();

                // Joined to the receipt line so the closure screen can name
                // the part without a second call.
                var nc = await (
                    from n in db.GRNNCDtl.AsNoTracking()
                    join d in db.GRNDtl.AsNoTracking() on n.GRNTransSlNo equals d.TransSlNo into dj
                    from d in dj.DefaultIfEmpty()
                    where n.MTransNo == mTransNo && n.SubscID == subscID && n.DeleteStatus == "N"
                    orderby n.SlNo
                    select new
                    {
                        n.TransSlNo, n.SlNo, n.MTransNo, n.GRNTransSlNo,
                        GRNLineNo = d.SlNo,
                        n.PartNo, PartCode = d.PartCode, PartDescription = d.PartDescription,
                        n.GradeNo, n.Grade, n.NCType, n.HeatNo, n.NCRemarks, n.NCQty, n.Unit,
                        n.CreatedBy, n.CreatedOn, n.ModifiedBy, n.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(new { Status = status, NonConformance = nc });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
