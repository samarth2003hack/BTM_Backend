/*==============================================================================
  EMSSoft — 05_AccountingTables.sql
  ------------------------------------------------------------------------------
  The accounting module carried over from LMSAPI:

      dbo.GroupHeads      chart-of-accounts group tree (self-referencing)
      dbo.AccountsMaster  ledger accounts, each under one group head
      dbo.AccountConfig   maps a module + account role to a ledger account
      dbo.TaxMaster       GST / cess rates and their posting accounts
      dbo.GroupView       flattened 4-level view of the GroupHeads tree

  Column types match the live LoanDRC definitions exactly, so data can be moved
  between the two databases without conversion.

  Run after 04_SeedData.sql:
      05_AccountingTables.sql
      06_AccountingProcedures.sql
      07_AccountingSeed.sql
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  1. dbo.GroupHeads
     GrpStatus 'P' = primary group (top of a tree), 'S' = sub-group.
     GrpMastNo points at the parent GrpNo; it is NULL/0 for primary groups.
     BSStatus places the group on the Balance Sheet or the P&L.
==============================================================================*/
IF OBJECT_ID('dbo.GroupHeads', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GroupHeads
    (
        GrpNo         INT IDENTITY(1,1) NOT NULL,
        GrpName       varchar(50)   NOT NULL,
        GrpMastNo     INT           NULL,
        GrpStatus     char(1)       NOT NULL CONSTRAINT DF_GroupHeads_GrpStatus    DEFAULT('S'),
        BSStatus      varchar(20)   NOT NULL,
        FAStatus      varchar(10)   NULL,
        SystemIP      varchar(20)   NULL,
        LockStatus    char(1)       NULL CONSTRAINT DF_GroupHeads_LockStatus   DEFAULT('N'),
        DeleteStatus  char(1)       NULL CONSTRAINT DF_GroupHeads_DeleteStatus DEFAULT('N'),
        LockCounter   INT           NULL,
        CreatedBy     INT           NULL,
        CreatedOn     DATETIME      NULL CONSTRAINT DF_GroupHeads_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME      NULL,
        DeleteReason  varchar(250)  NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_GroupHeads PRIMARY KEY CLUSTERED (GrpNo)
    );
    PRINT 'Created dbo.GroupHeads';
END
GO

IF OBJECT_ID('dbo.GroupHeads', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_GroupHeads_GrpName' AND object_id = OBJECT_ID('dbo.GroupHeads'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_GroupHeads_GrpName
        ON dbo.GroupHeads (SubscID, GrpName)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.GroupHeads', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GroupHeads_GrpMastNo' AND object_id = OBJECT_ID('dbo.GroupHeads'))
    CREATE NONCLUSTERED INDEX IX_GroupHeads_GrpMastNo
        ON dbo.GroupHeads (SubscID, GrpMastNo)
        INCLUDE (GrpName, GrpStatus, BSStatus)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  2. dbo.AccountsMaster
     AcType classifies the ledger for payment pickers (CASH / BANK / CUSTOMER /
     SUPPLIER / Asset / Liability / …). GroupHeadNo ties it into the tree above.
==============================================================================*/
IF OBJECT_ID('dbo.AccountsMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountsMaster
    (
        AcNo           INT IDENTITY(1,1) NOT NULL,
        AcName         varchar(100)  NOT NULL,
        AcAlias        varchar(100)  NULL,
        AcType         varchar(20)   NOT NULL,
        VendNo         INT           NULL,
        GroupHeadNo    INT           NOT NULL,
        CommonAccount  char(1)       NULL,
        SystemIP       varchar(20)   NULL,
        LockStatus     char(1)       NULL CONSTRAINT DF_AccountsMaster_LockStatus   DEFAULT('N'),
        DeleteStatus   char(1)       NULL CONSTRAINT DF_AccountsMaster_DeleteStatus DEFAULT('N'),
        LockCounter    INT           NULL,
        CreatedBy      INT           NULL,
        CreatedOn      DATETIME      NULL CONSTRAINT DF_AccountsMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy     INT           NULL,
        ModifiedOn     DATETIME      NULL,
        DeleteReason   varchar(250)  NULL,
        SubscID        INT           NULL,
        TempNo         INT           NULL,
        CONSTRAINT PK_AccountsMaster PRIMARY KEY CLUSTERED (AcNo)
    );
    PRINT 'Created dbo.AccountsMaster';
END
GO

IF OBJECT_ID('dbo.AccountsMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AccountsMaster_AcName' AND object_id = OBJECT_ID('dbo.AccountsMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_AccountsMaster_AcName
        ON dbo.AccountsMaster (SubscID, AcName)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.AccountsMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountsMaster_AcType' AND object_id = OBJECT_ID('dbo.AccountsMaster'))
    CREATE NONCLUSTERED INDEX IX_AccountsMaster_AcType
        ON dbo.AccountsMaster (SubscID, AcType)
        INCLUDE (AcName, AcAlias, GroupHeadNo, LockStatus)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.AccountsMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountsMaster_GroupHeadNo' AND object_id = OBJECT_ID('dbo.AccountsMaster'))
    CREATE NONCLUSTERED INDEX IX_AccountsMaster_GroupHeadNo
        ON dbo.AccountsMaster (SubscID, GroupHeadNo)
        INCLUDE (AcName, AcType)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  3. dbo.AccountConfig
     "Which ledger should module X use for role Y" — e.g.
     (ModuleName 'Sales', ACCType 'RoundOff') -> AcNo 47.
     One row per (ModuleName, ACCType, SubscID).
==============================================================================*/
IF OBJECT_ID('dbo.AccountConfig', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountConfig
    (
        MTransNo    INT IDENTITY(1,1) NOT NULL,
        ModuleName  varchar(50)  NULL,
        ACCType     varchar(30)  NULL,
        AcNo        INT          NULL,
        SubscID     INT          NULL,
        CONSTRAINT PK_AccountConfig PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_AccountConfig UNIQUE (ModuleName, ACCType, SubscID)
    );
    PRINT 'Created dbo.AccountConfig';
END
GO

IF OBJECT_ID('dbo.AccountConfig', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountConfig_Module' AND object_id = OBJECT_ID('dbo.AccountConfig'))
    CREATE NONCLUSTERED INDEX IX_AccountConfig_Module
        ON dbo.AccountConfig (SubscID, ModuleName, ACCType)
        INCLUDE (AcNo);
GO


/*==============================================================================
  4. dbo.TaxMaster
     TaxP is the total GST rate; the CGST/SGST/IGST/Cess columns name the ledger
     each component posts to. UsedIn restricts a rate to Sales, Purchase or both.
==============================================================================*/
IF OBJECT_ID('dbo.TaxMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TaxMaster
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL,
        TaxCode       varchar(20)   NOT NULL,
        TaxP          numeric(6,2)  NOT NULL,
        CessP         numeric(6,2)  NULL,
        CGSTAcNo      INT           NULL,
        SGSTAcNo      INT           NULL,
        IGSTAcNo      INT           NULL,
        CessAcNo      INT           NULL,
        PSACNo        INT           NULL,
        UsedIn        varchar(20)   NOT NULL,
        SystemIP      varchar(20)   NULL,
        LockStatus    char(1)       NULL CONSTRAINT DF_TaxMaster_LockStatus   DEFAULT('N'),
        LockCounter   INT           NULL,
        CreatedBy     INT           NULL,
        CreatedOn     DATETIME      NULL CONSTRAINT DF_TaxMaster_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy    INT           NULL,
        ModifiedOn    DATETIME      NULL,
        DeleteStatus  char(1)       NULL CONSTRAINT DF_TaxMaster_DeleteStatus DEFAULT('N'),
        DeleteReason  varchar(250)  NULL,
        SubscID       INT           NULL,
        CONSTRAINT PK_TaxMaster PRIMARY KEY CLUSTERED (MTransNo)
    );
    PRINT 'Created dbo.TaxMaster';
END
GO

IF OBJECT_ID('dbo.TaxMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxMaster_TaxCode' AND object_id = OBJECT_ID('dbo.TaxMaster'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_TaxMaster_TaxCode
        ON dbo.TaxMaster (SubscID, TaxCode)
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID('dbo.TaxMaster', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TaxMaster_UsedIn' AND object_id = OBJECT_ID('dbo.TaxMaster'))
    CREATE NONCLUSTERED INDEX IX_TaxMaster_UsedIn
        ON dbo.TaxMaster (SubscID, UsedIn)
        INCLUDE (TaxCode, TaxP, CessP)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  5. dbo.GroupView
     Flattens the GroupHeads tree to four levels and emits a sortable path (LNo)
     so a report can print the chart of accounts in hierarchical order.

     Carried over from LoanDRC with two corrections:
       - the source view had no SubscID and no DeleteStatus filter, so it mixed
         every subscription's groups together and kept deleted ones. Both are
         now projected/filtered.
       - the implicit-join syntax (FROM a, b WHERE ...) is written as explicit
         JOINs; the result set is identical.
==============================================================================*/
IF OBJECT_ID('dbo.GroupView', 'V') IS NOT NULL DROP VIEW dbo.GroupView;
GO
CREATE VIEW dbo.GroupView
AS
    SELECT '1' AS [Level],
           CONCAT('A', FORMAT(g1.GrpNo, '000'), '000000000') AS LNo,
           g1.GrpNo, g1.GrpName, g1.GrpMastNo, g1.BSStatus, g1.GrpStatus, g1.FAStatus, g1.SubscID
      FROM dbo.GroupHeads g1
     WHERE g1.GrpStatus = 'P' AND ISNULL(g1.DeleteStatus, 'N') = 'N'

    UNION ALL

    SELECT '2',
           CONCAT('A', FORMAT(g1.GrpNo, '000'), FORMAT(g2.GrpNo, '000'), '000000'),
           g2.GrpNo, g2.GrpName, g2.GrpMastNo, g2.BSStatus, g2.GrpStatus, g2.FAStatus, g2.SubscID
      FROM dbo.GroupHeads g1
      JOIN dbo.GroupHeads g2 ON g2.GrpMastNo = g1.GrpNo
     WHERE g1.GrpStatus = 'P' AND g2.GrpStatus = 'S'
       AND ISNULL(g1.DeleteStatus, 'N') = 'N' AND ISNULL(g2.DeleteStatus, 'N') = 'N'

    UNION ALL

    SELECT '3',
           CONCAT('A', FORMAT(g1.GrpNo, '000'), FORMAT(g2.GrpNo, '000'), FORMAT(g3.GrpNo, '000'), '000'),
           g3.GrpNo, g3.GrpName, g3.GrpMastNo, g3.BSStatus, g3.GrpStatus, g3.FAStatus, g3.SubscID
      FROM dbo.GroupHeads g1
      JOIN dbo.GroupHeads g2 ON g2.GrpMastNo = g1.GrpNo
      JOIN dbo.GroupHeads g3 ON g3.GrpMastNo = g2.GrpNo
     WHERE g1.GrpStatus = 'P' AND g2.GrpStatus = 'S' AND g3.GrpStatus = 'S'
       AND ISNULL(g1.DeleteStatus, 'N') = 'N' AND ISNULL(g2.DeleteStatus, 'N') = 'N'
       AND ISNULL(g3.DeleteStatus, 'N') = 'N'

    UNION ALL

    SELECT '4',
           CONCAT('A', FORMAT(g1.GrpNo, '000'), FORMAT(g2.GrpNo, '000'), FORMAT(g3.GrpNo, '000'), FORMAT(g4.GrpNo, '000')),
           g4.GrpNo, g4.GrpName, g4.GrpMastNo, g4.BSStatus, g4.GrpStatus, g4.FAStatus, g4.SubscID
      FROM dbo.GroupHeads g1
      JOIN dbo.GroupHeads g2 ON g2.GrpMastNo = g1.GrpNo
      JOIN dbo.GroupHeads g3 ON g3.GrpMastNo = g2.GrpNo
      JOIN dbo.GroupHeads g4 ON g4.GrpMastNo = g3.GrpNo
     WHERE g1.GrpStatus = 'P' AND g2.GrpStatus = 'S' AND g3.GrpStatus = 'S' AND g4.GrpStatus = 'S'
       AND ISNULL(g1.DeleteStatus, 'N') = 'N' AND ISNULL(g2.DeleteStatus, 'N') = 'N'
       AND ISNULL(g3.DeleteStatus, 'N') = 'N' AND ISNULL(g4.DeleteStatus, 'N') = 'N';
GO

PRINT '05_AccountingTables.sql complete — 4 tables + GroupView ready.';
GO
