using EMSAPI.Models;
using Ionic.Zip;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Database backup: takes a .bak into wwwroot/BackUp and serves it as an
    /// AES-256 password-protected zip.
    ///
    /// Unlike the LMSAPI original, the server, database name and archive
    /// password all come from configuration — nothing is hard-coded, so a
    /// deployment cannot silently back up the wrong database.
    ///
    /// SECURITY: these endpoints are unauthenticated, and a database backup is
    /// the entire dataset in one file. Put them behind an admin-only
    /// authorization policy before this is reachable from anywhere but
    /// localhost.
    /// </summary>
    [Route("api/[controller]")]
    public class BackupController : EmsControllerBase
    {
        private readonly IWebHostEnvironment env;
        private readonly IConfiguration config;

        public BackupController(IWebHostEnvironment environment, IConfiguration configuration)
        {
            env = environment;
            config = configuration;
        }

        private string BackupFolder
        {
            get
            {
                var root = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
                var folder = Path.Combine(root, "BackUp");

                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                return folder;
            }
        }

        /// <summary>Backups already on disk, newest first.</summary>
        [HttpGet("GetFiles")]
        public IActionResult GetFiles()
        {
            try
            {
                var files = new DirectoryInfo(BackupFolder)
                    .GetFiles("*.bak")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Select(f => new
                    {
                        FileName = f.Name,
                        SizeMB = Math.Round(f.Length / 1024d / 1024d, 2),
                        CreatedOn = f.CreationTime
                    })
                    .ToList();

                return OkResult(files, files.Count > 0 ? "Data found." : "No backups found.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Runs BACKUP DATABASE for the configured database.</summary>
        [HttpPost("Backup")]
        public async Task<IActionResult> Backup()
        {
            var connectionString = config.GetConnectionString("EMSConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
                return ErrorResult(new InvalidOperationException("Connection string 'EMSConnection' is not configured."));

            try
            {
                // Take the database name from the connection string rather than a
                // literal, so this always backs up the database the API is using.
                var builder = new SqlConnectionStringBuilder(connectionString);
                var dbName = builder.InitialCatalog;

                if (string.IsNullOrWhiteSpace(dbName))
                    return ErrorResult(new InvalidOperationException("The connection string does not name a database."));

                var fileName = $"{dbName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.bak";
                var fullPath = Path.Combine(BackupFolder, fileName);

                await using var con = new SqlConnection(connectionString);
                await con.OpenAsync();

                // The database name is an identifier and cannot be a parameter, so
                // it is quoted with QUOTENAME; the path is passed as a parameter.
                var cmd = new SqlCommand(
                    @"DECLARE @sql NVARCHAR(MAX) =
                          N'BACKUP DATABASE ' + QUOTENAME(@db) +
                          N' TO DISK = @path WITH INIT, COMPRESSION, STATS = 10';
                      EXEC sp_executesql @sql, N'@path NVARCHAR(4000)', @path = @path;", con)
                {
                    CommandTimeout = 1800
                };

                cmd.Parameters.AddWithValue("@db", dbName);
                cmd.Parameters.AddWithValue("@path", fullPath);

                await cmd.ExecuteNonQueryAsync();

                var info = new FileInfo(fullPath);

                return OkResult(new
                {
                    FileName = fileName,
                    SizeMB = Math.Round(info.Length / 1024d / 1024d, 2)
                }, "Backup completed successfully.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Streams a backup as an AES-256 password-protected zip.</summary>
        [HttpGet("Download")]
        public IActionResult Download([FromQuery] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequestResult("File name is required.");

            // Reduce to a bare file name — no directory component can survive,
            // so the caller cannot walk out of the backup folder.
            fileName = Path.GetFileName(fileName);

            if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Only .bak files can be downloaded.");

            var filePath = Path.Combine(BackupFolder, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFoundResult("Backup file not found.");

            var password = config["Backup:ZipPassword"];

            if (string.IsNullOrWhiteSpace(password))
                return ErrorResult(new InvalidOperationException(
                    "Backup:ZipPassword is not configured. Set it in appsettings before downloading a backup."));

            try
            {
                // A unique temp path so concurrent downloads cannot collide.
                var zipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");

                using (var zip = new ZipFile())
                {
                    zip.Password = password;
                    zip.Encryption = EncryptionAlgorithm.WinZipAes256;
                    zip.UseZip64WhenSaving = Zip64Option.AsNecessary;   // .bak files can exceed 4 GB
                    zip.AddFile(filePath, "");
                    zip.Save(zipPath);
                }

                // Stream rather than ReadAllBytes: a multi-GB backup must not be
                // buffered in memory. DeleteOnClose removes the temp zip once the
                // response has finished streaming.
                var stream = new FileStream(
                    zipPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    FileOptions.DeleteOnClose | FileOptions.Asynchronous);

                return File(stream, "application/zip", fileName + ".zip");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Removes a backup file from disk.</summary>
        [HttpDelete("DeleteFile")]
        public IActionResult DeleteFile([FromQuery] string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return BadRequestResult("File name is required.");

            fileName = Path.GetFileName(fileName);

            if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                return BadRequestResult("Only .bak files can be deleted.");

            var filePath = Path.Combine(BackupFolder, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFoundResult("Backup file not found.");

            try
            {
                System.IO.File.Delete(filePath);
                return OkResult(new { FileName = fileName }, "Backup deleted.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
