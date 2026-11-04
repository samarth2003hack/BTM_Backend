using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EMSAPI.Models
{
    /// <summary>
    /// Fixed lists and date helpers of the maintenance module.
    /// </summary>
    public static class MaintRules
    {
        public static readonly string[] Frequencies = { "Daily", "Weekly", "Monthly", "Half-Yearly", "Yearly" };
        private static readonly int[] FrequencyMonths = { 0, 0, 1, 6, 12 };
        public static readonly string[] Categories = { "Mechanical", "Electrical", "General" };
        public static readonly string[] EntryStatuses = { "OK", "NOK", "NA" };
        public static readonly string[] BreakdownTypes = { "Mechanical", "Electrical", "Hydraulic", "Pneumatic" };
        public static readonly string[] BreakdownStatuses = { "Open", "In Progress", "Closed" };
        public static readonly string[] StaffRoles = { "Maintenance Supervisor", "Production Supervisor", "Fitter", "Electrician" };
        public const string OperatorType = "Operator Name";
        public const string OperationType = "Operation";
        public static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        // Available machine hours for MTBF: three production shifts of 8 hours.
        public const int ShiftsPerDay = 3;
        public const int ShiftHours = 8;

        public static int FreqMonths(string f) { var i = Array.IndexOf(Frequencies, f); return i < 0 ? 0 : FrequencyMonths[i]; }
        public static int FreqIndex(string f) { var i = Array.IndexOf(Frequencies, f); return i < 0 ? 99 : i; }

        /// <summary>Server local today (the server runs in IST).</summary>
        public static DateTime Today => DateTime.Today;

        /// <summary>Server local now, to the minute.</summary>
        public static DateTime Now
        {
            get { var n = DateTime.Now; return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 0); }
        }

        public static string Ymd(DateTime? d) => d == null ? "" : d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static string Dt(DateTime? d) => d == null ? "" : d.Value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        public static string Ddmmyyyy(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

        /// <summary>Financial-year code: 2026-10-14 → "2627" (April starts the year).</summary>
        public static string FyCode(DateTime d)
        {
            var s = d.Month >= 4 ? d.Year : d.Year - 1;
            return (s % 100).ToString("00") + ((s + 1) % 100).ToString("00");
        }

        public static DateTime MonthStart(int y, int m) => new(y, m, 1);
        public static DateTime MonthEnd(int y, int m) => new(y, m, DateTime.DaysInMonth(y, m));
        public static string PeriodLabel(int y, int m) => MonthNames[m - 1] + " " + y;
        public static string PeriodKey(int y, int m) => y + "-" + m.ToString("00");

        // W1 = days 1–7 … W4 = 22–28, W5 = 29–month end (only when the month has one).
        public static int WeeksIn(int dim) => dim > 28 ? 5 : 4;
        public static int WeekEnd(int w, int dim) => w == 5 ? dim : Math.Min(w * 7, dim);

        public static double Hours(DateTime a, DateTime b) =>
            Math.Max(0, Math.Round((b - a).TotalHours, 2, MidpointRounding.AwayFromZero));

        public static double R1(double v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
        public static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        private static readonly Regex DateRx = new(@"^(\d{4})-(\d{2})-(\d{2})$");
        private static readonly Regex DateTimeRx = new(@"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})");

        /// <summary>"YYYY-MM-DD" → date.</summary>
        public static bool TryDate(string? s, out DateTime d)
        {
            d = default;
            var m = DateRx.Match((s ?? "").Trim());
            return m.Success && DateTime.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        /// <summary>"YYYY-MM-DDTHH:MM…" → local date-time to the minute.</summary>
        public static bool TryDateTime(string? s, out DateTime d)
        {
            d = default;
            var m = DateTimeRx.Match((s ?? "").Trim());
            if (!m.Success) return false;
            try
            {
                d = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                                 int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        public static string Text(string? s) => (s ?? "").Trim();
    }

    /// <summary>
    /// One subscription's maintenance data, loaded in a single round trip, with
    /// every derived figure the pages show: due points (with half-yearly /
    /// yearly points only when due), compliance, downtime, MTTR / MTBF, the
    /// maintenance report and the machine history card. Ported rule-for-rule
    /// from the maintenance section of Config/js/production-mock-store.js.
    /// </summary>
    public class MaintenanceData
    {
        public int SubscID { get; private set; }
        public List<MntMachine> Machines { get; } = new();
        public List<MntChecklist> Checklists { get; } = new();
        public List<(int MachineNo, int PointNo, DateTime DoneOn)> OpeningDone { get; } = new();
        public List<MntSheet> Sheets { get; } = new();
        public List<MntBreakdown> Breakdowns { get; } = new();
        public List<MntPerson> People { get; } = new();

        private static readonly Dictionary<string, int> HistoryKindOrder = new()
        {
            ["breakdown"] = 0, ["nok"] = 1, ["periodic"] = 2, ["sheet"] = 3
        };

        // ================================================================
        // LOAD
        // ================================================================

        public static async Task<MaintenanceData> LoadAsync(EMSContext db, int subscID)
        {
            var data = new MaintenanceData { SubscID = subscID };
            var con = db.Database.GetDbConnection();
            if (con.State != ConnectionState.Open) await con.OpenAsync();

            await using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT MTransNo, MachineCode, MachineName, ISNULL(LockStatus, 'Y'), ISNULL(DeleteStatus, 'N'),
                       CreatedOn, DateofInst, ISNULL(Operation, ''), ISNULL(Remarks, '')
                  FROM dbo.MachineMst WHERE SubscID = @S;

                SELECT MTransNo, ChecklistName, DocNo, RevNo, RevDate, ISNULL(Remarks, ''), LockStatus, CreatedOn
                  FROM dbo.PMChecklistMst WHERE SubscID = @S AND DeleteStatus = 'N' ORDER BY MTransNo;

                SELECT cm.ChecklistNo, cm.MachineNo
                  FROM dbo.PMChecklistMachine cm
                  JOIN dbo.PMChecklistMst c ON c.MTransNo = cm.ChecklistNo
                 WHERE c.SubscID = @S AND c.DeleteStatus = 'N' ORDER BY cm.TransSlNo;

                SELECT p.TransSlNo, p.ChecklistNo, p.Frequency, p.Category, p.SlNo, p.[CheckPoint]
                  FROM dbo.PMChecklistPoint p
                  JOIN dbo.PMChecklistMst c ON c.MTransNo = p.ChecklistNo
                 WHERE c.SubscID = @S AND c.DeleteStatus = 'N' AND p.DeleteStatus = 'N';

                SELECT MachineNo, PointNo, DoneOn FROM dbo.PMOpeningDone WHERE SubscID = @S AND DeleteStatus = 'N';

                SELECT MTransNo, SheetNo, MachineNo, ChecklistNo, ISNULL(DocNo, ''), ISNULL(RevNo, ''), RevDate,
                       [Year], [Month], ISNULL(Remarks, ''), DeptSupBy, DeptSupOn, MntSupBy, MntSupOn, CreatedOn
                  FROM dbo.PMSheetMst WHERE SubscID = @S AND DeleteStatus = 'N' ORDER BY MTransNo;

                SELECT sp.MTransNo, sp.PointNo, sp.SlNo, sp.[CheckPoint], sp.Frequency, sp.Category, ISNULL(sp.Remarks, '')
                  FROM dbo.PMSheetPoint sp
                  JOIN dbo.PMSheetMst s ON s.MTransNo = sp.MTransNo
                 WHERE s.SubscID = @S AND s.DeleteStatus = 'N' ORDER BY sp.TransSlNo;

                SELECT e.MTransNo, e.PointNo, e.Slot, e.EntryStatus, e.DoneOn, e.DoneBy
                  FROM dbo.PMSheetEntry e
                  JOIN dbo.PMSheetMst s ON s.MTransNo = e.MTransNo
                 WHERE s.SubscID = @S AND s.DeleteStatus = 'N' ORDER BY e.TransSlNo;

                SELECT d.MTransNo, d.DayNo, d.IsIdle, d.OperatorNo
                  FROM dbo.PMSheetDay d
                  JOIN dbo.PMSheetMst s ON s.MTransNo = d.MTransNo
                 WHERE s.SubscID = @S AND s.DeleteStatus = 'N' ORDER BY d.DayNo;

                SELECT MTransNo, BreakdownNo, MachineNo, BreakdownOn, BreakdownType, ReportedBy, Problem, AttendedBy,
                       RepairStart, RepairEnd, ISNULL(RootCause, ''), ISNULL(ActionTaken, ''), ISNULL(SparesUsed, ''),
                       ISNULL(Remarks, ''), PMSheetNo, BreakdownStatus, CreatedOn
                  FROM dbo.BreakdownMst WHERE SubscID = @S AND DeleteStatus = 'N';

                SELECT MTransNo, ISNULL(MasterName, ''), MasterType, ISNULL(LockStatus, 'Y'), ISNULL(DeleteStatus, 'N')
                  FROM dbo.OtherMaster
                 WHERE SubscID = @S
                   AND MasterType IN ('Maintenance Supervisor', 'Production Supervisor', 'Fitter', 'Electrician',
                                      'Operator Name', 'Operation');";
            var p = cmd.CreateParameter();
            p.ParameterName = "@S";
            p.DbType = DbType.Int32;
            p.Value = subscID;
            cmd.Parameters.Add(p);

            await using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
                data.Machines.Add(new MntMachine
                {
                    No = r.GetInt32(0), Code = Str(r, 1), Name = Str(r, 2), Lock = Str(r, 3),
                    Deleted = Str(r, 4) == "Y", CreatedOn = Date(r, 5), InstalledOn = Date(r, 6),
                    Operation = Str(r, 7), Remarks = Str(r, 8)
                });

            await r.NextResultAsync();
            var clById = new Dictionary<int, MntChecklist>();
            while (await r.ReadAsync())
            {
                var c = new MntChecklist
                {
                    No = r.GetInt32(0), Name = Str(r, 1), DocNo = Str(r, 2), RevNo = Str(r, 3), RevDate = Date(r, 4),
                    Remarks = Str(r, 5), Lock = Str(r, 6), CreatedOn = Date(r, 7)
                };
                data.Checklists.Add(c);
                clById[c.No] = c;
            }

            await r.NextResultAsync();
            while (await r.ReadAsync())
                if (clById.TryGetValue(r.GetInt32(0), out var c) && !c.Machines.Contains(r.GetInt32(1)))
                    c.Machines.Add(r.GetInt32(1));

            await r.NextResultAsync();
            while (await r.ReadAsync())
                if (clById.TryGetValue(r.GetInt32(1), out var c))
                    c.Points.Add(new MntPoint
                    {
                        No = r.GetInt32(0), ChecklistNo = c.No, Frequency = Str(r, 2), Category = Str(r, 3),
                        SlNo = r.GetInt32(4), CheckPoint = Str(r, 5)
                    });

            await r.NextResultAsync();
            while (await r.ReadAsync())
                data.OpeningDone.Add((r.GetInt32(0), r.GetInt32(1), r.GetDateTime(2).Date));

            await r.NextResultAsync();
            var shById = new Dictionary<int, MntSheet>();
            while (await r.ReadAsync())
            {
                var s = new MntSheet
                {
                    No = r.GetInt32(0), SheetNo = Str(r, 1), MachineNo = r.GetInt32(2), ChecklistNo = r.GetInt32(3),
                    DocNo = Str(r, 4), RevNo = Str(r, 5), RevDate = Date(r, 6),
                    Year = Convert.ToInt32(r.GetValue(7)), Month = Convert.ToInt32(r.GetValue(8)),
                    Remarks = Str(r, 9), DeptSupBy = Int(r, 10), DeptSupOn = Date(r, 11),
                    MntSupBy = Int(r, 12), MntSupOn = Date(r, 13), CreatedOn = Date(r, 14)
                };
                data.Sheets.Add(s);
                shById[s.No] = s;
            }

            await r.NextResultAsync();
            while (await r.ReadAsync())
                if (shById.TryGetValue(r.GetInt32(0), out var s))
                    s.Points.Add(new MntSheetPoint
                    {
                        PointNo = r.GetInt32(1), SlNo = r.GetInt32(2), CheckPoint = Str(r, 3),
                        Frequency = Str(r, 4), Category = Str(r, 5), Remarks = Str(r, 6)
                    });

            await r.NextResultAsync();
            while (await r.ReadAsync())
                if (shById.TryGetValue(r.GetInt32(0), out var s))
                    s.Entries.Add(new MntEntry
                    {
                        PointNo = r.GetInt32(1), Slot = Convert.ToInt32(r.GetValue(2)), Status = Str(r, 3),
                        DoneOn = Date(r, 4), DoneBy = Int(r, 5)
                    });

            await r.NextResultAsync();
            while (await r.ReadAsync())
                if (shById.TryGetValue(r.GetInt32(0), out var s))
                {
                    var day = Convert.ToInt32(r.GetValue(1));
                    if (r.GetBoolean(2)) s.IdleDays.Add(day);
                    var op = Int(r, 3);
                    if (op > 0) s.DayOperators[day] = op.Value;
                }

            await r.NextResultAsync();
            while (await r.ReadAsync())
                data.Breakdowns.Add(new MntBreakdown
                {
                    No = r.GetInt32(0), BreakdownNo = Str(r, 1), MachineNo = r.GetInt32(2), BreakdownOn = r.GetDateTime(3),
                    BreakdownType = Str(r, 4), ReportedBy = Str(r, 5), Problem = Str(r, 6), AttendedBy = Int(r, 7),
                    RepairStart = Dtm(r, 8), RepairEnd = Dtm(r, 9), RootCause = Str(r, 10), ActionTaken = Str(r, 11),
                    SparesUsed = Str(r, 12), Remarks = Str(r, 13), PMSheetNo = Int(r, 14), Status = Str(r, 15),
                    CreatedOn = Date(r, 16)
                });

            await r.NextResultAsync();
            while (await r.ReadAsync())
                data.People.Add(new MntPerson
                {
                    No = r.GetInt32(0), Name = Str(r, 1), Type = Str(r, 2), Lock = Str(r, 3), Deleted = Str(r, 4) == "Y"
                });

            return data;
        }

        private static string Str(DbDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i))!.Trim();
        private static int? Int(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
        private static DateTime? Date(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i)).Date;
        private static DateTime? Dtm(DbDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));

        // ================================================================
        // LOOKUPS
        // ================================================================

        public MntMachine? Machine(int no) => Machines.FirstOrDefault(m => m.No == no);
        public MntMachine? LiveMachine(int no) => Machines.FirstOrDefault(m => m.No == no && !m.Deleted);
        public MntChecklist? Checklist(int no) => Checklists.FirstOrDefault(c => c.No == no);
        public MntSheet? Sheet(int no) => Sheets.FirstOrDefault(s => s.No == no);
        public MntBreakdown? Breakdown(int no) => Breakdowns.FirstOrDefault(b => b.No == no);

        public string StaffName(int? no) =>
            no is > 0 ? People.FirstOrDefault(x => x.No == no && MaintRules.StaffRoles.Contains(x.Type))?.Name ?? "" : "";

        public bool IsStaff(int no) =>
            no > 0 && People.Any(x => x.No == no && !x.Deleted && MaintRules.StaffRoles.Contains(x.Type));

        public MntPerson? Operator(int no) =>
            People.FirstOrDefault(x => x.No == no && x.Type == MaintRules.OperatorType);

        public List<string> OperationNames(MntMachine m) =>
            (m.Operation ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => int.TryParse(id, out var n)
                    ? People.FirstOrDefault(x => x.No == n && x.Type == MaintRules.OperationType)?.Name
                    : null)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

        // ================================================================
        // CHECKLISTS
        // ================================================================

        public static List<MntPoint> OrderedPoints(MntChecklist c) =>
            c.Points.OrderBy(p => MaintRules.FreqIndex(p.Frequency)).ThenBy(p => p.SlNo).ToList();

        /// <summary>The active checklist that covers a machine (a machine is in at most one).</summary>
        public MntChecklist? ChecklistOfMachine(int machineNo) =>
            Checklists.FirstOrDefault(c => c.Lock != "N" && c.Machines.Contains(machineNo));

        public int SheetUseCount(int checklistNo) => Sheets.Count(s => s.ChecklistNo == checklistNo);

        public PMChecklistView DecorateChecklist(MntChecklist c, bool withPoints)
        {
            var pts = OrderedPoints(c);
            var v = new PMChecklistView
            {
                MTransNo = c.No,
                ChecklistName = c.Name,
                DocNo = c.DocNo,
                RevNo = c.RevNo,
                RevDate = MaintRules.Ymd(c.RevDate),
                Machines = c.Machines.ToList(),
                MachineCodes = c.Machines.Select(no => Machine(no)?.Code ?? "").Where(x => x != "").ToList(),
                LockStatus = c.Lock == "N" ? "N" : "Y",
                Remarks = c.Remarks,
                CreatedOn = MaintRules.Ymd(c.CreatedOn),
                PointCount = pts.Count,
                SheetUseCount = SheetUseCount(c.No)
            };
            foreach (var f in MaintRules.Frequencies)
                v.FrequencyCounts[f] = pts.Count(p => p.Frequency == f);
            if (withPoints)
                v.Points = pts.Select(p => new PMChecklistPointView
                {
                    TransSlNo = p.No, ChecklistNo = p.ChecklistNo, Frequency = p.Frequency,
                    Category = p.Category, SlNo = p.SlNo, CheckPoint = p.CheckPoint
                }).ToList();
            return v;
        }

        // ================================================================
        // DUE POINTS AND COMPLIANCE
        // ================================================================

        /// <summary>
        /// Latest date a point was performed (OK or Not OK) on a machine before
        /// a date — from the opening history and every check sheet.
        /// </summary>
        public DateTime? LastDoneOf(int machineNo, int pointNo, DateTime before)
        {
            DateTime? best = null;
            foreach (var o in OpeningDone)
                if (o.MachineNo == machineNo && o.PointNo == pointNo && o.DoneOn < before && (best == null || o.DoneOn > best))
                    best = o.DoneOn;
            foreach (var sh in Sheets)
            {
                if (sh.MachineNo != machineNo) continue;
                foreach (var e in sh.Entries)
                    if (e.PointNo == pointNo && (e.Status == "OK" || e.Status == "NOK") && e.DoneOn != null
                        && e.DoneOn < before && (best == null || e.DoneOn > best))
                        best = e.DoneOn;
            }
            return best;
        }

        /// <summary>
        /// The checklist points that belong on a machine's sheet for a month,
        /// with last-done / next-due dates for the periodic ones. Half-yearly /
        /// yearly points only when due by the month end (or never done).
        /// </summary>
        public List<PMDuePoint> PointsDueFor(MntChecklist cl, int machineNo, int y, int m)
        {
            var start = MaintRules.MonthStart(y, m);
            var end = MaintRules.MonthEnd(y, m);
            var rows = new List<PMDuePoint>();
            foreach (var p in OrderedPoints(cl))
            {
                var months = MaintRules.FreqMonths(p.Frequency);
                DateTime? last = null, next = null;
                if (months > 0)
                {
                    last = LastDoneOf(machineNo, p.No, start);
                    next = last?.AddMonths(months);
                }
                if (months >= 6 && next != null && next > end) continue;
                rows.Add(new PMDuePoint
                {
                    PointNo = p.No, SlNo = p.SlNo, CheckPoint = p.CheckPoint, Frequency = p.Frequency,
                    Category = p.Category, LastDone = MaintRules.Ymd(last), NextDue = MaintRules.Ymd(next)
                });
            }
            return rows;
        }

        /// <summary>
        /// A slot is due once its period has ended, or as soon as it is done.
        /// NA (not applicable) slots and idle days are never due.
        /// </summary>
        public static PMSheetSummary Summarize(int y, int m, IEnumerable<(int PointNo, string Frequency)> points,
                                               ICollection<int> idle, IEnumerable<MntEntry> entries)
        {
            var dim = DateTime.DaysInMonth(y, m);
            var today = MaintRules.Today;
            var byKey = new Dictionary<(int, int), MntEntry>();
            foreach (var e in entries) byKey[(e.PointNo, e.Slot)] = e;
            int due = 0, done = 0, notOk = 0;
            foreach (var p in points)
            {
                var slots = new List<(int Slot, DateTime End)>();
                if (p.Frequency == "Daily")
                {
                    for (var d = 1; d <= dim; d++)
                        if (!idle.Contains(d)) slots.Add((d, new DateTime(y, m, d)));
                }
                else if (p.Frequency == "Weekly")
                {
                    for (var w = 1; w <= MaintRules.WeeksIn(dim); w++)
                        slots.Add((w, new DateTime(y, m, MaintRules.WeekEnd(w, dim))));
                }
                else slots.Add((0, new DateTime(y, m, dim)));

                foreach (var sl in slots)
                {
                    var st = byKey.TryGetValue((p.PointNo, sl.Slot), out var e) ? e.Status : "";
                    if (st == "NA") continue;
                    var performed = st == "OK" || st == "NOK";
                    if (performed || sl.End < today)
                    {
                        due++;
                        if (performed) done++;
                        if (st == "NOK") notOk++;
                    }
                }
            }
            return new PMSheetSummary
            {
                DueCount = due,
                DoneCount = done,
                MissedCount = due - done,
                NotOkCount = notOk,
                Compliance = due > 0 ? MaintRules.R1(done * 100.0 / due) : null
            };
        }

        public static string SheetStatus(MntSheet s) =>
            s.MntSupBy is > 0 ? "Approved" : s.DeptSupBy is > 0 ? "Dept Approved" : "Draft";

        public PMSheetView DecorateSheet(MntSheet sh, bool full)
        {
            var mc = Machine(sh.MachineNo);
            var cl = Checklist(sh.ChecklistNo);
            var sum = Summarize(sh.Year, sh.Month, sh.Points.Select(p => (p.PointNo, p.Frequency)), sh.IdleDays, sh.Entries);
            var v = new PMSheetView
            {
                MTransNo = sh.No,
                SheetNo = sh.SheetNo,
                MachineNo = sh.MachineNo,
                ChecklistNo = sh.ChecklistNo,
                DocNo = sh.DocNo,
                RevNo = sh.RevNo,
                RevDate = MaintRules.Ymd(sh.RevDate),
                Year = sh.Year,
                Month = sh.Month,
                Remarks = sh.Remarks,
                DeptSupBy = sh.DeptSupBy ?? 0,
                DeptSupOn = MaintRules.Ymd(sh.DeptSupOn),
                MntSupBy = sh.MntSupBy ?? 0,
                MntSupOn = MaintRules.Ymd(sh.MntSupOn),
                CreatedOn = MaintRules.Ymd(sh.CreatedOn),
                MachineCode = mc?.Code ?? "",
                MachineName = mc?.Name ?? "",
                ChecklistName = cl?.Name ?? "",
                PeriodLabel = MaintRules.PeriodLabel(sh.Year, sh.Month),
                PeriodKey = MaintRules.PeriodKey(sh.Year, sh.Month),
                SheetStatus = SheetStatus(sh),
                MonthOver = MaintRules.MonthEnd(sh.Year, sh.Month) < MaintRules.Today,
                DeptSupName = StaffName(sh.DeptSupBy),
                MntSupName = StaffName(sh.MntSupBy),
                BreakdownCount = Breakdowns.Count(b => b.PMSheetNo == sh.No),
                DueCount = sum.DueCount,
                DoneCount = sum.DoneCount,
                MissedCount = sum.MissedCount,
                NotOkCount = sum.NotOkCount,
                Compliance = sum.Compliance
            };
            if (full)
            {
                var start = MaintRules.MonthStart(sh.Year, sh.Month);
                v.Points = sh.Points.Select(p =>
                {
                    var months = MaintRules.FreqMonths(p.Frequency);
                    var last = months > 0 ? LastDoneOf(sh.MachineNo, p.PointNo, start) : null;
                    return new PMDuePoint
                    {
                        PointNo = p.PointNo, SlNo = p.SlNo, CheckPoint = p.CheckPoint, Frequency = p.Frequency,
                        Category = p.Category, LastDone = MaintRules.Ymd(last), NextDue = MaintRules.Ymd(last?.AddMonths(months))
                    };
                }).ToList();
                v.IdleDays = sh.IdleDays.OrderBy(d => d).ToList();
                v.DayOperators = sh.DayOperators.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
                v.Entries = sh.Entries.Select(e => new PMSheetEntryView
                {
                    PointNo = e.PointNo, Slot = e.Slot, Status = e.Status, DoneOn = MaintRules.Ymd(e.DoneOn), DoneBy = e.DoneBy ?? 0
                }).ToList();
                v.PointRemarks = sh.Points.Where(p => p.Remarks != "")
                    .GroupBy(p => p.PointNo).ToDictionary(g => g.Key.ToString(), g => g.First().Remarks);
            }
            return v;
        }

        // ================================================================
        // BREAKDOWNS
        // ================================================================

        public BreakdownView DecorateBreakdown(MntBreakdown b)
        {
            var mc = Machine(b.MachineNo);
            var closed = b.Status == "Closed";
            var now = MaintRules.Now;
            var sh = b.PMSheetNo is > 0 ? Sheet(b.PMSheetNo.Value) : null;
            // An open breakdown's downtime keeps running until now.
            var endAt = closed ? b.RepairEnd : now;
            return new BreakdownView
            {
                MTransNo = b.No,
                BreakdownNo = b.BreakdownNo,
                MachineNo = b.MachineNo,
                MachineCode = mc?.Code ?? "",
                MachineName = mc?.Name ?? "",
                BreakdownOn = MaintRules.Dt(b.BreakdownOn),
                BreakdownDate = MaintRules.Ymd(b.BreakdownOn),
                BreakdownType = b.BreakdownType,
                ReportedBy = b.ReportedBy,
                Problem = b.Problem,
                AttendedBy = b.AttendedBy ?? 0,
                AttendedByName = StaffName(b.AttendedBy),
                RepairStart = MaintRules.Dt(b.RepairStart),
                RepairEnd = MaintRules.Dt(b.RepairEnd),
                RootCause = b.RootCause,
                ActionTaken = b.ActionTaken,
                SparesUsed = b.SparesUsed,
                Remarks = b.Remarks,
                PMSheetNo = sh != null ? sh.No : 0,
                PmSheetRef = sh?.SheetNo ?? "",
                BreakdownStatus = b.Status,
                CreatedOn = MaintRules.Ymd(b.CreatedOn),
                DowntimeHrs = endAt != null ? MaintRules.Hours(b.BreakdownOn, endAt.Value) : 0,
                RepairHrs = b.RepairStart != null && endAt != null ? MaintRules.Hours(b.RepairStart.Value, endAt.Value) : 0,
                IsRunning = !closed
            };
        }

        public List<BreakdownView> BreakdownList() =>
            Breakdowns.OrderByDescending(b => b.BreakdownOn).ThenByDescending(b => b.No).Select(DecorateBreakdown).ToList();

        /// <summary>The number the next breakdown raised today will get.</summary>
        public string NextBreakdownNo(IEnumerable<string> allNumbersIncludingDeleted, DateTime on)
        {
            var prefix = "BD-" + MaintRules.FyCode(on) + "-";
            var max = allNumbersIncludingDeleted
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .Select(n => int.TryParse(n[prefix.Length..], out var k) ? k : 0)
                .DefaultIfEmpty(0).Max();
            return prefix + (max + 1).ToString("0000");
        }

        // ================================================================
        // MAINTENANCE REPORT
        // ================================================================

        public record Filters(DateTime From, DateTime To, int MachineNo);

        /// <summary>Report filters with the defaults: 1st of last month to today.</summary>
        public static Filters ReportFilters(string? from, string? to, int machineNo)
        {
            var today = MaintRules.Today;
            var f = MaintRules.TryDate(from, out var a) ? a : new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            var t = MaintRules.TryDate(to, out var b) ? b : today;
            return new Filters(f, t, machineNo);
        }

        /// <summary>Active machines that follow an active checklist (optionally one machine).</summary>
        public List<(MntMachine Machine, MntChecklist Checklist)> CoveredMachines(int machineNo)
        {
            var outList = new List<(MntMachine, MntChecklist)>();
            foreach (var c in Checklists)
            {
                if (c.Lock == "N") continue;
                foreach (var no in c.Machines)
                {
                    var mc = LiveMachine(no);
                    if (mc == null || mc.Lock == "N") continue;
                    if (machineNo > 0 && no != machineNo) continue;
                    if (outList.Any(o => o.Item1.No == mc.No)) continue;
                    outList.Add((mc, c));
                }
            }
            return outList.OrderBy(o => o.Item1.Code, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// One row per machine per month; a covered machine with no sheet for a
        /// month is reported "Not Started" with everything past counted as missed.
        /// Months before the first sheet ever recorded (go-live), and months
        /// before the machine was created, are not reported as missed.
        /// </summary>
        public List<ComplianceRow> ComplianceRows(Filters f)
        {
            var rows = new List<ComplianceRow>();
            var today = MaintRules.Today;
            DateTime? goLive = Sheets.Count == 0 ? null : Sheets.Min(s => MaintRules.MonthStart(s.Year, s.Month));
            var covered = CoveredMachines(f.MachineNo);

            for (var p = new DateTime(f.From.Year, f.From.Month, 1); p <= f.To; p = p.AddMonths(1))
            {
                if (p > today) break;
                var beforeGoLive = goLive == null || p < goLive;
                var seen = new HashSet<int>();
                foreach (var sh in Sheets)
                {
                    if (sh.Year != p.Year || sh.Month != p.Month) continue;
                    if (f.MachineNo > 0 && sh.MachineNo != f.MachineNo) continue;
                    seen.Add(sh.MachineNo);
                    var d = DecorateSheet(sh, false);
                    rows.Add(new ComplianceRow
                    {
                        MTransNo = d.MTransNo, SheetNo = d.SheetNo, MachineNo = d.MachineNo, MachineCode = d.MachineCode,
                        MachineName = d.MachineName, PeriodKey = d.PeriodKey, PeriodLabel = d.PeriodLabel,
                        ChecklistName = d.ChecklistName, DueCount = d.DueCount, DoneCount = d.DoneCount,
                        MissedCount = d.MissedCount, NotOkCount = d.NotOkCount, Compliance = d.Compliance,
                        SheetStatus = d.SheetStatus
                    });
                }
                var monthEnd = MaintRules.MonthEnd(p.Year, p.Month);
                foreach (var (mc, cl) in covered)
                {
                    if (beforeGoLive || seen.Contains(mc.No) || (mc.CreatedOn != null && mc.CreatedOn > monthEnd)) continue;
                    var idle = SundaysOf(p.Year, p.Month);
                    var sum = Summarize(p.Year, p.Month,
                        PointsDueFor(cl, mc.No, p.Year, p.Month).Select(x => (x.PointNo, x.Frequency)),
                        idle, Array.Empty<MntEntry>());
                    rows.Add(new ComplianceRow
                    {
                        MTransNo = 0, SheetNo = "", MachineNo = mc.No, MachineCode = mc.Code, MachineName = mc.Name,
                        PeriodKey = MaintRules.PeriodKey(p.Year, p.Month), PeriodLabel = MaintRules.PeriodLabel(p.Year, p.Month),
                        ChecklistName = cl.Name, DueCount = sum.DueCount, DoneCount = 0, MissedCount = sum.DueCount,
                        NotOkCount = 0, Compliance = sum.Compliance, SheetStatus = "Not Started"
                    });
                }
            }
            return rows.OrderByDescending(r => r.PeriodKey, StringComparer.Ordinal)
                       .ThenBy(r => r.MachineCode, StringComparer.Ordinal).ToList();
        }

        public static List<int> SundaysOf(int y, int m)
        {
            var list = new List<int>();
            for (var d = 1; d <= DateTime.DaysInMonth(y, m); d++)
                if (new DateTime(y, m, d).DayOfWeek == DayOfWeek.Sunday) list.Add(d);
            return list;
        }

        /// <summary>
        /// Monthly / half-yearly / yearly points: last done, next due and how
        /// late, up to the end of the "to" month — never done and overdue first.
        /// </summary>
        public List<DueRow> DueRows(Filters f)
        {
            var today = MaintRules.Today;
            var horizon = MaintRules.MonthEnd(f.To.Year, f.To.Month);
            var rows = new List<DueRow>();
            foreach (var (mc, cl) in CoveredMachines(f.MachineNo))
            {
                foreach (var p in OrderedPoints(cl))
                {
                    var months = MaintRules.FreqMonths(p.Frequency);
                    if (months == 0) continue;
                    var last = LastDoneOf(mc.No, p.No, DateTime.MaxValue);
                    var next = last?.AddMonths(months);
                    if (next != null && next > horizon) continue;
                    var late = next != null ? (int)(today - next.Value).TotalDays : (int?)null;
                    rows.Add(new DueRow
                    {
                        MachineNo = mc.No, MachineCode = mc.Code, MachineName = mc.Name, ChecklistName = cl.Name,
                        Frequency = p.Frequency, Category = p.Category, CheckPoint = p.CheckPoint,
                        LastDone = MaintRules.Ymd(last), NextDue = MaintRules.Ymd(next),
                        DaysOverdue = late is > 0 ? late.Value : 0,
                        DueStatus = last == null ? "Never Done" : late > 0 ? "Overdue" : "Due"
                    });
                }
            }
            var rank = new Dictionary<string, int> { ["Never Done"] = 0, ["Overdue"] = 1, ["Due"] = 2 };
            return rows.OrderBy(r => rank[r.DueStatus]).ThenByDescending(r => r.DaysOverdue)
                       .ThenBy(r => r.MachineCode, StringComparer.Ordinal).ToList();
        }

        public List<BreakdownView> BreakdownRows(Filters f) =>
            Breakdowns.Where(b => b.BreakdownOn.Date >= f.From && b.BreakdownOn.Date <= f.To
                                  && (f.MachineNo == 0 || b.MachineNo == f.MachineNo))
                      .OrderByDescending(b => b.BreakdownOn).ThenByDescending(b => b.No)
                      .Select(DecorateBreakdown).ToList();

        /// <summary>PM sheets and breakdowns of the period on one timeline.</summary>
        public List<MaintenanceHistoryRow> HistoryRows(Filters f)
        {
            var rows = new List<MaintenanceHistoryRow>();
            foreach (var c in ComplianceRows(f))
            {
                if (c.MTransNo == 0) continue;
                rows.Add(new MaintenanceHistoryRow
                {
                    EventDate = c.PeriodKey + "-01", MachineCode = c.MachineCode, MachineName = c.MachineName,
                    EventType = "PM Check Sheet", RefNo = c.SheetNo,
                    Details = c.ChecklistName + " — " + c.DoneCount + " of " + c.DueCount + " checks done, " + c.NotOkCount + " not OK",
                    Value = c.Compliance == null ? "" : Num(c.Compliance.Value) + "%",
                    EventStatus = c.SheetStatus
                });
            }
            foreach (var b in BreakdownRows(f))
                rows.Add(new MaintenanceHistoryRow
                {
                    EventDate = b.BreakdownDate, MachineCode = b.MachineCode, MachineName = b.MachineName,
                    EventType = "Breakdown", RefNo = b.BreakdownNo,
                    Details = b.Problem + (b.RootCause != "" ? " — cause: " + b.RootCause : ""),
                    Value = Num(b.DowntimeHrs) + " hrs",
                    EventStatus = b.BreakdownStatus
                });
            return rows.OrderByDescending(r => r.EventDate, StringComparer.Ordinal)
                       .ThenBy(r => r.MachineCode, StringComparer.Ordinal).ToList();
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public MaintenanceKpis Kpis(Filters f)
        {
            var comp = ComplianceRows(f).Where(r => r.Compliance != null).ToList();
            var due = DueRows(f with { To = MaintRules.Today });
            var bds = BreakdownRows(f);
            var closed = bds.Where(b => b.BreakdownStatus == "Closed").ToList();
            var downtime = bds.Sum(b => b.DowntimeHrs);
            var closedDowntime = closed.Sum(b => b.DowntimeHrs);
            var machineCount = f.MachineNo > 0 ? 1 : CoveredMachines(0).Count;
            var days = (f.To - f.From).Days + 1;
            var available = (double)days * MaintRules.ShiftsPerDay * MaintRules.ShiftHours * machineCount;
            return new MaintenanceKpis
            {
                AvgCompliance = comp.Count > 0 ? MaintRules.R1(comp.Average(r => r.Compliance!.Value)) : null,
                OverduePoints = due.Count(r => r.DueStatus != "Due"),
                OpenBreakdowns = Breakdowns.Count(b => b.Status != "Closed" && (f.MachineNo == 0 || b.MachineNo == f.MachineNo)),
                BreakdownCount = bds.Count,
                DowntimeHrs = MaintRules.R2(downtime),
                MttrHrs = closed.Count > 0 ? MaintRules.R2(closedDowntime / closed.Count) : null,
                MtbfHrs = bds.Count > 0 ? MaintRules.R2(Math.Max(0, available - downtime) / bds.Count) : null
            };
        }

        // ================================================================
        // MACHINE HISTORY CARD
        // ================================================================

        /// <summary>
        /// The earliest monthly / half-yearly / yearly check coming due on a
        /// machine (a never-done one counts as due now).
        /// </summary>
        public object? NextPmDueOf(int machineNo)
        {
            var cl = ChecklistOfMachine(machineNo);
            if (cl == null) return null;
            var today = MaintRules.Today;
            (DateTime DueOn, MntPoint P, bool Never)? best = null;
            foreach (var p in OrderedPoints(cl))
            {
                var months = MaintRules.FreqMonths(p.Frequency);
                if (months == 0) continue;
                var last = LastDoneOf(machineNo, p.No, DateTime.MaxValue);
                var dueOn = last?.AddMonths(months) ?? today;
                if (best == null || dueOn < best.Value.DueOn) best = (dueOn, p, last == null);
            }
            if (best == null) return null;
            return new
            {
                dueOn = MaintRules.Ymd(best.Value.DueOn),
                checkPoint = best.Value.P.CheckPoint,
                frequency = best.Value.P.Frequency,
                neverDone = best.Value.Never,
                overdue = best.Value.DueOn < today
            };
        }

        /// <summary>
        /// One machine's history card: its details, a summary of the period and
        /// every PM sheet, not-OK observation, periodic job and breakdown on one
        /// timeline. eventType: "" (all) | "sheet" | "nok" | "periodic" | "breakdown".
        /// </summary>
        public object HistoryCard(MntMachine mc, Filters f, string eventType)
        {
            var machineNo = mc.No;
            bool InRange(DateTime? d) => d != null && d >= f.From && d <= f.To;
            var cl = ChecklistOfMachine(machineNo);
            var rows = new List<HistoryCardEvent>();
            var nokCount = 0;
            var periodicDone = 0;

            foreach (var sh in Sheets.Where(s => s.MachineNo == machineNo))
            {
                var start = MaintRules.MonthStart(sh.Year, sh.Month);
                var end = MaintRules.MonthEnd(sh.Year, sh.Month);
                var d = DecorateSheet(sh, false);
                if (start <= f.To && end >= f.From)
                {
                    var signed = string.Join(" / ", new[] { d.DeptSupName, d.MntSupName }.Where(x => x != ""));
                    rows.Add(new HistoryCardEvent
                    {
                        Kind = "sheet", EventDate = MaintRules.Ymd(start), EventType = "PM Check Sheet", RefNo = d.SheetNo,
                        Subject = d.PeriodLabel + " — " + d.ChecklistName,
                        Details = d.DoneCount + " of " + d.DueCount + " checks done, " + d.MissedCount + " missed, " + d.NotOkCount + " not OK",
                        DoneBy = signed != "" ? "Signed: " + signed : "",
                        Compliance = d.Compliance, EventStatus = d.SheetStatus
                    });
                }
                var pointMap = sh.Points.GroupBy(p => p.PointNo).ToDictionary(g => g.Key, g => g.First());
                var raised = Breakdowns.Where(b => b.PMSheetNo == sh.No).Select(b => b.BreakdownNo).ToList();
                foreach (var e in sh.Entries)
                {
                    if (!pointMap.TryGetValue(e.PointNo, out var p) || !InRange(e.DoneOn)) continue;
                    var periodic = MaintRules.FreqMonths(p.Frequency) > 0;
                    var remark = p.Remarks != "" ? " — " + p.Remarks : "";
                    if (e.Status == "NOK") nokCount++;
                    if (periodic && (e.Status == "OK" || e.Status == "NOK"))
                    {
                        periodicDone++;
                        rows.Add(new HistoryCardEvent
                        {
                            Kind = "periodic", EventDate = MaintRules.Ymd(e.DoneOn), EventType = "Periodic Maintenance",
                            RefNo = d.SheetNo, Subject = p.CheckPoint,
                            Details = p.Frequency + " " + p.Category.ToLowerInvariant() + " check" + remark,
                            DoneBy = StaffName(e.DoneBy), EventStatus = e.Status == "OK" ? "Done" : "Not OK"
                        });
                    }
                    else if (!periodic && e.Status == "NOK")
                    {
                        // Daily checks are signed by the day's operator, weekly ones by staff.
                        var op = p.Frequency == "Daily" && sh.DayOperators.TryGetValue(e.Slot, out var opNo) ? Operator(opNo) : null;
                        rows.Add(new HistoryCardEvent
                        {
                            Kind = "nok", EventDate = MaintRules.Ymd(e.DoneOn), EventType = "PM Not OK",
                            RefNo = d.SheetNo, Subject = p.CheckPoint,
                            Details = p.Frequency + " check" + (p.Frequency == "Weekly" ? " (W" + e.Slot + ")" : "") + remark
                                      + (raised.Count > 0 ? " · breakdown " + string.Join(", ", raised) : ""),
                            DoneBy = op != null ? op.Name : StaffName(e.DoneBy),
                            EventStatus = "Not OK"
                        });
                    }
                }
            }

            var bds = BreakdownRows(f with { MachineNo = machineNo });
            foreach (var b in bds)
                rows.Add(new HistoryCardEvent
                {
                    Kind = "breakdown", EventDate = b.BreakdownDate, EventTime = b.BreakdownOn.Length >= 16 ? b.BreakdownOn.Substring(11, 5) : "",
                    EventType = "Breakdown", RefNo = b.BreakdownNo, Subject = b.Problem,
                    Details = string.Join(" · ", new[]
                    {
                        b.BreakdownType,
                        b.RootCause != "" ? "Cause: " + b.RootCause : "",
                        b.ActionTaken != "" ? "Action: " + b.ActionTaken : ""
                    }.Where(x => x != "")),
                    DoneBy = b.AttendedByName, SparesUsed = b.SparesUsed, DowntimeHrs = b.DowntimeHrs,
                    EventStatus = b.BreakdownStatus
                });

            // Summary of the whole period (independent of the event filter).
            var comp = ComplianceRows(f with { MachineNo = machineNo });
            var withComp = comp.Where(r => r.Compliance != null).ToList();
            var closed = bds.Where(b => b.BreakdownStatus == "Closed").ToList();
            var downtime = bds.Sum(b => b.DowntimeHrs);
            var closedDowntime = closed.Sum(b => b.DowntimeHrs);
            var available = (double)((f.To - f.From).Days + 1) * MaintRules.ShiftsPerDay * MaintRules.ShiftHours;
            var all = Breakdowns.Where(b => b.MachineNo == machineNo).OrderByDescending(b => b.BreakdownOn).ToList();
            var open = all.FirstOrDefault(b => b.Status != "Closed");

            return new
            {
                machine = new
                {
                    machineNo = mc.No,
                    machineCode = mc.Code,
                    machineName = mc.Name,
                    operations = string.Join(", ", OperationNames(mc)),
                    checklistName = cl?.Name ?? "",
                    docNo = cl?.DocNo ?? "",
                    revNo = cl?.RevNo ?? "",
                    inServiceSince = MaintRules.Ymd(mc.InstalledOn ?? mc.CreatedOn),
                    status = mc.Lock == "N" ? "Inactive" : "Active",
                    condition = open != null ? "Under breakdown (" + open.BreakdownNo + ", " + open.Status + ")" : "Running",
                    remarks = mc.Remarks
                },
                summary = new
                {
                    sheetCount = comp.Count(r => r.MTransNo > 0),
                    monthsWithoutSheet = comp.Count(r => r.MTransNo == 0),
                    avgCompliance = withComp.Count > 0 ? MaintRules.R1(withComp.Average(r => r.Compliance!.Value)) : (double?)null,
                    notOkCount = nokCount,
                    periodicDone,
                    breakdownCount = bds.Count,
                    downtimeHrs = MaintRules.R2(downtime),
                    mttrHrs = closed.Count > 0 ? MaintRules.R2(closedDowntime / closed.Count) : (double?)null,
                    mtbfHrs = bds.Count > 0 ? MaintRules.R2(Math.Max(0, available - downtime) / bds.Count) : (double?)null,
                    lastBreakdown = all.Count > 0
                        ? new { breakdownNo = all[0].BreakdownNo, date = MaintRules.Ymd(all[0].BreakdownOn), problem = all[0].Problem }
                        : null,
                    nextPmDue = NextPmDueOf(machineNo)
                },
                rows = rows.Where(r => eventType == "" || r.Kind == eventType)
                           .OrderByDescending(r => r.EventDate, StringComparer.Ordinal)
                           .ThenBy(r => HistoryKindOrder[r.Kind])
                           .ToList()
            };
        }
    }
}
