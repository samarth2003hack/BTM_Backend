using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Terms agreed with one customer. Pass TermSlNo 0 on save and the procedure
    /// allocates the next number; send 1.10 / 1.20 style values to sub-number.
    /// The reusable library these are usually copied from is StandardTerms.
    /// </summary>
    [Route("api/[controller]")]
    public class CustomerTermController : EmsControllerBase
    {
        private readonly EMSContext db;

        public CustomerTermController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_CustomerTerm dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.MTransNo <= 0)
                return BadRequestResult("Customer is required.");

            if (string.IsNullOrWhiteSpace(dto.TermTitle) && string.IsNullOrWhiteSpace(dto.TermDescription))
                return BadRequestResult("Term Title or Description is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallCustomerTermSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Terms of one customer, in term order.</summary>
        [HttpGet("GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var data = await db.CustomerTerm
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.TermSlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.MTransNo,
                        x.TermSlNo,
                        x.TermTitle,
                        x.TermDescription,
                        x.TermType,
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
                return BadRequestResult("Invalid TransSlNo.");

            try
            {
                var data = await db.CustomerTerm
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TransSlNo == id &&
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
                var result = await db.CallCustomerTermSP(new SP_CustomerTerm
                {
                    TransSlNo = id,
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
