using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    /*  Goods receipt and everything downstream of it: store allocation, heat
        numbers, inward inspection and its closure.

        The inward flow is one row in GRNMst moving through three stages, with
        GRNType as the marker:

            'N'  received      GRN entry wrote BillQty / RecQty
            'Q'  inspected     inspection wrote Acc/Rej/Rew/AOD + InspNo
            'C'  NC closed     closure wrote NCCloseDate / NCRemarks

        Each stage has its own SP_* input type below, and each writes only its
        own columns — which is what lets two people work the same GRN at
        different stages without overwriting one another.  */

    // ============================================================
    // GRNMst
    // ============================================================
    [Table("GRNMst")]
    public class GRNMst
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
        public string? InwardNo { get; set; }
        public DateTime? InwardDate { get; set; }
        public string? LRNo { get; set; }
        public DateTime? LRDate { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }
        public string? DocNotes { get; set; }
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
        [Column(TypeName = "numeric(6,2)")]  public decimal? TDSP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TDSAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? TCSP { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? TCSAmt { get; set; }
        public string? OtherCharges { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? OtherAmt { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? RoundOff { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? NetValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? DebitValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? CreditValue { get; set; }
        public string? TxnCurrency { get; set; }
        [Column(TypeName = "numeric(6,2)")]  public decimal? CurrencyRate { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? CurrencyAmount { get; set; }
        public string? VehicleNo { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? BillValue { get; set; }

        public string? TargetType { get; set; }

        /// <summary>'N' received, 'Q' inspected, 'C' non-conformance closed.</summary>
        public string? GRNType { get; set; }

        public DateTime? QCDate { get; set; }
        public string? QCverifyBy { get; set; }
        public string? InspNo { get; set; }
        public DateTime? NCCloseDate { get; set; }
        public string? NCRemarks { get; set; }
        public int? NCCloseUserNo { get; set; }
        public string? NCCloseAttachment { get; set; }

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
    /// Input to SP_GRNMst: Insert, Update, Delete, Lock, Unlock or Recalc.
    ///
    /// On TargetType 'PO' the vendor block and the tax type are copied off the
    /// purchase order, and VendorNo is ignored — a receipt cannot be booked
    /// against a different supplier from the one the order was placed on.
    /// </summary>
    public class SP_GRNMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        /// <summary>POMst.MTransNo when TargetType is 'PO'.</summary>
        public int? TargetTransNo { get; set; }

        /// <summary>'PO' or 'Direct'.</summary>
        public string? TargetType { get; set; }

        public int? VendorNo { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? InwardNo { get; set; }
        public DateTime? InwardDate { get; set; }
        public string? LRNo { get; set; }
        public DateTime? LRDate { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }
        public string? VehicleNo { get; set; }
        public decimal? BillValue { get; set; }
        public string? DocNotes { get; set; }
        public decimal? TDSP { get; set; }
        public decimal? TCSP { get; set; }
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
    // GRNDtl
    // ============================================================
    [Table("GRNDtl")]
    public class GRNDtl
    {
        [Key] public int TransSlNo { get; set; }
        public int DocNo { get; set; }
        public int VendorNo { get; set; }
        public DateTime DocDate { get; set; }

        /// <summary>On a PO-backed GRN this is the PODtl.SlNo being received.</summary>
        [Column(TypeName = "numeric(12,2)")] public decimal? SlNo { get; set; }

        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public string? HSNCode { get; set; }

        [Column(TypeName = "numeric(12,4)")] public decimal BillQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal RecQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal AccQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal RejQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal RewQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal AODQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal Weight { get; set; }
        public string? Unit { get; set; }
        public string? RateType { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal Rate { get; set; }
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

        public string? BarCode { get; set; }
        public string? ScanStatus { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? POQty { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? LandingCost { get; set; }
        [Column(TypeName = "numeric(16,2)")] public decimal? LandingValue { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? RegdNo { get; set; }
        public string? RejReason { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? Unit2 { get; set; }
        [Column(TypeName = "numeric(14,4)")] public decimal? ConvQty { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? InspRemarks { get; set; }
        public int? PatternNo { get; set; }
        public string? CoreGrade { get; set; }
        public string? Spare { get; set; }
        public string? SpareGrade { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SpareQty { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? PLoosQty { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? CoreQty { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? CLooseQty { get; set; }

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
    /// Input to SP_GRNDtl — the receipt itself. Carries no inspection
    /// quantity: those belong to SP_GRNInspDtl.
    /// </summary>
    public class SP_GRNDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }

        /// <summary>PODtl.TransSlNo being received against; 0 on a direct GRN.
        /// When set, the line number, rate, discount, packing and grade are
        /// taken from the order, not from this request.</summary>
        public int POTransSlNo { get; set; }

        public int? PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? BillQty { get; set; }
        public decimal? RecQty { get; set; }
        public decimal? Weight { get; set; }
        public decimal? Rate { get; set; }
        public decimal? DiscP { get; set; }
        public decimal? PnFP { get; set; }
        public decimal? FreightAmt { get; set; }
        public string? Grade { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
        public string? BarCode { get; set; }
        public string? ChassisNo { get; set; }
        public string? EngineNo { get; set; }
        public string? RegdNo { get; set; }
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
    // GRNNCDtl
    // ============================================================
    [Table("GRNNCDtl")]
    public class GRNNCDtl
    {
        [Key] public int TransSlNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

        /// <summary>'Reject', 'Rework' or 'AOD'.</summary>
        public string? NCType { get; set; }

        public string? HeatNo { get; set; }
        public string? NCRemarks { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal NCQty { get; set; }
        public string? Unit { get; set; }

        /// <summary>GRNDtl.TransSlNo the non-conformance was raised on.</summary>
        public int? GRNTransSlNo { get; set; }

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
    /// Input to SP_GRNNCDtl. Part, grade and unit are copied from the receipt
    /// line, so they are not here.
    ///
    /// On DeleteAll, GRNTransSlNo carries the GRN's MTransNo rather than a
    /// line — that branch clears every NC on the whole document.
    /// </summary>
    public class SP_GRNNCDtl
    {
        public int TransSlNo { get; set; }
        public int GRNTransSlNo { get; set; }
        public string? NCType { get; set; }
        public string? HeatNo { get; set; }
        public string? NCRemarks { get; set; }
        public decimal? NCQty { get; set; }
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
    // GRNQRCode
    // ============================================================
    [Table("GRNQRCode")]
    public class GRNQRCode
    {
        [Key] public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int? GRNSlNo { get; set; }
        public int? POTransNo { get; set; }
        public int? LinkNo { get; set; }
        public int? PartNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? Qty { get; set; }
        public int? LQty { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? QRCode { get; set; }
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

    /// <summary>Input to SP_GRNQRCode: Generate, Delete or DeleteAll.</summary>
    public class SP_GRNQRCode
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }

        /// <summary>GRNDtl.TransSlNo the labels belong to.</summary>
        public int GRNSlNo { get; set; }

        /// <summary>How many labels to print.</summary>
        public int LQty { get; set; }

        /// <summary>Quantity each label stands for.</summary>
        public decimal? Qty { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Generate";
    }

    // ============================================================
    // GRNAttachment
    // ============================================================
    [Table("GRNAttachment")]
    public class GRNAttachment
    {
        [Key] public int TransSlNo { get; set; }
        public int? MTransNo { get; set; }
        [Column(TypeName = "numeric(6,2)")] public decimal? SlNo { get; set; }
        public string? AttNotes { get; set; }

        /// <summary>Stored path, relative to the API web root. The bytes are
        /// on disk; only the path is in the row.</summary>
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

    /// <summary>Input to SP_GRNAttachment: Insert, Update, Delete or DeleteAll.</summary>
    public class SP_GRNAttachment
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public string? AttNotes { get; set; }
        public string? AttFile { get; set; }
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
    // Store allocation
    // ============================================================

    /// <summary>
    /// Input to SP_GRNStoreAlloc: Allocate one line, AllocateAll unallocated
    /// lines on the GRN, or Clear one line.
    /// </summary>
    public class SP_GRNStoreAlloc
    {
        /// <summary>GRNDtl.TransSlNo. Ignored on AllocateAll.</summary>
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Allocate";
    }

    [Table("StoreLocation")]
    public class StoreLocation
    {
        [Key] public int MTransNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SLNo { get; set; }

        /// <summary>0 means a bin any part may be put in.</summary>
        public int PartNo { get; set; }

        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
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

    /// <summary>Input to SP_StoreLocation: Insert, Update or Delete.</summary>
    public class SP_StoreLocation
    {
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
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
    // Inward inspection
    // ============================================================

    /// <summary>
    /// Input to SP_GRNInspDtl — the verdict on one receipt line.
    /// Status 'Save' records it, 'Clear' zeroes it.
    ///
    /// AccQty + RejQty + RewQty + AODQty must equal the line's RecQty, or the
    /// procedure refuses: everything received has to be accounted for.
    /// </summary>
    public class SP_GRNInspDtl
    {
        /// <summary>GRNDtl.TransSlNo.</summary>
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? AccQty { get; set; }
        public decimal? RejQty { get; set; }
        public decimal? RewQty { get; set; }
        public decimal? AODQty { get; set; }
        public string? InspRemarks { get; set; }
        public string? RejReason { get; set; }
        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Save";
    }

    /// <summary>
    /// Input to SP_GRNInspPost — Post stamps the inspection number and moves
    /// the GRN to 'Q'; Reopen takes it back to 'N'.
    ///
    /// Post is where the whole document is checked: every line inspected, and
    /// every non-conforming quantity backed by NC lines that add up to it.
    /// </summary>
    public class SP_GRNInspPost
    {
        public int MTransNo { get; set; }
        public DateTime? QCDate { get; set; }
        public string? QCverifyBy { get; set; }
        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Post";
    }

    /// <summary>
    /// Input to SP_GRNNCClose — Close moves an inspected GRN to 'C', Reopen
    /// returns it to 'Q'.
    /// </summary>
    public class SP_GRNNCClose
    {
        public int MTransNo { get; set; }
        public DateTime? NCCloseDate { get; set; }
        public string? NCRemarks { get; set; }

        /// <summary>Stored path of the closure evidence, if any.</summary>
        public string? NCCloseAttachment { get; set; }

        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Close";
    }

    /// <summary>
    /// VW_GRNInwardStatus — one row per GRN with its stage and its totals.
    /// All three inward worklists read this, so they cannot disagree about
    /// which stage a receipt is at.
    /// </summary>
    public class GRNInwardStatus
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public string? TargetType { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? InwardNo { get; set; }
        public DateTime? InwardDate { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }
        public string? LRNo { get; set; }
        public DateTime? LRDate { get; set; }
        [Column(TypeName = "numeric(14,2)")] public decimal? BillValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? NetValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? DebitValue { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal? CreditValue { get; set; }
        public string? GRNType { get; set; }
        public string? InspNo { get; set; }
        public DateTime? QCDate { get; set; }
        public string? QCverifyBy { get; set; }
        public DateTime? NCCloseDate { get; set; }
        public int? NCCloseUserNo { get; set; }
        public string? NCRemarks { get; set; }
        public string? NCCloseAttachment { get; set; }
        public string? LockStatus { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }

        /// <summary>'Received', 'Inspected' or 'NC Closed'.</summary>
        public string? StageName { get; set; }

        public int LineCount { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalBillQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalRecQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalAccQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalRejQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalRewQty { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalAODQty { get; set; }
        public int UnallocatedLines { get; set; }
        public int NCLines { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal TotalNCQty { get; set; }
    }

    // ============================================================
    // Heat number traceability
    // ============================================================
    [Table("HeatNoMst")]
    public class HeatNoMst
    {
        [Key] public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        /// <summary>GRNMst.MTransNo the heats were recorded against.</summary>
        public int? TargetTransNo { get; set; }

        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        [Column(TypeName = "numeric(12,6)")] public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }
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

    /// <summary>Input to SP_HeatNoMst: Insert, Update, Delete, Lock or Unlock.</summary>
    public class SP_HeatNoMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        /// <summary>GRNMst.MTransNo. The part must actually have been received
        /// on it.</summary>
        public int TargetTransNo { get; set; }

        public int PartNo { get; set; }
        public string? Grade { get; set; }
        public decimal? Qty { get; set; }
        public string? DocNotes { get; set; }
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

    [Table("HeatNoDtl")]
    public class HeatNoDtl
    {
        [Key] public int TransSlNo { get; set; }
        [Column(TypeName = "numeric(12,2)")] public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? ItemRemarks { get; set; }
        [Column(TypeName = "numeric(12,4)")] public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public string? HeatNo { get; set; }
        public string? TCNo { get; set; }
        public DateTime? TCDate { get; set; }
        public string? TCFile { get; set; }
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
    /// Input to SP_HeatNoDtl: Insert, Update, Delete or DeleteAll.
    ///
    /// RefFrom/RefTo expand into HeatRefNo — the piece serial numbers this
    /// heat covers. They are rewritten whole on every save, so correcting a
    /// range replaces it rather than adding to it. Pass 0/0 for none.
    /// </summary>
    public class SP_HeatNoDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? Grade { get; set; }
        public string? HeatNo { get; set; }
        public string? TCNo { get; set; }
        public DateTime? TCDate { get; set; }
        public string? TCFile { get; set; }
        public decimal? Qty { get; set; }
        public string? ItemRemarks { get; set; }
        public int RefFrom { get; set; }
        public int RefTo { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    [Table("HeatRefNo")]
    public class HeatRefNo
    {
        [Key] public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int HTransSlNo { get; set; }
        public int RefNo { get; set; }
        public int PartNo { get; set; }
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
    // GRN screen — one atomic save
    // ============================================================

    /// <summary>
    /// Body of api/GRN/Mst/SaveFull: the header, every line received, and the
    /// saved lines the user dropped — written in ONE database transaction, so
    /// a refused line (for example BillQty above the PO balance) leaves no
    /// half-saved GRN behind.
    ///
    /// Header.Status is 'Insert' or 'Update'. Each line's Status is derived
    /// from its TransSlNo (0 = Insert). CreatedOn / ModifiedOn are stamped by
    /// the server; any client value is ignored. Header.DocNo is a preview only
    /// — SP_GRNMst allocates the real number on insert.
    /// </summary>
    public class GRNSaveFullRequest
    {
        public SP_GRNMst Header { get; set; } = new();
        public List<SP_GRNDtl> Lines { get; set; } = new();

        /// <summary>GRNDtl.TransSlNo values to delete (Update only).</summary>
        public List<int> DeletedLines { get; set; } = new();

        /// <summary>Lock the GRN after saving (LockStatus 'Y' — receipt
        /// confirmed, visible to the store / inspection worklists).</summary>
        public bool Confirm { get; set; } = true;
    }
}
