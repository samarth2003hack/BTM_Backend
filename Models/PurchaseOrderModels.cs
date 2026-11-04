using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    /*  Purchase order: header, lines, terms, user fields — plus the four
        read-only views the order screen's source picker runs on.

        Numeric columns carry an explicit [Column(TypeName)]. Without it EF
        assumes decimal(18,2) and silently rounds a 4-decimal quantity or a
        6,2 percentage on the way through. The types here match
        SQL/13_PurchaseOrderGRNTables.sql column for column.

        Writes never go through these entities — they go through the SP_*
        procedures via the Call*SP helpers on EMSContext. The DbSets are for
        reading.  */

    // ============================================================
    // POMst
    // ============================================================
    [Table("POMst")]
    public class POMst
    {
        [Key] public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public int VendorNo { get; set; }
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
        public string? DocAmdNotes { get; set; }
        public string? DocStatus { get; set; }
        public string? TaxType { get; set; }

        [Column(TypeName = "numeric(16,2)")] public decimal? BasicValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? ExemptValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TaxableValue { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? PnFAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? FreightAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? IGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? CGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? SGSTAmt { get; set; }
        public string? OtherCharges { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? OtherAmt { get; set; }
        [Column(TypeName = "numeric(6,4)")]  public decimal? RoundOff { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? NetValue { get; set; }
        public string? TxnCurrency { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? CurrencyRate { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? CurrencyAmount { get; set; }

        public string? TargetType { get; set; }
        public string? PurSubject { get; set; }
        public int? VendorNoShip { get; set; }
        public string? BusinessUnit { get; set; }
        public string? Plant { get; set; }
        public string? Source { get; set; }

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

        // Set by SP_POApproval 'Reject' (LockStatus 'R'); cleared on approval.
        public string? RejectReason { get; set; }
        public int? RejectedBy { get; set; }
        public DateTime? RejectedOn { get; set; }

        /// <summary>The requisition behind the order, whichever route it came
        /// by: TargetTransNo itself for 'PR', resolved through the quote's RFQ
        /// for 'PQ'. Filled by the controller, not stored.</summary>
        [NotMapped] public int? PRTransNo { get; set; }

        /// <summary>The RFQ a 'PQ' order's quote answered. Not stored.</summary>
        [NotMapped] public int? RFQTransNo { get; set; }
    }

    /// <summary>
    /// Input to SP_POMst. Status drives the branch: Insert, Update, Delete,
    /// Lock, Unlock, Amend or Recalc.
    ///
    /// The vendor block (code, name, address, GSTIN, state, tax type) is NOT
    /// here on purpose — the procedure snapshots it from SupplierMaster, so an
    /// order keeps the address it was placed on. Totals are likewise absent:
    /// the procedure adds the order up from its own lines.
    /// </summary>
    public class SP_POMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }
        public int? TargetTransNo { get; set; }

        /// <summary>'PQ' from an approved supplier quote, 'PR' straight off a
        /// requisition, or 'Direct'.</summary>
        public string? TargetType { get; set; }

        public int? VendorNo { get; set; }
        public int? VendorNoShip { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        public string? PurSubject { get; set; }
        public string? BusinessUnit { get; set; }
        public string? Plant { get; set; }
        public string? Source { get; set; }
        public string? OtherCharges { get; set; }
        public decimal? OtherAmt { get; set; }
        public decimal? RoundOff { get; set; }
        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
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
    // PODtl
    // ============================================================
    [Table("PODtl")]
    public class PODtl
    {
        [Key] public int TransSlNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }

        [Column(TypeName = "numeric(12,4)")] public decimal Qty { get; set; }
        public string? Unit { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal Rate { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? MRP { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? BasicRate { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal BasicValue { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? DiscP { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? DiscAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? PnFP { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? PnFAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? FreightAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? TaxableValue { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? GSTP { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? IGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? CGSTAmt { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? SGSTAmt { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TotalValue { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? Qty2 { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? ConvRatio { get; set; }
        public string? TxnUnit { get; set; }
        public string? Unit2 { get; set; }

        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public int? OAYear { get; set; }
        public DateTime? DeleveryDate { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? CoreGrade { get; set; }
        public string? Spare { get; set; }
        public string? SpareGrade { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SpareQty { get; set; }
        public string? MtrlStd { get; set; }

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

    /// <summary>
    /// Input to SP_PODtl. Part code, description, HSN, units, conversion ratio
    /// and the GST percentage are absent by design: the procedure takes them
    /// from PartMaster. What is here is what is negotiable.
    /// </summary>
    public class SP_PODtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int? PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Qty2 { get; set; }
        public decimal? Rate { get; set; }
        public decimal? MRP { get; set; }
        public decimal? DiscP { get; set; }
        public decimal? PnFP { get; set; }
        public decimal? FreightAmt { get; set; }
        public DateTime? DeleveryDate { get; set; }
        // MaterialGrade.GradeNo of the part; when sent, the procedure takes
        // the grade name from the master (SQL/40).
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? MtrlStd { get; set; }
        public string? CoreGrade { get; set; }
        public string? Spare { get; set; }
        public string? SpareGrade { get; set; }
        public decimal? SpareQty { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // POTerms
    // ============================================================
    [Table("POTerms")]
    public class POTerms
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

    // ============================================================
    // POExtras
    // ============================================================
    [Table("POExtras")]
    public class POExtras
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

    /// <summary>Input to SP_POTerms and SP_POExtras — both take the same shape.</summary>
    public class SP_POLine
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
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    /// <summary>
    /// Input to SP_POGenerateFromQuote — the "approved quote, no item picker"
    /// path. Everything else about the order comes off the quote.
    /// </summary>
    public class SP_POGenerateFromQuote
    {
        /// <summary>RFQPriceMst.MTransNo of the approved supplier quote.</summary>
        public int QuoteTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? PurSubject { get; set; }
        public string? BusinessUnit { get; set; }
        public string? Plant { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public int? SubscID { get; set; }
    }

    // ============================================================
    // Read-only views behind the source picker and the GRN screen
    // ============================================================

    /// <summary>
    /// VW_PurReqForPO — approved requisitions, each already told apart:
    /// SourceMode 'Quote' means an approved supplier quote exists and the
    /// order generates itself; 'Items' means the buyer picks items by hand.
    /// </summary>
    public class PurReqForPO
    {
        public int PRTransNo { get; set; }
        public string? PRDocSeries { get; set; }
        public int PRDocYear { get; set; }
        public int PRDocNo { get; set; }
        public DateTime PRDocDate { get; set; }
        public short? PRDocAmdNo { get; set; }
        public string? ReqBy { get; set; }
        public string? ReqDept { get; set; }
        public string? Project { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        public string? RFQReq { get; set; }
        public string? LockStatus { get; set; }
        public int? SubscID { get; set; }
        public int LineCount { get; set; }
        public int RFQCount { get; set; }
        public int QuoteCount { get; set; }
        public int ApprovedQuotes { get; set; }
        public string? HasRFQ { get; set; }
        public string? SourceMode { get; set; }
    }

    /// <summary>
    /// VW_QuoteForPO — the approved quotes a requisition may be ordered from.
    /// POTransNo is non-null once one has already been ordered.
    /// </summary>
    public class QuoteForPO
    {
        public int? PRTransNo { get; set; }
        public int RFQTransNo { get; set; }
        public string? RFQDocSeries { get; set; }
        public int? RFQDocYear { get; set; }
        public int? RFQDocNo { get; set; }
        public DateTime? RFQDocDate { get; set; }
        public int QuoteTransNo { get; set; }
        public string? QuoteDocSeries { get; set; }
        public int? QuoteDocYear { get; set; }
        public int? QuoteDocNo { get; set; }
        public DateTime? QuoteDocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public short? QuoteDocAmdNo { get; set; }
        public int? VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? ContactNo { get; set; }
        public string? VendorAddress { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public int? PRYear { get; set; }
        public int? PRNo { get; set; }
        public string? LockStatus { get; set; }
        public int? SubscID { get; set; }
        public int LineCount { get; set; }
        [Column(TypeName = "numeric(18,4)")] public decimal QuoteValue { get; set; }
        public int? POTransNo { get; set; }
        public int? PODocYear { get; set; }
        public int? PODocNo { get; set; }
    }

    /// <summary>
    /// VW_PurReqItemsForPO — the item picker's rows, with everything already
    /// ordered (by either route) subtracted.
    /// </summary>
    public class PurReqItemsForPO
    {
        public int PRTransNo { get; set; }
        public string? PRDocSeries { get; set; }
        public int PRDocYear { get; set; }
        public int PRDocNo { get; set; }
        public DateTime PRDocDate { get; set; }
        public DateTime? DueDate { get; set; }
        public int? SubscID { get; set; }
        public int PRTransSlNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? PRSlNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? HSNCode { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? GSTP { get; set; }
        public string? PrimaryUnit { get; set; }
        public string? SecondaryUnit { get; set; }
        [Column(TypeName = "numeric(14,4)")] public decimal? ConversionRatio { get; set; }
        public string? TxnUnit { get; set; }
        // MaterialGrade of the requisition line (SQL/40).
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? ItemRemarks { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal? ReqQty { get; set; }
        public string? Unit { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal OrderedQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal? BalanceQty { get; set; }
    }

    /// <summary>
    /// VW_POPendingGRN — purchase order lines with something still to receive.
    /// Seeds the GRN screen: BalanceQty is the most a new receipt may bill.
    /// </summary>
    public class POPendingGRN
    {
        public int POTransNo { get; set; }
        public string? PODocSeries { get; set; }
        public int PODocYear { get; set; }
        public int PODocNo { get; set; }
        public DateTime PODocDate { get; set; }
        public short? PODocAmdNo { get; set; }
        public string? TargetType { get; set; }
        public int VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? TaxType { get; set; }
        public string? TxnCurrency { get; set; }
        public int? SubscID { get; set; }
        public int POTransSlNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? LinkNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? MtrlStd { get; set; }
        public string? CoreGrade { get; set; }
        public string? Spare { get; set; }
        public string? SpareGrade { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SpareQty { get; set; }
        public DateTime? DeleveryDate { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? Unit { get; set; }
        public string? Unit2 { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? ConvRatio { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal Rate { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? DiscP { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? PnFP { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? GSTP { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal POQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal BilledQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal BalanceQty { get; set; }
        [Column(TypeName = "numeric(14,4)")] public decimal FreightRate { get; set; }
    }
}
