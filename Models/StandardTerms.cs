using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // The reusable term library. One table serves both sides of the ledger:
    // TermType ('CUSTOMER' / 'SUPPLIER' / 'PAYMENT' / ...) says which picker a
    // term belongs in, and titles are unique per type per subscription.
    [Table("StandardTerms")]
    public class StandardTerms
    {
        [Key]
        public int MTransNo { get; set; }

        public string? TermType { get; set; }
        public decimal? TermSlNo { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }

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

    // Parameters for SP_StandardTerms. Status = 'Insert' | 'Update' | 'Delete'.
    // TermType is upper-cased by the procedure; TermSlNo 0 takes the next number
    // within the type.
    public class SP_StandardTerms
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string TermType { get; set; } = string.Empty;
        public decimal? TermSlNo { get; set; }
        public string TermTitle { get; set; } = string.Empty;
        public string? TermDescription { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
