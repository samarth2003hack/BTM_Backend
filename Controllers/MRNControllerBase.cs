using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Material Requisition Note — shared by the two MRN screens, which differ
    /// only in MRNType:
    ///
    ///     MRNController    api/MRN    'PROD'  production MRN — raw material
    ///                                         for a part being produced (the
    ///                                         Conv* columns name that part)
    ///     MRNGController   api/MRNG   'GEN'   general / extra MRN — any item
    ///
    ///     GET  Mst/GetList/{subscID}?userNo=        the register
    ///     GET  Mst/GetData/{id}/{subscID}           one header
    ///     GET  Mst/GetNextDocNo/{subscID}?docSeries=&amp;docYear=   preview only
    ///     POST Mst/SaveData                         header insert / update
    ///     POST Mst/Approve/{id}?userNo=&amp;subscID=    S -> Y
    ///     POST Mst/Unlock/{id}?userNo=&amp;subscID=     Y -> S (nothing issued yet)
    ///     POST Mst/DeleteData/{id}?userNo=&amp;subscID=&amp;reason=   soft delete
    ///     GET  Dtl/GetList/{mTransNo}/{subscID}     the lines
    ///     POST Dtl/SaveData                         one line insert / update
    ///     POST Dtl/DeleteData/{transSlNo}?userNo=&amp;subscID=
    ///
    /// Lookups come from existing endpoints: OA cascade api/OA/Lookup/*,
    /// parts api/PartMaster/GetDropdown, grades api/MaterialGrade/GetDropdown.
    ///
    /// LockStatus: N = executive's draft, S = submitted, Y = approved. Store
    /// Issue reads approved MRNs and MRNDtl.BalQty (kept equal to Qty here).
    /// Once anything has been issued against an MRN it can no longer be
    /// edited, unlocked or deleted. SP_MRNMst allocates the DocNo (SQL 44);
    /// status changes go through SP_MRNStatus (SQL 45).
    /// </summary>
    public abstract class MRNControllerBase : EmsControllerBase
    {
        protected readonly EMSContext db;

        protected MRNControllerBase(EMSContext context) => db = context;

        /// <summary>'PROD' or 'GEN'.</summary>
        protected abstract string MRNType { get; }

        private bool IsProduction => MRNType == "PROD";

        private const string Executive = "EXECUTIVE";

        private async Task<string?> UserTypeOf(int userNo) =>
            (await db.UserMaster.AsNoTracking()
                .Where(u => u.MTransNo == userNo)
                .Select(u => u.UserType)
                .FirstOrDefaultAsync())?.Trim().ToUpperInvariant();

        private IQueryable<MRNMst> LiveHeaders(int subscID) =>
            db.MRNMst.AsNoTracking().Where(m =>
                m.MRNType == MRNType && m.SubscID == subscID && m.DeleteStatus == "N");

        /// <summary>"-" and blank both mean the line is ungraded.</summary>
        private static string? CleanGrade(string? grade)
        {
            var g = (grade ?? "").Trim();
            return g == "" || g == "-" ? null : g;
        }

        // ============================================================
        // MST — HEADER
        // ============================================================

        /// <summary>
        /// The register, scoped as the legacy screen was: an executive sees
        /// their own MRNs plus approved ones; anyone else also sees every
        /// submitted MRN (they are the approvers). Other people's drafts stay
        /// private.
        /// </summary>
        [HttpGet("Mst/GetList/{subscID}")]
        public async Task<IActionResult> MstGetList(
            int subscID,
            [FromQuery] int userNo = 0,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (userNo <= 0) return BadRequestResult("Invalid User.");

            try
            {
                var userType = await UserTypeOf(userNo) ?? "";
                var isExec = userType == Executive;

                var query = LiveHeaders(subscID).Where(m =>
                    m.CreatedBy == userNo || m.LockStatus == "Y" || (!isExec && m.LockStatus == "S"));

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(m => m.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(m => m.DocSeries == docSeries.Trim());

                var data = await query
                    .OrderByDescending(m => m.MTransNo)
                    .Select(m => new
                    {
                        m.MTransNo,
                        DocSeries = m.DocSeries.Trim(),
                        m.DocYear,
                        m.DocNo,
                        m.DocDate,
                        RequiredBy = m.Requiredby,
                        m.Department,
                        m.DocNotes,
                        m.LockStatus,
                        m.CreatedBy,
                        m.CreatedOn,
                        m.ModifiedBy,
                        m.ModifiedOn,
                        CreatedByName = db.UserMaster.Where(u => u.MTransNo == m.CreatedBy)
                                                     .Select(u => u.UserName).FirstOrDefault(),
                        LineCount = db.MRNDtl.Count(d => d.MTransNo == m.MTransNo && d.DeleteStatus == "N")
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
                var data = await LiveHeaders(subscID)
                    .Where(m => m.MTransNo == id)
                    .Select(m => new
                    {
                        m.MTransNo,
                        DocSeries = m.DocSeries.Trim(),
                        m.DocYear,
                        m.DocNo,
                        m.DocDate,
                        m.MRNType,
                        RequiredBy = m.Requiredby,
                        m.Department,
                        m.DocNotes,
                        m.LockStatus,
                        m.CreatedBy,
                        m.CreatedOn,
                        m.ModifiedBy,
                        m.ModifiedOn,
                        CreatedByName = db.UserMaster.Where(u => u.MTransNo == m.CreatedBy)
                                                     .Select(u => u.UserName).FirstOrDefault(),
                        ModifiedByName = db.UserMaster.Where(u => u.MTransNo == m.ModifiedBy)
                                                      .Select(u => u.UserName).FirstOrDefault()
                    })
                    .FirstOrDefaultAsync();

                if (data == null) return NotFoundResult("MRN not found.");

                var issued = await db.MRNIssueCount(id);
                return OkResult(new
                {
                    data.MTransNo, data.DocSeries, data.DocYear, data.DocNo, data.DocDate,
                    data.MRNType, data.RequiredBy, data.Department, data.DocNotes,
                    data.LockStatus, data.CreatedBy, data.CreatedOn, data.ModifiedBy,
                    data.ModifiedOn, data.CreatedByName, data.ModifiedByName,
                    IssueCount = issued
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The number the next MRN in a series + year would get — display
        /// only; SP_MRNMst allocates the real one at insert with the same
        /// rule (live rows of this MRNType, MAX + 1).
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
                var max = await LiveHeaders(subscID)
                    .Where(m => m.DocSeries == series && m.DocYear == docYear)
                    .MaxAsync(m => (int?)m.DocNo) ?? 0;
                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] MRNMstSaveRequest dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");

            var series = (dto.DocSeries ?? "").Trim();
            if (series == "") return BadRequestResult("Doc Series is required.");
            if (series.Length > 10) return BadRequestResult("Doc Series is too long.");
            var docYear = dto.DocYear > 0 ? dto.DocYear
                        : int.TryParse(series.Length > 3 ? series[3..].Trim() : "", out var y) ? y : 0;
            if (docYear <= 0) return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null) return BadRequestResult("MRN Date is required.");
            if (string.IsNullOrWhiteSpace(dto.RequiredBy)) return BadRequestResult("Required By is required.");
            if (string.IsNullOrWhiteSpace(dto.Department)) return BadRequestResult("Department is required.");

            try
            {
                var userType = await UserTypeOf(dto.UserNo);
                if (userType == null) return BadRequestResult("Invalid User.");
                var isExec = userType == Executive;

                MRNMst? existing = null;
                if (dto.MTransNo > 0)
                {
                    existing = await LiveHeaders(dto.SubscID).FirstOrDefaultAsync(m => m.MTransNo == dto.MTransNo);
                    if (existing == null) return NotFoundResult("MRN not found, or it has been deleted.");

                    if (await db.MRNIssueCount(existing.MTransNo) > 0)
                        return ConflictResult("Material has already been issued against this MRN - it cannot be changed.");
                    if (existing.LockStatus == "Y" && isExec)
                        return ConflictResult("An executive cannot change an approved MRN.");
                    if (existing.LockStatus != "Y" && existing.CreatedBy != dto.UserNo && userType != "ADMIN")
                        return ConflictResult("Only the creator can change an MRN that is not approved.");
                }

                // N only for an executive's draft; Y only to keep an approved
                // MRN approved when a non-executive edits it. Everything else
                // is submitted for approval.
                var requested = (dto.LockStatus ?? "S").Trim().ToUpperInvariant();
                var lockStatus = requested switch
                {
                    "N" when isExec => "N",
                    "Y" when !isExec && existing?.LockStatus == "Y" => "Y",
                    _ => "S"
                };

                var id = await db.CallMRNMstSP(new SP_MRNMst
                {
                    MTransNo   = dto.MTransNo,
                    DocSeries  = series,
                    DocYear    = docYear,
                    DocDate    = dto.DocDate.Value.Date,
                    MRNType    = MRNType,
                    Requiredby = dto.RequiredBy!.Trim(),
                    Department = dto.Department!.Trim(),
                    DocNotes   = string.IsNullOrWhiteSpace(dto.DocNotes) ? null : dto.DocNotes.Trim(),
                    LockStatus = lockStatus,
                    SystemIP   = ClientIp,
                    CreatedBy  = dto.UserNo,
                    CreatedOn  = DateTime.Now,
                    SubscID    = dto.SubscID
                });

                if (id <= 0) return ConflictResult("The MRN could not be saved.");

                var docNo = await db.MRNMst.AsNoTracking()
                    .Where(m => m.MTransNo == id).Select(m => m.DocNo).FirstOrDefaultAsync();

                return OkResult(new { ID = id, DocNo = docNo, LockStatus = lockStatus },
                    dto.MTransNo > 0 ? "MRN updated." : $"MRN No {docNo} saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Mst/Approve/{id}")]
        public Task<IActionResult> MstApprove(int id, [FromQuery] int userNo, [FromQuery] int subscID) =>
            RunStatus("Approve", id, 0, userNo, subscID, null);

        [HttpPost("Mst/Unlock/{id}")]
        public Task<IActionResult> MstUnlock(int id, [FromQuery] int userNo, [FromQuery] int subscID) =>
            RunStatus("Unlock", id, 0, userNo, subscID, null);

        [HttpPost("Mst/DeleteData/{id}")]
        public async Task<IActionResult> MstDeleteData(
            int id, [FromQuery] int userNo, [FromQuery] int subscID, [FromQuery] string reason = "")
        {
            if (string.IsNullOrWhiteSpace(reason)) return BadRequestResult("A delete reason is required.");
            return await RunStatus("Delete", id, 0, userNo, subscID, reason.Trim());
        }

        private async Task<IActionResult> RunStatus(
            string action, int mTransNo, int transSlNo, int userNo, int subscID, string? reason)
        {
            if ((mTransNo <= 0 && transSlNo <= 0) || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var (rid, message) = await db.CallMRNStatusSP(
                    action, MRNType, mTransNo, transSlNo, userNo, reason, ClientIp, subscID);
                if (rid == 0) return ConflictResult(message);
                return OkResult(new { ID = rid }, message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — LINES
        // ============================================================

        [HttpGet("Dtl/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                if (!await LiveHeaders(subscID).AnyAsync(m => m.MTransNo == mTransNo))
                    return NotFoundResult("MRN not found.");

                var data = await (
                    from d in db.MRNDtl.AsNoTracking()
                    where d.MTransNo == mTransNo && d.DeleteStatus == "N"
                    join p in db.PartMaster.AsNoTracking() on d.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    join c in db.PartMaster.AsNoTracking() on d.ConvPartNo equals (int?)c.MTransNo into cj
                    from c in cj.DefaultIfEmpty()
                    orderby d.SlNo, d.TransSlNo
                    select new
                    {
                        d.MTransNo,
                        d.TransSlNo,
                        d.SlNo,
                        d.OAYear,
                        d.OANo,
                        oaID = d.OAID,
                        d.PartNo,
                        PartCode = p != null ? p.PartCode : null,
                        PartName = d.Description ?? (p != null ? p.PartName : null),
                        PartType = p != null ? p.PartType : null,
                        d.GradeNo,
                        d.Grade,
                        d.Qty,
                        d.BalQty,
                        d.Unit,
                        d.Rate,
                        d.Amount,
                        d.ItemRemarks,
                        d.ConvPartNo,
                        ConvPartCode = c != null ? c.PartCode : null,
                        ConvDescription = d.ConvDescription ?? (c != null ? c.PartName : null),
                        ConvUnit = c != null ? c.PrimaryUnit : null,
                        d.ConvGradeNo,
                        d.ConvGrade,
                        d.ConvQty
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] MRNDtlSaveRequest dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0) return BadRequestResult("Save the MRN header first.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            if (dto.PartNo <= 0) return BadRequestResult(IsProduction ? "Select the raw material." : "Select the item.");
            if (dto.Qty <= 0) return BadRequestResult("Qty must be greater than zero.");
            if (IsProduction)
            {
                if (dto.ConvPartNo <= 0) return BadRequestResult("Select the part to produce.");
                if (dto.ConvQty <= 0) return BadRequestResult("Qty of the part to produce must be greater than zero.");
            }

            try
            {
                var header = await LiveHeaders(dto.SubscID).FirstOrDefaultAsync(m => m.MTransNo == dto.MTransNo);
                if (header == null) return NotFoundResult("MRN not found, or it has been deleted.");
                if (await db.MRNIssueCount(header.MTransNo) > 0)
                    return ConflictResult("Material has already been issued against this MRN - its lines cannot be changed.");

                var userType = await UserTypeOf(dto.UserNo);
                if (userType == null) return BadRequestResult("Invalid User.");
                if (header.LockStatus == "Y" && userType == Executive)
                    return ConflictResult("An executive cannot change an approved MRN.");

                if (dto.TransSlNo > 0 &&
                    !await db.MRNDtl.AnyAsync(d => d.TransSlNo == dto.TransSlNo &&
                                                   d.MTransNo == dto.MTransNo && d.DeleteStatus == "N"))
                    return NotFoundResult("MRN line not found, or it has been deleted.");

                // OA Year / No / ID: optional, all three or none, live current OA only.
                var oaProblem = await db.CheckOALink(dto.OAYear, dto.OANo, dto.OAID, dto.SubscID);
                if (!string.IsNullOrEmpty(oaProblem)) return BadRequestResult(oaProblem);
                var hasOA = dto.OAYear > 0;

                var part = await db.PartMaster.AsNoTracking().FirstOrDefaultAsync(p =>
                    p.MTransNo == dto.PartNo && p.SubscID == dto.SubscID && p.DeleteStatus == "N");
                if (part == null) return BadRequestResult("The selected part was not found in Part Master.");

                var grade = CleanGrade(dto.Grade);
                int? gradeNo = null;
                if (grade != null)
                {
                    gradeNo = await GradeNoOf(dto.PartNo, grade, dto.SubscID);
                    if (gradeNo == null)
                        return BadRequestResult($"Grade '{grade}' is not one of {part.PartCode}'s grades.");
                }

                PartMaster? conv = null;
                string? convGrade = null;
                int? convGradeNo = null;
                if (IsProduction)
                {
                    conv = await db.PartMaster.AsNoTracking().FirstOrDefaultAsync(p =>
                        p.MTransNo == dto.ConvPartNo && p.SubscID == dto.SubscID && p.DeleteStatus == "N");
                    if (conv == null) return BadRequestResult("The part to produce was not found in Part Master.");

                    convGrade = CleanGrade(dto.ConvGrade);
                    if (convGrade != null)
                    {
                        convGradeNo = await GradeNoOf(dto.ConvPartNo, convGrade, dto.SubscID);
                        if (convGradeNo == null)
                            return BadRequestResult($"Grade '{convGrade}' is not one of {conv.PartCode}'s grades.");
                    }
                }

                // Valued at the Part Master price, as the legacy screen did.
                var rate = part.SalesPrice ?? 0m;
                var unit = string.IsNullOrWhiteSpace(part.PrimaryUnit) ? (dto.Unit ?? "").Trim() : part.PrimaryUnit!.Trim();
                var remarks = (dto.ItemRemarks ?? "").Trim();

                var id = await db.CallMRNDtlSP(new SP_MRNDtl
                {
                    MTransNo        = dto.MTransNo,
                    TransSlNo       = dto.TransSlNo,
                    SlNo            = dto.SlNo,
                    PartNo          = dto.PartNo,
                    OAYear          = hasOA ? dto.OAYear : null,
                    OANo            = hasOA ? dto.OANo : null,
                    OAID            = hasOA ? dto.OAID : null,
                    PartDescription = part.PartName,
                    GradeNo         = gradeNo,
                    Grade           = grade,
                    ItemRemarks     = remarks == "" ? null : remarks,
                    Qty             = Math.Round(dto.Qty, 2),
                    BalQty          = Math.Round(dto.Qty, 2),   // Store Issue draws this down
                    Unit            = unit == "" ? null : unit,
                    Rate            = Math.Round(rate, 2),
                    Amount          = Math.Round(dto.Qty * rate, 2),
                    ConvPartNo      = conv?.MTransNo,
                    ConvDescription = conv?.PartName,
                    ConvGradeNo     = convGradeNo,
                    ConvGrade       = convGrade,
                    ConvQty         = IsProduction ? Math.Round(dto.ConvQty, 2) : null,
                    CreatedBy       = dto.UserNo,
                    CreatedOn       = DateTime.Now,
                    SubscID         = dto.SubscID
                });

                if (id <= 0) return ConflictResult("The line could not be saved.");
                return OkResult(new { ID = id }, "Line saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Dtl/DeleteData/{transSlNo}")]
        public Task<IActionResult> DtlDeleteData(int transSlNo, [FromQuery] int userNo, [FromQuery] int subscID) =>
            RunStatus("DtlDelete", 0, transSlNo, userNo, subscID, null);

        /// <summary>MaterialGrade.GradeNo of one of the part's live grades.</summary>
        private async Task<int?> GradeNoOf(int partNo, string grade, int subscID) =>
            await db.MaterialGrade.AsNoTracking()
                .Where(g => g.PartNo == partNo && g.SubscID == subscID &&
                            g.DeleteStatus == "N" && g.Grade == grade)
                .Select(g => (int?)g.GradeNo)
                .FirstOrDefaultAsync();
    }
}
