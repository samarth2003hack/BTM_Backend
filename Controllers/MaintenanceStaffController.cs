using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Maintenance / production staff for the PM Check Sheet and Breakdown
    /// pages: the Other Master rows of type 'Maintenance Supervisor',
    /// 'Production Supervisor', 'Fitter' and 'Electrician' (SQL/52). Inactive
    /// rows are included with their lockStatus so old documents still show
    /// the name; the pages offer only the active ones.
    /// </summary>
    [Route("api/MaintenanceStaff")]
    public class MaintenanceStaffController : MaintenanceControllerBase
    {
        public MaintenanceStaffController(EMSContext context) : base(context) { }

        /// <summary>
        /// { masterNo, staffName, role, lockStatus }. Optional ?role= narrows
        /// the list (comma-separated, e.g. role=Fitter,Electrician).
        /// </summary>
        [HttpGet("GetList/{subscID}")]
        public Task<IActionResult> GetList(int subscID, [FromQuery] string? role = null)
        {
            if (subscID <= 0) return Task.FromResult(BadRequestResult("Invalid Subscription."));

            var roles = (role ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Where(r => MaintRules.StaffRoles.Contains(r)).ToList();
            if (roles.Count == 0) roles = MaintRules.StaffRoles.ToList();

            return RunQuery(@"
                SELECT o.MTransNo                    AS masterNo,
                       o.MasterName                  AS staffName,
                       o.MasterType                  AS role,
                       ISNULL(o.LockStatus, 'Y')     AS lockStatus
                  FROM dbo.OtherMaster o
                 WHERE o.SubscID = @SubscID
                   AND ISNULL(o.DeleteStatus, 'N') = 'N'
                   AND o.MasterType IN (SELECT value FROM STRING_SPLIT(@Roles, '|'))
                 ORDER BY o.MasterName",
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@Roles", SqlDbType.VarChar, 400) { Value = string.Join("|", roles) });
        }
    }
}
