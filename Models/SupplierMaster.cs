using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A supplier, plus its contacts and terms. Deleting the supplier
    // soft-deletes those and the vendor's PriceList rates (SP_SupplierMaster).
    [Table("SupplierMaster")]
    public class SupplierMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? SupplierCode { get; set; }
        public string? SupplierType { get; set; }
        public string? SupplierName { get; set; }
        public string? AliasName { get; set; }
        public string? Category { get; set; }
        public string? SupplierAddress { get; set; }
        public string? StateName { get; set; }
        public int? StateCode { get; set; }
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
        public int? CreditPeriod { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? IFSC { get; set; }
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

    // Parameters for SP_SupplierMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_SupplierMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string SupplierCode { get; set; } = string.Empty;
        public string SupplierType { get; set; } = "SUPPLIER";
        public string SupplierName { get; set; } = string.Empty;
        public string? AliasName { get; set; }
        public string? Category { get; set; }
        public string? SupplierAddress { get; set; }
        public string? StateName { get; set; }
        public int? StateCode { get; set; }
        public string? PinCode { get; set; }
        public string? PinCodeLocation { get; set; }
        public string? PhoneNo { get; set; }
        public string? MobileNo { get; set; }
        public string? EmailID { get; set; }
        public string? Website { get; set; }
        public string? TaxType { get; set; }
        public string? GSTIN { get; set; }
        public string? PAN { get; set; }
        public string TxnCurrency { get; set; } = "INR";
        public int? CreditPeriod { get; set; }
        public string? BankName { get; set; }
        public string? BankAccountNo { get; set; }
        public string? IFSC { get; set; }
        public string? Remarks { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    // Contact people at a supplier. MTransNo is the supplier; TransSlNo the row.
    [Table("SupplierContacts")]
    public class SupplierContacts
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

    // Parameters for SP_SupplierContacts. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_SupplierContacts
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

    // Terms agreed with one supplier. Pass TermSlNo 0 to have the next
    // number allocated.
    [Table("SupplierTerm")]
    public class SupplierTerm
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

    // Parameters for SP_SupplierTerm. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_SupplierTerm
    {
        public int TransSlNo { get; set; }
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public decimal? TermSlNo { get; set; }
        public string? TermTitle { get; set; }
        public string? TermDescription { get; set; }
        public string TermType { get; set; } = "SUPPLIER";

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
