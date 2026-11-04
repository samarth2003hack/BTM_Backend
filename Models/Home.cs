namespace EMSAPI.Models
{
    public class LoginRequest
    {
        public string? Loginid { get; set; }
        public string? PW { get; set; }
    }

    public class FileModel
    {
        public string? FileName { get; set; }
    }

    // Shared envelope returned by every controller in this project.
    public class ApiResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? Data { get; set; }
        public int StatusCode { get; set; }
    }
}
