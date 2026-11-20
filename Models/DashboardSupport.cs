using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;

namespace EMSAPI.Models
{
    /// <summary>
    /// Short server-side cache for the dashboard endpoints (Sales, Purchase,
    /// Production, Maintenance, Inventory, Quality).
    ///
    /// A dashboard runs many aggregate queries. The first person to open it
    /// pays for them; anyone who opens the same dashboard (same company, same
    /// filters) in the next few minutes gets the stored answer at once.
    ///   • Key  = area + SubscID + the request's filters + today's date, so a
    ///            filter change or a new day never reuses an old answer.
    ///   • TTL  = "Dashboards:CacheMinutes" in appsettings (default 3; 0 turns
    ///            the cache off).
    ///   • Refresh=true on a request skips the cache and rebuilds it.
    ///   • Only one build per key runs at a time: when ten users open the same
    ///     dashboard together, one runs the queries and the rest wait for it.
    /// Each build is timed per block and logged, so a slow block shows up in
    /// the server log with real numbers ("Dashboard Inventory/summary built in
    /// 840 ms: stock 610, dies 40, ...").
    /// </summary>
    public sealed class DashboardCache
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
        private readonly IMemoryCache cache;
        private readonly ILogger logger;
        private readonly TimeSpan ttl;

        public DashboardCache(IMemoryCache cache, IConfiguration config, ILogger<DashboardCache> logger)
        {
            this.cache = cache;
            this.logger = logger;
            var minutes = config.GetValue<double?>("Dashboards:CacheMinutes") ?? 3;
            ttl = TimeSpan.FromMinutes(Math.Max(0, minutes));
        }

        private sealed record Entry(Dictionary<string, object?> Data, DateTime GeneratedAt, long BuildMs);

        /// <summary>
        /// The dashboard data for <paramref name="key"/>: from the cache when it
        /// is fresh (and refresh is false), else built now and stored. A "meta"
        /// block is added: { generatedAt, fromCache, buildMs, cacheMinutes }.
        /// </summary>
        public async Task<Dictionary<string, object?>> GetOrBuild(
            string area, string key, bool refresh, Func<DashTimer, Task<Dictionary<string, object?>>> build)
        {
            var fullKey = "dash|" + area + "|" + key + "|" + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!refresh && ttl > TimeSpan.Zero && cache.TryGetValue(fullKey, out Entry? hit) && hit != null)
                return WithMeta(hit, true);

