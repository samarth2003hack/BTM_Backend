using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Ship-to addresses of a customer. The billing address is on
    /// CustomerMaster; these are where the goods go.
    /// </summary>
    [Route("api/[controller]")]
    public class ShippingAddressController : EmsControllerBase
    {
        private readonly EMSContext db;

        public ShippingAddressController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_ShippingAddress dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.MTransNo <= 0)
                return BadRequestResult("Customer is required.");

            if (string.IsNullOrWhiteSpace(dto.ShipName))
                return BadRequestResult("Ship-to Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallShippingAddressSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Addresses of one customer.</summary>
        [HttpGet("GetList/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetList(int mTransNo, int subscID)
        {
            if (mTransNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var data = await db.ShippingAddress
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderBy(x => x.ShipName)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.MTransNo,
                        x.ShipName,
                        x.ShipAlias,
                        x.ShipAddress,
                        x.ShipState,
                        x.ShipStateCode,
                        x.PinCodeLocation,
                        x.PinCode,
                        x.PhoneNo,
                        x.MobileNo,
                        x.EmailID,
                        x.GSTIN,
                        x.PAN,
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
                var data = await db.ShippingAddress
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

        /// <summary>Ship-to picker for one customer — active addresses only.</summary>
        [HttpGet("GetDropdown/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetDropdown(int mTransNo, int subscID)
        {
            try
            {
                var data = await db.ShippingAddress
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.ShipName)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.ShipName,
                        x.ShipAddress,
                        x.ShipState,
                        x.ShipStateCode,
                        x.PinCode,
                        x.GSTIN
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
                var result = await db.CallShippingAddressSP(new SP_ShippingAddress
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
