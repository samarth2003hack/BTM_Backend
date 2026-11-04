using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Tells each module which ledger to post a given role to — e.g.
    /// (Sales, RoundOff) resolves to the Round Off account.
    /// Unique on (ModuleName, ACCType, SubscID); SaveData upserts.
    /// </summary>
    [Route("api/[controller]")]
    public class AccountConfigController : EmsControllerBase
    {
        private readonly EMSContext db;

        public AccountConfigController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_AccountConfig dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.ModuleName))
                return BadRequestResult("Module Name is required.");

            if (string.IsNullOrWhiteSpace(dto.ACCType))
                return BadRequestResult("Account Type is required.");

            if (dto.AcNo <= 0)
                return BadRequestResult("A ledger account is required.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var result = await db.CallAccountConfigSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Every mapping for a subscription, with the target ledger's name.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string moduleName = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = from c in db.AccountConfig.AsNoTracking()
                            join a in db.AccountsMaster.AsNoTracking() on c.AcNo equals a.AcNo into aj
                            from a in aj.DefaultIfEmpty()
                            where c.SubscID == subscID
                            select new
                            {
                                c.MTransNo,
                                c.ModuleName,
                                AccType = c.ACCType,
                                c.AcNo,
                                AcName = a.AcName,
                                c.SubscID
                            };

                if (!string.IsNullOrWhiteSpace(moduleName))
                    query = query.Where(x => x.ModuleName == moduleName);

                var data = await query
                    .OrderBy(x => x.ModuleName)
                    .ThenBy(x => x.AccType)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Resolves one mapping to its ledger — the lookup a posting routine makes
        /// before it writes a voucher line.
        /// </summary>
        [HttpGet("GetAccount/{moduleName}/{accType}/{subscID}")]
        public async Task<IActionResult> GetAccount(string moduleName, string accType, int subscID)
        {
            if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(accType))
                return BadRequestResult("Module Name and Account Type are required.");

            try
            {
                var data = await (from c in db.AccountConfig.AsNoTracking()
                                  join a in db.AccountsMaster.AsNoTracking() on c.AcNo equals a.AcNo into aj
                                  from a in aj.DefaultIfEmpty()
                                  where c.SubscID == subscID &&
                                        c.ModuleName == moduleName &&
                                        c.ACCType == accType
                                  select new
                                  {
                                      c.MTransNo,
                                      c.ModuleName,
                                      AccType = c.ACCType,
                                      c.AcNo,
                                      AcName = a.AcName,
                                      AcType = a.AcType
                                  })
                                 .FirstOrDefaultAsync();

                if (data == null)
                    return NotFoundResult($"No account is configured for {moduleName} / {accType}.");

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Distinct module names that have at least one mapping.</summary>
        [HttpGet("GetModules/{subscID}")]
        public async Task<IActionResult> GetModules(int subscID)
        {
            try
            {
                var data = await db.AccountConfig
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID)
                    .Select(x => x.ModuleName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

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
                var result = await db.CallAccountConfigSP(new SP_AccountConfig
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
