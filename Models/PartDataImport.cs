namespace EMSAPI.Models
{
    // Part Data Import (PartDataImportController). Same design as the
    // Customer / Supplier imports, for PartMaster and its Material Grade
    // child. Unlike them the code is NOT generated: Part Code is typed in the
    // file, is the part's unique key, and is what the grades sheet links by.
    // Every cell arrives as text and is parsed on the server.
    public class PartImportRequest
    {
        /// <summary>ALL | PART | GRADE</summary>
        public string ImportType { get; set; } = "ALL";

        /// <summary>SKIP | UPDATE - what to do with a part / grade that already exists.</summary>
        public string ExistingMode { get; set; } = "SKIP";

        public int SubscID { get; set; }
        public int CreatedBy { get; set; }

        public List<PartImportRow> Parts { get; set; } = new();
        public List<GradeImportRow> Grades { get; set; } = new();
    }

    public class PartImportRow
    {
        public int RowNo { get; set; }
        public string? PartCode { get; set; }
        public string? PartName { get; set; }
        public string? HSNCode { get; set; }
        public string? TaxP { get; set; }
        public string? SPart { get; set; }
        public string? PPart { get; set; }
        public string? MPart { get; set; }
        public string? Category { get; set; }
        public string? SubCategory { get; set; }
        public string? InventoryGroup { get; set; }
        public string? PartType { get; set; }
        public string? PartClass { get; set; }
        public string? PrimaryUnit { get; set; }
        public string? SecondaryUnit { get; set; }
        public string? ConversionRatio { get; set; }
        public string? TxnUnit { get; set; }
        public string? StoreLocation { get; set; }
        public string? Rack { get; set; }
        public string? RackRow { get; set; }
        public string? RackCase { get; set; }
        public string? PartLength { get; set; }
        public string? PartWidth { get; set; }
        public string? PartHeight { get; set; }
        public string? PartWeight { get; set; }
        public string? Status { get; set; }
    }

    public class GradeImportRow
    {
        public int RowNo { get; set; }
        public string? PartCode { get; set; }
        public string? Grade { get; set; }
        public string? MatrStd { get; set; }
        public string? MinStock { get; set; }
        public string? MaxStock { get; set; }
        public string? MOQ { get; set; }
        public string? Status { get; set; }
    }

    // ── Response ─────────────────────────────────────────────────────────
    // Counts, issues and masters reuse the Customer import shapes.

    public class PartImportReport
    {
        public bool DryRun { get; set; }
        public bool CanImport { get; set; }
        public bool Imported { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CustomerImportCount> Summary { get; set; } = new();
        public List<CustomerImportMaster> MastersToCreate { get; set; } = new();
        public List<CustomerImportIssue> Issues { get; set; } = new();
        public List<PartImportResult> Results { get; set; } = new();
    }

    public class PartImportResult
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string PartCode { get; set; } = string.Empty;
        public string? PartName { get; set; }
        public string? Item { get; set; }
        public string Action { get; set; } = string.Empty;   // New | Update | Skip | Error
        public string? Message { get; set; }
    }
}
