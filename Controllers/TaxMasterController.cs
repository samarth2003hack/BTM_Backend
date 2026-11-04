using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// GST rates and the ledgers each component posts to.
    /// UsedIn restricts a rate to 'Sales', 'Purchase' or 'Both'.
    /// </summary>
    [Route("api/[controller]")]
    public class TaxMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public TaxMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_TaxMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.TaxCode))
                return BadRequestResult("Tax Code is required.");

            if (dto.TaxP < 0)
                return BadRequestResult("Tax percentage cannot be negative.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallTaxMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string usedIn = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.TaxMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                // 'Both' rates are valid for whichever side the caller asked about.
                if (!string.IsNullOrWhiteSpace(usedIn))
                    query = query.Where(x => x.UsedIn == usedIn || x.UsedIn == "Both");

                var data = await query
                    .OrderBy(x => x.TaxP)
                    .ThenBy(x => x.TaxCode)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.TaxCode,
                        x.TaxP,
                        x.CessP,
                        x.CGSTAcNo,
                        x.SGSTAcNo,
                        x.IGSTAcNo,
                        x.CessAcNo,
                        x.PSACNo,
                        x.UsedIn,
                        x.LockStatus,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.TaxMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id &&
                                              x.SubscID == subscID &&
                                              x.DeleteStatus == "N");

                if (data == null)
                    return NotFoundResult();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID, [FromQuery] string usedIn = "")
        {
            try
            {
                var query = db.TaxMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y");

                if (!string.IsNullOrWhiteSpace(usedIn))
                    query = query.Where(x => x.UsedIn == usedIn || x.UsedIn == "Both");

                var data = await query
                    .OrderBy(x => x.TaxP)
                    .Select(x => new { x.MTransNo, x.TaxCode, x.TaxP, x.CessP })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
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
                var result = await db.CallTaxMasterSP(new SP_TaxMaster
                {
                    MTransNo = id,
                    CreatedBy = userNo,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "Delete"
                });

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
