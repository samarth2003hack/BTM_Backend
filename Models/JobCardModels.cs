namespace EMSAPI.Models
{
    // ============================================================
    // Job Card chain request bodies (SQL/53_JobCard.sql).
    // The pages post camelCase JSON; property names follow them.
    // ============================================================

    /// <summary>POST api/JobCard/SaveMasterData — Status 'Insert' | 'Update'.</summary>
    public class JobCardSaveRequest
    {
        public int MTransNo { get; set; }
        public DateTime? JobDate { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public int OAID { get; set; }
        public int PartNo { get; set; }
        public string? Grade { get; set; }
        public int RouteNo { get; set; }
        public decimal Qty { get; set; }
        public int RMPartNo { get; set; }
        public string? RMGrade { get; set; }
        public decimal RMQty { get; set; }
        public string? Operator { get; set; }          // may be blank when PlanId > 0
        public string? Shift { get; set; }             // may be blank when PlanId > 0
        public string? Remarks { get; set; }
        // Production Plan link (SQL 58). Set by the plan release; a job card
        // with PlanId > 0 keeps its OA line, part, grade, route, qty, RM,
        // machine, die and planned dates (change them through Reschedule).
        public int PlanId { get; set; }
        public int PlanJobSeq { get; set; }
        public int PlanLineSlNo { get; set; }
        public DateTime? PlannedStart { get; set; }
        public DateTime? PlannedEnd { get; set; }
        public int MachineNo { get; set; }
        public int DieNo { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    /// <summary>One production entry inside JobCardEntrySaveRequest.</summary>
    public class JobCardEntryInput
    {
        public int StepSlNo { get; set; }
        public int MachineNo { get; set; }
        public int DieNo { get; set; }
        public DateTime? EntryDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public decimal SettingMin { get; set; }
        public decimal IdleMin { get; set; }
        public decimal Qty { get; set; }
        public string? Remarks { get; set; }
        // SQL 58: who ran it, why it stood idle, what came out rejected.
        public string? Operator { get; set; }
        public string? Shift { get; set; }
        public string? IdleReason { get; set; }
        public decimal RejectedQty { get; set; }
        public int UserNo { get; set; }
    }

    /// <summary>POST api/JobCardStatus/SaveDtlData — replaces the whole entry set.</summary>
    public class JobCardEntrySaveRequest
    {
        public int MTransNo { get; set; }
        public List<JobCardEntryInput>? Entries { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>POST api/JobCardInspection/SaveData — Status 'Insert' | 'Update'.</summary>
    public class JobCardInspectionSaveRequest
    {
        public int MTransNo { get; set; }
        public DateTime? InspDate { get; set; }
        /// <summary>JobCardMst.MTransNo (the page calls it jobCardNo).</summary>
        public int JobCardNo { get; set; }
        public string? InspectedBy { get; set; }
        public decimal InspectedQty { get; set; }
        public decimal OKQty { get; set; }
        public decimal ReworkQty { get; set; }
        public decimal RejectedQty { get; set; }
        public string? RejectionReason { get; set; }   // Other Master 'Rejection Reason' (SQL 58)
        public string? Remarks { get; set; }
        public int CreatedBy { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    /// <summary>Input to SP_JobCardMst once the controller has resolved grades and the OA line.</summary>
    public class SP_JobCardMst
    {
        public string Status { get; set; } = "Insert";
        public int MTransNo { get; set; }
        public DateTime JobDate { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public int OAID { get; set; }
        public int OALinkNo { get; set; }
        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string Grade { get; set; } = "";
        public int RouteNo { get; set; }
        public decimal Qty { get; set; }
        public int RMPartNo { get; set; }
        public int RMGradeNo { get; set; }
        public string RMGrade { get; set; } = "";
        public decimal RMQty { get; set; }
        public string? RMUnit { get; set; }
        public string Operator { get; set; } = "";
        public string Shift { get; set; } = "";
        public string? Remarks { get; set; }
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>Input to SP_JobCardInspection.</summary>
    public class SP_JobCardInspection
    {
        public string Status { get; set; } = "Insert";
        public int MTransNo { get; set; }
        public DateTime InspDate { get; set; }
        public int JobCardTransNo { get; set; }
        public string InspectedBy { get; set; } = "";
        public decimal InspectedQty { get; set; }
        public decimal OKQty { get; set; }
        public decimal ReworkQty { get; set; }
        public decimal RejectedQty { get; set; }
        public string? Remarks { get; set; }
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
    }
}
