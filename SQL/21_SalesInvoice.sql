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
