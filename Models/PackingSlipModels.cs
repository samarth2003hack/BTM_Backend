using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // PackingSlipMst — Packing Slip header (no amendments)
    // Packs the lines of ONE Order Acceptance: TargetTransNo /
    // TargetDocYear / TargetDocNo / TargetAmdNo and OAYear / OANo /
    // OADate all describe that OA. The customer block is a snapshot
    // of the OA header, taken by the controller at save time.
    // ============================================================
    [Table("PackingSlipMst")]
    public class PackingSlipMst
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

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public DateTime? OADate { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }   // CHAR(10) — 'N' open, 'Y' invoiced; read-only from DB
        public int? TotalCases { get; set; }
        public decimal? TotalWeight { get; set; }

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

    public class SP_PackingSlipMst
    {
        public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }          // allocated by the SP on Insert
        public DateTime? DocDate { get; set; }

        public int? TargetTransNo { get; set; }  // OAMst.MTransNo — the page sends this
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

        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public DateTime? OADate { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert
        public int? TotalCases { get; set; }
        public decimal? TotalWeight { get; set; }

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
    // PackingSlipDtl — one row per OA line per case.
    // OAID = OADtl.TransSlNo. One OA line may be split across
    // several cases, but only once per CaseNo.
    // ============================================================
    [Table("PackingSlipDtl")]
    public class PackingSlipDtl
    {
        [Key] public int TransSlNo { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int OAID { get; set; }
        public int CaseNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? MOC { get; set; }
        public string? OARemarks { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        [Column(TypeName = "numeric(12,4)")]
        public decimal Qty { get; set; }            // NOT NULL
        public string? Unit { get; set; }
        public string? CaseSize { get; set; }
        [Column(TypeName = "numeric(12,2)")]
        public decimal? NetWeight { get; set; }
        [Column(TypeName = "numeric(12,2)")]
        public decimal? GrossWeight { get; set; }
        public string? ValveSlNo { get; set; }
        public string? BoxType { get; set; }

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

    public class SP_PackingSlipDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }       // allocated by the SP on Insert
        public int? OAID { get; set; }
        public int? CaseNo { get; set; }
        // Part / MOC / OA remark / unit / HSN are snapshotted from the OA
        // line by the controller; whatever the client sends is overwritten.
        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? MOC { get; set; }
        public string? OARemarks { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public string? CaseSize { get; set; }
        public decimal? NetWeight { get; set; }
        public decimal? GrossWeight { get; set; }
        public string? ValveSlNo { get; set; }
        public string? BoxType { get; set; }

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
