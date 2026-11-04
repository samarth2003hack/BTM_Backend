using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // Chart-of-accounts group tree.
    //   GrpStatus 'P' = primary group (root), 'S' = sub-group.
    //   GrpMastNo is the parent GrpNo; NULL for a primary group.
    //   BSStatus places the group on the Balance Sheet or the P&L.
    [Table("GroupHeads")]
    public class GroupHeads
    {
        [Key]
        public int GrpNo { get; set; }

        public string? GrpName { get; set; }
        public int? GrpMastNo { get; set; }
        public string? GrpStatus { get; set; }
        public string? BSStatus { get; set; }
        public string? FAStatus { get; set; }

        public string? SystemIP { get; set; }
        public string? LockStatus { get; set; }
        public string? DeleteStatus { get; set; }
        public int? LockCounter { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    // Read-only projection of dbo.GroupView — the GroupHeads tree flattened to
    // four levels with a sortable path (LNo) for hierarchical reports.
    public class GroupView
    {
        public string? Level { get; set; }
        public string? LNo { get; set; }
        public int GrpNo { get; set; }
        public string? GrpName { get; set; }
        public int? GrpMastNo { get; set; }
        public string? BSStatus { get; set; }
        public string? GrpStatus { get; set; }
        public string? FAStatus { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_GroupHeads. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_GroupHeads
    {
        public int GrpNo { get; set; }
        public int MCount { get; set; }

        public string GrpName { get; set; } = string.Empty;
        public int? GrpMastNo { get; set; }
        public string GrpStatus { get; set; } = "S";
        public string BSStatus { get; set; } = string.Empty;
        public string? FAStatus { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class GroupHeadsResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
