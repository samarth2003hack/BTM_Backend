/*==============================================================================
  EMSSoft — 04_SeedData.sql
  ------------------------------------------------------------------------------
  Minimum data needed to log in and navigate. Idempotent — safe to re-run.

  Everything is seeded under SubscID = 1.

  DEFAULT LOGIN
      User Id  : admin
      Password : Admin@123
      >>> Change this immediately via /api/Home/UpdatePassword. <<<

  The PW column stores a TripleDES/ECB/PKCS7 ciphertext (key "mskl-1sv4-xklq23"),
  which is what UserMasterController.Encrypt produces. The literal below is the
  ciphertext for 'Admin@123'.

  ------------------------------------------------------------------------------
  NOTE ON LockStatus
  ------------------------------------------------------------------------------
  In the login flow LockStatus is the ACTIVE flag, not a lock:
      'Y' = active and allowed to sign in
      'N' = inactive  ("kindly contact your reporting manager")
      'B' = blocked   ("contact backend admin")
  The column DEFAULT is 'N', so a user created without an explicit LockStatus
  cannot sign in until it is set to 'Y'. The seeded admin below sets it to 'Y'.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Subsc INT = 1;


/*------------------------------------------------------------------------------
  StateMaster — Indian states / UTs with their GST state codes
------------------------------------------------------------------------------*/
;WITH S(State, StateCode) AS
(
    SELECT 'Jammu and Kashmir', 1              UNION ALL
    SELECT 'Himachal Pradesh', 2               UNION ALL
    SELECT 'Punjab', 3                         UNION ALL
    SELECT 'Chandigarh', 4                     UNION ALL
    SELECT 'Uttarakhand', 5                    UNION ALL
    SELECT 'Haryana', 6                        UNION ALL
    SELECT 'Delhi', 7                          UNION ALL
    SELECT 'Rajasthan', 8                      UNION ALL
    SELECT 'Uttar Pradesh', 9                  UNION ALL
    SELECT 'Bihar', 10                         UNION ALL
    SELECT 'Sikkim', 11                        UNION ALL
    SELECT 'Arunachal Pradesh', 12             UNION ALL
    SELECT 'Nagaland', 13                      UNION ALL
    SELECT 'Manipur', 14                       UNION ALL
    SELECT 'Mizoram', 15                       UNION ALL
    SELECT 'Tripura', 16                       UNION ALL
    SELECT 'Meghalaya', 17                     UNION ALL
    SELECT 'Assam', 18                         UNION ALL
    SELECT 'West Bengal', 19                   UNION ALL
    SELECT 'Jharkhand', 20                     UNION ALL
    SELECT 'Odisha', 21                        UNION ALL
    SELECT 'Chhattisgarh', 22                  UNION ALL
    SELECT 'Madhya Pradesh', 23                UNION ALL
    SELECT 'Gujarat', 24                       UNION ALL
    SELECT 'Dadra and Nagar Haveli and Daman and Diu', 26 UNION ALL
    SELECT 'Maharashtra', 27                   UNION ALL
    SELECT 'Karnataka', 29                     UNION ALL
    SELECT 'Goa', 30                           UNION ALL
    SELECT 'Lakshadweep', 31                   UNION ALL
    SELECT 'Kerala', 32                        UNION ALL
    SELECT 'Tamil Nadu', 33                    UNION ALL
    SELECT 'Puducherry', 34                    UNION ALL
    SELECT 'Andaman and Nicobar Islands', 35   UNION ALL
    SELECT 'Telangana', 36                     UNION ALL
    SELECT 'Andhra Pradesh', 37                UNION ALL
    SELECT 'Ladakh', 38                        UNION ALL
    SELECT 'Other Territory', 97
)
INSERT INTO dbo.StateMaster (State, StateCode, CreatedBy, DeleteStatus, LockStatus, SubscID)
SELECT S.State, S.StateCode, 1, 'N', 'Y', @Subsc
  FROM S
 WHERE NOT EXISTS (SELECT 1 FROM dbo.StateMaster t
                    WHERE t.StateCode = S.StateCode AND t.DeleteStatus = 'N' AND t.SubscID = @Subsc);

