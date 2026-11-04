using EMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EMSAPI.Controllers
{
    /// <summary>
    /// Bulk import of parts and their material grades from the Part Data
    /// Import page. Same design as CustomerDataImportController, except that
    /// the Part Code comes from the file (it is NOT generated) and is the key
    /// the Material Grades sheet links by.
    ///
    /// Validate and Import run one pipeline; Validate stops before writing.
    /// Import is all-or-nothing. Inside the transaction:
    ///   1. lookup values the file uses but the masters lack - Category, Sub
    ///      Category, Inventory Group, UOM, Store Location (Other Master), HSN
    ///      codes (HSN Master) and grades (Material Standard)
    ///   2. parts       (SP_PartMaster)
    ///   3. grades      (SP_MaterialGrade)
    ///
    /// Tax % is checked against the rates in Tax Master but never creates one:
    /// a tax rate carries accounting set-up the import cannot guess.
    ///
    /// A grade's GradeNo is the MaterialStd row id, the same value the Part
    /// Master page stores, so imported grades open correctly there.
    /// </summary>
    [Route("api/[controller]")]
    public class PartDataImportController : EmsControllerBase
    {
        private readonly EMSContext db;

        public PartDataImportController(EMSContext context) => db = context;

        // Other Master types PartMaster.html reads its dropdowns from.
        private const string CategoryType = "Part Category";
        private const string SubCategoryType = "Part Sub Category";
        private const string InventoryGroupType = "Inventory Group";
        private const string UnitType = "UOM";
        private const string StoreLocationType = "Store Location";
        private const string LookupDepartment = "Inventory";

        private static readonly string[] OtherTypes =
            { CategoryType, SubCategoryType, InventoryGroupType, UnitType, StoreLocationType };

        private const int MaxRows = 10000;

        private const string SheetParts = "Parts";
        private const string SheetGrades = "Material Grades";

        // Closed lists - the same values the Part Master page offers.
        private static readonly string[] PartTypes = { "BOUGHTOUT", "RAW MATERIAL", "COMPONENT", "CONSUMABLE", "DIE" };
        private static readonly string[] PartClasses = { "A", "B", "C" };

        private static readonly Regex Spaces = new(@"\s+");

        //======================================================================
        // Endpoints
        //======================================================================

        /// <summary>Current master values, for the template's "Lists" sheet.</summary>
        [HttpGet("GetLookups/{subscID}")]
        public async Task<IActionResult> GetLookups(int subscID)
        {
            if (subscID <= 0)
                return BadRequestResult("Invalid Subscription.");

            try
            {
                var others = await db.OtherMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && OtherTypes.Contains(x.MasterType!))
                    .OrderBy(x => x.MasterName)
                    .Select(x => new { x.MasterType, x.MasterName })
                    .ToListAsync();

                var hsn = await db.HSNMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.HSNCode)
                    .Select(x => new { x.HSNCode, x.HSNDescription })
                    .ToListAsync();

                var taxRates = await db.TaxMaster.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .Select(x => x.TaxP).Distinct().OrderBy(x => x)
                    .ToListAsync();

                var grades = await db.MaterialStd.AsNoTracking()
                    .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                    .OrderBy(x => x.Grade).ThenBy(x => x.MatrStd)
                    .Select(x => new { x.Grade, x.MatrStd })
                    .ToListAsync();

                IEnumerable<string?> Of(string t) => others.Where(x => x.MasterType == t).Select(x => x.MasterName);

                return OkResult(new
                {
                    Categories = Of(CategoryType),
                    SubCategories = Of(SubCategoryType),
                    InventoryGroups = Of(InventoryGroupType),
                    Units = Of(UnitType),
                    StoreLocations = Of(StoreLocationType),
                    HsnCodes = hsn,
                    TaxRates = taxRates,
                    Grades = grades,
                    PartTypes,
                    PartClasses,
                    TxnUnits = new[] { "P", "S" },
                    YesNo = new[] { "Y", "N" },
                    StatusValues = new[] { "Active", "Inactive" }
                });
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        /// <summary>Dry run: every check the import makes, nothing written.</summary>
        [HttpPost("Validate")]
        public Task<IActionResult> Validate([FromBody] PartImportRequest req) => Run(req, dryRun: true);

        [HttpPost("Import")]
        public Task<IActionResult> Import([FromBody] PartImportRequest req) => Run(req, dryRun: false);

        //======================================================================
        // Pipeline
        //======================================================================

        private async Task<IActionResult> Run(PartImportRequest req, bool dryRun)
        {
            if (req == null)
                return BadRequestResult("Invalid request.");
            if (req.SubscID <= 0 || req.CreatedBy <= 0)
                return BadRequestResult("Invalid input parameters.");

            var type = (req.ImportType ?? "ALL").Trim().ToUpperInvariant();
            if (type is not ("ALL" or "PART" or "GRADE"))
                return BadRequestResult("Import Type must be ALL, PART or GRADE.");

            if (type is not ("ALL" or "PART")) req.Parts = new();
            if (type is not ("ALL" or "GRADE")) req.Grades = new();

            var total = req.Parts.Count + req.Grades.Count;
            if (total == 0)
                return BadRequestResult("The file has no data rows for the selected import type.");
            if (total > MaxRows)
                return BadRequestResult($"At most {MaxRows} rows can be imported at once; this file has {total}. Split it.");

            try
            {
                var plan = new ImportPlan(req, await LookupCache.LoadAsync(db, req.SubscID));

                PlanParts(plan);
                PlanGrades(plan);

                var report = plan.BuildReport(dryRun);

                const string nothingToDo = "Nothing to import - every row already exists. Choose 'Update' to overwrite them.";

                if (dryRun)
                    return OkResult(report,
                        report.CanImport ? "Validation passed. The file is ready to import."
                        : report.ErrorCount == 0 ? nothingToDo
                        : $"Validation found {report.ErrorCount} error(s). Fix them and validate again.");

                if (!report.CanImport)
                    return Conflict(Envelope(false,
                        report.ErrorCount == 0 ? nothingToDo
                        : $"Import blocked: {report.ErrorCount} error(s) in the file. Nothing was imported.",
                        report, 409));

                return await Execute(plan, report);
            }
            catch (Exception ex)
            {
                return ErrorResult(ex);
            }
        }

        private static object Envelope(bool success, string message, object? data, int status) =>
            new { success, message, data, statusCode = status };

        //======================================================================
        // Execute
        //======================================================================

        private async Task<IActionResult> Execute(ImportPlan plan, PartImportReport report)
        {
            var ip = ClientIp;
            var req = plan.Request;
            var lk = plan.Lookups;

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                // 1. Masters -------------------------------------------------
                foreach (var m in plan.MastersToCreate)
                {
                    (int ID, string Message) r;
                    if (m.Master == "HSN Code")
                    {
                        r = await db.CallHSNMasterSP(new SP_HSNMaster
                        {
                            HSNCode = m.Value,
                            SystemIP = ip,
                            LockStatus = "Y",
                            CreatedBy = req.CreatedBy,
                            SubscID = req.SubscID,
                            Status = "Insert"
                        });
                    }
                    else if (m.Master == "Grade")
                    {
                        var std = plan.StdOf[m];
                        r = await db.CallMaterialStdSP(new SP_MaterialStd
                        {
                            Grade = std.Grade,
                            MatrStd = std.Std,
                            SystemIP = ip,
                            LockStatus = "Y",
                            CreatedBy = req.CreatedBy,
                            SubscID = req.SubscID,
                            Status = "Insert"
                        });
                        if (r.ID > 0) std.Id = r.ID;
                    }
                    else
                    {
                        var masterType = OtherTypeOf(m.Master);
                        await EnsureMasterTypeListed(masterType, req.SubscID);
                        r = await db.CallOtherMasterSP(new SP_OtherMaster
                        {
                            MasterType = masterType,
                            MasterName = m.Value,
                            Department = LookupDepartment,
                            SystemIP = ip,
                            LockStatus = "Y",
                            CreatedBy = req.CreatedBy,
                            SubscID = req.SubscID,
                            Status = "Insert"
                        });
                        // CatNo / SubCatNo / GroupNo carry this id.
                        if (r.ID > 0) lk.Others[masterType][Key(m.Value)] = (m.Value, r.ID);
                    }

                    if (r.ID <= 0)
                        throw new ImportAbort("Masters", 0, $"{m.Master} '{m.Value}' could not be created: {r.Message}");
                    m.Created = true;
                }

                // 2. Parts --------------------------------------------------
                foreach (var p in plan.Parts.Where(x => x.Action is "New" or "Update"))
                {
                    var dto = p.Build!();
                    dto.SystemIP = ip;
                    var r = await AtRow(SheetParts, p.RowNo, () => db.CallPartMasterSP(dto));
                    if (r.ID <= 0)
                        throw new ImportAbort(SheetParts, p.RowNo, r.Message);

                    p.MTransNo = r.ID;
                    p.Result.Message = p.Action == "New" ? "Created." : "Updated.";
                }

                // 3. Grades -------------------------------------------------
                foreach (var g in plan.Grades.Where(x => x.Action is "New" or "Update"))
                {
                    var partNo = g.Parent.MTransNo;
                    if (partNo <= 0)
                        throw new ImportAbort(SheetGrades, g.RowNo, $"Part '{g.Result.PartCode}' was not saved.");

                    var r = await AtRow(SheetGrades, g.RowNo, () => g.Execute!(partNo, ip));
                    if (r.ID <= 0)
                        throw new ImportAbort(SheetGrades, g.RowNo, r.Message);

                    g.Result.Message = g.Action == "New" ? "Created." : "Updated.";
                }

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                // A procedure that rolled back on its own has already ended the
                // transaction; rolling back again would throw and hide the cause.
                try { await tx.RollbackAsync(); } catch { /* already rolled back */ }

                var abort = ex as ImportAbort;
                var sheet = abort?.Sheet ?? "Import";
                var row = abort?.RowNo ?? 0;
                var message = abort?.Message ?? GetFullExceptionMessage(ex);

                report.Imported = false;
                report.CanImport = false;
                report.ErrorCount++;
                report.Issues.Insert(0, new CustomerImportIssue
                {
                    Sheet = sheet, RowNo = row, Level = "Error", Field = "", Message = message
                });
                foreach (var m in report.MastersToCreate) m.Created = false;
                foreach (var res in report.Results)
                {
                    if (res.Sheet == sheet && res.RowNo == row) { res.Action = "Error"; res.Message = message; }
                    else if (res.Action != "Skip") res.Message = "Rolled back - nothing was imported.";
                }

                var where = row > 0 ? $"{sheet} row {row}" : sheet;
                return Conflict(Envelope(false, $"Import failed at {where}: {message} Nothing was imported.", report, 409));
            }

            report.Imported = true;
            report.DryRun = false;

            var done = report.Summary.Sum(s => s.New + s.Update);
            var created = report.MastersToCreate.Count;
            return OkResult(report,
                $"Import complete: {done} row(s) saved" +
                (created > 0 ? $", {created} master value(s) created." : "."));
        }

        private static string OtherTypeOf(string master) => master switch
        {
            "Category" => CategoryType,
            "Sub Category" => SubCategoryType,
            "Inventory Group" => InventoryGroupType,
            "Unit" => UnitType,
            "Store Location" => StoreLocationType,
            _ => throw new InvalidOperationException("Unknown master " + master)
        };

        /// <summary>Runs one procedure call and pins any exception to its sheet row.</summary>
        private static async Task<(int ID, string Message)> AtRow(
            string sheet, int rowNo, Func<Task<(int ID, string Message)>> call)
        {
            try
            {
                return await call();
            }
            catch (Exception ex) when (ex is not ImportAbort)
            {
                throw new ImportAbort(sheet, rowNo, GetFullExceptionMessage(ex));
            }
        }

        /// <summary>The Other Master page only offers types listed in OtherMasterList.</summary>
        private async Task EnsureMasterTypeListed(string masterType, int subscID)
        {
            var listed = await db.OtherMasterList.AsNoTracking()
                .AnyAsync(x => x.MasterType == masterType && x.SubscID == subscID && x.DeleteStatus == "N");
            if (listed) return;

            var r = await db.CallOtherMasterListSP(new SP_OtherMasterList
            {
                MasterType = masterType,
                Department = LookupDepartment,
                SubscID = subscID,
                Status = "Insert"
            });
            if (r.ID <= 0)
                throw new ImportAbort("Masters", 0, $"Other Master type '{masterType}' could not be registered: {r.Message}");
        }

        //======================================================================
        // Planning - Parts
        //======================================================================

        private static void PlanParts(ImportPlan p)
        {
            const string S = SheetParts;
            var req = p.Request;
            var lk = p.Lookups;

            foreach (var r in req.Parts)
            {
                var errs = p.ErrorCount;
                var code = Clean(r.PartCode);
                var plan = new PartPlan { RowNo = r.RowNo };
                plan.Result = new PartImportResult { Sheet = S, RowNo = r.RowNo, PartCode = code ?? "", PartName = Name(r.PartName) };
                p.Parts.Add(plan);

                if (code == null)
                {
                    p.Error(S, r.RowNo, "Part Code", "Part Code is required - it is the part's key and is not generated.");
                    plan.Fail("Part Code is required.");
                    continue;
                }
                p.Length(S, r.RowNo, "Part Code", code, 30);

                if (p.PartsInFile.TryGetValue(Key(code), out var first))
                {
                    p.Error(S, r.RowNo, "Part Code", $"'{code}' appears again - first on row {first.RowNo}. Each part once.");
                    plan.Fail("Duplicate in file.");
                    continue;
                }
                p.PartsInFile[Key(code)] = plan;

                lk.Parts.TryGetValue(Key(code), out var ex);
                plan.Existing = ex;

                if (ex != null && !p.UpdateExisting)
                {
                    plan.Action = "Skip";
                    plan.MTransNo = ex.MTransNo;
                    plan.Result.PartName ??= ex.PartName;
                    plan.Result.Action = "Skip";
                    plan.Result.Message = "Already in Part Master - skipped.";
                    continue;
                }

                var isNew = ex == null;

                // Name
                var name = Name(r.PartName) ?? ex?.PartName;
                if (name == null) p.Error(S, r.RowNo, "Part Name", "Part Name is required.");
                p.Length(S, r.RowNo, "Part Name", name, 250);

                // HSN - created in HSN Master when missing
                var hsn = ex?.HSNCode;
                if (Upper(r.HSNCode) is { } h)
                    hsn = p.ResolveHsn(S, r.RowNo, h);

                // Tax % - must be a Tax Master rate
                decimal? taxP = ex?.TaxP;
                if (Clean(r.TaxP) is { } taxText)
                {
                    var t = taxText.TrimEnd('%').Trim();
                    if (!TryDec(t, out var tv) || tv < 0 || tv > 100)
                        p.Error(S, r.RowNo, "Tax %", $"'{taxText}' must be a number between 0 and 100.");
                    else if (lk.TaxRates.Count > 0 && !lk.TaxRates.Contains(Math.Round(tv, 2)))
                        p.Error(S, r.RowNo, "Tax %", $"{tv}% is not a Tax Master rate. Use one of: {string.Join(", ", lk.TaxRates.Select(x => x.ToString("0.##")))}.");
                    else taxP = Math.Round(tv, 2);
                }
                else if (isNew)
                    p.Error(S, r.RowNo, "Tax %", "Tax % is required.");

                // Flags
                var sPart = p.ParseYesNo(S, r.RowNo, "Sale Part", r.SPart) ?? ex?.SPart ?? "N";
                var pPart = p.ParseYesNo(S, r.RowNo, "Purchase Part", r.PPart) ?? ex?.PPart ?? "N";
                var mPart = p.ParseYesNo(S, r.RowNo, "Manufactured Part", r.MPart) ?? ex?.MPart ?? "N";

                // Classification - Other Master, created when missing
                var category = p.ResolveOther(S, r.RowNo, "Category", CategoryType, Name(r.Category)) ?? ex?.Category;
                var subCategory = p.ResolveOther(S, r.RowNo, "Sub Category", SubCategoryType, Name(r.SubCategory)) ?? ex?.SubCategory;
                var invGroup = p.ResolveOther(S, r.RowNo, "Inventory Group", InventoryGroupType, Name(r.InventoryGroup)) ?? ex?.InventoryGroup;

                string? partType = ex?.PartType;
                if (Clean(r.PartType) is { } pt)
                {
                    partType = Pick(pt, PartTypes);
                    if (partType == null)
                        p.Error(S, r.RowNo, "Part Type", $"'{pt}' is not valid. Use: {string.Join(", ", PartTypes)}.");
                }
                else if (isNew)
                    p.Error(S, r.RowNo, "Part Type", $"Part Type is required ({string.Join(", ", PartTypes)}).");

                string? partClass = ex?.PartClass;
                if (Clean(r.PartClass) is { } pc)
                {
                    partClass = Pick(pc, PartClasses);
                    if (partClass == null)
                        p.Error(S, r.RowNo, "Part Class", $"'{pc}' is not valid. Use: {string.Join(", ", PartClasses)}.");
                }

                // Units
                var primary = p.ResolveOther(S, r.RowNo, "Unit", UnitType, Name(r.PrimaryUnit), 10) ?? ex?.PrimaryUnit;
                if (primary == null && isNew)
                    p.Error(S, r.RowNo, "Primary Unit", "Primary Unit is required.");

                var secondary = p.ResolveOther(S, r.RowNo, "Unit", UnitType, Name(r.SecondaryUnit), 10)
                                ?? ex?.SecondaryUnit ?? primary;   // blank = same as primary

                decimal? ratio = ex?.ConversionRatio;
                if (Clean(r.ConversionRatio) is { } crText)
                {
                    if (TryDec(crText, out var cr) && cr > 0 && cr < 10000000000m) ratio = Math.Round(cr, 4);
                    else p.Error(S, r.RowNo, "Conversion Ratio", $"'{crText}' must be a number greater than 0.");
                }
                ratio ??= 1;
                if (primary != null && secondary != null && Key(primary) == Key(secondary) && ratio != 1)
                    p.Warn(S, r.RowNo, "Conversion Ratio", $"Primary and Secondary Unit are both {primary} but the ratio is {ratio}.");

                var txnUnit = ex?.TxnUnit ?? "P";
                if (Clean(r.TxnUnit) is { } tu)
                {
                    switch (tu.ToUpperInvariant())
                    {
                        case "P": case "PRIMARY": txnUnit = "P"; break;
                        case "S": case "SECONDARY": txnUnit = "S"; break;
                        default: p.Error(S, r.RowNo, "Txn Unit", $"'{tu}' is not valid. Use P (Primary) or S (Secondary)."); break;
                    }
                }

                // Storage
                var storeLoc = p.ResolveOther(S, r.RowNo, "Store Location", StoreLocationType, Name(r.StoreLocation)) ?? ex?.StoreLocation;
                var rack = Clean(r.Rack) ?? ex?.Rack;
                p.Length(S, r.RowNo, "Rack", rack, 10);
                var rackRow = Clean(r.RackRow) ?? ex?.RackRow;
                p.Length(S, r.RowNo, "Row", rackRow, 10);
                var rackCase = Clean(r.RackCase) ?? ex?.RackCase;
                p.Length(S, r.RowNo, "Case", rackCase, 10);

                // Dimensions
                var length = p.ParseQty(S, r.RowNo, "Length (mm)", r.PartLength, 2) ?? ex?.PartLength;
                var width = p.ParseQty(S, r.RowNo, "Width (mm)", r.PartWidth, 2) ?? ex?.PartWidth;
                var height = p.ParseQty(S, r.RowNo, "Height (mm)", r.PartHeight, 2) ?? ex?.PartHeight;
                var weight = p.ParseQty(S, r.RowNo, "Weight (Kg)", r.PartWeight, 3) ?? ex?.PartWeight;

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                if (p.ErrorCount > errs)
                {
                    plan.Fail("Row has errors.");
                    continue;
                }

                plan.Action = isNew ? "New" : "Update";
                plan.Result.PartName = name;
                plan.Result.Action = plan.Action;
                plan.Result.Message = isNew ? "Will be created." : "Will update the existing part.";

                // Built at import time: CatNo / SubCatNo / GroupNo are Other Master
                // ids, which new values only get once step 1 has created them.
                // An update starts from the stored row so fields this import does
                // not carry (prices, descriptions, drawing...) are kept.
                plan.Build = () => new SP_PartMaster
                {
                    MTransNo = ex?.MTransNo ?? 0,
                    MCount = isNew ? 0 : (ex!.LockCounter ?? 0) + 1,
                    PartCode = ex?.PartCode ?? code,
                    PartName = name!,
                    CodeDetails = ex?.CodeDetails,
                    PartDetails = ex?.PartDetails,
                    ShortDescription = ex?.ShortDescription,
                    FullDescription = ex?.FullDescription,
                    DrawingNo = ex?.DrawingNo,
                    DrgRevNo = ex?.DrgRevNo,
                    ColorCode = ex?.ColorCode,
                    CatNo = lk.OtherId(CategoryType, category),
                    SubCatNo = lk.OtherId(SubCategoryType, subCategory),
                    GroupNo = lk.OtherId(InventoryGroupType, invGroup),
                    Category = category,
                    SubCategory = subCategory,
                    InventoryGroup = invGroup,
                    Department = ex?.Department,
                    Brand = ex?.Brand,
                    PartType = partType,
                    PartClass = partClass,
                    HSNCode = hsn,
                    TaxP = taxP,
                    PrimaryUnit = primary,
                    SecondaryUnit = secondary,
                    ConversionRatio = ratio,
                    TxnUnit = txnUnit,
                    Packing = ex?.Packing,
                    PackQty = ex?.PackQty,
                    PackSize = ex?.PackSize,
                    SPart = sPart,
                    PPart = pPart,
                    MPart = mPart,
                    MinStock = ex?.MinStock,
                    MaxStock = ex?.MaxStock,
                    MOQ = ex?.MOQ,
                    ShelfLife = ex?.ShelfLife,
                    CostPrice = ex?.CostPrice,
                    SalesPrice = ex?.SalesPrice,
                    MRP = ex?.MRP,
                    MfrLicNo = ex?.MfrLicNo,
                    MfgStd = ex?.MfgStd,
                    StoreLocation = storeLoc,
                    Rack = rack,
                    RackRow = rackRow,
                    RackCase = rackCase,
                    PartLength = length,
                    PartWidth = width,
                    PartHeight = height,
                    PartWeight = weight,
                    AtchFileName = ex?.AtchFileName,
                    LockStatus = lockStatus,
                    CreatedBy = req.CreatedBy,
                    CreatedOn = DateTime.Now,
                    SubscID = req.SubscID,
                    Status = isNew ? "Insert" : "Update"
                };
            }
        }

        //======================================================================
        // Planning - Material Grades
        //======================================================================

        private static void PlanGrades(ImportPlan p)
        {
            const string S = SheetGrades;
            var req = p.Request;
            var seen = new Dictionary<string, int>();

            foreach (var r in req.Grades)
            {
                var errs = p.ErrorCount;
                var gradeText = Name(r.Grade);
                var g = p.NewGrade(r.RowNo, r.PartCode, gradeText);
                if (g == null) continue;

                if (gradeText == null) p.Error(S, r.RowNo, "Grade", "Grade is required.");

                // Which Material Standard row this grade is
                StdRef? std = null;
                if (gradeText != null)
                    std = p.ResolveStd(S, r.RowNo, gradeText, Name(r.MatrStd));

                GradeRow? ex = null;
                if (std != null)
                {
                    var k = g.Parent.Key + "|" + Key(std.Grade);
                    if (seen.TryGetValue(k, out var first))
                        p.Error(S, r.RowNo, "Grade", $"Grade '{std.Grade}' is already listed for this part on row {first}.");
                    else seen[k] = r.RowNo;

                    if (g.Parent.ExistingId > 0)
                        p.Lookups.Grades.TryGetValue(g.Parent.ExistingId + "|" + Key(std.Grade), out ex);
                }

                var min = p.ParseQty(S, r.RowNo, "Min Stock", r.MinStock, 2) ?? ex?.MinStock;
                var max = p.ParseQty(S, r.RowNo, "Max Stock", r.MaxStock, 2) ?? ex?.MaxStock;
                var moq = p.ParseQty(S, r.RowNo, "MOQ", r.MOQ, 2) ?? ex?.MOQ;
                if (min != null && max != null && max < min)
                    p.Error(S, r.RowNo, "Max Stock", $"Max Stock ({max}) cannot be less than Min Stock ({min}).");

                var lockStatus = p.ParseStatus(S, r.RowNo, r.Status) ?? ex?.LockStatus ?? "Y";

                if (!p.Settle(g, errs, ex != null)) continue;

                var transNo = ex?.MTransNo ?? 0;
                var keepGradeNo = ex != null && Clean(r.MatrStd) == null;   // no new standard given: keep the number
                var mCount = ex == null ? 0 : (ex.LockCounter ?? 0) + 1;
                g.Execute = (partNo, ip) => p.Db.CallMaterialGradeSP(new SP_MaterialGrade
                {
                    MTransNo = transNo,
                    MCount = mCount,
                    PartNo = partNo,
                    // Read at import time: a standard created in step 1 only has its id then.
                    GradeNo = keepGradeNo ? ex!.GradeNo : std!.Id,
                    Grade = std!.Grade,
                    MinStock = min,
                    MaxStock = max,
                    MOQ = moq,
                    SystemIP = ip,
                    LockStatus = lockStatus,
                    CreatedBy = req.CreatedBy,
                    SubscID = req.SubscID,
                    Status = transNo > 0 ? "Update" : "Insert"
                });
            }
        }

        //======================================================================
        // Text helpers
        //======================================================================

        private static string? Clean(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        private static string? Name(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v.Trim(), " ");

        private static string? Upper(string? v) =>
            string.IsNullOrWhiteSpace(v) ? null : Spaces.Replace(v, "").ToUpperInvariant();

        private static string Key(string v) => Spaces.Replace(v.Trim(), " ").ToUpperInvariant();

        private static string? Pick(string value, IEnumerable<string> allowed)
        {
            static string Flat(string s) => Regex.Replace(s, @"[\s\-_]", "").ToUpperInvariant();
            var v = Flat(value);
            return allowed.FirstOrDefault(a => Flat(a) == v);
        }

        private static bool TryDec(string text, out decimal value) =>
            decimal.TryParse(text.Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

        //======================================================================
        // Plan state
        //======================================================================

        private sealed class ImportAbort : Exception
        {
            public string Sheet { get; }
            public int RowNo { get; }
            public ImportAbort(string sheet, int rowNo, string message) : base(message)
            {
                Sheet = sheet;
                RowNo = rowNo;
            }
        }

        /// <summary>A Material Standard row; Id is 0 until a new one is created.</summary>
        private sealed class StdRef
        {
            public int Id;
            public string Grade = "";
            public string Std = "";
        }

        private sealed class PartPlan
        {
            public int RowNo;
            public string Action = "Error";
            public PartMaster? Existing;
            public Func<SP_PartMaster>? Build;
            public int MTransNo;
            public PartImportResult Result = null!;

            public void Fail(string message)
            {
                Action = "Error";
                Result.Action = "Error";
                Result.Message = message;
            }
        }

        /// <summary>The part a grade row hangs off - a row of this file, or one already saved.</summary>
        private sealed class ParentRef
        {
            public string Key = "";
            public PartPlan? Plan;
            public PartMaster? Existing;

            public int ExistingId => Plan?.Existing?.MTransNo ?? Existing?.MTransNo ?? 0;
            public int MTransNo => Plan?.MTransNo ?? Existing?.MTransNo ?? 0;
        }

        private sealed class GradePlan
        {
            public int RowNo;
            public string Action = "Error";
            public ParentRef Parent = null!;
            public Func<int, string?, Task<(int ID, string Message)>>? Execute;
            public PartImportResult Result = null!;
        }

        private sealed record GradeRow(int MTransNo, int GradeNo, decimal? MinStock, decimal? MaxStock,
            decimal? MOQ, string? LockStatus, int? LockCounter);

        /// <summary>Everything the checks compare against, read once per request.</summary>
        private sealed class LookupCache
        {
            public EMSContext Db = null!;
            // MasterType -> KEY(name) -> (stored name, Other Master id)
            public Dictionary<string, Dictionary<string, (string Name, int Id)>> Others = new();
            public Dictionary<string, string> Hsn = new();                    // KEY -> stored code
            public HashSet<decimal> TaxRates = new();
            public Dictionary<string, StdRef> StdByGradeStd = new();          // KEY(grade)|KEY(std)
            public Dictionary<string, StdRef> StdByGrade = new();             // KEY(grade) -> lowest id
            public Dictionary<string, PartMaster> Parts = new();              // KEY(code)
            public Dictionary<string, GradeRow> Grades = new();               // "PartNo|KEY(grade)"

            public int? OtherId(string masterType, string? name)
            {
                if (name == null) return 0;
                return Others[masterType].TryGetValue(Key(name), out var v) ? v.Id : 0;
            }

            public static async Task<LookupCache> LoadAsync(EMSContext db, int subscID)
            {
                var c = new LookupCache { Db = db };
                foreach (var t in OtherTypes) c.Others[t] = new();

                foreach (var o in await db.OtherMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.MasterName != null &&
                                         OtherTypes.Contains(x.MasterType!))
                             .Select(x => new { x.MTransNo, x.MasterType, x.MasterName }).ToListAsync())
                    c.Others[o.MasterType!].TryAdd(Key(o.MasterName!), (o.MasterName!.Trim(), o.MTransNo));

                foreach (var h in await db.HSNMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.HSNCode != null)
                             .Select(x => x.HSNCode).ToListAsync())
                    c.Hsn.TryAdd(Key(h!), h!.Trim());

                foreach (var t in await db.TaxMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N")
                             .Select(x => x.TaxP).Distinct().ToListAsync())
                    c.TaxRates.Add(Math.Round(t, 2));

                foreach (var s in await db.MaterialStd.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.Grade != null)
                             .OrderBy(x => x.MTransNo)
                             .Select(x => new { x.MTransNo, x.Grade, x.MatrStd }).ToListAsync())
                {
                    var r = new StdRef { Id = s.MTransNo, Grade = s.Grade!.Trim(), Std = s.MatrStd?.Trim() ?? "" };
                    c.StdByGradeStd.TryAdd(Key(r.Grade) + "|" + Key(r.Std), r);
                    c.StdByGrade.TryAdd(Key(r.Grade), r);
                }

                foreach (var pm in await db.PartMaster.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.PartCode != null)
                             .ToListAsync())
                    c.Parts.TryAdd(Key(pm.PartCode!), pm);

                foreach (var x in await db.MaterialGrade.AsNoTracking()
                             .Where(x => x.SubscID == subscID && x.DeleteStatus == "N" && x.Grade != null).ToListAsync())
                    c.Grades.TryAdd(x.PartNo + "|" + Key(x.Grade!),
                        new GradeRow(x.MTransNo, x.GradeNo, x.MinStock, x.MaxStock, x.MOQ, x.LockStatus, x.LockCounter));

                return c;
            }
        }

        private sealed class ImportPlan
        {
            public PartImportRequest Request { get; }
            public LookupCache Lookups { get; }
            public EMSContext Db => Lookups.Db;
            public bool UpdateExisting { get; }

            public List<CustomerImportIssue> Issues { get; } = new();
            public List<PartPlan> Parts { get; } = new();
            public Dictionary<string, PartPlan> PartsInFile { get; } = new();
            public List<GradePlan> Grades { get; } = new();

            // New master values planned by this file.
            private readonly Dictionary<string, Dictionary<string, string>> newOthers = new();  // type -> KEY -> name
            private readonly Dictionary<string, string> newHsn = new();
            public Dictionary<string, StdRef> NewStds { get; } = new();        // KEY(grade)|KEY(std)
            public Dictionary<CustomerImportMaster, StdRef> StdOf { get; } = new();  // "Grade" master line -> its row

            public List<CustomerImportMaster> MastersToCreate { get; } = new();

            public int ErrorCount => Issues.Count(i => i.Level == "Error");

            public ImportPlan(PartImportRequest req, LookupCache lookups)
            {
                Request = req;
                Lookups = lookups;
                UpdateExisting = string.Equals(req.ExistingMode?.Trim(), "UPDATE", StringComparison.OrdinalIgnoreCase);
                foreach (var t in OtherTypes) newOthers[t] = new();
            }

            public void Error(string sheet, int row, string field, string message) =>
                Issues.Add(new CustomerImportIssue { Sheet = sheet, RowNo = row, Level = "Error", Field = field, Message = message });

            public void Warn(string sheet, int row, string field, string message) =>
                Issues.Add(new CustomerImportIssue { Sheet = sheet, RowNo = row, Level = "Warning", Field = field, Message = message });

            public void Length(string sheet, int row, string field, string? value, int max)
            {
                if (value != null && value.Length > max)
                    Error(sheet, row, field, $"{field} is {value.Length} characters; the maximum is {max}.");
            }

            public string? ParseStatus(string sheet, int row, string? text)
            {
                var v = Clean(text);
                if (v == null) return null;
                switch (v.ToUpperInvariant())
                {
                    case "ACTIVE": case "Y": case "YES": case "1": return "Y";
                    case "INACTIVE": case "N": case "NO": case "0": return "N";
                }
                Error(sheet, row, "Status", $"'{v}' is not valid. Use Active or Inactive.");
                return null;
            }

            public string? ParseYesNo(string sheet, int row, string field, string? text)
            {
                var v = Clean(text);
                if (v == null) return null;
                switch (v.ToUpperInvariant())
                {
                    case "Y": case "YES": case "1": case "TRUE": return "Y";
                    case "N": case "NO": case "0": case "FALSE": return "N";
                }
                Error(sheet, row, field, $"'{v}' is not valid. Use Y or N.");
                return null;
            }

            /// <summary>A quantity / dimension: number, 0 or more, rounded to the column's scale; null when blank.</summary>
            public decimal? ParseQty(string sheet, int row, string field, string? text, int scale)
            {
                var v = Clean(text);
                if (v == null) return null;
                if (TryDec(v, out var d) && d >= 0 && d < 1000000000000m) return Math.Round(d, scale);
                Error(sheet, row, field, $"'{v}' must be a number, 0 or more.");
                return null;
            }

            /// <summary>Other Master value (stored spelling); a missing one is planned for creation.</summary>
            public string? ResolveOther(string sheet, int row, string field, string masterType, string? value, int max = 50)
            {
                if (value == null) return null;
                if (value.Length > max)
                {
                    Error(sheet, row, field, $"{field} '{value}' is {value.Length} characters; the maximum is {max}.");
                    return null;
                }
                var k = Key(value);
                if (Lookups.Others[masterType].TryGetValue(k, out var stored)) return stored.Name;
                if (newOthers[masterType].TryGetValue(k, out var planned)) return planned;

                newOthers[masterType][k] = value;
                MastersToCreate.Add(new CustomerImportMaster
                {
                    Master = field, Value = value, Detail = $"Other Master - {masterType}"
                });
                return value;
            }

            /// <summary>HSN code; a missing one is created in HSN Master (no description).</summary>
            public string? ResolveHsn(string sheet, int row, string code)
            {
                if (code.Length > 10)
                {
                    Error(sheet, row, "HSN Code", $"'{code}' is longer than 10 characters.");
                    return null;
                }
                if (!Regex.IsMatch(code, "^[0-9]{4,8}$"))
                    Warn(sheet, row, "HSN Code", $"'{code}' is not the usual 4-8 digit HSN / SAC code.");

                var k = Key(code);
                if (Lookups.Hsn.TryGetValue(k, out var stored)) return stored;
                if (newHsn.TryGetValue(k, out stored)) return stored;

                newHsn[k] = code;
                MastersToCreate.Add(new CustomerImportMaster { Master = "HSN Code", Value = code, Detail = "HSN Master - add the description there" });
                return code;
            }

            /// <summary>
            /// The Material Standard row a grade refers to. With a standard: that
            /// exact (grade, standard) row, created when missing. Without one: the
            /// grade's first row - a new grade cannot be created without its standard.
            /// </summary>
            public StdRef? ResolveStd(string sheet, int row, string grade, string? std)
            {
                if (grade.Length > 50)
                {
                    Error(sheet, row, "Grade", $"Grade is {grade.Length} characters; the maximum is 50.");
                    return null;
                }
                if (std == null)
                {
                    if (Lookups.StdByGrade.TryGetValue(Key(grade), out var any)) return any;
                    var planned = NewStds.Values.FirstOrDefault(s => Key(s.Grade) == Key(grade));
                    if (planned != null) return planned;
                    Error(sheet, row, "Grade", $"Grade '{grade}' is not in Material Standard. Add its Material Standard so it can be created, or correct the spelling.");
                    return null;
                }
                if (std.Length > 100)
                {
                    Error(sheet, row, "Material Standard", $"Material Standard is {std.Length} characters; the maximum is 100.");
                    return null;
                }

                var k = Key(grade) + "|" + Key(std);
                if (Lookups.StdByGradeStd.TryGetValue(k, out var found)) return found;
                if (NewStds.TryGetValue(k, out var plannedStd)) return plannedStd;

                var r = new StdRef { Grade = grade, Std = std };
                NewStds[k] = r;
                var master = new CustomerImportMaster
                {
                    Master = "Grade", Value = $"{grade} / {std}", Detail = "Material Standard"
                };
                MastersToCreate.Add(master);
                StdOf[master] = r;
                return r;
            }

            /// <summary>Starts a grade row and links it to its part by Part Code.</summary>
            public GradePlan? NewGrade(int row, string? partCode, string? item)
            {
                var code = Clean(partCode);
                var g = new GradePlan
                {
                    RowNo = row,
                    Result = new PartImportResult { Sheet = SheetGrades, RowNo = row, PartCode = code ?? "", Item = item }
                };
                Grades.Add(g);

                void Fail(string msg)
                {
                    Error(SheetGrades, row, "Part Code", msg);
                    g.Result.Action = "Error";
                    g.Result.Message = msg;
                }

                if (code == null)
                {
                    Fail("Part Code is required - it links this grade to its part.");
                    return null;
                }

                if (PartsInFile.TryGetValue(Key(code), out var plan))
                {
                    if (plan.Action == "Error")
                    {
                        Fail($"Part '{code}' (Parts sheet row {plan.RowNo}) has errors - fix that row first.");
                        return null;
                    }
                    g.Parent = new ParentRef { Key = Key(code), Plan = plan };
                    g.Result.PartName = plan.Result.PartName;
                    return g;
                }

                if (Lookups.Parts.TryGetValue(Key(code), out var existing))
                {
                    g.Parent = new ParentRef { Key = Key(code), Existing = existing };
                    g.Result.PartName = existing.PartName;
                    return g;
                }

                Fail($"Part '{code}' is not in Part Master" +
                     (Request.Parts.Count > 0 ? " or the Parts sheet." : " - import the part first, or use import type 'All'."));
                return null;
            }

            public bool Settle(GradePlan g, int errorsBefore, bool exists)
            {
                if (ErrorCount > errorsBefore)
                {
                    g.Result.Action = "Error";
                    g.Result.Message = "Row has errors.";
                    return false;
                }

                if (exists && !UpdateExisting)
                {
                    g.Action = "Skip";
                    g.Result.Action = "Skip";
                    g.Result.Message = "Part already has this grade - skipped.";
                    return false;
                }

                g.Action = exists ? "Update" : "New";
                g.Result.Action = g.Action;
                g.Result.Message = exists ? "Will update the existing grade." : "Will be added.";
                return true;
            }

            public PartImportReport BuildReport(bool dryRun)
            {
                var results = Parts.Select(p => p.Result).Concat(Grades.Select(g => g.Result)).ToList();

                CustomerImportCount Count(string sheet, int rows) => new()
                {
                    Sheet = sheet,
                    Rows = rows,
                    New = results.Count(r => r.Sheet == sheet && r.Action == "New"),
                    Update = results.Count(r => r.Sheet == sheet && r.Action == "Update"),
                    Skip = results.Count(r => r.Sheet == sheet && r.Action == "Skip"),
                    Errors = results.Count(r => r.Sheet == sheet && r.Action == "Error")
                };

                var summary = new List<CustomerImportCount>();
                if (Request.Parts.Count > 0) summary.Add(Count(SheetParts, Request.Parts.Count));
                if (Request.Grades.Count > 0) summary.Add(Count(SheetGrades, Request.Grades.Count));

                var errors = ErrorCount;
                var work = results.Count(r => r.Action is "New" or "Update");

                if (errors == 0 && work == 0)
                    Issues.Add(new CustomerImportIssue
                    {
                        Sheet = "Import", Level = "Warning", Field = "",
                        Message = "Every row already exists and would be skipped. Choose 'Update existing' to overwrite them."
                    });

                return new PartImportReport
                {
                    DryRun = dryRun,
                    CanImport = errors == 0 && work > 0,
                    ErrorCount = errors,
                    WarningCount = Issues.Count(i => i.Level == "Warning"),
                    Summary = summary,
                    MastersToCreate = MastersToCreate,
                    Issues = Issues.OrderBy(i => i.Level == "Error" ? 0 : 1)
                                   .ThenBy(i => i.Sheet).ThenBy(i => i.RowNo).ToList(),
                    Results = results
                };
            }
        }
    }
}
