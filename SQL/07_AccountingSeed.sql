/*==============================================================================
  EMSSoft — 07_AccountingSeed.sql
  ------------------------------------------------------------------------------
  A working chart of accounts so the accounting screens have something to open:

      GroupHeads      15 primary groups + the usual sub-groups
      AccountsMaster  cash / bank / GST / round-off / trading ledgers
      TaxMaster       the standard GST slabs, wired to the GST ledgers
      AccountConfig   default module-to-ledger mappings

  Everything is seeded under SubscID = 1 and is idempotent — re-running only
  adds what is missing. Identity values are never assumed; rows are looked up by
  name so the script survives a partially populated database.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Subsc INT = 1;
DECLARE @User  INT = 1;


/*------------------------------------------------------------------------------
  1. Primary groups
------------------------------------------------------------------------------*/
;WITH P(GrpName, BSStatus, FAStatus) AS
(
    SELECT 'Capital Account',        'Balance Sheet', 'N' UNION ALL
    SELECT 'Loans (Liability)',      'Balance Sheet', 'N' UNION ALL
    SELECT 'Current Liabilities',    'Balance Sheet', 'N' UNION ALL
    SELECT 'Fixed Assets',           'Balance Sheet', 'Y' UNION ALL
    SELECT 'Investments',            'Balance Sheet', 'N' UNION ALL
    SELECT 'Current Assets',         'Balance Sheet', 'N' UNION ALL
    SELECT 'Branch / Divisions',     'Balance Sheet', 'N' UNION ALL
    SELECT 'Misc. Expenses (Asset)', 'Balance Sheet', 'N' UNION ALL
    SELECT 'Suspense A/c',           'Balance Sheet', 'N' UNION ALL
    SELECT 'Sales Accounts',         'Profit & Loss', 'N' UNION ALL
    SELECT 'Purchase Accounts',      'Profit & Loss', 'N' UNION ALL
    SELECT 'Direct Incomes',         'Profit & Loss', 'N' UNION ALL
    SELECT 'Direct Expenses',        'Profit & Loss', 'N' UNION ALL
    SELECT 'Indirect Incomes',       'Profit & Loss', 'N' UNION ALL
    SELECT 'Indirect Expenses',      'Profit & Loss', 'N'
)
INSERT INTO dbo.GroupHeads (GrpName, GrpMastNo, GrpStatus, BSStatus, FAStatus,
                            LockStatus, DeleteStatus, LockCounter, CreatedBy, SubscID)
SELECT P.GrpName, NULL, 'P', P.BSStatus, P.FAStatus, 'Y', 'N', 1, @User, @Subsc
  FROM P
 WHERE NOT EXISTS (SELECT 1 FROM dbo.GroupHeads g
                    WHERE g.GrpName = P.GrpName AND g.DeleteStatus = 'N' AND g.SubscID = @Subsc);

PRINT 'GroupHeads: primary groups seeded.';


/*------------------------------------------------------------------------------
  2. Sub-groups — parents resolved by name, never by a hard-coded GrpNo
------------------------------------------------------------------------------*/
;WITH S(GrpName, ParentName, BSStatus) AS
(
    SELECT 'Reserves & Surplus',        'Capital Account',     'Balance Sheet' UNION ALL
    SELECT 'Secured Loans',             'Loans (Liability)',   'Balance Sheet' UNION ALL
    SELECT 'Unsecured Loans',           'Loans (Liability)',   'Balance Sheet' UNION ALL
    SELECT 'Bank OD A/c',               'Loans (Liability)',   'Balance Sheet' UNION ALL
    SELECT 'Duties & Taxes',            'Current Liabilities', 'Balance Sheet' UNION ALL
    SELECT 'Sundry Creditors',          'Current Liabilities', 'Balance Sheet' UNION ALL
    SELECT 'Provisions',                'Current Liabilities', 'Balance Sheet' UNION ALL
    SELECT 'Bank Accounts',             'Current Assets',      'Balance Sheet' UNION ALL
    SELECT 'Cash-in-Hand',              'Current Assets',      'Balance Sheet' UNION ALL
    SELECT 'Sundry Debtors',            'Current Assets',      'Balance Sheet' UNION ALL
    SELECT 'Stock-in-Hand',             'Current Assets',      'Balance Sheet' UNION ALL
    SELECT 'Loans & Advances (Asset)',  'Current Assets',      'Balance Sheet'
)
INSERT INTO dbo.GroupHeads (GrpName, GrpMastNo, GrpStatus, BSStatus, FAStatus,
                            LockStatus, DeleteStatus, LockCounter, CreatedBy, SubscID)
SELECT S.GrpName, p.GrpNo, 'S', S.BSStatus, 'N', 'Y', 'N', 1, @User, @Subsc
  FROM S
  JOIN dbo.GroupHeads p
    ON p.GrpName = S.ParentName AND p.DeleteStatus = 'N' AND p.SubscID = @Subsc
 WHERE NOT EXISTS (SELECT 1 FROM dbo.GroupHeads g
                    WHERE g.GrpName = S.GrpName AND g.DeleteStatus = 'N' AND g.SubscID = @Subsc);

