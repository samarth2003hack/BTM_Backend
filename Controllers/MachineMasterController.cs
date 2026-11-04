using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Machine Master (Masters/MachineMaster.html). A machine is mapped to the
    /// Other Master 'Operation' rows it can perform; Routing Chart steps and
    /// Job Card entries run on it. Rules live in dbo.SP_MachineMst (SQL 54).
    /// Saves answer "OK^&lt;MTransNo&gt;"; a refused save / delete is a 409 with
    /// the reason as the message.
    /// </summary>
    [Route("api/[controller]")]
    public class MachineMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public MachineMasterController(EMSContext context) => db = context;

        // ============================================================
        // SAVE / UPDATE
        // Body: { mTransNo, machineCode, machineName, operations: [int],
        //         remarks, lockStatus, createdBy, subscID, status,
        //         capacityUnit, shiftsPerDay, efficiencyPct,
        //         minPieceWt, maxPieceWt }   (planning fields, SQL 58;
        //         an absent planning field keeps its stored value)
        // ============================================================

        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] MachineSaveRequest dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");
            if (dto.SubscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            var userNo = dto.CreatedBy > 0 ? dto.CreatedBy : dto.UserNo;
            if (userNo <= 0)
                return BadRequestResult("Invalid User.");

            var isUpdate = string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);
            if (isUpdate && dto.MTransNo <= 0)
                return BadRequestResult("Invalid MTransNo.");

            // Operations arrive as an id array (or, from older callers, as text).
            var ops = (dto.Operations ?? ParseIds(dto.Operation))
                .Where(x => x > 0).Distinct().OrderBy(x => x).ToList();

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var now = DateTime.Now;
                var result = await db.CallMachineMstSP(new SP_MachineMst
                {
                    MTransNo = isUpdate ? dto.MTransNo : 0,
                    MachineCode = (dto.MachineCode ?? "").Trim().ToUpperInvariant(),
                    MachineName = (dto.MachineName ?? "").Trim(),
                    DateofInst = dto.DateofInst,
                    Capacity = dto.Capacity,
                    Operation = string.Join(",", ops),
                    Remarks = (dto.Remarks ?? "").Trim(),
                    LockStatus = dto.LockStatus == "N" ? "N" : "Y",
                    CreatedBy = userNo,
                    CreatedOn = now,
                    ModifiedBy = userNo,
                    ModifiedOn = now,
                    SubscID = dto.SubscID,
                    Status = isUpdate ? "Update" : "Insert"
                });

                if (result.ID == 0) return ConflictResult(result.Message);

                // Planning fields (hammer rate and fit) — SQL 58.
                var plan = await db.CallMachinePlanFieldsSP(result.ID,
                    dto.CapacityUnit == null ? null : dto.CapacityUnit.Trim(),
                    dto.ShiftsPerDay, dto.EfficiencyPct, dto.MinPieceWt, dto.MaxPieceWt,
                    userNo, dto.SubscID);
                if (plan.ID == 0) return ConflictResult(plan.Message);

                await tx.CommitAsync();
                return OkResult($"OK^{result.ID}", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // DELETE — refused while a route step, a job-card entry or a
        // maintenance record uses the machine.
        // ============================================================

        [HttpDelete("DeleteData/{id}")]
        [HttpPost("DeleteData/{id}")]
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
                var result = await db.CallMachineMstSP(new SP_MachineMst
                {
                    MTransNo = id,
                    ModifiedBy = userNo,
                    ModifiedOn = DateTime.Now,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason.Trim(),
                    Status = "Delete"
                });
                if (result.ID == 0) return ConflictResult(result.Message);
                return OkResult("OK", result.Message);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET LIST
        // ============================================================

        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] string? lockStatus = null,
            [FromQuery] string? search = null)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadMachines(subscID, 0, lockStatus, search);
                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET DATA — one machine, same shape as a GetList row.
        // ============================================================

        [HttpGet("GetData/{id}/{subscID}")]
        public async Task<IActionResult> GetData(int id, int subscID)
        {
            if (id <= 0) return BadRequestResult("Invalid MTransNo.");
            if (subscID <= 0) return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadMachines(subscID, id, null, null);
                if (data.Count == 0) return NotFoundResult("Machine not found.");
                return OkResult(data[0]);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // GET DROPDOWN — active machines only, with their operations.
        // ============================================================

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await LoadMachines(subscID, 0, "Y", null);
                return OkResult(data);
            }
            catch (Exception ex) { return ErrorResult(ex); }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<List<object>> LoadMachines(int subscID, int id, string? lockStatus, string? search)
        {
            var entryUse = await db.PmTableExistsAsync("JobCardEntry")
                ? "(SELECT COUNT(*) FROM dbo.JobCardEntry e WHERE e.MachineNo = m.MTransNo AND e.DeleteStatus = 'N')"
                : "0";

            var sql = $"""
                SELECT m.MTransNo                          AS mTransNo,
                       m.MachineCode                       AS machineCode,
                       m.MachineName                       AS machineName,
                       ISNULL(m.Operation, '')             AS operation,
                       ISNULL(m.Remarks, '')               AS remarks,
                       m.DateofInst                        AS dateofInst,
                       m.Capacity                          AS capacity,
                       ISNULL(m.CapacityUnit, '')          AS capacityUnit,
                       ISNULL(m.ShiftsPerDay, 1)           AS shiftsPerDay,
                       ISNULL(m.EfficiencyPct, 100)        AS efficiencyPct,
                       ISNULL(m.MinPieceWt, 0)             AS minPieceWt,
                       ISNULL(m.MaxPieceWt, 0)             AS maxPieceWt,
                       m.LockStatus                        AS lockStatus,
                       CONVERT(char(10), m.CreatedOn, 23)  AS createdOn,
                       (SELECT COUNT(*)
                          FROM dbo.RoutingChartDtl d
                          JOIN dbo.RoutingChartMst h ON h.MTransNo = d.MTransNo AND h.DeleteStatus = 'N'
                         WHERE d.MachineNo = m.MTransNo AND d.DeleteStatus = 'N') AS routeUseCount,
                       {entryUse}                          AS entryUseCount
                  FROM dbo.MachineMst m
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N'
                   AND (@Id = 0 OR m.MTransNo = @Id)
                   AND (@Lock = '' OR m.LockStatus = @Lock)
                   AND (@Search = '' OR m.MachineCode LIKE '%' + @Search + '%'
                                     OR m.MachineName LIKE '%' + @Search + '%'
                                     OR m.Remarks     LIKE '%' + @Search + '%')
                 ORDER BY m.MachineCode
                """;

            var rows = await db.PmQueryRowsAsync(sql,
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID },
                new SqlParameter("@Id", SqlDbType.Int) { Value = id },
                new SqlParameter("@Lock", SqlDbType.VarChar, 1) { Value = (lockStatus ?? "").Trim() },
                new SqlParameter("@Search", SqlDbType.VarChar, 100) { Value = (search ?? "").Trim() });

            // Operation names
            var opRows = await db.PmQueryRowsAsync(
                "SELECT MTransNo AS id, MasterName AS name FROM dbo.OtherMaster " +
                "WHERE MasterType = 'Operation' AND SubscID = @SubscID AND DeleteStatus = 'N'",
                new SqlParameter("@SubscID", SqlDbType.Int) { Value = subscID });
            var opNames = opRows.ToDictionary(r => Convert.ToInt32(r["id"]), r => r["name"]?.ToString() ?? "");

            return rows.Select(r =>
            {
                var ids = ParseIds(r["operation"]?.ToString());
                return (object)new
                {
                    mTransNo = Convert.ToInt32(r["mTransNo"]),
                    machineCode = r["machineCode"],
                    machineName = r["machineName"],
                    operations = ids.Select(x => x.ToString()).ToList(),
                    operationNames = ids.Select(x => opNames.TryGetValue(x, out var n) ? n : "").ToList(),
                    operation = r["operation"],
                    dateofInst = r["dateofInst"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["dateofInst"]),
                    capacity = r["capacity"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["capacity"]),
                    capacityUnit = r["capacityUnit"],
                    shiftsPerDay = Convert.ToInt32(r["shiftsPerDay"]),
                    efficiencyPct = Convert.ToDecimal(r["efficiencyPct"]),
                    minPieceWt = Convert.ToDecimal(r["minPieceWt"]),
                    maxPieceWt = Convert.ToDecimal(r["maxPieceWt"]),
                    lockStatus = r["lockStatus"],
                    remarks = r["remarks"],
                    createdOn = r["createdOn"],
                    routeUseCount = Convert.ToInt32(r["routeUseCount"]),
                    entryUseCount = Convert.ToInt32(r["entryUseCount"])
                };
            }).ToList();
        }

        private static List<int> ParseIds(string? csv) =>
            (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .Where(n => n > 0)
                .ToList();
    }

    // ── Request DTO ───────────────────────────────────────────────────────────
    public class MachineSaveRequest
    {
        public int MTransNo { get; set; }
        public string? MachineCode { get; set; }
        public string? MachineName { get; set; }
        public DateTime? DateofInst { get; set; }
        public decimal? Capacity { get; set; }
        // Planning (SQL 58) — null keeps the stored value.
        public string? CapacityUnit { get; set; }
        public int? ShiftsPerDay { get; set; }
        public decimal? EfficiencyPct { get; set; }
        public decimal? MinPieceWt { get; set; }
        public decimal? MaxPieceWt { get; set; }
        public List<int>? Operations { get; set; }   // preferred: array of int ids
        public string? Operation { get; set; }   // fallback: comma-separated string
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int UserNo { get; set; }   // alias for CreatedBy
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
