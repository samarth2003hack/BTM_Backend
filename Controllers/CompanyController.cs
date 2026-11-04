using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class CompanyController : EmsControllerBase
    {
        private readonly EMSContext db;

        public CompanyController(EMSContext context) => db = context;

        //=========================================
        // SAVE (Insert / Update via SP_Company)
        //=========================================
        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_Company dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.CompCode))
                return BadRequestResult("Company Code is required.");

            if (string.IsNullOrWhiteSpace(dto.CompName))
                return BadRequestResult("Company Name is required.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            if (dto.CreatedBy <= 0)
                return BadRequestResult("Invalid User.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallCompanySP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // LIST (grid)
        //=========================================
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.Company
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.CompName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.CompCode,
                        x.CompName,
                        x.TradingName,
                        x.CompAddress,
                        x.State,
                        x.StateCode,
                        x.PhoneNo,
                        x.PinCode,
                        x.GeneralEmail,
                        x.Website,
                        x.GSTIN,
                        x.PAN,
                        x.CIN,
                        x.TxnCurrency,
                        x.FYear,
                        x.YearCode,
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

        //=========================================
        // SINGLE RECORD (edit form)
        //=========================================
        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.Company
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

        //=========================================
        // DROPDOWN (active companies only)
        //=========================================
        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            try
            {
                var data = await db.Company
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.CompName)
                    .Select(x => new { x.MTransNo, x.CompCode, x.CompName })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // SOFT DELETE
        //=========================================
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
                var result = await db.CallCompanySP(new SP_Company
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
