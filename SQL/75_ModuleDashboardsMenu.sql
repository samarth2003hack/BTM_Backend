/*==============================================================================
  75. MENU - Production, Maintenance, Inventory and Quality dashboards

  Adds a "Dashboard" row at the top (MSLNo 0) of four sections:
      Production   Modules/Production/Dashboard.html
      Maintenance  Modules/Maintenance/MaintenanceDashboard.html
      Store        Modules/Store/InventoryDashboard.html
      Quality      Modules/Quality/QualityDashboard.html

  APIs: ProductionDashboardController, MaintenanceDashboardController,
  InventoryDashboardController, QualityDashboardController (read only). No
  table or procedure changes.

  Rows are matched by page path; a new row takes the next free MTransNo.
  Grants full access ('A') to every access template that holds grants, like
  every new page - take a dashboard off a template in Access Template if that
  role should not see it.
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 75_ModuleDashboardsMenu.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Rows TABLE (Section VARCHAR(50), Page VARCHAR(100));
INSERT INTO @Rows VALUES
    ('Production',  'Modules/Production/Dashboard.html'),
    ('Maintenance', 'Modules/Maintenance/MaintenanceDashboard.html'),
    ('Store',       'Modules/Store/InventoryDashboard.html'),
    ('Quality',     'Modules/Quality/QualityDashboard.html');

DECLARE @Section VARCHAR(50), @Page VARCHAR(100), @Max INT;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Section, Page FROM @Rows;
OPEN c;
FETCH NEXT FROM c INTO @Section, @Page;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Menulist WHERE ModulePage = @Page)
    BEGIN
        SET @Max = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);
        INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
        SELECT TOP (1) @Max + 1, 'EMS', s.CSLNo, @Section, s.SSLNo, 'Dashboard', 0, 'bi-speedometer2', @Page
          FROM dbo.Menulist s
         WHERE s.Category = 'EMS' AND s.Section = @Section
         ORDER BY s.MSLNo;
        PRINT @Section + ': ' + CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row added.';
    END
    ELSE
    BEGIN
        UPDATE dbo.Menulist SET Section = @Section, MSLNo = 0, ModuleName = 'Dashboard', ModuleIcon = 'bi-speedometer2'
         WHERE ModulePage = @Page;
        PRINT @Section + ': menu row already present - name, place and icon refreshed.';
    END
    FETCH NEXT FROM c INTO @Section, @Page;
END
CLOSE c;
DEALLOCATE c;

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT m.MTransNo FROM dbo.Menulist m JOIN @Rows r ON r.Page = m.ModulePage) M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added.';

SELECT m.MTransNo, m.Section, m.SSLNo, m.MSLNo, m.ModuleName, m.ModulePage
  FROM dbo.Menulist m JOIN @Rows r ON r.Page = m.ModulePage
 ORDER BY m.SSLNo;
