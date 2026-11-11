using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EMSAPI.Models
{
    // An item / part. SPart, PPart and MPart are the sales, purchase and
    // manufacturing flags — they decide which module's pickers show the part.
    // PartCode is unique per subscription; PartName deliberately is not.
    [Table("PartMaster")]
    public class PartMaster
    {
        [Key]
        public int MTransNo { get; set; }

        public string? PartCode { get; set; }
        public string? PartName { get; set; }
        public string? CodeDetails { get; set; }
        public string? PartDetails { get; set; }
        public string? ShortDescription { get; set; }
        public string? FullDescription { get; set; }
        public string? DrawingNo { get; set; }
        public int? DrgRevNo { get; set; }
        public string? ColorCode { get; set; }
        public int? CatNo { get; set; }
        public int? SubCatNo { get; set; }
        public int? GroupNo { get; set; }
        public string? Category { get; set; }
        public string? SubCategory { get; set; }
        public string? InventoryGroup { get; set; }
        public string? Department { get; set; }
        public string? Brand { get; set; }
        public string? PartType { get; set; }
        public string? PartClass { get; set; }
        public string? HSNCode { get; set; }
        public decimal? TaxP { get; set; }
        public string? PrimaryUnit { get; set; }
        public string? SecondaryUnit { get; set; }
        public decimal? ConversionRatio { get; set; }
        public string? TxnUnit { get; set; }
        public string? Packing { get; set; }
        public decimal? PackQty { get; set; }
        public decimal? PackSize { get; set; }
        public string? SPart { get; set; }
        public string? PPart { get; set; }
        public string? MPart { get; set; }
        public decimal? MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public decimal? MOQ { get; set; }
        public decimal? ShelfLife { get; set; }
        public decimal? CostPrice { get; set; }
        public decimal? SalesPrice { get; set; }
        public decimal? MRP { get; set; }
        public string? MfrLicNo { get; set; }
        public string? MfgStd { get; set; }
        public string? StoreLocation { get; set; }
        public string? Rack { get; set; }
        public string? RackRow { get; set; }
        public string? RackCase { get; set; }
        public decimal? PartLength { get; set; }
        public decimal? PartWidth { get; set; }
        public decimal? PartHeight { get; set; }
        public decimal? PartWeight { get; set; }
        public string? AtchFileName { get; set; }

        // Extra allowed % on GRN (SQL 70): a supplier may deliver up to this %
        // above the PO quantity. 0 = no extra.
        public decimal? ExcessPct { get; set; }

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

    // Parameters for SP_PartMaster. Status = 'Insert' | 'Update' | 'Delete'.
    // The procedure defaults SPart/PPart/MPart to 'N' and fills ConversionRatio
    // with 1 when a secondary unit is given without one.
    public class SP_PartMaster
    {
        public int MTransNo { get; set; }
        public int MCount { get; set; }

        public string PartCode { get; set; } = string.Empty;
        public string PartName { get; set; } = string.Empty;
        public string? CodeDetails { get; set; }
        public string? PartDetails { get; set; }
        public string? ShortDescription { get; set; }
        public string? FullDescription { get; set; }
        public string? DrawingNo { get; set; }
        public int? DrgRevNo { get; set; }
        public string? ColorCode { get; set; }
        public int? CatNo { get; set; }
        public int? SubCatNo { get; set; }
        public int? GroupNo { get; set; }
        public string? Category { get; set; }
        public string? SubCategory { get; set; }
        public string? InventoryGroup { get; set; }
        public string? Department { get; set; }
        public string? Brand { get; set; }
        public string? PartType { get; set; }
        public string? PartClass { get; set; }
        public string? HSNCode { get; set; }
        public decimal? TaxP { get; set; }
        public string? PrimaryUnit { get; set; }
        public string? SecondaryUnit { get; set; }
        public decimal? ConversionRatio { get; set; }
        public string? TxnUnit { get; set; }
        public string? Packing { get; set; }
        public decimal? PackQty { get; set; }
        public decimal? PackSize { get; set; }
        public string? SPart { get; set; } = "N";
        public string? PPart { get; set; } = "N";
        public string? MPart { get; set; } = "N";
        public decimal? MinStock { get; set; }
        public decimal? MaxStock { get; set; }
        public decimal? MOQ { get; set; }
        public decimal? ShelfLife { get; set; }
        public decimal? CostPrice { get; set; }
        public decimal? SalesPrice { get; set; }
        public decimal? MRP { get; set; }
        public string? MfrLicNo { get; set; }
        public string? MfgStd { get; set; }
        public string? StoreLocation { get; set; }
        public string? Rack { get; set; }
        public string? RackRow { get; set; }
        public string? RackCase { get; set; }
        public decimal? PartLength { get; set; }
        public decimal? PartWidth { get; set; }
        public decimal? PartHeight { get; set; }
        public decimal? PartWeight { get; set; }
        public string? AtchFileName { get; set; }

        // Extra allowed % on GRN (SQL 70). Saved by SP_PartMasterExcess after
        // SP_PartMaster; null keeps the stored value.
        public decimal? ExcessPct { get; set; }

        public string? SystemIP { get; set; }
        public string LockStatus { get; set; } = "N";
        public int CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public string? DeleteReason { get; set; }
        public int SubscID { get; set; }
        public string Status { get; set; } = "Insert";
    }
}
