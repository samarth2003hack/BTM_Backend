using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A customer, plus its three child tables: contacts, terms and ship-to
    // addresses. Deleting the customer soft-deletes all three (SP_CustomerMaster).
    [Table("CustomerMaster")]
    public class CustomerMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? CustomerCode { get; set; }
        public string? CustomerType { get; set; }
        public string? CustomerName { get; set; }
        public string? AliasName { get; set; }
        public string? Category { get; set; }
        public string? CustomerAddress { get; set; }
        public string? PinCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? StateName { get; set; }
        public int? StateCode { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? Website { get; set; }
        public string? TaxType { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string? TxnCurrency { get; set; }
        public string? SalesExec { get; set; }
        public int? CreditPeriod { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? CustomerPriceList { get; set; }
        public string? VendorCode { get; set; }
        public string? Remarks { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_CustomerMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_CustomerMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerType { get; set; } = "CUSTOMER";
        public string CustomerName { get; set; } = string.Empty;
        public string? AliasName { get; set; }
        public string? Category { get; set; }
        public string? CustomerAddress { get; set; }
        public string? PinCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? StateName { get; set; }
        public int? StateCode { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? Website { get; set; }
        public string? TaxType { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string TxnCurrency { get; set; } = "INR";
        public string? SalesExec { get; set; }
        public int? CreditPeriod { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? CustomerPriceList { get; set; }
        public string? VendorCode { get; set; }
        public string? Remarks { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Contact people at a customer. MTransNo is the customer; TransSlNo the row.
    [Table("CustomerContacts")]
    public class CustomerContacts
    {
        [Key]
        public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public string? Title { get; set; }
        public string? CPName { get; set; }
        public string? Designation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? Email { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_CustomerContacts. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_CustomerContacts
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string? Title { get; set; }
        public string CPName { get; set; } = string.Empty;
        public string? Designation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? Email { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Terms agreed with one customer. TermSlNo is decimal so terms can be
    // sub-numbered (1.10, 1.20); pass 0 to have the next number allocated.
    [Table("CustomerTerm")]
    public class CustomerTerm
    {
        [Key]
        public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public decimal? TermSlNo { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }
        public string? TermType { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_CustomerTerm. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_CustomerTerm
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public decimal? TermSlNo { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }
        public string TermType { get; set; } = "CUSTOMER";

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Ship-to addresses for a customer — the invoice address lives on
    // CustomerMaster, these are where the goods actually go.
    [Table("ShippingAddress")]
    public class ShippingAddress
    {
        [Key]
        public int TransSlNo { get; set; }

        public int MTransNo { get; set; }
        public string? ShipName { get; set; }
        public string? ShipAlias { get; set; }
        public string? ShipAddress { get; set; }
        public string? ShipState { get; set; }
        public int? ShipStateCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? PinCode { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }

        public string? SystemIP { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteStatus { get; set; }
        public string? DeleteReason { get; set; }
        public string? LockStatus { get; set; }
        public int? LockCounter { get; set; }
        public int SubscID { get; set; }
    }

    // Parameters for SP_ShippingAddress. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_ShippingAddress
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string ShipName { get; set; } = string.Empty;
        public string? ShipAlias { get; set; }
        public string? ShipAddress { get; set; }
        public string? ShipState { get; set; }
        public int? ShipStateCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? PinCode { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
