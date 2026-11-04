using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// The reusable term library. One table serves both sides of the ledger:
    /// TermType ('CUSTOMER' / 'SUPPLIER' / 'PAYMENT' / ...) decides which picker
    /// a term shows up in, and titles are unique per type.
    /// </summary>
    [Route("api/[controller]")]
    public class StandardTermsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public StandardTermsController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_StandardTerms dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.TermType))
                return BadRequestResult("Term Type is required.");

            if (string.IsNullOrWhiteSpace(dto.TermTitle))
                return BadRequestResult("Term Title is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallStandardTermsSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Every term, or just one type when termType is supplied.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string termType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.StandardTerms
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(termType))
                {
                    var type = termType.Trim().ToUpperInvariant();
                    query = query.Where(x => x.TermType == type);
                }

                var data = await query
                    .OrderBy(x => x.TermType)
                    .ThenBy(x => x.TermSlNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.TermType,
                        x.TermSlNo,
                        x.TermTitle,
                        x.TermDescription,
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
                var data = await db.StandardTerms
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

        /// <summary>Active terms of one type, for the "copy standard terms" picker.</summary>
        [HttpGet("GetDropdown/{termType}/{subscID}")]
        public async Task<IActionResult> GetDropdown(string termType, int subscID)
        {
            if (string.IsNullOrWhiteSpace(termType))
                return BadRequestResult("Term Type is required.");

            try
            {
                var type = termType.Trim().ToUpperInvariant();

                var data = await db.StandardTerms
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.TermType == type &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.TermSlNo)
                    .Select(x => new { x.MTransNo, x.TermSlNo, x.TermTitle, x.TermDescription })
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
                var result = await db.CallStandardTermsSP(new SP_StandardTerms
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
