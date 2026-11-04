using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Vendor rates per part. A vendor/part pair holds many rows over time — the
    /// history is the point — so saving does not overwrite the previous rate;
    /// it adds one with a new effective date. What is rejected is a second live
    /// rate for the same vendor/part/grade on the same date (SP_PriceList).
    /// </summary>
    [Route("api/[controller]")]
    public class PriceListController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PriceListController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_PriceList dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.VendorNo <= 0)
                return BadRequestResult("Vendor is required.");

            if (dto.PartNo <= 0)
                return BadRequestResult("Part is required.");

            if (dto.Rate == null)
                return BadRequestResult("Rate is required.");

            if (dto.Rate < 0)
                return BadRequestResult("Rate cannot be negative.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                dto.SystemIP = ClientIp;

                var result = await db.CallPriceListSP(dto);

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
        /// Rates with the vendor and part names resolved, newest effective date
        /// first. Narrow by vendor or part; both are optional.
        /// </summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = from r in db.PriceList.AsNoTracking()
                            join v in db.SupplierMaster.AsNoTracking() on r.VendorNo equals v.MTransNo
                            where r.SubscID == subscID && r.DeleteStatus == "N"
                            select new { r, v };
                var data = await query
                    .Select(x => new
                    {
                        x.r.VendorNo,
                        x.v.SupplierCode,
                        x.v.SupplierName,
                    })
                    .Distinct()
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
        /// <summary>
        /// Rates with the vendor and part names resolved, newest effective date
        /// first. Narrow by vendor or part; both are optional.
        /// </summary>
        [HttpGet("GetDataList/{subscID}")]
        public async Task<IActionResult> GetDataList(
            int subscID,
            [FromQuery] int vendorNo = 0,
            [FromQuery] int partNo = 0)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = from r in db.PriceList.AsNoTracking()
                            join v in db.SupplierMaster.AsNoTracking() on r.VendorNo equals v.MTransNo
                            join p in db.PartMaster.AsNoTracking() on r.PartNo equals p.MTransNo
                            where r.SubscID == subscID && r.DeleteStatus == "N"
                            select new { r, v, p };

                if (vendorNo > 0)
                    query = query.Where(x => x.r.VendorNo == vendorNo);

                if (partNo > 0)
                    query = query.Where(x => x.r.PartNo == partNo);

                var data = await query
                    .OrderByDescending(x => x.r.EffectiveDt)
                    .ThenBy(x => x.p.PartCode)
                    .Select(x => new
                    {
                        x.r.MTransNo,
                        x.r.VendorNo,
                        x.v.SupplierCode,
                        x.v.SupplierName,
                        x.r.PartNo,
                        x.p.PartCode,
                        x.p.PartName,
                        x.r.GradeNo,
                        x.r.Grade,
                        x.r.Unit,
                        x.r.Rate,
                        x.r.EffectiveDt,
                        x.r.LeadTime,
                        x.r.ItemRemarks,
                        x.r.LockStatus,
                        x.r.CreatedBy,
                        x.r.CreatedOn,
                        x.r.ModifiedBy,
                        x.r.ModifiedOn
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
                var data = await db.PriceList
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
        /// Every vendor's current rate for one part: the newest row on or before
        /// asOn (today by default) per vendor, cheapest first. This is the lookup
        /// a purchase screen wants — GetList returns the whole history instead.
        /// </summary>
        [HttpGet("GetCurrentRates/{partNo}/{subscID}")]
        public async Task<IActionResult> GetCurrentRates(
            int partNo,
            int subscID,
            [FromQuery] DateTime? asOn = null)
        {
            if (partNo <= 0)
                return BadRequestResult("Invalid PartNo.");

            try
            {
                // Not named "on" — that is a query-expression keyword.
                var asOnDate = (asOn ?? DateTime.Today).Date;

                var rows = await (from r in db.PriceList.AsNoTracking()
                                  join v in db.SupplierMaster.AsNoTracking() on r.VendorNo equals v.MTransNo
                                  where r.SubscID == subscID &&
                                        r.PartNo == partNo &&
                                        r.DeleteStatus == "N" &&
                                        v.DeleteStatus == "N" &&
                                        (r.EffectiveDt == null || r.EffectiveDt <= asOnDate)
                                  select new
                                  {
                                      r.MTransNo,
                                      r.VendorNo,
                                      v.SupplierCode,
                                      v.SupplierName,
                                      r.GradeNo,
                                      r.Grade,
                                      r.Unit,
                                      r.Rate,
                                      r.EffectiveDt,
                                      r.LeadTime,
                                      r.ItemRemarks
                                  })
                                 .ToListAsync();

                // One row per vendor and grade — the latest effective date wins.
                var current = rows
                    .GroupBy(x => new { x.VendorNo, x.GradeNo })
                    .Select(g => g.OrderByDescending(x => x.EffectiveDt)
                                  .ThenByDescending(x => x.MTransNo)
                                  .First())
                    .OrderBy(x => x.Rate)
                    .ToList();

                return OkResult(current, current.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Rate history of one vendor/part pair, newest first.</summary>
        [HttpGet("GetHistory/{vendorNo}/{partNo}/{subscID}")]
        public async Task<IActionResult> GetHistory(int vendorNo, int partNo, int subscID)
        {
            if (vendorNo <= 0 || partNo <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var data = await db.PriceList
                    .AsNoTracking()
                    .Where(x => x.VendorNo == vendorNo &&
                                x.PartNo == partNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderByDescending(x => x.EffectiveDt)
                    .ThenByDescending(x => x.MTransNo)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.GradeNo,
                        x.Grade,
                        x.Unit,
                        x.Rate,
                        x.EffectiveDt,
                        x.LeadTime,
                        x.ItemRemarks,
                        x.LockStatus
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
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
                var result = await db.CallPriceListSP(new SP_PriceList
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
