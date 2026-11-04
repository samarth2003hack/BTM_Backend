/*==============================================================================
  EMSSoft — 11_MenulistFolders.sql
  ------------------------------------------------------------------------------
  Rebuilds dbo.Menulist as the full navigation tree for the four Modules
  folders, one Section per folder:

      Modules/Masters/          -> Section 'Masters'         (SSLNo 1)  23 modules
      Modules/Administration/   -> Section 'Administration'  (SSLNo 2)   6 modules
      Modules/Accounts/         -> Section 'Accounts'        (SSLNo 3)   8 modules
      Modules/Utilities/        -> Section 'Utilities'       (SSLNo 4)   5 modules

  The @Menu table below is the whole truth: anything under Category 'EMS' that
  is not in it is removed, and everything in it is inserted or re-pointed.

  ------------------------------------------------------------------------------
  NOT EVERY PAGE EXISTS YET
  ------------------------------------------------------------------------------
  Only 8 of the 42 .html files are built today (the five Accounts screens and
  the three Utilities screens). The rest are listed so the sidebar shows the
  whole application structure as the pages are written — until then those links
  land on a 404. This is deliberate; see the completeness query at the foot of
  this script, which reports the menu grouped by section.

  ------------------------------------------------------------------------------
  MTransNo
  ------------------------------------------------------------------------------
  Hand-assigned, and the value AccessTemplate.ModuleNo points at. Every number
  here either keeps the module it already had (101-106, 201-203, 301-303,
  501-504, 601-602 from the original seeds; 502 / 505-508 from the current
  table) or is new (107-123, 204-206). No number is ever pointed at a DIFFERENT
  module than before, so a surviving grant can never silently change meaning.

  Idempotent — safe to re-run. Run AFTER 07_AccountingSeed.sql.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Subsc INT = 1;
DECLARE @Category VARCHAR(50) = 'EMS';

/*------------------------------------------------------------------------------
  The desired menu.
------------------------------------------------------------------------------*/
DECLARE @Menu TABLE
(
    MTransNo   INT          NOT NULL PRIMARY KEY,
    Category   VARCHAR(50)  NULL,
    CSLNo      INT          NULL,
    Section    VARCHAR(50)  NULL,
    SSLNo      INT          NULL,
    ModuleName VARCHAR(50)  NULL,
    MSLNo      INT          NULL,
    ModuleIcon VARCHAR(50)  NULL,
    ModulePage VARCHAR(100) NULL
);

INSERT INTO @Menu (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)

/*--- Masters (SSLNo 1) — Modules/Masters/ ------------------------------------*/
SELECT 101, @Category, 1, 'Masters', 1, 'Company Master',        1, 'bi-building',            'Modules/Masters/CompanyMaster.html'       UNION ALL
SELECT 102, @Category, 1, 'Masters', 1, 'Branch Master',         2, 'bi-diagram-3',           'Modules/Masters/BranchMaster.html'        UNION ALL
SELECT 103, @Category, 1, 'Masters', 1, 'Bank Master',           3, 'bi-bank',                'Modules/Masters/BankMaster.html'          UNION ALL
SELECT 104, @Category, 1, 'Masters', 1, 'Currency Master',       4, 'bi-currency-exchange',   'Modules/Masters/CurrencyMaster.html'      UNION ALL
SELECT 105, @Category, 1, 'Masters', 1, 'State Master',          5, 'bi-geo-alt',             'Modules/Masters/StateMaster.html'         UNION ALL
SELECT 106, @Category, 1, 'Masters', 1, 'Other Master',          6, 'bi-list-ul',             'Modules/Masters/OtherMaster.html'         UNION ALL
SELECT 107, @Category, 1, 'Masters', 1, 'Customer Master',       7, 'bi-person-vcard',        'Modules/Masters/CustomerMaster.html'      UNION ALL
SELECT 108, @Category, 1, 'Masters', 1, 'Supplier Master',       8, 'bi-truck',               'Modules/Masters/SupplierMaster.html'      UNION ALL
SELECT 109, @Category, 1, 'Masters', 1, 'Part Master',           9, 'bi-box-seam',            'Modules/Masters/PartMaster.html'          UNION ALL
SELECT 110, @Category, 1, 'Masters', 1, 'Part Code Master',     10, 'bi-upc-scan',            'Modules/Masters/PartCodeMaster.html'      UNION ALL
SELECT 111, @Category, 1, 'Masters', 1, 'Part Parameter List',  11, 'bi-sliders2',            'Modules/Masters/PartParameterList.html'   UNION ALL
SELECT 112, @Category, 1, 'Masters', 1, 'HSN Master',           12, 'bi-upc',                 'Modules/Masters/HSNMaster.html'           UNION ALL
SELECT 113, @Category, 1, 'Masters', 1, 'Material Standard',    13, 'bi-rulers',              'Modules/Masters/MaterialStd.html'         UNION ALL
SELECT 114, @Category, 1, 'Masters', 1, 'Material Grade',       14, 'bi-award',               'Modules/Masters/MaterialGrade.html'       UNION ALL
SELECT 115, @Category, 1, 'Masters', 1, 'Material Composition', 15, 'bi-droplet-half',        'Modules/Masters/MaterialComposition.html' UNION ALL
SELECT 116, @Category, 1, 'Masters', 1, 'Chemical Master',      16, 'bi-thermometer-half',    'Modules/Masters/ChemicalMaster.html'      UNION ALL
SELECT 117, @Category, 1, 'Masters', 1, 'Mechanical Master',    17, 'bi-gear-wide-connected', 'Modules/Masters/MechanicalMaster.html'    UNION ALL
SELECT 118, @Category, 1, 'Masters', 1, 'MOC Master',           18, 'bi-layers',              'Modules/Masters/MOCMaster.html'           UNION ALL
SELECT 119, @Category, 1, 'Masters', 1, 'MOC Template',         19, 'bi-file-earmark-ruled',  'Modules/Masters/MOCTemplate.html'         UNION ALL
SELECT 120, @Category, 1, 'Masters', 1, 'Drawing Register',     20, 'bi-file-earmark-image',  'Modules/Masters/DrawingRegister.html'     UNION ALL
SELECT 121, @Category, 1, 'Masters', 1, 'Standard Terms',       21, 'bi-file-text',           'Modules/Masters/StandardTerms.html'       UNION ALL
SELECT 122, @Category, 1, 'Masters', 1, 'Price List',           22, 'bi-tags',                'Modules/Masters/PriceList.html'           UNION ALL
SELECT 123, @Category, 1, 'Masters', 1, 'Sales Team',           23, 'bi-people-fill',         'Modules/Masters/SalesTeam.html'           UNION ALL

