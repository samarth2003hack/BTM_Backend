/*==============================================================================
  51. SALES RETURN REPORT MENU  -  adds SalesReturnReport.html to the
      "Sales Reports" sidebar section created by 27_SalesReportsMenu.sql.

  The page reads GET api/SalesReturnReport/AllReports (SalesReportsController,
  inline SQL over SalesReturnMst / SalesReturnDtl). No stored procedures or
  tables are needed, only the menu row and its AccessTemplate grants (a
  Menulist row alone is invisible, see 25_SalesMenuAndLookups.sql).

  It sits right after "Sales Report" (MSLNo 8); the three attachment viewers
  move down to 9-11 (27_SalesReportsMenu.sql carries the same numbers).

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - same value as in script 27.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops row 821 (as it does 811-820).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Folder VARCHAR(60) = 'Modules/SalesReports/';   -- <- check, see header

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
    (821, 'Sales Reports', 7, 'Sales Return Report', 8, 'bi-arrow-return-left', @Folder + 'SalesReturnReport.html');

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

-- Attachment viewers follow the new report.
UPDATE dbo.Menulist SET MSLNo = 9  WHERE MTransNo = 818;
UPDATE dbo.Menulist SET MSLNo = 10 WHERE MTransNo = 819;
UPDATE dbo.Menulist SET MSLNo = 11 WHERE MTransNo = 820;

-- Same batch as the MERGE (no GO): @Menu would not survive a batch boundary.
-- Every template that already holds grants gets full access to the report,
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
