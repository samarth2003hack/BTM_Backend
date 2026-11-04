using System.Collections.Concurrent;

namespace EMSAPI.Models
{
    /// <summary>
    /// One live session per user, held in memory and keyed on UserMaster.MTransNo.
    ///
    /// The database already enforces a single session durably — signing in rotates
    /// UserMaster.TokenNo, so every earlier token stops authenticating. What that
    /// alone cannot do is TELL the older browser what happened: its next call just
    /// fails. This register carries the explanation, so the browser that lost the
    /// session can say who took it over instead of dying on a 400.
    ///
    ///     login            -> Start() replaces whatever was there
    ///     SessionHeartbeat -> Touch() keeps it alive, or reports the takeover
    ///     logout/EndSession-> End() releases it
    ///
    /// Deliberately in-memory and single-host: a restart forgets every session,
    /// which fails OPEN. Nobody is locked out and no stale entry can block a
    /// sign-in, because the authority on "is this token still valid" was never
    /// this class — it is UserMaster.TokenNo.
    /// </summary>
    public class ActiveUserSession
    {
        public int UserNo { get; set; }
        public string? LoginId { get; set; }
        public string? UserName { get; set; }

        /// <summary>
        /// The ENCRYPTED token handed to that browser — the same string the client
        /// keeps in localStorage — so a heartbeat can prove which session it is.
        /// </summary>
        public string? TokenNo { get; set; }

        public string? IPAddress { get; set; }
        public string? LatLong { get; set; }
        public string? Location { get; set; }
        public string? LoginType { get; set; }

        /// <summary>AccessLog row opened for this login, so logout stamps the right one.</summary>
        public int AccessLogNo { get; set; }

        public DateTime LoginOn { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class UserSessionRegistry
    {
        /// <summary>
        /// A session counts as dead once its browser has been silent this long.
        /// The client beats every 60s, so five minutes absorbs a slow network or a
        /// suspended laptop without stranding a dead session for ever.
        /// </summary>
        public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

        public const string StatusActive = "active";
        public const string StatusTakenOver = "takenover";
        public const string StatusUnknown = "unknown";

        private readonly ConcurrentDictionary<int, ActiveUserSession> sessions = new();

        // Base64 tokens travel in query strings, where an unencoded '+' arrives as
        // a space. Normalising both sides keeps that from reading as a mismatch.
        private static string Norm(string? token) =>
            (token ?? string.Empty).Trim().Replace(' ', '+');

        /// <summary>The entry as stored, however long ago it was last seen.</summary>
        public ActiveUserSession? Get(int userNo) =>
            sessions.TryGetValue(userNo, out var s) ? s : null;

        /// <summary>
        /// The entry only while it is still alive. An idle one is dropped and null
        /// returned, so a stale session never blocks a fresh sign-in.
        /// </summary>
        public ActiveUserSession? GetActive(int userNo)
        {
            if (!sessions.TryGetValue(userNo, out var s)) return null;

            if (DateTime.Now - s.LastSeen > IdleTimeout)
            {
                sessions.TryRemove(userNo, out _);
                return null;
            }

            return s;
        }

        /// <summary>
        /// Registers a sign-in. Any session this user already had is REPLACED —
        /// that replacement is what the older browser later reads as a takeover.
        /// </summary>
        public void Start(ActiveUserSession session)
        {
            if (session == null || session.UserNo <= 0) return;

            session.TokenNo = Norm(session.TokenNo);
            if (session.LoginOn == default) session.LoginOn = DateTime.Now;
            session.LastSeen = DateTime.Now;

            sessions[session.UserNo] = session;
        }

        /// <summary>
        /// Heartbeat. Returns <c>active</c> when this IS the live session (and
        /// refreshes it), <c>takenover</c> when someone else signed in with the same
        /// login — <paramref name="current"/> then describes them — or
        /// <c>unknown</c> when nothing is on record.
        ///
        /// A client must do NOTHING on <c>unknown</c>. An API restart empties the
        /// register, and signing a user out on that guess would log out everybody.
        /// </summary>
        public string Touch(int userNo, string? tokenNo, out ActiveUserSession? current)
        {
            current = null;

            if (userNo <= 0) return StatusUnknown;
            if (!sessions.TryGetValue(userNo, out var s)) return StatusUnknown;

            current = s;

            // A matching token revives its own entry even past the idle timeout —
            // the browser is demonstrably still there.
            if (string.Equals(Norm(s.TokenNo), Norm(tokenNo), StringComparison.Ordinal))
            {
                s.LastSeen = DateTime.Now;
                return StatusActive;
            }

            return StatusTakenOver;
        }

        /// <summary>
        /// Fills in what only the browser knows — geolocation — plus the audit row id.
        /// Only the session itself may update its own details.
        /// </summary>
        public void Update(int userNo, string? tokenNo, string? latLong, string? location, int accessLogNo)
        {
            if (!sessions.TryGetValue(userNo, out var s)) return;

            if (!string.IsNullOrWhiteSpace(tokenNo) &&
                !string.Equals(Norm(s.TokenNo), Norm(tokenNo), StringComparison.Ordinal))
                return;

            if (!string.IsNullOrWhiteSpace(latLong)) s.LatLong = latLong;
            if (!string.IsNullOrWhiteSpace(location)) s.Location = location;
            if (accessLogNo > 0) s.AccessLogNo = accessLogNo;
        }

        /// <summary>
        /// Clean logout. Only the session holding the current token may end it, so
        /// a logout clicked in a browser that was ALREADY taken over cannot kill the
        /// newer session. Returns the removed session, or null when nothing matched.
        /// </summary>
        public ActiveUserSession? End(int userNo, string? tokenNo)
        {
            if (!sessions.TryGetValue(userNo, out var s)) return null;
            if (!string.Equals(Norm(s.TokenNo), Norm(tokenNo), StringComparison.Ordinal)) return null;

            sessions.TryRemove(userNo, out _);
            return s;
        }
    }
}
