using EMSAPI.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using static EMSAPI.Models.PlanScheduleEngine;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Shared reads and maths of the Production Planning module (SQL 59):
    /// the plan, its work calendar and hammers, the day split
    /// (PlanScheduleEngine), what each plan line has forged, what every OA
    /// line still owes, and the raw-material allocation — the server side of
    /// what Config/js/planning-mock-store.js did for the pages.
    ///
    /// Data sources:
    ///   hammers      MachineMst (live, active) whose Operation CSV holds an
    ///                'Operation' OtherMaster row named like "Forg%"; shifts /
    ///                efficiency from MachineMst.ShiftsPerDay / EfficiencyPct
    ///   calendar     WorkCalendarMst (active, the month) + WorkCalendarHoliday
    ///   OA lines     OAMst / OADtl, live current revision, not rejected
    ///   dispatched   STxnDtl.LinkNo -> OADtl, matched across OA revisions by
    ///                DocSeries + DocYear + DocNo + line SlNo (as SalesInvoice)
    ///   forged       JobCardEntry qty at the job card route's Forging step
    ///                (the last step when the route has no Forging step)
    ///   rejected     JobCardInspection.RejectedQty
    ///   raw material ConvBOM (FG part + FG grade, blank FG grade = all grades)
    ///   stock        VW_StoreStock (in − out) by part + grade
    ///   reserved     open job cards' RM still to issue (VW_JobCardSummary)
    ///   open POs     VW_POPendingGRN.BalanceQty by part + grade
    /// Pending to forge = OA qty − MAX(dispatched, forged − rejected): every
    /// piece dispatched was made, and every good forged piece is made.
    /// </summary>
    public abstract class PlanningControllerBase : JobCardControllerBase
    {
        protected PlanningControllerBase(EMSContext context) : base(context) { }

        protected static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        protected static string Today => DateTime.Today.ToString("yyyy-MM-dd", Inv);
        protected static double R2(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        protected static double Dbl(Dictionary<string, object?>? row, string key) => (double)D(row, key);

        protected static string LastOfMonth(string calMonth)
        {
            if (!DateTime.TryParseExact((calMonth ?? "") + "-01", "yyyy-MM-dd", Inv, DateTimeStyles.None, out var d)) return "";
            return Iso(d.AddMonths(1).AddDays(-1));
        }

        /// <summary>
        /// "26-27/0041" when the OA year is a financial-year code (2627), else
        /// "2026/0041" when it is a calendar year.
        /// </summary>
        protected static string OALabel(int year, int no)
        {
            var isFyCode = year >= 1000 && year <= 9999 && (year / 100 + 1) % 100 == year % 100;
            var y = isFyCode ? (year / 100).ToString("00", Inv) + "-" + (year % 100).ToString("00", Inv) : year.ToString(Inv);
            return y + "/" + no.ToString("0000", Inv);
        }

        protected static string MaterialClassOf(string grade)
        {
            var g = (grade ?? "").ToUpperInvariant();
            if (g.Contains("SS") || g.Contains("304") || g.Contains("316") || g.Contains("321") || g.Contains("F51") || g.Contains("F53"))
                return "Stainless Steel";
            if (g.Contains("F11") || g.Contains("F22") || g.Contains("F91") || g.Contains("MNCR") || g.Contains("EN36")
                || g.Contains("8620") || g.Contains("4140") || g.Contains("EN19") || g.Contains("EN24") || g.Contains("CRMO"))
                return "Alloy Steel";
            return "Carbon Steel";
        }

        // ================================================================
        // SHAPES
        // ================================================================

        protected sealed class PlanLine
        {
            public int SlNo; public string Source = "OA"; public int OAYear, OANo, OAID, OALinkNo;
            public string Grade = ""; public double PlanQty, Rate; public int RouteNo; public string Reason = "";
        }

        protected sealed class PlanJob
        {
            public int Seq; public string PlanType = "Main"; public int PartNo, DieNo, MachineNo;
            public double DaysRequired; public string FixedStart = "", Remarks = "";
            public List<PlanLine> Lines = new();
        }

        protected sealed class PlanHead
        {
            public int MTransNo, RevNo; public string PlanNo = "", PlanDate = "", PlanMonth = "", PlanStatus = "", Remarks = "", CreatedOn = "";
            public Dictionary<int, string> Weeks = new();
            public List<PlanJob> Jobs = new();
            public List<Dictionary<string, object?>> Reschedules = new();
        }

        protected sealed class HammerRow
        {
            public int MachineNo; public string Code = "", Name = ""; public double Shifts = 1, Efficiency = 100, MinWt, MaxWt;
        }

        protected sealed class OALineRow
        {
            public int OAYear, OANo, OAID, TransSlNo, PartNo; public string DocSeries = "", Label = "", OADate = "", Customer = "",
                PoNo = "", PoDate = "", DeliveryDate = "", CommittedDate = "", Priority = "Normal", Grade = "";
            public bool CustomerMaterial; public double Qty, Rate, Dispatched;
            public string PlanDate => string.IsNullOrEmpty(CommittedDate) ? DeliveryDate : CommittedDate;
            public string Key => OAYear + "/" + OANo + "/" + OAID;
        }

        protected sealed class RouteRow
        {
            public int RouteNo, PartNo, GradeNo, BatchQty, PostForgeDays, HammerNo; public double SetupMin, ForgeTimeMin;
            public string Name = "", Grade = "";
        }

        protected sealed class BarRow
        {
            public int RMPartNo, RMGradeNo; public string RMPartCode = "", RMGrade = "", Unit = ""; public double CutWt, Dia;
            public string Key => RMPartNo + "|" + RMGrade;
            public string Text => (string.IsNullOrEmpty(RMPartCode) ? "" : RMPartCode + " · ") + RMGrade + (Dia > 0 ? " · Ø" + Dia.ToString("0.##", Inv) : "");
        }

        protected sealed class JobCardRef
        {
            public int MTransNo, PlanId, Seq, LineSlNo, OAYear, OANo, OAID; public string No = "", Status = "";
            public double Qty, Forged, Rejected, RMBalance; public int RMPartNo; public string RMGrade = "";
            public Dictionary<string, double> ForgedByDate = new(), RejectedByDate = new();
        }

        /// <summary>Everything the planning reads for one subscription, loaded once per request.</summary>
        protected sealed class PlanningData
        {
            public int SubscID;
            public List<HammerRow> Hammers = new();
            public Dictionary<int, (string Code, string Name)> Parts = new();
            public Dictionary<int, (string Name, string Status, double Remaining)> Dies = new();
            public Dictionary<string, OALineRow> OALines = new();
            public Dictionary<int, RouteRow> Routes = new();
            public List<Dictionary<string, object?>> ConvBom = new();
            public List<JobCardRef> JobCards = new();
            public Dictionary<(int PartNo, string Grade), int> GradeNos = new();

            public string HammerName(int no) => Hammers.FirstOrDefault(h => h.MachineNo == no)?.Name ?? (no > 0 ? "Machine " + no : "");
            public string PartName(int no) => Parts.TryGetValue(no, out var p) ? p.Name : "";
            public string PartCode(int no) => Parts.TryGetValue(no, out var p) ? p.Code : "";
            public OALineRow? OALine(int y, int n, int id) => OALines.TryGetValue(y + "/" + n + "/" + id, out var o) ? o : null;

            public RouteRow? RouteFor(int partNo, string grade, int hammerNo = 0)
            {
                var mine = Routes.Values.Where(r => r.PartNo == partNo && string.Equals(r.Grade, grade, StringComparison.OrdinalIgnoreCase))
                                        .OrderBy(r => r.RouteNo).ToList();
                return (hammerNo > 0 ? mine.FirstOrDefault(r => r.HammerNo == hammerNo) : null) ?? mine.FirstOrDefault();
            }

            public BarRow BarOf(int partNo, string grade)
            {
                var rows = ConvBom.Where(r => I(r, "fPartNo") == partNo).ToList();
                var pick = rows.FirstOrDefault(r => string.Equals(S(r, "fGrade"), grade ?? "", StringComparison.OrdinalIgnoreCase))
                        ?? rows.FirstOrDefault(r => S(r, "fGrade") == "");
                if (pick == null) return new BarRow();
                return new BarRow
                {
                    RMPartNo = I(pick, "partNo"), RMGradeNo = I(pick, "gradeNo"), RMPartCode = S(pick, "partCode"),
                    RMGrade = S(pick, "grade"), Unit = S(pick, "unit"), CutWt = (double)D(pick, "qty"), Dia = (double)D(pick, "barDia"),
                };
            }

            public JobCardRef? JobCardOf(int planId, int seq, int lineSlNo) =>
                JobCards.FirstOrDefault(j => j.PlanId == planId && j.Seq == seq && j.LineSlNo == lineSlNo);

            public int PostForgeDaysOf(int partNo, string grade, int hammerNo = 0) => RouteFor(partNo, grade, hammerNo)?.PostForgeDays ?? 0;
        }

        // ================================================================
        // LOADERS
        // ================================================================

        protected async Task<PlanningData> LoadPlanningData(int subscID)
        {
            var d = new PlanningData { SubscID = subscID };
            var s = Int("@SubscID", subscID);

            // Hammers — Machine Master machines mapped to a Forging operation.
            foreach (var r in await Query(@"
                SELECT m.MTransNo AS machineNo, ISNULL(m.MachineCode, '') AS code, ISNULL(m.MachineName, '') AS name,
                       ISNULL(m.ShiftsPerDay, 1) AS shifts, ISNULL(m.EfficiencyPct, 100) AS efficiency,
                       ISNULL(m.MinPieceWt, 0) AS minWt, ISNULL(m.MaxPieceWt, 0) AS maxWt
                  FROM dbo.MachineMst m
                 WHERE m.SubscID = @SubscID AND ISNULL(m.DeleteStatus, 'N') = 'N' AND ISNULL(m.LockStatus, 'Y') = 'Y'
                   AND EXISTS (SELECT 1 FROM STRING_SPLIT(ISNULL(m.Operation, ''), ',') x
                                 JOIN dbo.OtherMaster o ON o.MTransNo = TRY_CONVERT(INT, LTRIM(RTRIM(x.value)))
                                WHERE o.MasterType = 'Operation' AND o.MasterName LIKE '%forg%')
                 ORDER BY m.MachineCode", Int("@SubscID", subscID)))
                d.Hammers.Add(new HammerRow
                {
                    MachineNo = I(r, "machineNo"), Code = S(r, "code"), Name = S(r, "name"),
                    Shifts = Dbl(r, "shifts"), Efficiency = Dbl(r, "efficiency"), MinWt = Dbl(r, "minWt"), MaxWt = Dbl(r, "maxWt"),
                });

            foreach (var r in await Query("SELECT MTransNo AS partNo, ISNULL(PartCode, '') AS code, ISNULL(PartName, '') AS name FROM dbo.PartMaster WHERE SubscID = @SubscID", Int("@SubscID", subscID)))
                d.Parts[I(r, "partNo")] = (S(r, "code"), S(r, "name"));

            foreach (var r in await Query(@"
                SELECT g.PartNo AS partNo, g.GradeNo AS gradeNo, LTRIM(RTRIM(g.Grade)) AS grade
                  FROM dbo.MaterialGrade g WHERE g.SubscID = @SubscID AND g.DeleteStatus = 'N'", Int("@SubscID", subscID)))
                d.GradeNos[(I(r, "partNo"), S(r, "grade").ToUpperInvariant())] = I(r, "gradeNo");

            foreach (var r in await Query(@"
                SELECT dm.MTransNo AS dieNo, CONCAT(ISNULL(p.PartCode, ''), CASE WHEN mg.Grade IS NULL THEN '' ELSE ' · ' + mg.Grade END) AS name,
                       ISNULL(dm.DieStatus, 'Ready') AS status,
                       CASE WHEN ISNULL(dm.MaxQty, 0) - ISNULL(dm.OpeningQty, 0) - ISNULL(dm.UsedQty, 0) < 0 THEN 0
                            ELSE ISNULL(dm.MaxQty, 0) - ISNULL(dm.OpeningQty, 0) - ISNULL(dm.UsedQty, 0) END AS remaining
                  FROM dbo.DieMst dm
                  LEFT JOIN dbo.PartMaster p ON p.MTransNo = dm.PartNo
                  OUTER APPLY (SELECT TOP (1) g.Grade FROM dbo.MaterialGrade g
                                WHERE g.PartNo = dm.PartNo AND g.GradeNo = dm.GradeNo AND g.SubscID = dm.SubscID AND g.DeleteStatus = 'N') mg
                 WHERE dm.SubscID = @SubscID AND ISNULL(dm.DeleteStatus, 'N') = 'N'", s))
                d.Dies[I(r, "dieNo")] = (S(r, "name"), S(r, "status"), Dbl(r, "remaining"));

            // Routes, with the machine and time of their Forging step.
            foreach (var r in await Query(@"
                SELECT h.MTransNo AS routeNo, h.PartNo AS partNo, ISNULL(h.GradeNo, 0) AS gradeNo,
                       ISNULL(mg.Grade, '') AS grade, ISNULL(h.RouteName, '') AS name, ISNULL(h.BatchQty, 0) AS batchQty,
                       ISNULL(h.PostForgeDays, 0) AS postForgeDays,
                       ISNULL(f.MachineNo, 0) AS hammerNo, ISNULL(f.SetupMin, 0) AS setupMin, ISNULL(f.TimeMin, 0) AS forgeTimeMin
                  FROM dbo.RoutingChartMst h
                  OUTER APPLY (SELECT TOP (1) g.Grade FROM dbo.MaterialGrade g
                                WHERE g.PartNo = h.PartNo AND g.GradeNo = h.GradeNo AND g.SubscID = h.SubscID AND g.DeleteStatus = 'N') mg
                  OUTER APPLY (SELECT TOP (1) rd.MachineNo, rd.SetupMin, rd.TimeMin FROM dbo.RoutingChartDtl rd
                                 JOIN dbo.OtherMaster o ON o.MTransNo = rd.Operation
                                WHERE rd.MTransNo = h.MTransNo AND ISNULL(rd.DeleteStatus, 'N') = 'N' AND o.MasterName LIKE '%forg%'
                                ORDER BY rd.SlNo) f
                 WHERE h.SubscID = @SubscID AND ISNULL(h.DeleteStatus, 'N') = 'N' AND ISNULL(h.LockStatus, 'Y') = 'Y'", Int("@SubscID", subscID)))
                d.Routes[I(r, "routeNo")] = new RouteRow
                {
                    RouteNo = I(r, "routeNo"), PartNo = I(r, "partNo"), GradeNo = I(r, "gradeNo"), Grade = S(r, "grade"),
                    Name = S(r, "name"), BatchQty = I(r, "batchQty"), PostForgeDays = I(r, "postForgeDays"),
                    HammerNo = I(r, "hammerNo"), SetupMin = Dbl(r, "setupMin"), ForgeTimeMin = Dbl(r, "forgeTimeMin"),
                };

            d.ConvBom = await Query(@"
                SELECT b.FPartNo AS fPartNo, LTRIM(RTRIM(ISNULL(b.FGrade, ''))) AS fGrade, b.PartNo AS partNo,
                       ISNULL(b.GradeNo, 0) AS gradeNo, LTRIM(RTRIM(ISNULL(b.Grade, ''))) AS grade, ISNULL(b.Qty, 0) AS qty,
                       ISNULL(b.Unit, '') AS unit, ISNULL(b.BarDia, 0) AS barDia, ISNULL(p.PartCode, '') AS partCode
                  FROM dbo.ConvBOM b LEFT JOIN dbo.PartMaster p ON p.MTransNo = b.PartNo
                 WHERE b.SubscID = @SubscID AND b.DeleteStatus = 'N' AND ISNULL(b.LockStatus, 'Y') <> 'N'
                 ORDER BY b.MTransNo", Int("@SubscID", subscID));

            // OA lines (live, current revision, not rejected) with what has been dispatched.
            foreach (var r in await Query(@"
                WITH billed AS (
                    SELECT m2.DocSeries, m2.DocYear, m2.DocNo, CAST(d2.SlNo AS INT) AS SlNo, SUM(s.Qty) AS Qty
                      FROM dbo.STxnDtl s
                      JOIN dbo.STxnMst h ON h.MTransNo = s.MTransNo AND h.DeleteStatus = 'N'
                      JOIN dbo.OADtl d2  ON d2.TransSlNo = s.LinkNo
                      JOIN dbo.OAMst m2  ON m2.MTransNo = d2.MTransNo
                     WHERE s.DeleteStatus = 'N' AND h.SubscID = @SubscID
                     GROUP BY m2.DocSeries, m2.DocYear, m2.DocNo, CAST(d2.SlNo AS INT))
                SELECT m.DocYear AS oaYear, m.DocNo AS oaNo, CAST(d.SlNo AS INT) AS oaId, d.TransSlNo AS transSlNo,
                       ISNULL(m.DocSeries, '') AS docSeries, CONVERT(char(10), m.DocDate, 23) AS oaDate,
                       ISNULL(m.VendorName, '') AS customer, ISNULL(m.RefNo, '') AS poNo, CONVERT(char(10), m.RefDate, 23) AS poDate,
                       d.PartNo AS partNo, LTRIM(RTRIM(ISNULL(d.MocCode, ''))) AS grade, ISNULL(d.Qty, 0) AS qty, ISNULL(d.Rate, 0) AS rate,
                       CONVERT(char(10), d.DeliveryDate, 23) AS deliveryDate, CONVERT(char(10), d.CommittedDate, 23) AS committedDate,
                       ISNULL(d.Priority, 'Normal') AS priority, ISNULL(d.CustomerMaterial, 'N') AS customerMaterial,
                       ISNULL(b.Qty, 0) AS dispatched
                  FROM dbo.OAMst m
                  JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                  LEFT JOIN billed b ON b.DocSeries = m.DocSeries AND b.DocYear = m.DocYear AND b.DocNo = m.DocNo AND b.SlNo = CAST(d.SlNo AS INT)
                 WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N' AND ISNULL(m.LockStatus, '') <> 'R'", Int("@SubscID", subscID)))
            {
                var o = new OALineRow
                {
                    OAYear = I(r, "oaYear"), OANo = I(r, "oaNo"), OAID = I(r, "oaId"), TransSlNo = I(r, "transSlNo"),
                    DocSeries = S(r, "docSeries"), OADate = S(r, "oaDate"), Customer = S(r, "customer"), PoNo = S(r, "poNo"),
                    PoDate = S(r, "poDate"), PartNo = I(r, "partNo"), Grade = S(r, "grade"), Qty = Dbl(r, "qty"), Rate = Dbl(r, "rate"),
                    DeliveryDate = S(r, "deliveryDate"), CommittedDate = S(r, "committedDate"), Priority = S(r, "priority"),
                    CustomerMaterial = S(r, "customerMaterial") == "Y", Dispatched = Dbl(r, "dispatched"),
                };
                o.Label = OALabel(o.OAYear, o.OANo);
                d.OALines[o.Key] = o;
            }

            // Job cards with what they forged (Forging step, by day) and rejected.
            var jcs = await Query(@"
                SELECT j.MTransNo AS mTransNo, j.JobCardNo AS no, ISNULL(j.PlanID, 0) AS planId, ISNULL(j.PlanJobSeq, 0) AS seq,
                       ISNULL(j.PlanLineSlNo, 0) AS lineSlNo, j.OAYear AS oaYear, j.OANo AS oaNo, j.OAID AS oaId, j.Qty AS qty,
                       ISNULL(s.JobStatus, '') AS status, ISNULL(s.RejectedQty, 0) AS rejected, ISNULL(s.RMBalanceQty, 0) AS rmBalance,
                       j.RMPartNo AS rmPartNo, LTRIM(RTRIM(j.RMGrade)) AS rmGrade
                  FROM dbo.JobCardMst j
                  LEFT JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
                 WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N'", Int("@SubscID", subscID));
            var byNo = new Dictionary<int, JobCardRef>();
            foreach (var r in jcs)
            {
                var j = new JobCardRef
                {
                    MTransNo = I(r, "mTransNo"), No = S(r, "no"), PlanId = I(r, "planId"), Seq = I(r, "seq"), LineSlNo = I(r, "lineSlNo"),
                    OAYear = I(r, "oaYear"), OANo = I(r, "oaNo"), OAID = I(r, "oaId"), Qty = Dbl(r, "qty"), Status = S(r, "status"),
                    Rejected = Dbl(r, "rejected"), RMBalance = Dbl(r, "rmBalance"), RMPartNo = I(r, "rmPartNo"), RMGrade = S(r, "rmGrade"),
                };
                d.JobCards.Add(j);
                byNo[j.MTransNo] = j;
            }
            foreach (var r in await Query(@"
                SELECT e.MTransNo AS jc, CONVERT(char(10), e.EntryDate, 23) AS d, SUM(e.Qty) AS q
                  FROM dbo.JobCardEntry e
                  JOIN dbo.JobCardMst j ON j.MTransNo = e.MTransNo AND j.DeleteStatus = 'N'
                  OUTER APPLY (SELECT ISNULL(MIN(CASE WHEN o.MasterName LIKE '%forg%' THEN rd.SlNo END), MAX(rd.SlNo)) AS ForgeSlNo
                                 FROM dbo.RoutingChartDtl rd
                                 LEFT JOIN dbo.OtherMaster o ON o.MTransNo = rd.Operation
                                WHERE rd.MTransNo = j.RouteNo AND ISNULL(rd.DeleteStatus, 'N') = 'N') f
                 WHERE e.DeleteStatus = 'N' AND j.SubscID = @SubscID AND e.StepSlNo = f.ForgeSlNo
                 GROUP BY e.MTransNo, CONVERT(char(10), e.EntryDate, 23)", Int("@SubscID", subscID)))
                if (byNo.TryGetValue(I(r, "jc"), out var j))
                {
                    j.ForgedByDate[S(r, "d")] = Dbl(r, "q");
                    j.Forged += Dbl(r, "q");
                }
            foreach (var r in await Query(@"
                SELECT i.JobCardTransNo AS jc, CONVERT(char(10), i.InspDate, 23) AS d, SUM(i.RejectedQty) AS q
                  FROM dbo.JobCardInspection i
                 WHERE i.DeleteStatus = 'N' AND i.SubscID = @SubscID AND ISNULL(i.RejectedQty, 0) > 0
                 GROUP BY i.JobCardTransNo, CONVERT(char(10), i.InspDate, 23)", Int("@SubscID", subscID)))
                if (byNo.TryGetValue(I(r, "jc"), out var j))
                    j.RejectedByDate[S(r, "d")] = Dbl(r, "q");
            return d;
        }

        protected async Task<List<PlanHead>> LoadPlans(int subscID, int planId = 0)
        {
            var heads = (await Query(@"
                SELECT MTransNo AS mTransNo, PlanNo AS planNo, CONVERT(char(10), PlanDate, 23) AS planDate, PlanMonth AS planMonth,
                       RevNo AS revNo, PlanStatus AS planStatus, ISNULL(Remarks, '') AS remarks, CONVERT(char(10), CreatedOn, 23) AS createdOn
                  FROM dbo.ProductionPlanMst
                 WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND (@Id = 0 OR MTransNo = @Id)
                 ORDER BY PlanMonth DESC, MTransNo DESC", Int("@SubscID", subscID), Int("@Id", planId)))
                .Select(r => new PlanHead
                {
                    MTransNo = I(r, "mTransNo"), PlanNo = S(r, "planNo"), PlanDate = S(r, "planDate"), PlanMonth = S(r, "planMonth"),
                    RevNo = I(r, "revNo"), PlanStatus = S(r, "planStatus"), Remarks = S(r, "remarks"), CreatedOn = S(r, "createdOn"),
                }).ToList();
            if (heads.Count == 0) return heads;
            var byId = heads.ToDictionary(h => h.MTransNo);
            var ids = string.Join(",", heads.Select(h => h.MTransNo));

            foreach (var r in await Query($"SELECT MTransNo AS id, WeekNo AS weekNo, WeekStatus AS status FROM dbo.ProductionPlanWeek WHERE MTransNo IN ({ids})"))
                byId[I(r, "id")].Weeks[I(r, "weekNo")] = S(r, "status");

            foreach (var r in await Query($@"
                SELECT MTransNo AS id, JobSeq AS seq, PlanType AS planType, PartNo AS partNo, ISNULL(DieNo, 0) AS dieNo,
                       ISNULL(MachineNo, 0) AS machineNo, DaysRequired AS days, CONVERT(char(10), FixedStart, 23) AS fixedStart,
                       ISNULL(Remarks, '') AS remarks
                  FROM dbo.ProductionPlanJob WHERE MTransNo IN ({ids}) AND DeleteStatus = 'N' ORDER BY MTransNo, JobSeq"))
                byId[I(r, "id")].Jobs.Add(new PlanJob
                {
                    Seq = I(r, "seq"), PlanType = S(r, "planType"), PartNo = I(r, "partNo"), DieNo = I(r, "dieNo"),
                    MachineNo = I(r, "machineNo"), DaysRequired = Dbl(r, "days"), FixedStart = S(r, "fixedStart"), Remarks = S(r, "remarks"),
                });

            foreach (var r in await Query($@"
                SELECT MTransNo AS id, JobSeq AS seq, LineSlNo AS slNo, Source AS source, ISNULL(OAYear, 0) AS oaYear,
                       ISNULL(OANo, 0) AS oaNo, ISNULL(OAID, 0) AS oaId, ISNULL(OALinkNo, 0) AS oaLinkNo, Grade AS grade,
                       PlanQty AS planQty, Rate AS rate, ISNULL(RouteNo, 0) AS routeNo, ISNULL(Reason, '') AS reason
                  FROM dbo.ProductionPlanLine WHERE MTransNo IN ({ids}) AND DeleteStatus = 'N' ORDER BY MTransNo, JobSeq, LineSlNo"))
            {
                var job = byId[I(r, "id")].Jobs.FirstOrDefault(j => j.Seq == I(r, "seq"));
                job?.Lines.Add(new PlanLine
                {
                    SlNo = I(r, "slNo"), Source = S(r, "source"), OAYear = I(r, "oaYear"), OANo = I(r, "oaNo"), OAID = I(r, "oaId"),
                    OALinkNo = I(r, "oaLinkNo"), Grade = S(r, "grade"), PlanQty = Dbl(r, "planQty"), Rate = Dbl(r, "rate"),
                    RouteNo = I(r, "routeNo"), Reason = S(r, "reason"),
                });
            }

            foreach (var r in await Query($@"
                SELECT x.MTransNo AS id, CONVERT(char(10), x.CreatedOn, 23) AS [on], x.CreatedBy AS [by],
                       ISNULL(u.UserId, '') AS byName, x.JobSeq AS jobSeq, ISNULL(x.FromText, '') AS [from],
                       ISNULL(x.ToText, '') AS [to], x.Reason AS reason
                  FROM dbo.ProductionPlanReschedule x
                  LEFT JOIN dbo.UserMaster u ON u.MTransNo = x.CreatedBy
                 WHERE x.MTransNo IN ({ids}) ORDER BY x.TransSlNo"))
            {
                var id = I(r, "id");
                r.Remove("id");
                byId[id].Reschedules.Add(r);
            }
            return heads;
        }

        /// <summary>The month's active work calendar with the hammers, or null when there is none.</summary>
        protected async Task<CalendarInfo?> CalendarOf(string calMonth, PlanningData data)
        {
            var cal = await QueryOne(@"
                SELECT TOP (1) c.MTransNo AS id, c.CalMonth AS calMonth, c.ShiftName AS shiftName, c.ShiftStart AS shiftStart,
                       c.ShiftEnd AS shiftEnd, c.BreakMin AS breakMin, c.WeeklyOff AS weeklyOff
                  FROM dbo.WorkCalendarMst c
                 WHERE c.SubscID = @SubscID AND c.DeleteStatus = 'N' AND c.CalMonth = @Month
                 ORDER BY CASE WHEN c.LockStatus = 'Y' THEN 0 ELSE 1 END, c.MTransNo",
                Int("@SubscID", data.SubscID), Str("@Month", calMonth, 7));
            if (cal == null) return null;
            var hol = (await Query("SELECT CONVERT(char(10), HoliDate, 23) AS d FROM dbo.WorkCalendarHoliday WHERE MTransNo = @Id AND DeleteStatus = 'N'",
                Int("@Id", I(cal, "id")))).Select(r => S(r, "d"));
            return CalendarInfoOf(S(cal, "calMonth"), S(cal, "shiftName"), S(cal, "shiftStart"), S(cal, "shiftEnd"),
                I(cal, "breakMin"), S(cal, "weeklyOff"), hol,
                data.Hammers.Select(h => new Hammer { MachineNo = h.MachineNo, Shifts = h.Shifts, Efficiency = h.Efficiency }));
        }

        // ================================================================
        // SCHEDULE
        // ================================================================

        protected static Built BuildPlan(PlanHead plan, CalendarInfo? info)
        {
            var jobs = plan.Jobs.Select(j =>
            {
                var e = new Job { PlanType = j.PlanType, MachineNo = j.MachineNo, DaysRequired = j.DaysRequired, FixedStart = j.FixedStart };
                e.LineQtys.AddRange(j.Lines.Select(l => l.PlanQty));
                return e;
            }).ToList();
            return Build(jobs, info);
        }

        /// <summary>The day split to store: one row per line and date (rows past month end dropped).</summary>
        protected static List<object> DaysOf(PlanHead plan, Built built) =>
            built.Buckets.Where(b => b.Date != "" && b.Qty > 0)
                .GroupBy(b => new { b.JobIdx, b.LineIdx, b.Date })
                .Select(g => (object)new
                {
                    jobSeq = plan.Jobs[g.Key.JobIdx].Seq,
                    lineSlNo = plan.Jobs[g.Key.JobIdx].Lines[g.Key.LineIdx].SlNo,
                    planDate = g.Key.Date,
                    planQty = g.Sum(x => x.Qty),
                }).ToList();

        /// <summary>Planned qty by line and date, as stored (ProductionPlanDay).</summary>
        protected async Task<Dictionary<(int Seq, int Line), Dictionary<string, double>>> StoredDays(int planId)
        {
            var map = new Dictionary<(int, int), Dictionary<string, double>>();
            foreach (var r in await Query(@"SELECT JobSeq AS seq, LineSlNo AS line, CONVERT(char(10), PlanDate, 23) AS d, SUM(PlanQty) AS q
                                              FROM dbo.ProductionPlanDay WHERE MTransNo = @Id GROUP BY JobSeq, LineSlNo, PlanDate",
                         Int("@Id", planId)))
            {
                var k = (I(r, "seq"), I(r, "line"));
                if (!map.TryGetValue(k, out var m)) map[k] = m = new Dictionary<string, double>();
                m[S(r, "d")] = Dbl(r, "q");
            }
            return map;
        }

        /// <summary>A plan's month, weeks (Open / Firm / Released) and working dates.</summary>
        protected static object PeriodsOf(PlanHead p, CalendarInfo? info) => new
        {
            planId = p.MTransNo, planNo = p.PlanNo, planMonth = p.PlanMonth, planDate = p.PlanDate, revNo = p.RevNo,
            status = p.PlanStatus, monthStart = p.PlanMonth + "-01", monthEnd = LastOfMonth(p.PlanMonth),
            dates = info?.Dates ?? new List<string>(),
            weeks = (info?.Weeks ?? new List<Week>()).Select(w => new
            {
                weekNo = w.WeekNo, label = w.Label, from = w.From, to = w.To, dates = w.Dates,
                status = p.Weeks.TryGetValue(w.WeekNo, out var st) ? st : "Open",
            }).ToList(),
        };

        protected static (string From, string To) RangeOf(PlanHead p, CalendarInfo? info, string? period, int weekNo, string? date)
        {
            if (period == "Week")
            {
                var w = info?.Weeks.FirstOrDefault(x => x.WeekNo == weekNo);
                return w == null ? ("", "") : (w.From, w.To);
            }
            if (period == "Day") return (date ?? "", date ?? "");
            return (p.PlanMonth + "-01", LastOfMonth(p.PlanMonth));
        }

        // ================================================================
        // LINE FACTS — one row per MAIN plan line, day by day
        // ================================================================

        protected sealed class LineFact
        {
            public int Seq, LineNo, PartNo, MachineNo, OAId, RouteNo, DieNo; public string PartName = "", DieName = "", Hammer = "", Source = "",
                OANo = "", OAText = "", Customer = "", Grade = "", BarText = "", BarKey = "", DeliveryDate = "", JobStart = "", JobEnd = "",
                RouteName = "", JobCardNo = "", Remarks = "", ForgeBy = "", OAKey = "";
            public double Rate, CutWt, PlanQty; public bool Overflow, ShortClosed, CustomerMaterial;
            public BarRow Bar = new();
            public Dictionary<string, double> Planned = new(), Forged = new(), Rejected = new();
        }

        protected static double SumBefore(Dictionary<string, double> map, string date) =>
            map.Where(kv => string.CompareOrdinal(kv.Key, date) < 0).Sum(kv => kv.Value);

        protected static double SumBetween(Dictionary<string, double> map, string from, string to) =>
            map.Where(kv => string.CompareOrdinal(kv.Key, from) >= 0 && string.CompareOrdinal(kv.Key, to) <= 0).Sum(kv => kv.Value);

        protected async Task<List<LineFact>> LineFacts(PlanHead plan, PlanningData data)
        {
            var days = await StoredDays(plan.MTransNo);
            var facts = new List<LineFact>();
            foreach (var j in plan.Jobs.Where(x => x.PlanType == "Main"))
            {
                var jobDates = j.Lines.SelectMany(l => days.TryGetValue((j.Seq, l.SlNo), out var m) ? m.Keys : Enumerable.Empty<string>())
                                      .OrderBy(x => x, StringComparer.Ordinal).ToList();
                foreach (var l in j.Lines)
                {
                    var o = l.Source == "OA" ? data.OALine(l.OAYear, l.OANo, l.OAID) : null;
                    var jc = data.JobCardOf(plan.MTransNo, j.Seq, l.SlNo);
                    var bar = data.BarOf(j.PartNo, l.Grade);
                    var route = l.RouteNo > 0 && data.Routes.TryGetValue(l.RouteNo, out var rr) ? rr : data.RouteFor(j.PartNo, l.Grade, j.MachineNo);
                    var planned = days.TryGetValue((j.Seq, l.SlNo), out var pm) ? pm : new Dictionary<string, double>();
                    facts.Add(new LineFact
                    {
                        Seq = j.Seq, LineNo = l.SlNo, PartNo = j.PartNo, PartName = data.PartName(j.PartNo),
                        DieNo = j.DieNo, DieName = data.Dies.TryGetValue(j.DieNo, out var die) ? die.Name : "",
                        MachineNo = j.MachineNo, Hammer = data.HammerName(j.MachineNo), Source = l.Source,
                        OANo = o?.Label ?? (l.Source == "OA" ? OALabel(l.OAYear, l.OANo) : ""), OAId = l.OAID,
                        OAKey = l.Source == "OA" ? l.OAYear + "/" + l.OANo + "/" + l.OAID : "",
                        OAText = l.Source == "OA" ? (o?.Label ?? OALabel(l.OAYear, l.OANo)) + " · " + l.OAID : "Stock · " + l.Reason,
                        Customer = o?.Customer ?? (l.Source == "OA" ? "" : (string.IsNullOrEmpty(l.Reason) ? "Stock" : l.Reason)),
                        Grade = l.Grade, Bar = bar, BarText = bar.Text, BarKey = bar.Key,
                        DeliveryDate = o?.PlanDate ?? "", Rate = o?.Rate ?? l.Rate, CutWt = bar.CutWt, PlanQty = l.PlanQty,
                        JobStart = jobDates.FirstOrDefault() ?? "", JobEnd = jobDates.LastOrDefault() ?? "",
                        RouteNo = route?.RouteNo ?? l.RouteNo, RouteName = route?.Name ?? "",
                        ForgeBy = o != null ? ForgeBy(o.PlanDate, route?.PostForgeDays ?? 0) : "",
                        JobCardNo = jc?.No ?? "", CustomerMaterial = o?.CustomerMaterial ?? false,
                        Planned = planned,
                        Forged = jc?.ForgedByDate ?? new Dictionary<string, double>(),
                        Rejected = jc?.RejectedByDate ?? new Dictionary<string, double>(),
                    });
                }
            }
            return facts;
        }

        // ================================================================
        // ORDERS — pending to forge, held by live plans
        // ================================================================

        /// <summary>Good forged qty on all job cards of an OA line (all dates).</summary>
        protected static double GoodForged(PlanningData data, OALineRow o) =>
            data.JobCards.Where(j => j.OAYear == o.OAYear && j.OANo == o.OANo && j.OAID == o.OAID)
                         .Sum(j => Math.Max(0, j.Forged - j.Rejected));

        protected static double PendingOf(PlanningData data, OALineRow o) =>
            Math.Max(0, o.Qty - Math.Max(o.Dispatched, GoodForged(data, o)));

        protected static bool IsLivePlan(PlanHead p) =>
            p.PlanStatus != "Closed" && string.CompareOrdinal(LastOfMonth(p.PlanMonth), Today) >= 0;

        /// <summary>What live plans' MAIN jobs still hold of each OA line: plan qty less forged on its job card.</summary>
        protected static Dictionary<string, (double Qty, List<string> Plans)> PlannedByOa(List<PlanHead> plans, PlanningData data, int excludePlanId)
        {
            var held = new Dictionary<string, (double Qty, List<string> Plans)>();
            foreach (var p in plans.Where(p => p.MTransNo != excludePlanId && IsLivePlan(p)))
                foreach (var j in p.Jobs.Where(j => j.PlanType == "Main"))
                    foreach (var l in j.Lines.Where(l => l.Source == "OA"))
                    {
                        var jc = data.JobCardOf(p.MTransNo, j.Seq, l.SlNo);
                        var left = Math.Max(0, l.PlanQty - (jc?.Forged ?? 0));
                        var k = l.OAYear + "/" + l.OANo + "/" + l.OAID;
                        if (!held.TryGetValue(k, out var h)) h = (0, new List<string>());
                        if (!h.Plans.Contains(p.PlanNo)) h.Plans.Add(p.PlanNo);
                        held[k] = (h.Qty + left, h.Plans);
                    }
            return held;
        }

        /// <summary>First and last planned day of each OA line on live plans (stored days).</summary>
        protected async Task<Dictionary<string, (string From, string To)>> PlannedDatesByOa(List<PlanHead> plans)
        {
            var map = new Dictionary<string, (string From, string To)>();
            foreach (var p in plans.Where(IsLivePlan))
            {
                var days = await StoredDays(p.MTransNo);
                foreach (var j in p.Jobs.Where(j => j.PlanType == "Main"))
                    foreach (var l in j.Lines.Where(l => l.Source == "OA"))
                    {
                        if (!days.TryGetValue((j.Seq, l.SlNo), out var m) || m.Count == 0) continue;
                        var k = l.OAYear + "/" + l.OANo + "/" + l.OAID;
                        var from = m.Keys.Min(StringComparer.Ordinal)!;
                        var to = m.Keys.Max(StringComparer.Ordinal)!;
                        map[k] = map.TryGetValue(k, out var x)
                            ? (string.CompareOrdinal(from, x.From) < 0 ? from : x.From, string.CompareOrdinal(to, x.To) > 0 ? to : x.To)
                            : (from, to);
                    }
            }
            return map;
        }

        // ================================================================
        // MATERIAL — allocation of bar to lines (stock, reserved, POs)
        // ================================================================

        protected sealed class NeedLine
        {
            public int SeqNo, JobSeq; public string OAKey = "", Ref = "", OANo = "", PlanNo = "", SoDate = "", Customer = "",
                PartName = "", Grade = "", RMText = "", BarKey = "", DeliveryDate = "", NeededBy = "", PoRefs = "", Status = "";
            public int RMPartNo, RMGradeNo; public double Dia, PendingQty, PlanQty, CutWt, Rate, ReqKg, FromStockKg, FromPoKg, PoLateKg, ShortKg;
            public bool Released, JobWork, Planned;

            public Dictionary<string, object?> ToRow() => new()
            {
                ["oaNo"] = OANo, ["planNo"] = PlanNo, ["jobSeq"] = JobSeq, ["planJob"] = Planned ? PlanNo + " · Job " + JobSeq : "",
                ["soDate"] = SoDate, ["customer"] = Customer, ["partName"] = PartName, ["grade"] = Grade, ["dia"] = Dia,
                ["rmText"] = RMText, ["rmPartNo"] = RMPartNo, ["rmGradeNo"] = RMGradeNo, ["deliveryDate"] = DeliveryDate,
                ["neededBy"] = NeededBy, ["pendingQty"] = PendingQty, ["planQty"] = PlanQty, ["cutWt"] = CutWt, ["rate"] = Rate,
                ["pendingValue"] = R2(PendingQty * Rate), ["reqKg"] = ReqKg, ["fromStockKg"] = FromStockKg, ["fromPoKg"] = FromPoKg,
                ["poLateKg"] = PoLateKg, ["shortKg"] = ShortKg, ["status"] = Status, ["poRefs"] = PoRefs, ["jobWork"] = JobWork,
            };
        }

        protected sealed class OpenPO { public string PoNo = "", Supplier = "", DueDate = ""; public double BalKg; }

        protected async Task<Dictionary<string, double>> StockAt(int subscID, string asOn)
        {
            var map = new Dictionary<string, double>();
            foreach (var r in await Query(@"
                SELECT PartNo AS partNo, LTRIM(RTRIM(ISNULL(Grade, ''))) AS grade, SUM(ISNULL(InQty, 0) - ISNULL(OutQty, 0)) AS bal
                  FROM dbo.VW_StoreStock WHERE SubscID = @SubscID AND (@AsOn = '' OR DocDate <= @AsOn)
                 GROUP BY PartNo, LTRIM(RTRIM(ISNULL(Grade, '')))", Int("@SubscID", subscID), Str("@AsOn", asOn ?? "", 10)))
                map[I(r, "partNo") + "|" + S(r, "grade")] = Dbl(r, "bal");
            return map;
        }

        protected async Task<Dictionary<string, List<OpenPO>>> OpenPOs(int subscID, string asOn)
        {
            var map = new Dictionary<string, List<OpenPO>>();
            foreach (var r in await Query(@"
                SELECT PartNo AS partNo, LTRIM(RTRIM(ISNULL(Grade, ''))) AS grade,
                       CONCAT(PODocYear, '/', PODocNo) AS poNo, ISNULL(VendorName, '') AS supplier,
                       CONVERT(char(10), DeleveryDate, 23) AS dueDate, BalanceQty AS bal
                  FROM dbo.VW_POPendingGRN
                 WHERE SubscID = @SubscID AND BalanceQty > 0 AND (@AsOn = '' OR PODocDate <= @AsOn)
                 ORDER BY DeleveryDate, PODocNo", Int("@SubscID", subscID), Str("@AsOn", asOn ?? "", 10)))
            {
                var k = I(r, "partNo") + "|" + S(r, "grade");
                if (!map.TryGetValue(k, out var list)) map[k] = list = new List<OpenPO>();
                list.Add(new OpenPO { PoNo = S(r, "poNo"), Supplier = S(r, "supplier"), DueDate = S(r, "dueDate"), BalKg = Dbl(r, "bal") });
            }
            return map;
        }

        /// <summary>
        /// Gives each bar's stock, then its open POs, to the lines in the order
        /// given (port of the store's allocate()). Stock still owed to open job
        /// cards (RESERVED) goes only to lines with a job card on the floor; the
        /// rest is FREE. PO kg arriving after a line's needed-by date is LATE.
        /// </summary>
        protected async Task<List<Dictionary<string, object?>>> Allocate(PlanningData data, List<NeedLine> lines, string asOn)
        {
            var stock = await StockAt(data.SubscID, asOn);
            var pos = await OpenPOs(data.SubscID, asOn);
            var reserved = data.JobCards.Where(j => j.Status != "Completed" && j.RMBalance > 0)
                .GroupBy(j => j.RMPartNo + "|" + j.RMGrade).ToDictionary(g => g.Key, g => g.Sum(j => j.RMBalance));

            var groups = new List<Dictionary<string, object?>>();
            foreach (var g in lines.GroupBy(l => l.BarKey))
            {
                var key = g.Key;
                var first = g.First();
                var stockKg = R2(stock.TryGetValue(key, out var st) ? st : 0);
                var reservedKg = R2(Math.Min(Math.Max(0, stockKg), reserved.TryGetValue(key, out var rv) ? rv : 0));
                var freeKg = R2(stockKg - reservedKg);
                var poList = (pos.TryGetValue(key, out var pl) ? pl : new List<OpenPO>())
                    .Select(x => new OpenPO { PoNo = x.PoNo, Supplier = x.Supplier, DueDate = x.DueDate, BalKg = x.BalKg }).ToList();
                var poKg = R2(poList.Sum(x => x.BalKg));
                double reservedLeft = reservedKg, freeLeft = Math.Max(0, freeKg), gross = 0, lateKg = 0;
                var shorts = new List<string>();
                foreach (var l in g)
                {
                    gross += l.ReqKg;
                    var fromReserved = l.Released ? R2(Math.Min(l.ReqKg, reservedLeft)) : 0;
                    reservedLeft = R2(reservedLeft - fromReserved);
                    var fromFree = R2(Math.Min(l.ReqKg - fromReserved, freeLeft));
                    freeLeft = R2(freeLeft - fromFree);
                    l.FromStockKg = R2(fromReserved + fromFree);
                    var rest = R2(l.ReqKg - l.FromStockKg);
                    var refs = new List<string>();
                    foreach (var po in poList)
                    {
                        if (rest <= 0 || po.BalKg <= 0) continue;
                        var take = R2(Math.Min(rest, po.BalKg));
                        po.BalKg = R2(po.BalKg - take);
                        rest = R2(rest - take);
                        l.FromPoKg = R2(l.FromPoKg + take);
                        if (!string.IsNullOrEmpty(l.NeededBy) && string.CompareOrdinal(po.DueDate, l.NeededBy) > 0) l.PoLateKg = R2(l.PoLateKg + take);
                        refs.Add(po.PoNo + " due " + po.DueDate);
                    }
                    l.PoRefs = string.Join(", ", refs);
                    l.ShortKg = R2(rest);
                    lateKg += l.PoLateKg;
                    l.Status = l.ShortKg > 0 ? (l.FromStockKg + l.FromPoKg > 0 ? "Partial" : "Shortage")
                        : l.PoLateKg > 0 ? "PO Late" : l.FromPoKg > 0 ? "On Order" : "Available";
                    if (l.ShortKg > 0 && !string.IsNullOrEmpty(l.Ref) && !shorts.Contains(l.Ref)) shorts.Add(l.Ref);
                }
                gross = R2(gross);
                var shortKg = R2(Math.Max(0, gross - stockKg - poKg));
                var srcPos = pos.TryGetValue(key, out var allPo) ? allPo : new List<OpenPO>();
                groups.Add(new Dictionary<string, object?>
                {
                    ["grade"] = first.Grade, ["dia"] = first.Dia, ["rmText"] = first.RMText, ["partNo"] = first.RMPartNo,
                    ["gradeNo"] = first.RMGradeNo, ["lines"] = g.Count(), ["grossKg"] = gross, ["stockKg"] = stockKg,
                    ["reservedKg"] = reservedKg, ["freeKg"] = freeKg, ["poKg"] = poKg, ["poLateKg"] = R2(lateKg),
                    ["poRef"] = string.Join(", ", srcPos.Select(x => x.Supplier + " (PO " + x.PoNo + ", due " + x.DueDate + ")")),
                    ["shortKg"] = shortKg,
                    ["status"] = shortKg > 0 ? "Shortage" : lateKg > 0 ? "PO Late" : gross <= stockKg ? "Available" : "Partial",
                    ["suppliers"] = string.Join(", ", srcPos.Select(x => x.Supplier).Where(x => x != "").Distinct()),
                    ["oaRefs"] = string.Join(", ", shorts),
                    ["neededBy"] = g.Where(l => l.NeededBy != "").Select(l => l.NeededBy).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "",
                });
            }
            return groups.OrderBy(x => (string?)x["grade"], StringComparer.Ordinal).ThenBy(x => (double)x["dia"]!).ToList();
        }

        /// <summary>Every pending OA line with its bar need, allocated (Pending basis).</summary>
        protected async Task<(List<Dictionary<string, object?>> Groups, List<NeedLine> Lines)> PendingAllocation(
            PlanningData data, List<PlanHead> plans, string asOn)
        {
            var planned = await PlannedDatesByOa(plans);
            var lines = new List<NeedLine>();
            var jobWork = new List<NeedLine>();
            var seq = 0;
            foreach (var o in data.OALines.Values.OrderBy(x => x.OADate, StringComparer.Ordinal).ThenBy(x => x.OANo).ThenBy(x => x.OAID))
            {
                seq++;
                if (!string.IsNullOrEmpty(asOn) && string.CompareOrdinal(o.OADate, asOn) > 0) continue;
                var pending = PendingOf(data, o);
                if (pending <= 0) continue;
                var bar = data.BarOf(o.PartNo, o.Grade);
                var l = new NeedLine
                {
                    SeqNo = seq, OAKey = o.Key, Ref = o.Label, OANo = o.Label, SoDate = o.OADate, Customer = o.Customer,
                    PartName = data.PartName(o.PartNo), Grade = bar.RMGrade, Dia = bar.Dia, RMText = bar.Text, BarKey = bar.Key,
                    RMPartNo = bar.RMPartNo, RMGradeNo = bar.RMGradeNo, DeliveryDate = o.PlanDate, PendingQty = pending,
                    CutWt = bar.CutWt, Rate = o.Rate, ReqKg = o.CustomerMaterial ? 0 : R2(pending * bar.CutWt),
                    JobWork = o.CustomerMaterial,
                    Released = data.JobCards.Any(j => j.OAYear == o.OAYear && j.OANo == o.OANo && j.OAID == o.OAID && j.Status != "Completed"),
                    NeededBy = planned.TryGetValue(o.Key, out var pd) ? pd.From : "",
                };
                if (l.JobWork) { l.Status = "Customer Material"; jobWork.Add(l); }
                else if (bar.RMPartNo <= 0) { l.Status = "No BOM"; jobWork.Add(l); }
                else lines.Add(l);
            }
            lines = lines.OrderBy(l => l.Released ? 0 : 1).ThenBy(l => l.DeliveryDate, StringComparer.Ordinal).ThenBy(l => l.SeqNo).ToList();
            var groups = await Allocate(data, lines, asOn);
            return (groups, lines.Concat(jobWork).ToList());
        }

        /// <summary>Material status of every pending OA line, by OA key — what the planner sees.</summary>
        protected async Task<Dictionary<string, string>> MaterialStatusMap(PlanningData data, List<PlanHead> plans)
        {
            var (_, lines) = await PendingAllocation(data, plans, Today);
            return lines.ToDictionary(l => l.OAKey, l => l.Status == "Shortage" ? "Not Available" : l.Status == "PO Late" ? "On Order" : l.Status);
        }
    }
}
