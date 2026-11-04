using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // A ledger account. AcType drives which pickers it appears in
    // (CASH / BANK / CUSTOMER / SUPPLIER / Asset / Liability / Income / Expense);
    // GroupHeadNo places it in the GroupHeads tree.
    [Table("AccountsMaster")]
    public class AccountsMaster
    {
        [Key]
        public int AcNo { get; set; }

        public string? AcName { get; set; }
        public string? AcAlias { get; set; }
        public string? AcType { get; set; }
        public int? VendNo { get; set; }
        public int GroupHeadNo { get; set; }
        public string? CommonAccount { get; set; }

        public string? SystemIP { get; set; }
        public string? LockStatus { get; set; }
        public string? DeleteStatus { get; set; }
        public int? LockCounter { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string? DeleteReason { get; set; }
        public int? SubscID { get; set; }
        public int? TempNo { get; set; }
    }

    // Parameters for SP_AccountMaster. Status = 'Insert' | 'Update' | 'Delete'.
    public class SP_AccountMaster
    {
        public int AcNo { get; set; }
        public int MCount { get; set; }

        public string AcName { get; set; } = string.Empty;
        public string? AcAlias { get; set; }
        public string AcType { get; set; } = string.Empty;
        public int? VendNo { get; set; }
        public int GroupHeadNo { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }

    public class AccountMasterResponse
    {
        public int ID { get; set; }
        public string? Message { get; set; }
    }

    // Maps "module X, role Y" to the ledger it posts to — e.g.
    // (Sales, RoundOff) -> Round Off. Unique on (ModuleName, ACCType, SubscID).
    [Table("AccountConfig")]
    public class AccountConfig
    {
        [Key]
        public int MTransNo { get; set; }

        [StringLength(50)]
        public string? ModuleName { get; set; }

        [StringLength(30)]
        public string? ACCType { get; set; }

        public int? AcNo { get; set; }
        public int? SubscID { get; set; }
    }

    // Parameters for SP_AccountConfig. Status = 'Insert' | 'Update' | 'Delete'.
    // Insert and Update both upsert on (ModuleName, ACCType, SubscID).
    public class SP_AccountConfig
    {
        public int MTransNo { get; set; }
        public string ModuleName { get; set; } = string.Empty;

        // Accepted as "AccType" as well, matching the LMSAPI payload.
        public string ACCType { get; set; } = string.Empty;

        public int AcNo { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
