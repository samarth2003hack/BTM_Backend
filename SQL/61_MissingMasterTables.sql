/*==============================================================================
  61. MISSING MASTER TABLES  -  Extra Terms, Material Composition, Sales Return
  ----------------------------------------------------------------------------
  Why this script exists
    The API code (models, controllers, EMSContext wrappers) already uses these
    objects, but no numbered script in this folder creates them (the database
    they came from had them). A database built only from scripts 01-60 gets
    "Invalid object name" on those pages. This script creates them, so a clean
    database works for the whole software.

  Creates (only when missing - safe to run again; existing data is never touched):
    Tables : ExtraTermsMaster, MaterialComposition, SalesReturnMst, SalesReturnDtl
    SPs    : SP_ExtraTermsMaster   (Insert / Update / Delete / Lock / Unlock)
             SP_SalesReturnMst     (Insert / Update / Delete / Draft / Submit /
                                    Approve / Reject)
             SP_SalesReturnDtl     (Insert / Update / Delete / DeleteAll)
    SP_MaterialComposition already exists in the database; only its table is added.

  Run with:  sqlcmd -I -S <server> -d <database> -i 61_MissingMasterTables.sql
  Run order: after 60.
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------ TABLES --------------------------------------*/
IF OBJECT_ID('dbo.ExtraTermsMaster', 'U') IS NULL
CREATE TABLE dbo.ExtraTermsMaster
(
    MTransNo     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExtraTermsMaster PRIMARY KEY,
    SlNo         DECIMAL(10,2)  NULL,
    ModuleName   VARCHAR(50)    NULL,
    Title        VARCHAR(30)    NULL,
    Description  VARCHAR(250)   NULL,
    ConfYN       CHAR(1)        NULL,
    SystemIP     VARCHAR(20)    NULL,
    CreatedBy    SMALLINT       NULL,
    CreatedOn    DATETIME       NULL,
    Modifiedby   SMALLINT       NULL,
    ModifiedOn   DATETIME       NULL,
    LockCounter  TINYINT        NULL,
    DeleteStatus CHAR(1)        NULL,
    LockStatus   CHAR(1)        NULL,
    SubscID      SMALLINT       NULL,
    DeleteReason VARCHAR(250)   NULL,
    TempNo       INT            NULL
);
GO

IF OBJECT_ID('dbo.MaterialComposition', 'U') IS NULL
CREATE TABLE dbo.MaterialComposition
(
    MTransNo      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MaterialComposition PRIMARY KEY,
    MaterialNo    INT            NOT NULL CONSTRAINT DF_MaterialComposition_MaterialNo DEFAULT 0,
    Material      VARCHAR(100)   NULL,
    HeatTreatment VARCHAR(100)   NULL,
    Description   VARCHAR(500)   NULL,
    ShortName     VARCHAR(50)    NULL,
    MinR          DECIMAL(18,4)  NULL,
    MaxR          DECIMAL(18,4)  NULL,
    MType         VARCHAR(50)    NULL,
    SystemIP      VARCHAR(20)    NULL,
    CreatedBy     INT            NULL,
    CreatedOn     DATETIME2      NULL,
    ModifiedBy    INT            NULL,
    ModifiedOn    DATETIME2      NULL,
    DeleteStatus  CHAR(1)        NULL,
    DeleteReason  VARCHAR(250)   NULL,
    LockStatus    CHAR(1)        NULL,
    LockCounter   INT            NULL,
    SubscID       INT            NULL
);
GO

