using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Heat No. Entry — the foundry heat and test-certificate record for
    /// material that has arrived.
    ///
    /// A heat document is raised AGAINST a GRN: the header names the receipt,
    /// the part, the grade and the total quantity covered; each line is one
    /// heat number with its test certificate; and a line may expand into the
    /// individual piece serial numbers it covers (HeatRefNo), which is what
    /// makes a finished casting traceable back to the melt it came from.
    ///
    ///     Source/GRNList/{subscID}      receipts that can carry heats
    ///     Source/GRNParts/{grn}         the parts received on one
    ///     Mst/SaveData                  open the heat document
    ///     Dtl/SaveData                  one heat, with RefFrom/RefTo
    ///     Dtl/CheckHeatNo               is this heat already recorded?
    ///     Dtl/GetByPart                 approved heats of a part, with balance
    ///                                   (the Store Issue heat-no picker)
    ///     Dtl/UploadTC                  attach the test certificate
    ///     Mst/Approve                   submitted (S) → approved (Y)
    ///     Mst/LockUnlock                Lock = approve, Unlock = back to S
    ///
    /// LockStatus: N = draft (an executive's own), S = submitted for
    /// approval, Y = approved. Saving an edit re-submits an approved
    /// document (its lines are locked while it is approved); the screen
    /// re-approves it afterwards. See SQL/32_HeatNoPage.sql.
    ///
    /// Two rules the procedures hold and the screen should surface:
    ///   * the heats on a document may not total more than the document's own
    ///     quantity;
    ///   * the same heat number cannot be recorded twice for the same part.
    /// </summary>
    [Route("api/[controller]")]
    public class HeatNoController : EmsControllerBase
    {
        private readonly EMSContext db;

        public HeatNoController(EMSContext context) => db = context;

        // ============================================================
        // SOURCE — which GRN, which part
        // ============================================================

        /// <summary>Confirmed receipts, newest first, to raise heats against.
        /// <c>pendingHeatOnly=true</c> keeps only the GRNs with at least one
        /// part that has no heat document yet — what the Add picker offers.</summary>
        [HttpGet("Source/GRNList/{subscID}")]
        public async Task<IActionResult> SourceGRNList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? search = null,
            [FromQuery] bool pendingHeatOnly = false)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.LineCount > 0 && x.LockStatus == "Y");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.VendorName!.Contains(search) || x.RefNo!.Contains(search));
                if (pendingHeatOnly)
                    query = query.Where(g => db.GRNDtl.Any(d =>
                        d.MTransNo == g.MTransNo && d.SubscID == subscID && d.DeleteStatus == "N" &&
                        !db.HeatNoMst.Any(h =>
                            h.TargetTransNo == g.MTransNo && h.PartNo == d.PartNo &&
                            h.SubscID == subscID && h.DeleteStatus == "N")));

                var data = await query
                    .OrderByDescending(x => x.DocYear).ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.VendorNo, x.VendorCode, x.VendorName, x.RefNo, x.RefDate,
                        x.GRNType, x.StageName, x.LineCount, x.TotalRecQty
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The parts received on one GRN, with how much of each is already
        /// covered by a heat document — so the same receipt is not recorded
        /// twice by accident.
        /// </summary>
        [HttpGet("Source/GRNParts/{grnTransNo}/{subscID}")]
        public async Task<IActionResult> SourceGRNParts(
            int grnTransNo,
            int subscID,
            [FromQuery] bool pendingOnly = false)
        {
            if (grnTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var lines = await db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == grnTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .GroupBy(x => new { x.PartNo, x.PartCode, x.PartDescription, x.GradeNo, x.Grade, x.Unit })
                    .Select(g => new
                    {
                        g.Key.PartNo, g.Key.PartCode, g.Key.PartDescription,
                        g.Key.GradeNo, g.Key.Grade, g.Key.Unit,
                        RecQty = g.Sum(x => x.RecQty)
                    })
                    .ToListAsync();

                if (lines.Count == 0)
                    return OkResult(lines, "No records found.");

                var covered = await db.HeatNoMst
                    .AsNoTracking()
                    .Where(x => x.TargetTransNo == grnTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .GroupBy(x => x.PartNo)
                    .Select(g => new
                    {
                        PartNo = g.Key,
                        Qty = g.Sum(x => x.Qty ?? 0),
                        HeatMTransNo = g.Min(x => x.MTransNo),
                        HeatDocNo = g.Min(x => x.DocNo)
                    })
                    .ToListAsync();

                // PartMaster.MfrLicNo = 'Y' is the legacy "heat traced" flag.
                // Reported, not filtered on: the new Part Master screen does
                // not set it yet.
                var partNos = lines.Select(l => l.PartNo).Distinct().ToList();
                var traced = await db.PartMaster
                    .AsNoTracking()
                    .Where(p => partNos.Contains(p.MTransNo) && p.MfrLicNo == "Y")
                    .Select(p => p.MTransNo)
                    .ToListAsync();

                var data = lines.Select(l =>
                {
                    var c = covered.FirstOrDefault(x => x.PartNo == l.PartNo);
                    return new
                    {
                        l.PartNo, l.PartCode, l.PartDescription, l.GradeNo, l.Grade, l.Unit,
                        l.RecQty,
                        HeatCoveredQty = c?.Qty ?? 0,
                        PendingQty     = l.RecQty - (c?.Qty ?? 0),
                        HeatMTransNo   = c?.HeatMTransNo,
                        HeatDocNo      = c?.HeatDocNo,
                        HeatTraced     = traced.Contains(l.PartNo)
                    };
                })
                .Where(x => !pendingOnly || x.HeatMTransNo == null)
                .ToList();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // MST — HEAT DOCUMENT
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_HeatNoMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.TargetTransNo <= 0)
                return BadRequestResult("Select the GRN these heats belong to.");
            if (dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallHeatNoMstSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);

                // The procedure allocates the number on insert; hand it back
                // so the screen can show what was actually issued.
                var docNo = await db.HeatNoMst.AsNoTracking()
                    .Where(x => x.MTransNo == result.ID)
                    .Select(x => x.DocNo)
                    .FirstOrDefaultAsync();

                return OkResult(new { ID = result.ID, DocNo = docNo }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Preview of the number the next document in a series will get. Only
        /// a preview: <c>SP_HeatNoMst</c> allocates the real one on insert,
        /// from the same query (deleted rows included, so no number is reused).
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
                var max = await db.HeatNoMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => x.DocNo) ?? 0;

                return OkResult(max + 1);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Approve a submitted document (LockStatus S → Y). Refused for a
        /// draft, for a document with no heat lines, and for an EXECUTIVE user.
        /// </summary>
        [HttpPost("Mst/Approve/{id}")]
        public async Task<IActionResult> MstApprove(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID)
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallHeatNoMstSP(new SP_HeatNoMst
                {
                    MTransNo   = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "Approve"
                });
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
                var result = await db.CallHeatNoMstSP(new SP_HeatNoMst
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
                var result = await db.CallHeatNoMstSP(new SP_HeatNoMst
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
            [FromQuery] int? partNo = null,
            [FromQuery] int? grnTransNo = null,
            [FromQuery] string? search = null,
            [FromQuery] int userNo = 0,
            [FromQuery] string? userType = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.HeatNoMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // The legacy list rule, applied when the caller says who it
                // is: a draft is seen only by its creator; a submitted
                // document by its creator and by non-executives (who approve).
                if (userNo > 0)
                {
                    var isExec = string.Equals((userType ?? "").Trim(), "EXECUTIVE",
                                               StringComparison.OrdinalIgnoreCase);
                    query = isExec
                        ? query.Where(x => x.CreatedBy == userNo || x.LockStatus == "Y")
                        : query.Where(x => x.CreatedBy == userNo || x.LockStatus == "Y" || x.LockStatus == "S");
                }

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (partNo.HasValue && partNo > 0)
                    query = query.Where(x => x.PartNo == partNo);
                if (grnTransNo.HasValue && grnTransNo > 0)
                    query = query.Where(x => x.TargetTransNo == grnTransNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.PartCode!.Contains(search)        ||
                        x.PartDescription!.Contains(search) ||
                        x.Grade!.Contains(search)           ||
                        x.DocNotes!.Contains(search));

                var data = await query
                    .OrderByDescending(x => x.DocYear).ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.TargetTransNo, x.TargetDocYear, x.TargetDocNo,
                        x.PartNo, x.PartCode, x.PartDescription,
                        x.GradeNo, x.Grade, x.Qty, x.Unit, x.DocNotes,
                        x.DocStatus, x.LockStatus, x.LockCounter,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn,
                        VendorName = db.GRNMst
                            .Where(g => g.MTransNo == x.TargetTransNo)
                            .Select(g => g.VendorName)
                            .FirstOrDefault(),
                        HeatQty = db.HeatNoDtl
                            .Where(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
                            .Sum(d => (decimal?)d.Qty) ?? 0,
                        LineCount = db.HeatNoDtl
                            .Count(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
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
                var data = await db.HeatNoMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Header plus every heat line, each with its serial-number
        /// range — what the screen needs to open a document.</summary>
        [HttpGet("Mst/GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> MstGetFullData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var header = await db.HeatNoMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (header == null) return NotFoundResult();

                return OkResult(new
                {
                    Header = header,
                    Lines  = await LoadLines(id, subscID)
                });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — INDIVIDUAL HEATS
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_HeatNoDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid heat document.");
            if (dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (string.IsNullOrWhiteSpace(dto.HeatNo))
                return BadRequestResult("Heat No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.RefFrom > 0 && dto.RefTo < dto.RefFrom)
                return BadRequestResult("Reference 'to' cannot be lower than 'from'.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallHeatNoDtlSP(dto);
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
                var result = await db.CallHeatNoDtlSP(new SP_HeatNoDtl
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
                var result = await db.CallHeatNoDtlSP(new SP_HeatNoDtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid heat document.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadLines(mTransNo, subscID);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Is this heat number already on file for this part? The screen calls
        /// it as the heat-number field is left, so the operator is told before
        /// the save is refused.
        /// </summary>
        [HttpGet("Dtl/CheckHeatNo/{subscID}")]
        public async Task<IActionResult> DtlCheckHeatNo(
            int subscID,
            [FromQuery] string heatNo = "",
            [FromQuery] int partNo = 0,
            [FromQuery] int excludeTransSlNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(heatNo)) return BadRequestResult("Heat No is required.");
            if (partNo <= 0) return BadRequestResult("Part No is required.");

            try
            {
                var trimmed = heatNo.Trim();

                var existing = await db.HeatNoDtl
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N"
                             && x.HeatNo == trimmed && x.PartNo == partNo
                             && x.TransSlNo != excludeTransSlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.MTransNo, x.HeatNo, x.Grade,
                        x.Qty, x.Unit, x.TCNo, x.TCDate
                    })
                    .FirstOrDefaultAsync();

                return OkResult(
                    new { Exists = existing != null, Existing = existing },
                    existing == null
                        ? "Heat number is available."
                        : $"Heat number {trimmed} is already recorded for this part.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Approved heat numbers of one part (and grade, when given) with what
        /// is left of each — the Store Issue line's Heat No picker (SQL 58).
        /// GetByPart?partNo=&amp;grade=&amp;subscID=
        /// Row: { heatNo, tcNo, tcDate, grade, unit, qty, issuedQty, balanceQty,
        ///        grnNo, grnTransNo, heatDocNo, transSlNo }
        /// issuedQty = Σ live StoreIssueDtl.Qty of this part carrying the heat.
        /// A heat without a grade of its own matches every grade.
        /// </summary>
        [HttpGet("Dtl/GetByPart")]
        public async Task<IActionResult> DtlGetByPart(
            [FromQuery] int partNo,
            [FromQuery] int subscID,
            [FromQuery] string? grade = null)
        {
            if (partNo <= 0) return BadRequestResult("Part No is required.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PmQueryRowsAsync("""
                    SELECT UPPER(LTRIM(RTRIM(d.HeatNo)))              AS heatNo,
                           ISNULL(d.TCNo, '')                         AS tcNo,
                           CONVERT(char(10), d.TCDate, 23)            AS tcDate,
                           LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(d.Grade)), ''), ISNULL(m.Grade, '')))) AS grade,
                           ISNULL(NULLIF(d.Unit, ''), ISNULL(m.Unit, '')) AS unit,
                           d.Qty                                      AS qty,
                           ISNULL(iss.q, 0)                           AS issuedQty,
                           d.Qty - ISNULL(iss.q, 0)                   AS balanceQty,
                           CONCAT(m.TargetDocYear, '-', m.TargetDocNo) AS grnNo,
                           m.TargetTransNo                            AS grnTransNo,
                           CONCAT(m.DocYear, '-', m.DocNo)            AS heatDocNo,
                           d.TransSlNo                                AS transSlNo
                      FROM dbo.HeatNoDtl d
                      JOIN dbo.HeatNoMst m ON m.MTransNo = d.MTransNo
                                          AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                     OUTER APPLY (SELECT SUM(i.Qty) AS q
                                    FROM dbo.StoreIssueDtl i
                                    JOIN dbo.StoreIssueMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
                                   WHERE i.DeleteStatus = 'N' AND i.SubscID = d.SubscID AND i.PartNo = d.PartNo
                                     AND UPPER(LTRIM(RTRIM(i.HeatNo))) = UPPER(LTRIM(RTRIM(d.HeatNo)))) iss
                     WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.PartNo = @PartNo
                       AND LTRIM(RTRIM(ISNULL(d.HeatNo, ''))) <> ''
                       AND (@Grade = ''
                            OR LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(d.Grade)), ''), ISNULL(m.Grade, '')))) IN ('', @Grade))
                     ORDER BY m.DocDate, m.DocNo, d.SlNo
                    """,
                    new SqlParameter("@PartNo",  SqlDbType.Int) { Value = partNo },
                    new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                    new SqlParameter("@Grade",   SqlDbType.VarChar, 50) { Value = (grade ?? "").Trim() });

                return OkResult(data, data.Count > 0 ? "Data found." : "No approved heats for this part.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The piece serial numbers a heat line covers. Returned as the range
        /// the screen shows, plus the full list for a report.
        /// </summary>
        [HttpGet("Dtl/GetRefNos/{hTransSlNo}/{subscID}")]
        public async Task<IActionResult> DtlGetRefNos(int hTransSlNo, int subscID)
        {
            if (hTransSlNo <= 0) return BadRequestResult("Invalid heat line.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var refs = await db.HeatRefNo
                    .AsNoTracking()
                    .Where(x => x.HTransSlNo == hTransSlNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => x.RefNo)
                    .OrderBy(x => x)
                    .ToListAsync();

                return OkResult(new
                {
                    Count   = refs.Count,
                    RefFrom = refs.Count > 0 ? refs.First() : (int?)null,
                    RefTo   = refs.Count > 0 ? refs.Last()  : (int?)null,
                    RefNos  = refs
                }, refs.Count > 0 ? "Data found." : "No serial numbers recorded.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The last piece reference number issued on any live heat document.
        /// A new document's ranges start at <c>NextRefNo</c>; the procedure
        /// refuses a range that overlaps one already issued elsewhere.
        /// </summary>
        [HttpGet("Dtl/GetRefMaxNo/{subscID}")]
        public async Task<IActionResult> DtlGetRefMaxNo(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var last = await db.HeatRefNo
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .MaxAsync(x => (int?)x.RefNo) ?? 0;

                return OkResult(new { LastRefNo = last, NextRefNo = last + 1 });
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Attach the test certificate to one heat line. The file is written
        /// under wwwroot/Uploads/HeatTC/{mTransNo}/ with a generated name (see
        /// <see cref="GrnUploads"/> for the allowlist and size limit) and its
        /// relative path is stored in HeatNoDtl.TCFile — the same path the
        /// static-file pipeline serves it back from. A replaced certificate's
        /// old file is kept on disk.
        /// </summary>
        [HttpPost("Dtl/UploadTC/{transSlNo}")]
        [RequestSizeLimit(30_000_000)]
        public async Task<IActionResult> DtlUploadTC(
            int transSlNo,
            [FromForm] IFormFile? file,
            [FromForm] int mTransNo,
            [FromForm] int userNo,
            [FromForm] int subscID)
        {
            if (file == null || file.Length == 0)
                return BadRequestResult("No file was uploaded.");
            if (transSlNo <= 0 || mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var exists = await db.HeatNoDtl.AsNoTracking().AnyAsync(x =>
                    x.TransSlNo == transSlNo && x.MTransNo == mTransNo &&
                    x.SubscID == subscID && x.DeleteStatus == "N");
                if (!exists) return NotFoundResult("Heat line not found.");

                var stored = await GrnUploads.SaveAsync(HttpContext, "HeatTC", mTransNo, file);
                if (stored.Error != null) return BadRequestResult(stored.Error);

                var result = await db.CallHeatNoDtlSP(new SP_HeatNoDtl
                {
                    TransSlNo  = transSlNo,
                    MTransNo   = mTransNo,
                    TCFile     = stored.RelativePath,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "TCFile"
                });

                if (result.ID == 0)
                {
                    // The row was refused, so the file on disk is an orphan.
                    GrnUploads.TryDelete(stored.FullPath);
                    return ConflictResult(result.Message);
                }

                return OkResult(new { ID = result.ID, TCFile = stored.RelativePath }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Trace a heat number: which receipt it arrived on, from which
        /// supplier, and which pieces it covers.
        /// </summary>
        [HttpGet("Trace/{subscID}")]
        public async Task<IActionResult> Trace(int subscID, [FromQuery] string heatNo = "")
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(heatNo)) return BadRequestResult("Heat No is required.");

            try
            {
                var trimmed = heatNo.Trim();

                var data = await (
                    from d in db.HeatNoDtl.AsNoTracking()
                    join m in db.HeatNoMst.AsNoTracking() on d.MTransNo equals m.MTransNo
                    join g in db.GRNMst.AsNoTracking() on m.TargetTransNo equals g.MTransNo into gj
                    from g in gj.DefaultIfEmpty()
                    where d.SubscID == subscID && d.DeleteStatus == "N" && d.HeatNo == trimmed
                    orderby m.DocYear, m.DocNo, d.SlNo
                    select new
                    {
                        HeatDocNo = m.DocNo,
                        m.DocYear,
                        m.DocDate,
                        d.TransSlNo,
                        d.HeatNo,
                        d.TCNo,
                        d.TCDate,
                        d.Qty,
                        d.Unit,
                        d.PartNo,
                        d.PartCode,
                        d.PartDescription,
                        d.Grade,
                        GRNTransNo = g.MTransNo,
                        GRNDocYear = g.DocYear,
                        GRNDocNo   = g.DocNo,
                        GRNDocDate = g.DocDate,
                        g.VendorNo,
                        g.VendorName,
                        SerialCount = db.HeatRefNo.Count(r =>
                            r.HTransSlNo == d.TransSlNo && r.DeleteStatus == "N")
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0
                    ? "Data found."
                    : $"Heat number {trimmed} has not been recorded.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // Shared internals
        // ============================================================

        private async Task<List<object>> LoadLines(int mTransNo, int subscID)
        {
            var lines = await db.HeatNoDtl
                .AsNoTracking()
                .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                .OrderBy(x => x.SlNo)
                .ToListAsync();

            if (lines.Count == 0) return new List<object>();

            var ids = lines.Select(l => l.TransSlNo).ToList();

            // The serial ranges, one round trip for the whole document rather
            // than one per line.
            var ranges = await db.HeatRefNo
                .AsNoTracking()
                .Where(x => ids.Contains(x.HTransSlNo) && x.DeleteStatus == "N")
                .GroupBy(x => x.HTransSlNo)
                .Select(g => new
                {
                    HTransSlNo = g.Key,
                    RefFrom = g.Min(x => x.RefNo),
                    RefTo   = g.Max(x => x.RefNo),
                    Count   = g.Count()
                })
                .ToListAsync();

            return lines.Select(l =>
            {
                var r = ranges.FirstOrDefault(x => x.HTransSlNo == l.TransSlNo);
                return (object)new
                {
                    l.TransSlNo, l.SlNo, l.MTransNo, l.PartNo, l.PartCode, l.PartDescription,
                    l.GradeNo, l.Grade, l.ItemRemarks, l.Qty, l.Unit,
                    l.HeatNo, l.TCNo, l.TCDate, l.TCFile,
                    RefFrom     = r?.RefFrom,
                    RefTo       = r?.RefTo,
                    SerialCount = r?.Count ?? 0,
                    l.CreatedBy, l.CreatedOn, l.ModifiedBy, l.ModifiedOn
                };
            }).ToList();
        }
    }
}
