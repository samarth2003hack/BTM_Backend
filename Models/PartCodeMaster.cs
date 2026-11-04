using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // The building blocks of a generated part code: for one Parameter (say
    // "BODY MATERIAL"), every Code it may take and what that code means.
    // Unique on (SubscID, Parameter, Code).
    [Table("PartCodeMaster")]
    public class PartCodeMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? Parameter { get; set; }
        public string? Code { get; set; }
        public string? Description { get; set; }
        public string? ValveType { get; set; }

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

    // Parameters for SP_PartCodeMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_PartCodeMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string Parameter { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ValveType { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Which parameters make up the code for a part type, and in what order.
    // SlNo is the parameter's position in the generated code; MaxLength caps the
    // segment. Unique on (SubscID, PartType, Parameter).
    [Table("PartParameterList")]
    public class PartParameterList
    {
        [Key]
        public int MTransNo { get; set; }

        public string? PartType { get; set; }
        public string? Parameter { get; set; }
        public int? SlNo { get; set; }
        public int? MaxLength { get; set; }

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

    // Parameters for SP_PartParameterList. Status = 'Insert' | 'Update' | 'Delete'.
    // SlNo 0 takes the next free position within the part type.
    public class SP_PartParameterList
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string PartType { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;
        public int? SlNo { get; set; }
        public int? MaxLength { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
