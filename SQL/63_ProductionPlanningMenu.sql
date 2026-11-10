/*==============================================================================
  63. PRODUCTION PLANNING  -  menu rows (with icons) + access grants

  Adds to the Production section (after 62_ProductionMenuOrder.sql):
      20  Production Plan       bi-kanban            Modules/Production/ProductionPlan.html
      30  Production Schedule   bi-calendar-week     Modules/Production/ProductionSchedule.html
      75  Plan vs Actual        bi-bar-chart-line    Modules/PurchaseReports/PlanVsActualReport.html

  Then grants full access ('A') to every access template that already holds
  grants (same rule as SQL 49 / 57) for those three pages and for Work Calendar
  (Modules/Masters/WorkCalendar.html), if its menu row exists.

  Rows are matched by page path; new rows take the next free MTransNo.
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 63_ProductionPlanningMenu.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Menu TABLE (Seq INT IDENTITY(1,1), ModuleName VARCHAR(50), MSLNo INT, ModuleIcon VARCHAR(50), ModulePage VARCHAR(100));
INSERT INTO @Menu (ModuleName, MSLNo, ModuleIcon, ModulePage) VALUES
    ('Production Plan',     20, 'bi-kanban',          'Modules/Production/ProductionPlan.html'),
    ('Production Schedule', 30, 'bi-calendar-week',   'Modules/Production/ProductionSchedule.html'),
    ('Plan vs Actual',      75, 'bi-bar-chart-line',  'Modules/PurchaseReports/PlanVsActualReport.html');

DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);

INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT @Max + ROW_NUMBER() OVER (ORDER BY n.Seq), 'EMS', 1, 'Production',
       (SELECT TOP (1) SSLNo FROM dbo.Menulist WHERE Category = 'EMS' AND Section = 'Production'),
       n.ModuleName, n.MSLNo, n.ModuleIcon, n.ModulePage
  FROM @Menu n
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.ModulePage = n.ModulePage);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) added.';

-- Every template that already holds grants gets full access to these pages.
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT MTransNo FROM dbo.Menulist
              WHERE ModulePage IN ('Modules/Production/ProductionPlan.html',
                                   'Modules/Production/ProductionSchedule.html',
                                   'Modules/PurchaseReports/PlanVsActualReport.html',
                                   'Modules/Masters/WorkCalendar.html')) M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added.';

SELECT m.MTransNo, m.MSLNo, m.ModuleName, m.ModuleIcon, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.Category = 'EMS' AND m.Section = 'Production'
 ORDER BY m.MSLNo;
