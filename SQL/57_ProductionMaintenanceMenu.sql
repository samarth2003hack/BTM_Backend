/*==============================================================================
  57. PRODUCTION / MAINTENANCE / REPORTS  -  menu rows

  Rows + AccessTemplate grants for the pages that went live with SQL 52-56:

      Masters             (SSLNo 1)   127 Machine Master, 128 Die Master,
                                      129 Routing Chart, 130 PM Checklist Master
      Production          (SSLNo 9)   903 Conversion BOM, 904 Job Card,
                                      905 Job Card Status, 906 Job Card Inspection
                                      (901 / 902 MRN pages come from SQL 49)
      Maintenance         (SSLNo 10)  911 PM Check Sheet, 912 Breakdown Maintenance
      Production Reports  (SSLNo 11)  921 Job Card Report,
                                      922 Job Card Inspection Report, 923 MRN Report
      Maintenance Reports (SSLNo 12)  931 Machine Maintenance Report,
                                      932 Machine History Card

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * Paths are relative to the EMSSoft root (Modules/Masters, Modules/Production,
    Modules/Maintenance, Modules/ProductionReports, Modules/MaintenanceReports).
  * Modules/Quality/JobCardInspection.html is the same page as the Production
    one; only the Production path is on the menu.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops these rows (as it does 701-732 and 801-902).
  * The grant goes to every template that already holds grants, with
    Department copied from its existing rows (same rule as SQL 49).

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
    (127, 'Masters',              1, 'Machine Master',               27, 'bi-cpu',                 'Modules/Masters/MachineMaster.html'),
    (128, 'Masters',              1, 'Die Master',                   28, 'bi-hexagon',             'Modules/Masters/DieMaster.html'),
    (129, 'Masters',              1, 'Routing Chart',                29, 'bi-diagram-3',           'Modules/Masters/RoutingChart.html'),
    (130, 'Masters',              1, 'PM Checklist Master',          30, 'bi-ui-checks',           'Modules/Masters/PMChecklistMaster.html'),

    (903, 'Production',           9, 'Conversion BOM',                3, 'bi-diagram-2',           'Modules/Production/ConvBOM.html'),
    (904, 'Production',           9, 'Job Card',                      4, 'bi-card-checklist',      'Modules/Production/JobCard.html'),
    (905, 'Production',           9, 'Job Card Status',               5, 'bi-bar-chart-steps',     'Modules/Production/JobCardStatus.html'),
    (906, 'Production',           9, 'Job Card Inspection',           6, 'bi-patch-check',         'Modules/Production/JobCardInspection.html'),

    (911, 'Maintenance',         10, 'PM Check Sheet',                1, 'bi-clipboard-check',     'Modules/Maintenance/PMCheckSheet.html'),
    (912, 'Maintenance',         10, 'Breakdown Maintenance',         2, 'bi-tools',               'Modules/Maintenance/BreakdownMaintenance.html'),

    (921, 'Production Reports',  11, 'Job Card Report',               1, 'bi-file-earmark-bar-graph', 'Modules/ProductionReports/JobCardReport.html'),
    (922, 'Production Reports',  11, 'Job Card Inspection Report',    2, 'bi-file-earmark-check',  'Modules/ProductionReports/JobCardInspectionReport.html'),
    (923, 'Production Reports',  11, 'MRN Report',                    3, 'bi-file-earmark-text',   'Modules/ProductionReports/MRNReport.html'),

    (931, 'Maintenance Reports', 12, 'Machine Maintenance Report',    1, 'bi-graph-up',            'Modules/MaintenanceReports/MachineMaintenanceReport.html'),
    (932, 'Maintenance Reports', 12, 'Machine History Card',          2, 'bi-journal-text',        'Modules/MaintenanceReports/MachineHistoryCard.html');

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

-- What was written.
SELECT M.MTransNo, M.Section, M.SSLNo, M.ModuleName, M.MSLNo, M.ModulePage,
       (SELECT COUNT(*) FROM dbo.AccessTemplate A WHERE A.ModuleNo = M.MTransNo) AS Grants
  FROM dbo.Menulist M
 WHERE M.MTransNo IN (127, 128, 129, 130, 903, 904, 905, 906, 911, 912, 921, 922, 923, 931, 932)
 ORDER BY M.SSLNo, M.MSLNo;
GO
