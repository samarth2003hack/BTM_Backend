using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // QtnMst — Sales Quotation Header (with amendment)
    // ============================================================
    [Table("QtnMst")]
    public class QtnMst
    {
        [Key] public int MTransNo { get; set; }

        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public int? DocYear { get; set; }
        public string? DocSeries { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }
        public string? CPName { get; set; }
        public string? CPDesignation { get; set; }
        public string? CPPhone { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? SalesExec { get; set; }
        public DateTime? TargetDate { get; set; }
        public string? RefProject { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        public string? DocStatus { get; set; }   // CHAR(1) — read-only from DB
        public string? TaxType { get; set; }

        public decimal? BasicValue { get; set; }
        public decimal? ExemptValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        public decimal? OtherAmt { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public string? StateName { get; set; }
        public string? FileAttach { get; set; }
        public string? Zone { get; set; }
        public string? QtnCategory { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? VehicleModel { get; set; }

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

        // Set by SP_QtnApproval 'Reject' (LockStatus 'R'); cleared on approval.
        public string? RejectReason { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedOn { get; set; }
    }

    public class SP_QtnMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? PhoneNo { get; set; }
        public string? EmailID { get; set; }
        public string? CPName { get; set; }
        public string? CPDesignation { get; set; }
        public string? CPPhone { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? SalesExec { get; set; }
        public DateTime? TargetDate { get; set; }
        public string? RefProject { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert
        public string? TaxType { get; set; }

        public decimal? BasicValue { get; set; }
        public decimal? ExemptValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        public decimal? OtherAmt { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }
        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public string? StateName { get; set; }
        public string? FileAttach { get; set; }
        public string? Zone { get; set; }
        public string? QtnCategory { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? VehicleModel { get; set; }

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
    // QtnDtl — Sales Quotation Line Items
    // ============================================================
    [Table("QtnDtl")]
    public class QtnDtl
    {
        [Key] public int TransSlNo { get; set; }

        public decimal? SLNo { get; set; }
        public int MTransNo { get; set; }
        public int? LinkRefYear { get; set; }
        public int? LinkRefNo { get; set; }
        public int? LinkRefId { get; set; }
        public int PartNo { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? CustPartNo { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public decimal Qty { get; set; }            // NOT NULL
        public decimal ConvQty { get; set; }        // NOT NULL
        public int? TxnUnit { get; set; }
        public string? Unit { get; set; }
        public decimal Rate { get; set; }           // NOT NULL
        public decimal BasicValue { get; set; }     // NOT NULL
        public decimal? DiscP { get; set; }
        public decimal? DiscAmt { get; set; }
        public decimal? PnFP { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? FreightRate { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? GSTP { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public decimal? TotalValue { get; set; }
        public int? LinkNo { get; set; }
        public string? PartCode { get; set; }
        public string? MocSysCode { get; set; }
        public string? MocSysDesc { get; set; }
        public string? MocCode { get; set; }
        public string? MocDescription { get; set; }
        public string? ValveCode { get; set; }
        public string? ValveDescription { get; set; }
        public string? MOCShort { get; set; }
        public string? Deviation { get; set; }
        public string? SpecialReq { get; set; }
        public string? PartSysCode { get; set; }
        public string? PartSysDesc { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdMode { get; set; }

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

    public class SP_QtnDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SLNo { get; set; }
        public int? LinkRefYear { get; set; }
        public int? LinkRefNo { get; set; }
        public int? LinkRefId { get; set; }
        public int? PartNo { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? CustPartNo { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public decimal? Qty { get; set; }
        public decimal? ConvQty { get; set; }
        public int? TxnUnit { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public decimal? BasicValue { get; set; }
        public decimal? DiscP { get; set; }
        public decimal? DiscAmt { get; set; }
        public decimal? PnFP { get; set; }
        public decimal? PnFAmt { get; set; }
        public decimal? FreightRate { get; set; }
        public decimal? FreightAmt { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? GSTP { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public decimal? TotalValue { get; set; }
        public int? LinkNo { get; set; }
        public string? PartCode { get; set; }
        public string? MocSysCode { get; set; }
        public string? MocSysDesc { get; set; }
        public string? MocCode { get; set; }
        public string? MocDescription { get; set; }
        public string? ValveCode { get; set; }
        public string? ValveDescription { get; set; }
        public string? MOCShort { get; set; }
        public string? Deviation { get; set; }
        public string? SpecialReq { get; set; }
        public string? PartSysCode { get; set; }
        public string? PartSysDesc { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdMode { get; set; }

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
    // QtnTerms — Sales Quotation Terms & Conditions
    // ============================================================
    [Table("QtnTerms")]
    public class QtnTerms
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

    public class SP_QtnTerms
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
    // QtnExtras — Sales Quotation Extra Charges
    // ============================================================
    [Table("QtnExtras")]
    public class QtnExtras
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

    public class SP_QtnExtras
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
    // QTNNotes — Sales Quotation Notes
    // ============================================================
    [Table("QTNNotes")]
    public class QTNNotes
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? Notes { get; set; }

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

    public class SP_QTNNotes
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? Notes { get; set; }

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
    // QtnAttachment — Sales Quotation Attachments
    // ============================================================
    [Table("QtnAttachment")]
    public class QtnAttachment
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? AttNotes { get; set; }
        public string? AttFile { get; set; }

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

    public class SP_QtnAttachment
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public string? AttNotes { get; set; }
        public string? AttFile { get; set; }

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
