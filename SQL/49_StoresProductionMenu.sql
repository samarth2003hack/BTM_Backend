/*==============================================================================
  49. STORES ISSUE / PRODUCTION MRN / PURCHASE STATUS REPORTS  -  menu rows

  Rows + AccessTemplate grants for the pages that went live with 44-48:

      Transactions (SSLNo 5)       715 Store Issue, 716 Store Returns
      Production   (SSLNo 9, new)  901 Production MRN, 902 General MRN
      Purchase Reports (SSLNo 8)   729 Purchase Requisition Report,
                                   730 Requisition Status Report,
                                   731 Purchase Status Report,
                                   732 RGP Status Report

  Run 33_StoresInwardMenu.sql too (rows 707-714: GRN, GRN QC, NC Closure,
  GRN Store, Heat No. ...) - this script does not repeat them.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * Paths are relative to the EMSSoft root: the pages live in
    'Modules/Purchase/', 'Modules/Production/' and 'Modules/PurchaseReports/'.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops these rows (as it does 701-728 and 801-820).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Menu TABLE
(
    MTransNo    INT,
    Section     VARCHAR(50),
    SSLNo       INT,
    ModuleName  VARCHAR(50),
    MSLNo       INT,
    ModuleIcon  VARCHAR(50),
    ModulePage  VARCHAR(100)
);

INSERT INTO @Menu (MTransNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
VALUES
    (715, 'Transactions',     5, 'Store Issue',                 15, 'bi-box-arrow-up-right',  'Modules/Purchase/StoreIssue.html'),
    (716, 'Transactions',     5, 'Store Returns',               16, 'bi-arrow-return-left',   'Modules/Purchase/IssueReturn.html'),
    (901, 'Production',       9, 'Production MRN',               1, 'bi-gear-wide-connected', 'Modules/Production/MRN.html'),
    (902, 'Production',       9, 'General MRN',                  2, 'bi-card-checklist',      'Modules/Production/MRNG.html'),
    (729, 'Purchase Reports', 8, 'Purchase Requisition Report',  9, 'bi-file-earmark-text',   'Modules/PurchaseReports/PurchaseRequisitionReport.html'),
    (730, 'Purchase Reports', 8, 'Requisition Status Report',   10, 'bi-list-check',          'Modules/PurchaseReports/RequisitionStatusReport.html'),
    (731, 'Purchase Reports', 8, 'Purchase Status Report',      11, 'bi-clipboard-data',      'Modules/PurchaseReports/PurchaseStatusReport.html'),
    (732, 'Purchase Reports', 8, 'RGP Status Report',           12, 'bi-arrow-left-right',    'Modules/PurchaseReports/RGPStatusReport.html');

MERGE dbo.Menulist AS T
USING @Menu AS S
   ON T.MTransNo = S.MTransNo
WHEN MATCHED THEN
    UPDATE SET T.Category   = 'EMS',
               T.CSLNo      = 1,
               T.Section    = S.Section,
               T.SSLNo      = S.SSLNo,
               T.ModuleName = S.ModuleName,
               T.MSLNo      = S.MSLNo,
               T.ModuleIcon = S.ModuleIcon,
               T.ModulePage = S.ModulePage
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    VALUES (S.MTransNo, 'EMS', 1, S.Section, S.SSLNo, S.ModuleName, S.MSLNo, S.ModuleIcon, S.ModulePage);

-- Same batch as the MERGE (no GO): @Menu would not survive a batch boundary.
-- Every template that already holds grants gets full access to the pages,
-- with Department copied from its existing rows.
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT  T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM  (
            SELECT TemplateName, SubscID, MAX(Department) AS Department
              FROM dbo.AccessTemplate
             WHERE TemplateName IS NOT NULL
             GROUP BY TemplateName, SubscID
        ) T
 CROSS JOIN (SELECT MTransNo FROM @Menu) M
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.AccessTemplate A
            WHERE A.TemplateName = T.TemplateName
              AND A.SubscID      = T.SubscID
              AND A.ModuleNo     = M.MTransNo);
GO

PRINT '49_StoresProductionMenu applied.';
GO
