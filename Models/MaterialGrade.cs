using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A stocking grade of one part. Min/max stock and MOQ live here rather than
    // on PartMaster because they differ per grade. GradeNo is the grade's number
    // within the part, unique on (PartNo, GradeNo).
    [Table("MaterialGrade")]
    public class MaterialGrade
    {
        [Key]
        public int MTransNo { get; set; }

        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string? Grade { get; set; }
        public decimal? MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public decimal? MOQ { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_MaterialGrade. Status = 'Insert' | 'Update' | 'Delete'.
    // GradeNo 0 takes the next number for the part. Deleting a grade that a
    // PriceList rate still points at is rejected.
    public class SP_MaterialGrade
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string Grade { get; set; } = string.Empty;
        public decimal? MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public decimal? MOQ { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Grade to material-standard cross reference — one grade may satisfy several
    // standards. Unique on (SubscID, Grade, MatrStd).
    [Table("MaterialStd")]
    public class MaterialStd
    {
        [Key]
        public int MTransNo { get; set; }

        public string? Grade { get; set; }
        public string? MatrStd { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_MaterialStd. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_MaterialStd
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string Grade { get; set; } = string.Empty;
        public string MatrStd { get; set; } = string.Empty;

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
