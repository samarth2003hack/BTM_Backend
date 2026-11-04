using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // ProformaMst — Proforma Invoice Header
    // Bills an Order Acceptance in advance: Target* = the OA.
    // DocNo is numbered per DocSeries + DocYear + InvType.
    // ============================================================
    [Table("ProformaMst")]
    public class ProformaMst
    {
        [Key] public int MTransNo { get; set; }

        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int DocYear { get; set; }
        public string? DocSeries { get; set; }
        public string? InvType { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }

        public int? TargetDocNo { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public int? TargetDocYear { get; set; }

        public int VendorNo { get; set; }
        public int? FromAccount { get; set; }
        public int? VendorNoShip { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }
        public string? TaxType { get; set; }

        [Column(TypeName = "numeric(16,2)")] public decimal? BasicValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? DiscountAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? ExemptValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TaxableValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? PnFGSTP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? FreightAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? FrightGSTP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? FrightGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? IGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? CGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? OtherAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? OtherP { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        [Column(TypeName = "numeric(12,0)")] public decimal? AdvanceP { get; set; }
        public decimal? Advance { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? Grossweight { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? Netweight { get; set; }

        public string? TxnCurrency { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? CurrencyRate { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? CurrencyAmount { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? oldCurrencyRate { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? oldCurrencyAmount { get; set; }

        public int? ProjectNo { get; set; }
        public string? SalesRep { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? IECodeNo { get; set; }
        public string? Declaration { get; set; }
        public int? BankNo { get; set; }
        public string? InvTag { get; set; }

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

    public class SP_ProformaMst
    {
        public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }            // preview only on a first issue — SP allocates
        public DateTime? DocDate { get; set; }
        public string? InvType { get; set; }        // Domestic | Export — splits the numbering
        public int? AmdNo { get; set; }             // 0 = first issue (the page has no amendments)
        public string? AmdNotes { get; set; }

        public int? TargetTransNo { get; set; }     // OAMst.MTransNo
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        // Customer snapshot — the controller copies these from the OA.
        public int? VendorNo { get; set; }
        public int? VendorNoShip { get; set; }
        public int? FromAccount { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? StateName { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? TaxType { get; set; }

        public decimal? BasicValue { get; set; }
        public decimal? DiscountAmt { get; set; }
        public decimal? ExemptValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? PnFGSTP { get; set; }
        public decimal? PnFGSTAmt { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? FrightGSTP { get; set; }
        public decimal? FrightGSTAmt { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        public decimal? OtherAmt { get; set; }
        public decimal? OtherP { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        public decimal? AdvanceP { get; set; }
        public decimal? Advance { get; set; }
        public decimal? Grossweight { get; set; }
        public decimal? Netweight { get; set; }

        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public int? ProjectNo { get; set; }
        public string? SalesRep { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? IECodeNo { get; set; }
        public string? Declaration { get; set; }
        public int? BankNo { get; set; }
        public string? InvTag { get; set; }

        /// <summary>
        /// Not a column: the document's total GST as the page computed it. When
        /// IGST/CGST/SGST are all left empty the controller splits this one
        /// figure by place of supply (see ProformaController.SplitGst).
        /// </summary>
        public decimal? GSTAmt { get; set; }

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
    // ProformaDtl — Proforma Invoice Lines
    // LinkNo = OADtl.TransSlNo of the OA line being billed.
    // ============================================================
    [Table("ProformaDtl")]
    public class ProformaDtl
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public int? SlNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? Description { get; set; }
        public string? ItemRemarks { get; set; }
        public string? CustPartNo { get; set; }
        public string? HSNCode { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal Qty { get; set; }
        public string? Unit { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal Rate { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal BasicValue { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? DiscP { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? DiscAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? PnFP { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? PnFAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? FreightRate { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? FreightAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? FreightGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? TaxableValue { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? GSTP { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? IGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? CGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? SGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TotalValue { get; set; }
        public int? DTargetDocNo { get; set; }
        public int? DTargetDocYear { get; set; }
        public string? Packing { get; set; }
        public int? BoxNo { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? NetWeight { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? RegdNo { get; set; }
        public int? LinkNo { get; set; }
        public DateTime? DeleveryDate { get; set; }
        public string? Store { get; set; }
        public int? VendorNo { get; set; }

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

    public class SP_ProformaDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int? SlNo { get; set; }               // SP allocates on Insert
        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? Description { get; set; }
        public string? ItemRemarks { get; set; }
        public string? CustPartNo { get; set; }
        public string? HSNCode { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public decimal? BasicValue { get; set; }
        public decimal? DiscP { get; set; }
        public decimal? DiscAmt { get; set; }
        public decimal? PnFP { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? PnFGSTAmt { get; set; }
        public decimal? FreightRate { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? FreightGSTAmt { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? GSTP { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public decimal? TotalValue { get; set; }
        public int? DTargetDocNo { get; set; }
        public int? DTargetDocYear { get; set; }
        public string? Packing { get; set; }
        public int? BoxNo { get; set; }
        public decimal? NetWeight { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? RegdNo { get; set; }
        public int? LinkNo { get; set; }             // OADtl.TransSlNo
        public DateTime? DeleveryDate { get; set; }
        public string? Store { get; set; }
        public int? VendorNo { get; set; }

        /// <summary>Not a column: the line's total GST, split by the controller.</summary>
        public decimal? GSTAmt { get; set; }

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
    // ProformaTerms — Proforma Invoice Terms
    // ============================================================
    [Table("ProformaTerms")]
    public class ProformaTerms
    {
        [Key] public int TransSlNo { get; set; }
        public int? MTransNo { get; set; }
        [Column(TypeName = "numeric(6,2)")] public decimal? SlNo { get; set; }
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

    public class SP_ProformaTerms
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
    // ProformaExtra — Proforma Invoice Extra lines
    // ============================================================
    [Table("ProformaExtra")]
    public class ProformaExtra
    {
        [Key] public int TransSlNo { get; set; }
        public int? MTransNo { get; set; }
        [Column(TypeName = "numeric(6,2)")] public decimal? SlNo { get; set; }
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

    public class SP_ProformaExtra
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
}