IF OBJECT_ID('dbo.SalesReturnMst', 'U') IS NULL
CREATE TABLE dbo.SalesReturnMst
(
    MTransNo       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturnMst PRIMARY KEY,
    DocSeries      CHAR(10)       NULL,
    DocYear        INT            NULL,
    DocNo          INT            NULL,
    DocDate        DATE           NULL,
    TargetTransNo  INT            NULL,
    TargetDocYear  INT            NULL,
    TargetDocNo    INT            NULL,
    VendorNo       INT            NULL,
    VendorNoShip   INT            NULL,
    VendorName     VARCHAR(100)   NULL,
    TxnGSTIN       VARCHAR(20)    NULL,
    TaxType        VARCHAR(20)    NULL,
    RefNo          VARCHAR(30)    NULL,
    RefDate        DATE           NULL,
    InvValue       DECIMAL(16,2)  NULL,
    TaxableValue   DECIMAL(16,2)  NULL,
    TaxP           DECIMAL(6,2)   NULL,
    IGSTAmt        DECIMAL(16,2)  NULL,
    CGSTAmt        DECIMAL(16,2)  NULL,
    SGSTAmt        DECIMAL(16,2)  NULL,
    RoundOff       DECIMAL(6,2)   NULL,
    NetValue       DECIMAL(18,2)  NULL,
    TxnCurrency    VARCHAR(30)    NULL,
    CurrencyRate   DECIMAL(12,2)  NULL,
    CurrencyAmount DECIMAL(14,2)  NULL,
    DocNotes       VARCHAR(250)   NULL,
    LockStatus     CHAR(1)        NULL,   -- N=Draft S=Submitted Y=Approved R=Rejected
    LockCounter    INT            NULL,
    SystemIP       VARCHAR(20)    NULL,
    CreatedBy      INT            NULL,
    CreatedOn      DATETIME2      NULL,
    ModifiedBy     INT            NULL,
    ModifiedOn     DATETIME2      NULL,
    DeleteStatus   CHAR(1)        NULL,
    DeleteReason   VARCHAR(250)   NULL,
    SubscID        INT            NULL
);
GO

IF OBJECT_ID('dbo.SalesReturnDtl', 'U') IS NULL
CREATE TABLE dbo.SalesReturnDtl
(
    TransSlNo      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesReturnDtl PRIMARY KEY,
    MTransNo       INT            NOT NULL,
    InvTransNo     INT            NOT NULL,
    SlNo           INT            NOT NULL,
    LinkNo         INT            NULL,
    PartNo         INT            NULL,
    PartCode       VARCHAR(50)    NULL,
    Description    VARCHAR(250)   NULL,
    MocDescription VARCHAR(250)   NULL,
    Qty            DECIMAL(12,2)  NOT NULL CONSTRAINT DF_SalesReturnDtl_Qty DEFAULT 0,
    ReturnQty      DECIMAL(12,2)  NOT NULL CONSTRAINT DF_SalesReturnDtl_ReturnQty DEFAULT 0,
    Unit           VARCHAR(20)    NULL,
    Rate           DECIMAL(14,2)  NOT NULL CONSTRAINT DF_SalesReturnDtl_Rate DEFAULT 0,
    GSTP           DECIMAL(6,2)   NOT NULL CONSTRAINT DF_SalesReturnDtl_GSTP DEFAULT 0,
    TotalValue     DECIMAL(16,2)  NOT NULL CONSTRAINT DF_SalesReturnDtl_TotalValue DEFAULT 0,
    ItemRemarks    VARCHAR(250)   NULL,
    SystemIP       VARCHAR(20)    NULL,
    CreatedBy      INT            NULL,
    CreatedOn      DATETIME2      NULL,
    ModifiedBy     INT            NULL,
    ModifiedOn     DATETIME2      NULL,
    DeleteStatus   CHAR(1)        NULL,
    DeleteReason   VARCHAR(250)   NULL,
    SubscID        INT            NULL
);
GO