PRINT 'StateMaster seeded.';


/*------------------------------------------------------------------------------
  CurrencyMaster — base currency plus the common trading currencies
------------------------------------------------------------------------------*/
;WITH C(Country, Currency, CurrencyCode, ExchangeRate) AS
(
    SELECT 'India',          'Indian Rupee',   'INR',  1.00 UNION ALL
    SELECT 'United States',  'US Dollar',      'USD', 83.00 UNION ALL
    SELECT 'Euro Zone',      'Euro',           'EUR', 90.00 UNION ALL
    SELECT 'United Kingdom', 'Pound Sterling', 'GBP',105.00 UNION ALL
    SELECT 'UAE',            'UAE Dirham',     'AED', 22.60
)
INSERT INTO dbo.CurrencyMaster (Country, Currency, CurrencyCode, ExchangeRate, EffectiveDate, CreatedBy, DeleteStatus, LockStatus, SubscID)
SELECT C.Country, C.Currency, C.CurrencyCode, C.ExchangeRate, CAST(GETDATE() AS DATE), 1, 'N', 'Y', @Subsc
  FROM C
 WHERE NOT EXISTS (SELECT 1 FROM dbo.CurrencyMaster t
                    WHERE t.CurrencyCode = C.CurrencyCode AND t.DeleteStatus = 'N' AND t.SubscID = @Subsc);

PRINT 'CurrencyMaster seeded.';


/*------------------------------------------------------------------------------
  OtherMasterList — the master types the Other Master page can edit
------------------------------------------------------------------------------*/
;WITH L(MasterType, Department) AS
(
    SELECT 'Department',   'Admin' UNION ALL
    SELECT 'Designation',  'Admin' UNION ALL
    SELECT 'User Type',    'Admin' UNION ALL
    SELECT 'Branch Type',  'Admin' UNION ALL
    SELECT 'Bank Type',    'Admin' UNION ALL
    SELECT 'Company Type', 'Admin'
)
INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT L.MasterType, L.Department, 'N', @Subsc
  FROM L
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OtherMasterList t
                    WHERE t.MasterType = L.MasterType AND t.Department = L.Department AND t.SubscID = @Subsc);

PRINT 'OtherMasterList seeded.';


/*------------------------------------------------------------------------------
  OtherMaster — starter values for the types above
------------------------------------------------------------------------------*/
;WITH M(MasterType, MasterName) AS
(
    SELECT 'Department',   'Accounts'        UNION ALL
    SELECT 'Department',   'Administration'  UNION ALL
    SELECT 'Department',   'Sales'           UNION ALL
    SELECT 'Department',   'Purchase'        UNION ALL
    SELECT 'Department',   'Stores'          UNION ALL
    SELECT 'Designation',  'Manager'         UNION ALL
    SELECT 'Designation',  'Executive'       UNION ALL
    SELECT 'Designation',  'Accountant'      UNION ALL
    SELECT 'User Type',    'ADMIN'           UNION ALL
    SELECT 'User Type',    'USER'            UNION ALL
    SELECT 'Branch Type',  'HEAD OFFICE'     UNION ALL
    SELECT 'Branch Type',  'BRANCH'          UNION ALL
    SELECT 'Branch Type',  'WAREHOUSE'       UNION ALL
    SELECT 'Bank Type',    'CURRENT'         UNION ALL
    SELECT 'Bank Type',    'SAVINGS'         UNION ALL
    SELECT 'Bank Type',    'OD'              UNION ALL
    SELECT 'Bank Type',    'CC'              UNION ALL
    SELECT 'Company Type', 'PRIVATE LIMITED' UNION ALL
    SELECT 'Company Type', 'PROPRIETORSHIP'  UNION ALL
    SELECT 'Company Type', 'PARTNERSHIP'     UNION ALL
    SELECT 'Company Type', 'LLP'
)
INSERT INTO dbo.OtherMaster (MasterType, MasterName, Department, CreatedBy, DeleteStatus, LockStatus, SubscID)
SELECT M.MasterType, M.MasterName, 'Admin', 1, 'N', 'Y', @Subsc
  FROM M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OtherMaster t
                    WHERE t.MasterType = M.MasterType AND t.MasterName = M.MasterName
                      AND t.DeleteStatus = 'N' AND t.SubscID = @Subsc);

