namespace EMSAPI.Models
{
    /*  Request shapes used only by the inward inspection screens (GRNQC.html).
        The procedures they drive are the same SP_GRNNCDtl / SP_GRNInspDtl /
        SP_GRNInspPost the single-row endpoints call; these just let the page
        submit a whole inspection as one unit of work.  */

    /// <summary>
    /// One inspection, submitted whole: the verdict on every receipt line,
    /// the non-conformance rows behind it, the NC rows the inspector removed,
    /// and the header stamped at posting.
    ///
    /// Applied in one transaction — reopen (when the GRN is already
    /// inspected), NC deletes, NC updates / inserts, line verdicts, post — so
    /// a refusal anywhere leaves the GRN exactly as it was.
    /// </summary>
    public class InwardInspectionSave
    {
        public int MTransNo { get; set; }

        /// <summary>Send as "YYYY-MM-DD". Blank means today (server date).</summary>
        public DateTime? QCDate { get; set; }

        public string? QCverifyBy { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }

        public List<InwardInspectionLine> Lines { get; set; } = new();
        public List<InwardInspectionNC> NC { get; set; } = new();

        /// <summary>GRNNCDtl.TransSlNo of saved NC rows to remove.</summary>
        public List<int> DeletedNC { get; set; } = new();
    }

    /// <summary>The verdict on one receipt line (GRNDtl.TransSlNo).</summary>
    public class InwardInspectionLine
    {
        public int TransSlNo { get; set; }
        public decimal AccQty { get; set; }
        public decimal RejQty { get; set; }
        public decimal RewQty { get; set; }
        public decimal AODQty { get; set; }
        public string? InspRemarks { get; set; }
    }

    /// <summary>One NC row. TransSlNo 0 inserts, otherwise updates.</summary>
    public class InwardInspectionNC
    {
        public int TransSlNo { get; set; }
        public int GRNTransSlNo { get; set; }
        public string? NCType { get; set; }
        public string? HeatNo { get; set; }
        public string? NCRemarks { get; set; }
        public decimal NCQty { get; set; }
    }
}
