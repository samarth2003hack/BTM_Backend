using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Store allocation — putting received material away into a bin, and the
    /// bin master behind it.
    ///
    /// Allocation writes GRNDtl.Store and GRNDtl.Location and nothing else, so
    /// it can run while the same GRN is being inspected. It is deliberately
    /// not gated on inspection: goods have to go somewhere the moment they are
    /// off the lorry, and the inspector needs to be told where to find them.
    ///
    /// A bin must exist on StoreLocation before anything can be put in it —
    /// that is what stops "MAIN"/"MIAN"/"Main Store" becoming three places.
    /// A StoreLocation row with PartNo 0 is a general bin; a row naming a part
    /// is that part's own, offered first.
    ///
    ///     Worklist/{subscID}            GRNs with lines not yet put away
    ///     Lines/GetList/{grn}           the lines, with their bin if any
    ///     Register/GetList|GetData      GRNStore.html's list / picker / header
    ///     SaveLines                     several lines at once, all or nothing
    ///     Allocate                      one line
    ///     AllocateAll/{grn}             every unallocated line at once
    ///     Clear/{transSlNo}             take a line back off the shelf
    ///     Master/*                      the bin master
    /// </summary>
    [Route("api/[controller]")]
    public class StoreAllocationController : EmsControllerBase
    {
        private readonly EMSContext db;

        public StoreAllocationController(EMSContext context) => db = context;

        // ============================================================
        // WORKLIST AND LINES
        // ============================================================

        /// <summary>
        /// GRNs with at least one line still unallocated. Confirmed receipts
        /// only unless includeDraft is set.
        /// </summary>
        [HttpGet("Worklist/{subscID}")]
        public async Task<IActionResult> Worklist(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] bool includeDraft = false,
            [FromQuery] bool includeDone = false)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNInwardStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.LineCount > 0);

                if (!includeDone)
                    query = query.Where(x => x.UnallocatedLines > 0);
                if (!includeDraft)
                    query = query.Where(x => x.LockStatus == "Y");
                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);

                var data = await query
                    .OrderBy(x => x.DocDate).ThenBy(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "Everything received has been put away.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// One GRN's lines with their current bin, and the bins suggested for
        /// each part — the part's own registered bins first, then the general
        /// ones.
        /// </summary>
        [HttpGet("Lines/GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> LinesGetList(
            int mTransNo,
            int subscID,
            [FromQuery] bool onlyPending = false)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo && x.SubscID == subscID && x.DeleteStatus == "N");

                if (onlyPending)
                    query = query.Where(x => x.Store == null || x.Store == "");

                var lines = await query
                    .OrderBy(x => x.SlNo)
                    .Select(x => new
                    {
                        x.TransSlNo, x.SlNo, x.PartNo, x.PartCode, x.PartDescription,
                        x.Grade, x.Unit, x.BillQty, x.RecQty, x.AccQty, x.AODQty,
                        x.Store, x.Location, x.BarCode, x.ItemRemarks,
                        x.OAYear, x.OANo, x.OAID
                    })
                    .ToListAsync();

                if (lines.Count == 0)
                    return OkResult(lines, "No records found.");

                var partNos = lines.Select(l => l.PartNo).Distinct().ToList();

                var bins = await db.StoreLocation
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N"
                             && (x.PartNo == 0 || partNos.Contains(x.PartNo)))
                    .Select(x => new { x.MTransNo, x.PartNo, x.Store, x.Location })
                    .ToListAsync();

                var data = lines.Select(l => new
                {
                    l.TransSlNo, l.SlNo, l.PartNo, l.PartCode, l.PartDescription,
                    l.Grade, l.Unit, l.BillQty, l.RecQty, l.AccQty, l.AODQty,
                    l.Store, l.Location, l.BarCode, l.ItemRemarks,
                    l.OAYear, l.OANo, l.OAID,
                    Allocated = !string.IsNullOrWhiteSpace(l.Store),
                    // The part's own bins first: they are the right answer far
                    // more often than a general bin is.
                    SuggestedBins = bins
                        .Where(b => b.PartNo == l.PartNo || b.PartNo == 0)
                        .OrderByDescending(b => b.PartNo == l.PartNo)
                        .ThenBy(b => b.Store).ThenBy(b => b.Location)
                        .Select(b => new { b.Store, b.Location, ForThisPart = b.PartNo == l.PartNo })
                        .ToList()
                }).ToList();

                return OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // PAGE REGISTER (GRNStore.html) — VW_GRNStoreAllocation
        // ============================================================

        /// <summary>
        /// GRNs as the store-allocation page lists them, counted over the
        /// accepted lines only (AccQty &gt; 0).
        ///
        ///     view=allocated  at least one accepted line has a store (the list)
        ///     view=pending    at least one accepted line still has none (the picker)
        ///     view=all        every GRN with an accepted line
        ///
        /// inspectedOnly (default true) keeps it to GRNType 'Q' / 'C': until
        /// inspection records a verdict AccQty is 0, so an uninspected receipt
        /// has nothing accepted to put away. The Allocate endpoints themselves
        /// stay ungated.
        /// </summary>
        [HttpGet("Register/GetList/{subscID}")]
        public async Task<IActionResult> RegisterGetList(
            int subscID,
            [FromQuery] string view = "all",
            [FromQuery] bool inspectedOnly = true,
            [FromQuery] int? docYear = null,
            [FromQuery] int? vendorNo = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.GRNStoreAllocationStatus
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.LineCount > 0);

                switch ((view ?? "all").Trim().ToLowerInvariant())
                {
                    case "allocated": query = query.Where(x => x.AllocatedCount > 0); break;
                    case "pending":   query = query.Where(x => x.PendingCount > 0); break;
                    case "all":       break;
                    default:          return BadRequestResult("view must be allocated, pending or all.");
                }

                if (inspectedOnly)
                    query = query.Where(x => x.GRNType == "Q" || x.GRNType == "C");
                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);

                var data = await query
                    .OrderBy(x => x.DocYear).ThenBy(x => x.DocNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>One GRN's header, inspection stamp and allocation counts.</summary>
        [HttpGet("Register/GetData/{mTransNo}/{subscID}")]
        public async Task<IActionResult> RegisterGetData(int mTransNo, int subscID)
        {
            if (mTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GRNStoreAllocationStatus
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == mTransNo && x.SubscID == subscID);

                return data == null ? NotFoundResult("GRN not found.") : OkResult(data, "Data found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Save several lines' bins in one go, all or nothing — what the page's
        /// Submit does. Each line runs through SP_GRNStoreAlloc ('Allocate', or
        /// 'Clear' when its Store is blank), so every rule the procedure
        /// enforces still holds; the first refusal rolls the whole batch back
        /// and comes back as a 409 naming the line.
        /// </summary>
        [HttpPost("SaveLines")]
        public async Task<IActionResult> SaveLines([FromBody] StoreAllocationSaveRequest req)
        {
            if (req == null) return BadRequestResult("Invalid request.");
            if (req.MTransNo <= 0) return BadRequestResult("Invalid GRN.");
            if (req.SubscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (req.UserNo <= 0) return BadRequestResult("Invalid User.");
            if (req.Lines == null || req.Lines.Count == 0) return BadRequestResult("No lines to save.");
            if (req.Lines.Any(l => l == null || l.TransSlNo <= 0)) return BadRequestResult("Invalid receipt line.");

            // A line sent twice keeps its last value.
            var lines = req.Lines
                .GroupBy(l => l.TransSlNo)
                .Select(g => g.Last())
                .ToList();

            var ids = lines.Select(l => l.TransSlNo).ToList();
            var slNos = await db.GRNDtl.AsNoTracking()
                .Where(x => ids.Contains(x.TransSlNo) && x.MTransNo == req.MTransNo && x.SubscID == req.SubscID)
                .Select(x => new { x.TransSlNo, x.SlNo })
                .ToDictionaryAsync(x => x.TransSlNo, x => x.SlNo);

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;
                int allocated = 0, cleared = 0;

                foreach (var line in lines)
                {
                    var clear = string.IsNullOrWhiteSpace(line.Store);
                    var result = await db.CallGRNStoreAllocSP(new SP_GRNStoreAlloc
                    {
                        TransSlNo  = line.TransSlNo,
                        MTransNo   = req.MTransNo,
                        Store      = clear ? null : line.Store!.Trim(),
                        Location   = clear || string.IsNullOrWhiteSpace(line.Location) ? null : line.Location.Trim(),
                        ModifiedBy = req.UserNo,
                        ModifiedOn = now,
                        SubscID    = req.SubscID,
                        SystemIP   = ClientIp,
                        Status     = clear ? "Clear" : "Allocate"
                    });

                    if (result.ID == 0)
                    {
                        await tx.RollbackAsync();
                        var label = slNos.TryGetValue(line.TransSlNo, out var sl) && sl != null
                            ? $"Line {sl:0.##}: " : "";
                        return ConflictResult(label + result.Message);
                    }

                    if (clear) cleared++; else allocated++;
                }

                await tx.CommitAsync();

                var msg = $"{allocated} line(s) allocated" + (cleared > 0 ? $", {cleared} cleared." : ".");
                return OkResult(new { ID = req.MTransNo, Allocated = allocated, Cleared = cleared }, msg);
            }
            catch (Exception ex)
            {
                // The procedure's own CATCH may already have rolled everything back.
                try { await tx.RollbackAsync(); } catch { }
                return ErrorResult(ex);
            }
        }

        // ============================================================
        // ALLOCATION
        // ============================================================

        [HttpPost("Allocate")]
        public async Task<IActionResult> Allocate([FromBody] SP_GRNStoreAlloc dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.TransSlNo <= 0)
                return BadRequestResult("Invalid receipt line.");
            if (dto.MTransNo <= 0)
                return BadRequestResult("Invalid GRN.");
            if (string.IsNullOrWhiteSpace(dto.Store))
                return BadRequestResult("Store is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.ModifiedBy == null || dto.ModifiedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP   = ClientIp;
                dto.ModifiedOn = DateTime.Now;
                dto.Status     = "Allocate";
                var result = await db.CallGRNStoreAllocSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>
        /// Put every unallocated line of a GRN into the same bin — the common
        /// case, where one delivery goes to one place. Lines already allocated
        /// are left alone.
        /// </summary>
        [HttpPost("AllocateAll/{mTransNo}")]
        public async Task<IActionResult> AllocateAll(
            int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string store = "",
            [FromQuery] string location = "")
        {
            if (mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");
            if (string.IsNullOrWhiteSpace(store))
                return BadRequestResult("Store is required.");

            try
            {
                var result = await db.CallGRNStoreAllocSP(new SP_GRNStoreAlloc
                {
                    MTransNo   = mTransNo,
                    Store      = store,
                    Location   = location,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "AllocateAll"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Clear/{transSlNo}")]
        public async Task<IActionResult> Clear(
            int transSlNo,
            [FromQuery] int mTransNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID)
        {
            if (transSlNo <= 0 || mTransNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallGRNStoreAllocSP(new SP_GRNStoreAlloc
                {
                    TransSlNo  = transSlNo,
                    MTransNo   = mTransNo,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID    = subscID,
                    SystemIP   = ClientIp,
                    Status     = "Clear"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // BIN MASTER
        // ============================================================

        [HttpPost("Master/SaveData")]
        public async Task<IActionResult> MasterSaveData([FromBody] SP_StoreLocation dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.Store))
                return BadRequestResult("Store is required.");
            if (dto.SubscID == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");
            if (dto.CreatedBy == null || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");
            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");
            if (dto.Status == "Update" && dto.MTransNo <= 0)
                return BadRequestResult("Invalid store location.");

            try
            {
                // Audit stamps are the server's; an update is by whoever saves it.
                var now = DateTime.Now;
                dto.SystemIP   = ClientIp;
                dto.CreatedOn  = now;
                dto.ModifiedOn = now;
                if (dto.ModifiedBy == null || dto.ModifiedBy <= 0) dto.ModifiedBy = dto.CreatedBy;
                var result = await db.CallStoreLocationSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        [HttpPost("Master/DeleteData/{id}")]
        public async Task<IActionResult> MasterDeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallStoreLocationSP(new SP_StoreLocation
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

        [HttpGet("Master/GetList/{subscID}")]
        public async Task<IActionResult> MasterGetList(
            int subscID,
            [FromQuery] int? partNo = null,
            [FromQuery] string? store = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.StoreLocation
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // A part's bins always include the general ones — those are
                // available to it too.
                if (partNo.HasValue && partNo > 0)
                    query = query.Where(x => x.PartNo == partNo || x.PartNo == 0);
                if (!string.IsNullOrWhiteSpace(store))
                    query = query.Where(x => x.Store == store.Trim());
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.Store!.Contains(search)    ||
                        x.Location!.Contains(search) ||
                        x.PartCode!.Contains(search) ||
                        x.PartDescription!.Contains(search));

                var data = await query
                    .OrderBy(x => x.Store).ThenBy(x => x.Location).ThenBy(x => x.PartCode)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The distinct store names — the first dropdown.</summary>
        [HttpGet("Master/GetStores/{subscID}")]
        public async Task<IActionResult> MasterGetStores(int subscID)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.StoreLocation
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.Store != null)
                    .Select(x => x.Store!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No store has been set up yet.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        /// <summary>The locations within one store — the second dropdown.</summary>
        [HttpGet("Master/GetLocations/{subscID}")]
        public async Task<IActionResult> MasterGetLocations(
            int subscID,
            [FromQuery] string store = "",
            [FromQuery] int partNo = 0)
        {
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");
            if (string.IsNullOrWhiteSpace(store)) return BadRequestResult("Store is required.");

            try
            {
                var query = db.StoreLocation
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N"
                             && x.Store == store.Trim() && x.Location != null);

                if (partNo > 0)
                    query = query.Where(x => x.PartNo == partNo || x.PartNo == 0);

                var data = await query
                    .Select(x => x.Location!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No location under this store.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
