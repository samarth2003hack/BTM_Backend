using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // LockStatus is the ACTIVE flag used by the login flow:
    //   'Y' = active, 'N' = inactive, 'B' = blocked.
    [Table("UserMaster")]
    public class UserMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? UserName { get; set; }
        public string? UserId { get; set; }
        public string? PW { get; set; }
        public string? UserType { get; set; }
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public string? ContactNo { get; set; }
        public string? EmailId { get; set; }
        public string? ImageFile { get; set; }
        public string? AccessTemplate { get; set; }
        public string? Branch { get; set; }

        public int? PWExpiry { get; set; }
        public int? BadLogins { get; set; }
        public int? TokenNo { get; set; }

        public DateTime? LastLogin { get; set; }

        [Column("logoutDate")]
        public DateTime? LogoutDate { get; set; }

        public string? HomePage { get; set; }
        public string? SystemIP { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int? SubscID { get; set; }

        // Personal details, edited only from My Profile (SQL 41).
        public string? AltContactNo { get; set; }
        public DateTime? DOB { get; set; }
        public string? Gender { get; set; }
        public string? MaritalStatus { get; set; }
        public DateTime? AnniversaryDate { get; set; }
        public string? Religion { get; set; }
        public string? Nationality { get; set; }
        public string? Address { get; set; }
        public string? AboutMe { get; set; }
    }

    // Body of POST UserMaster/Profile/Update. Only the caller's own row is
    // touched — the row comes from the token, never from this body. UserId,
    // Department, Designation, UserType and access fields are deliberately
    // absent: they belong to User Master, not to self-service.
    public class UpdateProfileRequest
    {
        public string? UserName { get; set; }
        public string? ContactNo { get; set; }
        public string? AltContactNo { get; set; }
        public string? EmailId { get; set; }
        public DateTime? DOB { get; set; }
        public string? Gender { get; set; }
        public string? MaritalStatus { get; set; }
        public DateTime? AnniversaryDate { get; set; }
        public string? Religion { get; set; }
        public string? Nationality { get; set; }
        public string? Address { get; set; }
        public string? AboutMe { get; set; }
    }

    // Parameters for SP_UserMaster. Status = 'Insert' | 'Update' | 'Delete'.
    // PW is encrypted by the controller before it reaches the procedure; leaving
    // it empty on Update keeps the stored password unchanged.
    public class SP_UserMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string UserName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string? PW { get; set; }
        public string UserType { get; set; } = "USER";
        public string? Designation { get; set; }
        public string? Department { get; set; }
        public string? ContactNo { get; set; }
        public string? EmailId { get; set; }
        public string? ImageFile { get; set; }
        public string? AccessTemplate { get; set; }
        public string? Branch { get; set; }

        public int? PWExpiry { get; set; }
        public int? BadLogins { get; set; }
        public string? HomePage { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class UserMasterResponse
    {
        public int ID { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateSubscRequest
    {
        public int SubscID { get; set; }
        public int UserNo { get; set; }
    }
}
