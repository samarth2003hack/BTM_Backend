/*==============================================================================
  EMSSoft — 02_Tables.sql
  ------------------------------------------------------------------------------
  Creates the 15 EMS tables, their constraints and their filtered indexes.
  Idempotent: every object is guarded by an existence check, so the script can
  be re-run safely.

  ------------------------------------------------------------------------------
  CORRECTIONS APPLIED TO THE SUPPLIED DDL
  ------------------------------------------------------------------------------
  The DDL as supplied does not compile as-is. The following were fixed; each fix
  is also flagged inline at the affected object with a  [FIX n]  marker.

    [FIX 1] dbo.EinvDtl
            A trailing comma after CONSTRAINT PK_EinvDtl ... made the CREATE
            TABLE a syntax error. Comma removed.

    [FIX 2] dbo.AccessTemplate
            - Index IX_TemplateMaster_TemplateName targeted a table named
              dbo.TemplateMaster, which is never created. Retargeted to
              dbo.AccessTemplate and renamed IX_AccessTemplate_TemplateName.
            - That index included a column MF, which does not exist. The
              corresponding column on the table is MyFavorite.
            - PK/UQ were named PK_TemplateMaster / UQ_TemplateMaster. Renamed to
              PK_AccessTemplate / UQ_AccessTemplate to match the table.

    [FIX 3] dbo.OtherMasterList
            CONSTRAINT UQ_OtherMasterList UNIQUE (MasterType, Department,
            SubscID) and IX_OtherMasterList_MasterType both reference a SubscID
            column that the table definition omits. Column
            "SubscID INT NULL" added (consistent with every other table here).

    [FIX 4] dbo.Menulist
            Index IX_Menulist_Ordering was declared on (Product, PSLNo, SSLNo,
            MSLNo). Product and PSLNo do not exist on this table; the
            corresponding columns are Category and CSLNo. Index rebuilt as
            (Category, CSLNo, SSLNo, MSLNo).

  NOTE ON THE UNIQUE CONSTRAINTS
    The natural keys are declared as UNIQUE (Name, DeleteStatus, SubscID). This
    is intentional in the supplied design but means only ONE row per name can
    ever be soft-deleted (a second delete of the same name collides on
    ('X','Y',subsc)). This is preserved exactly as supplied — the stored
    procedures in 03_StoredProcedures.sql work within that constraint.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  1. dbo.Company
==============================================================================*/
IF OBJECT_ID('dbo.Company', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Company
    (
        MTransNo         INT IDENTITY(1,1) NOT NULL,
        CompCode         varchar(10)   NOT NULL,
        CompName         varchar(150)  NOT NULL,
        TradingName      VARCHAR(150)  NULL,
        CompAddress      varchar(300)  NULL,
        State            varchar(50)   NULL,
        StateCode        int           NULL,
        PhoneNo          varchar(50)   NULL,
        PinCode          INT           NULL,
        SalesPhoneNo     varchar(50)   NULL,
        PurchasePhoneNo  varchar(50)   NULL,
        GeneralEmail     varchar(100)  NULL,
        SalesEmail       varchar(100)  NULL,
        PurchaseEmail    varchar(100)  NULL,
        Website          varchar(100)  NULL,
        GSTIN            varchar(15)   NULL,
        PAN              varchar(10)   NULL,
        CIN              varchar(30)   NULL,
        IECNO            varchar(50)   NULL,
        FromYear         date          NULL,
        ToYear           date          NULL,
        FYear            int           NULL,
        YearCode         varchar(100)  NULL,
        TxnCurrency      VARCHAR(10)   NOT NULL,
        CompDataSource   varchar(300)  NULL,
        SystemIP         VARCHAR(20)   NULL,
        CreatedBy        INT           NOT NULL,
        CreatedOn        DATETIME2     NOT NULL CONSTRAINT DF_Company_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy       INT           NULL,
        ModifiedOn       DATETIME2     NULL,
        DeleteStatus     CHAR(1)       NOT NULL CONSTRAINT DF_Company_DeleteStatus DEFAULT('N'),
        DeleteReason     VARCHAR(250)  NULL,
        LockStatus       CHAR(1)       NOT NULL CONSTRAINT DF_Company_LockStatus   DEFAULT('N'),
        LockCounter      INT           NULL,
        SubscID          INT           NULL,
        CONSTRAINT PK_Company     PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_Company     UNIQUE (CompName, DeleteStatus, SubscID),
        CONSTRAINT UQ_CompanyCode UNIQUE (CompCode, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.Company';
END
GO

IF OBJECT_ID('dbo.Company', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Company_CompCode' AND object_id = OBJECT_ID('dbo.Company'))
    CREATE NONCLUSTERED INDEX IX_Company_CompCode
        ON dbo.Company (SubscID, CompCode)
        INCLUDE (CompName, CompAddress)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.Company', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Company_CompName' AND object_id = OBJECT_ID('dbo.Company'))
    CREATE NONCLUSTERED INDEX IX_Company_CompName
        ON dbo.Company (SubscID, CompName)
        INCLUDE (CompCode)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  2. dbo.BranchMaster
==============================================================================*/
IF OBJECT_ID('dbo.BranchMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BranchMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        BranchCode    varchar(10)   NOT NULL,
        BranchType    varchar(15)   NOT NULL,
        BranchName    varchar(100)  NOT NULL,
        BranchAddress varchar(300)  NULL,
        PhoneNo       varchar(30)   NULL,
        EmailID       varchar(80)   NULL,
        PinCode       INT           NULL,
        GSTIN         varchar(15)   NULL,
        CIN           varchar(30)   NULL,
        Remarks       varchar(250)  NULL,
        BranchUser    INT           NULL,
        SystemIP      VARCHAR(20)   NULL,
        CreatedBy     INT           NOT NULL,
        CreatedOn     DATETIME2     NOT NULL CONSTRAINT DF_Branch_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME2     NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_Branch_DeleteStatus DEFAULT('N'),
        DeleteReason  VARCHAR(250)  NULL,
        LockStatus    CHAR(1)       NOT NULL CONSTRAINT DF_Branch_LockStatus   DEFAULT('N'),
        LockCounter   INT           NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_BranchMaster     PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_BranchMaster     UNIQUE (BranchName, DeleteStatus, SubscID),
        CONSTRAINT UQ_BranchMasterCode UNIQUE (BranchCode, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.BranchMaster';
END
GO

IF OBJECT_ID('dbo.BranchMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BranchMaster_BranchCode' AND object_id = OBJECT_ID('dbo.BranchMaster'))
    CREATE NONCLUSTERED INDEX IX_BranchMaster_BranchCode
        ON dbo.BranchMaster (SubscID, BranchCode)
        INCLUDE (BranchName, BranchType)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.BranchMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BranchMaster_BranchName' AND object_id = OBJECT_ID('dbo.BranchMaster'))
    CREATE NONCLUSTERED INDEX IX_BranchMaster_BranchName
        ON dbo.BranchMaster (SubscID, BranchName)
        INCLUDE (BranchCode, BranchType)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  3. dbo.EinvSetup   —   e-Invoice / e-Way-Bill credentials
==============================================================================*/
IF OBJECT_ID('dbo.EinvSetup', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EinvSetup
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        EUserID       varchar(50)   NULL,
        EPassword     varchar(50)   NULL,
        AppKey        varchar(200)  NULL,
        AuthToken     varchar(200)  NULL,
        Sek           varchar(200)  NULL,
        TokenExp      varchar(100)  NULL,
        EwbAppKey     varchar(200)  NULL,
        EwbAuthToken  varchar(200)  NULL,
        EwbSEK        varchar(200)  NULL,
        EwbTokenExp   varchar(100)  NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_EinvSetup_DeleteStatus DEFAULT('N'),
        SubscID       INT           NULL,
        CONSTRAINT PK_EinvSetup PRIMARY KEY CLUSTERED (MTransNo)
    );
    PRINT 'Created dbo.EinvSetup';
END
GO

/* One live credential row per subscription — supports the "get my setup" read. */
IF OBJECT_ID('dbo.EinvSetup', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EinvSetup_SubscID' AND object_id = OBJECT_ID('dbo.EinvSetup'))
    CREATE NONCLUSTERED INDEX IX_EinvSetup_SubscID
        ON dbo.EinvSetup (SubscID)
        INCLUDE (EUserID, TokenExp, EwbTokenExp)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  4. dbo.EinvDtl   —   IRN / e-Way-Bill result per transaction
  [FIX 1] Removed the trailing comma after CONSTRAINT PK_EinvDtl (syntax error).
==============================================================================*/
IF OBJECT_ID('dbo.EinvDtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EinvDtl
    (
        TransSlNo     INT IDENTITY(1,1) NOT NULL,
        MTransNo      INT           NOT NULL,
        IRNNo         varchar(500)  NULL,
        ScanQRNo      varchar(2000) NULL,
        ACKNo         varchar(50)   NULL,
        EWBillNo      varchar(50)   NULL,
        EWBillDt      varchar(50)   NULL,
        AckDt         varchar(50)   NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_EinvDtl_DeleteStatus DEFAULT('N'),
        SubscID       INT           NULL,
        CONSTRAINT PK_EinvDtl PRIMARY KEY CLUSTERED (TransSlNo)   -- [FIX 1]
    );
    PRINT 'Created dbo.EinvDtl';
END
GO

IF OBJECT_ID('dbo.EinvDtl', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EinvDtl_MTransNo' AND object_id = OBJECT_ID('dbo.EinvDtl'))
    CREATE NONCLUSTERED INDEX IX_EinvDtl_MTransNo
        ON dbo.EinvDtl (MTransNo)
        INCLUDE (IRNNo, EWBillNo, ACKNo)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.EinvDtl', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EinvDtl_EWBillNo' AND object_id = OBJECT_ID('dbo.EinvDtl'))
    CREATE NONCLUSTERED INDEX IX_EinvDtl_EWBillNo
        ON dbo.EinvDtl (EWBillNo)
        WHERE DeleteStatus = 'N' AND EWBillNo IS NOT NULL;
GO


/*==============================================================================
  5. dbo.BankMaster
==============================================================================*/
IF OBJECT_ID('dbo.BankMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.BankMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        BankCode      varchar(10)   NOT NULL,
        BankType      varchar(15)   NOT NULL,
        BankName      varchar(100)  NOT NULL,
        BankID        varchar(20)   NULL,
        IFSCCode      varchar(50)   NULL,
        BranchName    varchar(50)   NULL,
        BranchCode    varchar(20)   NULL,
        Location      varchar(100)  NULL,
        MICRCode      varchar(20)   NULL,
        SystemIP      varchar(20)   NULL,
        CreatedBy     INT           NULL,
        CreatedOn     DATETIME      NOT NULL CONSTRAINT DF_BankMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME      NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_BankMaster_DeleteStatus DEFAULT('N'),
        DeleteReason  varchar(250)  NULL,
        LockStatus    CHAR(1)       NOT NULL CONSTRAINT DF_BankMaster_LockStatus   DEFAULT('N'),
        LockCounter   INT           NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_BankMaster     PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_BankMaster     UNIQUE (BankName, DeleteStatus, SubscID),
        CONSTRAINT UQ_BankMasterCode UNIQUE (BankCode, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.BankMaster';
END
GO

IF OBJECT_ID('dbo.BankMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BankMaster_BankCode' AND object_id = OBJECT_ID('dbo.BankMaster'))
    CREATE NONCLUSTERED INDEX IX_BankMaster_BankCode
        ON dbo.BankMaster (SubscID, BankCode)
        INCLUDE (BankName, BankType)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.BankMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BankMaster_BankName' AND object_id = OBJECT_ID('dbo.BankMaster'))
    CREATE NONCLUSTERED INDEX IX_BankMaster_BankName
        ON dbo.BankMaster (SubscID, BankName)
        INCLUDE (BankCode, BankType)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  6. dbo.CurrencyMaster
==============================================================================*/
IF OBJECT_ID('dbo.CurrencyMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CurrencyMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        Country       varchar(50)    NULL,
        Currency      varchar(50)    NULL,
        CurrencyCode  varchar(10)    NULL,
        ExchangeRate  numeric(12, 2) NULL,
        EffectiveDate date           NULL,
        SystemIP      varchar(20)    NULL,
        CreatedBy     INT            NULL,
        CreatedOn     DATETIME       NOT NULL CONSTRAINT DF_CurrencyMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT            NULL,
        ModifiedOn    DATETIME       NULL,
        DeleteStatus  CHAR(1)        NOT NULL CONSTRAINT DF_CurrencyMaster_DeleteStatus DEFAULT('N'),
        DeleteReason  varchar(250)   NULL,
        LockStatus    CHAR(1)        NOT NULL CONSTRAINT DF_CurrencyMaster_LockStatus   DEFAULT('N'),
        LockCounter   INT            NULL,
        SubscID       INT            NULL,
        CONSTRAINT PK_CurrencyMaster     PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_CurrencyMaster     UNIQUE (Currency, DeleteStatus, SubscID),
        CONSTRAINT UQ_CurrencyMasterCode UNIQUE (CurrencyCode, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.CurrencyMaster';
END
GO

IF OBJECT_ID('dbo.CurrencyMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CurrencyMaster_CurrencyCode' AND object_id = OBJECT_ID('dbo.CurrencyMaster'))
    CREATE NONCLUSTERED INDEX IX_CurrencyMaster_CurrencyCode
        ON dbo.CurrencyMaster (SubscID, CurrencyCode)
        INCLUDE (Currency, ExchangeRate)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.CurrencyMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CurrencyMaster_Currency' AND object_id = OBJECT_ID('dbo.CurrencyMaster'))
    CREATE NONCLUSTERED INDEX IX_CurrencyMaster_Currency
        ON dbo.CurrencyMaster (SubscID, Currency)
        INCLUDE (CurrencyCode, ExchangeRate)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  7. dbo.StateMaster
==============================================================================*/
IF OBJECT_ID('dbo.StateMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StateMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        State         varchar(50)   NULL,
        StateCode     INT           NULL,
        SystemIP      varchar(20)   NULL,
        CreatedBy     INT           NULL,
        CreatedOn     DATETIME      NOT NULL CONSTRAINT DF_StateMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME      NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_StateMaster_DeleteStatus DEFAULT('N'),
        DeleteReason  varchar(250)  NULL,
        LockStatus    CHAR(1)       NOT NULL CONSTRAINT DF_StateMaster_LockStatus   DEFAULT('N'),
        LockCounter   INT           NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_StateMaster     PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_StateMaster     UNIQUE (State, DeleteStatus, SubscID),
        CONSTRAINT UQ_StateMasterCode UNIQUE (StateCode, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.StateMaster';
END
GO

IF OBJECT_ID('dbo.StateMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StateMaster_StateCode' AND object_id = OBJECT_ID('dbo.StateMaster'))
    CREATE NONCLUSTERED INDEX IX_StateMaster_StateCode
        ON dbo.StateMaster (SubscID, StateCode)
        INCLUDE (State)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.StateMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StateMaster_State' AND object_id = OBJECT_ID('dbo.StateMaster'))
    CREATE NONCLUSTERED INDEX IX_StateMaster_State
        ON dbo.StateMaster (SubscID, State)
        INCLUDE (StateCode)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  8. dbo.AccessLog   —   login / logout audit trail
==============================================================================*/
IF OBJECT_ID('dbo.AccessLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessLog
    (
        MTransNo    INT IDENTITY(1,1) NOT NULL,
        IPAddress   varchar(100)  NULL,
        LoginDate   date          NULL,
        LoginTime   varchar(10)   NULL,
        LogoutDate  date          NULL,
        LogoutTime  varchar(10)   NULL,
        LatLong     varchar(100)  NULL,
        Location    varchar(500)  NULL,
        LoginType   varchar(50)   NULL,
        LoginId     varchar(30)   NULL,
        SubscID     INT           NULL,
        CONSTRAINT PK_AccessLog PRIMARY KEY CLUSTERED (MTransNo)
    );
    PRINT 'Created dbo.AccessLog';
END
GO

IF OBJECT_ID('dbo.AccessLog', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccessLog_LoginId' AND object_id = OBJECT_ID('dbo.AccessLog'))
    CREATE NONCLUSTERED INDEX IX_AccessLog_LoginId
        ON dbo.AccessLog (LoginId, LoginDate)
        INCLUDE (IPAddress, LoginTime, LogoutDate, LogoutTime);
GO

IF OBJECT_ID('dbo.AccessLog', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccessLog_LoginDate' AND object_id = OBJECT_ID('dbo.AccessLog'))
    CREATE NONCLUSTERED INDEX IX_AccessLog_LoginDate
        ON dbo.AccessLog (LoginDate)
        INCLUDE (LoginId, IPAddress);
GO


/*==============================================================================
  9. dbo.UserMaster
==============================================================================*/
IF OBJECT_ID('dbo.UserMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserMaster
    (
        MTransNo        INT IDENTITY(1,1) NOT NULL,
        UserName        varchar(100)  NOT NULL,
        UserId          varchar(30)   NOT NULL,
        PW              varchar(100)  NOT NULL,
        UserType        varchar(15)   NOT NULL,
        Designation     varchar(50)   NULL,
        Department      varchar(50)   NULL,
        ContactNo       varchar(20)   NULL,
        EmailId         varchar(80)   NULL,
        ImageFile       varchar(100)  NULL,
        AccessTemplate  varchar(50)   NULL,
        Branch          varchar(500)  NULL,
        PWExpiry        INT           NULL,
        BadLogins       INT           NULL,
        TokenNo         INT           NULL,
        LastLogin       DATETIME      NULL,
        logoutDate      DATETIME      NULL,
        HomePage        varchar(50)   NULL,
        SystemIP        varchar(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME      NOT NULL CONSTRAINT DF_UserMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME      NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_UserMaster_DeleteStatus DEFAULT('N'),
        DeleteReason    varchar(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_UserMaster_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_UserMaster        PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_UserMaster_UserId UNIQUE (UserId, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.UserMaster';
END
GO

IF OBJECT_ID('dbo.UserMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserMaster_UserId' AND object_id = OBJECT_ID('dbo.UserMaster'))
    CREATE NONCLUSTERED INDEX IX_UserMaster_UserId
        ON dbo.UserMaster (SubscID, UserId)
        INCLUDE (UserName, UserType, LockStatus, BadLogins)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.UserMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserMaster_UserName' AND object_id = OBJECT_ID('dbo.UserMaster'))
    CREATE NONCLUSTERED INDEX IX_UserMaster_UserName
        ON dbo.UserMaster (SubscID, UserName)
        INCLUDE (UserId, UserType)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
 10. dbo.AccessTemplate   —   per-template module permissions
  [FIX 2] PK/UQ renamed from *_TemplateMaster to *_AccessTemplate; the index was
          retargeted from the non-existent dbo.TemplateMaster to this table and
          its INCLUDE list now names MyFavorite instead of the non-existent MF.
==============================================================================*/
IF OBJECT_ID('dbo.AccessTemplate', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccessTemplate
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        TemplateName  varchar(50)  NULL,
        Department    varchar(50)  NULL,
        ModuleNo      INT          NOT NULL,
        AuthType      char(1)      NULL,
        MyFavorite    char(1)      NULL,
        SubscID       INT          NULL,
        CONSTRAINT PK_AccessTemplate PRIMARY KEY CLUSTERED (MTransNo),          -- [FIX 2]
        CONSTRAINT UQ_AccessTemplate UNIQUE (TemplateName, ModuleNo, SubscID)   -- [FIX 2]
    );
    PRINT 'Created dbo.AccessTemplate';
END
GO

IF OBJECT_ID('dbo.AccessTemplate', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccessTemplate_TemplateName' AND object_id = OBJECT_ID('dbo.AccessTemplate'))
    CREATE NONCLUSTERED INDEX IX_AccessTemplate_TemplateName
        ON dbo.AccessTemplate (SubscID, TemplateName, ModuleNo)
        INCLUDE (AuthType, MyFavorite, Department);                             -- [FIX 2]
GO


/*==============================================================================
 11. dbo.OtherMasterList   —   catalogue of the master types the UI can edit
  [FIX 3] SubscID column added — UQ_OtherMasterList and IX_OtherMasterList_
          MasterType both reference it but the supplied column list omitted it.
==============================================================================*/
IF OBJECT_ID('dbo.OtherMasterList', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OtherMasterList
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        MasterType    varchar(30)  NULL,
        Department    varchar(50)  NULL,
        DeleteStatus  CHAR(1)      NOT NULL CONSTRAINT DF_OtherMasterList_DeleteStatus DEFAULT('N'),
        SubscID       INT          NULL,                                         -- [FIX 3]
        CONSTRAINT PK_OtherMasterList PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_OtherMasterList UNIQUE (MasterType, Department, SubscID)
    );
    PRINT 'Created dbo.OtherMasterList';
END
GO

IF OBJECT_ID('dbo.OtherMasterList', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OtherMasterList_MasterType' AND object_id = OBJECT_ID('dbo.OtherMasterList'))
    CREATE NONCLUSTERED INDEX IX_OtherMasterList_MasterType
        ON dbo.OtherMasterList (SubscID, MasterType)
        INCLUDE (Department)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
 12. dbo.OtherMaster   —   generic key/value master rows, grouped by MasterType
==============================================================================*/
IF OBJECT_ID('dbo.OtherMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OtherMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        MasterType    varchar(50)   NULL,
        MasterName    varchar(50)   NULL,
        Department    varchar(50)   NULL,
        SystemIP      varchar(20)   NULL,
        CreatedBy     INT           NULL,
        CreatedOn     DATETIME      NOT NULL CONSTRAINT DF_OtherMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME      NULL,
        DeleteStatus  CHAR(1)       NOT NULL CONSTRAINT DF_OtherMaster_DeleteStatus DEFAULT('N'),
        DeleteReason  varchar(250)  NULL,
        LockStatus    CHAR(1)       NOT NULL CONSTRAINT DF_OtherMaster_LockStatus   DEFAULT('N'),
        LockCounter   INT           NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_OtherMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_OtherMaster UNIQUE (MasterType, MasterName, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.OtherMaster';
END
GO

IF OBJECT_ID('dbo.OtherMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OtherMaster_MasterType' AND object_id = OBJECT_ID('dbo.OtherMaster'))
    CREATE NONCLUSTERED INDEX IX_OtherMaster_MasterType
        ON dbo.OtherMaster (SubscID, MasterType, MasterName)
        INCLUDE (Department)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
 13. dbo.Menulist   —   navigation catalogue.
      MTransNo is NOT an identity: the module number is assigned by hand and is
      the value AccessTemplate.ModuleNo points at.
  [FIX 4] IX_Menulist_Ordering was declared over (Product, PSLNo, ...), neither
          of which exists on this table. Rebuilt over (Category, CSLNo, ...).
==============================================================================*/
IF OBJECT_ID('dbo.Menulist', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Menulist
    (
        MTransNo    INT           NOT NULL,
        Category    varchar(50)   NULL,
        CSLNo       INT           NULL,
        Section     varchar(50)   NULL,
        SSLNo       INT           NULL,
        ModuleName  varchar(50)   NULL,
        MSLNo       INT           NULL,
        ModuleIcon  varchar(50)   NULL,
        ModulePage  varchar(100)  NULL,
        CONSTRAINT PK_Menulist PRIMARY KEY CLUSTERED (MTransNo)
    );
    PRINT 'Created dbo.Menulist';
END
GO

IF OBJECT_ID('dbo.Menulist', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Menulist_Ordering' AND object_id = OBJECT_ID('dbo.Menulist'))
    CREATE NONCLUSTERED INDEX IX_Menulist_Ordering
        ON dbo.Menulist (Category, CSLNo, SSLNo, MSLNo)                          -- [FIX 4]
        INCLUDE (Section, ModuleName, ModuleIcon, ModulePage);
GO


/*==============================================================================
 14. dbo.GeneralConfig
==============================================================================*/
IF OBJECT_ID('dbo.GeneralConfig', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GeneralConfig
    (
        MTransNo          INT IDENTITY(1,1) NOT NULL,
        ConfigKey         varchar(100)  NOT NULL,
        ConfigValue       varchar(500)  NOT NULL,
        ConfigDepartment  varchar(50)   NULL,
        CreatedBy         INT           NULL,
        CreatedOn         DATETIME      NOT NULL CONSTRAINT DF_GeneralConfig_CreatedOn DEFAULT(GETDATE()),
        SubscID           INT           NULL,
        CONSTRAINT PK_GeneralConfig PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_GeneralConfig UNIQUE (ConfigKey, SubscID)
    );
    PRINT 'Created dbo.GeneralConfig';
END
GO

IF OBJECT_ID('dbo.GeneralConfig', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GeneralConfig_ConfigKey' AND object_id = OBJECT_ID('dbo.GeneralConfig'))
    CREATE NONCLUSTERED INDEX IX_GeneralConfig_ConfigKey
        ON dbo.GeneralConfig (SubscID, ConfigKey)
        INCLUDE (ConfigValue);
GO


/*==============================================================================
 15. dbo.ErrorLog
==============================================================================*/
IF OBJECT_ID('dbo.ErrorLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ErrorLog
    (
        MTransNo    INT IDENTITY(1,1) NOT NULL,
        ErrorMsg    varchar(2000) NULL,
        StackTrace  varchar(2000) NULL,
        ErrorDate   date          NULL,
        ErrorTime   varchar(10)   NULL,
        LoginType   varchar(50)   NULL,
        LoginId     varchar(30)   NULL,
        SubscID     INT           NULL,
        CONSTRAINT PK_ErrorLog PRIMARY KEY CLUSTERED (MTransNo)
    );
    PRINT 'Created dbo.ErrorLog';
END
GO

IF OBJECT_ID('dbo.ErrorLog', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ErrorLog_ErrorDate' AND object_id = OBJECT_ID('dbo.ErrorLog'))
    CREATE NONCLUSTERED INDEX IX_ErrorLog_ErrorDate
        ON dbo.ErrorLog (ErrorDate)
        INCLUDE (LoginId, ErrorMsg);
GO

PRINT '02_Tables.sql complete — 15 tables ready.';
GO
