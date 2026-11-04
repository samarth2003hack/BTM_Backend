using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // The data-change trail. Written entirely by the TR_Audit_* triggers — see
    // SQL/12_AuditLog.sql — so nothing in the API ever inserts into it. These
    // types exist to read it back.

    // One row per record changed.
    [Table("AuditLog")]
    public class AuditLog
    {
        [Key]
        public long AuditNo { get; set; }

        public string TableName { get; set; } = string.Empty;
        public string? ModuleName { get; set; }
        public string RecordID { get; set; } = string.Empty;

        // Set on detail tables, so every line of a document can be found from
        // the document's own key.
        public string? ParentTable { get; set; }
        public string? ParentRecordID { get; set; }

        // Insert | Update | Delete | SoftDelete | Restore
        public string ActionType { get; set; } = string.Empty;

        public int ChangeCount { get; set; }
        public string? Reason { get; set; }

        // The whole row as JSON, and only for tables whose AuditConfig row asks
        // for it. Off everywhere by default.
        public string? RecordSnapshot { get; set; }

        public int? ChangedBy { get; set; }
        public DateTime ChangedOn { get; set; }
        public string? SystemIP { get; set; }
        public string? HostName { get; set; }
        public string? AppName { get; set; }
        public string? DBUser { get; set; }
        public int? SubscID { get; set; }
    }

    // One row per column changed.
    [Table("AuditLogDtl")]
    public class AuditLogDtl
    {
        [Key]
        public long TransSlNo { get; set; }

        public long AuditNo { get; set; }
        public string ColumnName { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    // dbo.VW_AuditLog — the trail with the user's name resolved and the
    // timestamp split into date and time, matching the shape the AccessLog and
    // ErrorLog grids already consume.
    public class AuditLogView
    {
        public long AuditNo { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string? ModuleName { get; set; }
        public string RecordID { get; set; } = string.Empty;
        public string? ParentTable { get; set; }
        public string? ParentRecordID { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public int ChangeCount { get; set; }
        public string? Reason { get; set; }
        public int? ChangedBy { get; set; }
        public string? ChangedByName { get; set; }
        public string? ChangedByLogin { get; set; }
        public DateTime ChangedOn { get; set; }
        public DateTime ChangedDate { get; set; }
        public string? ChangedTime { get; set; }
        public string? SystemIP { get; set; }
        public string? HostName { get; set; }
        public string? AppName { get; set; }
        public string? DBUser { get; set; }
        public int? SubscID { get; set; }
    }

    // Which tables are audited, and what to call them. Edited by hand; the API
    // only reads it, to fill the module filter on the log screen.
    [Table("AuditConfig")]
    public class AuditConfig
    {
        [Key]
        public int MTransNo { get; set; }

        public string TableName { get; set; } = string.Empty;
        public string? ModuleName { get; set; }
        public string KeyColumn { get; set; } = string.Empty;
        public string? ParentTable { get; set; }
        public string? ParentKeyColumn { get; set; }
        public string CaptureSnapshot { get; set; } = "N";
        public string IsActive { get; set; } = "Y";
        public DateTime CreatedOn { get; set; }
    }
}
