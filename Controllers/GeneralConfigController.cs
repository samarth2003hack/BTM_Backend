using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class GeneralConfigController : EmsControllerBase
    {
        private readonly EMSContext db;

        public GeneralConfigController(EMSContext context) => db = context;

        /// <summary>Upserts on (ConfigKey, SubscID) — see SP_GeneralConfig.</summary>
        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_GeneralConfig dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.ConfigKey))
                return BadRequestResult("Config Key is required.");

            if (dto.ConfigValue == null)
                return BadRequestResult("Config Value is required.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                dto.CreatedOn ??= DateTime.Now;

                var result = await db.CallGeneralConfigSP(dto);

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
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.GeneralConfig
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID)
                    .OrderBy(x => x.ConfigKey)
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
                var data = await db.GeneralConfig
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id && x.SubscID == subscID);

                if (data == null)
                    return NotFoundResult();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Reads one setting by key — the common runtime lookup.</summary>
        [HttpGet("GetValue/{configKey}/{subscID}")]
        public async Task<IActionResult> GetValue(string configKey, int subscID)
        {
            if (string.IsNullOrWhiteSpace(configKey))
                return BadRequestResult("Config Key is required.");

            try
            {
                var data = await db.GeneralConfig
                    .AsNoTracking()
                    .Where(x => x.ConfigKey == configKey && x.SubscID == subscID)
                    .Select(x => new { x.MTransNo, x.ConfigKey, x.ConfigValue, x.ConfigDepartment })
                    .FirstOrDefaultAsync();

                if (data == null)
                    return NotFoundResult($"No setting found for '{configKey}'.");

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
                var result = await db.CallGeneralConfigSP(new SP_GeneralConfig
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
