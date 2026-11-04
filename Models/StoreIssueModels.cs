using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    /*  Store Issue / Store Returns.

            MRNMst/MRNDtl (approved)  ──►  StoreIssueMst/Dtl  ──►  StoreReturnsMst/Dtl
                                     LinkNo = MRNDtl.TransSlNo     LinkNo = StoreIssueDtl.TransSlNo

        Tables and save procedures: SQL/44_StoresIssueTables.sql.
        Views (stock, balances):    SQL/46_StoreIssueReturns.sql.

        The MRN tables are read only here, through VW_StoreIssueMRNLine — the
        MRN pages own their entity types.  */

    // ============================================================
    // TABLES
    // ============================================================
    [Table("StoreIssueMst")]
    public class StoreIssueMst
    {
        [Key] public int MTransNo { get; set; }
        public string DocSeries { get; set; } = "";
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public string? TargetType { get; set; }
        public string IndentorName { get; set; } = "";
        public string Department { get; set; } = "";
        public string? DocNotes { get; set; }
        public string DocStatus { get; set; } = "N";
        public string? SystemIP { get; set; }
        public string LS { get; set; } = "Y";
        public string FS { get; set; } = "N";
        public string LockStatus { get; set; } = "N";
        public string DeleteStatus { get; set; } = "N";
        public byte LockCounter { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
    }

    [Table("StoreIssueDtl")]
    public class StoreIssueDtl
    {
        [Key] public int TransSlNo { get; set; }
        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int? LinkNo { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? ItemRemarks { get; set; }
        public string? DCOpration { get; set; }
        public string? HeatNo { get; set; }          // SQL 58 — an approved heat of the part
        public decimal MRNQty { get; set; }
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string DeleteStatus { get; set; } = "N";
        public string LS { get; set; } = "Y";
        public string FS { get; set; } = "N";
        public int SubscID { get; set; }
    }

    [Table("StoreReturnsMst")]
    public class StoreReturnsMst
    {
        [Key] public int MTransNo { get; set; }
        public string DocSeries { get; set; } = "";
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public string? DocNotes { get; set; }
        public string DocStatus { get; set; } = "N";
        public string? SystemIP { get; set; }
        public string LS { get; set; } = "Y";
        public string FS { get; set; } = "N";
        public string LockStatus { get; set; } = "N";
        public string DeleteStatus { get; set; } = "N";
        public byte LockCounter { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
    }

    [Table("StoreReturnsDtl")]
    public class StoreReturnsDtl
    {
        [Key] public int TransSlNo { get; set; }
        public decimal? SlNo { get; set; }
        public int MTransNo { get; set; }
        public int? LinkNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartDescription { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal IssueQty { get; set; }
        public decimal RTNQty { get; set; }
        public string? ReturnType { get; set; }      // SQL 58 — Unused / End Piece / Scrap
        public string? Unit { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string DeleteStatus { get; set; } = "N";
        public string LS { get; set; } = "Y";
        public string FS { get; set; } = "N";
        public int SubscID { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
    }

    // ============================================================
    // VIEWS (keyless, SQL 46)
    // ============================================================
    [Keyless]
    [Table("VW_StoreStock")]
    public class StoreStockRow
    {
        public string TxnType { get; set; } = "";
        public int RefTransNo { get; set; }
        public int RefTransSlNo { get; set; }
        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public int PartNo { get; set; }
        public int? GradeNo { get; set; }
        public string Grade { get; set; } = "";
        public string? Store { get; set; }
        public string? Location { get; set; }
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public int SubscID { get; set; }
    }

    [Keyless]
    [Table("VW_StoreIssueMRNLine")]
    public class StoreIssueMRNLine
    {
        public int MTransNo { get; set; }
        public string? MRNType { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public string? Requiredby { get; set; }
        public string? Department { get; set; }
        public int SubscID { get; set; }
        public int LinkNo { get; set; }
        public int? SlNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartName { get; set; }
        public int? GradeNo { get; set; }
        public string Grade { get; set; } = "";
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? Unit { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal Qty { get; set; }
        public decimal IssuedQty { get; set; }
        public decimal BalQty { get; set; }
    }

    [Keyless]
    [Table("VW_StoreIssueLineBal")]
    public class StoreIssueLineBal
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public string? TargetType { get; set; }
        public string? IndentorName { get; set; }
        public string? Department { get; set; }
        public string? LockStatus { get; set; }
        public int SubscID { get; set; }
        public int LinkNo { get; set; }
        public decimal? SlNo { get; set; }
        public int PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartName { get; set; }
        public int? GradeNo { get; set; }
        public string Grade { get; set; } = "";
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? Unit { get; set; }
        public decimal Qty { get; set; }
        public decimal ReturnedQty { get; set; }
        public decimal BalQty { get; set; }
    }

    // ============================================================
    // REQUEST BODIES (field names as the pages post them)
    // ============================================================
    public class StoreIssueHeaderDto
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public string? Indentor { get; set; }
        public string? Department { get; set; }
        public string? TargetType { get; set; }
        public int TargetTransNo { get; set; }
        public int TargetDocYear { get; set; }
        public int TargetDocNo { get; set; }
        public string? DocNotes { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    public class StoreIssueLineDto
    {
        public int MTransNo { get; set; }
        public int TransSlNo { get; set; }
        public int SlNo { get; set; }
        public int PartNo { get; set; }
        public decimal Qty { get; set; }
        public string? ItemRemarks { get; set; }
        public int LinkNo { get; set; }
        public string? DcOperation { get; set; }
        public string? HeatNo { get; set; }          // SQL 58; blank = none
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    public class StoreReturnsHeaderDto
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime? DocDate { get; set; }
        public int TargetTransNo { get; set; }
        public string? DocNotes { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    public class StoreReturnsLineDto
    {
        public int MTransNo { get; set; }
        public int TransSlNo { get; set; }
        public int SlNo { get; set; }
        public int PartNo { get; set; }
        public decimal Qty { get; set; }
        public string? ItemRemarks { get; set; }
        public int LinkNo { get; set; }
        public string? ReturnType { get; set; }      // SQL 58; null = keep (new line: Unused)
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }
}
