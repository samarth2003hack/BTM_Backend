/*==============================================================================
  SALES MODULE - COMBINED DEPLOYMENT SCRIPT  (generated 2026-09-28)

  Runs, in order:
      26_FixSalesTargetFKs.sql       repoint two self-referencing foreign keys
      19_ProformaInvoice.sql         ProformaMst/Dtl/Terms/Extra + 4 procedures
      20_PackingSlip.sql             PackingSlipMst/Dtl + 2 procedures
      21_SalesInvoice.sql            STxnMst/Dtl/StxnTerms/STxnExtra, TransportMaster + 5 procedures
      23_QuotationAdditions.sql      SP_QtnMst - amendment / delete rules
      25_SalesMenuAndLookups.sql     Sales menu 801-807 + grants, OtherMaster lookup types

  HOW TO RUN
    sqlcmd:  sqlcmd -S <server> -E -C -b -i 00_SalesDeploy_All.sql
    SSMS:    Query > SQLCMD Mode ON, then execute (needed for :on error exit).
  Every part is idempotent - safe to re-run. Nothing is dropped except the two
  foreign keys in step 26, which are recreated immediately.

  BEFORE RUNNING: take a backup, and check @Folder in step 25 matches where the
  server serves the pages from (default 'Modules/Sales/').
==============================================================================*/
:on error exit

USE EMSSoft;
GO

/*------------------------------------------------------------------------------
  PRE-FLIGHT - these scripts build ON TOP of objects that were created directly
  in the dev database (no repo script exists for them). Stop before changing
  anything if the target server does not have them.
------------------------------------------------------------------------------*/
SET NOCOUNT ON;
DECLARE @Missing NVARCHAR(MAX) = N'';

SELECT @Missing += N', ' + R.Name
  FROM (VALUES (N'U', N'EnqMst'), (N'U', N'EnqDtl'), (N'U', N'QtnMst'), (N'U', N'QtnDtl'),
               (N'U', N'OAMst'),  (N'U', N'OADtl'),  (N'U', N'CustomerMaster'),
               (N'U', N'PartMaster'), (N'U', N'Company'), (N'U', N'BankMaster'),
               (N'U', N'Menulist'), (N'U', N'AccessTemplate'), (N'U', N'OtherMasterList'),
               (N'P', N'SP_QtnMst'), (N'P', N'SP_OAMst'), (N'P', N'SP_OADtl')) AS R(Type, Name)
 WHERE OBJECT_ID(N'dbo.' + R.Name, R.Type) IS NULL;

IF LEN(@Missing) > 0
BEGIN
    DECLARE @Msg NVARCHAR(MAX) = N'Pre-flight failed - missing in EMSSoft: ' + STUFF(@Missing, 1, 2, N'')
        + N'. Deploy the Enquiry / Quotation / Order Acceptance objects first. Nothing was changed.';
    RAISERROR(@Msg, 16, 1);
END
ELSE
    PRINT 'Pre-flight OK - prerequisites present.';
GO


PRINT '>>> 26_FixSalesTargetFKs.sql';
GO

/*==============================================================================
  26. FIX SALES TARGET FOREIGN KEYS

  Two constraints were created pointing at their OWN table instead of the
  upstream document their names describe:

      FK_QtnMst_EnqMst   QtnMst.TargetTransNo -> QtnMst.MTransNo   (should be EnqMst)
      FK_OAMst_QtnMst    OAMst.TargetTransNo  -> OAMst.MTransNo    (should be QtnMst)

  With them in place a quotation cannot be raised against an enquiry, nor an
  order acceptance against a quotation - the insert fails unless the enquiry's
  MTransNo happens to equal an existing quotation's. Each is dropped and
  recreated against the intended table. TargetTransNo stays nullable, so a
  document raised without an upstream reference is unaffected.

  Recreating the constraint validates existing rows: if any TargetTransNo does
  not match a row in the upstream table, the ADD fails and nothing is changed
  for that constraint - clean the row up first.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys
            WHERE name = 'FK_QtnMst_EnqMst'
              AND referenced_object_id <> OBJECT_ID(N'dbo.EnqMst'))
    ALTER TABLE dbo.QtnMst DROP CONSTRAINT FK_QtnMst_EnqMst;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_QtnMst_EnqMst')
    ALTER TABLE dbo.QtnMst WITH CHECK
      ADD CONSTRAINT FK_QtnMst_EnqMst FOREIGN KEY (TargetTransNo) REFERENCES dbo.EnqMst (MTransNo);
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys
            WHERE name = 'FK_OAMst_QtnMst'
              AND referenced_object_id <> OBJECT_ID(N'dbo.QtnMst'))
    ALTER TABLE dbo.OAMst DROP CONSTRAINT FK_OAMst_QtnMst;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_OAMst_QtnMst')
    ALTER TABLE dbo.OAMst WITH CHECK
      ADD CONSTRAINT FK_OAMst_QtnMst FOREIGN KEY (TargetTransNo) REFERENCES dbo.QtnMst (MTransNo);
GO

-- What is in place now.
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS ParentTable,
       OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable
  FROM sys.foreign_keys fk
 WHERE fk.name IN ('FK_QtnMst_EnqMst', 'FK_OAMst_QtnMst');
GO
GO


PRINT '>>> 19_ProformaInvoice.sql';
GO

/*
================================================================================
  19_ProformaInvoice.sql
  Proforma Invoice (Sales) — tables + write procedures.

    Tables : ProformaMst, ProformaDtl, ProformaTerms, ProformaExtra
    Procs  : SP_ProformaMst, SP_ProformaDtl, SP_ProformaTerms, SP_ProformaExtra

  A proforma bills an Order Acceptance in advance:
    ProformaMst.TargetTransNo / TargetDocYear / TargetDocNo / TargetAmdNo = the OA
    ProformaDtl.LinkNo                                                  = OADtl.TransSlNo

  Numbering: each DocSeries + DocYear + InvType (Domestic / Export) keeps its own
  DocNo sequence. SP_ProformaMst allocates DocNo itself on a first issue
  (AmdNo = 0) — whatever DocNo the client sent is only a preview.

  DEVIATION from the reference DDL: UQ_ProformaMst (and IX_ProformaMst_DocNo)
  also carry InvType. Without it Domestic #1 and Export #1 in the same series
  and year would collide, and the page's split numbering could never be saved.

  Idempotent: tables are created only when missing; procedures use
  create-stub-then-ALTER because master holds stray SP_Proforma* procedures
  that break CREATE OR ALTER (sp_ name resolution checks master first).
================================================================================
*/
USE EMSSoft;
GO

