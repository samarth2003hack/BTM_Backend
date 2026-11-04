/*==============================================================================
  EMSSoft - 09_MasterTables.sql
  ------------------------------------------------------------------------------
  Trading / inventory masters:

      dbo.CustomerMaster      customers, with three child tables
      dbo.CustomerContacts      contact people           (MTransNo -> CustomerMaster)
      dbo.CustomerTerm          per-customer terms       (MTransNo -> CustomerMaster)
      dbo.ShippingAddress       ship-to addresses        (MTransNo -> CustomerMaster)
      dbo.StandardTerms       reusable term library, split by TermType
      dbo.SupplierMaster      suppliers, with two child tables
      dbo.SupplierContacts      contact people           (MTransNo -> SupplierMaster)
      dbo.SupplierTerm          per-supplier terms       (MTransNo -> SupplierMaster)
      dbo.PartMaster          items / parts
      dbo.PartCodeMaster      code-building blocks per parameter
      dbo.PartParameterList   which parameters make up a part code, and in what order
      dbo.DrawingNos          drawings, optionally tied to a part
      dbo.MaterialGrade       stocking grades of a part   (PartNo -> PartMaster)
      dbo.MaterialStd         grade -> material standard cross reference
      dbo.HSNMaster           HSN codes
      dbo.PriceList           vendor / part rate history

  Conventions follow 02_Tables.sql and 05_AccountingTables.sql: soft delete via
  DeleteStatus, LockStatus as the active flag, SubscID on every row, and unique
  keys expressed as filtered indexes (WHERE DeleteStatus = 'N') so a soft-deleted
  row never blocks a new one with the same name.

  Run after 08_TestData.sql:
      09_MasterTables.sql
      10_MasterProcedures.sql

  Every statement is idempotent - re-running only adds what is missing.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  1. dbo.CustomerMaster
==============================================================================*/
IF OBJECT_ID('dbo.CustomerMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerMaster
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        CustomerCode        varchar(10)         NOT NULL,
        CustomerType        varchar(15)         NOT NULL,
        CustomerName        varchar(100)        NOT NULL,
        AliasName           varchar(100)        NULL,
        Category            varchar(50)         NULL,
        CustomerAddress     varchar(300)        NULL,
        PinCode             varchar(30)         NULL,
        PinCodeLocation     varchar(50)         NULL,
        StateName           varchar(50)         NULL,
        StateCode           INT                 NULL,
        PhoneNo             varchar(30)         NULL,
        MobileNo            varchar(30)         NULL,
        EmailID             varchar(80)         NULL,
        Website             varchar(80)         NULL,
        TaxType             varchar(20)         NULL,
        GSTIN               varchar(15)         NULL,
        PAN                 varchar(10)         NULL,
        TxnCurrency         varchar(15)         NULL,
        SalesExec           varchar(50)         NULL,
        CreditPeriod        INT                 NULL,
        CreditLimit         numeric(18, 2)      NULL,
        CustomerPriceList   varchar(50)         NULL,
        VendorCode          varchar(30)         NULL,
        Remarks             varchar(500)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_CustomerMaster_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_CustomerMaster_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_CustomerMaster_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_CustomerMaster_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_CustomerMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_CustomerMaster_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_CustomerMaster_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.CustomerMaster';
END
GO

IF OBJECT_ID('dbo.CustomerMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CustomerMaster_CustomerCode' AND object_id = OBJECT_ID('dbo.CustomerMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_CustomerMaster_CustomerCode
        ON dbo.CustomerMaster (SubscID, CustomerCode)
        INCLUDE (CustomerName, CustomerType, StateCode)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.CustomerMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CustomerMaster_CustomerName' AND object_id = OBJECT_ID('dbo.CustomerMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_CustomerMaster_CustomerName
        ON dbo.CustomerMaster (SubscID, CustomerName)
        INCLUDE (CustomerCode, CustomerType, PinCodeLocation)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.CustomerMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerMaster_GSTIN' AND object_id = OBJECT_ID('dbo.CustomerMaster'))
    CREATE NONCLUSTERED INDEX IX_CustomerMaster_GSTIN
        ON dbo.CustomerMaster (SubscID, GSTIN)
        INCLUDE (CustomerCode, CustomerName)
        WHERE DeleteStatus = 'N' AND GSTIN IS NOT NULL;
GO


/*==============================================================================
  2. dbo.CustomerContacts      (MTransNo -> CustomerMaster.MTransNo)
==============================================================================*/
IF OBJECT_ID('dbo.CustomerContacts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerContacts
    (
        TransSlNo           INT IDENTITY(1,1)   NOT NULL,
        MTransNo            INT                 NOT NULL,
        Title               varchar(30)         NULL,
        CPName              varchar(50)         NOT NULL,
        Designation         varchar(30)         NULL,
        PhoneNo             varchar(30)         NULL,
        MobileNo            varchar(30)         NULL,
        Email               varchar(80)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_CustomerContacts_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_CustomerContacts_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_CustomerContacts_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_CustomerContacts_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_CustomerContacts PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_CustomerContacts_CustomerMaster
            FOREIGN KEY (MTransNo) REFERENCES dbo.CustomerMaster (MTransNo),
        CONSTRAINT CK_CustomerContacts_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_CustomerContacts_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.CustomerContacts';
END
GO

IF OBJECT_ID('dbo.CustomerContacts', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerContacts_MTransNo' AND object_id = OBJECT_ID('dbo.CustomerContacts'))
    CREATE NONCLUSTERED INDEX IX_CustomerContacts_MTransNo
        ON dbo.CustomerContacts (MTransNo)
        INCLUDE (CPName, Designation, MobileNo, Email)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.CustomerContacts', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerContacts_CPName' AND object_id = OBJECT_ID('dbo.CustomerContacts'))
    CREATE NONCLUSTERED INDEX IX_CustomerContacts_CPName
        ON dbo.CustomerContacts (SubscID, CPName)
        INCLUDE (MTransNo, MobileNo)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  3. dbo.CustomerTerm          (MTransNo -> CustomerMaster.MTransNo)
     TermSlNo is numeric on purpose: 1.10, 1.20 ... sub-numbering.
==============================================================================*/
IF OBJECT_ID('dbo.CustomerTerm', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerTerm
    (
        TransSlNo           INT IDENTITY(1,1)   NOT NULL,
        MTransNo            INT                 NOT NULL,
        TermSlNo            numeric(10, 2)      NULL,
        TermTitle           varchar(30)         NULL,
        TermDescription     varchar(250)        NULL,
        TermType            varchar(30)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_CustomerTerm_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_CustomerTerm_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_CustomerTerm_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_CustomerTerm_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_CustomerTerm PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_CustomerTerm_CustomerMaster
            FOREIGN KEY (MTransNo) REFERENCES dbo.CustomerMaster (MTransNo),
        CONSTRAINT CK_CustomerTerm_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_CustomerTerm_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.CustomerTerm';
END
GO

IF OBJECT_ID('dbo.CustomerTerm', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CustomerTerm_MTransNo' AND object_id = OBJECT_ID('dbo.CustomerTerm'))
    CREATE NONCLUSTERED INDEX IX_CustomerTerm_MTransNo
        ON dbo.CustomerTerm (MTransNo, TermSlNo)
        INCLUDE (TermTitle, TermDescription)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  4. dbo.StandardTerms
     Serves BOTH customer and supplier terms via TermType, e.g.
     'CUSTOMER' / 'SUPPLIER' / 'PAYMENT'.
==============================================================================*/
IF OBJECT_ID('dbo.StandardTerms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StandardTerms
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        TermType            varchar(30)         NOT NULL,
        TermSlNo            numeric(10, 2)      NULL,
        TermTitle           varchar(30)         NOT NULL,
        TermDescription     varchar(250)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_StandardTerms_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_StandardTerms_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_StandardTerms_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_StandardTerms_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_StandardTerms PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_StandardTerms_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_StandardTerms_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.StandardTerms';
END
GO

IF OBJECT_ID('dbo.StandardTerms', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_StandardTerms_Title' AND object_id = OBJECT_ID('dbo.StandardTerms'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_StandardTerms_Title
        ON dbo.StandardTerms (SubscID, TermType, TermTitle)
        INCLUDE (TermSlNo, TermDescription)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.StandardTerms', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StandardTerms_Type' AND object_id = OBJECT_ID('dbo.StandardTerms'))
    CREATE NONCLUSTERED INDEX IX_StandardTerms_Type
        ON dbo.StandardTerms (SubscID, TermType, TermSlNo)
        INCLUDE (TermTitle, TermDescription)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  5. dbo.ShippingAddress       (MTransNo -> CustomerMaster.MTransNo)
==============================================================================*/
IF OBJECT_ID('dbo.ShippingAddress', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ShippingAddress
    (
        TransSlNo           INT IDENTITY(1,1)   NOT NULL,
        MTransNo            INT                 NOT NULL,
        ShipName            varchar(100)        NOT NULL,
        ShipAlias           varchar(100)        NULL,
        ShipAddress         varchar(300)        NULL,
        ShipState           varchar(50)         NULL,
        ShipStateCode       INT                 NULL,
        PinCodeLocation     varchar(50)         NULL,
        PinCode             varchar(30)         NULL,
        PhoneNo             varchar(30)         NULL,
        MobileNo            varchar(30)         NULL,
        EmailID             varchar(80)         NULL,
        GSTIN               varchar(15)         NULL,
        PAN                 varchar(10)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_ShippingAddress_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_ShippingAddress_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_ShippingAddress_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_ShippingAddress_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_ShippingAddress PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_ShippingAddress_CustomerMaster
            FOREIGN KEY (MTransNo) REFERENCES dbo.CustomerMaster (MTransNo),
        CONSTRAINT CK_ShippingAddress_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_ShippingAddress_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.ShippingAddress';
END
GO

IF OBJECT_ID('dbo.ShippingAddress', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShippingAddress_MTransNo' AND object_id = OBJECT_ID('dbo.ShippingAddress'))
    CREATE NONCLUSTERED INDEX IX_ShippingAddress_MTransNo
        ON dbo.ShippingAddress (MTransNo)
        INCLUDE (ShipName, ShipState, ShipStateCode, GSTIN)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.ShippingAddress', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ShippingAddress_ShipName' AND object_id = OBJECT_ID('dbo.ShippingAddress'))
    CREATE NONCLUSTERED INDEX IX_ShippingAddress_ShipName
        ON dbo.ShippingAddress (SubscID, ShipName)
        INCLUDE (MTransNo, PinCodeLocation, PinCode)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  6. dbo.SupplierMaster
==============================================================================*/
IF OBJECT_ID('dbo.SupplierMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierMaster
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        SupplierCode        varchar(10)         NOT NULL,
        SupplierType        varchar(15)         NOT NULL,
        SupplierName        varchar(100)        NOT NULL,
        AliasName           varchar(100)        NULL,
        Category            varchar(50)         NULL,
        SupplierAddress     varchar(300)        NULL,
        StateName           varchar(50)         NULL,
        StateCode           INT                 NULL,
        PinCode             varchar(30)         NULL,
        PinCodeLocation     varchar(50)         NULL,
        PhoneNo             varchar(30)         NULL,
        MobileNo            varchar(30)         NULL,
        EmailID             varchar(80)         NULL,
        Website             varchar(80)         NULL,
        TaxType             varchar(20)         NULL,
        GSTIN               varchar(15)         NULL,
        PAN                 varchar(10)         NULL,
        TxnCurrency         varchar(15)         NULL,
        CreditPeriod        INT                 NULL,
        BankName            varchar(50)         NULL,
        BankAccountNo       varchar(30)         NULL,
        IFSC                varchar(11)         NULL,
        Remarks             varchar(500)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_SupplierMaster_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_SupplierMaster_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_SupplierMaster_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_SupplierMaster_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_SupplierMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_SupplierMaster_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_SupplierMaster_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.SupplierMaster';
END
GO

IF OBJECT_ID('dbo.SupplierMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SupplierMaster_SupplierCode' AND object_id = OBJECT_ID('dbo.SupplierMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_SupplierMaster_SupplierCode
        ON dbo.SupplierMaster (SubscID, SupplierCode)
        INCLUDE (SupplierName, SupplierType, StateCode)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.SupplierMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SupplierMaster_SupplierName' AND object_id = OBJECT_ID('dbo.SupplierMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_SupplierMaster_SupplierName
        ON dbo.SupplierMaster (SubscID, SupplierName)
        INCLUDE (SupplierCode, SupplierType, PinCodeLocation)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.SupplierMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupplierMaster_GSTIN' AND object_id = OBJECT_ID('dbo.SupplierMaster'))
    CREATE NONCLUSTERED INDEX IX_SupplierMaster_GSTIN
        ON dbo.SupplierMaster (SubscID, GSTIN)
        INCLUDE (SupplierCode, SupplierName)
        WHERE DeleteStatus = 'N' AND GSTIN IS NOT NULL;
GO


/*==============================================================================
  7. dbo.SupplierContacts      (MTransNo -> SupplierMaster.MTransNo)
==============================================================================*/
IF OBJECT_ID('dbo.SupplierContacts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierContacts
    (
        TransSlNo           INT IDENTITY(1,1)   NOT NULL,
        MTransNo            INT                 NOT NULL,
        Title               varchar(30)         NULL,
        CPName              varchar(50)         NOT NULL,
        Designation         varchar(30)         NULL,
        PhoneNo             varchar(30)         NULL,
        MobileNo            varchar(30)         NULL,
        Email               varchar(80)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_SupplierContacts_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_SupplierContacts_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_SupplierContacts_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_SupplierContacts_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_SupplierContacts PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_SupplierContacts_SupplierMaster
            FOREIGN KEY (MTransNo) REFERENCES dbo.SupplierMaster (MTransNo),
        CONSTRAINT CK_SupplierContacts_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_SupplierContacts_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.SupplierContacts';
END
GO

IF OBJECT_ID('dbo.SupplierContacts', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupplierContacts_MTransNo' AND object_id = OBJECT_ID('dbo.SupplierContacts'))
    CREATE NONCLUSTERED INDEX IX_SupplierContacts_MTransNo
        ON dbo.SupplierContacts (MTransNo)
        INCLUDE (CPName, Designation, MobileNo, Email)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.SupplierContacts', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupplierContacts_CPName' AND object_id = OBJECT_ID('dbo.SupplierContacts'))
    CREATE NONCLUSTERED INDEX IX_SupplierContacts_CPName
        ON dbo.SupplierContacts (SubscID, CPName)
        INCLUDE (MTransNo, MobileNo)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  8. dbo.SupplierTerm          (MTransNo -> SupplierMaster.MTransNo)
==============================================================================*/
IF OBJECT_ID('dbo.SupplierTerm', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SupplierTerm
    (
        TransSlNo           INT IDENTITY(1,1)   NOT NULL,
        MTransNo            INT                 NOT NULL,
        TermSlNo            numeric(10, 2)      NULL,
        TermTitle           varchar(30)         NULL,
        TermDescription     varchar(250)        NULL,
        TermType            varchar(30)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_SupplierTerm_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_SupplierTerm_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_SupplierTerm_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_SupplierTerm_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_SupplierTerm PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_SupplierTerm_SupplierMaster
            FOREIGN KEY (MTransNo) REFERENCES dbo.SupplierMaster (MTransNo),
        CONSTRAINT CK_SupplierTerm_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_SupplierTerm_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.SupplierTerm';
END
GO

IF OBJECT_ID('dbo.SupplierTerm', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupplierTerm_MTransNo' AND object_id = OBJECT_ID('dbo.SupplierTerm'))
    CREATE NONCLUSTERED INDEX IX_SupplierTerm_MTransNo
        ON dbo.SupplierTerm (MTransNo, TermSlNo)
        INCLUDE (TermTitle, TermDescription)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  9. dbo.PartMaster
     SPart / PPart / MPart are the sales / purchase / manufacturing flags.
==============================================================================*/
IF OBJECT_ID('dbo.PartMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartMaster
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        PartCode            varchar(30)         NOT NULL,
        PartName            varchar(250)        NOT NULL,
        CodeDetails         varchar(100)        NULL,
        PartDetails         varchar(500)        NULL,
        ShortDescription    varchar(250)        NULL,
        FullDescription     varchar(1000)       NULL,
        DrawingNo           varchar(50)         NULL,
        DrgRevNo            INT                 NULL,
        ColorCode           varchar(20)         NULL,
        CatNo               INT                 NULL,
        SubCatNo            INT                 NULL,
        GroupNo             INT                 NULL,
        Category            varchar(50)         NULL,
        SubCategory         varchar(50)         NULL,
        InventoryGroup      varchar(50)         NULL,
        Department          varchar(50)         NULL,
        Brand               varchar(50)         NULL,
        PartType            varchar(30)         NULL,
        PartClass           varchar(10)         NULL,
        HSNCode             varchar(10)         NULL,
        TaxP                numeric(6, 2)       NULL,
        PrimaryUnit         varchar(10)         NULL,
        SecondaryUnit       varchar(10)         NULL,
        ConversionRatio     numeric(14, 4)      NULL,
        TxnUnit             varchar(10)         NULL,
        Packing             varchar(50)         NULL,
        PackQty             numeric(14, 2)      NULL,
        PackSize            numeric(14, 2)      NULL,
        SPart               CHAR(1)             NULL CONSTRAINT CK_PartMaster_SPart CHECK (SPart IN ('Y','N')),
        PPart               CHAR(1)             NULL CONSTRAINT CK_PartMaster_PPart CHECK (PPart IN ('Y','N')),
        MPart               CHAR(1)             NULL CONSTRAINT CK_PartMaster_MPart CHECK (MPart IN ('Y','N')),
        MinStock            numeric(14, 2)      NULL,
        MaxStock            numeric(14, 2)      NULL,
        MOQ                 numeric(14, 2)      NULL,
        ShelfLife           numeric(14, 2)      NULL,
        CostPrice           numeric(18, 2)      NULL,
        SalesPrice          numeric(18, 2)      NULL,
        MRP                 numeric(18, 2)      NULL,
        MfrLicNo            varchar(50)         NULL,
        MfgStd              varchar(50)         NULL,
        StoreLocation       varchar(50)         NULL,
        Rack                varchar(10)         NULL,
        RackRow             varchar(10)         NULL,
        RackCase            varchar(10)         NULL,
        PartLength          numeric(14, 2)      NULL,
        PartWidth           numeric(14, 2)      NULL,
        PartHeight          numeric(14, 2)      NULL,
        PartWeight          numeric(14, 3)      NULL,
        AtchFileName        varchar(250)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_PartMaster_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_PartMaster_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_PartMaster_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_PartMaster_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_PartMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_PartMaster_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_PartMaster_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.PartMaster';
END
GO

IF OBJECT_ID('dbo.PartMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartMaster_PartCode' AND object_id = OBJECT_ID('dbo.PartMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PartMaster_PartCode
        ON dbo.PartMaster (SubscID, PartCode)
        INCLUDE (PartName, PartType, PrimaryUnit, SalesPrice)
        WHERE DeleteStatus = 'N';
GO

/*  PartName is intentionally NOT unique - two parts may share a description.
    To block duplicates, replace the index below with a UNIQUE one:
        CREATE UNIQUE NONCLUSTERED INDEX UX_PartMaster_PartName
            ON dbo.PartMaster (SubscID, PartName) WHERE DeleteStatus = 'N';
    SP_PartMaster only enforces PartCode uniqueness, matching this choice.        */
IF OBJECT_ID('dbo.PartMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartMaster_PartName' AND object_id = OBJECT_ID('dbo.PartMaster'))
    CREATE NONCLUSTERED INDEX IX_PartMaster_PartName
        ON dbo.PartMaster (SubscID, PartName)
        INCLUDE (PartCode, PartType, HSNCode)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.PartMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartMaster_Category' AND object_id = OBJECT_ID('dbo.PartMaster'))
    CREATE NONCLUSTERED INDEX IX_PartMaster_Category
        ON dbo.PartMaster (SubscID, Category, SubCategory)
        INCLUDE (PartCode, PartName, InventoryGroup)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.PartMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartMaster_DrawingNo' AND object_id = OBJECT_ID('dbo.PartMaster'))
    CREATE NONCLUSTERED INDEX IX_PartMaster_DrawingNo
        ON dbo.PartMaster (SubscID, DrawingNo)
        INCLUDE (PartCode, PartName, DrgRevNo)
        WHERE DeleteStatus = 'N' AND DrawingNo IS NOT NULL;
GO


/*==============================================================================
  10. dbo.PartCodeMaster
      One row per (Parameter, Code) pair the part-code builder may emit.
==============================================================================*/
IF OBJECT_ID('dbo.PartCodeMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartCodeMaster
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        Parameter           varchar(50)         NOT NULL,
        Code                varchar(10)         NOT NULL,
        Description         varchar(250)        NULL,
        ValveType           varchar(50)         NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_PartCodeMaster_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_PartCodeMaster_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_PartCodeMaster_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_PartCodeMaster_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_PartCodeMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_PartCodeMaster_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_PartCodeMaster_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.PartCodeMaster';
END
GO

IF OBJECT_ID('dbo.PartCodeMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartCodeMaster_Parameter' AND object_id = OBJECT_ID('dbo.PartCodeMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PartCodeMaster_Parameter
        ON dbo.PartCodeMaster (SubscID, Parameter, Code)
        INCLUDE (Description, ValveType)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.PartCodeMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartCodeMaster_ValveType' AND object_id = OBJECT_ID('dbo.PartCodeMaster'))
    CREATE NONCLUSTERED INDEX IX_PartCodeMaster_ValveType
        ON dbo.PartCodeMaster (SubscID, ValveType)
        INCLUDE (Parameter, Code, Description)
        WHERE DeleteStatus = 'N' AND ValveType IS NOT NULL;
GO


/*==============================================================================
  11. dbo.PartParameterList
      SlNo is the position of the parameter inside the generated part code.
==============================================================================*/
IF OBJECT_ID('dbo.PartParameterList', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PartParameterList
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        PartType            varchar(30)         NOT NULL,
        Parameter           varchar(50)         NOT NULL,
        SlNo                INT                 NULL,
        MaxLength           INT                 NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_PartParameterList_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_PartParameterList_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_PartParameterList_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_PartParameterList_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_PartParameterList PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_PartParameterList_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_PartParameterList_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.PartParameterList';
END
GO

IF OBJECT_ID('dbo.PartParameterList', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PartParameterList_PartType' AND object_id = OBJECT_ID('dbo.PartParameterList'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PartParameterList_PartType
        ON dbo.PartParameterList (SubscID, PartType, Parameter)
        INCLUDE (SlNo, MaxLength)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.PartParameterList', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PartParameterList_Sequence' AND object_id = OBJECT_ID('dbo.PartParameterList'))
    CREATE NONCLUSTERED INDEX IX_PartParameterList_Sequence
        ON dbo.PartParameterList (SubscID, PartType, SlNo)
        INCLUDE (Parameter, MaxLength)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  12. dbo.DrawingNos           (PartNo -> PartMaster.MTransNo, optional)
==============================================================================*/
IF OBJECT_ID('dbo.DrawingNos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DrawingNos
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        DrawingNo           varchar(50)         NOT NULL,
        PartNo              INT                 NULL,
        DrawingType         varchar(50)         NULL,
        DrawingGroup        varchar(50)         NULL,
        PartGroup           varchar(50)         NULL,
        PSlNo               INT                 NULL,
        DrawingDescription  varchar(250)        NULL,
        DrawingRemarks      varchar(250)        NULL,
        RevNo               INT                 NULL,
        RevDate             date                NULL,
        RevStatus           CHAR(1)             NULL,
        FileAttach          varchar(250)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_DrawingNos_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_DrawingNos_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_DrawingNos_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_DrawingNos_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_DrawingNos PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT FK_DrawingNos_PartMaster
            FOREIGN KEY (PartNo) REFERENCES dbo.PartMaster (MTransNo),
        CONSTRAINT CK_DrawingNos_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_DrawingNos_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.DrawingNos';
END
GO

IF OBJECT_ID('dbo.DrawingNos', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DrawingNos_DrawingNo' AND object_id = OBJECT_ID('dbo.DrawingNos'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_DrawingNos_DrawingNo
        ON dbo.DrawingNos (SubscID, DrawingNo)
        INCLUDE (DrawingType, DrawingGroup, RevNo, PartNo)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.DrawingNos', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DrawingNos_Group' AND object_id = OBJECT_ID('dbo.DrawingNos'))
    CREATE NONCLUSTERED INDEX IX_DrawingNos_Group
        ON dbo.DrawingNos (SubscID, DrawingGroup, PartGroup, PSlNo)
        INCLUDE (DrawingNo, DrawingDescription)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.DrawingNos', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DrawingNos_PartNo' AND object_id = OBJECT_ID('dbo.DrawingNos'))
    CREATE NONCLUSTERED INDEX IX_DrawingNos_PartNo
        ON dbo.DrawingNos (PartNo)
        INCLUDE (DrawingNo, RevNo, RevDate)
        WHERE DeleteStatus = 'N' AND PartNo IS NOT NULL;
GO


/*==============================================================================
  13. dbo.MaterialGrade        (PartNo -> PartMaster.MTransNo)
      Stocking levels are held per grade, not per part.
==============================================================================*/
IF OBJECT_ID('dbo.MaterialGrade', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaterialGrade
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        PartNo              INT                 NOT NULL,
        GradeNo             INT                 NOT NULL,
        Grade               varchar(50)         NOT NULL,
        MinStock            numeric(14, 2)      NULL,
        MaxStock            numeric(14, 2)      NULL,
        MOQ                 numeric(14, 2)      NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_MaterialGrade_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_MaterialGrade_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_MaterialGrade_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_MaterialGrade_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_MaterialGrade PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT FK_MaterialGrade_PartMaster
            FOREIGN KEY (PartNo) REFERENCES dbo.PartMaster (MTransNo),
        CONSTRAINT CK_MaterialGrade_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_MaterialGrade_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.MaterialGrade';
END
GO

IF OBJECT_ID('dbo.MaterialGrade', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MaterialGrade_PartNo' AND object_id = OBJECT_ID('dbo.MaterialGrade'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_MaterialGrade_PartNo
        ON dbo.MaterialGrade (PartNo, GradeNo)
        INCLUDE (Grade, MinStock, MaxStock, MOQ)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.MaterialGrade', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaterialGrade_Grade' AND object_id = OBJECT_ID('dbo.MaterialGrade'))
    CREATE NONCLUSTERED INDEX IX_MaterialGrade_Grade
        ON dbo.MaterialGrade (SubscID, Grade)
        INCLUDE (PartNo, GradeNo)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  14. dbo.MaterialStd
==============================================================================*/
IF OBJECT_ID('dbo.MaterialStd', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaterialStd
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        Grade               varchar(50)         NOT NULL,
        MatrStd             varchar(100)        NOT NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_MaterialStd_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_MaterialStd_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_MaterialStd_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_MaterialStd_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_MaterialStd PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_MaterialStd_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_MaterialStd_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.MaterialStd';
END
GO

IF OBJECT_ID('dbo.MaterialStd', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MaterialStd_Grade' AND object_id = OBJECT_ID('dbo.MaterialStd'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_MaterialStd_Grade
        ON dbo.MaterialStd (SubscID, Grade, MatrStd)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.MaterialStd', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MaterialStd_MatrStd' AND object_id = OBJECT_ID('dbo.MaterialStd'))
    CREATE NONCLUSTERED INDEX IX_MaterialStd_MatrStd
        ON dbo.MaterialStd (SubscID, MatrStd)
        INCLUDE (Grade)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  15. dbo.HSNMaster
==============================================================================*/
IF OBJECT_ID('dbo.HSNMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HSNMaster
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        HSNCode             varchar(10)         NOT NULL,
        HSNDescription      varchar(250)        NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_HSNMaster_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_HSNMaster_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_HSNMaster_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_HSNMaster_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_HSNMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_HSNMaster_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_HSNMaster_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.HSNMaster';
END
GO

IF OBJECT_ID('dbo.HSNMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_HSNMaster_HSNCode' AND object_id = OBJECT_ID('dbo.HSNMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_HSNMaster_HSNCode
        ON dbo.HSNMaster (SubscID, HSNCode)
        INCLUDE (HSNDescription)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  16. dbo.PriceList
      VendorNo -> SupplierMaster.MTransNo, PartNo -> PartMaster.MTransNo.
      No unique key: a vendor/part pair holds many rate rows over time, keyed by
      EffectiveDt. SP_PriceList rejects a second row for the same
      vendor/part/grade on the same date, which is the rule the commented index
      below would enforce in the engine.
==============================================================================*/
IF OBJECT_ID('dbo.PriceList', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PriceList
    (
        MTransNo            INT IDENTITY(1,1)   NOT NULL,
        VendorNo            INT                 NOT NULL,
        PartNo              INT                 NOT NULL,
        GradeNo             INT                 NULL,
        Grade               varchar(50)         NULL,
        Unit                varchar(10)         NULL,
        Rate                numeric(18, 4)      NULL,
        EffectiveDt         date                NULL,
        ItemRemarks         varchar(250)        NULL,
        LeadTime            INT                 NULL,
        SystemIP            varchar(20)         NULL,
        CreatedBy           INT                 NULL,
        CreatedOn           DATETIME2(3)        NOT NULL CONSTRAINT DF_PriceList_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy          INT                 NULL,
        ModifiedOn          DATETIME2(3)        NULL,
        DeleteStatus        CHAR(1)             NOT NULL CONSTRAINT DF_PriceList_DeleteStatus DEFAULT ('N'),
        DeleteReason        varchar(250)        NULL,
        LockStatus          CHAR(1)             NOT NULL CONSTRAINT DF_PriceList_LockStatus   DEFAULT ('N'),
        LockCounter         INT                 NOT NULL CONSTRAINT DF_PriceList_LockCounter  DEFAULT (0),
        SubscID             INT                 NOT NULL,

        CONSTRAINT PK_PriceList PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT FK_PriceList_SupplierMaster
            FOREIGN KEY (VendorNo) REFERENCES dbo.SupplierMaster (MTransNo),
        CONSTRAINT FK_PriceList_PartMaster
            FOREIGN KEY (PartNo)   REFERENCES dbo.PartMaster (MTransNo),
        CONSTRAINT CK_PriceList_DeleteStatus CHECK (DeleteStatus IN ('Y','N')),
        CONSTRAINT CK_PriceList_LockStatus   CHECK (LockStatus   IN ('Y','N'))
    );
    PRINT 'Created dbo.PriceList';
END
GO

IF OBJECT_ID('dbo.PriceList', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PriceList_PartNo' AND object_id = OBJECT_ID('dbo.PriceList'))
    CREATE NONCLUSTERED INDEX IX_PriceList_PartNo
        ON dbo.PriceList (PartNo, EffectiveDt)
        INCLUDE (VendorNo, Grade, Rate, Unit)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.PriceList', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PriceList_VendorNo' AND object_id = OBJECT_ID('dbo.PriceList'))
    CREATE NONCLUSTERED INDEX IX_PriceList_VendorNo
        ON dbo.PriceList (VendorNo, PartNo)
        INCLUDE (Grade, Rate, EffectiveDt)
        WHERE DeleteStatus = 'N';
GO

/*  Optional - let the engine, rather than SP_PriceList, stop the same
    vendor/part/grade being priced twice on one effective date:

CREATE UNIQUE NONCLUSTERED INDEX UX_PriceList_Vendor_Part_Grade_Dt
    ON dbo.PriceList (SubscID, VendorNo, PartNo, GradeNo, EffectiveDt)
    WHERE DeleteStatus = 'N';
GO
*/


PRINT '09_MasterTables.sql complete - 16 tables ready.';
GO