PRINT 'GroupHeads: sub-groups seeded.';


/*------------------------------------------------------------------------------
  3. Ledger accounts
------------------------------------------------------------------------------*/
;WITH A(AcName, AcAlias, AcType, GroupName) AS
(
    SELECT 'Cash',                  'CASH',      'CASH',      'Cash-in-Hand'      UNION ALL
    SELECT 'Petty Cash',            'PCASH',     'CASH',      'Cash-in-Hand'      UNION ALL
    SELECT 'Bank Current A/c',      'BANK',      'BANK',      'Bank Accounts'     UNION ALL

    SELECT 'CGST Output',           'CGSTOUT',   'Liability', 'Duties & Taxes'    UNION ALL
    SELECT 'SGST Output',           'SGSTOUT',   'Liability', 'Duties & Taxes'    UNION ALL
    SELECT 'IGST Output',           'IGSTOUT',   'Liability', 'Duties & Taxes'    UNION ALL
    SELECT 'Cess Output',           'CESSOUT',   'Liability', 'Duties & Taxes'    UNION ALL
    SELECT 'CGST Input',            'CGSTIN',    'Asset',     'Duties & Taxes'    UNION ALL
    SELECT 'SGST Input',            'SGSTIN',    'Asset',     'Duties & Taxes'    UNION ALL
    SELECT 'IGST Input',            'IGSTIN',    'Asset',     'Duties & Taxes'    UNION ALL
    SELECT 'Cess Input',            'CESSIN',    'Asset',     'Duties & Taxes'    UNION ALL
    SELECT 'TDS Payable',           'TDSPAY',    'Liability', 'Duties & Taxes'    UNION ALL

    SELECT 'Sales',                 'SALES',     'Income',    'Sales Accounts'    UNION ALL
    SELECT 'Sales Return',          'SALESRET',  'Income',    'Sales Accounts'    UNION ALL
    SELECT 'Purchase',              'PURCHASE',  'Expense',   'Purchase Accounts' UNION ALL
    SELECT 'Purchase Return',       'PURCHRET',  'Expense',   'Purchase Accounts' UNION ALL

    SELECT 'Round Off',             'ROUNDOFF',  'Expense',   'Indirect Expenses' UNION ALL
    SELECT 'Discount Allowed',      'DISCALLOW', 'Expense',   'Indirect Expenses' UNION ALL
    SELECT 'Discount Received',     'DISCRECD',  'Income',    'Indirect Incomes'  UNION ALL
    SELECT 'Freight & Cartage',     'FREIGHT',   'Expense',   'Direct Expenses'   UNION ALL
    SELECT 'Opening Stock',         'OPSTOCK',   'Asset',     'Stock-in-Hand'     UNION ALL
    SELECT 'Closing Stock',         'CLSTOCK',   'Asset',     'Stock-in-Hand'
)
INSERT INTO dbo.AccountsMaster (AcName, AcAlias, AcType, GroupHeadNo, LockStatus,
                                DeleteStatus, LockCounter, CreatedBy, SubscID)
SELECT A.AcName, A.AcAlias, A.AcType, g.GrpNo, 'Y', 'N', 1, @User, @Subsc
  FROM A
  JOIN dbo.GroupHeads g
    ON g.GrpName = A.GroupName AND g.DeleteStatus = 'N' AND g.SubscID = @Subsc
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccountsMaster a
                    WHERE a.AcName = A.AcName AND a.DeleteStatus = 'N' AND a.SubscID = @Subsc);

PRINT 'AccountsMaster seeded.';


/*------------------------------------------------------------------------------
  4. GST slabs — output ledgers for sales, resolved by name
------------------------------------------------------------------------------*/
DECLARE @CGST INT, @SGST INT, @IGST INT, @Cess INT;

SELECT @CGST = AcNo FROM dbo.AccountsMaster WHERE AcName = 'CGST Output' AND DeleteStatus = 'N' AND SubscID = @Subsc;
SELECT @SGST = AcNo FROM dbo.AccountsMaster WHERE AcName = 'SGST Output' AND DeleteStatus = 'N' AND SubscID = @Subsc;
SELECT @IGST = AcNo FROM dbo.AccountsMaster WHERE AcName = 'IGST Output' AND DeleteStatus = 'N' AND SubscID = @Subsc;
SELECT @Cess = AcNo FROM dbo.AccountsMaster WHERE AcName = 'Cess Output' AND DeleteStatus = 'N' AND SubscID = @Subsc;

;WITH T(TaxCode, TaxP, CessP) AS
(
    SELECT 'GST 0',   0.00,  0.00 UNION ALL
    SELECT 'GST 0.25',0.25,  0.00 UNION ALL
    SELECT 'GST 3',   3.00,  0.00 UNION ALL
    SELECT 'GST 5',   5.00,  0.00 UNION ALL
    SELECT 'GST 12', 12.00,  0.00 UNION ALL
    SELECT 'GST 18', 18.00,  0.00 UNION ALL
    SELECT 'GST 28', 28.00,  0.00
)
INSERT INTO dbo.TaxMaster (TaxCode, TaxP, CessP, CGSTAcNo, SGSTAcNo, IGSTAcNo, CessAcNo,
                           UsedIn, LockStatus, LockCounter, CreatedBy, DeleteStatus, SubscID)
