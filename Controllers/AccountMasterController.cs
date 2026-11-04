using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Ledger accounts. Each one belongs to a GroupHeads node and carries an
    /// AcType that decides which pickers it shows up in.
    /// </summary>
    [Route("api/[controller]")]
    public class AccountMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public AccountMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_AccountMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.AcName))
                return BadRequestResult("Account Name is required.");

            if (string.IsNullOrWhiteSpace(dto.AcType))
                return BadRequestResult("Account Type is required.");

            if (dto.GroupHeadNo <= 0)
                return BadRequestResult("Group Head is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallAccountMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Grid rows, each carrying its group name for display.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] string acType = "")
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = from a in db.AccountsMaster.AsNoTracking()
                            join g in db.GroupHeads.AsNoTracking() on a.GroupHeadNo equals g.GrpNo into gj
                            from g in gj.DefaultIfEmpty()
                            where a.SubscID == subscID && a.DeleteStatus == "N"
                            select new
                            {
                                a.AcNo,
                                a.AcName,
                                a.AcAlias,
                                a.AcType,
                                a.VendNo,
                                a.GroupHeadNo,
                                GroupName = g.GrpName,
                                a.CommonAccount,
                                a.LockStatus,
                                a.CreatedBy,
                                a.CreatedOn,
                                a.ModifiedBy,
                                a.ModifiedOn
                            };

                if (!string.IsNullOrWhiteSpace(acType))
                    query = query.Where(x => x.AcType == acType);

                var data = await query.OrderBy(x => x.AcName).ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetData/{acNo}/{subscID}")]
        public async Task<IActionResult> GetData(int acNo, int subscID)
        {
            if (acNo <= 0)
                return BadRequestResult("Invalid AcNo.");

            try
            {
                var data = await db.AccountsMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.AcNo == acNo &&
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
        /// Active ledgers for a subscription, including the shared ones
        /// (SubscID = 0) that every subscription can post to.
        /// </summary>
        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID, [FromQuery] string acType = "")
        {
            try
            {
                var query = db.AccountsMaster
                    .AsNoTracking()
                    .Where(x => (x.SubscID == subscID || x.SubscID == 0) &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y");

                if (!string.IsNullOrWhiteSpace(acType))
                    query = query.Where(x => x.AcType == acType);

                var data = await query
                    .OrderBy(x => x.AcName)
                    .Select(x => new { x.AcNo, x.AcName, x.AcAlias, x.AcType, x.GroupHeadNo })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Ledgers a payment can be drawn FROM — restricted to the given pay mode
        /// (typically CASH or BANK).
        /// </summary>
        [HttpGet("FromAccount/{subscID}")]
        public async Task<IActionResult> FromAccount(int subscID, [FromQuery] string payMode = "")
        {
            if (string.IsNullOrWhiteSpace(payMode))
                return BadRequestResult("Pay Mode is required.");

            try
            {
                var data = await db.AccountsMaster
                    .AsNoTracking()
                    .Where(x => (x.SubscID == subscID || x.SubscID == 0) &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y" &&
                                x.AcType == payMode)
                    .OrderBy(x => x.AcName)
                    .Select(x => new { x.AcNo, x.AcName })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Ledgers a payment can be posted TO — everything that is not cash or bank.</summary>
        [HttpGet("ToAccount/{subscID}")]
        public async Task<IActionResult> ToAccount(int subscID)
        {
            try
            {
                var data = await db.AccountsMaster
                    .AsNoTracking()
                    .Where(x => (x.SubscID == subscID || x.SubscID == 0) &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y" &&
                                x.AcType != "CASH" &&
                                x.AcType != "BANK")
                    .OrderBy(x => x.AcName)
                    .Select(x => new { x.AcNo, x.AcName })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Flags a ledger as shared across companies. CommonAccount is a single
        /// character, so only 'Y' and 'N' are accepted.
        /// </summary>
        [HttpPost("UpdateCommonAccount/{acNo}")]
        public async Task<IActionResult> UpdateCommonAccount(int acNo, [FromQuery] string commonAccount)
        {
            if (acNo <= 0)
                return BadRequestResult("Invalid AcNo.");

            var flag = (commonAccount ?? string.Empty).Trim().ToUpperInvariant();

            if (flag != "Y" && flag != "N")
                return BadRequestResult("Common Account must be 'Y' or 'N'.");

            try
            {
                var account = await db.AccountsMaster.FirstOrDefaultAsync(x => x.AcNo == acNo);

                if (account == null)
                    return NotFoundResult($"Account not found for AcNo {acNo}.");

                account.CommonAccount = flag;
                await db.SaveChangesAsync();

                return OkResult(new { account.AcNo, account.CommonAccount }, "Common Account updated.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Ledgers already marked as shared, with the company they belong to.</summary>
        [HttpGet("CommonAccountList/{subscID}")]
        public async Task<IActionResult> CommonAccountList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await (from a in db.AccountsMaster.AsNoTracking()
                                  join c in db.Company.AsNoTracking() on a.SubscID equals c.MTransNo into cj
                                  from c in cj.DefaultIfEmpty()
                                  where a.DeleteStatus == "N" &&
                                        a.CommonAccount == "Y" &&
                                        (a.SubscID == subscID || a.SubscID == 0)
                                  orderby c.CompName, a.AcName
                                  select new
                                  {
                                      a.AcNo,
                                      a.AcName,
                                      CompanyName = c.CompName,
                                      a.SubscID
                                  })
                                 .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Ledgers not yet marked as shared — the "available" side of the picker.</summary>
        [HttpGet("WithoutCommonAccountList/{subscID}")]
        public async Task<IActionResult> WithoutCommonAccountList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.AccountsMaster
                    .AsNoTracking()
                    .Where(a => a.DeleteStatus == "N" &&
                                (a.SubscID == subscID || a.SubscID == 0) &&
                                (a.CommonAccount != "Y" || a.CommonAccount == null))
                    .OrderBy(a => a.AcName)
                    .Select(a => new { a.AcNo, a.AcName })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{acNo}")]
        public async Task<IActionResult> DeleteData(
            int acNo,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (acNo <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallAccountMasterSP(new SP_AccountMaster
                {
                    AcNo = acNo,
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
