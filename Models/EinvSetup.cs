using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // e-Invoice / e-Way-Bill gateway credentials — one live row per subscription.
    [Table("EinvSetup")]
    public class EinvSetup
    {
        [Key]
        public int MTransNo { get; set; }

        public string? EUserID { get; set; }
        public string? EPassword { get; set; }
        public string? AppKey { get; set; }
        public string? AuthToken { get; set; }
        public string? Sek { get; set; }
        public string? TokenExp { get; set; }

        public string? EwbAppKey { get; set; }
        public string? EwbAuthToken { get; set; }
        public string? EwbSEK { get; set; }
        public string? EwbTokenExp { get; set; }

        public string? DeleteStatus { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_EinvSetup.
    //   Status = 'Insert' / 'Update' -> upsert the credential row
    //            'Token'             -> refresh only the token fields
    //            'Delete'            -> soft-delete
    public class SP_EinvSetup
    {
        public int MTransNo { get; set; }

        public string? EUserID { get; set; }
        public string? EPassword { get; set; }
        public string? AppKey { get; set; }
        public string? AuthToken { get; set; }
        public string? Sek { get; set; }
        public string? TokenExp { get; set; }

        public string? EwbAppKey { get; set; }
        public string? EwbAuthToken { get; set; }
        public string? EwbSEK { get; set; }
        public string? EwbTokenExp { get; set; }

        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
