/*==========================================================================
  31_GRNStorePage.sql — backing for EMSSoft/Modules/Purchase/GRNStore.html
  --------------------------------------------------------------------------
  Idempotent: safe to run again.

  VW_GRNStoreAllocation — one row per GRN, counted over its ACCEPTED lines
  only (AccQty > 0). A line inspection rejected in full has nothing to put
  away, so it never keeps a GRN on the "awaiting allocation" list — which is
  why VW_GRNInwardStatus.UnallocatedLines (every live line) is not used here.
  That view is read by other pages and is left untouched.

      LineCount       accepted lines
      AllocatedCount  accepted lines with a Store
      PendingCount    accepted lines still without one
      AllocatedBy/On  who touched the most recently allocated accepted line
                      last — the page's "only the last allocator may edit"
                      rule reads it (GRNDtl carries no dedicated column)

  The page shows only inspected receipts (GRNType 'Q' / 'C'): AccQty stays 0
  until SP_GRNInspDtl records the verdict, so before inspection there is no
  accepted quantity to allocate. SP_GRNStoreAlloc itself stays ungated.

  No change to SP_GRNStoreAlloc / SP_StoreLocation is required.
==========================================================================*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.VW_GRNStoreAllocation
AS
SELECT  GM.MTransNo,
        GM.DocSeries,
        GM.DocYear,
        GM.DocNo,
        GM.DocDate,
        GM.VendorNo,
        GM.VendorName,
        GM.GRNType,
        GM.InspNo,
        GM.QCDate,
        GM.QCverifyBy,
        GM.LockStatus,
        GM.SubscID,
        ISNULL(A.LineCount, 0)                                AS LineCount,
        ISNULL(A.AllocatedCount, 0)                           AS AllocatedCount,
        ISNULL(A.LineCount, 0) - ISNULL(A.AllocatedCount, 0)  AS PendingCount,
        LA.ModifiedBy                                         AS AllocatedBy,
        LA.ModifiedOn                                         AS AllocatedOn
  FROM  dbo.GRNMst GM
 OUTER APPLY
       (
           SELECT COUNT(*) AS LineCount,
                  SUM(CASE WHEN NULLIF(LTRIM(RTRIM(GD.Store)), '') IS NULL THEN 0 ELSE 1 END)
                           AS AllocatedCount
             FROM dbo.GRNDtl GD
            WHERE GD.MTransNo = GM.MTransNo AND GD.DeleteStatus = 'N'
              AND GD.AccQty > 0
       ) A
 OUTER APPLY
       (
           SELECT TOP (1) GD.ModifiedBy, GD.ModifiedOn
             FROM dbo.GRNDtl GD
            WHERE GD.MTransNo = GM.MTransNo AND GD.DeleteStatus = 'N'
              AND GD.AccQty > 0
              AND NULLIF(LTRIM(RTRIM(GD.Store)), '') IS NOT NULL
            ORDER BY GD.ModifiedOn DESC, GD.TransSlNo DESC
       ) LA
 WHERE  GM.DeleteStatus = 'N';
GO