--------------------------------------------------------------------------------
-- 1. PROFORMAMST — proforma invoice header
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ProformaMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProformaMst
    (
        MTransNo INT IDENTITY(1,1) NOT NULL,
        DocNo INT NOT NULL,
        DocDate date NOT NULL,
        DocYear INT NOT NULL,
        DocSeries char(10) NOT NULL,
        TargetDocNo INT NULL,
        TargetTransNo INT NULL,
        TargetAmdNo INT NULL,
        TargetDocYear INT NULL,
        VendorNo INT NOT NULL,
        FromAccount INT NULL,
        VendorNoShip INT NULL,
        RefNo varchar(50) NULL,
        RefDate date NULL,
        DocNotes varchar(250) NULL,
        BasicValue numeric(16, 2) NULL,
        ExemptValue numeric(16, 2) NULL,
        TaxableValue numeric(16, 2) NULL,
        PnFAmt numeric(16, 2) NULL,
        PnFGSTP numeric(6, 2) NULL,
        PnFGSTAmt numeric(16, 2) NULL,
        FrightGSTP numeric(6, 2) NULL,
        FrightGSTAmt numeric(16, 2) NULL,
        IGSTAmt numeric(16, 2) NULL,
        CGSTAmt numeric(16, 2) NULL,
        SGSTAmt numeric(16, 2) NULL,
        OtherCharges varchar(50) NULL,
        OtherAmt numeric(16, 2) NULL,
        RoundOff numeric(6, 2) NULL,
        NetValue numeric(18, 2) NULL,
        Advance numeric(18, 2) NULL,
        Grossweight numeric(16, 2) NULL,
        Netweight numeric(16, 2) NULL,
        OtherP numeric(6, 2) NULL,
        TxnCurrency varchar(30) NULL,
        CurrencyRate numeric(12, 2) NULL,
        CurrencyAmount numeric(14, 2) NULL,
        ProjectNo INT NULL,
        oldCurrencyRate numeric(12, 2) NULL,
        oldCurrencyAmount numeric(12, 2) NULL,
        SalesRep varchar(50) NULL,
        TxnGSTIN varchar(20) NULL,
        IECodeNo varchar(50) NULL,
        Declaration varchar(500) NULL,
        BankNo INT NULL,
        InvTag char(10) NULL,
        AmdNo INT NULL,
        AmdNotes varchar(100) NULL,
        VendorCode varchar(20) NULL,
        VendorName varchar(100) NULL,
        Address varchar(500) NULL,
        GSTIN varchar(20) NULL,
        StateName varchar(50) NULL,
        PhoneNo varchar(30) NULL,
        EmailID varchar(80) NULL,
        DocStatus char(1) NULL,
        TaxType varchar(20) NULL,
        FreightAmt numeric(14, 2) NULL,
        InvType varchar(20) NULL,
        AdvanceP numeric(12, 0) NULL,
        DiscountAmt numeric(16, 2) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ProformaMst_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaMst_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaMst_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_ProformaMst PRIMARY KEY CLUSTERED (MTransNo),
        -- InvType added (see header note): numbering is split by invoice type.
        CONSTRAINT UQ_ProformaMst UNIQUE (DocSeries, DocYear, InvType, DocNo, AmdNo, DeleteStatus, SubscID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaMst_DocNo' AND object_id = OBJECT_ID(N'dbo.ProformaMst'))
    CREATE NONCLUSTERED INDEX IX_ProformaMst_DocNo
        ON dbo.ProformaMst (SubscID, DocSeries, DocYear, InvType, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaMst_DocDate' AND object_id = OBJECT_ID(N'dbo.ProformaMst'))
    CREATE NONCLUSTERED INDEX IX_ProformaMst_DocDate
        ON dbo.ProformaMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaMst_VendorNo' AND object_id = OBJECT_ID(N'dbo.ProformaMst'))
    CREATE NONCLUSTERED INDEX IX_ProformaMst_VendorNo
        ON dbo.ProformaMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaMst_TargetTransNo' AND object_id = OBJECT_ID(N'dbo.ProformaMst'))
    CREATE NONCLUSTERED INDEX IX_ProformaMst_TargetTransNo
        ON dbo.ProformaMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

--------------------------------------------------------------------------------
-- 2. PROFORMADTL — proforma invoice lines (child of ProformaMst)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ProformaDtl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProformaDtl
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NOT NULL,
        PartNo INT NOT NULL,
        Description varchar(250) NULL,
        ItemRemarks varchar(250) NULL,
        CustPartNo varchar(30) NULL,
        HSNCode varchar(10) NULL,
        Qty numeric(12, 2) NOT NULL,
        Unit varchar(20) NULL,
        Rate numeric(14, 2) NOT NULL,
        BasicValue numeric(16, 2) NOT NULL,
        DiscP numeric(6, 2) NULL,
        DiscAmt numeric(12, 2) NULL,
        PnFP numeric(6, 2) NULL,
        PnFAmt numeric(14, 2) NULL,
        PnFGSTAmt numeric(16, 2) NULL,
        FreightAmt numeric(16, 2) NULL,
        FreightGSTAmt numeric(14, 2) NULL,
        TaxableValue numeric(14, 2) NULL,
        GSTP numeric(6, 2) NULL,
        IGSTAmt numeric(14, 2) NULL,
        CGSTAmt numeric(14, 2) NULL,
        SGSTAmt numeric(14, 2) NULL,
        TotalValue numeric(16, 2) NULL,
        FreightRate numeric(16, 2) NULL,
        DTargetDocNo INT NULL,
        DTargetDocYear INT NULL,
        Packing varchar(50) NULL,
        BoxNo INT NULL,
        NetWeight numeric(16, 2) NULL,
        ChassisNo varchar(30) NULL,
        EngineNo varchar(30) NULL,
        RegdNo varchar(30) NULL,
        LinkNo INT NULL,
        SlNo INT NULL,
        PartCode varchar(50) NULL,
        DeleveryDate date NULL,
        Store varchar(50) NULL,
        VendorNo INT NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ProformaDtl_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaDtl_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaDtl_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_ProformaDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.ProformaDtl'))
    CREATE NONCLUSTERED INDEX IX_ProformaDtl_MTransNo
        ON dbo.ProformaDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, Description, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaDtl_PartNo' AND object_id = OBJECT_ID(N'dbo.ProformaDtl'))
    CREATE NONCLUSTERED INDEX IX_ProformaDtl_PartNo
        ON dbo.ProformaDtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit, Rate)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaDtl_VendorNo' AND object_id = OBJECT_ID(N'dbo.ProformaDtl'))
    CREATE NONCLUSTERED INDEX IX_ProformaDtl_VendorNo
        ON dbo.ProformaDtl (VendorNo)
        INCLUDE (MTransNo, PartNo, Qty, Rate)
        WHERE DeleteStatus = 'N';
GO

--------------------------------------------------------------------------------
-- 3. PROFORMATERMS — proforma invoice terms (child of ProformaMst)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ProformaTerms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProformaTerms
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NULL,
        SlNo numeric(6, 2) NULL,
        Title varchar(30) NULL,
        Description varchar(250) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ProformaTerms_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaTerms_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaTerms_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_ProformaTerms PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaTerms_MTransNo' AND object_id = OBJECT_ID(N'dbo.ProformaTerms'))
    CREATE NONCLUSTERED INDEX IX_ProformaTerms_MTransNo
        ON dbo.ProformaTerms (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO

--------------------------------------------------------------------------------
-- 4. PROFORMAEXTRA — proforma invoice extra lines (child of ProformaMst)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.ProformaExtra', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProformaExtra
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NULL,
        SlNo numeric(6, 2) NULL,
        Title varchar(30) NULL,
        Description varchar(250) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_ProformaExtra_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaExtra_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_ProformaExtra_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_ProformaExtra PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProformaExtra_MTransNo' AND object_id = OBJECT_ID(N'dbo.ProformaExtra'))
    CREATE NONCLUSTERED INDEX IX_ProformaExtra_MTransNo
        ON dbo.ProformaExtra (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO

--------------------------------------------------------------------------------
-- 5. SP_ProformaMst
--    Insert  : AmdNo 0 (first issue) allocates DocNo = MAX+1 per
--              DocSeries + DocYear + InvType + SubscID; refuses a duplicate.
--    Update  : numbering identity (series / year / type / no / amd) is fixed —
--              only the body is rewritten. Refused once locked.
--    Delete  : soft-deletes the header and every child row (frees the OA
--              balance the lines were holding). Refused once locked.
--    Lock / Unlock.
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_ProformaMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_ProformaMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ProformaMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @InvType        VARCHAR(20),
    @AmdNo          INT,
    @AmdNotes       VARCHAR(100),
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @VendorNoShip   INT,
    @FromAccount    INT,
    @VendorCode     VARCHAR(20),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @StateName      VARCHAR(50),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(250),
    @TaxType        VARCHAR(20),
    @BasicValue     NUMERIC(16, 2),
    @DiscountAmt    NUMERIC(16, 2),
    @ExemptValue    NUMERIC(16, 2),
    @TaxableValue   NUMERIC(16, 2),
    @PnFAmt         NUMERIC(16, 2),
    @PnFGSTP        NUMERIC(6, 2),
    @PnFGSTAmt      NUMERIC(16, 2),
    @FreightAmt     NUMERIC(14, 2),
    @FrightGSTP     NUMERIC(6, 2),
    @FrightGSTAmt   NUMERIC(16, 2),
    @IGSTAmt        NUMERIC(16, 2),
    @CGSTAmt        NUMERIC(16, 2),
    @SGSTAmt        NUMERIC(16, 2),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16, 2),
    @OtherP         NUMERIC(6, 2),
    @RoundOff       NUMERIC(6, 2),
    @NetValue       NUMERIC(18, 2),
    @AdvanceP       NUMERIC(12, 0),
    @Advance        NUMERIC(18, 2),
    @Grossweight    NUMERIC(16, 2),
    @Netweight      NUMERIC(16, 2),
    @TxnCurrency    VARCHAR(30),
    @CurrencyRate   NUMERIC(12, 2),
    @CurrencyAmount NUMERIC(14, 2),
    @ProjectNo      INT,
    @SalesRep       VARCHAR(50),
    @TxnGSTIN       VARCHAR(20),
    @IECodeNo       VARCHAR(50),
    @Declaration    VARCHAR(500),
    @BankNo         INT,
    @InvTag         CHAR(10),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MCount INT = 0;
    BEGIN TRY
        BEGIN TRANSACTION;

        SET @DocSeries = LTRIM(RTRIM(@DocSeries));
        SET @InvType   = LTRIM(RTRIM(@InvType));
        SET @AmdNo     = ISNULL(@AmdNo, 0);

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            IF (@AmdNo = 0)
            BEGIN
                -- The client's DocNo is a preview; the number is allocated here,
                -- under the transaction, per series + year + invoice type.
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.ProformaMst WITH (UPDLOCK, HOLDLOCK)
                WHERE DocSeries = @DocSeries
                  AND DocYear   = @DocYear
                  AND InvType   = @InvType
                  AND SubscID   = @SubscID;
            END

            SELECT @MCount = COUNT(MTransNo)
            FROM dbo.ProformaMst
            WHERE DocSeries    = @DocSeries
              AND DocYear      = @DocYear
              AND InvType      = @InvType
              AND DocNo        = @DocNo
              AND ISNULL(AmdNo, 0) = @AmdNo
              AND DeleteStatus = 'N'
              AND SubscID      = @SubscID;

            IF (@MCount > 0)
            BEGIN
                SET @ID = 0;
                SET @Message = 'This Proforma Invoice No already exists.';
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ProformaMst
                (
                    DocNo, DocDate, DocYear, DocSeries, InvType, AmdNo, AmdNotes,
                    TargetDocNo, TargetTransNo, TargetAmdNo, TargetDocYear,
                    VendorNo, FromAccount, VendorNoShip,
                    VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
                    RefNo, RefDate, DocNotes, TaxType,
                    BasicValue, DiscountAmt, ExemptValue, TaxableValue,
                    PnFAmt, PnFGSTP, PnFGSTAmt, FreightAmt, FrightGSTP, FrightGSTAmt,
                    IGSTAmt, CGSTAmt, SGSTAmt,
                    OtherCharges, OtherAmt, OtherP, RoundOff, NetValue,
                    AdvanceP, Advance, Grossweight, Netweight,
                    TxnCurrency, CurrencyRate, CurrencyAmount,
                    ProjectNo, SalesRep, TxnGSTIN, IECodeNo, Declaration, BankNo, InvTag,
                    DocStatus, SystemIP, CreatedBy, CreatedOn,
                    DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                )
                VALUES
                (
                    @DocNo, @DocDate, @DocYear, @DocSeries, @InvType, @AmdNo, LTRIM(RTRIM(@AmdNotes)),
                    @TargetDocNo, @TargetTransNo, @TargetAmdNo, @TargetDocYear,
                    @VendorNo, @FromAccount, @VendorNoShip,
                    LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)), LTRIM(RTRIM(@Address)),
                    LTRIM(RTRIM(@GSTIN)), LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@PhoneNo)),
                    LTRIM(RTRIM(@EmailID)),
                    LTRIM(RTRIM(@RefNo)), @RefDate, LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@TaxType)),
                    @BasicValue, @DiscountAmt, @ExemptValue, @TaxableValue,
                    @PnFAmt, @PnFGSTP, @PnFGSTAmt, @FreightAmt, @FrightGSTP, @FrightGSTAmt,
                    @IGSTAmt, @CGSTAmt, @SGSTAmt,
                    LTRIM(RTRIM(@OtherCharges)), @OtherAmt, @OtherP, @RoundOff, @NetValue,
                    @AdvanceP, @Advance, @Grossweight, @Netweight,
                    LTRIM(RTRIM(@TxnCurrency)), @CurrencyRate, @CurrencyAmount,
                    @ProjectNo, LTRIM(RTRIM(@SalesRep)), LTRIM(RTRIM(@TxnGSTIN)),
                    LTRIM(RTRIM(@IECodeNo)), LTRIM(RTRIM(@Declaration)), @BankNo, @InvTag,
                    'N',  -- DocStatus: open
                    @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                    'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                );

                SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Proforma Invoice saved successfully.';
            END
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ProformaMst
                       WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                         AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'This Proforma Invoice is locked and cannot be edited.';
            END
            ELSE
            BEGIN
                -- DocSeries / DocYear / InvType / DocNo / AmdNo are the document's
                -- identity and are deliberately not rewritten here.
                UPDATE dbo.ProformaMst
                SET DocDate        = @DocDate,
                    AmdNotes       = LTRIM(RTRIM(@AmdNotes)),
                    TargetDocNo    = @TargetDocNo,
                    TargetTransNo  = @TargetTransNo,
                    TargetAmdNo    = @TargetAmdNo,
                    TargetDocYear  = @TargetDocYear,
                    VendorNo       = @VendorNo,
                    FromAccount    = @FromAccount,
                    VendorNoShip   = @VendorNoShip,
                    VendorCode     = LTRIM(RTRIM(@VendorCode)),
                    VendorName     = LTRIM(RTRIM(@VendorName)),
                    Address        = LTRIM(RTRIM(@Address)),
                    GSTIN          = LTRIM(RTRIM(@GSTIN)),
                    StateName      = LTRIM(RTRIM(@StateName)),
                    PhoneNo        = LTRIM(RTRIM(@PhoneNo)),
                    EmailID        = LTRIM(RTRIM(@EmailID)),
                    RefNo          = LTRIM(RTRIM(@RefNo)),
                    RefDate        = @RefDate,
                    DocNotes       = LTRIM(RTRIM(@DocNotes)),
                    TaxType        = LTRIM(RTRIM(@TaxType)),
                    BasicValue     = @BasicValue,
                    DiscountAmt    = @DiscountAmt,
                    ExemptValue    = @ExemptValue,
                    TaxableValue   = @TaxableValue,
                    PnFAmt         = @PnFAmt,
                    PnFGSTP        = @PnFGSTP,
                    PnFGSTAmt      = @PnFGSTAmt,
                    FreightAmt     = @FreightAmt,
                    FrightGSTP     = @FrightGSTP,
                    FrightGSTAmt   = @FrightGSTAmt,
                    IGSTAmt        = @IGSTAmt,
                    CGSTAmt        = @CGSTAmt,
                    SGSTAmt        = @SGSTAmt,
                    OtherCharges   = LTRIM(RTRIM(@OtherCharges)),
                    OtherAmt       = @OtherAmt,
                    OtherP         = @OtherP,
                    RoundOff       = @RoundOff,
                    NetValue       = @NetValue,
                    AdvanceP       = @AdvanceP,
                    Advance        = @Advance,
                    Grossweight    = @Grossweight,
                    Netweight      = @Netweight,
                    TxnCurrency    = LTRIM(RTRIM(@TxnCurrency)),
                    CurrencyRate   = @CurrencyRate,
                    CurrencyAmount = @CurrencyAmount,
                    ProjectNo      = @ProjectNo,
                    SalesRep       = LTRIM(RTRIM(@SalesRep)),
                    TxnGSTIN       = LTRIM(RTRIM(@TxnGSTIN)),
                    IECodeNo       = LTRIM(RTRIM(@IECodeNo)),
                    Declaration    = LTRIM(RTRIM(@Declaration)),
                    BankNo         = @BankNo,
                    InvTag         = @InvTag,
                    SystemIP       = @SystemIP,
                    ModifiedBy     = @ModifiedBy,
                    ModifiedOn     = ISNULL(@ModifiedOn, GETDATE()),
                    LockCounter    = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Proforma Invoice updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- DELETE (header + every child row)
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ProformaMst
                       WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                         AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'This Proforma Invoice is locked and cannot be deleted.';
            END
            ELSE
            BEGIN
                UPDATE dbo.ProformaMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    UPDATE dbo.ProformaDtl
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    UPDATE dbo.ProformaTerms
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    UPDATE dbo.ProformaExtra
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    SET @ID = @MTransNo; SET @Message = 'Proforma Invoice deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK / UNLOCK
        --=========================================
        ELSE IF (@Status IN ('Lock', 'Unlock'))
        BEGIN
            UPDATE dbo.ProformaMst
            SET LockStatus  = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = CASE WHEN @Status = 'Lock'
                                    THEN 'Proforma Invoice locked successfully.'
                                    ELSE 'Proforma Invoice unlocked successfully.' END;
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 6. SP_ProformaDtl
--    Insert allocates SlNo = MAX+1 per MTransNo (so rows must be posted one at
--    a time). Insert and Update both refuse a quantity above the OA line's
--    balance: OADtl.Qty minus what other live proformas already bill against
--    the same OADtl.TransSlNo (LinkNo).
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_ProformaDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_ProformaDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ProformaDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           INT,
    @PartNo         INT,
    @PartCode       VARCHAR(50),
    @Description    VARCHAR(250),
    @ItemRemarks    VARCHAR(250),
    @CustPartNo     VARCHAR(30),
    @HSNCode        VARCHAR(10),
    @Qty            NUMERIC(12, 2),
    @Unit           VARCHAR(20),
    @Rate           NUMERIC(14, 2),
    @BasicValue     NUMERIC(16, 2),
    @DiscP          NUMERIC(6, 2),
    @DiscAmt        NUMERIC(12, 2),
    @PnFP           NUMERIC(6, 2),
    @PnFAmt         NUMERIC(14, 2),
    @PnFGSTAmt      NUMERIC(16, 2),
    @FreightRate    NUMERIC(16, 2),
    @FreightAmt     NUMERIC(16, 2),
    @FreightGSTAmt  NUMERIC(14, 2),
    @TaxableValue   NUMERIC(14, 2),
    @GSTP           NUMERIC(6, 2),
    @IGSTAmt        NUMERIC(14, 2),
    @CGSTAmt        NUMERIC(14, 2),
    @SGSTAmt        NUMERIC(14, 2),
    @TotalValue     NUMERIC(16, 2),
    @DTargetDocNo   INT,
    @DTargetDocYear INT,
    @Packing        VARCHAR(50),
    @BoxNo          INT,
    @NetWeight      NUMERIC(16, 2),
    @ChassisNo      VARCHAR(30),
    @EngineNo       VARCHAR(30),
    @RegdNo         VARCHAR(30),
    @LinkNo         INT,
    @DeleveryDate   DATE,
    @Store          VARCHAR(50),
    @VendorNo       INT,
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @OAQty NUMERIC(18, 4), @Billed NUMERIC(18, 4);
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            -- The header must be live and editable.
            IF NOT EXISTS (SELECT 1 FROM dbo.ProformaMst
                           WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                             AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Proforma Invoice not found.';
                GOTO Done;
            END
            IF EXISTS (SELECT 1 FROM dbo.ProformaMst
                       WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                         AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'This Proforma Invoice is locked and cannot be edited.';
                GOTO Done;
            END

            -- Over-billing guard against the OA line.
            IF (@LinkNo IS NOT NULL AND @LinkNo > 0)
            BEGIN
                SELECT @OAQty = Qty
                FROM dbo.OADtl
                WHERE TransSlNo = @LinkNo AND DeleteStatus = 'N';

                IF (@OAQty IS NULL)
                BEGIN
                    SET @ID = 0; SET @Message = 'Order acceptance line not found.';
                    GOTO Done;
                END

                SELECT @Billed = ISNULL(SUM(d.Qty), 0)
                FROM dbo.ProformaDtl d WITH (UPDLOCK, HOLDLOCK)
                JOIN dbo.ProformaMst m ON m.MTransNo = d.MTransNo
                WHERE d.LinkNo       = @LinkNo
                  AND d.DeleteStatus = 'N'
                  AND m.DeleteStatus = 'N'
                  AND d.SubscID      = @SubscID
                  AND d.TransSlNo   <> ISNULL(@TransSlNo, 0);

                IF (@Qty > @OAQty - @Billed)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Inv Qty ' + CONVERT(VARCHAR(30), @Qty)
                                 + ' is more than the OA balance '
                                 + CONVERT(VARCHAR(30), CONVERT(NUMERIC(18, 2), @OAQty - @Billed)) + '.';
                    GOTO Done;
                END
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.ProformaDtl
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.ProformaDtl
            (
                MTransNo, SlNo, PartNo, PartCode, Description, ItemRemarks, CustPartNo, HSNCode,
                Qty, Unit, Rate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt, PnFGSTAmt,
                FreightRate, FreightAmt, FreightGSTAmt, TaxableValue, GSTP,
                IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
                DTargetDocNo, DTargetDocYear, Packing, BoxNo, NetWeight,
                ChassisNo, EngineNo, RegdNo, LinkNo, DeleveryDate, Store, VendorNo,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @MTransNo, @SlNo, @PartNo, LTRIM(RTRIM(@PartCode)), LTRIM(RTRIM(@Description)),
                LTRIM(RTRIM(@ItemRemarks)), LTRIM(RTRIM(@CustPartNo)), LTRIM(RTRIM(@HSNCode)),
                @Qty, LTRIM(RTRIM(@Unit)), @Rate, @BasicValue, @DiscP, @DiscAmt, @PnFP, @PnFAmt, @PnFGSTAmt,
                @FreightRate, @FreightAmt, @FreightGSTAmt, @TaxableValue, @GSTP,
                @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
                @DTargetDocNo, @DTargetDocYear, LTRIM(RTRIM(@Packing)), @BoxNo, @NetWeight,
                LTRIM(RTRIM(@ChassisNo)), LTRIM(RTRIM(@EngineNo)), LTRIM(RTRIM(@RegdNo)),
                @LinkNo, @DeleveryDate, LTRIM(RTRIM(@Store)), @VendorNo,
                @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
            );

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.ProformaDtl
            SET PartNo         = @PartNo,
                PartCode       = LTRIM(RTRIM(@PartCode)),
                Description    = LTRIM(RTRIM(@Description)),
                ItemRemarks    = LTRIM(RTRIM(@ItemRemarks)),
                CustPartNo     = LTRIM(RTRIM(@CustPartNo)),
                HSNCode        = LTRIM(RTRIM(@HSNCode)),
                Qty            = @Qty,
                Unit           = LTRIM(RTRIM(@Unit)),
                Rate           = @Rate,
                BasicValue     = @BasicValue,
                DiscP          = @DiscP,
                DiscAmt        = @DiscAmt,
                PnFP           = @PnFP,
                PnFAmt         = @PnFAmt,
                PnFGSTAmt      = @PnFGSTAmt,
                FreightRate    = @FreightRate,
                FreightAmt     = @FreightAmt,
                FreightGSTAmt  = @FreightGSTAmt,
                TaxableValue   = @TaxableValue,
                GSTP           = @GSTP,
                IGSTAmt        = @IGSTAmt,
                CGSTAmt        = @CGSTAmt,
                SGSTAmt        = @SGSTAmt,
                TotalValue     = @TotalValue,
                DTargetDocNo   = @DTargetDocNo,
                DTargetDocYear = @DTargetDocYear,
                Packing        = LTRIM(RTRIM(@Packing)),
                BoxNo          = @BoxNo,
                NetWeight      = @NetWeight,
                ChassisNo      = LTRIM(RTRIM(@ChassisNo)),
                EngineNo       = LTRIM(RTRIM(@EngineNo)),
                RegdNo         = LTRIM(RTRIM(@RegdNo)),
                LinkNo         = @LinkNo,
                DeleveryDate   = @DeleveryDate,
                Store          = LTRIM(RTRIM(@Store)),
                VendorNo       = @VendorNo,
                SystemIP       = @SystemIP,
                ModifiedBy     = @ModifiedBy,
                ModifiedOn     = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus     = ISNULL(@LockStatus, LockStatus),
                LockCounter    = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.ProformaDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.ProformaDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active lines found.'; END
        END
        ELSE IF (@Status NOT IN ('Insert', 'Update'))
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        Done:
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 7. SP_ProformaTerms  (same contract as SP_QtnTerms)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_ProformaTerms', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_ProformaTerms AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ProformaTerms
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6, 2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.ProformaTerms
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.ProformaTerms
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Term saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.ProformaTerms
            SET Title       = LTRIM(RTRIM(@Title)),
                Description = LTRIM(RTRIM(@Description)),
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus  = ISNULL(@LockStatus, LockStatus),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.ProformaTerms
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.ProformaTerms
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All terms deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active terms found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 8. SP_ProformaExtra  (same contract as SP_ProformaTerms)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_ProformaExtra', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_ProformaExtra AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ProformaExtra
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6, 2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.ProformaExtra
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.ProformaExtra
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Extra saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.ProformaExtra
            SET Title       = LTRIM(RTRIM(@Title)),
                Description = LTRIM(RTRIM(@Description)),
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus  = ISNULL(@LockStatus, LockStatus),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Extra updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.ProformaExtra
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Extra deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.ProformaExtra
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All extras deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active extras found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

PRINT '19_ProformaInvoice.sql applied.';
GO
GO


PRINT '>>> 20_PackingSlip.sql';
GO

/*
================================================================================
 20_PackingSlip.sql
 Packing Slip (Sales) — tables + write procedures.

   PackingSlipMst   header: one slip packs lines of ONE Order Acceptance
                    (TargetTransNo / TargetDocYear / TargetDocNo / TargetAmdNo
                    and OAYear / OANo / OADate all describe that OA).
   PackingSlipDtl   one row per OA line per case. PackingSlipDtl.OAID is
                    OADtl.TransSlNo; the same OAID may appear on several rows
                    of one slip (one OA line split across cases), but only
                    once per CaseNo.

 No amendments on this document. SP_PackingSlipMst allocates DocNo itself
 (MAX(DocNo)+1 per DocSeries + DocYear + SubscID); SP_PackingSlipDtl
 allocates SlNo itself (MAX(SlNo)+1 per MTransNo) — post child rows one at a
 time, never in parallel.

 Idempotent: safe to run repeatedly.
================================================================================
*/
USE EMSSoft;
GO

--------------------------------------------------------------------------------
-- 1. PackingSlipMst
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PackingSlipMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingSlipMst
    (
        MTransNo INT IDENTITY(1,1) NOT NULL,
        DocSeries varchar(10) NOT NULL,
        DocYear INT NOT NULL,
        DocNo INT NOT NULL,
        DocDate date NOT NULL,
        TargetTransNo INT NULL,
        TargetDocYear INT NULL,
        TargetDocNo INT NULL,
        TargetAmdNo INT NULL,
        VendorNo INT NOT NULL,
        VendorCode varchar(20) NOT NULL,
        VendorName varchar(100) NOT NULL,
        Address varchar(500) NOT NULL,
        GSTIN varchar(20) NOT NULL,
        StateName varchar(50) NULL,
        PhoneNo varchar(30) NULL,
        EmailID varchar(80) NULL,
        OAYear INT NULL,
        OANo INT NULL,
        OADate date NULL,
        RefNo varchar(50) NULL,
        RefDate date NULL,
        DocNotes varchar(250) NULL,
        DocStatus char(10) NOT NULL,
        TotalCases INT NULL,
        TotalWeight numeric(18, 2) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_PackingSlipMst_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipMst_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipMst_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_PackingSlipMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_PackingSlipMst UNIQUE (DocSeries, DocYear, DocNo, DeleteStatus, SubscID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_DocNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_DocNo
        ON dbo.PackingSlipMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, DocNotes)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_DocDate' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_DocDate
        ON dbo.PackingSlipMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_VendorNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_VendorNo
        ON dbo.PackingSlipMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_TargetTransNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_TargetTransNo
        ON dbo.PackingSlipMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

--------------------------------------------------------------------------------
-- 2. PackingSlipDtl  (child of PackingSlipMst : MTransNo -> PackingSlipMst.MTransNo)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PackingSlipDtl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingSlipDtl
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        SlNo numeric(12, 2) NULL,
        MTransNo INT NOT NULL,
        OAID INT NOT NULL,
        CaseNo INT NOT NULL,
        PartNo INT NOT NULL,
        PartCode varchar(30) NULL,
        PartDescription varchar(250) NULL,
        MOC varchar(250) NULL,
        OARemarks varchar(250) NULL,
        ItemRemarks varchar(500) NULL,
        HSNCode varchar(10) NULL,
        Qty numeric(12, 4) NOT NULL,
        Unit varchar(20) NULL,
        CaseSize varchar(50) NULL,
        NetWeight numeric(12, 2) NULL,
        GrossWeight numeric(12, 2) NULL,
        ValveSlNo varchar(200) NULL,
        BoxType varchar(50) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_PackingSlipDtl_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipDtl_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipDtl_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_PackingSlipDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipDtl'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipDtl_MTransNo
        ON dbo.PackingSlipDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, PartDescription, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipDtl_PartNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipDtl'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipDtl_PartNo
        ON dbo.PackingSlipDtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

--------------------------------------------------------------------------------
-- 3. SP_PackingSlipMst
--    Insert / Update / Delete / Lock / Unlock.
--    DocStatus: 'N' open, 'Y' closed (invoiced) — a closed slip cannot be
--    edited or deleted. LockStatus is the approval flag the page drives
--    ('S' submitted, 'N' draft, 'Y' approved).
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_PackingSlipMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PackingSlipMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PackingSlipMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @DocSeries      VARCHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @VendorCode     VARCHAR(20),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @StateName      VARCHAR(50),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @OAYear         INT,
    @OANo           INT,
    @OADate         DATE,
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(250),
    @TotalCases     INT,
    @TotalWeight    NUMERIC(18, 2),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Closed CHAR(1) = 'N';

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
            FROM dbo.PackingSlipMst WITH (UPDLOCK, HOLDLOCK)
            WHERE DocSeries = LTRIM(RTRIM(@DocSeries))
              AND DocYear   = @DocYear
              AND SubscID   = @SubscID;

            INSERT INTO dbo.PackingSlipMst
            (
                DocSeries, DocYear, DocNo, DocDate,
                TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                VendorNo, VendorCode, VendorName, Address, GSTIN,
                StateName, PhoneNo, EmailID,
                OAYear, OANo, OADate, RefNo, RefDate,
                DocNotes, DocStatus, TotalCases, TotalWeight,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                @VendorNo, LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)),
                LTRIM(RTRIM(@Address)), LTRIM(RTRIM(@GSTIN)),
                LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@PhoneNo)), LTRIM(RTRIM(@EmailID)),
                @OAYear, @OANo, @OADate, LTRIM(RTRIM(@RefNo)), @RefDate,
                LTRIM(RTRIM(@DocNotes)), 'N', @TotalCases, @TotalWeight,
                @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Packing Slip saved successfully.';
        END

        --=========================================
        -- UPDATE  (series / year / number never change)
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @Closed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@Closed = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Packing Slip is already invoiced and cannot be changed.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipMst
                SET DocDate       = @DocDate,
                    TargetTransNo = @TargetTransNo,
                    TargetDocYear = @TargetDocYear,
                    TargetDocNo   = @TargetDocNo,
                    TargetAmdNo   = @TargetAmdNo,
                    VendorNo      = @VendorNo,
                    VendorCode    = LTRIM(RTRIM(@VendorCode)),
                    VendorName    = LTRIM(RTRIM(@VendorName)),
                    Address       = LTRIM(RTRIM(@Address)),
                    GSTIN         = LTRIM(RTRIM(@GSTIN)),
                    StateName     = LTRIM(RTRIM(@StateName)),
                    PhoneNo       = LTRIM(RTRIM(@PhoneNo)),
                    EmailID       = LTRIM(RTRIM(@EmailID)),
                    OAYear        = @OAYear,
                    OANo          = @OANo,
                    OADate        = @OADate,
                    RefNo         = LTRIM(RTRIM(@RefNo)),
                    RefDate       = @RefDate,
                    DocNotes      = LTRIM(RTRIM(@DocNotes)),
                    TotalCases    = @TotalCases,
                    TotalWeight   = @TotalWeight,
                    SystemIP      = @SystemIP,
                    ModifiedBy    = @ModifiedBy,
                    ModifiedOn    = ISNULL(@ModifiedOn, GETDATE()),
                    LockStatus    = ISNULL(@LockStatus, LockStatus),
                    LockCounter   = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Packing Slip updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- DELETE  (soft; its lines go with it)
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            SELECT @Closed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@Closed = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Packing Slip is already invoiced and cannot be deleted.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    UPDATE dbo.PackingSlipDtl
                    SET DeleteStatus = 'Y',
                        DeleteReason = @DeleteReason,
                        ModifiedBy   = @ModifiedBy,
                        ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo     = @MTransNo
                      AND SubscID      = @SubscID
                      AND DeleteStatus <> 'Y';

                    SET @ID = @MTransNo; SET @Message = 'Packing Slip deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK / UNLOCK
        --=========================================
        ELSE IF (@Status = 'Lock' OR @Status = 'Unlock')
        BEGIN
            UPDATE dbo.PackingSlipMst
            SET LockStatus  = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = CASE WHEN @Status = 'Lock'
                                    THEN 'Packing Slip locked successfully.'
                                    ELSE 'Packing Slip unlocked successfully.' END;
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 4. SP_PackingSlipDtl
--    Insert / Update / Delete / DeleteAll.
--    Insert + Update refuse (ID 0) when:
--      - the header is missing, deleted or invoiced;
--      - the OA line (OAID = OADtl.TransSlNo) is not on the slip's OA;
--      - the same OAID is already packed into the same CaseNo on this slip;
--      - the qty would take the OA line past its ordered qty, counting every
--        other non-deleted row on every non-deleted slip.
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_PackingSlipDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PackingSlipDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PackingSlipDtl
    @ID              INT             OUTPUT,
    @TransSlNo       INT,
    @MTransNo        INT,
    @SlNo            NUMERIC(12, 2),
    @OAID            INT,
    @CaseNo          INT,
    @PartNo          INT,
    @PartCode        VARCHAR(30),
    @PartDescription VARCHAR(250),
    @MOC             VARCHAR(250),
    @OARemarks       VARCHAR(250),
    @ItemRemarks     VARCHAR(500),
    @HSNCode         VARCHAR(10),
    @Qty             NUMERIC(12, 4),
    @Unit            VARCHAR(20),
    @CaseSize        VARCHAR(50),
    @NetWeight       NUMERIC(12, 2),
    @GrossWeight     NUMERIC(12, 2),
    @ValveSlNo       VARCHAR(200),
    @BoxType         VARCHAR(50),
    @SystemIP        VARCHAR(20),
    @CreatedBy       INT,
    @CreatedOn       DATETIME2,
    @ModifiedBy      INT,
    @ModifiedOn      DATETIME2,
    @DeleteReason    VARCHAR(250),
    @LockStatus      CHAR(1),
    @SubscID         INT,
    @Status          VARCHAR(20),
    @Message         NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert' OR @Status = 'Update')
        BEGIN
            DECLARE @OATransNo INT, @HdrClosed CHAR(1),
                    @OAQty NUMERIC(12, 4), @Packed NUMERIC(14, 4),
                    @Self INT = CASE WHEN @Status = 'Update' THEN @TransSlNo ELSE 0 END;

            SELECT @OATransNo = TargetTransNo,
                   @HdrClosed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SELECT @OAQty = Qty
            FROM dbo.OADtl
            WHERE TransSlNo = @OAID AND MTransNo = @OATransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            SELECT @Packed = ISNULL(SUM(d.Qty), 0)
            FROM dbo.PackingSlipDtl d WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.PackingSlipMst m ON m.MTransNo = d.MTransNo
            WHERE d.OAID = @OAID
              AND d.SubscID = @SubscID
              AND d.DeleteStatus = 'N'
              AND m.DeleteStatus = 'N'
              AND d.TransSlNo <> @Self;

            IF (@HdrClosed IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Packing Slip not found.'; END
            ELSE IF (@HdrClosed = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'Packing Slip is already invoiced and cannot be changed.'; END
            ELSE IF (@OAQty IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' is not on this order acceptance.'; END
            ELSE IF EXISTS (SELECT 1 FROM dbo.PackingSlipDtl
                            WHERE MTransNo = @MTransNo AND OAID = @OAID AND CaseNo = @CaseNo
                              AND DeleteStatus = 'N' AND TransSlNo <> @Self)
            BEGIN SET @ID = 0; SET @Message = 'OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' is already packed in case ' + CONVERT(VARCHAR(12), @CaseNo) + '.'; END
            ELSE IF (@Packed + @Qty > @OAQty)
            BEGIN
                SET @ID = 0;
                SET @Message = 'Qty for OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' exceeds the balance ('
                             + CONVERT(VARCHAR(20), CONVERT(NUMERIC(14, 2), @OAQty - @Packed)) + ').';
            END
            ELSE IF (@Status = 'Insert')
            BEGIN
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                FROM dbo.PackingSlipDtl
                WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

                INSERT INTO dbo.PackingSlipDtl
                (
                    SlNo, MTransNo, OAID, CaseNo, PartNo, PartCode, PartDescription,
                    MOC, OARemarks, ItemRemarks, HSNCode, Qty, Unit, CaseSize,
                    NetWeight, GrossWeight, ValveSlNo, BoxType,
                    SystemIP, CreatedBy, CreatedOn,
                    DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                )
                VALUES
                (
                    @SlNo, @MTransNo, @OAID, @CaseNo, @PartNo,
                    LTRIM(RTRIM(@PartCode)), LTRIM(RTRIM(@PartDescription)),
                    LTRIM(RTRIM(@MOC)), LTRIM(RTRIM(@OARemarks)), LTRIM(RTRIM(@ItemRemarks)),
                    LTRIM(RTRIM(@HSNCode)), @Qty, LTRIM(RTRIM(@Unit)), LTRIM(RTRIM(@CaseSize)),
                    @NetWeight, @GrossWeight, LTRIM(RTRIM(@ValveSlNo)), LTRIM(RTRIM(@BoxType)),
                    @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                    'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                );

                SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Case line saved successfully.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipDtl
                SET OAID            = @OAID,
                    CaseNo          = @CaseNo,
                    PartNo          = @PartNo,
                    PartCode        = LTRIM(RTRIM(@PartCode)),
                    PartDescription = LTRIM(RTRIM(@PartDescription)),
                    MOC             = LTRIM(RTRIM(@MOC)),
                    OARemarks       = LTRIM(RTRIM(@OARemarks)),
                    ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                    HSNCode         = LTRIM(RTRIM(@HSNCode)),
                    Qty             = @Qty,
                    Unit            = LTRIM(RTRIM(@Unit)),
                    CaseSize        = LTRIM(RTRIM(@CaseSize)),
                    NetWeight       = @NetWeight,
                    GrossWeight     = @GrossWeight,
                    ValveSlNo       = LTRIM(RTRIM(@ValveSlNo)),
                    BoxType         = LTRIM(RTRIM(@BoxType)),
                    SystemIP        = @SystemIP,
                    ModifiedBy      = @ModifiedBy,
                    ModifiedOn      = ISNULL(@ModifiedOn, GETDATE()),
                    LockStatus      = ISNULL(@LockStatus, LockStatus),
                    LockCounter     = ISNULL(LockCounter, 0) + 1
                WHERE TransSlNo    = @TransSlNo
                  AND MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @TransSlNo; SET @Message = 'Case line updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PackingSlipDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Case line deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PackingSlipDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All case lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active case lines found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
GO


PRINT '>>> 21_SalesInvoice.sql';
GO

/* =============================================================================
   21_SalesInvoice.sql
   Sales (Tax) Invoice — STxnMst / STxnDtl / StxnTerms / STxnExtra, the
   TransportMaster lookup, and their write procedures.

   Tables are copied from "Purchas, Store, Sales And Production Tables.txt"
   (items 72, 101-104) with ONE deliberate deviation:
     * UQ_STxnMst also carries InvType. The invoice numbering is split by
       invoice type (Domestic / Export each keep their own DocNo sequence,
       exactly as the legacy GetMaxNo did), so Domestic #1 and Export #1 in
       the same series + year must be able to coexist.
   STxnAcc (accounting distribution) is intentionally NOT created here —
   invoice-to-ledger posting is out of scope for this page.

   Every SP_* uses create-stub-then-ALTER because master carries stray SP_*
   procedures that would otherwise shadow an unqualified name.
   Idempotent: safe to re-run.
============================================================================= */
USE EMSSoft;
GO

/* ─────────────────────────────────────────────────────────────────────────────
   72. TRANSPORTMASTER
───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.TransportMaster', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TransportMaster
    (
        MTransNo INT IDENTITY(1,1) NOT NULL,
        TransportName varchar(100) NULL,
        TransportID varchar(50) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_TransportMaster_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_TransportMaster_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_TransportMaster_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_TransportMaster PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_TransportMaster UNIQUE (TransportName, DeleteStatus, SubscID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TransportMaster_Key' AND object_id = OBJECT_ID(N'dbo.TransportMaster'))
    CREATE NONCLUSTERED INDEX IX_TransportMaster_Key
        ON dbo.TransportMaster (SubscID, TransportName)
        INCLUDE (TransportID)
        WHERE DeleteStatus = 'N';
GO

/* ─────────────────────────────────────────────────────────────────────────────
   101. STXNMST — sales transaction (tax invoice) header
───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.STxnMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.STxnMst
    (
        MTransNo INT IDENTITY(1,1) NOT NULL,
        DocNo INT NOT NULL,
        DocDate date NOT NULL,
        DocYear INT NOT NULL,
        DocSeries char(10) NOT NULL,
        TargetDocNo INT NULL,
        TargetTransNo INT NULL,
        TargetAmdNo INT NULL,
        TargetDocYear INT NULL,
        VendorNo INT NOT NULL,
        FromAccount INT NULL,
        VendorNoShip INT NULL,
        RefNo varchar(50) NULL,
        RefDate date NULL,
        DocNotes varchar(250) NULL,
        BasicValue numeric(16, 2) NULL,
        ExemptValue numeric(16, 2) NULL,
        TaxableValue numeric(16, 2) NULL,
        PnFAmt numeric(16, 2) NULL,
        PnFGSTP numeric(6, 2) NULL,
        PnFGSTAmt numeric(16, 2) NULL,
        FrightGSTP numeric(6, 2) NULL,
        FrightGSTAmt numeric(16, 2) NULL,
        IGSTAmt numeric(16, 2) NULL,
        CGSTAmt numeric(16, 2) NULL,
        SGSTAmt numeric(16, 2) NULL,
        OtherCharges varchar(50) NULL,
        OtherAmt numeric(16, 2) NULL,
        RoundOff numeric(6, 2) NULL,
        NetValue numeric(18, 2) NULL,
        Advance numeric(18, 2) NULL,
        Grossweight numeric(16, 2) NULL,
        Netweight numeric(16, 2) NULL,
        OtherP numeric(6, 2) NULL,
        TxnCurrency varchar(30) NULL,
        CurrencyRate numeric(12, 2) NULL,
        CurrencyAmount numeric(14, 2) NULL,
        ProjectNo INT NULL,
        oldCurrencyRate numeric(12, 2) NULL,
        oldCurrencyAmount numeric(12, 2) NULL,
        SalesRep varchar(50) NULL,
        TxnGSTIN varchar(20) NULL,
        IECodeNo varchar(50) NULL,
        Declaration varchar(500) NULL,
        BankNo INT NULL,
        InvTag char(10) NULL,
        AmdNo INT NULL,
        AmdNotes varchar(100) NULL,
        VendorCode varchar(20) NULL,
        VendorName varchar(100) NULL,
        Address varchar(500) NULL,
        GSTIN varchar(20) NULL,
        StateName varchar(50) NULL,
        PhoneNo varchar(30) NULL,
        EmailID varchar(80) NULL,
        DocStatus char(1) NULL,
        TaxType varchar(20) NULL,
        FreightAmt numeric(14, 2) NULL,
        InvType varchar(20) NULL,
        FeedBackDate date NULL,
        FeedBackNotes varchar(500) NULL,
        DiscountAmt numeric(14, 2) NULL,
        WarrantyClaim numeric(14, 2) NULL,
        WServiceNo INT NULL,
        WServiceDate date NULL,
        IRNNo varchar(500) NULL,
        ScanQRNo varchar(2000) NULL,
        ACKNo varchar(50) NULL,
        EWBillNo varchar(50) NULL,
        EWBillDt varchar(50) NULL,
        AckDt varchar(50) NULL,
        ExtraString varchar(2000) NULL,
        TransportName varchar(100) NULL,
        TransportID varchar(50) NULL,
        BankName varchar(50) NULL,
        LUTNo varchar(20) NULL,
        PrintType varchar(20) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_STxnMst_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnMst_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnMst_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_STxnMst PRIMARY KEY CLUSTERED (MTransNo),
        -- Deviation from the reference doc: InvType added (see file header).
        CONSTRAINT UQ_STxnMst UNIQUE (DocSeries, DocYear, InvType, DocNo, AmdNo, DeleteStatus, SubscID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnMst_DocNo' AND object_id = OBJECT_ID(N'dbo.STxnMst'))
    CREATE NONCLUSTERED INDEX IX_STxnMst_DocNo
        ON dbo.STxnMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnMst_DocDate' AND object_id = OBJECT_ID(N'dbo.STxnMst'))
    CREATE NONCLUSTERED INDEX IX_STxnMst_DocDate
        ON dbo.STxnMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnMst_VendorNo' AND object_id = OBJECT_ID(N'dbo.STxnMst'))
    CREATE NONCLUSTERED INDEX IX_STxnMst_VendorNo
        ON dbo.STxnMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnMst_TargetTransNo' AND object_id = OBJECT_ID(N'dbo.STxnMst'))
    CREATE NONCLUSTERED INDEX IX_STxnMst_TargetTransNo
        ON dbo.STxnMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

/* ─────────────────────────────────────────────────────────────────────────────
   102. STXNDTL — invoice lines with tax break-up
   LinkNo = the OADtl.TransSlNo the line bills.
───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.STxnDtl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.STxnDtl
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NOT NULL,
        PartNo INT NOT NULL,
        Description varchar(250) NULL,
        ItemRemarks varchar(250) NULL,
        CustPartNo varchar(30) NULL,
        HSNCode varchar(10) NULL,
        Qty numeric(12, 2) NOT NULL,
        Unit varchar(20) NULL,
        Rate numeric(14, 2) NOT NULL,
        BasicValue numeric(16, 2) NOT NULL,
        DiscP numeric(6, 2) NULL,
        DiscAmt numeric(12, 2) NULL,
        PnFP numeric(6, 2) NULL,
        PnFAmt numeric(14, 2) NULL,
        PnFGSTAmt numeric(16, 2) NULL,
        FreightAmt numeric(16, 2) NULL,
        FreightGSTAmt numeric(14, 2) NULL,
        TaxableValue numeric(14, 2) NULL,
        GSTP numeric(6, 2) NULL,
        IGSTAmt numeric(14, 2) NULL,
        CGSTAmt numeric(14, 2) NULL,
        SGSTAmt numeric(14, 2) NULL,
        TotalValue numeric(16, 2) NULL,
        FreightRate numeric(16, 2) NULL,
        DTargetDocNo INT NULL,
        DTargetDocYear INT NULL,
        Packing varchar(50) NULL,
        BoxNo INT NULL,
        NetWeight numeric(16, 2) NULL,
        ChassisNo varchar(30) NULL,
        EngineNo varchar(30) NULL,
        RegdNo varchar(30) NULL,
        LinkNo INT NULL,
        SlNo INT NULL,
        PartCode varchar(50) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_STxnDtl_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnDtl_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnDtl_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_STxnDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.STxnDtl'))
    CREATE NONCLUSTERED INDEX IX_STxnDtl_MTransNo
        ON dbo.STxnDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, Description, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnDtl_PartNo' AND object_id = OBJECT_ID(N'dbo.STxnDtl'))
    CREATE NONCLUSTERED INDEX IX_STxnDtl_PartNo
        ON dbo.STxnDtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit, Rate)
        WHERE DeleteStatus = 'N';
GO

/* ─────────────────────────────────────────────────────────────────────────────
   103. STXNTERMS
───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.StxnTerms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StxnTerms
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NULL,
        SlNo numeric(6, 2) NULL,
        Title varchar(30) NULL,
        Description varchar(250) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_StxnTerms_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_StxnTerms_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_StxnTerms_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_StxnTerms PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StxnTerms_MTransNo' AND object_id = OBJECT_ID(N'dbo.StxnTerms'))
    CREATE NONCLUSTERED INDEX IX_StxnTerms_MTransNo
        ON dbo.StxnTerms (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO

/* ─────────────────────────────────────────────────────────────────────────────
   104. STXNEXTRA — the invoice's answered extra terms (Title → Description)
───────────────────────────────────────────────────────────────────────────── */
IF OBJECT_ID(N'dbo.STxnExtra', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.STxnExtra
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        MTransNo INT NULL,
        SlNo numeric(6, 2) NULL,
        Title varchar(30) NULL,
        Description varchar(250) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_STxnExtra_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnExtra_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_STxnExtra_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_STxnExtra PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_STxnExtra_MTransNo' AND object_id = OBJECT_ID(N'dbo.STxnExtra'))
    CREATE NONCLUSTERED INDEX IX_STxnExtra_MTransNo
        ON dbo.STxnExtra (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO

/* =============================================================================
   SP_TransportMaster — Insert / Update / Delete
============================================================================= */
IF OBJECT_ID(N'dbo.SP_TransportMaster', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_TransportMaster AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_TransportMaster
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @TransportName  VARCHAR(100),
    @TransportID    VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @LockStatus     CHAR(1),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        SET @TransportName = LTRIM(RTRIM(@TransportName));
        SET @TransportID   = NULLIF(LTRIM(RTRIM(@TransportID)), '');

        IF (@Status = 'Insert')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TransportMaster
                        WHERE TransportName = @TransportName AND DeleteStatus = 'N' AND SubscID = @SubscID)
            BEGIN
                SET @ID = 0; SET @Message = 'A transporter with this name already exists.';
            END
            ELSE
            BEGIN
                INSERT INTO dbo.TransportMaster
                (TransportName, TransportID, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
                VALUES
                (@TransportName, @TransportID, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                 'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Transporter saved successfully.';
            END
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TransportMaster
                        WHERE TransportName = @TransportName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
            BEGIN
                SET @ID = 0; SET @Message = 'Another transporter already uses this name.';
            END
            ELSE
            BEGIN
                UPDATE dbo.TransportMaster
                SET TransportName = @TransportName,
                    TransportID   = @TransportID,
                    SystemIP      = @SystemIP,
                    LockStatus    = ISNULL(@LockStatus, LockStatus),
                    LockCounter   = ISNULL(LockCounter, 0) + 1,
                    ModifiedBy    = @CreatedBy,
                    ModifiedOn    = GETDATE()
                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Transporter updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            -- UQ_TransportMaster includes DeleteStatus: a second deleted row with
            -- the same name would violate it, so refuse politely instead.
            IF EXISTS (SELECT 1 FROM dbo.TransportMaster
                        WHERE TransportName = (SELECT TransportName FROM dbo.TransportMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y' AND SubscID = @SubscID)
            BEGIN
                SET @ID = 0; SET @Message = 'A deleted transporter with the same name already exists. Rename it first.';
            END
            ELSE
            BEGIN
                UPDATE dbo.TransportMaster
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @CreatedBy,
                    ModifiedOn   = GETDATE()
                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Transporter deleted successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/* =============================================================================
   SP_STxnMst — invoice header
   Insert allocates DocNo = MAX+1 per DocSeries + DocYear + InvType + SubscID
   (deleted rows included, so a number is never re-issued and UQ_STxnMst can
   never collide on a later delete). DocSeries / DocYear / InvType / DocNo are
   fixed once issued — Update leaves them alone.
   IRN / e-way columns are written only when a value is supplied, so saving
   the form never wipes what an e-invoice step stamped.
   Delete cascades the soft delete to every child table.
============================================================================= */
IF OBJECT_ID(N'dbo.SP_STxnMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_STxnMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_STxnMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @InvType        VARCHAR(20),
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @FromAccount    INT,
    @VendorNoShip   INT,
    @VendorCode     VARCHAR(20),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @StateName      VARCHAR(50),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(250),
    @TaxType        VARCHAR(20),
    @BasicValue     NUMERIC(16, 2),
    @ExemptValue    NUMERIC(16, 2),
    @TaxableValue   NUMERIC(16, 2),
    @DiscountAmt    NUMERIC(14, 2),
    @PnFAmt         NUMERIC(16, 2),
    @PnFGSTP        NUMERIC(6, 2),
    @PnFGSTAmt      NUMERIC(16, 2),
    @FrightGSTP     NUMERIC(6, 2),
    @FrightGSTAmt   NUMERIC(16, 2),
    @FreightAmt     NUMERIC(14, 2),
    @IGSTAmt        NUMERIC(16, 2),
    @CGSTAmt        NUMERIC(16, 2),
    @SGSTAmt        NUMERIC(16, 2),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16, 2),
    @OtherP         NUMERIC(6, 2),
    @RoundOff       NUMERIC(6, 2),
    @NetValue       NUMERIC(18, 2),
    @Advance        NUMERIC(18, 2),
    @Grossweight    NUMERIC(16, 2),
    @Netweight      NUMERIC(16, 2),
    @TxnCurrency    VARCHAR(30),
    @CurrencyRate   NUMERIC(12, 2),
    @CurrencyAmount NUMERIC(14, 2),
    @SalesRep       VARCHAR(50),
    @TxnGSTIN       VARCHAR(20),
    @IECodeNo       VARCHAR(50),
    @Declaration    VARCHAR(500),
    @BankNo         INT,
    @BankName       VARCHAR(50),
    @InvTag         CHAR(10),
    @AmdNo          INT,
    @AmdNotes       VARCHAR(100),
    @TransportName  VARCHAR(100),
    @TransportID    VARCHAR(50),
    @LUTNo          VARCHAR(20),
    @PrintType      VARCHAR(20),
    @IRNNo          VARCHAR(500),
    @ScanQRNo       VARCHAR(2000),
    @ACKNo          VARCHAR(50),
    @AckDt          VARCHAR(50),
    @EWBillNo       VARCHAR(50),
    @EWBillDt       VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SET @DocSeries = LTRIM(RTRIM(@DocSeries));
            SET @InvType   = LTRIM(RTRIM(@InvType));

            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
            FROM dbo.STxnMst WITH (UPDLOCK, HOLDLOCK)
            WHERE DocSeries = @DocSeries
              AND DocYear   = @DocYear
              AND InvType   = @InvType
              AND SubscID   = @SubscID;

            INSERT INTO dbo.STxnMst
            (
                DocNo, DocDate, DocYear, DocSeries, InvType,
                TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                VendorNo, FromAccount, VendorNoShip,
                VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
                RefNo, RefDate, DocNotes, DocStatus, TaxType,
                BasicValue, ExemptValue, TaxableValue, DiscountAmt,
                PnFAmt, PnFGSTP, PnFGSTAmt, FrightGSTP, FrightGSTAmt, FreightAmt,
                IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt, OtherP,
                RoundOff, NetValue, Advance, Grossweight, Netweight,
                TxnCurrency, CurrencyRate, CurrencyAmount,
                SalesRep, TxnGSTIN, IECodeNo, Declaration,
                BankNo, BankName, InvTag, AmdNo, AmdNotes,
                TransportName, TransportID, LUTNo, PrintType,
                IRNNo, ScanQRNo, ACKNo, AckDt, EWBillNo, EWBillDt,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @DocNo, @DocDate, @DocYear, @DocSeries, @InvType,
                @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                @VendorNo, @FromAccount, @VendorNoShip,
                LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)), LTRIM(RTRIM(@Address)),
                LTRIM(RTRIM(@GSTIN)), LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@PhoneNo)), LTRIM(RTRIM(@EmailID)),
                LTRIM(RTRIM(@RefNo)), @RefDate, LTRIM(RTRIM(@DocNotes)),
                'N',  -- DocStatus: open
                LTRIM(RTRIM(@TaxType)),
                @BasicValue, @ExemptValue, @TaxableValue, @DiscountAmt,
                @PnFAmt, @PnFGSTP, @PnFGSTAmt, @FrightGSTP, @FrightGSTAmt, @FreightAmt,
                @IGSTAmt, @CGSTAmt, @SGSTAmt, LTRIM(RTRIM(@OtherCharges)), @OtherAmt, @OtherP,
                @RoundOff, @NetValue, @Advance, @Grossweight, @Netweight,
                LTRIM(RTRIM(@TxnCurrency)), @CurrencyRate, @CurrencyAmount,
                LTRIM(RTRIM(@SalesRep)), LTRIM(RTRIM(@TxnGSTIN)), LTRIM(RTRIM(@IECodeNo)), LTRIM(RTRIM(@Declaration)),
                @BankNo, LTRIM(RTRIM(@BankName)), @InvTag, ISNULL(@AmdNo, 0), LTRIM(RTRIM(@AmdNotes)),
                LTRIM(RTRIM(@TransportName)), LTRIM(RTRIM(@TransportID)), LTRIM(RTRIM(@LUTNo)), LTRIM(RTRIM(@PrintType)),
                @IRNNo, @ScanQRNo, @ACKNo, @AckDt, @EWBillNo, @EWBillDt,
                @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Sales Invoice saved successfully.';
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.STxnMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                          AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'This invoice is locked and cannot be edited.';
            END
            ELSE
            BEGIN
                UPDATE dbo.STxnMst
                SET DocDate        = @DocDate,
                    TargetTransNo  = @TargetTransNo,
                    TargetDocYear  = @TargetDocYear,
                    TargetDocNo    = @TargetDocNo,
                    TargetAmdNo    = @TargetAmdNo,
                    VendorNo       = @VendorNo,
                    FromAccount    = @FromAccount,
                    VendorNoShip   = @VendorNoShip,
                    VendorCode     = LTRIM(RTRIM(@VendorCode)),
                    VendorName     = LTRIM(RTRIM(@VendorName)),
                    Address        = LTRIM(RTRIM(@Address)),
                    GSTIN          = LTRIM(RTRIM(@GSTIN)),
                    StateName      = LTRIM(RTRIM(@StateName)),
                    PhoneNo        = LTRIM(RTRIM(@PhoneNo)),
                    EmailID        = LTRIM(RTRIM(@EmailID)),
                    RefNo          = LTRIM(RTRIM(@RefNo)),
                    RefDate        = @RefDate,
                    DocNotes       = LTRIM(RTRIM(@DocNotes)),
                    TaxType        = LTRIM(RTRIM(@TaxType)),
                    BasicValue     = @BasicValue,
                    ExemptValue    = @ExemptValue,
                    TaxableValue   = @TaxableValue,
                    DiscountAmt    = @DiscountAmt,
                    PnFAmt         = @PnFAmt,
                    PnFGSTP        = @PnFGSTP,
                    PnFGSTAmt      = @PnFGSTAmt,
                    FrightGSTP     = @FrightGSTP,
                    FrightGSTAmt   = @FrightGSTAmt,
                    FreightAmt     = @FreightAmt,
                    IGSTAmt        = @IGSTAmt,
                    CGSTAmt        = @CGSTAmt,
                    SGSTAmt        = @SGSTAmt,
                    OtherCharges   = LTRIM(RTRIM(@OtherCharges)),
                    OtherAmt       = @OtherAmt,
                    OtherP         = @OtherP,
                    RoundOff       = @RoundOff,
                    NetValue       = @NetValue,
                    Advance        = @Advance,
                    Grossweight    = @Grossweight,
                    Netweight      = @Netweight,
                    TxnCurrency    = LTRIM(RTRIM(@TxnCurrency)),
                    CurrencyRate   = @CurrencyRate,
                    CurrencyAmount = @CurrencyAmount,
                    SalesRep       = LTRIM(RTRIM(@SalesRep)),
                    TxnGSTIN       = LTRIM(RTRIM(@TxnGSTIN)),
                    IECodeNo       = LTRIM(RTRIM(@IECodeNo)),
                    Declaration    = LTRIM(RTRIM(@Declaration)),
                    BankNo         = @BankNo,
                    BankName       = LTRIM(RTRIM(@BankName)),
                    InvTag         = @InvTag,
                    AmdNotes       = LTRIM(RTRIM(@AmdNotes)),
                    TransportName  = LTRIM(RTRIM(@TransportName)),
                    TransportID    = LTRIM(RTRIM(@TransportID)),
                    LUTNo          = LTRIM(RTRIM(@LUTNo)),
                    PrintType      = LTRIM(RTRIM(@PrintType)),
                    IRNNo          = ISNULL(@IRNNo,    IRNNo),
                    ScanQRNo       = ISNULL(@ScanQRNo, ScanQRNo),
                    ACKNo          = ISNULL(@ACKNo,    ACKNo),
                    AckDt          = ISNULL(@AckDt,    AckDt),
                    EWBillNo       = ISNULL(@EWBillNo, EWBillNo),
                    EWBillDt       = ISNULL(@EWBillDt, EWBillDt),
                    SystemIP       = @SystemIP,
                    ModifiedBy     = @ModifiedBy,
                    ModifiedOn     = ISNULL(@ModifiedOn, GETDATE()),
                    LockCounter    = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Sales Invoice updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- DELETE (cascades to every child table)
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.STxnMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                          AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'This invoice is locked and cannot be deleted.';
            END
            ELSE
            BEGIN
                UPDATE dbo.STxnMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    UPDATE dbo.STxnDtl
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    UPDATE dbo.StxnTerms
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    UPDATE dbo.STxnExtra
                    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                    SET @ID = @MTransNo; SET @Message = 'Sales Invoice deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK / UNLOCK
        --=========================================
        ELSE IF (@Status IN ('Lock', 'Unlock'))
        BEGIN
            UPDATE dbo.STxnMst
            SET LockStatus  = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = CASE WHEN @Status = 'Lock' THEN 'Sales Invoice locked successfully.'
                                    ELSE 'Sales Invoice unlocked successfully.' END;
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/* =============================================================================
   SP_STxnDtl — invoice lines
   Insert allocates SlNo = MAX+1 per MTransNo. Insert / Update refuse a
   quantity above what is still open on the OA line (LinkNo): OA qty minus
   everything billed against the same OA line (same OA DocSeries + DocYear +
   DocNo + SlNo, so an amended OA revision still sees earlier invoices) on
   other non-deleted invoice lines.
============================================================================= */
IF OBJECT_ID(N'dbo.SP_STxnDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_STxnDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_STxnDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           INT,
    @LinkNo         INT,
    @PartNo         INT,
    @PartCode       VARCHAR(50),
    @Description    VARCHAR(250),
    @ItemRemarks    VARCHAR(250),
    @CustPartNo     VARCHAR(30),
    @HSNCode        VARCHAR(10),
    @Qty            NUMERIC(12, 2),
    @Unit           VARCHAR(20),
    @Rate           NUMERIC(14, 2),
    @BasicValue     NUMERIC(16, 2),
    @DiscP          NUMERIC(6, 2),
    @DiscAmt        NUMERIC(12, 2),
    @PnFP           NUMERIC(6, 2),
    @PnFAmt         NUMERIC(14, 2),
    @PnFGSTAmt      NUMERIC(16, 2),
    @FreightRate    NUMERIC(16, 2),
    @FreightAmt     NUMERIC(16, 2),
    @FreightGSTAmt  NUMERIC(14, 2),
    @TaxableValue   NUMERIC(14, 2),
    @GSTP           NUMERIC(6, 2),
    @IGSTAmt        NUMERIC(14, 2),
    @CGSTAmt        NUMERIC(14, 2),
    @SGSTAmt        NUMERIC(14, 2),
    @TotalValue     NUMERIC(16, 2),
    @DTargetDocNo   INT,
    @DTargetDocYear INT,
    @Packing        VARCHAR(50),
    @BoxNo          INT,
    @NetWeight      NUMERIC(16, 2),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @OAQty NUMERIC(12, 4), @Billed NUMERIC(14, 4),
                @OASeries CHAR(10), @OAYear INT, @OANo INT, @OASlNo NUMERIC(12, 2);

        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.STxnMst
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Sales Invoice not found.';
                GOTO Done;
            END

            IF (@LinkNo IS NOT NULL AND @LinkNo > 0)
            BEGIN
                SELECT @OAQty = d.Qty, @OASeries = m.DocSeries, @OAYear = m.DocYear,
                       @OANo = m.DocNo, @OASlNo = d.SlNo
                FROM dbo.OADtl d
                JOIN dbo.OAMst m ON m.MTransNo = d.MTransNo
                WHERE d.TransSlNo = @LinkNo AND d.DeleteStatus = 'N' AND m.DeleteStatus = 'N';

                IF (@OAQty IS NULL)
                BEGIN
                    SET @ID = 0; SET @Message = 'The order acceptance line no longer exists.';
                    GOTO Done;
                END

                SELECT @Billed = ISNULL(SUM(s.Qty), 0)
                FROM dbo.STxnDtl s
                JOIN dbo.STxnMst h ON h.MTransNo = s.MTransNo
                JOIN dbo.OADtl  d ON d.TransSlNo = s.LinkNo
                JOIN dbo.OAMst  m ON m.MTransNo = d.MTransNo
                WHERE s.DeleteStatus = 'N' AND h.DeleteStatus = 'N'
                  AND h.SubscID   = @SubscID
                  AND m.DocSeries = @OASeries AND m.DocYear = @OAYear AND m.DocNo = @OANo
                  AND d.SlNo      = @OASlNo
                  AND s.TransSlNo <> ISNULL(@TransSlNo, 0);

                IF (@Qty > @OAQty - @Billed)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Inv Qty cannot exceed the OA balance quantity ('
                                   + CONVERT(VARCHAR(30), CONVERT(NUMERIC(14, 2), @OAQty - @Billed)) + ').';
                    GOTO Done;
                END
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.STxnDtl
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.STxnDtl
            (
                MTransNo, SlNo, LinkNo, PartNo, PartCode, Description, ItemRemarks, CustPartNo, HSNCode,
                Qty, Unit, Rate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt, PnFGSTAmt,
                FreightRate, FreightAmt, FreightGSTAmt, TaxableValue, GSTP,
                IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
                DTargetDocNo, DTargetDocYear, Packing, BoxNo, NetWeight,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @MTransNo, @SlNo, @LinkNo, @PartNo, LTRIM(RTRIM(@PartCode)), LTRIM(RTRIM(@Description)),
                LTRIM(RTRIM(@ItemRemarks)), LTRIM(RTRIM(@CustPartNo)), LTRIM(RTRIM(@HSNCode)),
                @Qty, LTRIM(RTRIM(@Unit)), @Rate, @BasicValue, @DiscP, @DiscAmt, @PnFP, @PnFAmt, @PnFGSTAmt,
                @FreightRate, @FreightAmt, @FreightGSTAmt, @TaxableValue, @GSTP,
                @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
                @DTargetDocNo, @DTargetDocYear, LTRIM(RTRIM(@Packing)), @BoxNo, @NetWeight,
                @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
            );

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Invoice line saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.STxnDtl
            SET LinkNo        = @LinkNo,
                PartNo        = @PartNo,
                PartCode      = LTRIM(RTRIM(@PartCode)),
                Description   = LTRIM(RTRIM(@Description)),
                ItemRemarks   = LTRIM(RTRIM(@ItemRemarks)),
                CustPartNo    = LTRIM(RTRIM(@CustPartNo)),
                HSNCode       = LTRIM(RTRIM(@HSNCode)),
                Qty           = @Qty,
                Unit          = LTRIM(RTRIM(@Unit)),
                Rate          = @Rate,
                BasicValue    = @BasicValue,
                DiscP         = @DiscP,
                DiscAmt       = @DiscAmt,
                PnFP          = @PnFP,
                PnFAmt        = @PnFAmt,
                PnFGSTAmt     = @PnFGSTAmt,
                FreightRate   = @FreightRate,
                FreightAmt    = @FreightAmt,
                FreightGSTAmt = @FreightGSTAmt,
                TaxableValue  = @TaxableValue,
                GSTP          = @GSTP,
                IGSTAmt       = @IGSTAmt,
                CGSTAmt       = @CGSTAmt,
                SGSTAmt       = @SGSTAmt,
                TotalValue    = @TotalValue,
                DTargetDocNo  = @DTargetDocNo,
                DTargetDocYear= @DTargetDocYear,
                Packing       = LTRIM(RTRIM(@Packing)),
                BoxNo         = @BoxNo,
                NetWeight     = @NetWeight,
                SystemIP      = @SystemIP,
                ModifiedBy    = @ModifiedBy,
                ModifiedOn    = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus    = ISNULL(@LockStatus, LockStatus),
                LockCounter   = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Invoice line updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.STxnDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Invoice line deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.STxnDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All invoice lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active lines found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        Done:
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/* =============================================================================
   SP_StxnTerms / SP_STxnExtra — Title + Description children (SP_QtnTerms shape)
============================================================================= */
IF OBJECT_ID(N'dbo.SP_StxnTerms', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StxnTerms AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_StxnTerms
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6, 2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.StxnTerms
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.StxnTerms
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Term saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.StxnTerms
            SET Title       = LTRIM(RTRIM(@Title)),
                Description = LTRIM(RTRIM(@Description)),
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus  = ISNULL(@LockStatus, LockStatus),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.StxnTerms
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.StxnTerms
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All terms deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active terms found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

IF OBJECT_ID(N'dbo.SP_STxnExtra', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_STxnExtra AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_STxnExtra
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6, 2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.STxnExtra
            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.STxnExtra
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Extra saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.STxnExtra
            SET Title       = LTRIM(RTRIM(@Title)),
                Description = LTRIM(RTRIM(@Description)),
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockStatus  = ISNULL(@LockStatus, LockStatus),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE TransSlNo    = @TransSlNo
              AND MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Extra updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.STxnExtra
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Extra deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.STxnExtra
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All extras deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active extras found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
GO


PRINT '>>> 23_QuotationAdditions.sql';
GO

/*==============================================================================
  23. QUOTATION ADDITIONS  -  the rules the live Quotation screen relies on

  Quotation.html has moved off its in-page mock data and onto QtnController
  (/api/Qtn/...). The tables and the six procedures already exist, so NO
  SCHEMA CHANGE is needed. This script only re-issues dbo.SP_QtnMst with the
  revision rules the old procedure left to the caller:

    INSERT (amendment, @DocAmdNo > 0)
      * The document being amended must still have a current revision
        (DocAmdStatus 'N', not deleted) - otherwise it is refused.
      * The revision number is allocated here, like DocNo on a first issue:
        MAX(DocAmdNo) + 1 over EVERY row of the document, deleted ones
        included. UQ_QtnMst includes DeleteStatus, so re-using the number of
        a deleted revision would collide the next time that one is deleted.
      * Every older revision is marked superseded (DocAmdStatus 'Y').

    UPDATE
      * Refused on a superseded revision (it is history) and on an approved
        one (LockStatus 'Y') - both change only by amendment.

    DELETE
      * Refused on a superseded revision - only the current one can go.
      * Refused once an Order Acceptance (OAMst.TargetTransNo) is raised
        against it.
      * Deleting the current amendment re-instates the revision before it,
        so the document does not vanish from the list.

  Everything else (column list, trimming, DocNo allocation, Lock / Unlock)
  is unchanged from the procedure that was in the database.

  Idempotent - safe to re-run.
==============================================================================*/
USE EMSSoft;
GO

-- Stub-then-ALTER: an unqualified SP_ name resolves against master first.
IF OBJECT_ID(N'dbo.SP_QtnMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @DocAmdNo       SMALLINT,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @VendorCode     VARCHAR(30),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @CPName         VARCHAR(80),
    @CPDesignation  VARCHAR(50),
    @CPPhone        VARCHAR(30),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @SalesExec      VARCHAR(50),
    @TargetDate     DATE,
    @RefProject     VARCHAR(100),
    @DocNotes       VARCHAR(250),
    @DocAmdNotes    VARCHAR(250),
    @TaxType        VARCHAR(20),
    @BasicValue     NUMERIC(16, 2),
    @ExemptValue    NUMERIC(16, 2),
    @TaxableValue   NUMERIC(16, 2),
    @PnFAmt         NUMERIC(16, 2),
    @FreightAmt     NUMERIC(16, 2),
    @IGSTAmt        NUMERIC(16, 2),
    @CGSTAmt        NUMERIC(16, 2),
    @SGSTAmt        NUMERIC(16, 2),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16, 2),
    @RoundOff       NUMERIC(6, 2),
    @NetValue       NUMERIC(18, 2),
    @TxnCurrency    VARCHAR(30),
    @CurrencyRate   NUMERIC(12, 4),
    @CurrencyAmount NUMERIC(14, 2),
    @StateName      VARCHAR(50),
    @FileAttach     VARCHAR(100),
    @Zone           VARCHAR(50),
    @QtnCategory    VARCHAR(50),
    @ChassisNo      VARCHAR(50),
    @EngineNo       VARCHAR(50),
    @VehicleModel   VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CurAmdStatus CHAR(1),
            @CurLock      CHAR(1),
            @CurSeries    CHAR(10),
            @CurYear      INT,
            @CurDocNo     INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SET @ID = 0;
            SET @Message = NULL;

            IF (ISNULL(@DocAmdNo, 0) = 0)
            BEGIN
                SET @DocAmdNo = 0;
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.QtnMst
                WHERE DocSeries = @DocSeries
                  AND DocYear   = @DocYear
                  AND SubscID   = @SubscID;
            END
            ELSE
            BEGIN
                -- An amendment revises a document that still has a live,
                -- current revision.
                IF NOT EXISTS (SELECT 1 FROM dbo.QtnMst
                               WHERE DocSeries    = @DocSeries
                                 AND DocYear      = @DocYear
                                 AND DocNo        = @DocNo
                                 AND SubscID      = @SubscID
                                 AND DeleteStatus = 'N'
                                 AND DocAmdStatus = 'N')
                BEGIN
                    SET @Message = 'The quotation being amended was not found, or is not its current revision.';
                END
                ELSE
                BEGIN
                    -- Allocated here, over deleted rows too (UQ_QtnMst includes DeleteStatus).
                    SELECT @DocAmdNo = ISNULL(MAX(DocAmdNo), 0) + 1
                    FROM dbo.QtnMst
                    WHERE DocSeries = @DocSeries
                      AND DocYear   = @DocYear
                      AND DocNo     = @DocNo
                      AND SubscID   = @SubscID;

                    UPDATE dbo.QtnMst
                       SET DocAmdStatus = 'Y'
                     WHERE DocSeries    = @DocSeries
                       AND DocYear      = @DocYear
                       AND DocNo        = @DocNo
                       AND SubscID      = @SubscID
                       AND DocAmdNo     < @DocAmdNo
                       AND DeleteStatus = 'N';
                END
            END

            IF (@Message IS NULL)
            BEGIN
                SELECT @MCount = COUNT(MTransNo)
                FROM dbo.QtnMst
                WHERE DocSeries    = @DocSeries
                  AND DocYear      = @DocYear
                  AND DocNo        = @DocNo
                  AND DocAmdNo     = @DocAmdNo
                  AND DeleteStatus = 'N'
                  AND SubscID      = @SubscID;

                IF (@MCount > 0)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Quotation already exists.';
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.QtnMst
                    (
                        DocSeries, DocYear, DocNo, DocDate, DocAmdNo, DocAmdStatus,
                        TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                        VendorNo, VendorCode, VendorName, Address, GSTIN,
                        PhoneNo, EmailID, CPName, CPDesignation, CPPhone,
                        RefNo, RefDate, SalesExec, TargetDate, RefProject,
                        DocNotes, DocAmdNotes, DocStatus, TaxType,
                        BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
                        IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt,
                        RoundOff, NetValue, TxnCurrency, CurrencyRate, CurrencyAmount,
                        StateName, FileAttach, Zone, QtnCategory,
                        ChassisNo, EngineNo, VehicleModel,
                        SystemIP, CreatedBy, CreatedOn,
                        DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                    )
                    VALUES
                    (
                        LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @DocAmdNo, 'N',
                        @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                        @VendorNo, LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)),
                        LTRIM(RTRIM(@Address)), LTRIM(RTRIM(ISNULL(@GSTIN, ''))),
                        LTRIM(RTRIM(@PhoneNo)), LTRIM(RTRIM(@EmailID)),
                        LTRIM(RTRIM(@CPName)), LTRIM(RTRIM(@CPDesignation)),
                        LTRIM(RTRIM(@CPPhone)), LTRIM(RTRIM(@RefNo)), @RefDate,
                        LTRIM(RTRIM(@SalesExec)), @TargetDate, LTRIM(RTRIM(@RefProject)),
                        LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@DocAmdNotes)),
                        'N',  -- DocStatus hardcoded
                        LTRIM(RTRIM(@TaxType)),
                        @BasicValue, @ExemptValue, @TaxableValue, @PnFAmt, @FreightAmt,
                        @IGSTAmt, @CGSTAmt, @SGSTAmt,
                        LTRIM(RTRIM(@OtherCharges)), @OtherAmt, @RoundOff, @NetValue,
                        LTRIM(RTRIM(@TxnCurrency)), @CurrencyRate, @CurrencyAmount,
                        LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@FileAttach)),
                        LTRIM(RTRIM(@Zone)), LTRIM(RTRIM(@QtnCategory)),
                        LTRIM(RTRIM(@ChassisNo)), LTRIM(RTRIM(@EngineNo)),
                        LTRIM(RTRIM(@VehicleModel)),
                        @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                        'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                    );

                    SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                    SET @Message = CASE WHEN @DocAmdNo > 0
                                        THEN 'Quotation amended successfully.'
                                        ELSE 'Quotation saved successfully.' END;
                END
            END
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @CurAmdStatus = DocAmdStatus, @CurLock = LockStatus
            FROM dbo.QtnMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@CurAmdStatus IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            ELSE IF (@CurAmdStatus = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'This revision has been superseded by an amendment and cannot be edited.'; END
            ELSE IF (@CurLock = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'This quotation is approved and cannot be edited. Amend it to raise a revision.'; END
            ELSE
            BEGIN
                SELECT @MCount = COUNT(MTransNo)
                FROM dbo.QtnMst
                WHERE DocSeries    = @DocSeries
                  AND DocYear      = @DocYear
                  AND DocNo        = @DocNo
                  AND DocAmdNo     = @DocAmdNo
                  AND DeleteStatus = 'N'
                  AND SubscID      = @SubscID
                  AND MTransNo     <> @MTransNo;

                IF (@MCount > 0)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Quotation already exists.';
                END
                ELSE
                BEGIN
                    UPDATE dbo.QtnMst
                    SET DocDate        = @DocDate,
                        TargetTransNo  = @TargetTransNo,
                        TargetDocYear  = @TargetDocYear,
                        TargetDocNo    = @TargetDocNo,
                        TargetAmdNo    = @TargetAmdNo,
                        VendorNo       = @VendorNo,
                        VendorCode     = LTRIM(RTRIM(@VendorCode)),
                        VendorName     = LTRIM(RTRIM(@VendorName)),
                        Address        = LTRIM(RTRIM(@Address)),
                        GSTIN          = LTRIM(RTRIM(ISNULL(@GSTIN, ''))),
                        PhoneNo        = LTRIM(RTRIM(@PhoneNo)),
                        EmailID        = LTRIM(RTRIM(@EmailID)),
                        CPName         = LTRIM(RTRIM(@CPName)),
                        CPDesignation  = LTRIM(RTRIM(@CPDesignation)),
                        CPPhone        = LTRIM(RTRIM(@CPPhone)),
                        RefNo          = LTRIM(RTRIM(@RefNo)),
                        RefDate        = @RefDate,
                        SalesExec      = LTRIM(RTRIM(@SalesExec)),
                        TargetDate     = @TargetDate,
                        RefProject     = LTRIM(RTRIM(@RefProject)),
                        DocNotes       = LTRIM(RTRIM(@DocNotes)),
                        DocAmdNotes    = LTRIM(RTRIM(@DocAmdNotes)),
                        TaxType        = LTRIM(RTRIM(@TaxType)),
                        BasicValue     = @BasicValue,
                        ExemptValue    = @ExemptValue,
                        TaxableValue   = @TaxableValue,
                        PnFAmt         = @PnFAmt,
                        FreightAmt     = @FreightAmt,
                        IGSTAmt        = @IGSTAmt,
                        CGSTAmt        = @CGSTAmt,
                        SGSTAmt        = @SGSTAmt,
                        OtherCharges   = LTRIM(RTRIM(@OtherCharges)),
                        OtherAmt       = @OtherAmt,
                        RoundOff       = @RoundOff,
                        NetValue       = @NetValue,
                        TxnCurrency    = LTRIM(RTRIM(@TxnCurrency)),
                        CurrencyRate   = @CurrencyRate,
                        CurrencyAmount = @CurrencyAmount,
                        StateName      = LTRIM(RTRIM(@StateName)),
                        FileAttach     = LTRIM(RTRIM(@FileAttach)),
                        Zone           = LTRIM(RTRIM(@Zone)),
                        QtnCategory    = LTRIM(RTRIM(@QtnCategory)),
                        ChassisNo      = LTRIM(RTRIM(@ChassisNo)),
                        EngineNo       = LTRIM(RTRIM(@EngineNo)),
                        VehicleModel   = LTRIM(RTRIM(@VehicleModel)),
                        SystemIP       = @SystemIP,
                        ModifiedBy     = @ModifiedBy,
                        ModifiedOn     = ISNULL(@ModifiedOn, GETDATE()),
                        LockStatus     = ISNULL(@LockStatus, LockStatus),
                        LockCounter    = ISNULL(LockCounter, 0) + 1
                    WHERE MTransNo     = @MTransNo
                      AND SubscID      = @SubscID
                      AND DeleteStatus = 'N';

                    IF @@ROWCOUNT > 0
                    BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation updated successfully.'; END
                    ELSE
                    BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
                END
            END
        END

        --=========================================
        -- DELETE
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            SELECT @CurAmdStatus = DocAmdStatus,
                   @CurSeries    = DocSeries,
                   @CurYear      = DocYear,
                   @CurDocNo     = DocNo
            FROM dbo.QtnMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@CurAmdStatus IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            ELSE IF (@CurAmdStatus = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'Only the current revision of a quotation can be deleted.'; END
            ELSE IF EXISTS (SELECT 1 FROM dbo.OAMst
                            WHERE TargetTransNo = @MTransNo
                              AND SubscID       = @SubscID
                              AND DeleteStatus  = 'N')
            BEGIN SET @ID = 0; SET @Message = 'An Order Acceptance has been raised against this quotation, so it cannot be deleted.'; END
            ELSE
            BEGIN
                UPDATE dbo.QtnMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    -- The revision before it becomes current again.
                    UPDATE dbo.QtnMst
                       SET DocAmdStatus = 'N'
                     WHERE MTransNo = (SELECT TOP 1 MTransNo FROM dbo.QtnMst
                                       WHERE DocSeries    = @CurSeries
                                         AND DocYear      = @CurYear
                                         AND DocNo        = @CurDocNo
                                         AND SubscID      = @SubscID
                                         AND DeleteStatus = 'N'
                                       ORDER BY DocAmdNo DESC);

                    SET @ID = @MTransNo; SET @Message = 'Quotation deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK
        --=========================================
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.QtnMst
            SET LockStatus  = 'Y',
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- UNLOCK
        --=========================================
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.QtnMst
            SET LockStatus  = 'N',
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
GO


PRINT '>>> 25_SalesMenuAndLookups.sql';
GO

/*==============================================================================
  25. SALES MENU + LOOKUP TYPES  -  what the live Sales screens need to be
      reachable and to fill their dropdowns

  The seven pages in EMSSoft/Modules/Sales/ now run on the real API
  (LeadGeneration, Enq, Qtn, OA, Proforma, PackingSlip, SalesInvoice
  controllers). Two pieces of data were missing:

    1. A 'Sales' sidebar section. The menu had no Sales rows at all, and a
       Menulist row alone is invisible without an AccessTemplate grant, so both
       are written here (same MERGE + grant pattern as 18_PurchaseOrderPage.sql).

    2. OtherMaster lookup TYPES the Sales pages read through
       GET /api/OtherMaster/GetDropdown/{masterType}/{subscID}:
           DocSeries   document series on every Sales form
           Unit        units of measure on quotation / OA lines
           Region      quotation / OA region
           BoxType     packing slip case type
           Location    lead city
       Only the types are created. The values (e.g. a series such as
       'AF 2526') are business data - add them from the Other Master screen.
       Until a DocSeries value exists, the Sales forms cannot be saved.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/Sales/'. Set it
    to wherever the deployed server serves them from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 801-807 again (the same as 701-706).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SIDEBAR SECTION  +  GRANTS

  MTransNo 801-807 are unused. SSLNo 6 puts Sales after Transactions. Rows are
  in process order: Lead -> Enquiry -> Quotation -> OA -> Proforma ->
  Packing Slip -> Invoice.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/Sales/';   -- <- check, see header

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
    (801, 'Sales', 6, 'Lead Generation',   1, 'bi-person-plus',      @Folder + 'LeadGeneration.html'),
    (802, 'Sales', 6, 'Sales Enquiry',     2, 'bi-question-circle',  @Folder + 'SalesEnquiry.html'),
    (803, 'Sales', 6, 'Quotation',         3, 'bi-file-earmark-text',@Folder + 'Quotation.html'),
    (804, 'Sales', 6, 'Order Acceptance',  4, 'bi-clipboard-check',  @Folder + 'OrderAcceptance.html'),
    (805, 'Sales', 6, 'Proforma Invoice',  5, 'bi-receipt',          @Folder + 'Proforma.html'),
    (806, 'Sales', 6, 'Packing Slip',      6, 'bi-box-seam',         @Folder + 'PackingSlip.html'),
    (807, 'Sales', 6, 'Sales Invoice',     7, 'bi-receipt-cutoff',   @Folder + 'SalesInvoice.html');

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
-- Every template that already holds grants gets full access to the new
-- modules, with Department copied from its existing rows.
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

/*------------------------------------------------------------------------------
  2. OTHER MASTER LOOKUP TYPES

  One row per subscription that already has lookup types, so each tenant can
  maintain its own values. Department follows the existing rows ('Admin').
------------------------------------------------------------------------------*/
DECLARE @Types TABLE (MasterType VARCHAR(50));
INSERT INTO @Types (MasterType)
VALUES ('DocSeries'), ('Unit'), ('Region'), ('BoxType'), ('Location');

INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, 'Admin', 'N', S.SubscID
  FROM  @Types T
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMasterList L
            WHERE L.MasterType   = T.MasterType
              AND L.SubscID      = S.SubscID
              AND L.DeleteStatus = 'N');
GO

-- What was written.
SELECT M.MTransNo, M.Section, M.ModuleName, M.MSLNo, M.ModulePage,
       (SELECT COUNT(*) FROM dbo.AccessTemplate A WHERE A.ModuleNo = M.MTransNo) AS Grants
  FROM dbo.Menulist M
 WHERE M.MTransNo BETWEEN 801 AND 807
 ORDER BY M.MSLNo;

SELECT MTransNo, MasterType, Department, SubscID
  FROM dbo.OtherMasterList
 WHERE DeleteStatus = 'N'
 ORDER BY SubscID, MTransNo;
GO
GO

PRINT 'Sales deployment complete.';
GO