PRINT 'OtherMaster seeded.';


/*------------------------------------------------------------------------------
  Menulist — navigation catalogue.
  MTransNo is the module number AccessTemplate.ModuleNo refers to; it is assigned
  by hand here so the numbers stay stable across environments.
------------------------------------------------------------------------------*/
;WITH MENU(MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage) AS
(
    /*--- Masters ---*/
    SELECT 101, 'EMS', 1, 'Masters',        1, 'Company Master',        1, 'bi-building',     'Modules/Masters/CompanyMaster.html'          UNION ALL
    SELECT 102, 'EMS', 1, 'Masters',        1, 'Branch Master',         2, 'bi-diagram-3',    'Modules/Masters/BranchMaster.html'           UNION ALL
    SELECT 103, 'EMS', 1, 'Masters',        1, 'Bank Master',           3, 'bi-bank',         'Modules/Masters/BankMaster.html'             UNION ALL
    SELECT 104, 'EMS', 1, 'Masters',        1, 'Currency Master',       4, 'bi-currency-exchange', 'Modules/Masters/CurrencyMaster.html'    UNION ALL
    SELECT 105, 'EMS', 1, 'Masters',        1, 'State Master',          5, 'bi-geo-alt',      'Modules/Masters/StateMaster.html'            UNION ALL
    SELECT 106, 'EMS', 1, 'Masters',        1, 'Other Master',          6, 'bi-list-ul',      'Modules/Masters/OtherMaster.html'            UNION ALL

    /*--- Administration ---*/
    SELECT 201, 'EMS', 1, 'Administration', 2, 'User Management',       1, 'bi-people',       'Modules/Administration/UserMaster.html'      UNION ALL
    SELECT 202, 'EMS', 1, 'Administration', 2, 'Access Template',       2, 'bi-shield-lock',  'Modules/Administration/AccessTemplate.html'  UNION ALL
    SELECT 203, 'EMS', 1, 'Administration', 2, 'E-Invoice Setup',       3, 'bi-receipt',      'Modules/Administration/EinvSetup.html'       UNION ALL

    /*--- Utilities ---*/
    SELECT 301, 'EMS', 1, 'Utilities',      3, 'General Configuration', 1, 'bi-gear',         'Modules/Utilities/GeneralConfig.html'        UNION ALL
    SELECT 302, 'EMS', 1, 'Utilities',      3, 'Menu List',             2, 'bi-menu-button',  'Modules/Utilities/MenuList.html'             UNION ALL
    SELECT 303, 'EMS', 1, 'Utilities',      3, 'Other Master List',     3, 'bi-card-list',    'Modules/Utilities/OtherMasterList.html'      UNION ALL

    /*--- Logs & Registers ---*/
    SELECT 401, 'EMS', 1, 'Logs',           4, 'Access Log',            1, 'bi-clock-history','Modules/Logs/AccessLog.html'                 UNION ALL
    SELECT 402, 'EMS', 1, 'Logs',           4, 'Error Log',             2, 'bi-bug',          'Modules/Logs/ErrorLog.html'                  UNION ALL
    SELECT 403, 'EMS', 1, 'Logs',           4, 'E-Invoice Register',    3, 'bi-file-earmark-text', 'Modules/Logs/EinvRegister.html'
)
INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT MENU.MTransNo, MENU.Category, MENU.CSLNo, MENU.Section, MENU.SSLNo,
       MENU.ModuleName, MENU.MSLNo, MENU.ModuleIcon, MENU.ModulePage
  FROM MENU
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist t WHERE t.MTransNo = MENU.MTransNo);

PRINT 'Menulist seeded.';


