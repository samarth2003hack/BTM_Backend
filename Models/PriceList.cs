using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // What one vendor charges for one part, as at EffectiveDt. A vendor/part pair
    // holds many rows over time — the history is the point — so there is no
    // unique key on the pair; SP_PriceList only rejects a second live rate for
    // the same vendor/part/grade on the same date.
    [Table("PriceList")]
    public class PriceList
    {
        [Key]
        public int MTransNo { get; set; }

        public int VendorNo { get; set; }
        public int PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public DateTime? EffectiveDt { get; set; }
        public string? ItemRemarks { get; set; }
        public int? LeadTime { get; set; }

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

    // Parameters for SP_PriceList. Status = 'Insert' | 'Update' | 'Delete'.
    // The procedure fills Unit from the part's primary unit, Grade from the
    // grade row when only GradeNo is sent, and EffectiveDt with today.
    public class SP_PriceList
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public int VendorNo { get; set; }
        public int PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public DateTime? EffectiveDt { get; set; }
        public string? ItemRemarks { get; set; }
        public int? LeadTime { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
