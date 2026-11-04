using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("BankMaster")]
    public class BankMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? BankCode { get; set; }
        public string? BankType { get; set; }
        public string? BankName { get; set; }
        public string? BankID { get; set; }
        public string? IFSCCode { get; set; }
        public string? BranchName { get; set; }
        public string? BranchCode { get; set; }
        public string? Location { get; set; }
        public string? MICRCode { get; set; }

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

    // Parameters for SP_BankMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_BankMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string BankCode { get; set; } = string.Empty;
        public string BankType { get; set; } = "BANK";
        public string BankName { get; set; } = string.Empty;
        public string? BankID { get; set; }
        public string? IFSCCode { get; set; }
        public string? BranchName { get; set; }
        public string? BranchCode { get; set; }
        public string? Location { get; set; }
        public string? MICRCode { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class BankMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