/*------------------------- STORED PROCEDURES --------------------------------*/
-- create-stub-then-ALTER keeps the script re-runnable
IF OBJECT_ID('dbo.SP_ExtraTermsMaster', 'P') IS NULL EXEC('CREATE PROCEDURE dbo.SP_ExtraTermsMaster AS RETURN 0');
GO
ALTER PROCEDURE dbo.SP_ExtraTermsMaster
    @ID          INT OUTPUT,
    @MTransNo    INT,
    @MCount      INT,
    @SlNo        DECIMAL(10,2),
    @ModuleName  VARCHAR(50),
    @Title       VARCHAR(30),
    @Description VARCHAR(250),
    @ConfYN      CHAR(1),
    @TempNo      INT,
    @SystemIP    VARCHAR(20),
    @CreatedBy   SMALLINT,
    @CreatedOn   DATETIME,
    @ModifiedBy  SMALLINT,
    @ModifiedOn  DATETIME,
    @DeleteReason VARCHAR(250),
    @LockStatus  CHAR(1),
    @SubscID     SMALLINT,
    @Status      VARCHAR(20),
    @Message     NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            SELECT @MCount = COUNT(MTransNo) FROM dbo.ExtraTermsMaster
            WHERE ISNULL(DeleteStatus,'N') = 'N' AND SubscID = @SubscID
              AND ModuleName = @ModuleName AND Title = @Title;

            IF @MCount = 0
            BEGIN
                IF @SlNo IS NULL
                    SELECT @SlNo = ISNULL(MAX(SlNo),0) + 1 FROM dbo.ExtraTermsMaster
                    WHERE ISNULL(DeleteStatus,'N') = 'N' AND SubscID = @SubscID AND ModuleName = @ModuleName;

                INSERT INTO dbo.ExtraTermsMaster
                    (SlNo, ModuleName, Title, Description, ConfYN, SystemIP, CreatedBy, CreatedOn,
                     LockCounter, DeleteStatus, LockStatus, SubscID, TempNo)
                VALUES
                    (@SlNo, @ModuleName, @Title, @Description, ISNULL(@ConfYN,'N'), @SystemIP,
                     @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 1, 'N', ISNULL(@LockStatus,'N'), @SubscID, @TempNo);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Record inserted successfully.';
            END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Duplicate record already exists.'; END
        END
        ELSE IF @Status = 'Update'
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ExtraTermsMaster WHERE MTransNo = @MTransNo AND LockStatus = 'Y' AND ISNULL(DeleteStatus,'N') = 'N')
            BEGIN SET @ID = 0; SET @Message = 'Record is locked. Unlock it before editing.'; END
            ELSE IF EXISTS (SELECT 1 FROM dbo.ExtraTermsMaster
                            WHERE ISNULL(DeleteStatus,'N') = 'N' AND SubscID = @SubscID
                              AND ModuleName = @ModuleName AND Title = @Title AND MTransNo <> @MTransNo)
            BEGIN SET @ID = 0; SET @Message = 'Duplicate record already exists.'; END
            ELSE
            BEGIN
                UPDATE dbo.ExtraTermsMaster
                SET SlNo = ISNULL(@SlNo, SlNo), ModuleName = @ModuleName, Title = @Title,
                    Description = @Description, ConfYN = ISNULL(@ConfYN, ConfYN), TempNo = @TempNo,
                    SystemIP = @SystemIP, Modifiedby = ISNULL(@ModifiedBy, @CreatedBy),
                    ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                    LockCounter = ISNULL(LockCounter,0) + 1
                WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
                IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = 'Record updated successfully.'; END
                ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.ExtraTermsMaster
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, Modifiedby = @ModifiedBy,
                ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = 'Record deleted successfully.'; END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF @Status IN ('Lock','Unlock')
        BEGIN
            UPDATE dbo.ExtraTermsMaster
            SET LockStatus = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                Modifiedby = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = 'Record ' + LOWER(@Status) + 'ed successfully.'; END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE BEGIN SET @ID = 0; SET @Message = 'Unknown status.'; END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

