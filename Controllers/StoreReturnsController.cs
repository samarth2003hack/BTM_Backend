using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Store Returns (Issue Return) — material coming back to stores against
    /// a locked store issue. StoreReturnsMst.Target* → StoreIssueMst;
    /// StoreReturnsDtl.LinkNo → StoreIssueDtl.TransSlNo.
    ///
    ///     GetMasterList / GetMasterData / GetDetailsList   register, header, lines
    ///     GetIssueNo / GetIssueMst / GetIssueDtl           locked issues with balance
    ///     GetMaxNo / DocNoExists                           numbering
    ///     SaveMasterData / SaveDtlData                     header, then one line per call
    ///     Lock                                             return complete → LockStatus 'Y'
    ///     DtlDelete / RecDelete                            soft deletes
    ///
    /// Server-side rules: a line may not bring back more than was issued less
    /// what is already returned; and reducing or deleting a return may not
    /// leave the part's stock negative (the returned material has been
    /// issued again since).
    /// </summary>
    [Route("api/[controller]")]
    public class StoreReturnsController : StoreChainControllerBase
    {
        public StoreReturnsController(EMSContext context) : base(context) { }

        // ============================================================
        // REGISTER / READ
        // ============================================================

        [HttpGet("GetMasterList")]
        public async Task<IActionResult> GetMasterList(int UserNo = 0, string? UserType = null, int SubscID = 0)
        {
            if (UserNo <= 0) return BadRequestResult("Invalid User.");
            try
            {
                var seeAll = EditAllUserTypes.Contains(await UserTypeOf(UserNo));
                var q = db.StoreReturnsMst.AsNoTracking().Where(m => m.DeleteStatus == "N");
                if (SubscID > 0) q = q.Where(m => m.SubscID == SubscID);
                if (!seeAll) q = q.Where(m => m.CreatedBy == UserNo || m.LockStatus == "Y");

                var data = await q
                    .OrderByDescending(m => m.MTransNo)
                    .Select(m => new
                    {
                        m.MTransNo, m.DocSeries, m.DocYear, m.DocNo, m.DocDate,
                        m.TargetTransNo, m.TargetDocYear, m.TargetDocNo,
                        TargetDocDate = db.StoreIssueMst.Where(i => i.MTransNo == m.TargetTransNo).Select(i => (DateTime?)i.DocDate).FirstOrDefault(),
                        m.DocNotes, m.LockStatus, m.CreatedBy, m.CreatedOn,
                        LineCount = db.StoreReturnsDtl.Count(d => d.MTransNo == m.MTransNo && d.DeleteStatus == "N"),
                        TotalQty = db.StoreReturnsDtl.Where(d => d.MTransNo == m.MTransNo && d.DeleteStatus == "N")
                                                     .Sum(d => (decimal?)d.RTNQty) ?? 0
                    })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetMasterData")]
        public async Task<IActionResult> GetMasterData(int MTransNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Return.");
            try
            {
                var m = await db.StoreReturnsMst.AsNoTracking()
                    .Where(x => x.MTransNo == MTransNo && x.DeleteStatus == "N")
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.TargetTransNo, x.TargetDocYear, x.TargetDocNo,
                        TargetDocDate = db.StoreIssueMst.Where(i => i.MTransNo == x.TargetTransNo).Select(i => (DateTime?)i.DocDate).FirstOrDefault(),
                        TargetType = db.StoreIssueMst.Where(i => i.MTransNo == x.TargetTransNo).Select(i => i.TargetType).FirstOrDefault(),
                        x.DocNotes, x.LockStatus, x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn, x.SubscID,
                        LineCount = db.StoreReturnsDtl.Count(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N"),
                        TotalQty = db.StoreReturnsDtl.Where(d => d.MTransNo == x.MTransNo && d.DeleteStatus == "N")
                                                     .Sum(d => (decimal?)d.RTNQty) ?? 0
                    })
                    .FirstOrDefaultAsync();
                return m == null ? NotFoundResult("Store return not found.") : OkResult(m, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The return's own lines joined onto its issue's lines; BalQty
        /// leaves out this return's own quantity.</summary>
        [HttpGet("GetDetailsList")]
        public async Task<IActionResult> GetDetailsList(int MTransNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Return.");
            try
            {
                var h = await db.StoreReturnsMst.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store return not found.");

                var own = await db.StoreReturnsDtl.AsNoTracking()
                    .Where(d => d.MTransNo == MTransNo && d.DeleteStatus == "N")
                    .OrderBy(d => d.SlNo).ThenBy(d => d.TransSlNo)
                    .ToListAsync();
                var issue = await db.StoreIssueLineBals.AsNoTracking()
                    .Where(l => l.MTransNo == (h.TargetTransNo ?? 0))
                    .OrderBy(l => l.SlNo).ThenBy(l => l.LinkNo)
                    .ToListAsync();

                var rows = new List<object>();
                foreach (var l in issue)
                {
                    var o = own.FirstOrDefault(d => d.LinkNo == l.LinkNo);
                    var ownQty = o?.RTNQty ?? 0;
                    var bal = l.BalQty + ownQty;
                    if (bal <= 0 && o == null) continue;
                    rows.Add(new
                    {
                        TransSlNo = o?.TransSlNo ?? 0,
                        l.LinkNo, l.PartNo, l.PartCode, l.PartName, l.GradeNo, l.Grade,
                        l.OAYear, l.OANo, OaID = l.OAID,
                        IssueQty = l.Qty, BalQty = bal, RtnQty = ownQty,
                        l.Unit, ItemRemarks = o?.ItemRemarks ?? ""
                    });
                }
                foreach (var o in own.Where(d => !issue.Any(l => l.LinkNo == d.LinkNo)))
                {
                    rows.Add(new
                    {
                        o.TransSlNo, LinkNo = o.LinkNo ?? 0, o.PartNo, o.PartCode, PartName = o.PartDescription,
                        o.GradeNo, Grade = o.Grade ?? "", o.OAYear, o.OANo, OaID = o.OAID,
                        IssueQty = o.IssueQty, BalQty = o.RTNQty, RtnQty = o.RTNQty,
                        o.Unit, ItemRemarks = o.ItemRemarks ?? ""
                    });
                }
                return OkResult(rows, rows.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // SOURCE — locked store issues with something not yet returned
        // ============================================================

        [HttpGet("GetIssueNo")]
        public async Task<IActionResult> GetIssueNo(int SubscID)
        {
            if (SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            try
            {
                var data = await db.StoreIssueLineBals.AsNoTracking()
                    .Where(l => l.SubscID == SubscID && l.LockStatus == "Y" && l.BalQty > 0)
                    .GroupBy(l => new { l.MTransNo, l.DocSeries, l.DocYear, l.DocNo, l.DocDate, l.TargetType, l.IndentorName })
                    .Select(g => new
                    {
                        g.Key.MTransNo, g.Key.DocSeries, g.Key.DocYear, g.Key.DocNo, g.Key.DocDate,
                        g.Key.TargetType, Indentor = g.Key.IndentorName,
                        PendingLines = g.Count(), BalQty = g.Sum(x => x.BalQty)
                    })
                    .OrderByDescending(x => x.DocYear).ThenByDescending(x => x.DocNo)
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetIssueMst")]
        public async Task<IActionResult> GetIssueMst(int TransNo)
        {
            if (TransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            try
            {
                var m = await db.StoreIssueMst.AsNoTracking()
                    .Where(x => x.MTransNo == TransNo && x.DeleteStatus == "N" && x.LockStatus == "Y")
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo, x.DocDate,
                        x.TargetType, x.TargetDocYear, x.TargetDocNo,
                        Indentor = x.IndentorName, x.Department, x.SubscID
                    })
                    .FirstOrDefaultAsync();
                return m == null ? NotFoundResult("Store issue not found, deleted, or not yet locked.") : OkResult(m, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("GetIssueDtl")]
        public async Task<IActionResult> GetIssueDtl(int TransNo)
        {
            if (TransNo <= 0) return BadRequestResult("Invalid Store Issue.");
            try
            {
                var data = await db.StoreIssueLineBals.AsNoTracking()
                    .Where(l => l.MTransNo == TransNo && l.LockStatus == "Y" && l.BalQty > 0)
                    .OrderBy(l => l.SlNo).ThenBy(l => l.LinkNo)
                    .Select(l => new
                    {
                        TransSlNo = 0, l.LinkNo, l.PartNo, l.PartCode, l.PartName, l.GradeNo, l.Grade,
                        l.OAYear, l.OANo, OaID = l.OAID,
                        l.Qty, IssueQty = l.Qty, l.ReturnedQty, l.BalQty, RtnQty = 0m, l.Unit
                    })
                    .ToListAsync();
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
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
                var max = await db.StoreReturnsMst.AsNoTracking()
                    .Where(x => x.DeleteStatus == "N" && x.DocSeries == s && x.DocYear == mYear && x.SubscID == SubscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;
                return OkResult(max + 1, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpGet("DocNoExists")]
        public async Task<IActionResult> DocNoExists(string? DocSeries, int DocNo, int MTransNo, int SubscID)
        {
            try
            {
                var s = (DocSeries ?? "").Trim();
                var y = YearFromSeries(s);
                var clash = await db.StoreReturnsMst.AsNoTracking().AnyAsync(x =>
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
        public async Task<IActionResult> SaveMasterData([FromBody] StoreReturnsHeaderDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries)) return BadRequestResult("Select the Series.");
            var docYear = dto.DocYear > 0 ? dto.DocYear : YearFromSeries(dto.DocSeries);
            if (docYear <= 0) return BadRequestResult("Doc Year could not be read from the series.");
            if (dto.DocDate == null) return BadRequestResult("Returns Date is required.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");
            if (dto.SubscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                StoreReturnsMst h;
                if (dto.MTransNo > 0)
                {
                    var cur = await db.StoreReturnsMst.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.MTransNo == dto.MTransNo && x.DeleteStatus == "N");
                    if (cur == null) return NotFoundResult("Store return not found.");
                    if (!await CanEdit(cur.CreatedBy, dto.UserNo))
                        return ConflictResult("Only the user who made this return, an ADMIN or a MANAGER can edit it.");
                    h = cur;
                    h.DocDate = dto.DocDate.Value.Date;
                    h.DocNotes = Trunc(dto.DocNotes, 250);
                    h.CreatedBy = dto.UserNo;                    // the SP writes it to ModifiedBy
                }
                else
                {
                    if (dto.TargetTransNo <= 0) return BadRequestResult("Select the Issue No.");
                    var issue = await db.StoreIssueMst.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.MTransNo == dto.TargetTransNo && x.DeleteStatus == "N" && x.SubscID == dto.SubscID);
                    if (issue == null) return ConflictResult("The store issue has been deleted.");
                    if (issue.LockStatus != "Y") return ConflictResult("Store issue " + issue.DocYear + "-" + issue.DocNo + " is not locked yet.");
                    if (!await db.StoreIssueLineBals.AnyAsync(l => l.MTransNo == issue.MTransNo && l.BalQty > 0))
                        return ConflictResult("Store issue " + issue.DocYear + "-" + issue.DocNo + " has been returned in full.");

                    h = new StoreReturnsMst
                    {
                        MTransNo = 0,
                        DocSeries = dto.DocSeries.Trim(),
                        DocYear = docYear,
                        DocNo = dto.DocNo,
                        DocDate = dto.DocDate.Value.Date,
                        TargetTransNo = issue.MTransNo,
                        TargetDocYear = issue.DocYear,
                        TargetDocNo = issue.DocNo,
                        TargetAmdNo = 0,
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

                var id = await db.CallStoreReturnsMstSP(h);
                if (id <= 0) return ConflictResult("The store return could not be saved.");
                var docNo = await db.StoreReturnsMst.AsNoTracking().Where(x => x.MTransNo == id).Select(x => x.DocNo).FirstAsync();
                return OkResult(new { id, MTransNo = id, DocNo = docNo }, dto.MTransNo > 0 ? "Store return updated." : "Store return saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("SaveDtlData")]
        public async Task<IActionResult> SaveDtlData([FromBody] StoreReturnsLineDto dto)
        {
            if (dto == null) return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0) return BadRequestResult("Save the return header first.");
            if (dto.LinkNo <= 0) return BadRequestResult("The issue line is missing.");
            if (dto.Qty <= 0) return BadRequestResult("Return Qty must be above zero.");
            if (dto.UserNo <= 0) return BadRequestResult("Invalid User.");

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var h = await db.StoreReturnsMst.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == dto.MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store return not found.");
                if (!await CanEdit(h.CreatedBy, dto.UserNo))
                    return ConflictResult("Only the user who made this return, an ADMIN or a MANAGER can edit it.");

                var src = await db.StoreIssueLineBals.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.LinkNo == dto.LinkNo && l.MTransNo == (h.TargetTransNo ?? 0));
                if (src == null)
                    return ConflictResult("This line is not on live store issue " + h.TargetDocYear + "-" + h.TargetDocNo + ".");

                StoreReturnsDtl? cur = null;
                if (dto.TransSlNo > 0)
                {
                    cur = await db.StoreReturnsDtl.AsNoTracking().FirstOrDefaultAsync(d =>
                        d.TransSlNo == dto.TransSlNo && d.MTransNo == dto.MTransNo && d.DeleteStatus == "N");
                    if (cur == null) return NotFoundResult("Return line not found.");
                    if (cur.LinkNo != dto.LinkNo) return ConflictResult("A return line cannot be moved to another issue line.");
                }
                else if (await db.StoreReturnsDtl.AnyAsync(d => d.MTransNo == dto.MTransNo && d.LinkNo == dto.LinkNo && d.DeleteStatus == "N"))
                {
                    return ConflictResult(Label(src.PartCode, src.PartName) + " is already on this return.");
                }

                var label = Label(src.PartCode, src.PartName);
                var ownOld = cur?.RTNQty ?? 0;
                var bal = src.BalQty + ownOld;
                if (dto.Qty > bal)
                    return ConflictResult($"Return Qty {Qty(dto.Qty)} of {label} is above the {Qty(bal)} {src.Unit} issued and not yet returned.");

                // Bringing back less than before takes stock out again.
                if (dto.Qty < ownOld)
                {
                    var stock = await StockOf(src.PartNo, src.Grade, h.SubscID);
                    if (stock - (ownOld - dto.Qty) < 0)
                        return ConflictResult($"Only {Qty(stock)} {src.Unit} of {label} is still in stock - the returned material has been issued again.");
                }

                if (src.OAYear > 0 || src.OANo > 0 || src.OAID > 0)
                {
                    var oaMsg = await db.CheckOALinkAsync(src.OAYear, src.OANo, src.OAID, h.SubscID);
                    if (oaMsg != null) return ConflictResult(label + ": " + oaMsg);
                }

                var line = new StoreReturnsDtl
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
                    IssueQty = src.Qty,
                    RTNQty = dto.Qty,
                    Unit = Trunc(src.Unit, 20),
                    CreatedBy = dto.UserNo,
                    CreatedOn = DateTime.Now,
                    SubscID = h.SubscID
                };
                var id = await db.CallStoreReturnsDtlSP(line);
                if (id <= 0) return ConflictResult(label + " could not be saved.");
                await tx.CommitAsync();
                return OkResult(new { id, TransSlNo = id }, "Line saved.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Lock")]
        public async Task<IActionResult> Lock(int MTransNo, int UserNo)
        {
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Return.");
            try
            {
                var h = await db.StoreReturnsMst.FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store return not found.");
                if (!await CanEdit(h.CreatedBy, UserNo))
                    return ConflictResult("Only the user who made this return, an ADMIN or a MANAGER can lock it.");
                if (!await db.StoreReturnsDtl.AnyAsync(d => d.MTransNo == MTransNo && d.DeleteStatus == "N" && d.RTNQty > 0))
                    return ConflictResult("The return has no lines.");
                h.LockStatus = "Y";
                h.ModifiedBy = UserNo;
                h.ModifiedOn = DateTime.Now;
                await db.SaveChangesAsync();
                return OkResult(new { id = MTransNo, h.LockStatus }, "Store return locked.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE (soft)
        // ============================================================

        /// <summary>Refuses when taking these return quantities back out of
        /// stock would leave a part / grade negative.</summary>
        private async Task<string?> StockBlocksRemoval(IEnumerable<StoreReturnsDtl> lines, int subscID)
        {
            foreach (var g in lines.GroupBy(l => new { l.PartNo, Grade = NormGrade(l.Grade) }))
            {
                var qty = g.Sum(l => l.RTNQty);
                var stock = await StockOf(g.Key.PartNo, g.Key.Grade, subscID);
                if (stock - qty < 0)
                {
                    var f = g.First();
                    return $"Only {Qty(stock)} {f.Unit} of {Label(f.PartCode, f.PartDescription)} is still in stock - " +
                           "the returned material has been issued again, so the return cannot be removed.";
                }
            }
            return null;
        }

        [HttpPost("DtlDelete")]
        public async Task<IActionResult> DtlDelete(int TransSlNo, int UserNo)
        {
            if (TransSlNo <= 0) return BadRequestResult("Invalid line.");
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var d = await db.StoreReturnsDtl.FirstOrDefaultAsync(x => x.TransSlNo == TransSlNo && x.DeleteStatus == "N");
                if (d == null) return OkResult("Delete", "Line already deleted.");
                var h = await db.StoreReturnsMst.AsNoTracking().FirstOrDefaultAsync(x => x.MTransNo == d.MTransNo);
                if (h == null || !await CanEdit(h.CreatedBy, UserNo))
                    return ConflictResult("Only the user who made this return, an ADMIN or a MANAGER can change it.");
                var block = await StockBlocksRemoval(new[] { d }, h.SubscID);
                if (block != null) return ConflictResult(block);

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
            if (MTransNo <= 0) return BadRequestResult("Invalid Store Return.");
            if (string.IsNullOrWhiteSpace(mReason)) return BadRequestResult("Enter the reason for deleting.");
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.LockStoreChainAsync();

                var h = await db.StoreReturnsMst.FirstOrDefaultAsync(x => x.MTransNo == MTransNo && x.DeleteStatus == "N");
                if (h == null) return NotFoundResult("Store return not found.");
                if (!await CanDelete(h.CreatedBy, h.LockStatus, UserNo))
                    return ConflictResult("A locked store return can only be deleted by an ADMIN; an open one by the user who made it.");

                var lines = await db.StoreReturnsDtl.Where(d => d.MTransNo == MTransNo && d.DeleteStatus == "N").ToListAsync();
                var block = await StockBlocksRemoval(lines, h.SubscID);
                if (block != null) return ConflictResult(block);

                var now = DateTime.Now;
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
                return OkResult("Delete", "Store return deleted.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
