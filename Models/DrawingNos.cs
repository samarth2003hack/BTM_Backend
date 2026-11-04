using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A drawing. PartNo is optional: a drawing is its own master record and
    // survives the part it happens to be tagged to, so deleting a part leaves
    // its drawings alone. DrawingNo is unique per subscription.
    [Table("DrawingNos")]
    public class DrawingNos
    {
        [Key]
        public int MTransNo { get; set; }

        public string? DrawingNo { get; set; }
        public int? PartNo { get; set; }
        public string? DrawingType { get; set; }
        public string? DrawingGroup { get; set; }
        public string? PartGroup { get; set; }
        public int? PSlNo { get; set; }
        public string? DrawingDescription { get; set; }
        public string? DrawingRemarks { get; set; }
        public int? RevNo { get; set; }
        public DateTime? RevDate { get; set; }
        public string? RevStatus { get; set; }
        public string? FileAttach { get; set; }

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

    // Parameters for SP_DrawingNos. Status = 'Insert' | 'Update' | 'Delete'.
    // PartNo 0 is stored as NULL rather than failing the foreign key.
    public class SP_DrawingNos
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string DrawingNo { get; set; } = string.Empty;
        public int? PartNo { get; set; }
        public string? DrawingType { get; set; }
        public string? DrawingGroup { get; set; }
        public string? PartGroup { get; set; }
        public int? PSlNo { get; set; }
        public string? DrawingDescription { get; set; }
        public string? DrawingRemarks { get; set; }
        public int? RevNo { get; set; }
        public DateTime? RevDate { get; set; }
        public string? RevStatus { get; set; }
        public string? FileAttach { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
