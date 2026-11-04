using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("BranchMaster")]
    public class BranchMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? BranchCode { get; set; }
        public string? BranchType { get; set; }
        public string? BranchName { get; set; }
        public string? BranchAddress { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }
        public int? PinCode { get; set; }
        public string? GSTIN { get; set; }
        public string? CIN { get; set; }
        public string? Remarks { get; set; }
        public int? BranchUser { get; set; }

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

    // Parameters for SP_BranchMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_BranchMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string BranchCode { get; set; } = string.Empty;
        public string BranchType { get; set; } = "BRANCH";
        public string BranchName { get; set; } = string.Empty;
        public string? BranchAddress { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }
        public int? PinCode { get; set; }
        public string? GSTIN { get; set; }
        public string? CIN { get; set; }
        public string? Remarks { get; set; }
        public int? BranchUser { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class BranchMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
