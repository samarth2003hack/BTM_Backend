using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class LeadGenerationController : EmsControllerBase
    {
        private readonly EMSContext db;

        public LeadGenerationController(EMSContext context) => db = context;

        //=========================================
        // SAVE / UPDATE
        //=========================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_LeadGeneration dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
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

            if (dto.Status != "Insert" && dto.Status != "Update")
                return BadRequestResult("Status must be 'Insert' or 'Update'.");

            try
            {
                // SP_LeadGeneration stores @CreatedOn / @ModifiedOn verbatim, so the
                // server stamps them — a browser clock would land as UTC (5.5h off).
                if (dto.Status == "Insert") dto.CreatedOn = DateTime.Now;
                else { dto.ModifiedOn = DateTime.Now; dto.ModifiedBy ??= dto.CreatedBy; }

                dto.SystemIP = ClientIp;
                var result = await db.CallLeadGenerationSP(dto);
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        //=========================================
        // DELETE
        //=========================================

        [HttpPost("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallLeadGenerationSP(new SP_LeadGeneration
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

        //=========================================
        // LOCK / UNLOCK
        //=========================================

        [HttpPost("LockUnlock/{id}")]
        public async Task<IActionResult> LockUnlock(
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
                var result = await db.CallLeadGenerationSP(new SP_LeadGeneration
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

        //=========================================
        // GET LIST
        //=========================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] int? docYear = null,
            [FromQuery] string? docSeries = null,
            [FromQuery] int? vendorNo = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.LeadGeneration
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (docYear.HasValue && docYear > 0)
                    query = query.Where(x => x.DocYear == docYear);
                if (!string.IsNullOrWhiteSpace(docSeries))
                    query = query.Where(x => x.DocSeries == docSeries.Trim());
                if (vendorNo.HasValue && vendorNo > 0)
                    query = query.Where(x => x.VendorNo == vendorNo);
                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x =>
                        x.VendorName!.Contains(search)     ||
                        x.ContactPerson!.Contains(search)  ||
                        x.Product!.Contains(search)        ||
                        x.SalesExecutive!.Contains(search) ||
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
                        x.VendorNo,
                        x.VendorCode,
                        x.VendorName,
                        x.City,
                        x.ContactPerson,
                        x.PhoneNo,
                        x.Product,
                        x.Qty,
                        x.Followupdate,
                        x.SalesExecutive,
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

        //=========================================
        // GET DATA
        //=========================================

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.LeadGeneration
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.MTransNo     == id     &&
                        x.SubscID      == subscID &&
                        x.DeleteStatus == "N");

                if (data == null) return NotFoundResult();
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }
    }
}
