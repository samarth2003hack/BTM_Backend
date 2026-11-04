using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Contact people at a customer. Rows hang off CustomerMaster.MTransNo, so
    /// GetList is scoped to one customer rather than the whole subscription.
    /// </summary>
    [Route("api/[controller]")]
    public class CustomerContactsController : EmsControllerBase
    {
        private readonly EMSContext db;

        public CustomerContactsController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_CustomerContacts dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.MTransNo <= 0)
                return BadRequestResult("Customer is required.");

            if (string.IsNullOrWhiteSpace(dto.CPName))
                return BadRequestResult("Contact Person Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallCustomerContactsSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Contacts of one customer.</summary>
        [HttpGet("GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var data = await db.CustomerContacts
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.TransSlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.MTransNo,
                        x.Title,
                        x.CPName,
                        x.Designation,
                        x.PhoneNo,
                        x.MobileNo,
                        x.Email,
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
                var data = await db.CustomerContacts
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
                var result = await db.CallCustomerContactsSP(new SP_CustomerContacts
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