/*--- Administration (SSLNo 2) — Modules/Administration/ ----------------------*/
SELECT 201, @Category, 1, 'Administration', 2, 'User Management',    1, 'bi-people',             'Modules/Administration/UserMaster.html'     UNION ALL
SELECT 202, @Category, 1, 'Administration', 2, 'Access Template',    2, 'bi-shield-lock',        'Modules/Administration/AccessTemplate.html' UNION ALL
SELECT 203, @Category, 1, 'Administration', 2, 'E-Invoice Setup',    3, 'bi-receipt',            'Modules/Administration/EinvSetup.html'      UNION ALL
SELECT 204, @Category, 1, 'Administration', 2, 'E-Invoice Register', 4, 'bi-file-earmark-text',  'Modules/Administration/EinvRegister.html'   UNION ALL
SELECT 205, @Category, 1, 'Administration', 2, 'Access Log',         5, 'bi-clock-history',      'Modules/Administration/AccessLog.html'      UNION ALL
SELECT 206, @Category, 1, 'Administration', 2, 'Error Log',          6, 'bi-bug',                'Modules/Administration/ErrorLog.html'       UNION ALL

/*--- Accounts (SSLNo 3) — Modules/Accounts/ ----------------------------------*/
SELECT 505, @Category, 1, 'Accounts', 3, 'Dashboard',             1, 'bi-speedometer2',      'Modules/Accounts/Dashboard.html'     UNION ALL
SELECT 501, @Category, 1, 'Accounts', 3, 'Group Heads',           2, 'bi-diagram-2',         'Modules/Accounts/GroupHeads.html'    UNION ALL
SELECT 502, @Category, 1, 'Accounts', 3, 'Account Master',        3, 'bi-journal-bookmark',  'Modules/Accounts/AccountMaster.html' UNION ALL
SELECT 503, @Category, 1, 'Accounts', 3, 'Tax Master',            4, 'bi-percent',           'Modules/Accounts/TaxMaster.html'     UNION ALL
SELECT 504, @Category, 1, 'Accounts', 3, 'Account Configuration', 5, 'bi-sliders',           'Modules/Accounts/AccountConfig.html' UNION ALL
SELECT 506, @Category, 1, 'Accounts', 3, 'Purchase A/C',          6, 'bi-cart-check',        'Modules/Accounts/PurchaseAC.html'    UNION ALL
SELECT 507, @Category, 1, 'Accounts', 3, 'Account Books',         7, 'bi-book',              'Modules/Accounts/AccountBooks.html'  UNION ALL
SELECT 508, @Category, 1, 'Accounts', 3, 'General Ledger',        8, 'bi-file-earmark-text', 'Modules/Accounts/GeneralLedger.html' UNION ALL

