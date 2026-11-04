using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Suppliers. Deleting one soft-deletes its contacts, terms and the vendor's
    /// price-list rates — the rule lives in SP_SupplierMaster.
    /// </summary>
    [Route("api/[controller]")]
    public class SupplierMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public SupplierMasterController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_SupplierMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            // SupplierCode may be blank: on Insert SP_SupplierMaster allocates one
            // from the name (SQL/38), on Update it keeps the stored code.
            if (string.IsNullOrWhiteSpace(dto.SupplierName))
                return BadRequestResult("Supplier Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallSupplierMasterSP(dto);

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
                var query = db.SupplierMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.SupplierName!.Contains(search) ||
                                             x.SupplierCode!.Contains(search) ||
                                             x.GSTIN!.Contains(search));

                var data = await query
                    .OrderBy(x => x.SupplierName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SupplierCode,
                        x.SupplierType,
                        x.SupplierName,
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
                        x.BankName,
                        x.IFSC,
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
                var data = await db.SupplierMaster
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

        /// <summary>The supplier with its contacts and terms in one call.</summary>
        [HttpGet("GetFullData/{id}/{subscID}")]
        public async Task<IActionResult> GetFullData(int id, int subscID)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var supplier = await db.SupplierMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id &&
                                              x.SubscID == subscID &&
                                              x.DeleteStatus == "N");

                if (supplier == null)
                    return NotFoundResult();

                var contacts = await db.SupplierContacts
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.TransSlNo)
                    .ToListAsync();

                var terms = await db.SupplierTerm
                    .AsNoTracking()
                    .Where(x => x.MTransNo == id && x.DeleteStatus == "N")
                    .OrderBy(x => x.TermSlNo)
                    .ToListAsync();

                return OkResult(new { Supplier = supplier, Contacts = contacts, Terms = terms });
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
                var data = await db.SupplierMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.SupplierName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.SupplierCode,
                        x.SupplierName,
                        x.StateName,
                        x.StateCode,
                        x.GSTIN,
                        x.TxnCurrency,
                        x.CreditPeriod,
                        // The purchase order snapshots these onto its header.
                        x.SupplierAddress,
                        ContactNo = x.PhoneNo ?? x.MobileNo,
                        x.TaxType,
                        // Inter State / Intra State / Export: the GST split.
                        x.SupplierType
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
                var result = await db.CallSupplierMasterSP(new SP_SupplierMaster
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
