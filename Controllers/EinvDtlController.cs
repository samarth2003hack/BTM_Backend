using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// IRN / e-Way-Bill results recorded against a source transaction.
    /// EinvDtl.MTransNo is that source transaction's number.
    /// </summary>
    [Route("api/[controller]")]
    public class EinvDtlController : EmsControllerBase
    {
        private readonly EMSContext db;

        public EinvDtlController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_EinvDtl dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (dto.MTransNo <= 0)
                return BadRequestResult("Source transaction number is required.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var result = await db.CallEinvDtlSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>e-Invoice register — optionally narrowed to one source transaction.</summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID, [FromQuery] int mTransNo = 0)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var query = db.EinvDtl
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N");

                if (mTransNo > 0)
                    query = query.Where(x => x.MTransNo == mTransNo);

                var data = await query
                    .OrderByDescending(x => x.TransSlNo)
                    .Select(x => new
                    {
                        x.TransSlNo,
                        x.MTransNo,
                        x.IRNNo,
                        x.ACKNo,
                        x.AckDt,
                        x.EWBillNo,
                        x.EWBillDt
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Full row including the QR payload — used to print the invoice.</summary>
        [HttpGet("GetData/{transSlNo}/{subscID}")]
        public async Task<IActionResult> GetData(int transSlNo, int subscID)
        {
            if (transSlNo <= 0)
                return BadRequestResult("Invalid TransSlNo.");

            try
            {
                var data = await db.EinvDtl
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TransSlNo == transSlNo &&
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

        /// <summary>The live e-Invoice detail for one source transaction, if any.</summary>
        [HttpGet("GetByTransaction/{mTransNo}/{subscID}")]
        public async Task<IActionResult> GetByTransaction(int mTransNo, int subscID)
        {
            if (mTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.EinvDtl
                    .AsNoTracking()
                    .Where(x => x.MTransNo == mTransNo &&
                                x.SubscID == subscID &&
                                x.DeleteStatus == "N")
                    .OrderByDescending(x => x.TransSlNo)
                    .FirstOrDefaultAsync();

                if (data == null)
                    return NotFoundResult("No e-Invoice detail recorded for this transaction.");

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{transSlNo}")]
        public async Task<IActionResult> DeleteData(int transSlNo, [FromQuery] int subscID)
        {
            if (transSlNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            try
            {
                var result = await db.CallEinvDtlSP(new SP_EinvDtl
                {
                    TransSlNo = transSlNo,
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
