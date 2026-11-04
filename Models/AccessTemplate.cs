using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // One row per (template, module) grant. UserMaster.AccessTemplate names the
    // template a user belongs to.
    //   AuthType   : 'A' full access, 'V' view only, 'R' = revoke (never stored)
    //   MyFavorite : 'Y' pins the module to the user's shortcut bar
    [Table("AccessTemplate")]
    public class AccessTemplate
    {
        [Key]
        public int MTransNo { get; set; }

        public string? TemplateName { get; set; }
        public string? Department { get; set; }
        public int ModuleNo { get; set; }
        public string? AuthType { get; set; }
        public string? MyFavorite { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_AccessTemplate. Status = 'Insert' | 'Update' | 'Delete'.
    // AuthType 'R' revokes the grant regardless of Status.
    public class SP_AccessTemplate
    {
        public int MTransNo { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public string? Department { get; set; }
        public int ModuleNo { get; set; }
        public string? AuthType { get; set; }
        public string? MyFavorite { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class AccessTemplateResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
