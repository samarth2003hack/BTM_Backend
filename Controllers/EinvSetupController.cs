using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// e-Invoice / e-Way-Bill gateway credentials — one live row per
    /// subscription. SaveData upserts that row; RefreshToken updates only the
    /// session tokens returned by the gateway.
    /// </summary>
    [Route("api/[controller]")]
    public class EinvSetupController : EmsControllerBase
    {
        private readonly EMSContext db;

        public EinvSetupController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_EinvSetup dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            if (string.IsNullOrWhiteSpace(dto.EUserID))
                return BadRequestResult("e-Invoice User ID is required.");

            try
            {
                var result = await db.CallEinvSetupSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Stores freshly issued gateway tokens without touching credentials.</summary>
        [HttpPost("RefreshToken")]
        public async Task<IActionResult> RefreshToken([FromBody] SP_EinvSetup dto)
        {
            if (dto == null || dto.SubscID <= 0)
                return BadRequestResult("Invalid request.");

            try
            {
                dto.Status = "Token";

                var result = await db.CallEinvSetupSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// The live setup for a subscription. EPassword and the tokens are
        /// omitted — the UI never needs to read them back.
        /// </summary>
        [HttpGet("GetData/{subscID}")]
        public async Task<IActionResult> GetData(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.EinvSetup
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.EUserID,
                        x.AppKey,
                        x.TokenExp,
                        x.EwbAppKey,
                        x.EwbTokenExp,
                        HasCredentials = x.EPassword != null,
                        TokenActive = x.AuthToken != null,
                        EwbTokenActive = x.EwbAuthToken != null
                    })
                    .FirstOrDefaultAsync();

                if (data == null)
                    return NotFoundResult("No e-Invoice setup found for this subscription.");

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(int id, [FromQuery] int subscID)
        {
            if (id <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallEinvSetupSP(new SP_EinvSetup
                {
                    MTransNo = id,
                    SubscID = subscID,
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
