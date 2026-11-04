using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Per-template module permissions. A user's UserMaster.AccessTemplate names
    /// the template whose grants apply to them.
    ///     AuthType 'A' = full access, 'V' = view only, 'R' = revoke the grant.
    /// </summary>
    [Route("api/[controller]")]
    public class AccessTemplateController : EmsControllerBase
    {
        private readonly EMSContext db;

        public AccessTemplateController(EMSContext context) => db = context;

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_AccessTemplate dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.TemplateName))
                return BadRequestResult("Template Name is required.");

            if (dto.ModuleNo <= 0)
                return BadRequestResult("Module No is required.");

            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var result = await db.CallAccessTemplateSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Distinct template names defined for a subscription.</summary>
        [HttpGet("GetTemplateList/{subscID}")]
        public async Task<IActionResult> GetTemplateList(int subscID)
        {
            try
            {
                var data = await db.AccessTemplate
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID)
                    .Select(x => new { x.TemplateName, x.Department })
                    .Distinct()
                    .OrderBy(x => x.TemplateName)
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("Getcategory")]
        public async Task<IActionResult> Getcategory()
        {
            try
            {
                var mMasterData = await db.Menulist
                    .Select(om => new { om.Category })
                    .Distinct()
                    .OrderBy(x => x.Category)
                    .ToListAsync();

                return Ok(mMasterData);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving data: {ex.Message}");
            }
        }
        /// <summary>
        /// Every module, each carrying the grant this template holds on it (or
        /// null where no grant exists) — the shape the permission matrix binds to.
        /// </summary>
        [HttpGet("GetModule/{subscID}")]
        public async Task<IActionResult> GetModule(int subscID, [FromQuery] string templateName = "", [FromQuery] string category = "")
        {
            try
            {
                var modules = await db.Menulist
                    .AsNoTracking()
                    .Where(m => category == "" || m.Category == category)
                    .OrderBy(m => m.CSLNo).ThenBy(m => m.SSLNo).ThenBy(m => m.MSLNo)
                    .ToListAsync();

                var grants = await db.AccessTemplate
                    .AsNoTracking()
                    .Where(a => a.SubscID == subscID && a.TemplateName == templateName)
                    .ToListAsync();

                var data = modules.Select(m =>
                {
                    var grant = grants.FirstOrDefault(g => g.ModuleNo == m.MTransNo);

                    return new
                    {
                        m.MTransNo,
                        m.Category,
                        m.CSLNo,
                        m.Section,
                        m.SSLNo,
                        m.ModuleName,
                        m.MSLNo,
                        m.ModulePage,
                        AuthData = grant == null ? null : new
                        {
                            grant.MTransNo,
                            grant.AuthType,
                            grant.MyFavorite
                        }
                    };
                }).ToList();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Toggles the "my favourite" pin on one grant.</summary>
        [HttpPut("SetFavorite/{id}")]
        public async Task<IActionResult> SetFavorite(int id)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var row = await db.AccessTemplate.FirstOrDefaultAsync(x => x.MTransNo == id);

                if (row == null)
                    return NotFoundResult("Access record not found.");

                row.MyFavorite = row.MyFavorite == "Y" ? "N" : "Y";
                await db.SaveChangesAsync();

                return OkResult(new { row.MTransNo, row.MyFavorite }, "Favourite updated.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(
            int id,
            [FromQuery] string templateName,
            [FromQuery] int moduleNo,
            [FromQuery] int subscID)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return BadRequestResult("Template Name is required.");

            try
            {
                var result = await db.CallAccessTemplateSP(new SP_AccessTemplate
                {
                    MTransNo = id,
                    TemplateName = templateName,
                    ModuleNo = moduleNo,
                    SubscID = subscID,
                    Status = "Delete"
                });

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
