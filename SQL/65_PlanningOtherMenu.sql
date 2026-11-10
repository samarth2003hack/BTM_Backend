/*==============================================================================
  65. PLANNING PAGES IN OTHER SECTIONS  -  menu rows (with icons) + access grants

  Completes the menu for the planning module (SQL 63 added the Production ones):
      Sales     77   Pending Order Report      bi-hourglass-split  Modules/SalesReports/PendingOrderReport.html
      Purchase 165   RM Requirement            bi-calculator       Modules/PurchaseReports/RMRequirementReport.html
      Purchase 170   RM Stock Statement        bi-archive          Modules/PurchaseReports/RMStockStatement.html
      Store      7   Customer Material Inward  bi-box-arrow-in-down Modules/Store/CustomerMaterialInward.html

  Grants full access ('A') to every access template that already holds grants
  (same rule as SQL 49 / 57 / 63). Rows are matched by page path; new rows take
  the next free MTransNo. Idempotent - safe to re-run.
  Run with:  sqlcmd -I -S <server> -d <db> -i 65_PlanningOtherMenu.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Menu TABLE (Seq INT IDENTITY(1,1), Section VARCHAR(50), ModuleName VARCHAR(50), MSLNo INT, ModuleIcon VARCHAR(50), ModulePage VARCHAR(100));
INSERT INTO @Menu (Section, ModuleName, MSLNo, ModuleIcon, ModulePage) VALUES
    ('Sales',    'Pending Order Report',     77,  'bi-hourglass-split',   'Modules/SalesReports/PendingOrderReport.html'),
    ('Purchase', 'RM Requirement',           165, 'bi-calculator',        'Modules/PurchaseReports/RMRequirementReport.html'),
    ('Purchase', 'RM Stock Statement',       170, 'bi-archive',           'Modules/PurchaseReports/RMStockStatement.html'),
    ('Store',    'Customer Material Inward', 7,   'bi-box-arrow-in-down', 'Modules/Store/CustomerMaterialInward.html');

DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);

INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT @Max + ROW_NUMBER() OVER (ORDER BY n.Seq), 'EMS', 1, n.Section,
       (SELECT TOP (1) SSLNo FROM dbo.Menulist WHERE Category = 'EMS' AND Section = n.Section),
       n.ModuleName, n.MSLNo, n.ModuleIcon, n.ModulePage
  FROM @Menu n
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.ModulePage = n.ModulePage)
   AND EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.Category = 'EMS' AND m.Section = n.Section);
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

SELECT m.MTransNo, m.Section, m.MSLNo, m.ModuleName, m.ModuleIcon, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.ModulePage IN (SELECT ModulePage FROM @Menu)
 ORDER BY m.Section, m.MSLNo;
