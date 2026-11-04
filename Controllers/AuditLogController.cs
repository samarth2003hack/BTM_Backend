using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Read access to the data-change trail, plus the two maintenance calls.
    ///
    /// Nothing here writes to the trail. It is filled by the TR_Audit_*
    /// triggers on all 48 audited tables — see SQL/12_AuditLog.sql — which is
    /// deliberate: a trail the application can write to is a trail the
    /// application can forge, and one it can forget to write.
    ///
    /// Three ways in, for the three questions actually asked of an audit log:
    ///
    ///     GetList             what changed, across everything, in a date range
    ///     GetRecordHistory    what happened to this one record
    ///     GetDocumentHistory  what happened to this document and all its lines
    /// </summary>
    [Route("api/[controller]")]
    public class AuditLogController : EmsControllerBase
    {
        private readonly EMSContext db;

        public AuditLogController(EMSContext context) => db = context;

        // The trail outgrows a single response quickly, so every list is paged.
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;

        /// <summary>
        /// The trail for a date range, newest first, with optional filters.
        /// Defaults to the last 7 days so the endpoint never scans the table.
        /// </summary>
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(
            int subscID,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string tableName = "",
            [FromQuery] string moduleName = "",
            [FromQuery] string actionType = "",
            [FromQuery] int changedBy = 0,
            [FromQuery] string recordID = "",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                // toDate is inclusive of the whole day: the caller asks in
                // dates, the column is a timestamp.
                var to = (toDate ?? DateTime.Today).Date.AddDays(1);
                var from = (fromDate ?? DateTime.Today.AddDays(-7)).Date;

                if (from >= to)
                    return BadRequestResult("From Date must be on or before To Date.");

                page = page < 1 ? 1 : page;
                pageSize = pageSize < 1 ? DefaultPageSize
                         : pageSize > MaxPageSize ? MaxPageSize
                         : pageSize;

                var query = db.AuditLogView
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.ChangedOn >= from &&
                                x.ChangedOn < to);

                if (!string.IsNullOrWhiteSpace(tableName))
                    query = query.Where(x => x.TableName == tableName);

                if (!string.IsNullOrWhiteSpace(moduleName))
                    query = query.Where(x => x.ModuleName == moduleName);

                if (!string.IsNullOrWhiteSpace(actionType))
                    query = query.Where(x => x.ActionType == actionType);

                if (changedBy > 0)
                    query = query.Where(x => x.ChangedBy == changedBy);

                if (!string.IsNullOrWhiteSpace(recordID))
                    query = query.Where(x => x.RecordID == recordID);

                var total = await query.CountAsync();

                var rows = await query
                    .OrderByDescending(x => x.AuditNo)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return OkResult(
                    new { total, page, pageSize, rows },
                    total > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>One entry, with every column it changed.</summary>
        [HttpGet("GetData/{auditNo}/{subscID}")]
        public async Task<IActionResult> GetData(long auditNo, int subscID)
        {
            if (auditNo <= 0)
                return BadRequestResult("Invalid AuditNo.");

            try
            {
                var header = await db.AuditLogView
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.AuditNo == auditNo && x.SubscID == subscID);

                if (header == null)
                    return NotFoundResult();

                var changes = await ChangesFor(new[] { auditNo });

                var snapshot = await db.AuditLog
                    .AsNoTracking()
                    .Where(x => x.AuditNo == auditNo)
                    .Select(x => x.RecordSnapshot)
                    .FirstOrDefaultAsync();

                return OkResult(new { header, changes, snapshot });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Everything that ever happened to one record, oldest first, with the
        /// column changes inline. This is what a "History" button on a master
        /// screen calls.
        /// </summary>
        [HttpGet("GetRecordHistory/{tableName}/{recordID}/{subscID}")]
        public async Task<IActionResult> GetRecordHistory(
            string tableName,
            string recordID,
            int subscID,
            [FromQuery] int maxEntries = 200)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(recordID))
                return BadRequestResult("Table name and Record ID are required.");

            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var entries = await db.AuditLogView
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.TableName == tableName &&
                                x.RecordID == recordID)
                    .OrderByDescending(x => x.AuditNo)
                    .Take(maxEntries < 1 ? 200 : maxEntries)
                    .ToListAsync();

                return OkResult(
                    await WithChanges(entries),
                    entries.Count > 0 ? "Data found." : "No changes recorded for this record.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// A document and every line under it — the requisition header plus its
        /// items, the customer plus its contacts, terms and addresses. Takes
        /// the MASTER table and its key; the detail rows are found through
        /// AuditLog.ParentTable / ParentRecordID, which the triggers stamp.
        /// </summary>
        [HttpGet("GetDocumentHistory/{tableName}/{recordID}/{subscID}")]
        public async Task<IActionResult> GetDocumentHistory(
            string tableName,
            string recordID,
            int subscID,
            [FromQuery] int maxEntries = 500)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(recordID))
                return BadRequestResult("Table name and Record ID are required.");

            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var entries = await db.AuditLogView
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                ((x.TableName == tableName && x.RecordID == recordID) ||
                                 (x.ParentTable == tableName && x.ParentRecordID == recordID)))
                    .OrderByDescending(x => x.AuditNo)
                    .Take(maxEntries < 1 ? 500 : maxEntries)
                    .ToListAsync();

                return OkResult(
                    await WithChanges(entries),
                    entries.Count > 0 ? "Data found." : "No changes recorded for this document.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// The module and action filters for the log screen, and the list of
        /// users who actually appear in the trail — so the dropdowns offer only
        /// values that will return something.
        /// </summary>
        [HttpGet("GetFilters/{subscID}")]
        public async Task<IActionResult> GetFilters(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var modules = await db.AuditConfig
                    .AsNoTracking()
                    .Where(x => x.IsActive == "Y")
                    .OrderBy(x => x.ModuleName)
                    .Select(x => new { x.TableName, x.ModuleName, x.ParentTable })
                    .ToListAsync();

                var users = await db.AuditLogView
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.ChangedBy != null)
                    .Select(x => new { x.ChangedBy, x.ChangedByName })
                    .Distinct()
                    .OrderBy(x => x.ChangedByName)
                    .ToListAsync();

                var actions = new[] { "Insert", "Update", "SoftDelete", "Restore", "Delete" };

                return OkResult(new { modules, users, actions });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Coverage report: which tables are audited, whether their trigger is
        /// in place and enabled, and how much they have logged. Two result sets
        /// from SP_AuditLog_Status, flattened here into one payload.
        /// </summary>
        [HttpGet("GetStatus")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var covered = await db.AuditConfig
                    .AsNoTracking()
                    .OrderBy(x => x.TableName)
                    .Select(x => new
                    {
                        x.TableName,
                        x.ModuleName,
                        x.KeyColumn,
                        x.ParentTable,
                        x.CaptureSnapshot,
                        x.IsActive,
                        LoggedRows = db.AuditLog.Count(a => a.TableName == x.TableName)
                    })
                    .ToListAsync();

                return OkResult(new { tables = covered.Count, covered });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Registers tables added since the last run and builds their triggers.
        /// Run this after any schema change that adds a table.
        /// </summary>
        [HttpPost("Sync")]
        public async Task<IActionResult> Sync(
            [FromQuery] string table = "",
            [FromQuery] bool rebuild = false)
        {
            try
            {
                var message = await db.CallAuditLogSyncSP(table, rebuild);
                return OkResult(null, message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Retention. The procedure refuses anything under 30 days, so a
        /// mistyped value cannot wipe the trail.
        /// </summary>
        [HttpDelete("Purge")]
        public async Task<IActionResult> Purge(
            [FromQuery] int keepDays = 730,
            [FromQuery] int subscID = 0)
        {
            try
            {
                var message = await db.CallAuditLogPurgeSP(
                    keepDays,
                    subscID > 0 ? subscID : null);

                return OkResult(null, message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //======================================================================
        // Helpers
        //======================================================================

        /// <summary>
        /// Column changes for a set of entries, in one round trip rather than
        /// one per entry.
        /// </summary>
        private async Task<List<AuditLogDtl>> ChangesFor(IReadOnlyCollection<long> auditNos)
        {
            if (auditNos.Count == 0)
                return new List<AuditLogDtl>();

            return await db.AuditLogDtl
                .AsNoTracking()
                .Where(d => auditNos.Contains(d.AuditNo))
                .OrderBy(d => d.AuditNo)
                .ThenBy(d => d.TransSlNo)
                .ToListAsync();
        }

        /// <summary>Attaches each entry's column changes to it.</summary>
        private async Task<object> WithChanges(List<AuditLogView> entries)
        {
            var changes = await ChangesFor(entries.Select(e => e.AuditNo).ToList());

            var byEntry = changes
                .GroupBy(d => d.AuditNo)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(d => new { d.ColumnName, d.OldValue, d.NewValue }).ToList());

            var none = byEntry.Values.FirstOrDefault()?.Take(0).ToList()
                       ?? new List<AuditLogDtl>().Select(d => new { d.ColumnName, d.OldValue, d.NewValue }).ToList();

            return entries.Select(e => new
            {
                e.AuditNo,
                e.TableName,
                e.ModuleName,
                e.RecordID,
                e.ParentTable,
                e.ParentRecordID,
                e.ActionType,
                e.ChangeCount,
                e.Reason,
                e.ChangedBy,
                e.ChangedByName,
                e.ChangedByLogin,
                e.ChangedOn,
                e.ChangedDate,
                e.ChangedTime,
                e.SystemIP,
                e.HostName,
                changes = byEntry.TryGetValue(e.AuditNo, out var c) ? c : none
            });
        }
    }
}
