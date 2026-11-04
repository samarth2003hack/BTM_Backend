using System.Text.Json;

namespace EMSAPI.Models
{
    // ====================================================================
    // Maintenance module (SQL/55_Maintenance.sql) — request bodies, SP
    // inputs and the read models the API answers with. Every read model is
    // serialised camelCase; dates go out as "YYYY-MM-DD", date-times as
    // "YYYY-MM-DDTHH:MM" (what the pages' datetime-local inputs use).
    // ====================================================================

    // ── Request bodies ──────────────────────────────────────────────────

    /// <summary>POST api/PMChecklist/SaveMasterData.</summary>
    public class PMChecklistSaveDto
    {
        public int MTransNo { get; set; }
        public string? ChecklistName { get; set; }
        public string? DocNo { get; set; }
        public string? RevNo { get; set; }
        public string? RevDate { get; set; }
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public List<int>? Machines { get; set; }
        public List<PMChecklistPointDto>? Points { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    public class PMChecklistPointDto
    {
        public int TransSlNo { get; set; }
        public string? Frequency { get; set; }
        public string? Category { get; set; }
        public string? CheckPoint { get; set; }
    }

    /// <summary>
    /// POST api/PMCheckSheet/SaveMasterData. dayOperators / pointRemarks are
    /// objects keyed by day / point no; their values arrive as strings or
    /// numbers, so they are read leniently.
    /// </summary>
    public class PMSheetSaveDto
    {
        public int MTransNo { get; set; }
        public int MachineNo { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public List<int>? IdleDays { get; set; }
        public Dictionary<string, JsonElement>? DayOperators { get; set; }
        public List<PMSheetEntryDto>? Entries { get; set; }
        public Dictionary<string, JsonElement>? PointRemarks { get; set; }
        public string? Remarks { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    public class PMSheetEntryDto
    {
        public int PointNo { get; set; }
        public int Slot { get; set; }
        public string? Status { get; set; }
        public string? DoneOn { get; set; }
        public int DoneBy { get; set; }
    }

    /// <summary>POST api/PMCheckSheet/Approve.</summary>
    public class PMSheetApproveDto
    {
        public int MTransNo { get; set; }
        public string? Level { get; set; }
        public int ApprovedBy { get; set; }
        public int UserNo { get; set; }
    }

    /// <summary>POST api/Breakdown/SaveData.</summary>
    public class BreakdownSaveDto
    {
        public int MTransNo { get; set; }
        public int MachineNo { get; set; }
        public string? BreakdownOn { get; set; }
        public string? BreakdownType { get; set; }
        public string? ReportedBy { get; set; }
        public string? Problem { get; set; }
        public int AttendedBy { get; set; }
        public string? RepairStart { get; set; }
        public string? RepairEnd { get; set; }
        public string? RootCause { get; set; }
        public string? ActionTaken { get; set; }
        public string? SparesUsed { get; set; }
        public string? Remarks { get; set; }
        public int PMSheetNo { get; set; }
        public string? BreakdownStatus { get; set; }
        public int CreatedBy { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    // ── SP inputs ───────────────────────────────────────────────────────

    public class SP_PMChecklist
    {
        public int MTransNo { get; set; }
        public string? ChecklistName { get; set; }
        public string? DocNo { get; set; }
        public string? RevNo { get; set; }
        public DateTime? RevDate { get; set; }
        public string? Remarks { get; set; }
        public string LockStatus { get; set; } = "Y";
        public string MachinesJson { get; set; } = "[]";
        public string PointsJson { get; set; } = "[]";
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class SP_PMSheet
    {
        public int MTransNo { get; set; }
        public int MachineNo { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string? Remarks { get; set; }
        public string PointsJson { get; set; } = "[]";
        public string EntriesJson { get; set; } = "[]";
        public string DaysJson { get; set; } = "[]";
        public string RemarksJson { get; set; } = "[]";
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class SP_Breakdown
    {
        public int MTransNo { get; set; }
        public int MachineNo { get; set; }
        public DateTime BreakdownOn { get; set; }
        public string? BreakdownType { get; set; }
        public string? ReportedBy { get; set; }
        public string? Problem { get; set; }
        public int? AttendedBy { get; set; }
        public DateTime? RepairStart { get; set; }
        public DateTime? RepairEnd { get; set; }
        public string? RootCause { get; set; }
        public string? ActionTaken { get; set; }
        public string? SparesUsed { get; set; }
        public string? Remarks { get; set; }
        public int? PMSheetNo { get; set; }
        public string? BreakdownStatus { get; set; }
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // ── Stored rows (loaded by MaintenanceData) ─────────────────────────

    public class MntMachine
    {
        public int No { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Lock { get; set; } = "Y";
        public bool Deleted { get; set; }
        public DateTime? CreatedOn { get; set; }
        public DateTime? InstalledOn { get; set; }
        public string Operation { get; set; } = "";
        public string Remarks { get; set; } = "";
    }

    public class MntChecklist
    {
        public int No { get; set; }
        public string Name { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string RevNo { get; set; } = "";
        public DateTime? RevDate { get; set; }
        public string Remarks { get; set; } = "";
        public string Lock { get; set; } = "Y";
        public DateTime? CreatedOn { get; set; }
        public List<int> Machines { get; } = new();
        public List<MntPoint> Points { get; } = new();
    }

    public class MntPoint
    {
        public int No { get; set; }
        public int ChecklistNo { get; set; }
        public string Frequency { get; set; } = "";
        public string Category { get; set; } = "";
        public int SlNo { get; set; }
        public string CheckPoint { get; set; } = "";
    }

    public class MntSheet
    {
        public int No { get; set; }
        public string SheetNo { get; set; } = "";
        public int MachineNo { get; set; }
        public int ChecklistNo { get; set; }
        public string DocNo { get; set; } = "";
        public string RevNo { get; set; } = "";
        public DateTime? RevDate { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string Remarks { get; set; } = "";
        public int? DeptSupBy { get; set; }
        public DateTime? DeptSupOn { get; set; }
        public int? MntSupBy { get; set; }
        public DateTime? MntSupOn { get; set; }
        public DateTime? CreatedOn { get; set; }
        public List<MntSheetPoint> Points { get; } = new();
        public List<MntEntry> Entries { get; } = new();
        public List<int> IdleDays { get; } = new();
        public SortedDictionary<int, int> DayOperators { get; } = new();
    }

    public class MntSheetPoint
    {
        public int PointNo { get; set; }
        public int SlNo { get; set; }
        public string CheckPoint { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string Category { get; set; } = "";
        public string Remarks { get; set; } = "";
    }

    public class MntEntry
    {
        public int PointNo { get; set; }
        public int Slot { get; set; }
        public string Status { get; set; } = "";
        public DateTime? DoneOn { get; set; }
        public int? DoneBy { get; set; }
    }

    public class MntBreakdown
    {
        public int No { get; set; }
        public string BreakdownNo { get; set; } = "";
        public int MachineNo { get; set; }
        public DateTime BreakdownOn { get; set; }
        public string BreakdownType { get; set; } = "";
        public string ReportedBy { get; set; } = "";
        public string Problem { get; set; } = "";
        public int? AttendedBy { get; set; }
        public DateTime? RepairStart { get; set; }
        public DateTime? RepairEnd { get; set; }
        public string RootCause { get; set; } = "";
        public string ActionTaken { get; set; } = "";
        public string SparesUsed { get; set; } = "";
        public string Remarks { get; set; } = "";
        public int? PMSheetNo { get; set; }
        public string Status { get; set; } = "Open";
        public DateTime? CreatedOn { get; set; }
    }

    /// <summary>An OtherMaster row: maintenance staff, operator or operation.</summary>
    public class MntPerson
    {
        public int No { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Lock { get; set; } = "Y";
        public bool Deleted { get; set; }
    }

    // ── Read models ─────────────────────────────────────────────────────

    public class PMChecklistView
    {
        public int MTransNo { get; set; }
        public string ChecklistName { get; set; } = "";
        public string DocNo { get; set; } = "";
        public string RevNo { get; set; } = "";
        public string RevDate { get; set; } = "";
        public List<int> Machines { get; set; } = new();
        public List<string> MachineCodes { get; set; } = new();
        public string LockStatus { get; set; } = "Y";
        public string Remarks { get; set; } = "";
        public string CreatedOn { get; set; } = "";
        public int PointCount { get; set; }
        public Dictionary<string, int> FrequencyCounts { get; set; } = new();
        public int SheetUseCount { get; set; }
        public List<PMChecklistPointView>? Points { get; set; }
    }

    public class PMChecklistPointView
    {
        public int TransSlNo { get; set; }
        public int ChecklistNo { get; set; }
        public string Frequency { get; set; } = "";
        public string Category { get; set; } = "";
        public int SlNo { get; set; }
        public string CheckPoint { get; set; } = "";
    }

    /// <summary>A point due on a sheet, with last done / next due for the periodic ones.</summary>
    public class PMDuePoint
    {
        public int PointNo { get; set; }
        public int SlNo { get; set; }
        public string CheckPoint { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string Category { get; set; } = "";
        public string LastDone { get; set; } = "";
        public string NextDue { get; set; } = "";
    }

    public class PMSheetEntryView
    {
        public int PointNo { get; set; }
        public int Slot { get; set; }
        public string Status { get; set; } = "";
        public string DoneOn { get; set; } = "";
        public int DoneBy { get; set; }
    }

    public class PMSheetSummary
    {
        public int DueCount { get; set; }
        public int DoneCount { get; set; }
        public int MissedCount { get; set; }
        public int NotOkCount { get; set; }
        public double? Compliance { get; set; }
    }

    public class PMSheetView : PMSheetSummary
    {
        public int MTransNo { get; set; }
        public string SheetNo { get; set; } = "";
        public int MachineNo { get; set; }
        public int ChecklistNo { get; set; }
        public string DocNo { get; set; } = "";
        public string RevNo { get; set; } = "";
        public string RevDate { get; set; } = "";
        public int Year { get; set; }
        public int Month { get; set; }
        public string Remarks { get; set; } = "";
        public int DeptSupBy { get; set; }
        public string DeptSupOn { get; set; } = "";
        public int MntSupBy { get; set; }
        public string MntSupOn { get; set; } = "";
        public string CreatedOn { get; set; } = "";
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string ChecklistName { get; set; } = "";
        public string PeriodLabel { get; set; } = "";
        public string PeriodKey { get; set; } = "";
        public string SheetStatus { get; set; } = "Draft";
        public bool MonthOver { get; set; }
        public string DeptSupName { get; set; } = "";
        public string MntSupName { get; set; } = "";
        public int BreakdownCount { get; set; }
        // Children — only on GetMasterData (null on the list).
        public List<PMDuePoint>? Points { get; set; }
        public List<int>? IdleDays { get; set; }
        public Dictionary<string, int>? DayOperators { get; set; }
        public List<PMSheetEntryView>? Entries { get; set; }
        public Dictionary<string, string>? PointRemarks { get; set; }
    }

    public class BreakdownView
    {
        public int MTransNo { get; set; }
        public string BreakdownNo { get; set; } = "";
        public int MachineNo { get; set; }
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string BreakdownOn { get; set; } = "";
        public string BreakdownDate { get; set; } = "";
        public string BreakdownType { get; set; } = "";
        public string ReportedBy { get; set; } = "";
        public string Problem { get; set; } = "";
        public int AttendedBy { get; set; }
        public string AttendedByName { get; set; } = "";
        public string RepairStart { get; set; } = "";
        public string RepairEnd { get; set; } = "";
        public string RootCause { get; set; } = "";
        public string ActionTaken { get; set; } = "";
        public string SparesUsed { get; set; } = "";
        public string Remarks { get; set; } = "";
        public int PMSheetNo { get; set; }
        public string PmSheetRef { get; set; } = "";
        public string BreakdownStatus { get; set; } = "";
        public string CreatedOn { get; set; } = "";
        public double DowntimeHrs { get; set; }
        public double RepairHrs { get; set; }
        public bool IsRunning { get; set; }
    }

    public class ComplianceRow : PMSheetSummary
    {
        public int MTransNo { get; set; }
        public string SheetNo { get; set; } = "";
        public int MachineNo { get; set; }
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string PeriodKey { get; set; } = "";
        public string PeriodLabel { get; set; } = "";
        public string ChecklistName { get; set; } = "";
        public string SheetStatus { get; set; } = "";
    }

    public class DueRow
    {
        public int MachineNo { get; set; }
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string ChecklistName { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string Category { get; set; } = "";
        public string CheckPoint { get; set; } = "";
        public string LastDone { get; set; } = "";
        public string NextDue { get; set; } = "";
        public int DaysOverdue { get; set; }
        public string DueStatus { get; set; } = "";
    }

    public class MaintenanceHistoryRow
    {
        public string EventDate { get; set; } = "";
        public string MachineCode { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string EventType { get; set; } = "";
        public string RefNo { get; set; } = "";
        public string Details { get; set; } = "";
        public string Value { get; set; } = "";
        public string EventStatus { get; set; } = "";
    }

    public class MaintenanceKpis
    {
        public double? AvgCompliance { get; set; }
        public int OverduePoints { get; set; }
        public int OpenBreakdowns { get; set; }
        public int BreakdownCount { get; set; }
        public double DowntimeHrs { get; set; }
        public double? MttrHrs { get; set; }
        public double? MtbfHrs { get; set; }
    }

    public class HistoryCardEvent
    {
        public string Kind { get; set; } = "";
        public string EventDate { get; set; } = "";
        public string? EventTime { get; set; }
        public string EventType { get; set; } = "";
        public string RefNo { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Details { get; set; } = "";
        public string DoneBy { get; set; } = "";
        public string SparesUsed { get; set; } = "";
        public double? DowntimeHrs { get; set; }
        public double? Compliance { get; set; }
        public string EventStatus { get; set; } = "";
    }
}