SELECT T.TaxCode, T.TaxP, T.CessP, @CGST, @SGST, @IGST, @Cess,
       'Both', 'Y', 1, @User, 'N', @Subsc
  FROM T
 WHERE NOT EXISTS (SELECT 1 FROM dbo.TaxMaster t
                    WHERE t.TaxCode = T.TaxCode AND t.DeleteStatus = 'N' AND t.SubscID = @Subsc);

PRINT 'TaxMaster seeded.';


/*------------------------------------------------------------------------------
  5. Default module-to-ledger mappings
------------------------------------------------------------------------------*/
;WITH C(ModuleName, ACCType, AcName) AS
(
    SELECT 'Sales',    'SalesAccount',    'Sales'             UNION ALL
    SELECT 'Sales',    'RoundOff',        'Round Off'         UNION ALL
    SELECT 'Sales',    'Discount',        'Discount Allowed'  UNION ALL
    SELECT 'Sales',    'CGST',            'CGST Output'       UNION ALL
    SELECT 'Sales',    'SGST',            'SGST Output'       UNION ALL
    SELECT 'Sales',    'IGST',            'IGST Output'       UNION ALL
    SELECT 'Purchase', 'PurchaseAccount', 'Purchase'          UNION ALL
    SELECT 'Purchase', 'RoundOff',        'Round Off'         UNION ALL
    SELECT 'Purchase', 'Discount',        'Discount Received' UNION ALL
    SELECT 'Purchase', 'CGST',            'CGST Input'        UNION ALL
    SELECT 'Purchase', 'SGST',            'SGST Input'        UNION ALL
    SELECT 'Purchase', 'IGST',            'IGST Input'        UNION ALL
    SELECT 'Receipt',  'CashAccount',     'Cash'              UNION ALL
    SELECT 'Receipt',  'BankAccount',     'Bank Current A/c'  UNION ALL
    SELECT 'Payment',  'CashAccount',     'Cash'              UNION ALL
    SELECT 'Payment',  'BankAccount',     'Bank Current A/c'
)
INSERT INTO dbo.AccountConfig (ModuleName, ACCType, AcNo, SubscID)
SELECT C.ModuleName, C.ACCType, a.AcNo, @Subsc
  FROM C
  JOIN dbo.AccountsMaster a
    ON a.AcName = C.AcName AND a.DeleteStatus = 'N' AND a.SubscID = @Subsc
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccountConfig cfg
                    WHERE cfg.ModuleName = C.ModuleName
                      AND cfg.ACCType = C.ACCType
                      AND cfg.SubscID = @Subsc);

PRINT 'AccountConfig seeded.';


/*------------------------------------------------------------------------------
  6. Menu entries for the new modules, plus grants for the Administrator template
------------------------------------------------------------------------------*/
;WITH MENU(MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage) AS
(
    SELECT 501, 'EMS', 1, 'Accounts',  5, 'Group Heads',           1, 'bi-diagram-2',    'Modules/Accounts/GroupHeads.html'      UNION ALL
    SELECT 502, 'EMS', 1, 'Accounts',  5, 'Account Master',        2, 'bi-journal-text', 'Modules/Accounts/AccountMaster.html'   UNION ALL
    SELECT 503, 'EMS', 1, 'Accounts',  5, 'Tax Master',            3, 'bi-percent',      'Modules/Accounts/TaxMaster.html'       UNION ALL
    SELECT 504, 'EMS', 1, 'Accounts',  5, 'Account Configuration', 4, 'bi-sliders',      'Modules/Accounts/AccountConfig.html'   UNION ALL
    SELECT 601, 'EMS', 1, 'Utilities', 3, 'Data Backup',           4, 'bi-hdd',          'Modules/Utilities/DataBackup.html'     UNION ALL
    SELECT 602, 'EMS', 1, 'Utilities', 3, 'Table Update',          5, 'bi-table',        'Modules/Utilities/TableUpdate.html'
)
INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT MENU.MTransNo, MENU.Category, MENU.CSLNo, MENU.Section, MENU.SSLNo,
       MENU.ModuleName, MENU.MSLNo, MENU.ModuleIcon, MENU.ModulePage
  FROM MENU
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist t WHERE t.MTransNo = MENU.MTransNo);

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT 'Administrator', 'Admin', m.MTransNo, 'A', 'N', @Subsc
  FROM dbo.Menulist m
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate t
                    WHERE t.TemplateName = 'Administrator' AND t.ModuleNo = m.MTransNo AND t.SubscID = @Subsc);

PRINT 'Menulist / AccessTemplate updated for the accounting modules.';
GO

PRINT '07_AccountingSeed.sql complete.';
GO
