using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class PurReqController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PurReqController(EMSContext context) => db = context;

        // ============================================================
        // MST — HEADER
        // ============================================================

        [HttpPost("Mst/SaveData")]
        public async Task<IActionResult> MstSaveData([FromBody] SP_PurReqMst dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.DocSeries))
                return BadRequestResult("Doc Series is required.");
            if (dto.DocYear == null || dto.DocYear <= 0)
                return BadRequestResult("Doc Year is required.");
            if (dto.DocDate == null)
                return BadRequestResult("Doc Date is required.");
            if (dto.DocAmdNo == null)
                return BadRequestResult("Doc Amendment No is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                var result = await db.CallPurReqMstSP(dto);
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
                var result = await db.CallPurReqMstSP(new SP_PurReqMst
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

        /// <summary>
        /// Lock approves the requisition (LockStatus 'Y') — the gate Request For
        /// Quote and Purchase Order read, so nothing can be raised against a
        /// requisition until it is approved. Unlock takes it back to draft, but
        /// only while nothing has been raised from it yet.
        /// </summary>
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
                var pr = await db.PurReqMst.AsNoTracking()
                    .Where(x => x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => new { x.DocAmdStatus })
                    .FirstOrDefaultAsync();
                if (pr == null) return NotFoundResult();

                if (action == "Lock")
                {
                    if (pr.DocAmdStatus != "N")
                        return ConflictResult("Only the current revision of a requisition can be approved.");
                    if (!await db.PurReqDtl.AnyAsync(x => x.MTransNo == id && x.DeleteStatus == "N"))
                        return ConflictResult("Add at least one item before approving the requisition.");
                }
                else
                {
                    if (await db.RFQMst.AnyAsync(x => x.PRTransNo == id && x.DeleteStatus == "N"))
                        return ConflictResult("An RFQ has already been raised against this requisition. Delete it before un-approving.");
                    if (await db.POMst.AnyAsync(x => x.TargetType == "PR" && x.TargetTransNo == id && x.DeleteStatus == "N"))
                        return ConflictResult("A purchase order has already been raised against this requisition. Delete it before un-approving.");
                }

                var result = await db.CallPurReqMstSP(new SP_PurReqMst
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
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.PurReqMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.ReqBy!.Contains(search)  ||
                        x.ReqDept!.Contains(search) ||
                        x.Project!.Contains(search) ||
                        x.DocNotes!.Contains(search));

                var data = await query
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .ThenByDescending(x => x.DocAmdNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo,
                        x.DocDate, x.DocAmdNo, x.DocAmdStatus,
                        x.ReqBy, x.ReqDept, x.Project, x.DueDate,
                        x.DocStatus, x.RFQReq, x.LockStatus, x.LockCounter,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// The number the next first issue in a series + year would get — for
        /// display only. SP_PurReqMst allocates the real DocNo itself at insert.
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
                var max = await db.PurReqMst
                    .AsNoTracking()
                    .Where(x => x.DocSeries == series && x.DocYear == docYear && x.SubscID == subscID)
                    .MaxAsync(x => (int?)x.DocNo) ?? 0;

                return OkResult(max + 1);
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
                var data = await db.PurReqMst
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo == id && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Requisitions an RFQ can still be raised against — feeds the PR picker
        /// on Request For Quote. "Open" means the current revision of a
        /// requisition that asked for an RFQ and has not been processed yet.
        /// SP_PurReqMst stamps the newest revision DocAmdStatus = 'N' and demotes
        /// the ones it supersedes to 'Y', so 'N' is the row to offer.
        /// </summary>
        [HttpGet("Mst/GetOpenList/{subscID}")]
        public async Task<IActionResult> MstGetOpenList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PurReqMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.DocAmdStatus == "N" &&
                                x.RFQReq == "Y" &&
                                x.DocStatus == "N")
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo,
                        x.DocDate, x.DocAmdNo, x.ReqBy, x.ReqDept,
                        x.Project, x.DueDate, x.DocNotes, x.LockStatus
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Approved requisitions — feeds the PR picker on RFQ Price Update. A
        /// requisition counts as approved once it is locked (LockStatus = 'Y').
        /// </summary>
        [HttpGet("Mst/GetApprovedList/{subscID}")]
        public async Task<IActionResult> MstGetApprovedList(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PurReqMst
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.DocAmdStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderByDescending(x => x.DocYear)
                    .ThenByDescending(x => x.DocNo)
                    .Select(x => new
                    {
                        x.MTransNo, x.DocSeries, x.DocYear, x.DocNo,
                        x.DocDate, x.DocAmdNo, x.ReqBy, x.ReqDept,
                        x.Project, x.DueDate, x.DocNotes, x.DocStatus
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DTL — DETAIL LINES
        // ============================================================

        [HttpPost("Dtl/SaveData")]
        public async Task<IActionResult> DtlSaveData([FromBody] SP_PurReqDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid Purchase Requisition.");
            if (dto.PartNo == null || dto.PartNo <= 0)
                return BadRequestResult("Part No is required.");
            if (dto.Qty == null || dto.Qty <= 0)
                return BadRequestResult("Quantity is required.");
            if (string.IsNullOrWhiteSpace(dto.Unit))
                return BadRequestResult("Unit is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;
                await using var tx = await db.Database.BeginTransactionAsync();
                var result = await db.CallPurReqDtlSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);

                // Line required-by date (SQL 58); null = the header due date.
                var plan = await db.CallPurReqDtlPlanFieldsSP(result.ID, dto.RequiredBy,
                    dto.ModifiedBy ?? dto.CreatedBy ?? 0, dto.SubscID ?? 0);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                await tx.CommitAsync();
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
                var result = await db.CallPurReqDtlSP(new SP_PurReqDtl
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
                var result = await db.CallPurReqDtlSP(new SP_PurReqDtl
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
            if (mTransNo <= 0) return BadRequestResult("Invalid Purchase Requisition.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.PurReqDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo, x.PartNo,
                        x.ItemRemarks, x.Qty, x.Unit,
                        x.OAYear, x.OANo, x.OAID,
                        x.GradeNo, x.Grade, x.RequiredBy,
                        x.CreatedBy, x.CreatedOn, x.ModifiedBy, x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Requisition lines with the quantity still to be quoted — feeds the
        /// item grid on Request For Quote. Balance is the requisitioned quantity
        /// less everything already carried on live RFQs raised against this
        /// requisition, summed per part. Lines with nothing left are dropped.
        /// </summary>
        [HttpGet("Dtl/GetBalanceItems/{mTransNo}/{subscID}")]
        public async Task<IActionResult> DtlGetBalanceItems(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid Purchase Requisition.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                // Part code / name come from PartMaster — the picker shows them.
                var lines = await (
                    from x in db.PurReqDtl.AsNoTracking()
                    join p in db.PartMaster.AsNoTracking() on x.PartNo equals p.MTransNo into pj
                    from p in pj.DefaultIfEmpty()
                    where x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N"
                    orderby x.SlNo
                    select new
                    {
                        x.TransSlNo, x.SlNo, x.MTransNo, x.PartNo,
                        PartCode = p != null ? p.PartCode : null,
                        PartName = p != null ? p.PartName : null,
                        x.ItemRemarks, x.Qty, x.Unit,
                        x.GradeNo, x.Grade
                    })
                    .ToListAsync();

                // Everything already asked for on an RFQ against this PR. Only
                // current revisions count — a superseded RFQ no longer holds qty.
                var quoted = await (
                    from d in db.RFQDtl.AsNoTracking()
                    join m in db.RFQMst.AsNoTracking() on d.MTransNo equals m.MTransNo
                    where m.PRTransNo == mTransNo &&
                          m.SubscID == subscID &&
                          m.DeleteStatus == "N" &&
                          m.DocAmdStatus == "N" &&
                          d.DeleteStatus == "N"
                    group d by d.PartNo into g
                    select new { PartNo = g.Key, Qty = g.Sum(x => x.Qty) }
                ).ToListAsync();

                // Quoted quantity is summed per part, but a requisition may list
                // the same part on more than one line. Draw each line's share
                // from a per-part pool so the total is only ever deducted once.
                var pool = quoted.ToDictionary(x => x.PartNo, x => x.Qty);

                var data = lines
                    .Select(x =>
                    {
                        pool.TryGetValue(x.PartNo, out var available);
                        var qty = x.Qty ?? 0m;
                        var taken = Math.Min(available, qty);
                        pool[x.PartNo] = available - taken;

                        return new
                        {
                            x.TransSlNo, x.SlNo, x.MTransNo, x.PartNo,
                            x.PartCode, x.PartName,
                            x.ItemRemarks, x.Qty, x.Unit,
                            x.GradeNo, x.Grade,
                            PRQty = qty,
                            QuotedQty = taken,
                            BalanceQty = qty - taken,
                            BalQty = qty - taken
                        };
                    })
                    .Where(x => x.BalanceQty > 0m)
                    .ToList();

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
                var data = await db.PurReqDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.TransSlNo == transSlNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
