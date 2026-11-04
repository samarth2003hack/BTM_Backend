using System.Globalization;

namespace EMSAPI.Models
{
    /// <summary>
    /// Server copy of the planning rules in the front end's
    /// UI_Components/widgets/plan-schedule.js — the SAME rules, so the day
    /// split the planner sees is the one the reports, the week release and
    /// the reschedule store. Keep the two in step.
    ///
    ///   1. A month's working days = every date of the month that is not the
    ///      weekly off or a holiday (Work Calendar).
    ///   2. A hammer runs ShiftsPerDay × EfficiencyPct / 100 standard
    ///      shift-days per working day (Machine Master).
    ///   3. A job's Days Required is in standard shift-days; on its hammer it
    ///      takes Days ÷ rate working days.
    ///   4. MAIN jobs on one hammer run one after another in plan order;
    ///      different hammers run side by side from the month's first day.
    ///   5. A FIXED START holds the job until that day; the wait is idle.
    ///   6. A job's qty is spread over its days by the share of each day it
    ///      uses (largest remainder, so the days add up exactly).
    ///   7. Inside a job the lines are forged in order.
    ///   8. Days past the month's last working day are overflow (no date).
    ///   9. ALTERNATE jobs are standby and take no dates.
    ///  10. Weeks are Monday–Sunday, cut to the month, numbered W1, W2 …
    ///  12. Forge-by = delivery − post-forging days − 1 dispatch day.
    /// </summary>
    public static class PlanScheduleEngine
    {
        private const double Eps = 1e-9;
        private static readonly string[] DayNames =
            { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        public const int DispatchBufferDays = 1;

        // ── Shapes ──────────────────────────────────────────────────────

        public sealed class Hammer
        {
            public int MachineNo { get; set; }
            public double Shifts { get; set; } = 1;
            public double Efficiency { get; set; } = 100;
        }

        public sealed class Week
        {
            public int WeekNo { get; set; }
            public List<string> Dates { get; } = new();
            public string From => Dates.Count > 0 ? Dates[0] : "";
            public string To => Dates.Count > 0 ? Dates[^1] : "";
            public string Label => $"W{WeekNo} ({ShortDate(From)} – {ShortDate(To)})";
        }

        public sealed class CalendarInfo
        {
            public string CalMonth { get; set; } = "";
            public string ShiftName { get; set; } = "";
            public double NetHours { get; set; }
            public List<string> Dates { get; } = new();
            public List<Week> Weeks { get; } = new();
            public Dictionary<string, double> Rates { get; } = new();
            public bool HasMachines { get; set; }
            public int WorkingDays => Dates.Count;
            public double AvailableHours => Math.Round(Dates.Count * NetHours, 2);

            public double RateOf(int machineNo)
            {
                if (!HasMachines) return 1;
                return Rates.TryGetValue(machineNo.ToString(CultureInfo.InvariantCulture), out var r) ? r : 0;
            }

            public int WeekOfDate(string iso) => Weeks.FirstOrDefault(w => w.Dates.Contains(iso))?.WeekNo ?? 0;
        }

        public sealed class Job
        {
            public string PlanType { get; set; } = "Main";
            public int MachineNo { get; set; }
            public double DaysRequired { get; set; }
            public string FixedStart { get; set; } = "";
            public List<double> LineQtys { get; } = new();
        }

        public sealed class DayLine { public int LineIdx { get; set; } public double Qty { get; set; } }

        public sealed class JobDay
        {
            public string Date { get; set; } = "";
            public double Fraction { get; set; }
            public double Qty { get; set; }
            public List<DayLine> Lines { get; set; } = new();
        }

        public sealed class BuiltJob
        {
            public string StartDate { get; set; } = "";
            public string EndDate { get; set; } = "";
            public double? StartPos { get; set; }
            public double? EndPos { get; set; }
            public bool Overflow { get; set; }
            public bool NoCapacity { get; set; }
            public double IdleBefore { get; set; }
            public double CalDays { get; set; }
            public List<JobDay> Days { get; } = new();
        }

        public sealed class Bucket
        {
            public string Date { get; set; } = "";
            public int JobIdx { get; set; }
            public int LineIdx { get; set; }
            public double Qty { get; set; }
            public double Fraction { get; set; }
            public int Lane { get; set; }
        }

        public sealed class Lane
        {
            public double Rate { get; set; }
            public double UsedDays { get; set; }
            public double WorkDays { get; set; }
            public double IdleDays { get; set; }
            public int CapacityDays { get; set; }
            public int LoadPct { get; set; }
        }

        public sealed class Built
        {
            public List<BuiltJob> Jobs { get; } = new();
            public List<Bucket> Buckets { get; } = new();
            public Dictionary<int, Lane> Lanes { get; } = new();
            public double UsedDays { get; set; }
            public double IdleDays { get; set; }
            public int PeakLoadPct { get; set; }
        }

        // ── Calendar (rules 1, 2, 10) ───────────────────────────────────

        public static CalendarInfo CalendarInfoOf(string calMonth, string shiftName, string shiftStart, string shiftEnd,
            int breakMin, string weeklyOff, IEnumerable<string> holidays, IEnumerable<Hammer>? hammers)
        {
            var info = new CalendarInfo { CalMonth = calMonth ?? "", ShiftName = shiftName ?? "" };
            var span = MinutesOf(shiftEnd) - MinutesOf(shiftStart);
            if (span < 0) span += 24 * 60;
            var netMin = double.IsNaN(span) ? 0 : Math.Max(0, span - Math.Max(0, breakMin));
            info.NetHours = Math.Round(netMin / 60.0, 2, MidpointRounding.AwayFromZero);

            var off = Array.IndexOf(DayNames, string.IsNullOrWhiteSpace(weeklyOff) ? "Sunday" : weeklyOff);
            var hol = new HashSet<string>(holidays ?? Enumerable.Empty<string>());
            if (DateTime.TryParseExact(info.CalMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var first))
            {
                var days = DateTime.DaysInMonth(first.Year, first.Month);
                for (var d = 0; d < days; d++)
                {
                    var date = first.AddDays(d);
                    var iso = Iso(date);
                    if ((int)date.DayOfWeek == off || hol.Contains(iso)) continue;
                    info.Dates.Add(iso);
                }
            }

            foreach (var h in hammers ?? Enumerable.Empty<Hammer>())
            {
                if (h.MachineNo <= 0) continue;
                info.HasMachines = true;
                var shifts = Math.Max(0, h.Shifts);
                var eff = h.Efficiency > 0 ? h.Efficiency : 100;
                info.Rates[h.MachineNo.ToString(CultureInfo.InvariantCulture)] =
                    Math.Round(shifts * eff / 100.0, 4, MidpointRounding.AwayFromZero);
            }

            var byMonday = new Dictionary<string, Week>();
            foreach (var iso in info.Dates)
            {
                var d = ParseIso(iso);
                var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
                var key = Iso(monday);
                if (!byMonday.TryGetValue(key, out var w))
                {
                    w = new Week { WeekNo = info.Weeks.Count + 1 };
                    byMonday[key] = w;
                    info.Weeks.Add(w);
                }
                w.Dates.Add(iso);
            }
            return info;
        }

        // ── Splitting qty (rule 6) ──────────────────────────────────────

        public static List<double> SplitQty(double total, IList<double> weights)
        {
            var t = Math.Round(total, MidpointRounding.AwayFromZero);
            var outList = new List<double>();
            if (weights.Count == 0) return outList;
            var sum = weights.Sum(w => w);
            if (sum <= 0)
            {
                outList.AddRange(weights.Select(_ => 0d));
                outList[0] = t;
                return outList;
            }
            var exact = weights.Select(w => t * w / sum).ToList();
            outList.AddRange(exact.Select(x => Math.Floor(x + Eps)));
            var left = (int)(t - outList.Sum());
            var order = exact.Select((x, i) => new { i, r = x - Math.Floor(x + Eps) })
                             .OrderByDescending(o => o.r).ThenBy(o => o.i).Take(Math.Max(0, left));
            foreach (var o in order) outList[o.i]++;
            return outList;
        }

        // Rule 7: fill a day's pieces from the lines in order.
        private static List<DayLine> FillLines(double dayQty, double[] remaining)
        {
            var outList = new List<DayLine>();
            for (var i = 0; i < remaining.Length && dayQty > 0; i++)
            {
                if (remaining[i] <= 0) continue;
                var take = Math.Min(remaining[i], dayQty);
                remaining[i] -= take;
                dayQty -= take;
                outList.Add(new DayLine { LineIdx = i, Qty = take });
            }
            return outList;
        }

        // ── Build (rules 3–9) ───────────────────────────────────────────

        private sealed class LaneState { public double Rate; public double Cursor; public double WorkDays; public double IdleDays; }

        public static Built Build(IList<Job> jobs, CalendarInfo? info)
        {
            var built = new Built();
            var dates = info?.Dates ?? new List<string>();
            var lanes = new Dictionary<int, LaneState>();

            for (var jobIdx = 0; jobIdx < jobs.Count; jobIdx++)
            {
                var job = jobs[jobIdx];
                var res = new BuiltJob();
                built.Jobs.Add(res);
                if (job.PlanType == "Alternate" || dates.Count == 0) continue;

                if (!lanes.TryGetValue(job.MachineNo, out var lane))
                {
                    lane = new LaneState { Rate = info!.RateOf(job.MachineNo) };
                    lanes[job.MachineNo] = lane;
                }
                var lineQtys = job.LineQtys.ToArray();
                var totalQty = lineQtys.Sum();
                if (!(lane.Rate > 0))
                {
                    res.NoCapacity = true;
                    res.Overflow = true;
                    res.Days.Add(new JobDay { Date = "", Fraction = 0, Qty = totalQty,
                        Lines = FillLines(totalQty, (double[])lineQtys.Clone()) });
                    continue;
                }

                var calDays = Math.Max(0, job.DaysRequired) / lane.Rate;
                res.CalDays = Round(calDays, 4);
                if (!string.IsNullOrEmpty(job.FixedStart))
                {
                    var idx = dates.FindIndex(d => string.CompareOrdinal(d, job.FixedStart) >= 0);
                    var pos = idx < 0 ? dates.Count : idx;
                    if (pos > lane.Cursor + Eps)
                    {
                        res.IdleBefore = Round(pos - lane.Cursor, 2);
                        lane.IdleDays += pos - lane.Cursor;
                        lane.Cursor = pos;
                    }
                }
                var start = lane.Cursor;
                var end = lane.Cursor + calDays;
                lane.Cursor = end;
                lane.WorkDays += calDays;
                res.StartPos = Round(start, 4);
                res.EndPos = Round(end, 4);

                var slots = new List<(int Pos, double Fraction)>();
                var firstPos = (int)Math.Floor(start + Eps);
                var lastPos = Math.Max(firstPos, (int)Math.Ceiling(end - Eps) - 1);
                for (var d = firstPos; d <= lastPos; d++)
                {
                    var fraction = Math.Min(end, d + 1) - Math.Max(start, d);
                    if (fraction <= Eps && calDays > 0) continue;
                    slots.Add((d, calDays > 0 ? fraction : 0));
                }
                if (slots.Count == 0) slots.Add((firstPos, 0));

                var qtys = SplitQty(totalQty, slots.Select(s => s.Fraction > 0 ? s.Fraction : (calDays > 0 ? 0 : 1)).ToList());
                var remaining = (double[])lineQtys.Clone();
                for (var k = 0; k < slots.Count; k++)
                {
                    var s = slots[k];
                    var date = s.Pos < dates.Count ? dates[s.Pos] : "";
                    if (date == "") res.Overflow = true;
                    var lines = FillLines(qtys[k], remaining);
                    res.Days.Add(new JobDay { Date = date, Fraction = Round(s.Fraction, 4), Qty = qtys[k], Lines = lines });
                    foreach (var l in lines)
                        built.Buckets.Add(new Bucket { Date = date, JobIdx = jobIdx, LineIdx = l.LineIdx, Qty = l.Qty,
                            Fraction = Round(s.Fraction, 4), Lane = job.MachineNo });
                }
                var dated = res.Days.Where(x => x.Date != "").ToList();
                res.StartDate = dated.Count > 0 ? dated[0].Date : "";
                res.EndDate = dated.Count > 0 ? dated[^1].Date : "";
            }

            var cap = dates.Count;
            double used = 0, idle = 0;
            var peak = 0;
            foreach (var kv in lanes)
            {
                var l = kv.Value;
                var usedDays = Round(l.Cursor, 2);
                var loadPct = cap > 0 ? (int)Math.Round(usedDays / cap * 100, MidpointRounding.AwayFromZero) : 0;
                built.Lanes[kv.Key] = new Lane { Rate = l.Rate, UsedDays = usedDays, WorkDays = Round(l.WorkDays, 2),
                    IdleDays = Round(l.IdleDays, 2), CapacityDays = cap, LoadPct = loadPct };
                used = Math.Max(used, usedDays);
                idle += l.IdleDays;
                peak = Math.Max(peak, loadPct);
            }
            built.UsedDays = Round(used, 2);
            built.IdleDays = Round(idle, 2);
            built.PeakLoadPct = peak;
            return built;
        }

        // ── Forge-by (rule 12) ──────────────────────────────────────────

        public static string ForgeBy(string? deliveryDate, int postForgeDays)
        {
            if (string.IsNullOrEmpty(deliveryDate)) return "";
            var d = ParseIso(deliveryDate);
            return Iso(d.AddDays(-Math.Max(0, postForgeDays) - DispatchBufferDays));
        }

        // ── Helpers ─────────────────────────────────────────────────────

        public static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static DateTime ParseIso(string iso)
        {
            var s = (iso ?? "").Split('T')[0];
            return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d : DateTime.MinValue;
        }

        public static string ShortDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            var d = ParseIso(iso);
            return d.ToString("dd MMM", CultureInfo.InvariantCulture);
        }

        private static double MinutesOf(string? hhmm)
        {
            var p = (hhmm ?? "").Split(':');
            if (p.Length < 2 || !int.TryParse(p[0], out var h) || !int.TryParse(p[1], out var m)) return double.NaN;
            return h * 60 + m;
        }

        private static double Round(double v, int dp) => Math.Round(v, dp, MidpointRounding.AwayFromZero);
    }
}
