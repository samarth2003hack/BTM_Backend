/*==============================================================================
  33. STORES / INWARD MENU  -  sidebar rows for the eight inward pages

  Delivery Challan, RGP Outward, RGP Inward, GRN, GRN QC, GRN NC Closure,
  GRN Store and Heat No. now read live data (scripts 28-32), so they get
  menu rows and AccessTemplate grants (a Menulist row alone is invisible,
  see 25_SalesMenuAndLookups.sql).

  They join the Transactions section after Purchase Order (706), in the order
  material moves: out on a challan / gate pass, back in, then receipt,
  inspection, closure, store and heat traceability.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/Purchase/'.
    Rows 43 and 702-705 still point at 'Modules/Transactions/', which does
    not exist on the dev box. Set @Folder to wherever the deployed server
    serves the purchase pages from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 707-714 (as it does 701-706 and 801-820).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Folder VARCHAR(60) = 'Modules/Purchase/';   -- <- check, see header

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
    (707, 'Transactions', 5, 'Delivery Challan',  7, 'bi-truck',              @Folder + 'DC.html'),
    (708, 'Transactions', 5, 'RGP Outward',       8, 'bi-box-arrow-right',    @Folder + 'RGPOutward.html'),
    (709, 'Transactions', 5, 'RGP Inward',        9, 'bi-box-arrow-in-left',  @Folder + 'RGPInward.html'),
    (710, 'Transactions', 5, 'GRN',              10, 'bi-box-seam',           @Folder + 'GRN.html'),
    (711, 'Transactions', 5, 'GRN QC',           11, 'bi-clipboard-check',    @Folder + 'GRNQC.html'),
    (712, 'Transactions', 5, 'GRN NC Closure',   12, 'bi-x-octagon',          @Folder + 'GRNNCClosure.html'),
    (713, 'Transactions', 5, 'GRN Store',        13, 'bi-shop',               @Folder + 'GRNStore.html'),
    (714, 'Transactions', 5, 'Heat No.',         14, 'bi-fire',               @Folder + 'HeatNo.html');

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
