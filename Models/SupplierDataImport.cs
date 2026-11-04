namespace EMSAPI.Models
{
    // Supplier Data Import (SupplierDataImportController) - the Customer Data
    // Import design minus Shipping Address: Suppliers, Contacts and Terms, one
    // list per sheet, every cell as text. Children carry the SUPPLIER NAME,
    // since SP_SupplierMaster allocates the code during the import (SQL/38).
    public class SupplierImportRequest
    {
        /// <summary>ALL | SUPPLIER | CONTACT | TERM</summary>
        public string ImportType { get; set; } = "ALL";

        /// <summary>SKIP | UPDATE - what to do with a supplier / child row that already exists.</summary>
        public string ExistingMode { get; set; } = "SKIP";

        public int SubscID { get; set; }
        public int CreatedBy { get; set; }

        public List<SupplierImportRow> Suppliers { get; set; } = new();
        public List<SupplierContactImportRow> Contacts { get; set; } = new();
        public List<SupplierTermImportRow> Terms { get; set; } = new();
    }

    public class SupplierImportRow
    {
        public int RowNo { get; set; }
        public string? SupplierName { get; set; }
        public string? AliasName { get; set; }
        public string? SupplierType { get; set; }
        public string? Category { get; set; }
        public string? SupplierAddress { get; set; }
        public string? StateName { get; set; }
        public string? StateCode { get; set; }
        public string? PinCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? Website { get; set; }
        public string? TaxType { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string? TxnCurrency { get; set; }
        public string? CreditPeriod { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? IFSC { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
    }

    public class SupplierContactImportRow
    {
        public int RowNo { get; set; }
        public string? SupplierName { get; set; }
        public string? Title { get; set; }
        public string? CPName { get; set; }
        public string? Designation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? Email { get; set; }
        public string? Status { get; set; }
    }

    public class SupplierTermImportRow
    {
        public int RowNo { get; set; }
        public string? SupplierName { get; set; }
        public string? TermSlNo { get; set; }
        public string? TermType { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }
        public string? Status { get; set; }
    }

    // ── Response ─────────────────────────────────────────────────────────
    // Counts, issues and masters reuse the Customer import shapes; only the
    // per-row result differs, so its fields read supplierName / supplierCode.

    public class SupplierImportReport
    {
        public bool DryRun { get; set; }
        public bool CanImport { get; set; }
        public bool Imported { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CustomerImportCount> Summary { get; set; } = new();
        public List<CustomerImportMaster> MastersToCreate { get; set; } = new();
        public List<CustomerImportIssue> Issues { get; set; } = new();
        public List<SupplierImportResult> Results { get; set; } = new();
    }

    public class SupplierImportResult
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string? Item { get; set; }
        public string Action { get; set; } = string.Empty;   // New | Update | Skip | Error
        public string? SupplierCode { get; set; }
        public string? Message { get; set; }
    }
}
