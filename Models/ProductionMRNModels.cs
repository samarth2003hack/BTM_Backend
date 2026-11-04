using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // MRNMst — Material Requisition header (SQL 44)
    //   MRNType 'PROD'  production MRN  (Modules/Production/MRN.html)
    //   MRNType 'GEN'   general MRN     (Modules/Production/MRNG.html)
    // Column types follow the table exactly: SMALLINT -> short and
    // TINYINT -> byte, or EF's typed reader throws on read.
    // ============================================================
    [Table("MRNMst")]
    public class MRNMst
    {
        [Key] public int MTransNo { get; set; }

        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int DocYear { get; set; }
        public string DocSeries { get; set; } = "";
        public string? MRNType { get; set; }

        public int? TargetDocNo { get; set; }
        public int? TargetTransNo { get; set; }
        public int? TargetAmdNo { get; set; }
        public int? TargetDocYear { get; set; }

        public string? Requiredby { get; set; }
        public string? Department { get; set; }
        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }
        public string? DocNotes { get; set; }
        public string? DocStatus { get; set; }
        public string? LS { get; set; }
        public string? FS { get; set; }
        public string? SystemIP { get; set; }

        public string LockStatus { get; set; } = "N";   // N draft, S submitted, Y approved
        public string DeleteStatus { get; set; } = "N";
        public byte LockCounter { get; set; }
        public short CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public short? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public short SubscID { get; set; }
        public int? TempNo { get; set; }
    }

    // ============================================================
    // MRNDtl — Material Requisition line (SQL 44)
    //   PartNo / Grade / Qty   the material requisitioned
    //   Conv*                  PROD only: the part being produced with it
    //   BalQty                 what Store Issue may still issue (= Qty on save)
    // ============================================================
    [Table("MRNDtl")]
    public class MRNDtl
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public int? SlNo { get; set; }
        public int? LinkNo { get; set; }
        public int PartNo { get; set; }
        public string? Description { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal? Qty { get; set; }
        public decimal BalQty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public decimal? Amount { get; set; }
        public string? Store { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string DeleteStatus { get; set; } = "N";
        public string? LS { get; set; }
        public string? FS { get; set; }
        public short SubscID { get; set; }
        public int? TempNo { get; set; }

        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }

        public int? ConvPartNo { get; set; }
        public string? ConvDescription { get; set; }
        public string? ConvGrade { get; set; }
        public int? ConvGradeNo { get; set; }
        public decimal? ConvQty { get; set; }
    }

    // ============================================================
    // Request bodies
    // ============================================================

    /// <summary>Body of POST api/MRN|MRNG/Mst/SaveData.</summary>
    public class MRNMstSaveRequest
    {
        public int MTransNo { get; set; }           // 0 = new
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public DateTime? DocDate { get; set; }
        public string? RequiredBy { get; set; }
        public string? Department { get; set; }
        public string? DocNotes { get; set; }
        public string? LockStatus { get; set; }     // N draft (executive) | S submit | Y keep approved
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>Body of POST api/MRN|MRNG/Dtl/SaveData.</summary>
    public class MRNDtlSaveRequest
    {
        public int MTransNo { get; set; }
        public int TransSlNo { get; set; }          // 0 = new
        public int SlNo { get; set; }
        public int OAYear { get; set; }             // OA Year / No / ID: all three or none
        public int OANo { get; set; }
        public int OAID { get; set; }
        public int PartNo { get; set; }
        public string? Grade { get; set; }          // "-" or blank = ungraded
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public string? ItemRemarks { get; set; }
        public int ConvPartNo { get; set; }         // PROD only
        public string? ConvGrade { get; set; }      // PROD only
        public decimal ConvQty { get; set; }        // PROD only
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>Parameters of SP_MRNMst (SQL 44).</summary>
    public class SP_MRNMst
    {
        public int MTransNo { get; set; }
        public string DocSeries { get; set; } = "";
        public int DocYear { get; set; }
        public DateTime DocDate { get; set; }
        public string MRNType { get; set; } = "";
        public string? Requiredby { get; set; }
        public string? Department { get; set; }
        public string? DocNotes { get; set; }
        public string LockStatus { get; set; } = "S";
        public string? SystemIP { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>Parameters of SP_MRNDtl (SQL 44).</summary>
    public class SP_MRNDtl
    {
        public int MTransNo { get; set; }
        public int TransSlNo { get; set; }
        public int SlNo { get; set; }
        public int PartNo { get; set; }
        public int? OAYear { get; set; }
        public int? OANo { get; set; }
        public int? OAID { get; set; }
        public string? PartDescription { get; set; }
        public int? GradeNo { get; set; }
        public string? Grade { get; set; }
        public string? ItemRemarks { get; set; }
        public decimal Qty { get; set; }
        public decimal BalQty { get; set; }
        public string? Unit { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
        public int? ConvPartNo { get; set; }
        public string? ConvDescription { get; set; }
        public int? ConvGradeNo { get; set; }
        public string? ConvGrade { get; set; }
        public decimal? ConvQty { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public int SubscID { get; set; }
    }
}
