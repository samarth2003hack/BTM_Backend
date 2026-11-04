using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // Login / logout audit trail. A row is opened at login and stamped with the
    // logout date/time when the session ends.
    [Table("AccessLog")]
    public class AccessLog
    {
        [Key]
        public int MTransNo { get; set; }

        public string? IPAddress { get; set; }
        public DateTime? LoginDate { get; set; }
        public string? LoginTime { get; set; }
        public DateTime? LogoutDate { get; set; }
        public string? LogoutTime { get; set; }
        public string? LatLong { get; set; }
        public string? Location { get; set; }
        public string? LoginType { get; set; }
        public string? LoginId { get; set; }
        public int? SubscID { get; set; }
    }

    // Body of POST /api/Home/SaveAccessLog.
    //
    // The browser-only half of a login: where the user is, plus which session is
    // saying so. LoginId identifies the audit row to complete; UserNo and TokenNo
    // identify the live session, and the token is what authorises the write.
    public class AccessLogRequest
    {
        public string? LoginId { get; set; }
        public string? LoginType { get; set; }
        public string? LatLong { get; set; }
        public string? Location { get; set; }
        public int UserNo { get; set; }
        public string? TokenNo { get; set; }
    }

    // Parameters for SP_AccessLog. Status = 'Insert' (login) | 'Logout'.
    public class SP_AccessLog
    {
        public int MTransNo { get; set; }
        public string? IPAddress { get; set; }
        public string? LatLong { get; set; }
        public string? Location { get; set; }
        public string? LoginType { get; set; }
        public string LoginId { get; set; } = string.Empty;
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