            var gate = Locks.GetOrAdd(fullKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                // Someone else may have built it while this request waited.
                if (!refresh && ttl > TimeSpan.Zero && cache.TryGetValue(fullKey, out Entry? again) && again != null)
                    return WithMeta(again, true);

                var timer = new DashTimer();
                var data = await build(timer);
                var entry = new Entry(data, DateTime.Now, timer.TotalMs);
                if (ttl > TimeSpan.Zero) cache.Set(fullKey, entry, ttl);

                var level = timer.TotalMs > 3000 ? LogLevel.Warning : LogLevel.Information;
                logger.Log(level, "Dashboard {Area} built in {Ms} ms: {Blocks}", area, timer.TotalMs, timer.Describe());
                return WithMeta(entry, false);
            }
            finally
            {
                gate.Release();
            }
        }

        private Dictionary<string, object?> WithMeta(Entry e, bool fromCache)
        {
            var copy = new Dictionary<string, object?>(e.Data)
            {
                ["meta"] = new
                {
                    generatedAt = e.GeneratedAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                    fromCache,
                    buildMs = e.BuildMs,
                    cacheMinutes = ttl.TotalMinutes
                }
            };
            return copy;
        }

        /// <summary>An object's public properties as a dictionary (for builders that return anonymous objects).</summary>
        public static Dictionary<string, object?> FromObject(object o) =>
            o.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(o));

        /// <summary>The cache key part for a set of filters: "from|to|machine".</summary>
        public static string KeyOf(params object?[] parts) =>
            string.Join("|", parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture) ?? ""));
    }

    /// <summary>Times each block of one dashboard build.</summary>
    public sealed class DashTimer
    {
        private readonly Stopwatch total = Stopwatch.StartNew();
        private readonly List<(string Name, long Ms)> blocks = new();

        public long TotalMs => total.ElapsedMilliseconds;

        public async Task<T> Time<T>(string name, Func<Task<T>> work)
        {
            var sw = Stopwatch.StartNew();
            try { return await work(); }
            finally { blocks.Add((name, sw.ElapsedMilliseconds)); }
        }

        public T Time<T>(string name, Func<T> work)
        {
            var sw = Stopwatch.StartNew();
            try { return work(); }
            finally { blocks.Add((name, sw.ElapsedMilliseconds)); }
        }

        public string Describe() => string.Join(", ", blocks.Select(b => b.Name + " " + b.Ms));
    }

    /// <summary>
    /// Shared helpers of the module dashboards: reads, number formats, date
    /// ranges, trend buckets and the size limits every dashboard keeps — a
    /// list carries at most ListLimit rows plus its full count in listTotals,
    /// a chart its ChartLimit biggest rows plus ONE folded "Others (n)" row.
    /// </summary>
    public static class Dash
    {
        public const int ListLimit = 50;
        public const int ChartLimit = 10;
        public const int MaxRangeDays = 731;

        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

        // ── reads ───────────────────────────────────────────────────────

        /// <summary>Runs one read-only query; rows as column-name → value maps, strings trimmed.</summary>
        public static async Task<List<Dictionary<string, object?>>> Rows(DbConnection con, string sql, params SqlParameter[] parameters)
        {
            if (con.State != ConnectionState.Open) await con.OpenAsync();
            await using var cmd = con.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddRange(parameters);

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value is string s ? s.Trim() : value;
                }
                rows.Add(row);
            }
            return rows;
        }

        public static SqlParameter PInt(string name, int value) => new(name, SqlDbType.Int) { Value = value };
        public static SqlParameter PDate(string name, DateTime value) => new(name, SqlDbType.Date) { Value = value.Date };

        public static double Dbl(Dictionary<string, object?>? r, string k) =>
            r != null && r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToDouble(v, Inv) : 0;
        public static int Int(Dictionary<string, object?>? r, string k) =>
            r != null && r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToInt32(v, Inv) : 0;
        public static string Str(Dictionary<string, object?>? r, string k) =>
            r != null && r.TryGetValue(k, out var v) && v != null && v != DBNull.Value ? Convert.ToString(v, Inv)!.Trim() : "";
        public static DateTime? Date(Dictionary<string, object?>? r, string k) =>
            r != null && r.TryGetValue(k, out var v) && v is DateTime d ? d : null;

        public static double R1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
        public static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        public static double Pct(double part, double whole) => whole > 0 ? R1(part / whole * 100) : 0;

        // ── date range ──────────────────────────────────────────────────

        /// <summary>
        /// FromDate / ToDate ("yyyy-MM-dd"; default the 1st of this month to
        /// today). Returns an error message, or null when the range is good.
        /// </summary>
        public static string? Range(string? fromDate, string? toDate, out DateTime from, out DateTime to)
        {
            var today = DateTime.Today;
            from = ParseDate(fromDate) ?? new DateTime(today.Year, today.Month, 1);
            to = ParseDate(toDate) ?? today;
            if (from > to) return "From Date cannot be after To Date.";
            if ((to - from).TotalDays > MaxRangeDays) return "The period can be at most two years.";
            return null;
        }

        public static DateTime? ParseDate(string? s) =>
            DateTime.TryParseExact((s ?? "").Split('T')[0].Trim(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d) ? d : null;

        // ── trend buckets ───────────────────────────────────────────────

        /// <summary>Day up to 35 days, week up to 130, otherwise month — as the pages expect.</summary>
        public static string BucketOf(DateTime from, DateTime to)
        {
            var span = (to - from).Days + 1;
            return span <= 35 ? "day" : span <= 130 ? "week" : "month";
        }

        /// <summary>The bucket a date falls in: (sort key, label).</summary>
        public static (string Key, string Label) BucketKey(string bucket, DateTime d)
        {
            if (bucket == "day") return (Iso(d), d.ToString("dd MMM", Inv));
            if (bucket == "week")
            {
                var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
                return (Iso(monday), monday.ToString("dd MMM", Inv));
            }
            return (d.ToString("yyyy-MM", Inv), d.ToString("MMM yy", Inv));
        }

        /// <summary>
        /// Every bucket from <paramref name="from"/> to <paramref name="to"/>
        /// (capped at today), in order — so a quiet day still shows as a zero.
        /// Sundays are left out of a day trend unless something happened on one.
        /// </summary>
        public static List<(string Key, string Label, DateTime Date)> Buckets(string bucket, DateTime from, DateTime to, ISet<string>? busyDays = null)
        {
            var end = to > DateTime.Today ? DateTime.Today : to;
            var list = new List<(string, string, DateTime)>();
            var seen = new HashSet<string>();
            for (var d = from; d <= end; d = d.AddDays(1))
            {
                if (bucket == "day" && d.DayOfWeek == DayOfWeek.Sunday && (busyDays == null || !busyDays.Contains(Iso(d)))) continue;
                var (k, l) = BucketKey(bucket, d);
                if (seen.Add(k)) list.Add((k, l, d));
            }
            return list;
        }

        // ── size limits ─────────────────────────────────────────────────

        /// <summary>The first ListLimit rows; the full count goes in listTotals[key].</summary>
        public static List<T> Limit<T>(Dictionary<string, int> totals, string key, IEnumerable<T> rows)
        {
            var all = rows.ToList();
            totals[key] = all.Count;
            return all.Take(ListLimit).ToList();
        }

        /// <summary>
        /// The <paramref name="limit"/> biggest rows by value, plus ONE row
        /// built by <paramref name="other"/> from the rest (marked isOther), so
        /// the chart stays readable and its total still adds up.
        /// </summary>
        public static List<Dictionary<string, object?>> TopWithOthers(
            IEnumerable<Dictionary<string, object?>> rows, Func<Dictionary<string, object?>, double> value,
            Func<List<Dictionary<string, object?>>, Dictionary<string, object?>> other, int limit = ChartLimit)
        {
            var sorted = rows.OrderByDescending(value).ToList();
            if (sorted.Count <= limit) return sorted;
            var rest = sorted.Skip(limit).ToList();
            var o = other(rest);
            o["isOther"] = true;
            return sorted.Take(limit).Append(o).ToList();
        }

        public static double SumOf(IEnumerable<Dictionary<string, object?>> rows, string key) => rows.Sum(r => Dbl(r, key));

        // ── shared SQL ──────────────────────────────────────────────────

        /// <summary>
        /// Pieces waiting at each operation of the job cards running on the floor
        /// (VW_JobCardSummary.JobStatus 'In Progress', the same cards the WIP
        /// count shows): VW_JobCardStep.AvailableQty — what the previous step
        /// has passed on and this step has not processed yet. @SubscID,
        /// @MachineNo (0 = all; a job card's planned hammer).
        /// </summary>
        public const string WipByOperationSql = @"
            SELECT TOP (10) ISNULL(NULLIF(st.OperationName, ''), 'Step ' + CAST(st.SlNo AS varchar(5))) AS operation,
                   COUNT(DISTINCT st.JobCardTransNo) AS jobCards, SUM(st.AvailableQty) AS qty
              FROM dbo.VW_JobCardStep st
              JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = st.JobCardTransNo
              JOIN dbo.JobCardMst j ON j.MTransNo = st.JobCardTransNo AND j.DeleteStatus = 'N'
             WHERE st.SubscID = @SubscID AND s.JobStatus = 'In Progress' AND ISNULL(st.AvailableQty, 0) > 0
               AND (@MachineNo = 0 OR j.MachineNo = @MachineNo)
             GROUP BY ISNULL(NULLIF(st.OperationName, ''), 'Step ' + CAST(st.SlNo AS varchar(5)))
             ORDER BY SUM(st.AvailableQty) DESC";

        /// <summary>
        /// Pieces forged per job card entry: JobCardEntry.Qty at the job card
        /// route's Forging step (the last step when the route has none) —
        /// PlanningControllerBase's rule. Use as a FROM … WHERE fragment with
        /// aliases e (entry) and j (job card); add the date filter on e.EntryDate.
        /// </summary>
        public const string ForgedEntriesFrom = @"
              FROM dbo.JobCardEntry e
              JOIN dbo.JobCardMst j ON j.MTransNo = e.MTransNo AND j.DeleteStatus = 'N'
              OUTER APPLY (SELECT ISNULL(MIN(CASE WHEN o.MasterName LIKE '%forg%' THEN rd.SlNo END), MAX(rd.SlNo)) AS ForgeSlNo
                             FROM dbo.RoutingChartDtl rd
                             LEFT JOIN dbo.OtherMaster o ON o.MTransNo = rd.Operation
                            WHERE rd.MTransNo = j.RouteNo AND ISNULL(rd.DeleteStatus, 'N') = 'N') f
             WHERE e.DeleteStatus = 'N' AND j.SubscID = @SubscID AND e.StepSlNo = f.ForgeSlNo";
    }
}
