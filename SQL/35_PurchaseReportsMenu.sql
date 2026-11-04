/*==============================================================================
  35. PURCHASE REPORTS MENU  -  sidebar section for EMSSoft/Modules/PurchaseReports/

  The report pages read live data from PurchaseReportsController
  (GET api/PurchaseOrderReport|GRNReport/AllReports, plus the RFQ list
  endpoints) and InwardReportsController (GET api/DCReport|RGPReport|
  RGPInwardReport/AllReports). No tables or procedures are needed, only the
  menu rows and their AccessTemplate grants (a Menulist row alone is
  invisible, see 25_SalesMenuAndLookups.sql).

  Issue Report is listed although the Store Issue module (and its tables) does
  not exist yet, the same way the menu already lists other planned pages.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/PurchaseReports/'.
    Set it to wherever the deployed server serves them from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 721-728 (as it does 701-714 and 801-820).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  SIDEBAR SECTION  +  GRANTS

  MTransNo 721-728 are unused. SSLNo 8 puts Purchase Reports after Sales
  Reports, rows in the order material moves through purchasing.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/PurchaseReports/';   -- <- check, see header

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
    (721, 'Purchase Reports', 8, 'RFQ Report',            1, 'bi-envelope-paper',        @Folder + 'RFQReport.html'),
    (722, 'Purchase Reports', 8, 'RFQ Approval Report',   2, 'bi-patch-check',           @Folder + 'RFQApprovalReport.html'),
    (723, 'Purchase Reports', 8, 'Purchase Order Report', 3, 'bi-bag-check',             @Folder + 'PurchaseOrderReport.html'),
    (724, 'Purchase Reports', 8, 'GRN Report',            4, 'bi-box-seam',              @Folder + 'GRNReport.html'),
    (725, 'Purchase Reports', 8, 'DC Report',             5, 'bi-truck',                 @Folder + 'DCReport.html'),
    (726, 'Purchase Reports', 8, 'RGP Report',            6, 'bi-box-arrow-right',       @Folder + 'RGPReport.html'),
    (727, 'Purchase Reports', 8, 'RGP Inward Report',     7, 'bi-box-arrow-in-left',     @Folder + 'RGPInwardReport.html'),
    (728, 'Purchase Reports', 8, 'Issue Report',          8, 'bi-box-arrow-up-right',    @Folder + 'IssueReport.html');

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
-- Every template that already holds grants gets full access to the reports,
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
