namespace EMSAPI.Models
{
    // Customer Data Import (CustomerDataImportController). The page parses the
    // Excel workbook in the browser and posts one list per sheet. Every cell
    // arrives as text, exactly as Excel displayed it, and is parsed here, so a
    // bad number becomes a row issue rather than a 400 from model binding.
    //
    // Children (contacts, terms, shipping) carry the CUSTOMER NAME, not the
    // code: codes are allocated by SP_CustomerMaster during the import, so a
    // new customer has no code yet when the file is filled in.
    public class CustomerImportRequest
    {
        /// <summary>ALL | CUSTOMER | CONTACT | TERM | SHIPPING</summary>
        public string ImportType { get; set; } = "ALL";

        /// <summary>What to do when the customer (or child row) already exists: SKIP | UPDATE.</summary>
        public string ExistingMode { get; set; } = "SKIP";

        public int SubscID { get; set; }
        public int CreatedBy { get; set; }

        public List<CustomerImportRow> Customers { get; set; } = new();
        public List<ContactImportRow> Contacts { get; set; } = new();
        public List<TermImportRow> Terms { get; set; } = new();
        public List<ShippingImportRow> Shipping { get; set; } = new();
    }

    public class CustomerImportRow
    {
        public int RowNo { get; set; }
        public string? CustomerName { get; set; }
        public string? AliasName { get; set; }
        public string? CustomerType { get; set; }
        public string? Category { get; set; }
        public string? VendorCode { get; set; }
        public string? CustomerAddress { get; set; }
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
        public string? SalesExec { get; set; }
        public string? CreditPeriod { get; set; }
        public string? CreditLimit { get; set; }
        public string? Remarks { get; set; }
        public string? Status { get; set; }
    }

    public class ContactImportRow
    {
        public int RowNo { get; set; }
        public string? CustomerName { get; set; }
        public string? Title { get; set; }
        public string? CPName { get; set; }
        public string? Designation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? Email { get; set; }
        public string? Status { get; set; }
    }

    public class TermImportRow
    {
        public int RowNo { get; set; }
        public string? CustomerName { get; set; }
        public string? TermSlNo { get; set; }
        public string? TermType { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }
        public string? Status { get; set; }
    }

    public class ShippingImportRow
    {
        public int RowNo { get; set; }
        public string? CustomerName { get; set; }
        public string? ShipName { get; set; }
        public string? ShipAlias { get; set; }
        public string? ShipAddress { get; set; }
        public string? ShipState { get; set; }
        public string? ShipStateCode { get; set; }
        public string? PinCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string? Status { get; set; }
    }

    // ── Response ─────────────────────────────────────────────────────────

    public class CustomerImportReport
    {
        public bool DryRun { get; set; }
        public bool CanImport { get; set; }
        public bool Imported { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CustomerImportCount> Summary { get; set; } = new();
        public List<CustomerImportMaster> MastersToCreate { get; set; } = new();
        public List<CustomerImportIssue> Issues { get; set; } = new();
        public List<CustomerImportResult> Results { get; set; } = new();
    }

    public class CustomerImportCount
    {
        public string Sheet { get; set; } = string.Empty;
        public int Rows { get; set; }
        public int New { get; set; }
        public int Update { get; set; }
        public int Skip { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>A lookup value the file uses that its master does not hold yet.</summary>
    public class CustomerImportMaster
    {
        public string Master { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Detail { get; set; }
        public bool Created { get; set; }
    }

    public class CustomerImportIssue
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string Level { get; set; } = "Error";   // Error | Warning
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>One line per imported row: what happened to it and, for customers, the code it got.</summary>
    public class CustomerImportResult
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? Item { get; set; }
        public string Action { get; set; } = string.Empty;   // New | Update | Skip | Error
        public string? CustomerCode { get; set; }
        public string? Message { get; set; }
    }

    // Parameters for SP_CurrencyMaster when the import has to add a currency.
    // Named parameters on purpose: CallCurrencyMasterSP binds positionally and
    // does not pass @ExchangeRate / @EffectiveDate.
    public class SP_CurrencyMasterImport
    {
        public string? Country { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal ExchangeRate { get; set; } = 1;
        public DateTime EffectiveDate { get; set; } = DateTime.Today;
        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "Y";
        public int CreatedBy { get; set; }
        public int SubscID { get; set; }
    }
}
