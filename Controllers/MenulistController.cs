using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// The navigation catalogue. MTransNo is the module number that
    /// AccessTemplate.ModuleNo refers to, so deleting a menu row also removes
    /// every grant that pointed at it (handled inside SP_Menulist).
    /// </summary>
    [Route("api/[controller]")]
    public class MenulistController : EmsControllerBase
    {
        private readonly EMSContext db;

        public MenulistController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_Menulist dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.ModuleName))
                return BadRequestResult("Module Name is required.");

            try
            {
                var result = await db.CallMenulistSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetList")]
        public async Task<IActionResult> GetList([FromQuery] string category = "")
        {
            try
            {
                var query = db.Menulist.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(category))
                    query = query.Where(x => x.Category == category);

                var data = await query
                    .OrderBy(x => x.CSLNo)
                    .ThenBy(x => x.SSLNo)
                    .ThenBy(x => x.MSLNo)
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetData/{id}")]
        public async Task<IActionResult> GetData(int id)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var data = await db.Menulist
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id);

                if (data == null)
                    return NotFoundResult();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Distinct categories — the top level of the navigation tree.</summary>
        [HttpGet("GetCategory")]
        public async Task<IActionResult> GetCategory()
        {
            try
            {
                var data = await db.Menulist
                    .AsNoTracking()
                    .Where(x => x.Category != null)
                    .Select(x => new { x.Category, x.CSLNo })
                    .Distinct()
                    .OrderBy(x => x.CSLNo)
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(int id)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var result = await db.CallMenulistSP(new SP_Menulist
                {
                    MTransNo = id,
                    ModuleName = "-",   // not used by the Delete branch, but the DTO requires a value
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
