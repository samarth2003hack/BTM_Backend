using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Models
{
    /// <summary>
    /// VW_GRNStoreAllocation (SQL/31_GRNStorePage.sql) — one row per GRN with
    /// its allocation progress counted over the accepted lines only.
    /// Mapped by attribute so EMSContext.cs needs no OnModelCreating entry.
    /// </summary>
    [Keyless]
    [Table("VW_GRNStoreAllocation")]
    public class GRNStoreAllocationStatus
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public int VendorNo { get; set; }
        public string? VendorName { get; set; }
        public string? GRNType { get; set; }
        public string? InspNo { get; set; }
        public DateTime? QCDate { get; set; }
        public string? QCverifyBy { get; set; }
        public string? LockStatus { get; set; }
        public int? SubscID { get; set; }
        public int LineCount { get; set; }
        public int AllocatedCount { get; set; }
        public int PendingCount { get; set; }
        public int? AllocatedBy { get; set; }
        public DateTime? AllocatedOn { get; set; }
    }

    /// <summary>Body of POST api/StoreAllocation/SaveLines.</summary>
    public class StoreAllocationSaveRequest
    {
        public int MTransNo { get; set; }
        public int UserNo { get; set; }
        public int SubscID { get; set; }
        public List<StoreAllocationSaveLine> Lines { get; set; } = new();
    }

    /// <summary>
    /// One line's bin. A blank Store clears the line's allocation.
    /// </summary>
    public class StoreAllocationSaveLine
    {
        public int TransSlNo { get; set; }
        public string? Store { get; set; }
        public string? Location { get; set; }
    }

    public partial class EMSContext
    {
        public DbSet<GRNStoreAllocationStatus> GRNStoreAllocationStatus { get; set; } = null!;
    }
}
