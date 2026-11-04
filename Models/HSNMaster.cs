using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // HSN codes. PartMaster.HSNCode holds the code as text rather than a key, so
    // SP_HSNMaster checks by value before letting a code be deleted.
    [Table("HSNMaster")]
    public class HSNMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? HSNCode { get; set; }
        public string? HSNDescription { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_HSNMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_HSNMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string HSNCode { get; set; } = string.Empty;
        public string? HSNDescription { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
