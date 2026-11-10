/*==============================================================================
  62. PRODUCTION MENU ORDER

  Re-orders the Production section of the menu in workflow order.
  Only MSLNo (the item order) is changed. MTransNo, names, pages and access
  grants are untouched. Rows are matched by page path, so it works on any
  database whatever the MTransNo values are. Pages missing from the menu are
  skipped.

  Numbers go up in steps of 10 so new pages slot in without renumbering:
      20  free  ->  Production Plan      (Modules/Production/ProductionPlan.html)
      30  free  ->  Production Schedule  (Modules/Production/ProductionSchedule.html)

  Take a backup of the table first:
      SELECT * INTO dbo.Menulist_backup FROM dbo.Menulist;

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 62_ProductionMenuOrder.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Order TABLE (ModulePage VARCHAR(100), MSLNo INT);
INSERT INTO @Order (ModulePage, MSLNo) VALUES
    ('Modules/Production/ConvBOM.html',                 10),
    ('Modules/Production/JobCard.html',                 40),
    ('Modules/Production/JobCardStatus.html',           50),
    ('Modules/Quality/JobCardInspection.html',          60),
    ('Modules/PurchaseReports/JobcardReport.html',      70),
    ('Modules/Production/MRN.html',                     80),
    ('Modules/Production/MRNApproval.html',             90),
    ('Modules/PurchaseReports/ProductionMRNReport.html',100),
    ('Modules/Production/MRNG.html',                   110),
    ('Modules/Production/MRNGApproval.html',           120),
    ('Modules/PurchaseReports/GeneralMRNReport.html',  130),
    ('Modules/Production/RGPOutward.html',             140),
    ('Modules/Production/RGPApproval.html',            150),
    ('Modules/Production/DC.html',                     160);

UPDATE m
   SET m.MSLNo = o.MSLNo
  FROM dbo.Menulist m
  JOIN @Order o ON o.ModulePage = m.ModulePage
 WHERE m.Category = 'EMS' AND m.Section = 'Production'
   AND ISNULL(m.MSLNo, -1) <> o.MSLNo;

PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) re-ordered.';

SELECT m.MTransNo, m.MSLNo, m.ModuleName, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.Category = 'EMS' AND m.Section = 'Production'
 ORDER BY m.MSLNo;
