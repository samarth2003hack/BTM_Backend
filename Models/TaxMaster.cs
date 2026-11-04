using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A GST rate and the ledgers each component posts to.
    //   TaxP   total rate (CGST + SGST for intra-state, IGST for inter-state)
    //   UsedIn 'Sales' | 'Purchase' | 'Both'
    [Table("TaxMaster")]
    public class TaxMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? TaxCode { get; set; }

        [Column(TypeName = "numeric(6,2)")]
        public decimal TaxP { get; set; }

        [Column(TypeName = "numeric(6,2)")]
        public decimal? CessP { get; set; }

        public int? CGSTAcNo { get; set; }
        public int? SGSTAcNo { get; set; }
        public int? IGSTAcNo { get; set; }
        public int? CessAcNo { get; set; }
        public int? PSACNo { get; set; }
        public string? UsedIn { get; set; }

        public string? SystemIP { get; set; }
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

    // Parameters for SP_TaxMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_TaxMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string TaxCode { get; set; } = string.Empty;
        public decimal TaxP { get; set; }
        public decimal? CessP { get; set; }
        public int? CGSTAcNo { get; set; }
        public int? SGSTAcNo { get; set; }
        public int? IGSTAcNo { get; set; }
        public int? CessAcNo { get; set; }
        public int? PSACNo { get; set; }
        public string UsedIn { get; set; } = "Both";

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class TaxMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
