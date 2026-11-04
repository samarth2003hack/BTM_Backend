using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("GeneralConfig")]
    public class GeneralConfig
    {
        [Key]
        public int MTransNo { get; set; }

        public string? ConfigKey { get; set; }
        public string? ConfigValue { get; set; }
        public string? ConfigDepartment { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_GeneralConfig. The procedure upserts on
    // (ConfigKey, SubscID); Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_GeneralConfig
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string ConfigKey { get; set; } = string.Empty;
        public string ConfigValue { get; set; } = string.Empty;
        public string? ConfigDepartment { get; set; }

        public int CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
