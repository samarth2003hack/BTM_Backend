using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // Append-only application error sink.
    [Table("ErrorLog")]
    public class ErrorLog
    {
        [Key]
        public int MTransNo { get; set; }

        public string? ErrorMsg { get; set; }
        public string? StackTrace { get; set; }
        public DateTime? ErrorDate { get; set; }
        public string? ErrorTime { get; set; }
        public string? LoginType { get; set; }
        public string? LoginId { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_ErrorLog (insert only).
    public class SP_ErrorLog
    {
        public string? ErrorMsg { get; set; }
        public string? StackTrace { get; set; }
        public string? LoginType { get; set; }
        public string? LoginId { get; set; }
        public int SubscID { get; set; }
    }
}
