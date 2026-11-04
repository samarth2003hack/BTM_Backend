using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Authentication and navigation.
    ///
    /// LockStatus on UserMaster is the ACTIVE flag, not a lock:
    ///     'Y' = active, 'N' = inactive, 'B' = blocked.
    /// LockCounter counts consecutive failed sign-ins and is compared against
    /// the user's BadLogins allowance.
    /// </summary>
    [Route("api/[controller]")]
    public class HomeController : EmsControllerBase
    {
        private readonly EMSContext db;
        private readonly UserSessionRegistry sessions;

        public HomeController(EMSContext context, UserSessionRegistry sessionRegistry)
        {
            db = context;
            sessions = sessionRegistry;
        }

        /// <summary>Real client IP: the first hop of X-Forwarded-For behind a proxy, else the socket.</summary>
        private string RemoteIp()
        {
            var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
            return string.IsNullOrWhiteSpace(forwarded) ? (ClientIp ?? string.Empty) : forwarded;
        }

        //=========================================
        // LOGIN
        //=========================================
        // The only endpoint in the API that does not require a token — it is
        // what issues one. Everything else runs through TokenAuthFilter.
        //
        // ONE ACTIVE LOGIN PER USER (opt-in, so no existing caller is affected):
        //
        //   checkSession=true -> when this login id is still signed in somewhere,
        //                        NO token is issued and nothing is rotated. The
        //                        answer describes that session instead —
        //                        { status: "ActiveSession", session: { … } } — so
        //                        the person at the keyboard can see who holds it,
        //                        from where and since when, and decide.
        //   force=true        -> sign in anyway, taking the session over. The token
        //                        is rotated here, so the other browser is refused
        //                        on its next call and told why by its next
        //                        SessionHeartbeat.
        //
        // Without checkSession the behaviour is exactly as before: the session is
        // replaced silently.
        [SkipTokenCheck]
        [HttpGet("login")]
        public async Task<IActionResult> Login(
            [FromQuery] string loginid,
            [FromQuery] string pw,
            [FromQuery] bool checkSession = false,
            [FromQuery] bool force = false,
            // What kind of client is signing in — "Web", "Mobile". Left empty by
            // callers that predate it, whose audit rows then keep the value they
            // have always had.
            [FromQuery] string loginType = "")
        {
            if (string.IsNullOrWhiteSpace(loginid) || string.IsNullOrWhiteSpace(pw))
                return BadRequestResult("Login ID and password are required.");

            try
            {
                var user = await db.UserMaster
                    .FirstOrDefaultAsync(x => x.UserId == loginid && x.DeleteStatus == "N");

                // Same message whether the login ID is unknown or the password is
                // wrong, so the endpoint cannot be used to enumerate accounts.
                if (user == null)
                    return Unauthorized(new { success = false, message = "Invalid login ID or password.", data = (object?)null, statusCode = 401 });

                if (user.LockStatus == "B")
                    return Unauthorized(new { success = false, message = "This login ID is blocked. Please contact the system administrator.", data = (object?)null, statusCode = 401 });

                if (user.LockStatus != "Y")
                    return Unauthorized(new { success = false, message = "This login ID is inactive. Please contact your reporting manager.", data = (object?)null, statusCode = 401 });

                var lockCounter = user.LockCounter ?? 0;
                var maxBadLogins = user.BadLogins ?? 0;

                if (maxBadLogins > 0 && lockCounter >= maxBadLogins)
                    return Unauthorized(new { success = false, message = "Too many failed sign-in attempts. Your account is locked — reset your password to continue.", data = (object?)null, statusCode = 401 });

                var encrypted = CryptoHelper.Encrypt(pw);

                if (user.PW != encrypted)
                {
                    if (maxBadLogins > 0)
                    {
                        user.LockCounter = lockCounter + 1;
                        await db.SaveChangesAsync();
                    }

                    return Unauthorized(new { success = false, message = "Invalid login ID or password.", data = (object?)null, statusCode = 401 });
                }

                // The password is right. Before anything is changed: is this login
                // id still signed in? Only asked when the caller opted in and did
                // not force it — and nothing is touched either way, so the other
                // session stays live while the user reads the details.
                if (checkSession && !force)
                {
                    var openSession = await DescribeOpenSession(user, loginid);
                    if (openSession != null) return openSession;
                }

                // Successful sign-in: issue a session token and reset the counter.
                //
                // The token is the only credential the caller sends afterwards,
                // so TokenAuthFilter finds the user by it alone. That only works
                // while no two rows hold the same number — hence the retry.
                var tokenNo = 0;

                for (var attempt = 0; attempt < 25; attempt++)
                {
                    var candidate = Random.Shared.Next(100001, 999999);

                    var taken = await db.UserMaster
                        .AsNoTracking()
                        .AnyAsync(x => x.TokenNo == candidate && x.MTransNo != user.MTransNo);

                    if (!taken)
                    {
                        tokenNo = candidate;
                        break;
                    }
                }

                if (tokenNo == 0)
                    return ErrorResult(new Exception("Could not issue a session token. Please try again."));

                // Read before it is rotated: a token that was still there belongs to
                // a session this sign-in is about to take over.
                var previousToken = user.TokenNo ?? 0;
                var previous = previousToken > 0 ? sessions.Get(user.MTransNo) : null;

                user.TokenNo = tokenNo;
                user.LastLogin = DateTime.Now;
                user.LockCounter = 0;
                await db.SaveChangesAsync();

                // Close the audit row of the session just taken over, BEFORE opening
                // this one. Left open it would stay open for ever, and every later
                // "newest open row for this login" lookup would find that orphan
                // instead of a session that is really live.
                if (previousToken > 0)
                {
                    var previousMatches = previous != null && string.Equals(
                        NormToken(previous.TokenNo),
                        CryptoHelper.Encrypt(previousToken.ToString()),
                        StringComparison.Ordinal);

                    await db.CallAccessLogSP(new SP_AccessLog
                    {
                        // 0 falls back to the newest open row for this login, which
                        // is what the register would have pointed at anyway.
                        MTransNo = previousMatches ? previous.AccessLogNo : 0,
                        LoginId = user.UserId ?? loginid,
                        SubscID = user.SubscID ?? 0,
                        Status = "Logout"
                    });
                }

                // Open an AccessLog row for this session. The row number is not
                // returned: logout finds the newest open row for the login on
                // its own, so the client has no use for it.
                var log = await db.CallAccessLogSP(new SP_AccessLog
                {
                    IPAddress = RemoteIp(),
                    // The client type when the caller says so — it is what the
                    // "already logged in" dialog reports as "Device / Type" once the
                    // in-memory register has been forgotten by a restart.
                    LoginType = string.IsNullOrWhiteSpace(loginType) ? user.UserType : loginType.Trim(),
                    LoginId = user.UserId ?? loginid,
                    SubscID = user.SubscID ?? 0,
                    Status = "Insert"
                });

                var token = CryptoHelper.Encrypt(tokenNo.ToString());

                // This sign-in becomes THE session for this user. Whatever was
                // registered before is replaced — that replacement is what the
                // other browser reads as a takeover on its next heartbeat.
                sessions.Start(new ActiveUserSession
                {
                    UserNo = user.MTransNo,
                    LoginId = user.UserId,
                    UserName = user.UserName,
                    TokenNo = token,
                    IPAddress = RemoteIp(),
                    // What kind of client holds the session — "Web", "Mobile" — as
                    // reported by the caller. It is shown as "Device / Type" to the
                    // next person who tries this login id.
                    LoginType = string.IsNullOrWhiteSpace(loginType) ? "Web" : loginType.Trim(),
                    AccessLogNo = log.ID,
                    LoginOn = DateTime.Now
                });

                // The token and nothing else. It is the whole credential, and
                // every detail that used to ride along here — name, user type,
                // subscription, home page, password-expiry flag — now comes from
                // GetHomeInfo, which the token itself unlocks.
                return OkResult(new { TokenNo = token }, "Login successful.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// The "already logged in" answer for a user whose session is still open,
        /// or <c>null</c> when the login id is free and the sign-in may proceed.
        ///
        /// UserMaster.TokenNo decides. Login sets it and every sign-out path clears
        /// it, so a token that is still there means a session is still open — and
        /// unlike the in-memory register, it survives an API restart. That is why a
        /// browser left open overnight, or one that crashed without signing out,
        /// still meets this gate: the answer offers to take the session over, so
        /// nobody is ever locked out of their own account.
        ///
        /// The register only supplies the detail the database does not hold — last
        /// seen, geolocation, client type — and is dropped when it disagrees with
        /// the database, so a ghost entry can never block a user who did sign out.
        /// </summary>
        private async Task<IActionResult?> DescribeOpenSession(UserMaster user, string loginid)
        {
            var dbToken = user.TokenNo ?? 0;

            // Read the entry however long ago it was last seen: "last active 40
            // minutes ago" is exactly what tells someone their own forgotten
            // session apart from a colleague who is working in it right now.
            var live = sessions.Get(user.MTransNo);

            if (live != null)
            {
                // Signed out (token cleared) or superseded (token rotated by a
                // newer login) — either way the entry describes a session that no
                // longer exists and must not be reported as one.
                var stillOpen = dbToken > 0 && string.Equals(
                    NormToken(live.TokenNo),
                    CryptoHelper.Encrypt(dbToken.ToString()),
                    StringComparison.Ordinal);

                if (!stillOpen)
                {
                    sessions.End(user.MTransNo, live.TokenNo);
                    live = null;
                }
            }

            if (dbToken <= 0) return null;   // nobody is in — carry on and sign in

            // Whatever the register cannot say — because the API restarted since
            // that sign-in — comes from the audit row the login opened.
            var audit = await db.AccessLog
                .AsNoTracking()
                .Where(x => x.LoginId == user.UserId)
                .OrderByDescending(x => x.MTransNo)
                .FirstOrDefaultAsync();

            var loginOn = live?.LoginOn ?? user.LastLogin ?? AuditLoginOn(audit);
            var lastSeen = live?.LastSeen;
            var now = DateTime.Now;

            const string message = "This login ID is already signed in on another device.";

            return OkResult(new
            {
                status = "ActiveSession",
                message,
                session = new
                {
                    userNo = user.MTransNo,
                    userName = FirstFilled(live?.UserName, user.UserName),
                    loginId = FirstFilled(live?.LoginId, user.UserId, loginid),
                    loginType = FirstFilled(live?.LoginType, audit?.LoginType),
                    loginOn = loginOn?.ToString("o") ?? string.Empty,
                    loginDate = loginOn?.ToString("dd-MMM-yyyy") ?? string.Empty,
                    loginTime = loginOn?.ToString("hh:mm:ss tt") ?? string.Empty,
                    lastSeenOn = lastSeen?.ToString("o") ?? string.Empty,
                    lastSeenTime = lastSeen?.ToString("hh:mm:ss tt") ?? string.Empty,
                    idleMinutes = lastSeen == null
                        ? 0 : Math.Max(0, (int)Math.Floor((now - lastSeen.Value).TotalMinutes)),
                    activeMinutes = loginOn == null
                        ? 0 : Math.Max(0, (int)Math.Floor((now - loginOn.Value).TotalMinutes)),
                    ipAddress = FirstFilled(live?.IPAddress, audit?.IPAddress),
                    location = FirstFilled(live?.Location, audit?.Location),
                    latLong = FirstFilled(live?.LatLong, audit?.LatLong)
                }
            }, message);
        }

        /// <summary>The login moment on an audit row: its date plus its "HH:mm:ss".</summary>
        private static DateTime? AuditLoginOn(AccessLog? log)
        {
            if (log?.LoginDate == null) return null;

            return TimeSpan.TryParse(log.LoginTime, out var time)
                ? log.LoginDate.Value.Date.Add(time)
                : log.LoginDate.Value;
        }

        /// <summary>First value that is actually filled in, trimmed; "" when none is.</summary>
        private static string FirstFilled(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        private static string? Trunc(string? value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

        //=========================================
        // ACCESS LOG — where the live session is sitting
        //=========================================

        /// <summary>
        /// Adds to the session login already on record the one thing only the
        /// browser knows: the coordinates, and the place name they resolve to, when
        /// the user allowed location.
        ///
        /// Login opens the AccessLog row and stamps IP, date and time, so this
        /// UPDATES that row — it never opens a second one for the same sign-in.
        ///
        /// The point is not the audit trail alone: this location is what the
        /// "already logged in" dialog shows the next person who tries the same login
        /// id, which is what lets them tell their own forgotten session in another
        /// office from a colleague who is working in it right now.
        ///
        /// [SkipTokenCheck] because the client sends the token in the body, where
        /// the global filter does not look. It is still checked here against
        /// UserMaster.TokenNo, and a caller who does not hold the current session
        /// changes nothing. Best-effort throughout: the answer is always 200, so an
        /// audit write can never be the reason a sign-in fails.
        /// </summary>
        [SkipTokenCheck]
        [HttpPost("SaveAccessLog")]
        public async Task<IActionResult> SaveAccessLog([FromBody] AccessLogRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.LoginId))
                    return OkResult(new { success = false }, "LoginId is required.");

                var loginId = request.LoginId.Trim();

                var user = request.UserNo > 0
                    ? await db.UserMaster.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.MTransNo == request.UserNo)
                    : await db.UserMaster.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.UserId == loginId && x.DeleteStatus == "N");

                if (user == null)
                    return OkResult(new { success = false }, "User not found.");

                // Only the browser that holds the live token may say where the
                // session is — otherwise anyone could move a session on the map.
                var expected = CryptoHelper.Encrypt((user.TokenNo ?? 0).ToString());

                if ((user.TokenNo ?? 0) <= 0 ||
                    !string.Equals(NormToken(request.TokenNo), expected, StringComparison.Ordinal))
                    return OkResult(new { success = false }, "Not the current session — nothing recorded.");

                // The row login opened for this session: the newest still-open one
                // for this login id.
                var log = await db.AccessLog
                    .Where(x => x.LoginId == user.UserId && x.LogoutDate == null)
                    .OrderByDescending(x => x.MTransNo)
                    .FirstOrDefaultAsync();

                if (log != null)
                {
                    // Blanks are left alone: a denied location must not wipe what a
                    // previous, allowed one recorded.
                    if (!string.IsNullOrWhiteSpace(request.LatLong))
                        log.LatLong = Trunc(request.LatLong.Trim(), 100);

                    if (!string.IsNullOrWhiteSpace(request.Location))
                        log.Location = Trunc(request.Location.Trim(), 500);

                    await db.SaveChangesAsync();
                }

                sessions.Update(user.MTransNo, request.TokenNo,
                    request.LatLong, request.Location, log?.MTransNo ?? 0);

                return OkResult(new { success = true, mTransNo = log?.MTransNo ?? 0 },
                    "Session details recorded.");
            }
            catch (Exception ex)
            {
                // Never fail a sign-in over an audit write.
                return OkResult(new { success = false }, GetFullExceptionMessage(ex));
            }
        }

        //=========================================
        // LOGOUT
        //=========================================
        /// <summary>
        /// Ends the session the token belongs to.
        ///
        /// Takes no parameters — the token in the header says whose session this
        /// is. Clearing TokenNo invalidates that token immediately.
        /// </summary>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userNo = HttpContext.Items["UserNo"] as int? ?? 0;

            if (userNo <= 0)
                return BadRequestResult("Invalid or expired token. Please sign in again.");

            try
            {
                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == userNo);

                if (user == null)
                    return NotFoundResult("User not found.");

                // Release the live session before anything else, so the same user
                // can sign in again straight away without meeting their own ghost.
                sessions.End(userNo, CryptoHelper.Encrypt((user.TokenNo ?? 0).ToString()));

                user.TokenNo = null;
                user.LogoutDate = DateTime.Now;
                await db.SaveChangesAsync();

                // MTransNo 0 tells the procedure to close the newest open row
                // for this login, which is exactly this session.
                await db.CallAccessLogSP(new SP_AccessLog
                {
                    MTransNo = 0,
                    LoginId = user.UserId ?? string.Empty,
                    SubscID = user.SubscID ?? 0,
                    Status = "Logout"
                });

                return OkResult(new { UserNo = userNo }, "Logout successful.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // TOKEN VERIFICATION
        //=========================================

        /// <summary>
        /// Confirms the token in the X-Token header is still live.
        ///
        /// Takes no parameters: reaching this method at all means TokenAuthFilter
        /// already matched the token to a UserMaster row, so the answer is who
        /// that token belongs to.
        /// </summary>
        [HttpGet("TokenVerification")]
        public IActionResult TokenVerification()
        {
            var userNo = HttpContext.Items["UserNo"] as int? ?? 0;

            if (userNo <= 0)
                return Unauthorized(new { success = false, message = "Verification failed.", data = (object?)null, statusCode = 401 });

            return OkResult(new
            {
                Verified = true,
                UserNo = userNo,
                UserId = HttpContext.Items["UserId"] as string,
                SubscID = HttpContext.Items["SubscID"] as int? ?? 0
            }, "Verified.");
        }

        //=========================================
        // SESSION WATCH
        //=========================================

        // Base64 tokens travel in query strings, where an unencoded '+' arrives
        // as a space. Both sides are normalised before any comparison.
        private static string NormToken(string? token) =>
            (token ?? string.Empty).Trim().Replace(' ', '+');

        /// <summary>
        /// Answers one question, asked by the client about once a minute: is this
        /// browser still THE session for this user?
        ///
        ///     active     — yes, carry on
        ///     takenover  — someone signed in with the same login id; takenOverBy
        ///                  says who and from where, so the browser can explain
        ///                  itself before signing out
        ///     unknown    — nothing on record (API restarted, or a clean logout).
        ///                  The client MUST ignore this. Signing a user out on that
        ///                  guess would sign out everybody after a restart.
        ///
        /// [SkipTokenCheck] is deliberate and load-bearing. A taken-over browser
        /// holds a token that no longer matches UserMaster.TokenNo, so the global
        /// filter would refuse it with 400 — exactly the silent failure this
        /// endpoint exists to replace. It reads nothing a stale token could not
        /// already infer, and changes nothing.
        /// </summary>
        [SkipTokenCheck]
        [HttpGet("SessionHeartbeat")]
        public IActionResult SessionHeartbeat([FromQuery] int userNo, [FromQuery] string tokenNo = "")
        {
            try
            {
                var status = sessions.Touch(userNo, tokenNo, out var current);

                if (status == UserSessionRegistry.StatusTakenOver && current != null)
                {
                    return OkResult(new
                    {
                        status,
                        message = "You have been signed out because this login ID was used on another " +
                                  "device. Only one active session is allowed per user.",
                        takenOverBy = new
                        {
                            userName = current.UserName ?? string.Empty,
                            loginId = current.LoginId ?? string.Empty,
                            ipAddress = current.IPAddress ?? string.Empty,
                            location = current.Location ?? string.Empty,
                            loginDate = current.LoginOn.ToString("dd-MMM-yyyy"),
                            loginTime = current.LoginOn.ToString("hh:mm:ss tt")
                        }
                    }, "Session taken over.");
                }

                return OkResult(new { status, message = string.Empty, takenOverBy = (object?)null });
            }
            catch (Exception ex)
            {
                // A watchdog must never be the reason a page breaks. Report
                // unknown, which the client is required to ignore.
                return OkResult(new
                {
                    status = UserSessionRegistry.StatusUnknown,
                    message = ex.Message,
                    takenOverBy = (object?)null
                });
            }
        }

        /// <summary>
        /// Clean sign-out for the session watcher — the Logout button and the
        /// inactivity auto-logout both land here. Releases the live session,
        /// clears UserMaster.TokenNo and closes the audit row.
        ///
        /// The token must match the CURRENT session, so a logout clicked in a
        /// browser that was already taken over cannot disturb the newer one. That
        /// check is what authorises the call, which is why the endpoint can carry
        /// [SkipTokenCheck]: a stale token is answered rather than rejected, and
        /// still changes nothing.
        /// </summary>
        [SkipTokenCheck]
        [HttpPost("EndSession")]
        public async Task<IActionResult> EndSession(
            [FromQuery] int userNo,
            [FromQuery] string tokenNo = "",
            [FromQuery] string reason = "")
        {
            try
            {
                if (userNo <= 0)
                    return OkResult(new { success = false }, "userNo is required.");

                var ended = sessions.End(userNo, tokenNo);

                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == userNo);

                if (user == null)
                    return OkResult(new { success = false }, "User not found.");

                // The same proof of ownership against the database, so this still
                // works when an API restart emptied the in-memory register.
                var expected = CryptoHelper.Encrypt((user.TokenNo ?? 0).ToString());
                var mine = string.Equals(NormToken(tokenNo), expected, StringComparison.Ordinal);

                if (!mine && ended == null)
                    return OkResult(new { success = false }, "Not the current session — nothing released.");

                if (mine)
                {
                    user.TokenNo = null;
                    user.LogoutDate = DateTime.Now;
                    await db.SaveChangesAsync();

                    // MTransNo 0 closes the newest open row for this login.
                    await db.CallAccessLogSP(new SP_AccessLog
                    {
                        MTransNo = ended?.AccessLogNo ?? 0,
                        LoginId = user.UserId ?? string.Empty,
                        SubscID = user.SubscID ?? 0,
                        Status = "Logout"
                    });
                }

                return OkResult(new { success = true, reason = reason ?? string.Empty }, "Session ended.");
            }
            catch (Exception ex)
            {
                // Signing out must always succeed on the client side.
                return OkResult(new { success = false }, ex.Message);
            }
        }

        //=========================================
        // PAGE AUTHORIZATION
        //=========================================

        /// <summary>
        /// File-name-only key for a module page, so "../Masters/Page.html?x=1",
        /// "Masters\Page.html" and "page.HTML" all compare equal.
        /// </summary>
        private static string PageKey(string? page)
        {
            if (string.IsNullOrWhiteSpace(page)) return string.Empty;

            var s = page.Trim().Replace('\\', '/');

            var cut = s.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);

            var slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);

            return s.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Guard for a page being opened directly: the client sends the signed-in
        /// user and the page file name, and gets back whether that module is
        /// granted to the user's access template. It closes the gap the menu alone
        /// leaves — hiding a module from the sidebar does not stop someone typing
        /// its URL.
        ///
        /// Template resolution matches GetMenuList exactly: UserMaster.AccessTemplate,
        /// or — for a user with no template who is an ADMIN — the whole menu.
        ///
        /// Pages that are not menu modules at all (print views, child popups,
        /// anything never added to Menulist) are ALLOWED; only menu-driven modules
        /// are template controlled. Those answers carry inMenu = false, so the
        /// caller can tell "not restricted" apart from "explicitly granted".
        ///
        /// <paramref name="dept"/> narrows the search to one Menulist Category,
        /// the same value GetMenuList takes as `category`.
        /// </summary>
        [HttpGet("CheckPageAccess")]
        public async Task<IActionResult> CheckPageAccess(
            [FromQuery] int userNo = 0,
            [FromQuery] string page = "",
            [FromQuery] string dept = "")
        {
            dept ??= string.Empty;

            try
            {
                if (userNo <= 0 || string.IsNullOrWhiteSpace(page))
                    return OkResult(Denied(string.Empty, "Invalid request. User and page are required."));

                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return OkResult(Denied(string.Empty, "User not found."));

                var templateName = user.AccessTemplate ?? string.Empty;

                // 1. Is the requested page a menu module at all? Compared on the
                //    file name only, so it does not matter whether Menulist stores
                //    "Page.html" or "../Masters/Page.html", nor what the caller sends.
                var wanted = PageKey(page);

                var modules = await db.Menulist
                    .AsNoTracking()
                    .Where(m => dept == "" || m.Category == dept)
                    .Select(m => new { m.MTransNo, m.ModulePage, m.ModuleName, m.Category })
                    .ToListAsync();

                var matches = modules.Where(m => PageKey(m.ModulePage) == wanted).ToList();

                if (matches.Count == 0)
                {
                    // Not a menu module — there is nothing to authorise against.
                    return OkResult(new
                    {
                        allowed = true,
                        inMenu = false,
                        authType = "A",
                        templateName,
                        moduleName = string.Empty,
                        category = string.Empty,
                        homePage = user.HomePage ?? string.Empty,
                        message = string.Empty
                    });
                }

                // 2. A user with no template sees the whole menu only when ADMIN —
                //    the same rule GetMenuList applies.
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    if (user.UserType == "ADMIN")
                    {
                        return OkResult(new
                        {
                            allowed = true,
                            inMenu = true,
                            authType = "A",
                            templateName,
                            moduleName = matches[0].ModuleName ?? string.Empty,
                            category = matches[0].Category ?? string.Empty,
                            homePage = user.HomePage ?? string.Empty,
                            message = string.Empty
                        });
                    }

                    return OkResult(Denied(templateName, "No modules are assigned to this user.",
                        matches[0].ModuleName, matches[0].Category, user.HomePage));
                }

                // 3. Does the template grant any of the matching modules?
                var moduleNos = matches.Select(m => m.MTransNo).ToList();

                var grant = await db.AccessTemplate
                    .AsNoTracking()
                    .Where(a => a.TemplateName == templateName &&
                                a.SubscID == user.SubscID &&
                                moduleNos.Contains(a.ModuleNo))
                    .Select(a => new { a.ModuleNo, a.AuthType })
                    .FirstOrDefaultAsync();

                if (grant == null)
                {
                    return OkResult(Denied(templateName, "This page is not authorised for this user.",
                        matches[0].ModuleName, matches[0].Category, user.HomePage));
                }

                var granted = matches.First(m => m.MTransNo == grant.ModuleNo);

                return OkResult(new
                {
                    allowed = true,
                    inMenu = true,
                    authType = string.IsNullOrWhiteSpace(grant.AuthType) ? "A" : grant.AuthType,
                    templateName,
                    moduleName = granted.ModuleName ?? string.Empty,
                    category = granted.Category ?? string.Empty,
                    homePage = user.HomePage ?? string.Empty,
                    message = string.Empty
                });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>A refusal in the same shape as a grant, so one client branch reads both.</summary>
        private static object Denied(
            string templateName,
            string message,
            string? moduleName = null,
            string? category = null,
            string? homePage = null) => new
            {
                allowed = false,
                inMenu = true,
                authType = string.Empty,
                templateName,
                moduleName = moduleName ?? string.Empty,
                category = category ?? string.Empty,
                homePage = homePage ?? string.Empty,
                message
            };

        //=========================================
        // HOME INFORMATION
        //=========================================

        /// <summary>
        /// Everything the Home page needs, in one call.
        ///
        /// Takes no parameters: TokenAuthFilter has already matched the token to
        /// a UserMaster row and left the verified user number in HttpContext,
        /// so the token alone decides whose information comes back. A caller
        /// cannot ask for another user's home data by changing a query value.
        ///
        ///     GET /api/Home/GetHomeInfo
        ///     X-Token: &lt;token from /api/Home/login&gt;
        /// </summary>
        [HttpGet("GetHomeInfo")]
        public async Task<IActionResult> GetHomeInfo()
        {
            // Set by TokenAuthFilter once the token checks out.
            var userNo = HttpContext.Items["UserNo"] as int? ?? 0;

            if (userNo <= 0)
                return BadRequestResult("Invalid or expired token. Please sign in again.");

            try
            {
                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult("User not found.");

                var subscID = user.SubscID ?? 0;

                // The company the user works under — the first active one on the
                // subscription, which is what the badge on the Home page shows.
                var company = await db.Company
                    .AsNoTracking()
                    .Where(x => x.SubscID == subscID &&
                                x.DeleteStatus == "N" &&
                                x.LockStatus == "Y")
                    .OrderBy(x => x.CompName)
                    .Select(x => new
                    {
                        x.MTransNo,
                        x.CompCode,
                        x.CompName,
                        x.TradingName,
                        x.State,
                        x.GSTIN,
                        x.FYear,
                        x.YearCode,
                        x.TxnCurrency
                    })
                    .FirstOrDefaultAsync();

                // Same filters the individual GetList endpoints use, so the tiles
                // agree with the grids the user opens next.
                var branches = await db.BranchMaster.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.DeleteStatus == "N");

                var ledgers = await db.AccountsMaster.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.DeleteStatus == "N");

                var users = await db.UserMaster.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.DeleteStatus == "N");

                var banks = await db.BankMaster.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.DeleteStatus == "N");

                var companies = await db.Company.AsNoTracking()
                    .CountAsync(x => x.SubscID == subscID && x.DeleteStatus == "N");

                return OkResult(new
                {
                    User = new
                    {
                        UserNo = user.MTransNo,
                        user.UserName,
                        user.UserId,
                        user.UserType,
                        user.Designation,
                        user.Department,
                        user.EmailId,
                        user.ContactNo,
                        user.Branch,
                        user.ImageFile,
                        user.AccessTemplate,
                        user.HomePage,
                        user.LastLogin,
                        PasswordExpired = (user.PWExpiry ?? 0) == 1
                    },
                    SubscID = subscID,
                    Company = company,
                    Counts = new
                    {
                        Companies = companies,
                        Branches = branches,
                        Ledgers = ledgers,
                        Users = users,
                        Banks = banks
                    },
                    ServerTime = DateTime.Now
                }, "Home information loaded.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // PASSWORD MANAGEMENT
        //=========================================
        [HttpPost("UpdatePassword")]
        public async Task<IActionResult> UpdatePassword(
            [FromQuery] int userNo,
            [FromQuery] string oldPassword,
            [FromQuery] string newPassword)
        {
            if (userNo <= 0 ||
                string.IsNullOrWhiteSpace(oldPassword) ||
                string.IsNullOrWhiteSpace(newPassword))
                return BadRequestResult("Invalid input parameters.");

            if (oldPassword == newPassword)
                return BadRequestResult("The new password must differ from the current one.");

            if (newPassword.Length < 8)
                return BadRequestResult("The new password must be at least 8 characters long.");

            try
            {
                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == userNo);

                if (user == null)
                    return NotFoundResult("User not found.");

                if (user.PW != CryptoHelper.Encrypt(oldPassword))
                    return Unauthorized(new { success = false, message = "The current password is incorrect.", data = (object?)null, statusCode = 401 });

                user.PW = CryptoHelper.Encrypt(newPassword);
                user.PWExpiry = 0;
                user.LockCounter = 0;
                user.ModifiedBy = userNo;
                user.ModifiedOn = DateTime.Now;

                await db.SaveChangesAsync();

                return OkResult(new { UserNo = userNo }, "Password updated successfully.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>
        /// Administrator-initiated reset. adminUserNo must belong to an active
        /// ADMIN account — otherwise any caller could reset any password.
        /// </summary>
        [HttpPost("PasswordReset")]
        public async Task<IActionResult> PasswordReset(
            [FromQuery] int adminUserNo,
            [FromQuery] int userNo,
            [FromQuery] string password)
        {
            if (adminUserNo <= 0 || userNo <= 0 || string.IsNullOrWhiteSpace(password))
                return BadRequestResult("Invalid input parameters.");

            if (password.Length < 8)
                return BadRequestResult("The password must be at least 8 characters long.");

            try
            {
                var admin = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == adminUserNo &&
                                              x.DeleteStatus == "N" &&
                                              x.LockStatus == "Y" &&
                                              x.UserType == "ADMIN");

                if (admin == null)
                    return Unauthorized(new { success = false, message = "Only an active administrator can reset a password.", data = (object?)null, statusCode = 401 });

                var user = await db.UserMaster.FirstOrDefaultAsync(x => x.MTransNo == userNo);

                if (user == null)
                    return NotFoundResult("User not found.");

                user.PW = CryptoHelper.Encrypt(password);
                user.PWExpiry = 1;          // force a change at next sign-in
                user.LockCounter = 0;
                user.ModifiedBy = adminUserNo;
                user.ModifiedOn = DateTime.Now;

                await db.SaveChangesAsync();

                return OkResult(new { UserNo = userNo }, "Password reset successfully. The user must change it at next sign-in.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        //=========================================
        // NAVIGATION
        //=========================================

        /// <summary>
        /// The modules a user may open, resolved through their access template.
        /// A user with no template gets the full menu only when UserType = ADMIN;
        /// everyone else gets nothing.
        /// </summary>
        [HttpGet("GetMenuList")]
        public async Task<IActionResult> GetMenuList([FromQuery] int userNo, [FromQuery] string category = "")
        {
            if (userNo <= 0)
                return BadRequestResult("Invalid user.");

            try
            {
                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult("User not found.");

                var modules = db.Menulist.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(category))
                    modules = modules.Where(m => m.Category == category);

                if (!string.IsNullOrWhiteSpace(user.AccessTemplate))
                {
                    var data = await (from m in modules
                                      join a in db.AccessTemplate.AsNoTracking()
                                        on m.MTransNo equals a.ModuleNo
                                      where a.TemplateName == user.AccessTemplate &&
                                            a.SubscID == user.SubscID
                                      orderby m.CSLNo, m.SSLNo, m.MSLNo
                                      select new
                                      {
                                          m.MTransNo,
                                          m.Category,
                                          m.CSLNo,
                                          m.Section,
                                          m.SSLNo,
                                          m.ModuleName,
                                          m.MSLNo,
                                          m.ModuleIcon,
                                          m.ModulePage,
                                          a.AuthType,
                                          a.MyFavorite
                                      })
                                     .ToListAsync();

                    return OkResult(data);
                }

                if (user.UserType == "ADMIN")
                {
                    var data = await modules
                        .OrderBy(m => m.CSLNo).ThenBy(m => m.SSLNo).ThenBy(m => m.MSLNo)
                        .Select(m => new
                        {
                            m.MTransNo,
                            m.Category,
                            m.CSLNo,
                            m.Section,
                            m.SSLNo,
                            m.ModuleName,
                            m.MSLNo,
                            m.ModuleIcon,
                            m.ModulePage,
                            AuthType = "A",
                            MyFavorite = "N"
                        })
                        .ToListAsync();

                    return OkResult(data);
                }

                return OkResult(Array.Empty<object>(), "No modules are assigned to this user.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Distinct sections of the menu a user can see — drives the sidebar groups.</summary>
        [HttpGet("GetMenuSection")]
        public async Task<IActionResult> GetMenuSection([FromQuery] int userNo, [FromQuery] string category = "")
        {
            if (userNo <= 0)
                return BadRequestResult("Invalid user.");

            try
            {
                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null)
                    return NotFoundResult("User not found.");

                var modules = db.Menulist.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(category))
                    modules = modules.Where(m => m.Category == category);

                if (!string.IsNullOrWhiteSpace(user.AccessTemplate))
                {
                    var data = await (from m in modules
                                      join a in db.AccessTemplate.AsNoTracking()
                                        on m.MTransNo equals a.ModuleNo
                                      where a.TemplateName == user.AccessTemplate &&
                                            a.SubscID == user.SubscID
                                      select new { m.Section, m.SSLNo })
                                     .Distinct()
                                     .OrderBy(x => x.SSLNo)
                                     .ToListAsync();

                    return OkResult(data);
                }

                if (user.UserType == "ADMIN")
                {
                    var data = await modules
                        .Select(m => new { m.Section, m.SSLNo })
                        .Distinct()
                        .OrderBy(x => x.SSLNo)
                        .ToListAsync();

                    return OkResult(data);
                }

                return OkResult(Array.Empty<object>(), "No modules are assigned to this user.");
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>The user's pinned modules (MyFavorite = 'Y') for the shortcut bar.</summary>
        [HttpGet("GetFavorites")]
        public async Task<IActionResult> GetFavorites([FromQuery] int userNo)
        {
            if (userNo <= 0)
                return BadRequestResult("Invalid user.");

            try
            {
                var user = await db.UserMaster
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MTransNo == userNo && x.DeleteStatus == "N");

                if (user == null || string.IsNullOrWhiteSpace(user.AccessTemplate))
                    return OkResult(Array.Empty<object>());

                var data = await (from a in db.AccessTemplate.AsNoTracking()
                                  join m in db.Menulist.AsNoTracking()
                                    on a.ModuleNo equals m.MTransNo
                                  where a.TemplateName == user.AccessTemplate &&
                                        a.SubscID == user.SubscID &&
                                        a.MyFavorite == "Y"
                                  orderby m.CSLNo, m.SSLNo, m.MSLNo
                                  select new
                                  {
                                      m.MTransNo,
                                      m.ModuleName,
                                      m.ModuleIcon,
                                      m.ModulePage
                                  })
                                 .ToListAsync();

                return OkResult(data);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }
    }
}