/*--- Utilities (SSLNo 4) — Modules/Utilities/ --------------------------------*/
SELECT 301, @Category, 1, 'Utilities', 4, 'General Configuration', 1, 'bi-gear',              'Modules/Utilities/GeneralConfig.html'   UNION ALL
SELECT 302, @Category, 1, 'Utilities', 4, 'Menu List',             2, 'bi-menu-button-wide',  'Modules/Utilities/MenuList.html'        UNION ALL
SELECT 303, @Category, 1, 'Utilities', 4, 'Other Master List',     3, 'bi-card-list',         'Modules/Utilities/OtherMasterList.html' UNION ALL
SELECT 601, @Category, 1, 'Utilities', 4, 'Data Backup',           4, 'bi-hdd',               'Modules/Utilities/DataBackup.html'      UNION ALL
SELECT 602, @Category, 1, 'Utilities', 4, 'Table Update',          5, 'bi-table',             'Modules/Utilities/TableUpdate.html';


/*------------------------------------------------------------------------------
  1. Remove modules that are no longer in the tree.

     Scoped to Category 'EMS' so a menu belonging to another product is never
     touched. AccessTemplate rows go first: ModuleNo has no FK to Menulist, so
     orphaned grants would otherwise survive and keep matching if the number
     were ever reissued.
------------------------------------------------------------------------------*/
DECLARE @Stale TABLE (MTransNo INT NOT NULL PRIMARY KEY);

INSERT INTO @Stale (MTransNo)
SELECT m.MTransNo
  FROM dbo.Menulist m
 WHERE ISNULL(m.Category, @Category) = @Category
   AND NOT EXISTS (SELECT 1 FROM @Menu d WHERE d.MTransNo = m.MTransNo);

DELETE FROM dbo.AccessTemplate WHERE ModuleNo IN (SELECT MTransNo FROM @Stale);
DELETE FROM dbo.Menulist       WHERE MTransNo IN (SELECT MTransNo FROM @Stale);

PRINT 'Menulist: removed ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' module(s) no longer in the tree.';


/*------------------------------------------------------------------------------
  2. Re-point the modules that survived.

     A module can keep its number but change Section, ordering or icon, so an
     UPDATE is required — "insert if missing" alone would leave it in the old
     position.

     The NOT EXISTS/INTERSECT pair is the null-safe "these two rows differ"
     test. Written out as `m.Category <> d.Category OR …` it would miss every
     change involving a NULL, and `IS DISTINCT FROM` needs SQL Server 2022.
------------------------------------------------------------------------------*/
UPDATE m
   SET m.Category   = d.Category,
       m.CSLNo      = d.CSLNo,
       m.Section    = d.Section,
       m.SSLNo      = d.SSLNo,
       m.ModuleName = d.ModuleName,
       m.MSLNo      = d.MSLNo,
       m.ModuleIcon = d.ModuleIcon,
       m.ModulePage = d.ModulePage
  FROM dbo.Menulist m
  JOIN @Menu d ON d.MTransNo = m.MTransNo
 WHERE NOT EXISTS
       (
           SELECT m.Category, m.CSLNo, m.Section, m.SSLNo,
                  m.ModuleName, m.MSLNo, m.ModuleIcon, m.ModulePage
           INTERSECT
           SELECT d.Category, d.CSLNo, d.Section, d.SSLNo,
                  d.ModuleName, d.MSLNo, d.ModuleIcon, d.ModulePage
       );

PRINT 'Menulist: updated ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' existing module(s).';


/*------------------------------------------------------------------------------
  3. Add the modules that are missing.
------------------------------------------------------------------------------*/
INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT d.MTransNo, d.Category, d.CSLNo, d.Section, d.SSLNo, d.ModuleName, d.MSLNo, d.ModuleIcon, d.ModulePage
  FROM @Menu d
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.MTransNo = d.MTransNo);

PRINT 'Menulist: added ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' new module(s).';


/*------------------------------------------------------------------------------
  4. Grant the Administrator template everything.

     GetMenuList joins Menulist to AccessTemplate for any user that HAS a
     template, so a module with no grant is invisible in the sidebar even to the
     seeded admin (whose UserMaster.AccessTemplate = 'Administrator').
------------------------------------------------------------------------------*/
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT 'Administrator', 'Admin', m.MTransNo, 'A', 'N', @Subsc
  FROM dbo.Menulist m
 WHERE ISNULL(m.Category, @Category) = @Category
   AND NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate t
                    WHERE t.TemplateName = 'Administrator'
                      AND t.ModuleNo = m.MTransNo
                      AND t.SubscID = @Subsc);

PRINT 'AccessTemplate: granted ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' new module(s) to Administrator.';
GO


/*------------------------------------------------------------------------------
  The resulting menu, by section.
------------------------------------------------------------------------------*/
SELECT Section, SSLNo, COUNT(*) AS Modules
  FROM dbo.Menulist
 WHERE ISNULL(Category, 'EMS') = 'EMS'
 GROUP BY Section, SSLNo
 ORDER BY SSLNo;

SELECT Section, MSLNo, MTransNo, ModuleName, ModuleIcon, ModulePage
  FROM dbo.Menulist
 WHERE ISNULL(Category, 'EMS') = 'EMS'
 ORDER BY CSLNo, SSLNo, MSLNo;
GO

PRINT '11_MenulistFolders.sql complete.';
GO
