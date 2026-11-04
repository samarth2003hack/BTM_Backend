using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Customers. Deleting one soft-deletes its contacts, terms and shipping
    /// addresses too — that rule lives in SP_CustomerMaster, not here.
    /// </summary>
    [Route("api/[controller]")]
    public class CustomerMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public CustomerMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_CustomerMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            // CustomerCode may be blank: on Insert SP_CustomerMaster allocates one
            // from the name (SQL/37), on Update it keeps the stored code.
            if (string.IsNullOrWhiteSpace(dto.CustomerName))
                return BadRequestResult("Customer Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallCustomerMasterSP(dto);

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
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string search = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.CustomerMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.CustomerName!.Contains(search) ||
                                             x.CustomerCode!.Contains(search) ||
                                             x.GSTIN!.Contains(search));

                var data = await query
                    .OrderBy(x => x.CustomerName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.CustomerCode,
                        x.CustomerType,
                        x.CustomerName,
                        x.AliasName,
                        x.Category,
                        x.PinCodeLocation,
                        x.StateName,
                        x.StateCode,
                        x.MobileNo,
                        x.EmailID,
                        x.GSTIN,
                        x.TxnCurrency,
                        x.CreditPeriod,
                        x.CreditLimit,
                        x.SalesExec,
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
                var data = await db.CustomerMaster
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

        /// <summary>
        /// The customer and all three child collections in one call, so the edit
        /// screen does not need four round trips to open a record.
        /// </summary>
        [HttpGet("GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> GetFullData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var customer = await db.CustomerMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id &&
                                              x.SubscID == subscID &&
                                              x.DeleteStatus == "N");

                if (customer == null)
                    return NotFoundResult();

                var contacts = await db.CustomerContacts
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.TransSlNo)
                    .ToListAsync();

                var terms = await db.CustomerTerm
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.TermSlNo)
                    .ToListAsync();

                var addresses = await db.ShippingAddress
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.ShipName)
                    .ToListAsync();

                return OkResult(new { Customer = customer, Contacts = contacts, Terms = terms, ShippingAddresses = addresses });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            try
            {
                var data = await db.CustomerMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.CustomerName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.CustomerCode,
                        x.CustomerName,
                        x.StateName,
                        x.StateCode,
                        x.GSTIN,
                        x.TxnCurrency,
                        x.CreditPeriod,
                        x.CreditLimit
                    })
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
                var result = await db.CallCustomerMasterSP(new SP_CustomerMaster
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
