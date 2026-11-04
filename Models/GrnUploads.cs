namespace EMSAPI.Models
{
    /// <summary>
    /// Where inward attachments go on disk, and the rules for getting there.
    ///
    /// The database rows (GRNAttachment.AttFile, GRNMst.NCCloseAttachment)
    /// hold only the relative path this returns — the bytes live under the
    /// API's web root, so the same static-file pipeline that serves the front
    /// end serves them back.
    ///
    /// Three things are enforced here and nowhere else, so every caller gets
    /// them whether it remembers to or not:
    ///
    ///   1. The extension must be on the allowlist. An upload folder under the
    ///      web root is reachable by URL, so a .html or .js file dropped into
    ///      it would be served from the application's own origin — which is
    ///      how an attachment field turns into stored cross-site scripting.
    ///   2. The stored name is generated, never the caller's. Only the
    ///      extension survives, so "..\..\web.config" and a name that merely
    ///      collides with an existing file are both impossible.
    ///   3. The original name is kept for display by the caller, in AttNotes —
    ///      not used to address the file.
    /// </summary>
    public static class GrnUploads
    {
        /// <summary>
        /// Deliberately a short allowlist rather than a blocklist of dangerous
        /// types: a blocklist is only ever as good as its last update.
        /// </summary>
        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",  ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff",
            ".doc",  ".docx", ".xls", ".xlsx", ".csv", ".txt", ".rtf",
            ".zip",  ".rar", ".7z", ".eml", ".msg", ".dwg", ".dxf"
        };

        private const long MaxBytes = 25L * 1024 * 1024;   // 25 MB

        public record Stored(string? RelativePath, string? FullPath, string? Error);

        /// <summary>
        /// Writes the upload under wwwroot/Uploads/{area}/{ownerId}/ and
        /// returns the path to store on the row. On refusal, RelativePath is
        /// null and Error says why — the caller turns that into a 400.
        /// </summary>
        public static async Task<Stored> SaveAsync(HttpContext http, string area, int ownerId, IFormFile file)
        {
            if (file.Length == 0)
                return new Stored(null, null, "The uploaded file is empty.");

            if (file.Length > MaxBytes)
                return new Stored(null, null, $"File is larger than {MaxBytes / (1024 * 1024)} MB.");

            var ext = Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(ext) || !Allowed.Contains(ext))
                return new Stored(null, null,
                    $"'{ext}' files cannot be attached. Allowed: {string.Join(", ", Allowed.OrderBy(x => x))}.");

            var env  = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");

            var folder = Path.Combine(root, "Uploads", area, ownerId.ToString());
            Directory.CreateDirectory(folder);

            // The caller's name is never part of the path. Only the extension
            // is carried over, and it has already been checked.
            var name     = $"{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var fullPath = Path.Combine(folder, name);

            await using (var stream = new FileStream(fullPath, FileMode.CreateNew))
                await file.CopyToAsync(stream);

            return new Stored($"Uploads/{area}/{ownerId}/{name}", fullPath, null);
        }

        /// <summary>
        /// Removes a file written moments ago whose database row was then
        /// refused. Never used to delete an attachment a user can see — those
        /// are soft-deleted in the row and the file is kept, so the audit
        /// trail still leads to what was attached.
        /// </summary>
        public static void TryDelete(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;

            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch
            {
                // An orphaned temp file is not worth failing the request over;
                // the caller has already been told the save was refused.
            }
        }
    }
}
