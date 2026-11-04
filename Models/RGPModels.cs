using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // RGPMst — Returnable Gate Pass Outward Header
    // ============================================================
    [Table("RGPMst")]
    public class RGPMst
    {
        [Key] public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public string? RGPType { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }  // CHAR(1) — read-only from DB
        public decimal? ApproxValue { get; set; }

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

    public class SP_RGPMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public string? RGPType { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert
        public decimal? ApproxValue { get; set; }

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

    // ============================================================
    // RGPDtl — Returnable Gate Pass Outward Detail
    // ============================================================
    [Table("RGPDtl")]
    public class RGPDtl
    {
        [Key] public int TransSlNo { get; set; }

        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public decimal Qty { get; set; }        // NOT NULL
        public string? Unit { get; set; }
        public decimal Rate { get; set; }       // NOT NULL
        public decimal? Amount { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

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

    public class SP_RGPDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Amount { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

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

    // ============================================================
    // RGPTerms — Returnable Gate Pass Terms
    // ============================================================
    [Table("RGPTerms")]
    public class RGPTerms
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }

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

    public class SP_RGPTerms
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }

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

    // ============================================================
    // RGPInwMst — Returnable Gate Pass Inward Header
    // ============================================================
    [Table("RGPInwMst")]
    public class RGPInwMst
    {
        [Key] public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public string? TargetType { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? InwardNo { get; set; }
        public DateTime? InwardDate { get; set; }
        public string? LRNo { get; set; }
        public DateTime? LRDate { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }

        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }  // CHAR(1) — read-only from DB

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

    public class SP_RGPInwMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public string? TargetType { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? InwardNo { get; set; }
        public DateTime? InwardDate { get; set; }
        public string? LRNo { get; set; }
        public DateTime? LRDate { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }

        public string? DocNotes { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert

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

    // ============================================================
    // RGPInwDtl — Returnable Gate Pass Inward Detail
    // ============================================================
    [Table("RGPInwDtl")]
    public class RGPInwDtl
    {
        [Key] public int TransSlNo { get; set; }

        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public decimal? RGPQty { get; set; }
        public decimal Qty { get; set; }        // NOT NULL
        public string? Unit { get; set; }
        public decimal Rate { get; set; }       // NOT NULL
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

        // RGPDtl.TransSlNo this line receives against (SQL 28).
        public int? LinkNo { get; set; }

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

    public class SP_RGPInwDtl
    {
        // RGPDtl.TransSlNo being received. SP_RGPInwDtl takes part, grade,
        // unit, rate and RGPQty off that line — the client's copies are ignored.
        public int? LinkNo { get; set; }

        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public decimal? RGPQty { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

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
