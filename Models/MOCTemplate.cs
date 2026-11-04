using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    // The table (and SP_MOCTemplate) is dbo.MOCTemplate. Without this the DbSet
    // name makes EF query "MOCTemplates", which does not exist.
    [System.ComponentModel.DataAnnotations.Schema.Table("MOCTemplate")]
    public class MOCTemplate
    {
        [Key]
        public int MTransNo { get; set; }

        public string? ValveType { get; set; }
        public string? MOCParameter { get; set; }
        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }

        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }

        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public int? SubscID { get; set; }
    }

    public class SP_MOCTemplate
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string ValveType { get; set; } = string.Empty;
        public string MOCParameter { get; set; } = string.Empty;

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";

        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;

        public string? DeleteReason { get; set; }   // ← was missing

        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
