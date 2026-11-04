using System.ComponentModel.DataAnnotations;

namespace EMSAPI.Models
{
    public class MaterialComposition
    {
        [Key]
        public int MTransNo { get; set; }

        public int MaterialNo { get; set; }

        public string? Material { get; set; }
        public string? HeatTreatment { get; set; }
        public string? Description { get; set; }
        public string? ShortName { get; set; }

        public decimal? MinR { get; set; }
        public decimal? MaxR { get; set; }

        public string? MType { get; set; }
        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }

        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }

        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public int? SubscID { get; set; }
    }


    public class SP_MaterialComposition
    {
        public int MTransNo { get; set; }

        public int MCount { get; set; }

        public int MaterialNo { get; set; }

        public string? Material { get; set; }
        public string? HeatTreatment { get; set; }
        public string? Description { get; set; }
        public string? ShortName { get; set; }

        public decimal? MinR { get; set; }
        public decimal? MaxR { get; set; }

        public string? MType { get; set; }

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