using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // ExtraTermsMaster — Standalone Master
    // ============================================================
    [Table("ExtraTermsMaster")]
    public class ExtraTermsMaster
    {
        [Key] public int MTransNo { get; set; }

        public decimal? SlNo { get; set; }
        public string? ModuleName { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ConfYN { get; set; }

        public string? SystemIP { get; set; }
        public short? CreatedBy { get; set; }        // SMALLINT
        public DateTime? CreatedOn { get; set; }     // DATETIME
        public short? Modifiedby { get; set; }       // SMALLINT — exact casing from table
        public DateTime? ModifiedOn { get; set; }    // DATETIME

        public byte? LockCounter { get; set; }       // TINYINT
        public string? DeleteStatus { get; set; }
        public string? LockStatus { get; set; }
        public short? SubscID { get; set; }          // SMALLINT
        public string? DeleteReason { get; set; }
        public int? TempNo { get; set; }
    }

    public class SP_ExtraTermsMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public decimal? SlNo { get; set; }
        public string? ModuleName { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ConfYN { get; set; }
        public int? TempNo { get; set; }

        public string? SystemIP { get; set; }
        public short? CreatedBy { get; set; }        // SMALLINT
        public DateTime? CreatedOn { get; set; }     // DATETIME
        public short? ModifiedBy { get; set; }       // SMALLINT — DTO uses standard casing
        public DateTime? ModifiedOn { get; set; }    // DATETIME

        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public short? SubscID { get; set; }          // SMALLINT

        public string Status { get; set; } = "Insert";
    }
}
