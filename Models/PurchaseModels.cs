using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // PurReqMst
    // ============================================================
    [Table("PurReqMst")]
    public class PurReqMst
    {
        [Key] public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }
        public string? ReqBy { get; set; }
        public string? ReqDept { get; set; }
        public string? Project { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        public string? DocStatus { get; set; }
        public string? RFQReq { get; set; }
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

    public class SP_PurReqMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }
        public string? ReqBy { get; set; }
        public string? ReqDept { get; set; }
        public string? Project { get; set; }
        public DateTime? DueDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        public string? RFQReq { get; set; }
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
    // PurReqDtl
    // ============================================================
    [Table("PurReqDtl")]
    public class PurReqDtl
    {
        [Key] public int TransSlNo { get; set; }
        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        // Optional OA link (SQL/36): OAMst.DocYear / DocNo / OADtl.SlNo.
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        // MaterialGrade of the part (SQL/40): GradeNo + its name.
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public DateTime? RequiredBy { get; set; }    // SQL 58; null = header due date
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_PurReqDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int? PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        // Optional OA link — all three or none; checked by FN_OALinkCheck.
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        // Mandatory: one of the part's MaterialGrade numbers. The procedure
        // re-reads the name from the master.
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        // Line need-by date (SQL 58, SP_PurReqDtlPlanFields); null = header due date.
        public DateTime? RequiredBy { get; set; }
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
    // RFQMst
    // ============================================================
    [Table("RFQMst")]
    public class RFQMst
    {
        [Key] public int MTransNo { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public int? DocYear { get; set; }
        public string? DocSeries { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }
        public int? PRTransNo { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
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

    public class SP_RFQMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public short? DocAmdNo { get; set; }
        public int? PRTransNo { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
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
    // RFQDtl
    // ============================================================
    [Table("RFQDtl")]
    public class RFQDtl
    {
        [Key] public int TransSlNo { get; set; }
        public decimal? SLNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_RFQDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SLNo { get; set; }
        public int? PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        // Carried from the requisition line; optional for older lines.
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
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
    // RFQSupplier
    // ============================================================
    [Table("RFQSupplier")]
    public class RFQSupplier
    {
        [Key] public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? ContactNo { get; set; }
        public string? VendorAddress { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_RFQSupplier
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SlNo { get; set; }
        public int? VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? ContactNo { get; set; }
        public string? VendorAddress { get; set; }
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
    // RFQTerms
    // ============================================================
    [Table("RFQTerms")]
    public class RFQTerms
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
        public int? SubscID { get; set; }
    }

    public class SP_RFQTerms
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

    // ============================================================
    // RFQPriceMst
    // ============================================================
    [Table("RFQPriceMst")]
    public class RFQPriceMst
    {
        [Key] public int MTransNo { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public int? DocYear { get; set; }
        public string? DocSeries { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }
        public string? DocAmdNotes { get; set; }
        public int? TargetTransNo { get; set; }
        public int? VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? ContactNo { get; set; }
        public string? VendorAddress { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }
        public int? PRYear { get; set; }
        public int? PRNo { get; set; }
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

    public class SP_RFQPriceMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdNotes { get; set; }
        public int? TargetTransNo { get; set; }
        public int? VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? ContactNo { get; set; }
        public string? VendorAddress { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public int? PRYear { get; set; }
        public int? PRNo { get; set; }
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
    // RFQPriceDtl
    // ============================================================
    [Table("RFQPriceDtl")]
    public class RFQPriceDtl
    {
        [Key] public int TransSlNo { get; set; }
        public decimal? SLNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public decimal Rate { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_RFQPriceDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SLNo { get; set; }
        public int? PartNo { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        // Carried from the RFQ line; optional for older lines.
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
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
    // RFQPriceTerms
    // ============================================================
    [Table("RFQPriceTerms")]
    public class RFQPriceTerms
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
        public int? SubscID { get; set; }
    }

    public class SP_RFQPriceTerms
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

    // ============================================================
    // RFQ APPROVAL
    // ============================================================

    /// <summary>
    /// VW_RFQApprovalList — the approved RFQs that have quotations against them,
    /// which is one row on the RFQ Approval and Price Comparison grids.
    ///
    /// DocStatus is the approval state the grid shows ('Y' once a supplier has
    /// won); LockStatus is the earlier step-3 approval that let the suppliers
    /// quote at all. The Approved* columns describe the winning quotation and
    /// are null until one is picked.
    /// </summary>
    public class RFQApprovalList
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocNotes { get; set; }
        public int? PRTransNo { get; set; }
        public int? PRYear { get; set; }
        public int? PRNo { get; set; }
        public int QuoteCount { get; set; }
        public string? DocStatus { get; set; }
        public string? LockStatus { get; set; }
        public int ApprovedQuoteNo { get; set; }
        public int? ApprovedVendorNo { get; set; }
        public string? ApprovedVendorName { get; set; }
        public DateTime? ApprovedOn { get; set; }
        public int? ApprovedBy { get; set; }
        public int? SubscID { get; set; }
    }

    /// <summary>
    /// Input to SP_RFQApproval — picks the winning quotation for one RFQ.
    /// MTransNo is the RFQMst row; ApprovedQuoteNo is the RFQPriceMst row that
    /// won. The procedure locks that quotation, closes the others, and closes
    /// the RFQ, all in one transaction.
    /// </summary>
    public class SP_RFQApproval
    {
        public int MTransNo { get; set; }
        public int ApprovedQuoteNo { get; set; }
        public string? SystemIP { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public int? SubscID { get; set; }
        public string Status { get; set; } = "Approve";
    }
}
