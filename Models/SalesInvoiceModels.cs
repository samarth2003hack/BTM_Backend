using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // STxnMst — Sales (Tax) Invoice Header
    // Target* = the Order Acceptance the invoice bills.
    // ============================================================
    [Table("STxnMst")]
    public class STxnMst
    {
        [Key] public int MTransNo { get; set; }

        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int DocYear { get; set; }
        public string? DocSeries { get; set; }
        public string? InvType { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

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
        [Column(TypeName = "numeric(16,2)")] public decimal? ExemptValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TaxableValue { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? DiscountAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? PnFGSTP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFGSTAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? FrightGSTP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? FrightGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? FreightAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? IGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? CGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? OtherAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? OtherP { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        public decimal? Advance { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? Grossweight { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? Netweight { get; set; }

        public string? TxnCurrency { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? CurrencyRate { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? CurrencyAmount { get; set; }

        public string? SalesRep { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? IECodeNo { get; set; }
        public string? Declaration { get; set; }
        public int? BankNo { get; set; }
        public string? BankName { get; set; }
        public string? InvTag { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }

        public string? TransportName { get; set; }
        public string? TransportID { get; set; }
        public string? LUTNo { get; set; }
        public string? PrintType { get; set; }

        public string? IRNNo { get; set; }
        public string? ScanQRNo { get; set; }
        public string? ACKNo { get; set; }
        public string? AckDt { get; set; }
        public string? EWBillNo { get; set; }
        public string? EWBillDt { get; set; }

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

    public class SP_STxnMst
    {
        public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }          // ignored on Insert — the SP allocates it
        public DateTime? DocDate { get; set; }
        public string? InvType { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        public int? VendorNo { get; set; }
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
        public string? TaxType { get; set; }

        public decimal? BasicValue { get; set; }
        public decimal? ExemptValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? DiscountAmt { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? PnFGSTP { get; set; }
        public decimal? PnFGSTAmt { get; set; }
        public decimal? FrightGSTP { get; set; }
        public decimal? FrightGSTAmt { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        public decimal? OtherAmt { get; set; }
        public decimal? OtherP { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        public decimal? Advance { get; set; }
        public decimal? Grossweight { get; set; }
        public decimal? Netweight { get; set; }

        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public string? SalesRep { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? IECodeNo { get; set; }
        public string? Declaration { get; set; }
        public int? BankNo { get; set; }
        public string? BankName { get; set; }
        public string? InvTag { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }

        public string? TransportName { get; set; }
        public string? TransportID { get; set; }
        public string? LUTNo { get; set; }
        public string? PrintType { get; set; }

        // E-invoice / e-way stamps — written only when supplied (SP keeps the
        // stored value on NULL), so saving the form never wipes them.
        public string? IRNNo { get; set; }
        public string? ScanQRNo { get; set; }
        public string? ACKNo { get; set; }
        public string? AckDt { get; set; }
        public string? EWBillNo { get; set; }
        public string? EWBillDt { get; set; }

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
    // STxnDtl — Sales Invoice Lines (LinkNo = OADtl.TransSlNo)
    // ============================================================
    [Table("STxnDtl")]
    public class STxnDtl
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public int? SlNo { get; set; }
        public int? LinkNo { get; set; }
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

    public class SP_STxnDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int? SlNo { get; set; }           // ignored on Insert — the SP allocates it
        public int? LinkNo { get; set; }
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
    // StxnTerms — Sales Invoice Terms
    // ============================================================
    [Table("StxnTerms")]
    public class StxnTerms
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

    public class SP_StxnTerms
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
    // STxnExtra — the invoice's answered extra terms
    // ============================================================
    [Table("STxnExtra")]
    public class STxnExtra
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

    public class SP_STxnExtra
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
    // TransportMaster — transporters printed on dispatch documents
    // ============================================================
    [Table("TransportMaster")]
    public class TransportMaster
    {
        [Key] public int MTransNo { get; set; }

        public string? TransportName { get; set; }
        public string? TransportID { get; set; }

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

    // Parameters for SP_TransportMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_TransportMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string TransportName { get; set; } = string.Empty;
        public string? TransportID { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
