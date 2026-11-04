using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("CurrencyMaster")]
    public class CurrencyMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? Country { get; set; }
        public string? Currency { get; set; }
        public string? CurrencyCode { get; set; }

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

    public class SP_CurrencyMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? Country { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class CurrencyMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}