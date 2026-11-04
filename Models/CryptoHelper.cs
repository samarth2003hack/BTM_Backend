using System.Security.Cryptography;
using System.Text;

namespace EMSAPI.Models
{
    /// <summary>
    /// Reversible password storage for UserMaster.PW.
    ///
    /// The scheme (TripleDES/ECB/PKCS7 with a fixed key) is carried over from
    /// LMSAPI so existing tooling and any migrated user rows stay compatible.
    ///
    /// It is reversible by design because the UI shows the current password back
    /// to an administrator on the user-edit screen. That is NOT how passwords
    /// should be stored: a one-way hash (PBKDF2 / bcrypt / Argon2) plus a
    /// "reset password" flow instead of "reveal password" is the correct fix.
    /// Anyone who can read the database or this source can recover every
    /// password. Treat replacing this as a security to-do, not a permanent design.
    /// </summary>
    public static class CryptoHelper
    {
        private const string DefaultKey = "mskl-1sv4-xklq23";

        public static string Encrypt(string input, string key = DefaultKey)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            var inputArray = Encoding.UTF8.GetBytes(input);

            using var tripleDES = TripleDES.Create();
            tripleDES.Key = Encoding.UTF8.GetBytes(key);
            tripleDES.Mode = CipherMode.ECB;
            tripleDES.Padding = PaddingMode.PKCS7;

            using var transform = tripleDES.CreateEncryptor();
            var result = transform.TransformFinalBlock(inputArray, 0, inputArray.Length);

            return Convert.ToBase64String(result, 0, result.Length);
        }

        public static string Decrypt(string input, string key = DefaultKey)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            try
            {
                var inputArray = Convert.FromBase64String(input);

                using var tripleDES = TripleDES.Create();
                tripleDES.Key = Encoding.UTF8.GetBytes(key);
                tripleDES.Mode = CipherMode.ECB;
                tripleDES.Padding = PaddingMode.PKCS7;

                using var transform = tripleDES.CreateDecryptor();
                var result = transform.TransformFinalBlock(inputArray, 0, inputArray.Length);

                return Encoding.UTF8.GetString(result);
            }
            catch
            {
                // A value that was never encrypted (or was written by hand) must
                // not take the whole request down.
                return string.Empty;
            }
        }
    }
}
