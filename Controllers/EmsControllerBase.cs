using Microsoft.AspNetCore.Mvc;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Shared response envelope and error formatting for every EMS controller.
    ///
    /// Every endpoint answers with the same shape so the front-end can handle
    /// results uniformly:
    ///     { success, message, data, statusCode }
    /// </summary>
    [ApiController]
    public abstract class EmsControllerBase : ControllerBase
    {
        protected IActionResult OkResult(object? data, string message = "Success.") =>
            Ok(new { success = true, message, data, statusCode = 200 });

        protected IActionResult BadRequestResult(string message) =>
            BadRequest(new { success = false, message, data = (object?)null, statusCode = 400 });

        protected IActionResult NotFoundResult(string message = "Record not found.") =>
            NotFound(new { success = false, message, data = (object?)null, statusCode = 404 });

        protected IActionResult ConflictResult(string message) =>
            Conflict(new { success = false, message, data = (object?)null, statusCode = 409 });

        protected IActionResult ErrorResult(Exception ex) =>
            StatusCode(500, new
            {
                success = false,
                message = GetFullExceptionMessage(ex),
                data = (object?)null,
                statusCode = 500
            });

        /// <summary>Flattens an exception chain into one readable line.</summary>
        protected static string GetFullExceptionMessage(Exception ex)
        {
            var message = ex.Message;
            var inner = ex.InnerException;
            var level = 1;

            while (inner != null)
            {
                message += $" | Inner Level {level}: {inner.Message}";
                inner = inner.InnerException;
                level++;
            }

            return message;
        }

        /// <summary>Caller IP, used to stamp SystemIP on every write.</summary>
        protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
