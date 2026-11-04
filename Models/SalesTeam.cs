using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    public class SalesTeam
    {
        [Key]
        public int MTransNo { get; set; }

        public string? SalesManager { get; set; }
        public string? SalesMan { get; set; }

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


    public class SP_SalesTeam
    {
        public int MTransNo { get; set; }

        public int MCount { get; set; }

        public string? SalesManager { get; set; }
        public string? SalesMan { get; set; }

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