using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    public class MechanicalMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? MechanicalCode { get; set; }
        public string? MechanicalName { get; set; }
        public string? ShortName { get; set; }
        public string? Unit { get; set; }

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


    public class SP_MechanicalMaster
    {
        public int MTransNo { get; set; }

        public int MCount { get; set; }

        public string? MechanicalCode { get; set; }
        public string? MechanicalName { get; set; }
        public string? ShortName { get; set; }
        public string? Unit { get; set; }

        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }

        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteReason { get; set; }

        public string? LockStatus { get; set; }

        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }
}