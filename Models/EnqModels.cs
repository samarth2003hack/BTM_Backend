using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // LeadGeneration — Lead Header (no amendment)
    // ============================================================
    [Table("LeadGeneration")]
    public class LeadGeneration
    {
        [Key] public int MTransNo { get; set; }

        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public int? DocYear { get; set; }
        public string? DocSeries { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? VendorAddress { get; set; }
        public string? City { get; set; }
        public string? ContactPerson { get; set; }
        public string? PhoneNo { get; set; }
        public string? Product { get; set; }
        public int? Qty { get; set; }
        public DateTime? Followupdate { get; set; }
        public string? DocNotes { get; set; }
        public string? SalesExecutive { get; set; }
        public string? DocStatus { get; set; }   // read-only from DB
        public string? FileAttach { get; set; }
        public string? Emailid { get; set; }

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

    public class SP_LeadGeneration
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? VendorAddress { get; set; }
        public string? City { get; set; }
        public string? ContactPerson { get; set; }
        public string? PhoneNo { get; set; }
        public string? Product { get; set; }
        public int? Qty { get; set; }
        public DateTime? Followupdate { get; set; }
        public string? DocNotes { get; set; }
        public string? SalesExecutive { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert
        public string? FileAttach { get; set; }
        public string? Emailid { get; set; }

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
    // EnqMst — Customer Enquiry Header (with amendment)
    // ============================================================
    [Table("EnqMst")]
    public class EnqMst
    {
        [Key] public int MTransNo { get; set; }

        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public int? DocYear { get; set; }
        public string? DocSeries { get; set; }
        public short? DocAmdNo { get; set; }
        public string? DocAmdStatus { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? VendorAddress { get; set; }
        public string? StateName { get; set; }
        public string? Zone { get; set; }
        public string? CPName { get; set; }
        public string? CPDesignation { get; set; }
        public string? CPPhone { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? SalesExec { get; set; }
        public string? EnqSource { get; set; }
        public string? EnqPriority { get; set; }
        public string? EnqCategory { get; set; }
        public string? RefProject { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        public string? DocStatus { get; set; }   // read-only from DB
        public string? FileAttach { get; set; }
        public string? CPEmail { get; set; }
        public string? LeadStatus { get; set; }
        public string? EnqRemarks { get; set; }
        public string? CPAphone { get; set; }
        public string? CPCC { get; set; }
        public string? CPACC { get; set; }
        public DateTime? Nextfollowupdate { get; set; }
        public int? DrvNo { get; set; }

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

    public class SP_EnqMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public short? DocAmdNo { get; set; }

        public int? VendorNo { get; set; }
        public string? VendorCode { get; set; }
        public string? VendorName { get; set; }
        public string? VendorAddress { get; set; }
        public string? StateName { get; set; }
        public string? Zone { get; set; }
        public string? CPName { get; set; }
        public string? CPDesignation { get; set; }
        public string? CPPhone { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string? SalesExec { get; set; }
        public string? EnqSource { get; set; }
        public string? EnqPriority { get; set; }
        public string? EnqCategory { get; set; }
        public string? RefProject { get; set; }
        public string? DocNotes { get; set; }
        public string? DocAmdNotes { get; set; }
        // DocStatus removed — SP hardcodes 'N' on Insert
        public string? FileAttach { get; set; }
        public string? CPEmail { get; set; }
        public string? LeadStatus { get; set; }
        public string? EnqRemarks { get; set; }
        public string? CPAphone { get; set; }
        public string? CPCC { get; set; }
        public string? CPACC { get; set; }
        public DateTime? Nextfollowupdate { get; set; }
        public int? DrvNo { get; set; }

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
    // EnqDtl — Customer Enquiry Item Lines
    // ============================================================
    [Table("EnqDtl")]
    public class EnqDtl
    {
        [Key] public int TransSlNo { get; set; }

        public decimal? SLNo { get; set; }
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? Description { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal Qty { get; set; }        // NOT NULL
        public string? Unit { get; set; }
        public string? CustPartNo { get; set; }
        public string? PartCode { get; set; }
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

    public class SP_EnqDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SLNo { get; set; }
        public int? PartNo { get; set; }
        public string? Description { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public string? Unit { get; set; }
        public string? CustPartNo { get; set; }
        public string? PartCode { get; set; }
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
    // EnqFollowUp — Enquiry Follow-up History
    // ============================================================
    [Table("EnqFollowUp")]
    public class EnqFollowUp
    {
        [Key] public int TransSlNo { get; set; }

        public decimal? SLNo { get; set; }
        public int MTransNo { get; set; }
        public string? FollowUpType { get; set; }
        public DateTime? FollowupDate { get; set; }
        public string? FollowupNotes { get; set; }
        public DateTime NextVisitDate { get; set; }  // NOT NULL
        public int? EnqNo { get; set; }
        public int? QtnNo { get; set; }
        public int? OANo { get; set; }
        public int? PINo { get; set; }
        public int? InvNo { get; set; }

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

    public class SP_EnqFollowUp
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public decimal? SLNo { get; set; }
        public string? FollowUpType { get; set; }
        public DateTime? FollowupDate { get; set; }
        public string? FollowupNotes { get; set; }
        public DateTime? NextVisitDate { get; set; }
        public int? EnqNo { get; set; }
        public int? QtnNo { get; set; }
        public int? OANo { get; set; }
        public int? PINo { get; set; }
        public int? InvNo { get; set; }

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
    // ENQFeasibilityMst — Feasibility Checklist Master
    // ============================================================
    [Table("ENQFeasibilityMst")]
    public class ENQFeasibilityMst
    {
        [Key] public int MTransNo { get; set; }

        public decimal? SlNo { get; set; }
        public string? FeasibilityPoint { get; set; }
        public string? Remarks { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }
        public string? DocStatus { get; set; }
        public string? DocStatusNote { get; set; }

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

    public class SP_ENQFeasibilityMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public decimal? SlNo { get; set; }
        public string? FeasibilityPoint { get; set; }
        public string? Remarks { get; set; }
        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }
        public string? DocStatus { get; set; }      // data field — passed as param
        public string? DocStatusNote { get; set; }

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
