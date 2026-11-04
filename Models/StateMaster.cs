using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("StateMaster")]
    public class StateMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? State { get; set; }
        public int? StateCode { get; set; }

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

    // Parameters for SP_StateMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_StateMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string State { get; set; } = string.Empty;
        public int StateCode { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class StateMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
