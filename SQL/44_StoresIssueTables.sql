/*==============================================================================
  44. STORES ISSUE CHAIN  -  MRN (production / general), Store Issue, Store Returns

  Tables and save procedures cloned from the AquaFlow reference database
  (EMSAFT), so the EMSSoft pages and controllers can follow the legacy flow:

      MRNMst / MRNDtl                 material requisition (MRN)
          MRNType 'PROD'  production MRN   (Modules/Production/MRN.html)
          MRNType 'GEN'   general MRN      (Modules/Production/MRNG.html)
      StoreIssueMst / StoreIssueDtl   issue against an approved MRN
          TargetTransNo / TargetDocNo -> MRNMst, StoreIssueDtl.LinkNo -> MRNDtl
      StoreReturnsMst / StoreReturnsDtl  return against a store issue
          TargetTransNo / TargetDocNo -> StoreIssueMst, LinkNo -> StoreIssueDtl

  Differences from the reference:
    * LinkNo added on StoreIssueDtl / StoreReturnsDtl so balances are traced
      per line instead of by PartNo.
    * SP_StoreIssueDtl / SP_StoreReturnsDtl return @ID on insert and update
      (the reference returned nothing).
    * Doc numbers unique per SubscID + DocSeries + DocYear (+ MRNType for MRN)
      among live rows, as in the reference UX_ indexes.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  TABLES
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.MRNMst', N'U') IS NULL
CREATE TABLE dbo.MRNMst
(
    MTransNo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MRNMst PRIMARY KEY,
    DocNo          INT           NOT NULL,
    DocDate        DATE          NOT NULL,
    DocYear        INT           NOT NULL,
    DocSeries      CHAR(10)      NOT NULL,
    MRNType        VARCHAR(30)   NULL,
    TargetDocNo    INT           NULL,
    TargetTransNo  INT           NULL,
    TargetAmdNo    INT           NULL,
    TargetDocYear  INT           NULL,
    Requiredby     VARCHAR(50)   NULL,
    Department     VARCHAR(50)   NULL,
    RefNo          VARCHAR(50)   NULL,
    RefDate        DATE          NULL,
    DocNotes       VARCHAR(250)  NULL,
    DocStatus      CHAR(1)       NULL,
    LS             CHAR(1)       NULL,
    FS             CHAR(1)       NULL,
    SystemIP       VARCHAR(20)   NULL,
    LockStatus     CHAR(1)       NOT NULL CONSTRAINT DF_MRNMst_LockStatus DEFAULT ('N'),
    DeleteStatus   CHAR(1)       NOT NULL CONSTRAINT DF_MRNMst_DeleteStatus DEFAULT ('N'),
    LockCounter    TINYINT       NOT NULL CONSTRAINT DF_MRNMst_LockCounter DEFAULT (1),
    CreatedBy      SMALLINT      NOT NULL,
    CreatedOn      DATETIME      NOT NULL,
    ModifiedBy     SMALLINT      NULL,
    ModifiedOn     DATETIME      NULL,
    DeleteReason   VARCHAR(250)  NULL,
    SubscID        SMALLINT      NOT NULL,
    TempNo         INT           NULL
);
GO

IF OBJECT_ID(N'dbo.MRNDtl', N'U') IS NULL
CREATE TABLE dbo.MRNDtl
(
    TransSlNo        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MRNDtl PRIMARY KEY,
    MTransNo         INT            NOT NULL,
    SlNo             INT            NULL,
    LinkNo           INT            NULL,
    PartNo           INT            NOT NULL,
    Description      VARCHAR(250)   NULL,
    ItemRemarks      VARCHAR(250)   NULL,
    Qty              NUMERIC(12,2)  NULL,
    BalQty           NUMERIC(12,2)  NOT NULL CONSTRAINT DF_MRNDtl_BalQty DEFAULT (0),
    Unit             VARCHAR(20)    NULL,
    Rate             NUMERIC(12,2)  NULL,
    Amount           NUMERIC(14,2)  NULL,
    Store            VARCHAR(50)    NULL,
    CreatedBy        INT            NULL,
    CreatedOn        DATETIME       NULL,
    ModifiedBy       INT            NULL,
    ModifiedOn       DATETIME       NULL,
    DeleteStatus     CHAR(1)        NOT NULL CONSTRAINT DF_MRNDtl_DeleteStatus DEFAULT ('N'),
    LS               CHAR(1)        NULL,
    FS               CHAR(1)        NULL,
    SubscID          SMALLINT       NOT NULL,
    TempNo           INT            NULL,
    OAYear           INT            NULL,
    OANo             INT            NULL,
    OAID             INT            NULL,
    GradeNo          INT            NULL,
    Grade            VARCHAR(50)    NULL,
    ConvPartNo       INT            NULL,
    ConvDescription  VARCHAR(250)   NULL,
    ConvGrade        VARCHAR(50)    NULL,
    ConvGradeNo      INT            NULL,
    ConvQty          NUMERIC(12,2)  NULL
);
GO

IF OBJECT_ID(N'dbo.StoreIssueMst', N'U') IS NULL
CREATE TABLE dbo.StoreIssueMst
(
    MTransNo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StoreIssueMst PRIMARY KEY,
    DocSeries      CHAR(10)      NOT NULL,
    DocYear        INT           NOT NULL,
    DocNo          INT           NOT NULL,
    DocDate        DATE          NOT NULL,
    TargetTransNo  INT           NULL,
    TargetDocYear  INT           NULL,
    TargetDocNo    INT           NULL,
    TargetAmdNo    INT           NULL,
    TargetType     VARCHAR(50)   NULL,
    IndentorName   VARCHAR(100)  NOT NULL,
    Department     VARCHAR(50)   NOT NULL,
    DocNotes       VARCHAR(250)  NULL,
    DocStatus      CHAR(10)      NOT NULL CONSTRAINT DF_StoreIssueMst_DocStatus DEFAULT ('N'),
    SystemIP       VARCHAR(20)   NULL,
    LS             CHAR(1)       NOT NULL CONSTRAINT DF_StoreIssueMst_LS DEFAULT ('Y'),
    FS             CHAR(1)       NOT NULL CONSTRAINT DF_StoreIssueMst_FS DEFAULT ('N'),
    LockStatus     CHAR(1)       NOT NULL CONSTRAINT DF_StoreIssueMst_LockStatus DEFAULT ('N'),
    DeleteStatus   CHAR(1)       NOT NULL CONSTRAINT DF_StoreIssueMst_DeleteStatus DEFAULT ('N'),
    LockCounter    TINYINT       NOT NULL CONSTRAINT DF_StoreIssueMst_LockCounter DEFAULT (1),
    CreatedBy      INT           NOT NULL,
    CreatedOn      DATETIME      NOT NULL,
    ModifiedBy     INT           NULL,
    ModifiedOn     DATETIME      NULL,
    DeleteReason   VARCHAR(250)  NULL,
    SubscID        INT           NOT NULL
);
GO

IF OBJECT_ID(N'dbo.StoreIssueDtl', N'U') IS NULL
CREATE TABLE dbo.StoreIssueDtl
(
    TransSlNo        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StoreIssueDtl PRIMARY KEY,
    SlNo             NUMERIC(12,2)  NULL,
    MTransNo         INT            NOT NULL,
    LinkNo           INT            NULL,
    OAYear           INT            NULL,
    OANo             INT            NULL,
    OAID             INT            NULL,
    PartNo           INT            NOT NULL,
    PartCode         VARCHAR(30)    NULL,
    PartDescription  VARCHAR(100)   NULL,
    GradeNo          INT            NULL,
    Grade            VARCHAR(50)    NULL,
    ItemRemarks      VARCHAR(500)   NULL,
    DCOpration       VARCHAR(150)   NULL,
    MRNQty           NUMERIC(12,4)  NOT NULL CONSTRAINT DF_StoreIssueDtl_MRNQty DEFAULT (0),
    Qty              NUMERIC(12,4)  NOT NULL,
    Unit             VARCHAR(20)    NULL,
    CreatedBy        INT            NOT NULL,
    CreatedOn        DATETIME       NOT NULL,
    ModifiedBy       INT            NULL,
    ModifiedOn       DATETIME       NULL,
    DeleteStatus     CHAR(1)        NOT NULL CONSTRAINT DF_StoreIssueDtl_DeleteStatus DEFAULT ('N'),
    LS               CHAR(1)        NOT NULL CONSTRAINT DF_StoreIssueDtl_LS DEFAULT ('Y'),
    FS               CHAR(1)        NOT NULL CONSTRAINT DF_StoreIssueDtl_FS DEFAULT ('N'),
    SubscID          INT            NOT NULL
);
GO

IF OBJECT_ID(N'dbo.StoreReturnsMst', N'U') IS NULL
CREATE TABLE dbo.StoreReturnsMst
(
    MTransNo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StoreReturnsMst PRIMARY KEY,
    DocSeries      CHAR(10)      NOT NULL,
    DocYear        INT           NOT NULL,
    DocNo          INT           NOT NULL,
    DocDate        DATE          NOT NULL,
    TargetTransNo  INT           NULL,
    TargetDocYear  INT           NULL,
    TargetDocNo    INT           NULL,
    TargetAmdNo    INT           NULL,
    DocNotes       VARCHAR(250)  NULL,
    DocStatus      CHAR(10)      NOT NULL CONSTRAINT DF_StoreReturnsMst_DocStatus DEFAULT ('N'),
    SystemIP       VARCHAR(20)   NULL,
    LS             CHAR(1)       NOT NULL CONSTRAINT DF_StoreReturnsMst_LS DEFAULT ('Y'),
    FS             CHAR(1)       NOT NULL CONSTRAINT DF_StoreReturnsMst_FS DEFAULT ('N'),
    LockStatus     CHAR(1)       NOT NULL CONSTRAINT DF_StoreReturnsMst_LockStatus DEFAULT ('N'),
    DeleteStatus   CHAR(1)       NOT NULL CONSTRAINT DF_StoreReturnsMst_DeleteStatus DEFAULT ('N'),
    LockCounter    TINYINT       NOT NULL CONSTRAINT DF_StoreReturnsMst_LockCounter DEFAULT (1),
    CreatedBy      INT           NOT NULL,
    CreatedOn      DATETIME      NOT NULL,
    ModifiedBy     INT           NULL,
    ModifiedOn     DATETIME      NULL,
    DeleteReason   VARCHAR(250)  NULL,
    SubscID        INT           NOT NULL
);
GO

IF OBJECT_ID(N'dbo.StoreReturnsDtl', N'U') IS NULL
CREATE TABLE dbo.StoreReturnsDtl
(
    TransSlNo        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StoreReturnsDtl PRIMARY KEY,
    SlNo             NUMERIC(12,2)  NULL,
    MTransNo         INT            NOT NULL,
    LinkNo           INT            NULL,
    PartNo           INT            NOT NULL,
    PartCode         VARCHAR(30)    NULL,
    PartDescription  VARCHAR(100)   NULL,
    ItemRemarks      VARCHAR(500)   NULL,
    IssueQty         NUMERIC(12,4)  NOT NULL CONSTRAINT DF_StoreReturnsDtl_IssueQty DEFAULT (0),
    RTNQty           NUMERIC(12,4)  NOT NULL,
    Unit             VARCHAR(20)    NULL,
    CreatedBy        INT            NOT NULL,
    CreatedOn        DATETIME       NOT NULL,
    ModifiedBy       INT            NULL,
    ModifiedOn       DATETIME       NULL,
    DeleteStatus     CHAR(1)        NOT NULL CONSTRAINT DF_StoreReturnsDtl_DeleteStatus DEFAULT ('N'),
    LS               CHAR(1)        NOT NULL CONSTRAINT DF_StoreReturnsDtl_LS DEFAULT ('Y'),
    FS               CHAR(1)        NOT NULL CONSTRAINT DF_StoreReturnsDtl_FS DEFAULT ('N'),
    SubscID          INT            NOT NULL,
    OAYear           INT            NULL,
    OANo             INT            NULL,
    OAID             INT            NULL,
    GradeNo          INT            NULL,
    Grade            VARCHAR(50)    NULL
);
GO

/* LinkNo on tables that may predate this script */
IF COL_LENGTH('dbo.StoreIssueDtl', 'LinkNo') IS NULL
    ALTER TABLE dbo.StoreIssueDtl ADD LinkNo INT NULL;
