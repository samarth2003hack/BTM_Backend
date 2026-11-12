namespace EMSAPI.Models
{
    // Order Acceptance Data Import (OADataImportController). Same design as
    // the Part / Customer imports: the page sends every cell as text, the
    // server parses and checks it, Validate is a dry run and Import is
    // all-or-nothing.
    //
    // Two sheets, linked by Order Ref (the order's number in the old system,
    // kept in OAMst.OtherRefNo). The OA No itself is always allocated by
    // SP_OAMst, exactly as on the Order Acceptance page.
    public class OAImportRequest
    {
        /// <summary>DRAFT | SUBMITTED | APPROVED - the status every imported OA gets.</summary>
        public string ApprovalMode { get; set; } = "SUBMITTED";

        /// <summary>Series used when an order row leaves Series blank (Other Master, type DocSeries).</summary>
        public string? DocSeries { get; set; }

        /// <summary>Copy the standard CUSTOMER terms (Standard Terms) onto every imported OA.</summary>
        public bool AddStandardTerms { get; set; }

        public int SubscID { get; set; }
        public int CreatedBy { get; set; }

        public List<OAImportOrderRow> Orders { get; set; } = new();
        public List<OAImportLineRow> Lines { get; set; } = new();
    }

    public class OAImportOrderRow
    {
        public int RowNo { get; set; }
        public string? OrderRef { get; set; }
        public string? Series { get; set; }
        public string? OADate { get; set; }
        public string? CustomerCode { get; set; }
        public string? CustomerName { get; set; }
        public string? PONo { get; set; }
        public string? PODate { get; set; }
        public string? DeliveryDate { get; set; }
        public string? CommittedDate { get; set; }
        public string? Currency { get; set; }
        public string? CurrencyRate { get; set; }
        public string? Notes { get; set; }
    }

    public class OAImportLineRow
    {
        public int RowNo { get; set; }
        public string? OrderRef { get; set; }
        public string? PartCode { get; set; }
        public string? Grade { get; set; }
        public string? Qty { get; set; }
        public string? Rate { get; set; }
        public string? DiscP { get; set; }
        public string? PnFP { get; set; }
        public string? FreightAmt { get; set; }
        public string? DeliveryDate { get; set; }
        public string? CommittedDate { get; set; }
        public string? Urgent { get; set; }
        public string? CustomerMaterial { get; set; }
        public string? CustPartNo { get; set; }
        public string? POID { get; set; }
        public string? Remarks { get; set; }
        public string? Deviation { get; set; }
        public string? SpecialReq { get; set; }
    }

    // ── Response ─────────────────────────────────────────────────────────
    // Counts and issues reuse the Customer import shapes.

    public class OAImportReport
    {
        public bool DryRun { get; set; }
        public bool CanImport { get; set; }
        public bool Imported { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public string ApprovalMode { get; set; } = string.Empty;
        public List<CustomerImportCount> Summary { get; set; } = new();
        public List<CustomerImportIssue> Issues { get; set; } = new();
        public List<OAImportResult> Results { get; set; } = new();
    }

    public class OAImportResult
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNo { get; set; }
        public string OrderRef { get; set; } = string.Empty;
        public string? Customer { get; set; }
        public string? Item { get; set; }
        public string Action { get; set; } = string.Empty;   // New | Skip | Error
        public string? Message { get; set; }

        /// <summary>The OA No allocated (or, for a skipped order, the one it already has).</summary>
        public string? OANo { get; set; }

        /// <summary>Order: net value. Line: total value. As the OA page computes them.</summary>
        public decimal? Value { get; set; }
    }
}
