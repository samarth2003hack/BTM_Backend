using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("OtherMaster")]
    public class OtherMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? MasterType { get; set; }
        public string? MasterName { get; set; }
        public string? Department { get; set; }

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

    // Catalogue of the master types the Other Master page is allowed to edit.
    [Table("OtherMasterList")]
    public class OtherMasterList
    {
        [Key]
        public int MTransNo { get; set; }

        public string? MasterType { get; set; }
        public string? Department { get; set; }
        public string? DeleteStatus { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_OtherMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_OtherMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string MasterType { get; set; } = string.Empty;
        public string MasterName { get; set; } = string.Empty;
        public string? Department { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Parameters for SP_OtherMasterList. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_OtherMasterList
    {
        public int MTransNo { get; set; }
        public string MasterType { get; set; } = string.Empty;
        public string? Department { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class OtherMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
