using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    [Route("api/[controller]")]
    public class UserMasterController : EmsControllerBase
    {
        private readonly EMSContext db;

        public UserMasterController(EMSContext context) => db = context;

        //=========================================
        // SAVE (Insert / Update via SP_UserMaster)
        //=========================================
        [HttpPost("SaveData")]
        public async Task<IActionResult> SaveData([FromBody] SP_UserMaster dto)
        {
            if (dto == null)
                return BadRequestResult("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.UserId))
                return BadRequestResult("Login ID is required.");

            if (string.IsNullOrWhiteSpace(dto.UserName))
                return BadRequestResult("User Name is required.");

            if (dto.SubscID <= 0 || dto.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            var isInsert = !string.Equals(dto.Status, "Update", StringComparison.OrdinalIgnoreCase);

            if (isInsert && string.IsNullOrWhiteSpace(dto.PW))
                return BadRequestResult("Password is required.");

            try
            {
                // Blank on Update means "keep the existing password" — the
                // procedure handles that, so only encrypt when a value is given.
                if (!string.IsNullOrWhiteSpace(dto.PW))
                    dto.PW = CryptoHelper.Encrypt(dto.PW);

                dto.SystemIP = ClientIp;

                var result = await db.CallUserMasterSP(dto);

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // LIST — never returns PW
        //=========================================
        [HttpGet("GetList/{subscID}")]
        public async Task<IActionResult> GetList(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var data = await db.UserMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.UserName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.UserName,
                        x.UserId,
                        x.UserType,
                        x.Designation,
                        x.Department,
                        x.ContactNo,
                        x.EmailId,
                        x.ImageFile,
                        x.AccessTemplate,
                        x.Branch,
                        x.PWExpiry,
                        x.BadLogins,
                        x.LastLogin,
                        x.LogoutDate,
                        x.HomePage,
                        x.LockStatus,
                        x.LockCounter,
                        x.CreatedBy,
                        x.CreatedOn,
                        x.ModifiedBy,
                        x.ModifiedOn
                    })
                    .ToListAsync();

                return OkResult(data, data.Count > 0 ? "Data found." : "No records found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // SINGLE RECORD — decrypts PW for the edit form
        //=========================================
        [HttpGet("GetData/{id}")]
        public async Task<IActionResult> GetData(int id)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                // Materialise first: decryption runs in .NET, not in SQL.
                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == id && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult();

                var data = new
                {
                    user.MTransNo,
                    user.UserName,
                    user.UserId,
                    PW = CryptoHelper.Decrypt(user.PW ?? string.Empty),
                    user.UserType,
                    user.Designation,
                    user.Department,
                    user.ContactNo,
                    user.EmailId,
                    user.ImageFile,
                    user.AccessTemplate,
                    user.Branch,
                    user.PWExpiry,
                    user.BadLogins,
                    user.TokenNo,
                    user.LastLogin,
                    user.LogoutDate,
                    user.HomePage,
                    user.LockStatus,
                    user.LockCounter,
                    user.SubscID
                };

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpGet("GetDropdown/{subscID}")]
        public async Task<IActionResult> GetDropdown(int subscID)
        {
            try
            {
                var data = await db.UserMaster
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.UserName)
                    .Select(x => new { x.MTransNo, x.UserId, x.UserName, x.UserType })
                    .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpDelete("DeleteData/{id}")]
        public async Task<IActionResult> DeleteData(
            int id,
            [FromQuery] int userNo,
            [FromQuery] int subscID,
            [FromQuery] string reason = "")
        {
            if (id <= 0 || userNo <= 0 || subscID <= 0)
                return BadRequestResult("Invalid input parameters.");

            if (id == userNo)
                return BadRequestResult("You cannot delete the account you are signed in with.");

            try
            {
                var result = await db.CallUserMasterSP(new SP_UserMaster
                {
                    MTransNo = id,
                    CreatedBy = userNo,
                    SubscID = subscID,
                    DeleteReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason,
                    Status = "Delete"
                });

                if (result.ID == 0)
                    return ConflictResult(result.Message);

                return OkResult(new { ID = result.ID }, result.Message);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // Switch the active subscription of a user
        //=========================================
        [HttpPost("UpdateSubscID")]
        public async Task<IActionResult> UpdateSubscID([FromBody] UpdateSubscRequest req)
        {
            if (req == null || req.UserNo <= 0 || req.SubscID <= 0)
                return BadRequestResult("Invalid request.");

            try
            {
                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == req.UserNo);

                if (user == null)
                    return NotFoundResult("User not found.");

                user.SubscID = req.SubscID;
                await db.SaveChangesAsync();

                return OkResult(new { req.UserNo, req.SubscID }, "Subscription updated successfully.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // MY PROFILE — self-service, signed-in user only
        //
        // These never take a user number: the row is the one whose token
        // came in (TokenAuthFilter puts it in HttpContext.Items). They only
        // UPDATE that existing row — nothing here can create a user.
        //=========================================
        private static readonly string[] Genders = { "Male", "Female", "Other" };
        private static readonly string[] MaritalStatuses = { "Married", "Unmarried" };

        private static readonly HashSet<string> PhotoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif" };

        private const long MaxPhotoBytes = 1L * 1024 * 1024;   // 1 MB, as the page says

        private int CallerUserNo => HttpContext.Items["UserNo"] as int? ?? 0;

        [HttpGet("Profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userNo = CallerUserNo;
            if (userNo <= 0)
                return BadRequestResult("Invalid session.");

            try
            {
                var data = await db.UserMaster
                    .AsNoTracking()
                    .Where(x => x.MTransNo == userNo && x.DeleteStatus == "N")
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.UserName,
                        x.UserId,
                        x.UserType,
                        x.Designation,
                        x.Department,
                        x.ContactNo,
                        x.AltContactNo,
                        x.EmailId,
                        x.ImageFile,
                        x.DOB,
                        x.Gender,
                        x.MaritalStatus,
                        x.AnniversaryDate,
                        x.Religion,
                        x.Nationality,
                        x.Address,
                        x.AboutMe,
                        x.LastLogin
                    })
                    .FirstOrDefaultAsync();

                if (data == null)
                    return NotFoundResult("User not found.");

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        [HttpPost("Profile/Update")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
        {
            var userNo = CallerUserNo;
            if (userNo <= 0)
                return BadRequestResult("Invalid session.");

            if (req == null)
                return BadRequestResult("Invalid request.");

            var userName = Clean(req.UserName);
            var contactNo = Clean(req.ContactNo);
            var altContactNo = Clean(req.AltContactNo);
            var emailId = Clean(req.EmailId);
            var gender = Clean(req.Gender);
            var maritalStatus = Clean(req.MaritalStatus);
            var today = DateTime.Today;

            if (userName == null) return BadRequestResult("User Name is required.");
            if (userName.Length > 100) return BadRequestResult("User Name cannot exceed 100 characters.");

            if (contactNo == null) return BadRequestResult("Phone Number is required.");
            if (!IsPhone(contactNo)) return BadRequestResult("Phone Number must be 10 digits.");
            if (altContactNo != null && !IsPhone(altContactNo))
                return BadRequestResult("Alternative Contact Number must be 10 digits.");
            if (altContactNo != null && altContactNo == contactNo)
                return BadRequestResult("Alternative Contact Number must differ from the Phone Number.");

            if (emailId == null) return BadRequestResult("Email is required.");
            if (emailId.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(emailId, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return BadRequestResult("Enter a valid Email address.");

            if (req.DOB == null) return BadRequestResult("Date of Birth is required.");
            if (req.DOB.Value.Date > today) return BadRequestResult("Date of Birth cannot be in the future.");
            if (req.DOB.Value.Year < 1900) return BadRequestResult("Enter a valid Date of Birth.");

            gender = Genders.FirstOrDefault(g => string.Equals(g, gender, StringComparison.OrdinalIgnoreCase));
            if (gender == null) return BadRequestResult("Select a Gender.");

            if (maritalStatus != null)
            {
                maritalStatus = MaritalStatuses.FirstOrDefault(m => string.Equals(m, maritalStatus, StringComparison.OrdinalIgnoreCase));
                if (maritalStatus == null) return BadRequestResult("Invalid Marital Status.");
            }

            // Anniversary only means something for a married user; anything
            // else clears it rather than leaving a stale date behind.
            DateTime? anniversary = maritalStatus == "Married" ? req.AnniversaryDate?.Date : null;
            if (anniversary != null)
            {
                if (anniversary > today) return BadRequestResult("Anniversary Date cannot be in the future.");
                if (anniversary <= req.DOB.Value.Date) return BadRequestResult("Anniversary Date must be after the Date of Birth.");
            }

            var religion = Clean(req.Religion);
            var nationality = Clean(req.Nationality);
            var address = Clean(req.Address);
            var aboutMe = Clean(req.AboutMe);

            if (religion?.Length > 50) return BadRequestResult("Religion cannot exceed 50 characters.");
            if (nationality?.Length > 50) return BadRequestResult("Nationality cannot exceed 50 characters.");
            if (address?.Length > 250) return BadRequestResult("Address cannot exceed 250 characters.");
            if (aboutMe?.Length > 500) return BadRequestResult("About Me cannot exceed 500 characters.");

            try
            {
                var user = await db.UserMaster
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult("User not found.");

                user.UserName = userName;
                user.ContactNo = contactNo;
                user.AltContactNo = altContactNo;
                user.EmailId = emailId;
                user.DOB = req.DOB.Value.Date;
                user.Gender = gender;
                user.MaritalStatus = maritalStatus;
                user.AnniversaryDate = anniversary;
                user.Religion = religion;
                user.Nationality = nationality;
                user.Address = address;
                user.AboutMe = aboutMe;
                user.SystemIP = ClientIp;
                user.ModifiedBy = userNo;
                user.ModifiedOn = DateTime.Now;

                await db.SaveChangesAsync();

                return OkResult(new { ID = userNo }, "Profile updated successfully.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Replaces the caller's photo. Stored under wwwroot/Uploads/UserPhoto/{userNo}/
        /// via GrnUploads (generated name, extension allowlist); only images up to
        /// 1 MB are accepted here. The previous photo file is removed once the
        /// row points at the new one.
        /// </summary>
        [HttpPost("Profile/Photo")]
        [RequestSizeLimit(2 * 1024 * 1024)]
        public async Task<IActionResult> UpdateProfilePhoto([FromForm] IFormFile? file)
        {
            var userNo = CallerUserNo;
            if (userNo <= 0)
                return BadRequestResult("Invalid session.");

            if (file == null || file.Length == 0)
                return BadRequestResult("Choose a photo to upload.");

            if (!PhotoExtensions.Contains(Path.GetExtension(file.FileName) ?? ""))
                return BadRequestResult("Only JPG, GIF or PNG photos are allowed.");

            if (file.Length > MaxPhotoBytes)
                return BadRequestResult("The photo must be 1 MB or smaller.");

            GrnUploads.Stored? stored = null;

            try
            {
                var user = await db.UserMaster
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult("User not found.");

                stored = await GrnUploads.SaveAsync(HttpContext, "UserPhoto", userNo, file);
                if (stored.RelativePath == null)
                    return BadRequestResult(stored.Error ?? "The photo could not be saved.");

                var previous = user.ImageFile;

                user.ImageFile = stored.RelativePath;
                user.SystemIP = ClientIp;
                user.ModifiedBy = userNo;
                user.ModifiedOn = DateTime.Now;

                await db.SaveChangesAsync();

                // Only ever delete inside this user's own photo folder — the old
                // value may be a legacy name set from User Master.
                var ownPrefix = $"Uploads/UserPhoto/{userNo}/";
                if (!string.IsNullOrWhiteSpace(previous) &&
                    previous.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !previous.Contains(".."))
                {
                    var env = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
                    var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
                    GrnUploads.TryDelete(Path.Combine(root, previous.Replace('/', Path.DirectorySeparatorChar)));
                }

                return OkResult(new { ImageFile = stored.RelativePath }, "Photo updated successfully.");
            }
            catch (Exception ex)
            {
                GrnUploads.TryDelete(stored?.FullPath);
                return ErrorResult(ex);
            }
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsPhone(string value) =>
            value.Length == 10 && value.All(char.IsDigit);

        //=========================================
        // Unlock an account locked out by bad logins
        //=========================================
        [HttpPost("ResetLockCounter/{id}")]
        public async Task<IActionResult> ResetLockCounter(int id)
        {
            if (id <= 0)
                return BadRequestResult("Invalid MTransNo.");

            try
            {
                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == id);

                if (user == null)
                    return NotFoundResult("User not found.");

                user.LockCounter = 0;
                await db.SaveChangesAsync();

                return OkResult(new { ID = id }, "Login attempts reset.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
