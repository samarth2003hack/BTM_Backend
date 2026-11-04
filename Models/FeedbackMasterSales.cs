using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    public class FeedbackMasterSales
    {
        [Key]
        public int MTransNo { get; set; }

        public string FeedbackParameter { get; set; } = string.Empty;
        public string FeedbackType { get; set; } = string.Empty;

        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }

        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }

        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string DeleteStatus { get; set; } = "N";
        public string? DeleteReason { get; set; }

        public string LockStatus { get; set; } = "N";
        public int? LockCounter { get; set; }

        public int? SubscID { get; set; }
    }


    public class SP_FeedbackMasterSales
    {
        public int MTransNo { get; set; }

        public int MCount { get; set; }

        public string FeedbackParameter { get; set; } = string.Empty;
        public string FeedbackType { get; set; } = string.Empty;

        public int? AmdNo { get; set; }
        public string? AmdNotes { get; set; }

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