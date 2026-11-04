using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // MTransNo is the hand-assigned module number (no IDENTITY on the table) and
    // is the value AccessTemplate.ModuleNo points at.
    [Table("Menulist")]
    public class Menulist
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int MTransNo { get; set; }

        public string? Category { get; set; }
        public int? CSLNo { get; set; }
        public string? Section { get; set; }
        public int? SSLNo { get; set; }
        public string? ModuleName { get; set; }
        public int? MSLNo { get; set; }
        public string? ModuleIcon { get; set; }
        public string? ModulePage { get; set; }
    }

    // Parameters for SP_Menulist. Status = 'Insert' | 'Update' | 'Delete'.
    // Pass MTransNo = 0 on Insert to have the next module number allocated.
    public class SP_Menulist
    {
        public int MTransNo { get; set; }
        public string? Category { get; set; }
        public int? CSLNo { get; set; }
        public string? Section { get; set; }
        public int? SSLNo { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public int? MSLNo { get; set; }
        public string? ModuleIcon { get; set; }
        public string? ModulePage { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
