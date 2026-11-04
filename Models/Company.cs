using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    [Table("Company")]
    public class Company
    {
        [Key]
        public int MTransNo { get; set; }

        public string? CompCode { get; set; }
        public string? CompName { get; set; }
        public string? TradingName { get; set; }
        public string? CompAddress { get; set; }
        public string? State { get; set; }
        public int? StateCode { get; set; }
        public string? PhoneNo { get; set; }
        public int? PinCode { get; set; }
        public string? SalesPhoneNo { get; set; }
        public string? PurchasePhoneNo { get; set; }
        public string? GeneralEmail { get; set; }
        public string? SalesEmail { get; set; }
        public string? PurchaseEmail { get; set; }
        public string? Website { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string? CIN { get; set; }
        public string? IECNO { get; set; }
        public DateTime? FromYear { get; set; }
        public DateTime? ToYear { get; set; }
        public int? FYear { get; set; }
        public string? YearCode { get; set; }
        public string? TxnCurrency { get; set; }
        public string? CompDataSource { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_Company. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_Company
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string CompCode { get; set; } = string.Empty;
        public string CompName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public string? CompAddress { get; set; }
        public string? State { get; set; }
        public int? StateCode { get; set; }
        public string? PhoneNo { get; set; }
        public int? PinCode { get; set; }
        public string? SalesPhoneNo { get; set; }
        public string? PurchasePhoneNo { get; set; }
        public string? GeneralEmail { get; set; }
        public string? SalesEmail { get; set; }
        public string? PurchaseEmail { get; set; }
        public string? Website { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string? CIN { get; set; }
        public string? IECNO { get; set; }
        public DateTime? FromYear { get; set; }
        public DateTime? ToYear { get; set; }
        public int? FYear { get; set; }
        public string? YearCode { get; set; }
        public string TxnCurrency { get; set; } = "INR";
        public string? CompDataSource { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class CompanyResponse
    {
        public int ID { get; set; }
        public string? Message { get; set; }
    }
}