IF OBJECT_ID('dbo.SP_SalesReturnMst', 'P') IS NULL EXEC('CREATE PROCEDURE dbo.SP_SalesReturnMst AS RETURN 0');
GO
ALTER PROCEDURE dbo.SP_SalesReturnMst
    @ID             INT OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @VendorNo       INT,
    @VendorNoShip   INT,
    @VendorName     VARCHAR(100),
    @TxnGSTIN       VARCHAR(20),
    @TaxType        VARCHAR(20),
    @RefNo          VARCHAR(30),
    @RefDate        DATE,
    @InvValue       DECIMAL(16,2),
    @TaxableValue   DECIMAL(16,2),
    @TaxP           DECIMAL(6,2),
    @IGSTAmt        DECIMAL(16,2),
    @CGSTAmt        DECIMAL(16,2),
    @SGSTAmt        DECIMAL(16,2),
    @RoundOff       DECIMAL(6,2),
    @NetValue       DECIMAL(18,2),
    @TxnCurrency    VARCHAR(30),
    @CurrencyRate   DECIMAL(12,2),
    @CurrencyAmount DECIMAL(14,2),
    @DocNotes       VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            IF ISNULL(@DocNo,0) = 0
                SELECT @DocNo = ISNULL(MAX(DocNo),0) + 1 FROM dbo.SalesReturnMst
                WHERE DocYear = @DocYear AND DocSeries = @DocSeries AND SubscID = @SubscID;

            SELECT @MCount = COUNT(MTransNo) FROM dbo.SalesReturnMst
            WHERE ISNULL(DeleteStatus,'N') = 'N' AND SubscID = @SubscID
              AND DocYear = @DocYear AND DocSeries = @DocSeries AND DocNo = @DocNo;

            IF @MCount = 0
            BEGIN
                INSERT INTO dbo.SalesReturnMst
                    (DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo,
                     VendorNo, VendorNoShip, VendorName, TxnGSTIN, TaxType, RefNo, RefDate,
                     InvValue, TaxableValue, TaxP, IGSTAmt, CGSTAmt, SGSTAmt, RoundOff, NetValue,
                     TxnCurrency, CurrencyRate, CurrencyAmount, DocNotes,
                     LockStatus, LockCounter, SystemIP, CreatedBy, CreatedOn, DeleteStatus, SubscID)
                VALUES
                    (@DocSeries, @DocYear, @DocNo, @DocDate, @TargetTransNo, @TargetDocYear, @TargetDocNo,
                     @VendorNo, @VendorNoShip, @VendorName, @TxnGSTIN, @TaxType, @RefNo, @RefDate,
                     @InvValue, @TaxableValue, @TaxP, @IGSTAmt, @CGSTAmt, @SGSTAmt, @RoundOff, @NetValue,
                     @TxnCurrency, @CurrencyRate, @CurrencyAmount, @DocNotes,
                     'N', 1, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Record inserted successfully.';
            END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Duplicate record already exists.'; END
        END
        ELSE IF @Status = 'Update'
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SalesReturnMst WHERE MTransNo = @MTransNo AND LockStatus IN ('S','Y') AND ISNULL(DeleteStatus,'N') = 'N')
            BEGIN SET @ID = 0; SET @Message = 'Submitted or approved return cannot be edited. Send it back to Draft first.'; END
            ELSE
            BEGIN
                UPDATE dbo.SalesReturnMst
                SET DocDate = @DocDate, TargetTransNo = @TargetTransNo, TargetDocYear = @TargetDocYear,
                    TargetDocNo = @TargetDocNo, VendorNo = @VendorNo, VendorNoShip = @VendorNoShip,
                    VendorName = @VendorName, TxnGSTIN = @TxnGSTIN, TaxType = @TaxType,
                    RefNo = @RefNo, RefDate = @RefDate, InvValue = @InvValue, TaxableValue = @TaxableValue,
                    TaxP = @TaxP, IGSTAmt = @IGSTAmt, CGSTAmt = @CGSTAmt, SGSTAmt = @SGSTAmt,
                    RoundOff = @RoundOff, NetValue = @NetValue, TxnCurrency = @TxnCurrency,
                    CurrencyRate = @CurrencyRate, CurrencyAmount = @CurrencyAmount, DocNotes = @DocNotes,
                    SystemIP = @SystemIP, ModifiedBy = ISNULL(@ModifiedBy, @CreatedBy),
                    ModifiedOn = ISNULL(@ModifiedOn, GETDATE()), LockCounter = ISNULL(LockCounter,0) + 1
                WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
                IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = 'Record updated successfully.'; END
                ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SalesReturnMst WHERE MTransNo = @MTransNo AND LockStatus IN ('S','Y') AND ISNULL(DeleteStatus,'N') = 'N')
            BEGIN SET @ID = 0; SET @Message = 'Submitted or approved return cannot be deleted. Send it back to Draft first.'; END
            ELSE
            BEGIN
                UPDATE dbo.SalesReturnMst
                SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, ModifiedBy = @ModifiedBy,
                    ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
                IF @@ROWCOUNT > 0
                BEGIN
                    UPDATE dbo.SalesReturnDtl SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                           ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
                    SET @ID = @MTransNo; SET @Message = 'Record deleted successfully.';
                END
                ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF @Status IN ('Draft','Submit','Approve','Reject')
        BEGIN
            UPDATE dbo.SalesReturnMst
            SET LockStatus = CASE @Status WHEN 'Draft' THEN 'N' WHEN 'Submit' THEN 'S'
                                          WHEN 'Approve' THEN 'Y' ELSE 'R' END,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = 'Status changed to ' + @Status + '.'; END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE BEGIN SET @ID = 0; SET @Message = 'Unknown status.'; END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

