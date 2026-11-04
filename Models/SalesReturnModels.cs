using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // ============================================================
    // SalesReturnMst — Sales Return (Credit Note) Header
    // ============================================================
    [Table("SalesReturnMst")]
    public class SalesReturnMst
    {
        [Key] public int MTransNo { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }

        public int? VendorNo { get; set; }
        public int? VendorNoShip { get; set; }
        public string? VendorName { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? TaxType { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }

        public decimal? InvValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? TaxP { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }

        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public string? DocNotes { get; set; }

        // N=Draft S=Submitted Y=Approved R=Rejected
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_SalesReturnMst
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? DocSeries { get; set; }
        public int? DocYear { get; set; }
        public int? DocNo { get; set; }
        public DateTime? DocDate { get; set; }

        public int? TargetTransNo { get; set; }
        public int? TargetDocYear { get; set; }
        public int? TargetDocNo { get; set; }

        public int? VendorNo { get; set; }
        public int? VendorNoShip { get; set; }
        public string? VendorName { get; set; }
        public string? TxnGSTIN { get; set; }
        public string? TaxType { get; set; }

        public string? RefNo { get; set; }
        public DateTime? RefDate { get; set; }

        public decimal? InvValue { get; set; }
        public decimal? TaxableValue { get; set; }
        public decimal? TaxP { get; set; }
        public decimal? IGSTAmt { get; set; }
        public decimal? CGSTAmt { get; set; }
        public decimal? SGSTAmt { get; set; }
        public decimal? RoundOff { get; set; }
        public decimal? NetValue { get; set; }

        public string? TxnCurrency { get; set; }
        public decimal? CurrencyRate { get; set; }
        public decimal? CurrencyAmount { get; set; }

        public string? DocNotes { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }  // N=Draft S=Submit Y=Approve R=Reject
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }

    // ============================================================
    // SalesReturnDtl — Sales Return Line Items
    // ============================================================
    [Table("SalesReturnDtl")]
    public class SalesReturnDtl
    {
        [Key] public int TransSlNo { get; set; }

        public int MTransNo { get; set; }     // NOT NULL
        public int InvTransNo { get; set; }   // NOT NULL
        public int SlNo { get; set; }          // NOT NULL — INT (not NUMERIC)
        public int? LinkNo { get; set; }

        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? Description { get; set; }
        public string? MocDescription { get; set; }

        public decimal Qty { get; set; }        // NOT NULL
        public decimal ReturnQty { get; set; }  // NOT NULL
        public string? Unit { get; set; }
        public decimal Rate { get; set; }       // NOT NULL
        public decimal GSTP { get; set; }       // NOT NULL
        public decimal TotalValue { get; set; } // NOT NULL

        public string? ItemRemarks { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
    }

    public class SP_SalesReturnDtl
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int? InvTransNo { get; set; }
        public int? SlNo { get; set; }
        public int? LinkNo { get; set; }
        public int? PartNo { get; set; }
        public string? PartCode { get; set; }
        public string? Description { get; set; }
        public string? MocDescription { get; set; }
        public decimal? Qty { get; set; }
        public decimal? ReturnQty { get; set; }
        public string? Unit { get; set; }
        public decimal? Rate { get; set; }
        public decimal? GSTP { get; set; }
        public decimal? TotalValue { get; set; }
        public string? ItemRemarks { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }

        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }

        public string Status { get; set; } = "Insert";
    }
}
