namespace EMSAPI.Models
{
    // ============================================================
    // Customer Material Inward request bodies
    // (SQL/60_CustomerMaterialInward.sql).
    // The page posts camelCase JSON; property names follow it.
    // ============================================================

    /// <summary>POST api/CustomerMaterialInward/Mst/SaveData — Status 'Insert' | 'Update'.</summary>
    public class CustMatInwardSaveRequest
    {
        public int MTransNo { get; set; }
        public DateTime? InwardDate { get; set; }
        /// <summary>Cross-check only: the customer is read off the OA.</summary>
        public int CustomerNo { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }
        public string? ReceivedBy { get; set; }
        public string? Remarks { get; set; }
        /// <summary>Received | Verified.</summary>
        public string? DocStatus { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    /// <summary>One bar lot inside CustMatInwardLinesRequest.</summary>
    public class CustMatInwardLineInput
    {
        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string? Grade { get; set; }
        public decimal? BarDia { get; set; }
        public decimal Qty { get; set; }
        public string? Unit { get; set; }
        public string? HeatNo { get; set; }
        public string? TCNo { get; set; }
        /// <summary>OADtl.SlNo of the inward's OA, 0 = not linked.</summary>
        public int OAID { get; set; }
        public string? Remarks { get; set; }
    }

    /// <summary>POST api/CustomerMaterialInward/Dtl/SaveData — replaces the whole line set.</summary>
    public class CustMatInwardLinesRequest
    {
        public int MTransNo { get; set; }
        public List<CustMatInwardLineInput>? Lines { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
    }

    /// <summary>Parameters of dbo.SP_CustMatInwardMst.</summary>
    public class SP_CustMatInwardMst
    {
        public string Status { get; set; } = "Insert";
        public int MTransNo { get; set; }
        public DateTime InwardDate { get; set; }
        public int CustomerNo { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public string? DCNo { get; set; }
        public DateTime? DCDate { get; set; }
        public string? ReceivedBy { get; set; }
        public string? Remarks { get; set; }
        public string? DocStatus { get; set; }
        public string? SystemIP { get; set; }
        public int UserNo { get; set; }
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
    }
}
