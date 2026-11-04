namespace EMSAPI.Models
{
    // ============================================================
    // Production Planning — request DTOs (SQL 59)
    // The pages post camelCase JSON; binding is case-insensitive.
    // ============================================================

    /// <summary>Masters/WorkCalendar.html → POST api/WorkCalendar/SaveData</summary>
    public class WorkCalendarSaveRequest
    {
        public int MTransNo { get; set; }
        public string? CalMonth { get; set; }
        public string? ShiftName { get; set; }
        public string? ShiftStart { get; set; }
        public string? ShiftEnd { get; set; }
        public int BreakMin { get; set; }
        public string? WeeklyOff { get; set; }
        public List<WorkCalendarHolidayDto>? Holidays { get; set; }
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    public class WorkCalendarHolidayDto
    {
        public string? HoliDate { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>Production/ProductionPlan.html → POST api/ProductionPlan/SaveMasterData</summary>
    public class ProductionPlanSaveRequest
    {
        public int MTransNo { get; set; }
        public string? PlanNo { get; set; }
        public string? PlanDate { get; set; }
        public string? PlanMonth { get; set; }
        public int RevNo { get; set; }
        public string? PlanStatus { get; set; }
        public string? Remarks { get; set; }
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>Production/ProductionPlan.html → POST api/ProductionPlan/SaveDtlData.
    /// `Schedule` (the page's own day split) is accepted but not trusted — the
    /// API works the days out again with PlanScheduleEngine.</summary>
    public class ProductionPlanDtlSaveRequest
    {
        public int MTransNo { get; set; }
        public List<ProductionPlanJobDto>? Jobs { get; set; }
        public List<ProductionPlanDayDto>? Schedule { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    public class ProductionPlanJobDto
    {
        public int Seq { get; set; }
        public string? PlanType { get; set; }
        public int PartNo { get; set; }
        public int DieNo { get; set; }
        public int MachineNo { get; set; }
        public decimal DaysRequired { get; set; }
        public string? FixedStart { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? Remarks { get; set; }
        public List<ProductionPlanLineDto>? Lines { get; set; }
    }

    public class ProductionPlanLineDto
    {
        public int SlNo { get; set; }
        public string? Source { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public int OAId { get; set; }
        public string? Grade { get; set; }
        public decimal PlanQty { get; set; }
        public decimal Rate { get; set; }
        public int RouteNo { get; set; }
        public string? Reason { get; set; }
    }

    public class ProductionPlanDayDto
    {
        public int JobSeq { get; set; }
        public int LineSlNo { get; set; }
        public string? PlanDate { get; set; }
        public decimal PlanQty { get; set; }
    }

    /// <summary>Production/ProductionPlan.html → POST api/ProductionPlan/Reschedule</summary>
    public class ProductionPlanRescheduleRequest
    {
        public int MTransNo { get; set; }
        public int JobSeq { get; set; }
        public string? FixedStart { get; set; }
        public decimal DaysRequired { get; set; }
        public string? Reason { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>PurchaseReports/RMRequirementReport.html → POST api/RMRequirement/CreatePR (one row per bar).</summary>
    public class RMCreatePRLine
    {
        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string? Grade { get; set; }
        public decimal Dia { get; set; }
        public decimal QtyKg { get; set; }
        public string? OaRefs { get; set; }
        public string? DueDate { get; set; }
    }
}
