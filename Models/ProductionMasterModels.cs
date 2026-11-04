using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // Request bodies of the four production master pages (SQL 54).
    // The pages post camelCase JSON; binding is case-insensitive.
    // ============================================================

    /// <summary>MachineMaster.html → POST api/MachineMaster/SaveData</summary>
    public class MachineSaveRequest
    {
        public int MTransNo { get; set; }
        public string? MachineCode { get; set; }
        public string? MachineName { get; set; }
        public List<int>? Operations { get; set; }   // OtherMaster.MTransNo ('Operation')
        public string? Operation { get; set; }       // legacy comma-separated form
        public DateTime? DateofInst { get; set; }
        public decimal? Capacity { get; set; }
        public string? CapacityUnit { get; set; }    // planning fields (SQL 58); null = keep
        public int? ShiftsPerDay { get; set; }
        public decimal? EfficiencyPct { get; set; }
        public decimal? MinPieceWt { get; set; }
        public decimal? MaxPieceWt { get; set; }
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>DieMaster.html → POST api/DieMaster/SaveData</summary>
    public class DieSaveRequest
    {
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? Grade { get; set; }           // grade TEXT, resolved to GradeNo server-side
        public int GradeNo { get; set; }
        public int DieTypeNo { get; set; }           // OtherMaster.MTransNo ('Die Type')
        public string? DieType { get; set; }         // or the die type name
        public decimal MaxQty { get; set; }
        public decimal OpeningQty { get; set; }
        public string? DieStatus { get; set; }       // Ready / In Repair / Scrapped (SQL 58); null = keep
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>RoutingChart.html → POST api/RoutingChart/SaveMasterData</summary>
    public class RouteSaveRequest
    {
        public int MTransNo { get; set; }
        public int PartNo { get; set; }
        public string? Grade { get; set; }           // grade TEXT, resolved to GradeNo server-side
        public int GradeNo { get; set; }
        public string? RouteName { get; set; }
        public int BatchQty { get; set; }
        public int? PostForgeDays { get; set; }      // SQL 58; null = keep
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    // ============================================================
    // ConvBOM — Conversion BOM (SQL 54): one row per finished part
    // (+ optional FG grade) and raw-material line.
    // ============================================================
    [Table("ConvBOM")]
    public class ConvBOM
    {
        [Key] public int MTransNo { get; set; }

        public int FPartNo { get; set; }
        public int? FGradeNo { get; set; }
        public string? FGrade { get; set; }
        public int PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public string? Remarks { get; set; }

        public string? SystemIP { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string DeleteStatus { get; set; } = "N";
        public string? DeleteReason { get; set; }
        public string LockStatus { get; set; } = "N";
        public int LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>ConvBOM.html → POST api/ConvBOM/SaveData</summary>
    public class ConvBOMSaveRequest
    {
        public int MTransNo { get; set; }
        public int FPartNo { get; set; }
        public int FGradeNo { get; set; }
        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public decimal Qty { get; set; }             // cut weight, kg per piece
        // Bar details (SQL 58) — the page posts BarDia / CutLength / YieldPct; null = keep.
        public decimal? BarDia { get; set; }
        public decimal? CutLength { get; set; }
        public decimal? YieldPct { get; set; }
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }
}
