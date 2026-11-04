using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // IRN / e-Way-Bill result recorded against a source transaction (MTransNo).
    [Table("EinvDtl")]
    public class EinvDtl
    {
        [Key]
        public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public string? IRNNo { get; set; }
        public string? ScanQRNo { get; set; }
        public string? ACKNo { get; set; }
        public string? EWBillNo { get; set; }
        public string? EWBillDt { get; set; }
        public string? AckDt { get; set; }

        public string? DeleteStatus { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_EinvDtl. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_EinvDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }

        public string? IRNNo { get; set; }
        public string? ScanQRNo { get; set; }
        public string? ACKNo { get; set; }
        public string? EWBillNo { get; set; }
        public string? EWBillDt { get; set; }
        public string? AckDt { get; set; }

        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