IF COL_LENGTH('dbo.StoreReturnsDtl', 'LinkNo') IS NULL
    ALTER TABLE dbo.StoreReturnsDtl ADD LinkNo INT NULL;
GO

/*------------------------------------------------------------------------------
  INDEXES
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_MRNMst_DocNo' AND object_id = OBJECT_ID(N'dbo.MRNMst'))
    CREATE UNIQUE INDEX UX_MRNMst_DocNo ON dbo.MRNMst (SubscID, DocSeries, DocYear, MRNType, DocNo) WHERE DeleteStatus = 'N';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MRNDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.MRNDtl'))
    CREATE INDEX IX_MRNDtl_MTransNo ON dbo.MRNDtl (MTransNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StoreIssueMst_DocNo' AND object_id = OBJECT_ID(N'dbo.StoreIssueMst'))
    CREATE UNIQUE INDEX UX_StoreIssueMst_DocNo ON dbo.StoreIssueMst (SubscID, DocSeries, DocYear, DocNo) WHERE DeleteStatus = 'N';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreIssueDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.StoreIssueDtl'))
    CREATE INDEX IX_StoreIssueDtl_MTransNo ON dbo.StoreIssueDtl (MTransNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreIssueDtl_LinkNo' AND object_id = OBJECT_ID(N'dbo.StoreIssueDtl'))
    CREATE INDEX IX_StoreIssueDtl_LinkNo ON dbo.StoreIssueDtl (LinkNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_StoreReturnsMst_DocNo' AND object_id = OBJECT_ID(N'dbo.StoreReturnsMst'))
    CREATE UNIQUE INDEX UX_StoreReturnsMst_DocNo ON dbo.StoreReturnsMst (SubscID, DocSeries, DocYear, DocNo) WHERE DeleteStatus = 'N';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreReturnsDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.StoreReturnsDtl'))
    CREATE INDEX IX_StoreReturnsDtl_MTransNo ON dbo.StoreReturnsDtl (MTransNo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StoreReturnsDtl_LinkNo' AND object_id = OBJECT_ID(N'dbo.StoreReturnsDtl'))
    CREATE INDEX IX_StoreReturnsDtl_LinkNo ON dbo.StoreReturnsDtl (LinkNo);
GO

/*------------------------------------------------------------------------------
  SP_MRNMst  -  allocates DocNo per DocSeries + DocYear + MRNType on insert
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MRNMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_MRNMst AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_MRNMst
    @ID int output,
    @MTransNo int,
    @DocSeries char(10),
    @DocYear int,
    @DocNo int,
    @DocDate date,
    @TargetTransNo int,
    @TargetDocYear int,
    @TargetDocNo int,
    @TargetAmdNo int,
    @MRNType varchar(30),
    @Requiredby varchar(50),
    @Department varchar(50),
    @RefNo varchar(50),
    @RefDate date,
    @DocNotes varchar(250),
    @DocStatus char(1),
    @SystemIP varchar(20),
    @LockStatus char(1),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    IF (@MTransNo = 0)
    BEGIN
        SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
          FROM dbo.MRNMst WITH (UPDLOCK, HOLDLOCK)
         WHERE DocYear = @DocYear AND DocSeries = @DocSeries AND MRNType = @MRNType
           AND SubscID = @SubscID AND DeleteStatus = 'N';

        INSERT INTO dbo.MRNMst
        (
            DocSeries, DocYear, DocNo, DocDate, MRNType, TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
            Requiredby, Department, RefNo, RefDate, DocNotes, DocStatus, SystemIP, LockStatus, DeleteStatus,
            LockCounter, CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @DocSeries, @DocYear, @DocNo, @DocDate, @MRNType, @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @Requiredby, @Department, @RefNo, @RefDate, @DocNotes, @DocStatus, @SystemIP, @LockStatus, 'N',
            1, @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.MRNMst
           SET DocDate = @DocDate, MRNType = @MRNType, RefNo = @RefNo, RefDate = @RefDate,
               Requiredby = @Requiredby, Department = @Department, DocNotes = @DocNotes, DocStatus = @DocStatus,
               SystemIP = @SystemIP, LockStatus = @LockStatus, LockCounter = LockCounter + 1,
               ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE MTransNo = @MTransNo;
        SELECT @ID = @MTransNo;
    END
    COMMIT TRAN;
END
GO

/*------------------------------------------------------------------------------
  SP_MRNDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MRNDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_MRNDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_MRNDtl
    @ID int output,
    @MTransNo int,
    @TransSlNo int,
    @SlNo int,
    @LinkNo int,
    @PartNo int,
    @OAYear int,
    @OANo int,
    @OAID int,
    @PartDescription varchar(250),
    @GradeNo int,
    @Grade varchar(50),
    @ItemRemarks varchar(250),
    @Store varchar(50),
    @Qty numeric(12, 2),
    @BalQty numeric(12, 2),
    @Unit varchar(20),
    @Rate numeric(12, 2),
    @Amount numeric(14, 2),
    @ConvPartNo int,
    @ConvDescription varchar(250),
    @ConvGradeNo int,
    @ConvGrade varchar(50),
    @ConvQty numeric(12, 2),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    IF (@TransSlNo = 0)
    BEGIN
        INSERT INTO dbo.MRNDtl
        (
            MTransNo, SlNo, OAYear, OANo, OAID, PartNo, LinkNo, Description, GradeNo, Grade, ItemRemarks, Store,
            Qty, BalQty, Unit, Rate, Amount, ConvPartNo, ConvDescription, ConvGradeNo, ConvGrade, ConvQty,
            DeleteStatus, CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @MTransNo, @SlNo, @OAYear, @OANo, @OAID, @PartNo, @LinkNo, @PartDescription, @GradeNo, @Grade, @ItemRemarks, @Store,
            @Qty, @BalQty, @Unit, @Rate, @Amount, @ConvPartNo, @ConvDescription, @ConvGradeNo, @ConvGrade, @ConvQty,
            'N', @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.MRNDtl
           SET SlNo = @SlNo, OAYear = @OAYear, OANo = @OANo, OAID = @OAID, PartNo = @PartNo,
               Description = @PartDescription, GradeNo = @GradeNo, Grade = @Grade, ItemRemarks = @ItemRemarks,
               Store = @Store, Qty = @Qty, BalQty = @BalQty, Unit = @Unit, Rate = @Rate, Amount = @Amount,
               ConvPartNo = @ConvPartNo, ConvDescription = @ConvDescription, ConvGradeNo = @ConvGradeNo,
               ConvGrade = @ConvGrade, ConvQty = @ConvQty, ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE TransSlNo = @TransSlNo;
        SELECT @ID = @TransSlNo;
    END
END
GO

/*------------------------------------------------------------------------------
  SP_StoreIssueMst
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreIssueMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StoreIssueMst AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_StoreIssueMst
    @ID int output,
    @MTransNo int,
    @DocSeries char(10),
    @DocYear int,
    @DocNo int,
    @DocDate date,
    @TargetTransNo int,
    @TargetDocYear int,
    @TargetDocNo int,
    @TargetAmdNo int,
    @TargetType varchar(50),
    @IndentorName varchar(100),
    @Department varchar(50),
    @DocNotes varchar(250),
    @DocStatus char(1),
    @SystemIP varchar(20),
    @LockStatus char(1),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    IF (@MTransNo = 0)
    BEGIN
        SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
          FROM dbo.StoreIssueMst WITH (UPDLOCK, HOLDLOCK)
         WHERE DocYear = @DocYear AND DocSeries = @DocSeries AND SubscID = @SubscID AND DeleteStatus = 'N';

        INSERT INTO dbo.StoreIssueMst
        (
            DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
            IndentorName, Department, DocNotes, DocStatus, SystemIP, LockStatus, DeleteStatus, LockCounter,
            CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @DocSeries, @DocYear, @DocNo, @DocDate, @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo, @TargetType,
            @IndentorName, @Department, @DocNotes, @DocStatus, @SystemIP, @LockStatus, 'N', 1,
            @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.StoreIssueMst
           SET DocDate = @DocDate, TargetType = @TargetType, IndentorName = @IndentorName, Department = @Department,
               DocNotes = @DocNotes, DocStatus = @DocStatus, SystemIP = @SystemIP, LockStatus = @LockStatus,
               LockCounter = LockCounter + 1, ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE MTransNo = @MTransNo;
        SELECT @ID = @MTransNo;
    END
    COMMIT TRAN;
END
GO

/*------------------------------------------------------------------------------
  SP_StoreIssueDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreIssueDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StoreIssueDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_StoreIssueDtl
    @ID int output,
    @MTransNo int,
    @TransSlNo int,
    @SlNo int,
    @LinkNo int,
    @PartNo int,
    @OAYear int,
    @OANo int,
    @OAID int,
    @PartCode varchar(30),
    @PartDescription varchar(100),
    @GradeNo int,
    @Grade varchar(50),
    @ItemRemarks varchar(500),
    @DCOpration varchar(150),
    @MRNQty numeric(12, 4),
    @Qty numeric(12, 4),
    @Unit varchar(20),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    IF (@TransSlNo = 0)
    BEGIN
        INSERT INTO dbo.StoreIssueDtl
        (
            MTransNo, SlNo, LinkNo, OAYear, OANo, OAID, PartNo, PartCode, PartDescription, GradeNo, Grade,
            ItemRemarks, DCOpration, MRNQty, Qty, Unit, DeleteStatus, CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @MTransNo, @SlNo, @LinkNo, @OAYear, @OANo, @OAID, @PartNo, @PartCode, @PartDescription, @GradeNo, @Grade,
            @ItemRemarks, @DCOpration, @MRNQty, @Qty, @Unit, 'N', @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.StoreIssueDtl
           SET SlNo = @SlNo, LinkNo = @LinkNo, OAYear = @OAYear, OANo = @OANo, OAID = @OAID, PartNo = @PartNo,
               PartCode = @PartCode, PartDescription = @PartDescription, GradeNo = @GradeNo, Grade = @Grade,
               ItemRemarks = @ItemRemarks, DCOpration = @DCOpration, MRNQty = @MRNQty, Qty = @Qty, Unit = @Unit,
               ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE TransSlNo = @TransSlNo;
        SELECT @ID = @TransSlNo;
    END
END
GO

/*------------------------------------------------------------------------------
  SP_StoreReturnsMst
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreReturnsMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StoreReturnsMst AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_StoreReturnsMst
    @ID int output,
    @MTransNo int,
    @DocSeries char(10),
    @DocYear int,
    @DocNo int,
    @DocDate date,
    @TargetTransNo int,
    @TargetDocYear int,
    @TargetDocNo int,
    @TargetAmdNo int,
    @DocNotes varchar(250),
    @DocStatus char(1),
    @SystemIP varchar(20),
    @LockStatus char(1),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRAN;
    IF (@MTransNo = 0)
    BEGIN
        SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
          FROM dbo.StoreReturnsMst WITH (UPDLOCK, HOLDLOCK)
         WHERE DocYear = @DocYear AND DocSeries = @DocSeries AND SubscID = @SubscID AND DeleteStatus = 'N';

        INSERT INTO dbo.StoreReturnsMst
        (
            DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
            DocNotes, DocStatus, SystemIP, LockStatus, DeleteStatus, LockCounter, CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @DocSeries, @DocYear, @DocNo, @DocDate, @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @DocNotes, @DocStatus, @SystemIP, @LockStatus, 'N', 1, @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.StoreReturnsMst
           SET DocDate = @DocDate, DocNotes = @DocNotes, DocStatus = @DocStatus, SystemIP = @SystemIP,
               LockStatus = @LockStatus, LockCounter = LockCounter + 1, ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE MTransNo = @MTransNo;
        SELECT @ID = @MTransNo;
    END
    COMMIT TRAN;
END
GO

/*------------------------------------------------------------------------------
  SP_StoreReturnsDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreReturnsDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StoreReturnsDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_StoreReturnsDtl
    @ID int output,
    @MTransNo int,
    @TransSlNo int,
    @SlNo int,
    @LinkNo int,
    @PartNo int,
    @OAYear int,
    @OANo int,
    @OAID int,
    @PartCode varchar(30),
    @PartDescription varchar(100),
    @GradeNo int,
    @Grade varchar(50),
    @ItemRemarks varchar(500),
    @IssueQty numeric(12, 4),
    @RTNQty numeric(12, 4),
    @Unit varchar(20),
    @CreatedBy int,
    @CreatedOn datetime,
    @SubscID int
AS
BEGIN
    SET NOCOUNT ON;
    IF (@TransSlNo = 0)
    BEGIN
        INSERT INTO dbo.StoreReturnsDtl
        (
            MTransNo, SlNo, LinkNo, OAYear, OANo, OAID, PartNo, PartCode, PartDescription, GradeNo, Grade,
            ItemRemarks, IssueQty, RTNQty, Unit, DeleteStatus, CreatedBy, CreatedOn, SubscID, LS, FS
        )
        VALUES
        (
            @MTransNo, @SlNo, @LinkNo, @OAYear, @OANo, @OAID, @PartNo, @PartCode, @PartDescription, @GradeNo, @Grade,
            @ItemRemarks, @IssueQty, @RTNQty, @Unit, 'N', @CreatedBy, @CreatedOn, @SubscID, 'Y', 'N'
        );
        SELECT @ID = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE dbo.StoreReturnsDtl
           SET SlNo = @SlNo, LinkNo = @LinkNo, OAYear = @OAYear, OANo = @OANo, OAID = @OAID, PartNo = @PartNo,
               PartCode = @PartCode, PartDescription = @PartDescription, GradeNo = @GradeNo, Grade = @Grade,
               ItemRemarks = @ItemRemarks, IssueQty = @IssueQty, RTNQty = @RTNQty, Unit = @Unit,
               ModifiedBy = @CreatedBy, ModifiedOn = @CreatedOn
         WHERE TransSlNo = @TransSlNo;
        SELECT @ID = @TransSlNo;
    END
END
GO

PRINT '44_StoresIssueTables applied.';
GO