IF OBJECT_ID('dbo.SP_SalesReturnDtl', 'P') IS NULL EXEC('CREATE PROCEDURE dbo.SP_SalesReturnDtl AS RETURN 0');
GO
ALTER PROCEDURE dbo.SP_SalesReturnDtl
    @ID             INT OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @InvTransNo     INT,
    @SlNo           INT,
    @LinkNo         INT,
    @PartNo         INT,
    @PartCode       VARCHAR(50),
    @Description    VARCHAR(250),
    @MocDescription VARCHAR(250),
    @Qty            DECIMAL(12,2),
    @ReturnQty      DECIMAL(12,2),
    @Unit           VARCHAR(20),
    @Rate           DECIMAL(14,2),
    @GSTP           DECIMAL(6,2),
    @TotalValue     DECIMAL(16,2),
    @ItemRemarks    VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status IN ('Insert','Update') AND EXISTS
           (SELECT 1 FROM dbo.SalesReturnMst WHERE MTransNo = @MTransNo AND LockStatus IN ('S','Y') AND ISNULL(DeleteStatus,'N') = 'N')
        BEGIN SET @ID = 0; SET @Message = 'Submitted or approved return cannot be edited. Send it back to Draft first.'; END
        ELSE IF @Status = 'Insert'
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SalesReturnDtl WHERE MTransNo = @MTransNo AND InvTransNo = @InvTransNo
                         AND SlNo = @SlNo AND ISNULL(DeleteStatus,'N') = 'N')
            BEGIN SET @ID = 0; SET @Message = 'This invoice line is already in the return.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.SalesReturnDtl
                    (MTransNo, InvTransNo, SlNo, LinkNo, PartNo, PartCode, Description, MocDescription,
                     Qty, ReturnQty, Unit, Rate, GSTP, TotalValue, ItemRemarks,
                     SystemIP, CreatedBy, CreatedOn, DeleteStatus, SubscID)
                VALUES
                    (@MTransNo, @InvTransNo, @SlNo, @LinkNo, @PartNo, @PartCode, @Description, @MocDescription,
                     ISNULL(@Qty,0), ISNULL(@ReturnQty,0), @Unit, ISNULL(@Rate,0), ISNULL(@GSTP,0), ISNULL(@TotalValue,0),
                     @ItemRemarks, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', @SubscID);
                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Record inserted successfully.';
            END
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.SalesReturnDtl
            SET InvTransNo = @InvTransNo, SlNo = @SlNo, LinkNo = @LinkNo, PartNo = @PartNo, PartCode = @PartCode,
                Description = @Description, MocDescription = @MocDescription, Qty = ISNULL(@Qty,0),
                ReturnQty = ISNULL(@ReturnQty,0), Unit = @Unit, Rate = ISNULL(@Rate,0), GSTP = ISNULL(@GSTP,0),
                TotalValue = ISNULL(@TotalValue,0), ItemRemarks = @ItemRemarks, SystemIP = @SystemIP,
                ModifiedBy = ISNULL(@ModifiedBy, @CreatedBy), ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            IF @@ROWCOUNT > 0 BEGIN SET @ID = @TransSlNo; SET @Message = 'Record updated successfully.'; END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.SalesReturnDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, ModifiedBy = @ModifiedBy,
                ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            IF @@ROWCOUNT > 0 BEGIN SET @ID = @TransSlNo; SET @Message = 'Record deleted successfully.'; END
            ELSE BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF @Status = 'DeleteAll'
        BEGIN
            UPDATE dbo.SalesReturnDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, ModifiedBy = @ModifiedBy,
                ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus,'N') = 'N';
            SET @ID = @MTransNo; SET @Message = 'All lines deleted.';
        END
        ELSE BEGIN SET @ID = 0; SET @Message = 'Unknown status.'; END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

PRINT '61_MissingMasterTables.sql applied.';
GO
