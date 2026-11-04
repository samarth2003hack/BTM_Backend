
using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    
    public class MOCMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? ValveType { get; set; }

        public string? CodeDetails { get; set; }

        public string MOCCode { get; set; } = "";

        public string? MOCDetails { get; set; }

        public string MOCDescription { get; set; } = "";

        public string MOCShort { get; set; } = "";

        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; }

        public int? ModifiedBy { get; set; }

        public DateTime? ModifiedOn { get; set; }

        public string DeleteStatus { get; set; } = "N";

        public string? DeleteReason { get; set; }

        public string LockStatus { get; set; } = "N";

        public int? LockCounter { get; set; }

        public int? SubscID { get; set; }
    }

    public class SP_MOCMaster
    {
        public int MTransNo { get; set; }

        public int MCount { get; set; }

        public string? ValveType { get; set; }

        public string? CodeDetails { get; set; }

        public string MOCCode { get; set; } = "";

        public string? MOCDetails { get; set; }

        public string MOCDescription { get; set; } = "";

        public string MOCShort { get; set; } = "";

        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.Now.AddMinutes(-150);

        public int? ModifiedBy { get; set; }

        public DateTime? ModifiedOn { get; set; }

        public string DeleteStatus { get; set; } = "N";

        public string? DeleteReason { get; set; }

        public string LockStatus { get; set; } = "N";

        public int? LockCounter { get; set; }

        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }
}


