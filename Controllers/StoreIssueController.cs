using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Shared plumbing for the Store Issue and Store Returns controllers:
    /// who may touch a document, and stock in hand from VW_StoreStock.
    /// </summary>
    public abstract class StoreChainControllerBase : EmsControllerBase
    {
        protected readonly EMSContext db;
        protected StoreChainControllerBase(EMSContext context) => db = context;

        /// <summary>User types that may edit any document, whoever raised it.</summary>
        protected static readonly string[] EditAllUserTypes = { "ADMIN", "MANAGER" };

        protected async Task<string> UserTypeOf(int userNo) =>
            (await db.UserMaster.AsNoTracking()
                .Where(u => u.MTransNo == userNo)
                .Select(u => u.UserType)
                .FirstOrDefaultAsync() ?? "").Trim().ToUpperInvariant();

        /// <summary>The creator, an ADMIN or a MANAGER — the legacy edit rule.</summary>
        protected async Task<bool> CanEdit(int createdBy, int userNo) =>
            userNo > 0 && (createdBy == userNo || EditAllUserTypes.Contains(await UserTypeOf(userNo)));

        /// <summary>ADMIN always; the creator only while the document is open.</summary>
        protected async Task<bool> CanDelete(int createdBy, string lockStatus, int userNo)
        {
            if (userNo <= 0) return false;
            if (await UserTypeOf(userNo) == "ADMIN") return true;
            return createdBy == userNo && lockStatus != "Y";
        }

        protected static string NormGrade(string? grade) => (grade ?? "").Trim();

        /// <summary>
        /// Stock in hand of one part in one grade, optionally leaving out every
        /// line of one store issue (the one being edited).
        /// </summary>
        protected async Task<decimal> StockOf(int partNo, string? grade, int subscID, int excludeIssueMTransNo = 0)
        {
            var g = NormGrade(grade);
            return await db.StoreStock.AsNoTracking()
                .Where(s => s.PartNo == partNo && s.Grade == g && s.SubscID == subscID &&
                            !(s.TxnType == "ISSUE" && s.RefTransNo == excludeIssueMTransNo))
                .SumAsync(s => (decimal?)(s.InQty - s.OutQty)) ?? 0m;
        }

        protected static string Label(string? code, string? name) =>
            !string.IsNullOrWhiteSpace(code) ? code.Trim() : (name ?? "").Trim();

        protected static string Qty(decimal q) => q.ToString("0.##");

        protected static string Trunc(string? s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s[..max] : s;
        }

        protected static int YearFromSeries(string? series)
        {
            var s = (series ?? "").Trim();
            return s.Length > 3 && int.TryParse(s[3..].Trim(), out var y) ? y : 0;
        }
    }

    /// <summary>
    /// Store Issue — material issued from stores against an approved MRN
    /// (Production 'PROD' or General 'GEN') or a job card's raw material
    /// (Issue Type 'Job Card', source type 'JC' — SQL 56). Each source line
    /// comes from VW_StoreIssueMRNLine, so the balance / stock / return rules
    /// below apply to all three alike.
    ///
    ///     GetMasterList / GetMasterData / GetDetailsList   register, header, lines
    ///     GetMRNNo / GetMRNMst / GetMRNDtl                 approved MRNs with balance
    ///     GetStock                                         stock in hand (VW_StoreStock)
    ///     GetMaxNo / DocNoExists                           numbering
    ///     SaveMasterData / SaveDtlData                     header, then one line per call
    ///     Lock                                             issue complete → LockStatus 'Y'
    ///     DtlDelete / RecDelete                            soft deletes
    ///
    /// As in the legacy screen an issue is saved Open (N) and locked (Y) once
    /// its lines are written; only locked issues can be returned against.
    /// Server-side rules: a line may not take more than the MRN line's balance
    /// (MRN Qty less live issues) nor more than the stock in hand; an issue
    /// line cannot drop below what has been returned against it; an issue (or
    /// line) with live returns cannot be deleted. See SQL 44 / 46.
    /// </summary>
    [Route("api/[controller]")]
    public class StoreIssueController : StoreChainControllerBase
    {
        public StoreIssueController(EMSContext context) : base(context) { }

        /// <summary>
        /// Issue Type on the page → source type in VW_StoreIssueMRNLine:
        /// MRNMst.MRNType for MRNs, 'JC' for a job card's raw material (SQL 56).
        /// </summary>
        private static string? MrnTypeOf(string? targetType) => (targetType ?? "").Trim() switch
        {
            "Production MRN" => "PROD",
            "General MRN" => "GEN",
            "Job Card" => "JC",
            _ => null
        };

        private const string UnsupportedType =
            "Store issue is supported against Production MRN, General MRN and Job Card only.";

        // ============================================================
        // REGISTER / READ
        // ============================================================

        [HttpGet("GetMasterList")]
        public async Task<IActionResult> GetMasterList(int UserNo = 0, string? UserType = null, int SubscID = 0)
        {
            if (UserNo <= 0) return BadRequestResult("Invalid User.");
            try
            {
                var type = await UserTypeOf(UserNo);
                var seeAll = EditAllUserTypes.Contains(type);

                var q = db.StoreIssueMst.AsNoTracking().Where(m => m.DeleteStatus == "N");
                if (SubscID > 0) q = q.Where(m => m.SubscID == SubscID);
                if (!seeAll) q = q.Where(m => m.CreatedBy == UserNo || m.LockStatus == "Y");

                var data = await q
                    .OrderByDescending(m => m.MTransNo)
                    .Select(m => new
                    {
                        m.MTransNo, m.DocSeries, m.DocYear, m.DocNo, m.DocDate,
                        m.TargetType, m.TargetTransNo, m.TargetDocYear, m.TargetDocNo,
                        Indentor = m.IndentorName, m.Department, m.DocNotes,
                        m.LockStatus, m.CreatedBy, m.CreatedOn,
                        LineCount = db.StoreIssueDtl.Count(d => d.MTransNo == m.MTransNo && d.DeleteStatus == "N"),
                        TotalQty = db.StoreIssueDtl.Where(d => d.MTransNo == m.MTransNo && d.DeleteStatus == "N")
                                                   .Sum(d => (decimal?)d.Qty) ?? 0
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData(int MTransNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            try
            {
                var m = await db.StoreIssueMst.AsNoTracking()
                    .Where(x => x.MTransNo == MTransNo && x.DeleteStatus == "N")
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.TargetType, x.TargetTransNo, x.TargetDocYear, x.TargetDocNo,
                        Indentor = x.IndentorName, x.Department, x.DocNotes,
                        x.LockStatus, x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn, x.SubscID,
                        LineCount = db.StoreIssueDtl.Count(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N"),
                        TotalQty = db.StoreIssueDtl.Where(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
                                                   .Sum(d => (decimal?)d.Qty) ?? 0
                    })
                    .FirstOrDefaultAsync();
                return m == null ? NotFoundResult("Store issue not found.") : OkResult(m, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The issue's own lines joined onto its MRN's lines. Every MRN line
        /// with a balance comes back, issued on this issue or not, so an edit
        /// can still pick up a line the first save skipped. BalQty leaves out
        /// this issue's own quantity, so re-keying it never reads as over-issue.
        /// </summary>
        [HttpGet("GetDetailsList")]
        public async Task<IActionResult> GetDetailsList(int MTransNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            try
            {
                var h = await db.StoreIssueMst.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store issue not found.");

                var own = await db.StoreIssueDtl.AsNoTracking()
                    .Where(d => d.MTransNo == MTransNo && d.DeleteStatus == "N")
                    .OrderBy(d => d.SlNo).ThenBy(d => d.TransSlNo)
                    .ToListAsync();
                var returned = await db.StoreIssueLineBals.AsNoTracking()
                    .Where(b => b.MTransNo == MTransNo)
                    .Select(b => new { b.LinkNo, b.ReturnedQty })
                    .ToListAsync();
                var srcType = MrnTypeOf(h.TargetType);
                var mrn = await db.StoreIssueMRNLines.AsNoTracking()
                    .Where(l => l.MTransNo == (h.TargetTransNo ?? 0) && l.MRNType == srcType)
                    .OrderBy(l => l.SlNo).ThenBy(l => l.LinkNo)
                    .ToListAsync();

                var rows = new List<object>();
                foreach (var l in mrn)
                {
                    var o = own.FirstOrDefault(d => d.LinkNo == l.LinkNo);
                    var ownQty = o?.Qty ?? 0;
                    var bal = l.BalQty + ownQty;
                    if (bal <= 0 && o == null) continue;
                    rows.Add(new
                    {
                        TransSlNo = o?.TransSlNo ?? 0,
                        l.LinkNo, l.PartNo, l.PartCode, l.PartName, l.GradeNo, l.Grade,
                        l.OAYear, l.OANo, OaID = l.OAID,
                        DcOperation = o?.DCOpration ?? "",
                        MrnQty = l.Qty, BalQty = bal, IssueQty = ownQty,
                        ReturnedQty = o == null ? 0 : returned.FirstOrDefault(r => r.LinkNo == o.TransSlNo)?.ReturnedQty ?? 0,
                        HeatNo = o?.HeatNo ?? "",
                        l.Unit,
                        ItemRemarks = o?.ItemRemarks ?? ""
                    });
                }
                // Lines whose MRN line is no longer offered (MRN un-approved or
                // deleted since) still show, capped at what they already hold.
                foreach (var o in own.Where(d => !mrn.Any(l => l.LinkNo == d.LinkNo)))
                {
                    rows.Add(new
                    {
                        o.TransSlNo, LinkNo = o.LinkNo ?? 0, o.PartNo, o.PartCode, PartName = o.PartDescription,
                        o.GradeNo, Grade = o.Grade ?? "", o.OAYear, o.OANo, OaID = o.OAID,
                        DcOperation = o.DCOpration ?? "",
                        MrnQty = o.MRNQty, BalQty = o.Qty, IssueQty = o.Qty,
                        ReturnedQty = returned.FirstOrDefault(r => r.LinkNo == o.TransSlNo)?.ReturnedQty ?? 0,
                        HeatNo = o.HeatNo ?? "",
                        o.Unit, ItemRemarks = o.ItemRemarks ?? ""
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — approved MRNs with something left to issue
        // ============================================================

        [HttpGet("GetMRNNo")]
        public async Task<IActionResult> GetMRNNo(string? TargetType, int SubscID)
        {
            var mrnType = MrnTypeOf(TargetType);
            if (mrnType == null) return BadRequestResult(UnsupportedType);
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await db.StoreIssueMRNLines.AsNoTracking()
                    .Where(l => l.MRNType == mrnType && l.SubscID == SubscID && l.BalQty > 0)
                    .GroupBy(l => new { l.MTransNo, l.DocSeries, l.DocYear, l.DocNo, l.DocDate, l.Requiredby, l.Department })
                    .Select(g => new
                    {
                        g.Key.MTransNo, g.Key.DocSeries, g.Key.DocYear, g.Key.DocNo, g.Key.DocDate,
                        RequiredBy = g.Key.Requiredby, g.Key.Department,
                        PendingLines = g.Count(), BalQty = g.Sum(x => x.BalQty)
                    })
                    .OrderByDescending(x => x.DocYear).ThenByDescending(x => x.DocNo)
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetMRNMst")]
        public async Task<IActionResult> GetMRNMst(string? TargetType, int TransNo)
        {
            var mrnType = MrnTypeOf(TargetType);
            if (mrnType == null) return BadRequestResult(UnsupportedType);
            if (TransNo <= 0) return BadRequestResult("Invalid MRN.");
            try
            {
                var m = await db.StoreIssueMRNLines.AsNoTracking()
                    .Where(l => l.MTransNo == TransNo && l.MRNType == mrnType)
                    .Select(l => new
                    {
                        l.MTransNo, l.MRNType, l.DocSeries, l.DocYear, l.DocNo, l.DocDate,
                        RequiredBy = l.Requiredby, l.Department, l.SubscID
                    })
                    .FirstOrDefaultAsync();
                return m == null ? NotFoundResult("MRN not found, not approved, or not a " + TargetType + ".") : OkResult(m, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetMRNDtl")]
        public async Task<IActionResult> GetMRNDtl(string? TargetType, int TransNo)
        {
            var mrnType = MrnTypeOf(TargetType);
            if (mrnType == null) return BadRequestResult(UnsupportedType);
            if (TransNo <= 0) return BadRequestResult("Invalid MRN.");
            try
            {
                var data = await db.StoreIssueMRNLines.AsNoTracking()
                    .Where(l => l.MTransNo == TransNo && l.MRNType == mrnType && l.BalQty > 0)
                    .OrderBy(l => l.SlNo).ThenBy(l => l.LinkNo)
                    .Select(l => new
                    {
                        TransSlNo = 0, l.LinkNo, l.PartNo, l.PartCode, l.PartName, l.GradeNo, l.Grade,
                        l.OAYear, l.OANo, OaID = l.OAID, DcOperation = "",
                        l.Qty, MrnQty = l.Qty, l.IssuedQty, l.BalQty, IssueQty = 0m,
                        l.Unit, l.ItemRemarks
                    })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Stock in hand of a part in a grade. MTransNo = the issue
        /// being edited, whose own quantity counts as still in hand.</summary>
        [HttpGet("GetStock")]
        public async Task<IActionResult> GetStock(int PartNo, string? Grade, int MTransNo = 0, int SubscID = 0)
        {
            if (PartNo <= 0) return BadRequestResult("Invalid Part.");
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var stock = await StockOf(PartNo, Grade, SubscID, MTransNo);
                return OkResult(new { PartNo, Grade = NormGrade(Grade), StockQty = stock }, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // NUMBERING
        // ============================================================

        [HttpGet("GetMaxNo")]
        public async Task<IActionResult> GetMaxNo(string? DocSeries, int mYear, int SubscID)
        {
            if (string.IsNullOrWhiteSpace(DocSeries)) return BadRequestResult("Doc Series is required.");
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var s = DocSeries.Trim();
                var max = await db.StoreIssueMst.AsNoTracking()
                    .Where(x => x.DeleteStatus == "N" && x.DocSeries == s && x.DocYear == mYear && x.SubscID == SubscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;
                return OkResult(max + 1, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>"S" when another live issue holds this number in the series, else "N".</summary>
        [HttpGet("DocNoExists")]
        public async Task<IActionResult> DocNoExists(string? DocSeries, int DocNo, int MTransNo, int SubscID)
        {
            try
            {
                var s = (DocSeries ?? "").Trim();
                var y = YearFromSeries(s);
                var clash = await db.StoreIssueMst.AsNoTracking().AnyAsync(x =>
                    x.DeleteStatus == "N" && x.DocSeries == s && x.DocNo == DocNo &&
                    (y == 0 || x.DocYear == y) && x.SubscID == SubscID && x.MTransNo != MTransNo);
                return OkResult(clash ? "S" : "N");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SAVE
        // ============================================================

        [HttpPost("SaveMasterData")]
        public async Task<IActionResult> SaveMasterData([FromBody] StoreIssueHeaderDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries)) return BadRequestResult("Select the Series.");
            var docYear = dto.DocYear > 0 ? dto.DocYear : YearFromSeries(dto.DocSeries);
            if (docYear <= 0) return BadRequestResult("Doc Year could not be read from the series.");
            if (dto.DocDate == null) return BadRequestResult("Issue Date is required.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                StoreIssueMst h;
                if (dto.MTransNo > 0)
                {
                    var cur = await db.StoreIssueMst.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.MTransNo == dto.MTransNo && x.DeleteStatus == "N");
                    if (cur == null) return NotFoundResult("Store issue not found.");
                    if (!await CanEdit(cur.CreatedBy, dto.UserNo))
                        return ConflictResult("Only the user who made this issue, an ADMIN or a MANAGER can edit it.");
                    h = cur;
                    h.DocDate = dto.DocDate.Value.Date;
                    h.IndentorName = Trunc(dto.Indentor, 100);
                    h.Department = Trunc(dto.Department, 50);
                    h.DocNotes = Trunc(dto.DocNotes, 250);
                    h.CreatedBy = dto.UserNo;                    // the SP writes it to ModifiedBy
                }
                else
                {
                    var mrnType = MrnTypeOf(dto.TargetType);
                    if (mrnType == null) return BadRequestResult("Select the Issue Type (Production MRN, General MRN or Job Card).");
                    if (dto.TargetTransNo <= 0) return BadRequestResult("Select the MRN No.");
                    var src = await db.StoreIssueMRNLines.AsNoTracking()
                        .Where(l => l.MTransNo == dto.TargetTransNo && l.MRNType == mrnType && l.SubscID == dto.SubscID)
                        .Select(l => new { l.DocYear, l.DocNo, l.Requiredby, l.Department })
                        .FirstOrDefaultAsync();
                    if (src == null) return ConflictResult("The MRN is not approved, has been deleted, or is not a " + dto.TargetType + ".");
                    var hasBal = await db.StoreIssueMRNLines.AsNoTracking()
                        .AnyAsync(l => l.MTransNo == dto.TargetTransNo && l.BalQty > 0);
                    if (!hasBal) return ConflictResult("MRN " + src.DocYear + "-" + src.DocNo + " has been issued in full.");

                    h = new StoreIssueMst
                    {
                        MTransNo = 0,
                        DocSeries = dto.DocSeries.Trim(),
                        DocYear = docYear,
                        DocNo = dto.DocNo,
                        DocDate = dto.DocDate.Value.Date,
                        TargetTransNo = dto.TargetTransNo,
                        TargetDocYear = src.DocYear,
                        TargetDocNo = src.DocNo,
                        TargetAmdNo = 0,
                        TargetType = dto.TargetType!.Trim(),
                        IndentorName = Trunc(string.IsNullOrWhiteSpace(dto.Indentor) ? src.Requiredby : dto.Indentor, 100),
                        Department = Trunc(string.IsNullOrWhiteSpace(dto.Department) ? src.Department : dto.Department, 50),
                        DocNotes = Trunc(dto.DocNotes, 250),
                        DocStatus = "N",
                        LockStatus = "N",
                        CreatedBy = dto.UserNo,
                        SubscID = dto.SubscID
                    };
                }
                h.SystemIP = ClientIp is { Length: > 20 } ip ? ip[..20] : ClientIp;
                h.CreatedOn = DateTime.Now;
                h.DocStatus = string.IsNullOrWhiteSpace(h.DocStatus) ? "N" : h.DocStatus.Trim();

                var id = await db.CallStoreIssueMstSP(h);
                if (id <= 0) return ConflictResult("The store issue could not be saved.");
                var docNo = await db.StoreIssueMst.AsNoTracking().Where(x => x.MTransNo == id).Select(x => x.DocNo).FirstAsync();
                return OkResult(new { id, MTransNo = id, DocNo = docNo }, dto.MTransNo > 0 ? "Store issue updated." : "Store issue saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One issued line. Part, grade, unit and OA come off the MRN line
        /// (LinkNo); only Qty, remarks and the operation come from the page.
        /// </summary>
        [HttpPost("SaveDtlData")]
        public async Task<IActionResult> SaveDtlData([FromBody] StoreIssueLineDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0) return BadRequestResult("Save the issue header first.");
            if (dto.LinkNo <= 0) return BadRequestResult("The MRN line is missing.");
            if (dto.Qty <= 0) return BadRequestResult("Issue Qty must be above zero.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var h = await db.StoreIssueMst.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == dto.MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store issue not found.");
                if (!await CanEdit(h.CreatedBy, dto.UserNo))
                    return ConflictResult("Only the user who made this issue, an ADMIN or a MANAGER can edit it.");

                var srcType = MrnTypeOf(h.TargetType);
                var src = await db.StoreIssueMRNLines.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.LinkNo == dto.LinkNo && l.MTransNo == (h.TargetTransNo ?? 0) &&
                                              l.MRNType == srcType);
                if (src == null)
                    return ConflictResult("This line is not on an approved, live MRN " + h.TargetDocYear + "-" + h.TargetDocNo + ".");

                StoreIssueDtl? cur = null;
                if (dto.TransSlNo > 0)
                {
                    cur = await db.StoreIssueDtl.AsNoTracking().FirstOrDefaultAsync(d =>
                        d.TransSlNo == dto.TransSlNo && d.MTransNo == dto.MTransNo && d.DeleteStatus == "N");
                    if (cur == null) return NotFoundResult("Issue line not found.");
                    if (cur.LinkNo != dto.LinkNo) return ConflictResult("An issue line cannot be moved to another MRN line.");
                }
                else if (await db.StoreIssueDtl.AnyAsync(d => d.MTransNo == dto.MTransNo && d.LinkNo == dto.LinkNo && d.DeleteStatus == "N"))
                {
                    return ConflictResult(Label(src.PartCode, src.PartName) + " is already on this issue.");
                }

                var label = Label(src.PartCode, src.PartName);
                var ownOld = cur?.Qty ?? 0;
                var bal = src.BalQty + ownOld;
                if (dto.Qty > bal)
                    return ConflictResult($"Issue Qty {Qty(dto.Qty)} of {label} is above the " +
                        (srcType == "JC" ? "job card's raw material" : "MRN") + $" balance of {Qty(bal)} {src.Unit}.");

                if (cur != null)
                {
                    var ret = await db.StoreIssueLineBals.AsNoTracking()
                        .Where(b => b.LinkNo == cur.TransSlNo).Select(b => b.ReturnedQty).FirstOrDefaultAsync();
                    if (dto.Qty < ret)
                        return ConflictResult($"{Qty(ret)} {src.Unit} of {label} has already been returned against this line; Issue Qty cannot go below that.");
                }

                var stock = await StockOf(src.PartNo, src.Grade, h.SubscID) + ownOld;
                if (dto.Qty > stock)
                    return ConflictResult($"Only {Qty(stock)} {src.Unit} of {label}" +
                        (src.Grade.Length > 0 ? $" (grade {src.Grade})" : "") + " is in stock.");

                if (src.OAYear > 0 || src.OANo > 0 || src.OAID > 0)
                {
                    var oaMsg = await db.CheckOALinkAsync(src.OAYear, src.OANo, src.OAID, h.SubscID);
                    if (oaMsg != null) return ConflictResult(label + ": " + oaMsg);
                }

                var line = new StoreIssueDtl
                {
                    TransSlNo = cur?.TransSlNo ?? 0,
                    MTransNo = dto.MTransNo,
                    SlNo = dto.SlNo > 0 ? dto.SlNo : src.SlNo,
                    LinkNo = dto.LinkNo,
                    PartNo = src.PartNo,
                    PartCode = Trunc(src.PartCode, 30),
                    PartDescription = Trunc(src.PartName, 100),
                    GradeNo = src.GradeNo,
                    Grade = src.Grade,
                    OAYear = src.OAYear, OANo = src.OANo, OAID = src.OAID,
                    ItemRemarks = Trunc(dto.ItemRemarks, 500),
                    DCOpration = Trunc(dto.DcOperation, 150),
                    MRNQty = src.Qty,
                    Qty = dto.Qty,
                    Unit = Trunc(src.Unit, 20),
                    CreatedBy = dto.UserNo,
                    CreatedOn = DateTime.Now,
                    SubscID = h.SubscID
                };
                var id = await db.CallStoreIssueDtlSP(line);
                if (id <= 0) return ConflictResult(label + " could not be saved.");

                // Heat number off the bar tag (SQL 58): blank, or an approved
                // heat of this part — see api/HeatNo/Dtl/GetByPart.
                var plan = await db.CallStoreIssueDtlPlanFieldsSP(id,
                    Trunc(dto.HeatNo, 50).ToUpperInvariant(), dto.UserNo, h.SubscID);
                if (plan.ID == 0) return ConflictResult(label + ": " + plan.Message);

                await tx.CommitAsync();
                return OkResult(new { id, TransSlNo = id }, "Line saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>Marks the issue complete (LockStatus 'Y') once its lines are
        /// written — the legacy save's closing step.</summary>
        [HttpPost("Lock")]
        public async Task<IActionResult> Lock(int MTransNo, int UserNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            try
            {
                var h = await db.StoreIssueMst.FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store issue not found.");
                if (!await CanEdit(h.CreatedBy, UserNo))
                    return ConflictResult("Only the user who made this issue, an ADMIN or a MANAGER can lock it.");
                if (!await db.StoreIssueDtl.AnyAsync(d => d.MTransNo == MTransNo && d.DeleteStatus == "N" && d.Qty > 0))
                    return ConflictResult("The issue has no lines.");
                h.LockStatus = "Y";
                h.ModifiedBy = UserNo;
                h.ModifiedOn = DateTime.Now;
                await db.SaveChangesAsync();
                return OkResult(new { id = MTransNo, h.LockStatus }, "Store issue locked.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE (soft)
        // ============================================================

        [HttpPost("DtlDelete")]
        public async Task<IActionResult> DtlDelete(int TransSlNo, int UserNo)
        {
            if (TransSlNo <= 0) return BadRequestResult("Invalid line.");
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var d = await db.StoreIssueDtl.FirstOrDefaultAsync(x => x.TransSlNo == TransSlNo && x.DeleteStatus == "N");
                if (d == null) return OkResult("Delete", "Line already deleted.");
                var h = await db.StoreIssueMst.AsNoTracking().FirstOrDefaultAsync(x => x.MTransNo == d.MTransNo);
                if (h == null || !await CanEdit(h.CreatedBy, UserNo))
                    return ConflictResult("Only the user who made this issue, an ADMIN or a MANAGER can change it.");
                var ret = await db.StoreIssueLineBals.AsNoTracking()
                    .Where(b => b.LinkNo == TransSlNo).Select(b => b.ReturnedQty).FirstOrDefaultAsync();
                if (ret > 0)
                    return ConflictResult($"{Label(d.PartCode, d.PartDescription)}: {Qty(ret)} {d.Unit} has been returned against this line - delete the return first.");

                d.DeleteStatus = "Y";
                d.ModifiedBy = UserNo;
                d.ModifiedOn = DateTime.Now;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return OkResult("Delete", "Line deleted.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("RecDelete")]
        public async Task<IActionResult> RecDelete(int MTransNo, int UserNo, string? mReason)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            if (string.IsNullOrWhiteSpace(mReason)) return BadRequestResult("Enter the reason for deleting.");
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var h = await db.StoreIssueMst.FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store issue not found.");
                if (!await CanDelete(h.CreatedBy, h.LockStatus, UserNo))
                    return ConflictResult("A locked store issue can only be deleted by an ADMIN; an open one by the user who made it.");

                var returns = await db.StoreReturnsMst.AsNoTracking()
                    .Where(r => r.TargetTransNo == MTransNo && r.DeleteStatus == "N")
                    .Select(r => r.DocYear + "-" + r.DocNo)
                    .ToListAsync();
                if (returns.Count > 0)
                    return ConflictResult("Material has been returned against this issue (Return No " +
                        string.Join(", ", returns) + ") - delete the return(s) first.");

                var now = DateTime.Now;
                var lines = await db.StoreIssueDtl.Where(d => d.MTransNo == MTransNo && d.DeleteStatus == "N").ToListAsync();
                foreach (var d in lines)
                {
                    d.DeleteStatus = "Y";
                    d.ModifiedBy = UserNo;
                    d.ModifiedOn = now;
                }
                h.DeleteStatus = "Y";
                h.DeleteReason = Trunc(mReason, 250);
                h.ModifiedBy = UserNo;
                h.ModifiedOn = now;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return OkResult("Delete", "Store issue deleted.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
