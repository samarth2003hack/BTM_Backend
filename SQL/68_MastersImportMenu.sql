/*==============================================================================
  68. MASTERS MENU - data import pages

  Adds the three data-import pages to the Masters section, after Other Master
  and before Masters Report List, with their existing icon. Same pages that
  are already under BIS Utilities; those rows are left as they are.

  Ordering (MSLNo) of the Masters section after this script:
      210 Other Master
      220 Customer Data Import     (new)
      230 Supplier Data Import     (new)
      240 Part Data Import         (new)
      250 Masters Report List      (moved from 220)

  Rows are matched by page path; new rows take the next free MTransNo.
  Grants full access ('A') to every access template that holds grants.
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 68_MastersImportMenu.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Menu TABLE (Seq INT IDENTITY(1,1), ModuleName VARCHAR(50), MSLNo INT, ModuleIcon VARCHAR(50), ModulePage VARCHAR(100));
INSERT INTO @Menu (ModuleName, MSLNo, ModuleIcon, ModulePage) VALUES
    ('Customer Data Import', 220, 'bi-file-earmark-arrow-up', 'Modules/Masters/CustomerDataImport.html'),
    ('Supplier Data Import', 230, 'bi-file-earmark-arrow-up', 'Modules/Masters/SupplierDataImport.html'),
    ('Part Data Import',     240, 'bi-file-earmark-arrow-up', 'Modules/Masters/PartDataImport.html');

-- Masters Report List moves to the end of the group.
UPDATE dbo.Menulist
   SET MSLNo = 250
 WHERE Category = 'EMS' AND Section = 'Masters' AND ModulePage = 'Modules/Masters/MasterReportList.html'
   AND ISNULL(MSLNo, -1) <> 250;
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' row(s) moved to the end of Masters.';

-- The three pages already have menu rows (BIS Utilities, MTransNo 15-17).
-- They are MOVED into Masters, keeping their MTransNo and access grants.
UPDATE m
   SET m.Section  = 'Masters',
       m.SSLNo    = (SELECT TOP (1) x.SSLNo FROM dbo.Menulist x WHERE x.Category = 'EMS' AND x.Section = 'Masters'),
       m.MSLNo    = n.MSLNo,
       m.ModuleName = n.ModuleName,
       m.ModuleIcon = n.ModuleIcon
  FROM dbo.Menulist m
  JOIN @Menu n ON n.ModulePage = m.ModulePage
 WHERE m.Category = 'EMS';
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) moved into Masters.';

-- Any of the three that has no row at all is added.
DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);
INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT @Max + ROW_NUMBER() OVER (ORDER BY n.Seq), 'EMS', 1, 'Masters',
       (SELECT TOP (1) SSLNo FROM dbo.Menulist WHERE Category = 'EMS' AND Section = 'Masters'),
       n.ModuleName, n.MSLNo, n.ModuleIcon, n.ModulePage
  FROM @Menu n
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.ModulePage = n.ModulePage);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) added.';

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT MTransNo FROM dbo.Menulist
              WHERE ModulePage IN (SELECT ModulePage FROM @Menu)) M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added.';

SELECT m.MTransNo, m.MSLNo, m.ModuleName, m.ModuleIcon, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.Category = 'EMS' AND m.Section = 'Masters'
 ORDER BY m.MSLNo;
