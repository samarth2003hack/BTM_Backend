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