/*------------------------------------------------------------------------------
  AccessTemplate — grant the "Administrator" template full access to every module
      AuthType 'A' = full (add / edit / delete),  'V' = view only
------------------------------------------------------------------------------*/
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT 'Administrator', 'Admin', m.MTransNo, 'A', 'N', @Subsc
  FROM dbo.Menulist m
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate t
                    WHERE t.TemplateName = 'Administrator' AND t.ModuleNo = m.MTransNo AND t.SubscID = @Subsc);

PRINT 'AccessTemplate seeded.';


/*------------------------------------------------------------------------------
  UserMaster — default administrator
      LockStatus 'Y' = active (see the note at the top of this file)
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM dbo.UserMaster WHERE UserId = 'admin' AND DeleteStatus = 'N' AND SubscID = @Subsc)
BEGIN
    INSERT INTO dbo.UserMaster
    (
        UserName, UserId, PW, UserType, Designation, Department,
        ContactNo, EmailId, AccessTemplate, Branch,
        PWExpiry, BadLogins, LockCounter, HomePage,
        CreatedBy, DeleteStatus, LockStatus, SubscID
    )
    VALUES
    (
        'System Administrator', 'admin',
        '1uAE9XZfv/QDe4atfgI7zA==',                 -- TripleDES('Admin@123')
        'ADMIN', 'Administrator', 'Administration',
        NULL, NULL, 'Administrator', NULL,
        0, 5, 0, 'Modules/Home.html',
        1, 'N', 'Y', @Subsc
    );
    PRINT 'UserMaster seeded — login admin / Admin@123 (change this now).';
END
ELSE
    PRINT 'UserMaster: admin already present — skipped.';


/*------------------------------------------------------------------------------
  GeneralConfig — application-level settings
------------------------------------------------------------------------------*/
;WITH G(ConfigKey, ConfigValue, ConfigDepartment) AS
(
    SELECT 'AppName',          'EMSSoft',        'Admin' UNION ALL
    SELECT 'BaseCurrency',     'INR',            'Admin' UNION ALL
    SELECT 'DateFormat',       'dd-MM-yyyy',     'Admin' UNION ALL
    SELECT 'MaxBadLogins',     '5',              'Admin' UNION ALL
    SELECT 'PasswordExpiryDays','90',            'Admin' UNION ALL
    SELECT 'EinvEnabled',      'N',              'Admin'
)
INSERT INTO dbo.GeneralConfig (ConfigKey, ConfigValue, ConfigDepartment, CreatedBy, SubscID)
SELECT G.ConfigKey, G.ConfigValue, G.ConfigDepartment, 1, @Subsc
  FROM G
 WHERE NOT EXISTS (SELECT 1 FROM dbo.GeneralConfig t
                    WHERE t.ConfigKey = G.ConfigKey AND t.SubscID = @Subsc);

PRINT 'GeneralConfig seeded.';


/*------------------------------------------------------------------------------
  Company / BranchMaster — one demo row each so the app has a working context
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM dbo.Company WHERE CompCode = 'C001' AND DeleteStatus = 'N' AND SubscID = @Subsc)
    INSERT INTO dbo.Company
        (CompCode, CompName, TradingName, CompAddress, State, StateCode,
         TxnCurrency, FYear, FromYear, ToYear, YearCode,
         CreatedBy, DeleteStatus, LockStatus, SubscID)
    VALUES
        ('C001', 'Demo Enterprises Private Limited', 'Demo Enterprises',
         'Plot 1, Industrial Area', 'Karnataka', 29,
         'INR', 2026, '2026-04-01', '2027-03-31', '2026-2027',
         1, 'N', 'Y', @Subsc);

IF NOT EXISTS (SELECT 1 FROM dbo.BranchMaster WHERE BranchCode = 'B001' AND DeleteStatus = 'N' AND SubscID = @Subsc)
    INSERT INTO dbo.BranchMaster
        (BranchCode, BranchType, BranchName, BranchAddress,
         CreatedBy, DeleteStatus, LockStatus, SubscID)
    VALUES
        ('B001', 'HEAD OFFICE', 'Head Office', 'Plot 1, Industrial Area',
         1, 'N', 'Y', @Subsc);

PRINT 'Company / BranchMaster demo rows seeded.';
GO

PRINT '04_SeedData.sql complete.';
GO
