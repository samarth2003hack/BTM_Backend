using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // MachineMst — Machine Master
    // ============================================================
    [Table("MachineMst")]
    public class MachineMst
    {
        [Key] public int MTransNo { get; set; }

        public string? MachineCode { get; set; }
        public string? MachineName { get; set; }
        public DateTime? DateofInst { get; set; }
        public decimal? Capacity { get; set; }
        public string? Operation { get; set; }   // Comma-separated OtherMaster.MTransNo values
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }  // Y=Active N=Inactive
        public int? LockCounter { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_MachineMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? MachineCode { get; set; }
        public string? MachineName { get; set; }
        public DateTime? DateofInst { get; set; }
        public decimal? Capacity { get; set; }
        public string? Operation { get; set; }   // Comma-separated OtherMaster.MTransNo values
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? SystemIP { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // DieMst — Die Master
    // ============================================================
    [Table("DieMst")]
    public class DieMst
    {
        [Key] public int MTransNo { get; set; }

        public int? PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? DieType { get; set; }
        public decimal? MaxQty { get; set; }
        public decimal? OpeningQty { get; set; }
        public decimal? UsedQty { get; set; }       // Updated by production, not this form
        public decimal? ProcessQty { get; set; }    // OpeningQty + UsedQty — computed by SP
        public decimal? RemainingQty { get; set; }  // MaxQty - ProcessQty — computed by SP
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_DieMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public int? PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? DieType { get; set; }
        public decimal? MaxQty { get; set; }
        public decimal? OpeningQty { get; set; }
        // UsedQty, ProcessQty, RemainingQty NOT in DTO — computed by SP
        public string? Remarks { get; set; }
        public string? LockStatus { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? SystemIP { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // RoutingChartMst — Routing Chart Header
    // ============================================================
    [Table("RoutingChartMst")]
    public class RoutingChartMst
    {
        [Key] public int MTransNo { get; set; }

        public int? PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? RouteName { get; set; }
        public int? BatchQty { get; set; }
        public string? DieType { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_RoutingChartMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public int? PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string? RouteName { get; set; }
        public int? BatchQty { get; set; }
        public string? DieType { get; set; }
        public string? LockStatus { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? SystemIP { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // RoutingChartDtl — Routing Chart Steps
    // ============================================================
    [Table("RoutingChartDtl")]
    public class RoutingChartDtl
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }   // NOT NULL
        public int SlNo { get; set; }       // NOT NULL — step order integer
        public int Operation { get; set; }  // NOT NULL — OtherMaster.MTransNo
        public int MachineNo { get; set; }  // NOT NULL
        public decimal? TimeMin { get; set; }
        public decimal? OperCost { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_RoutingChartDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int? SlNo { get; set; }
        public int? Operation { get; set; }    // OtherMaster.MTransNo
        public int? MachineNo { get; set; }
        public decimal? TimeMin { get; set; }
        public decimal? OperCost { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // DTOs for RoutingChart page-specific payloads
    // ============================================================

    // SaveDtlData receives: { mTransNo, steps: [{slNo, operationNo, machineNo, timeMin}] }
    public class RoutingChartStepDto
    {
        public int SlNo { get; set; }
        public int OperationNo { get; set; }   // OtherMaster.MTransNo
        public int MachineNo { get; set; }
        public decimal TimeMin { get; set; }   // minutes per batch
        public decimal? SetupMin { get; set; } // die-setup minutes (SQL 58)
    }

    public class RoutingChartSaveDtlRequest
    {
        public int MTransNo { get; set; }
        public List<RoutingChartStepDto> Steps { get; set; } = new();
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }
}
