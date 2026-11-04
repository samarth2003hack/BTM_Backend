using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Models
{
    /// <summary>
    /// Marks an endpoint (or a whole controller) as reachable without a token.
    /// Only sign-in and the handful of endpoints a signed-out user genuinely
    /// needs should carry this.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkipTokenCheckAttribute : Attribute
    {
    }

    /// <summary>
    /// Rejects any request whose token does not match a token currently stored
    /// in UserMaster.
    ///
    /// The token is the ONLY credential a caller sends — an API key, in effect.
    /// It travels either as a header (preferred) or as a query-string parameter:
    ///
    ///     Header     Query fallback
    ///     --------   --------------
    ///     X-Token    tokenNo
    ///
    /// The token is what /api/Home/login returned: the TripleDES ciphertext of
    /// UserMaster.TokenNo. The filter decrypts it back to that number and looks
    /// for the UserMaster row holding it. A row found means the caller is that
    /// user, and the request runs; no row means the request is refused. The
    /// caller never has to say who they are — the token alone answers it.
    ///
    /// Signing in again issues a new TokenNo, so tokens from any earlier session
    /// stop working — one active session per account. Signing out clears
    /// TokenNo, which invalidates the token immediately.
    ///
    /// A request is refused with 400 Bad Request, as specified, when the token
    /// is missing, malformed, or does not match an active user's stored value.
    ///
    /// NOTE ON SCOPE
    /// This is authentication (proving who the caller is), not authorization.
    /// It does not check that the caller is entitled to the record they are
    /// touching: any signed-in user can still call any endpoint for any
    /// SubscID, because SubscID arrives as a plain parameter. Enforcing that a
    /// caller may only touch their own subscription — and honouring the
    /// AccessTemplate grants per module — is the next step.
    /// </summary>
    public sealed class TokenAuthFilter : IAsyncAuthorizationFilter
    {
        public const string TokenHeader = "X-Token";

        private readonly EMSContext db;
        private readonly ILogger<TokenAuthFilter> logger;

        public TokenAuthFilter(EMSContext context, ILogger<TokenAuthFilter> log)
        {
            db = context;
            logger = log;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // Endpoints explicitly opted out (sign-in, and nothing else by default).
            var endpoint = context.ActionDescriptor.EndpointMetadata;

            if (endpoint.OfType<SkipTokenCheckAttribute>().Any())
                return;

            var request = context.HttpContext.Request;

            var rawToken = FirstNonEmpty(request.Headers[TokenHeader], request.Query["tokenNo"]);

            if (string.IsNullOrWhiteSpace(rawToken))
            {
                context.Result = Reject("Authentication token is required. Send it in the " + TokenHeader + " header.");
                return;
            }

            // The same message for every failure mode below, so a caller cannot
            // use the response to work out which tokens exist or which accounts
            // are locked.
            const string mismatch = "Invalid or expired token. Please sign in again.";

            // Turn the ciphertext back into the number stored on UserMaster.
            // A value that was not produced by CryptoHelper decrypts to an empty
            // string, so a malformed token stops here.
            var plain = CryptoHelper.Decrypt(rawToken.Trim());

            if (!int.TryParse(plain, out var tokenNo) || tokenNo <= 0)
            {
                logger.LogWarning("Malformed token on {Path}", request.Path);
                context.Result = Reject(mismatch);
                return;
            }

            // The token IS the identity: whichever active user holds this
            // TokenNo is the caller. Login guarantees no two active users hold
            // the same one, so this matches at most one row.
            var user = await db.UserMaster
                .AsNoTracking()
                .Where(x => x.TokenNo == tokenNo &&
                            x.DeleteStatus == "N" &&
                            x.LockStatus == "Y")
                .Select(x => new { x.MTransNo, x.UserId, x.SubscID })
                .FirstOrDefaultAsync();

            if (user == null)
            {
                logger.LogWarning("Unknown or expired token on {Path}", request.Path);
                context.Result = Reject(mismatch);
                return;
            }

            // Downstream code can read the verified caller instead of trusting
            // a userNo that arrived in the body.
            context.HttpContext.Items["UserNo"] = user.MTransNo;
            context.HttpContext.Items["UserId"] = user.UserId;
            context.HttpContext.Items["SubscID"] = user.SubscID ?? 0;
        }

        private static string? FirstNonEmpty(params Microsoft.Extensions.Primitives.StringValues[] candidates)
        {
            foreach (var c in candidates)
            {
                var value = c.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        private static IActionResult Reject(string message) =>
            new BadRequestObjectResult(new
            {
                success = false,
                message,
                data = (object?)null,
                statusCode = 400
            });
    }
}
