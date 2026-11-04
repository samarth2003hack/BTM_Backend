/*==============================================================================
  EMSSoft — 03_StoredProcedures.sql
  ------------------------------------------------------------------------------
  One save-procedure per table. All of them share the same contract so the API
  layer (EMSContext.Call*SP) can treat them uniformly:

      @ID       INT           OUTPUT   -- affected MTransNo, or 0 on failure
      @MTransNo INT                    -- 0 / omitted on Insert
      @MCount   INT                    -- client row counter, stored where useful
      ... business columns ...
      @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @SubscID
      @Status   VARCHAR(20)            -- 'Insert' | 'Update' | 'Delete'
      @Message  NVARCHAR(200) OUTPUT   -- human-readable result

  Every procedure is wrapped in TRY/CATCH: on any failure @ID is set to 0 and
  @Message carries the error text, so the controller can surface it directly.

  ------------------------------------------------------------------------------
  IMPORTANT — soft delete and the supplied UNIQUE constraints
  ------------------------------------------------------------------------------
  The natural keys are UNIQUE (Name, DeleteStatus, SubscID). Because
  DeleteStatus participates in the key, only ONE row per (name, subscription)
  can ever sit in the 'Y' (deleted) state — deleting a second record with the
  same name collides with the first. The schema is preserved exactly as
  supplied, so each Delete branch pre-checks for that collision and returns a
  clear message instead of a raw constraint error.

  To lift the limitation later, replace each UQ_* with a filtered unique index:
      CREATE UNIQUE INDEX UQ_Company ON dbo.Company (CompName, SubscID)
          WHERE DeleteStatus = 'N';
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  SP_Company
==============================================================================*/
IF OBJECT_ID('dbo.SP_Company', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_Company;
GO
CREATE PROCEDURE dbo.SP_Company
    @ID              INT           OUTPUT,
    @MTransNo        INT           = 0,
    @MCount          INT           = 0,
    @CompCode        VARCHAR(10)   = NULL,
    @CompName        VARCHAR(150)  = NULL,
    @TradingName     VARCHAR(150)  = NULL,
    @CompAddress     VARCHAR(300)  = NULL,
    @State           VARCHAR(50)   = NULL,
    @StateCode       INT           = NULL,
    @PhoneNo         VARCHAR(50)   = NULL,
    @PinCode         INT           = NULL,
    @SalesPhoneNo    VARCHAR(50)   = NULL,
    @PurchasePhoneNo VARCHAR(50)   = NULL,
    @GeneralEmail    VARCHAR(100)  = NULL,
    @SalesEmail      VARCHAR(100)  = NULL,
    @PurchaseEmail   VARCHAR(100)  = NULL,
    @Website         VARCHAR(100)  = NULL,
    @GSTIN           VARCHAR(15)   = NULL,
    @PAN             VARCHAR(10)   = NULL,
    @CIN             VARCHAR(30)   = NULL,
    @IECNO           VARCHAR(50)   = NULL,
    @FromYear        DATE          = NULL,
    @ToYear          DATE          = NULL,
    @FYear           INT           = NULL,
    @YearCode        VARCHAR(100)  = NULL,
    @TxnCurrency     VARCHAR(10)   = NULL,
    @CompDataSource  VARCHAR(300)  = NULL,
    @SystemIP        VARCHAR(20)   = NULL,
    @LockStatus      CHAR(1)       = 'N',
    @CreatedBy       INT           = 0,
    @CreatedOn       DATETIME2     = NULL,
    @DeleteReason    VARCHAR(250)  = NULL,
    @SubscID         INT           = 0,
    @Status          VARCHAR(20)   = 'Insert',
    @Message         NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2 = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        /*-------------------------------- DELETE --------------------------------*/
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60001, 'A valid MTransNo is required to delete a company.', 1;

            IF EXISTS (SELECT 1 FROM dbo.Company
                        WHERE CompName = (SELECT CompName FROM dbo.Company WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60002, 'A deleted company with the same name already exists. Rename or purge it first.', 1;

            UPDATE dbo.Company
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo
               AND SubscID  = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60003, 'Company record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Company deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        IF @CompCode IS NULL OR LTRIM(RTRIM(@CompCode)) = ''
            THROW 60004, 'Company code is required.', 1;
        IF @CompName IS NULL OR LTRIM(RTRIM(@CompName)) = ''
            THROW 60005, 'Company name is required.', 1;
        IF @TxnCurrency IS NULL OR LTRIM(RTRIM(@TxnCurrency)) = ''
            SET @TxnCurrency = 'INR';

        SET @CompCode = LTRIM(RTRIM(@CompCode));
        SET @CompName = LTRIM(RTRIM(@CompName));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.Company
                        WHERE CompName = @CompName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60006, 'Another company already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.Company
                        WHERE CompCode = @CompCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60007, 'Another company already uses this code.', 1;

            UPDATE dbo.Company
               SET CompCode        = @CompCode,
                   CompName        = @CompName,
                   TradingName     = @TradingName,
                   CompAddress     = @CompAddress,
                   State           = @State,
                   StateCode       = @StateCode,
                   PhoneNo         = @PhoneNo,
                   PinCode         = @PinCode,
                   SalesPhoneNo    = @SalesPhoneNo,
                   PurchasePhoneNo = @PurchasePhoneNo,
                   GeneralEmail    = @GeneralEmail,
                   SalesEmail      = @SalesEmail,
                   PurchaseEmail   = @PurchaseEmail,
                   Website         = @Website,
                   GSTIN           = @GSTIN,
                   PAN             = @PAN,
                   CIN             = @CIN,
                   IECNO           = @IECNO,
                   FromYear        = @FromYear,
                   ToYear          = @ToYear,
                   FYear           = @FYear,
                   YearCode        = @YearCode,
                   TxnCurrency     = @TxnCurrency,
                   CompDataSource  = @CompDataSource,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE MTransNo = @MTransNo
               AND SubscID  = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60008, 'Company record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Company updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.Company
                    WHERE CompName = @CompName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'A company with this name already exists.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.Company
                    WHERE CompCode = @CompCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'A company with this code already exists.';
            RETURN;
        END

        INSERT INTO dbo.Company
        (
            CompCode, CompName, TradingName, CompAddress, State, StateCode,
            PhoneNo, PinCode, SalesPhoneNo, PurchasePhoneNo,
            GeneralEmail, SalesEmail, PurchaseEmail, Website,
            GSTIN, PAN, CIN, IECNO,
            FromYear, ToYear, FYear, YearCode, TxnCurrency, CompDataSource,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @CompCode, @CompName, @TradingName, @CompAddress, @State, @StateCode,
            @PhoneNo, @PinCode, @SalesPhoneNo, @PurchasePhoneNo,
            @GeneralEmail, @SalesEmail, @PurchaseEmail, @Website,
            @GSTIN, @PAN, @CIN, @IECNO,
            @FromYear, @ToYear, @FYear, @YearCode, @TxnCurrency, @CompDataSource,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Company saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_BranchMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_BranchMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_BranchMaster;
GO
CREATE PROCEDURE dbo.SP_BranchMaster
    @ID            INT           OUTPUT,
    @MTransNo      INT           = 0,
    @MCount        INT           = 0,
    @BranchCode    VARCHAR(10)   = NULL,
    @BranchType    VARCHAR(15)   = NULL,
    @BranchName    VARCHAR(100)  = NULL,
    @BranchAddress VARCHAR(300)  = NULL,
    @PhoneNo       VARCHAR(30)   = NULL,
    @EmailID       VARCHAR(80)   = NULL,
    @PinCode       INT           = NULL,
    @GSTIN         VARCHAR(15)   = NULL,
    @CIN           VARCHAR(30)   = NULL,
    @Remarks       VARCHAR(250)  = NULL,
    @BranchUser    INT           = NULL,
    @SystemIP      VARCHAR(20)   = NULL,
    @LockStatus    CHAR(1)       = 'N',
    @CreatedBy     INT           = 0,
    @CreatedOn     DATETIME2     = NULL,
    @DeleteReason  VARCHAR(250)  = NULL,
    @SubscID       INT           = 0,
    @Status        VARCHAR(20)   = 'Insert',
    @Message       NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2 = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60101, 'A valid MTransNo is required to delete a branch.', 1;

            IF EXISTS (SELECT 1 FROM dbo.BranchMaster
                        WHERE BranchName = (SELECT BranchName FROM dbo.BranchMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60102, 'A deleted branch with the same name already exists. Rename or purge it first.', 1;

            UPDATE dbo.BranchMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60103, 'Branch record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Branch deleted successfully.';
            RETURN;
        END

        IF @BranchCode IS NULL OR LTRIM(RTRIM(@BranchCode)) = ''
            THROW 60104, 'Branch code is required.', 1;
        IF @BranchName IS NULL OR LTRIM(RTRIM(@BranchName)) = ''
            THROW 60105, 'Branch name is required.', 1;
        IF @BranchType IS NULL OR LTRIM(RTRIM(@BranchType)) = ''
            SET @BranchType = 'BRANCH';

        SET @BranchCode = LTRIM(RTRIM(@BranchCode));
        SET @BranchName = LTRIM(RTRIM(@BranchName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.BranchMaster
                        WHERE BranchName = @BranchName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60106, 'Another branch already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.BranchMaster
                        WHERE BranchCode = @BranchCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60107, 'Another branch already uses this code.', 1;

            UPDATE dbo.BranchMaster
               SET BranchCode    = @BranchCode,
                   BranchType    = @BranchType,
                   BranchName    = @BranchName,
                   BranchAddress = @BranchAddress,
                   PhoneNo       = @PhoneNo,
                   EmailID       = @EmailID,
                   PinCode       = @PinCode,
                   GSTIN         = @GSTIN,
                   CIN           = @CIN,
                   Remarks       = @Remarks,
                   BranchUser    = @BranchUser,
                   SystemIP      = @SystemIP,
                   LockStatus    = ISNULL(@LockStatus, 'N'),
                   LockCounter   = @MCount,
                   ModifiedBy    = @CreatedBy,
                   ModifiedOn    = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60108, 'Branch record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Branch updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.BranchMaster
                    WHERE BranchName = @BranchName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A branch with this name already exists.'; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.BranchMaster
                    WHERE BranchCode = @BranchCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A branch with this code already exists.'; RETURN;
        END

        INSERT INTO dbo.BranchMaster
        (
            BranchCode, BranchType, BranchName, BranchAddress, PhoneNo, EmailID,
            PinCode, GSTIN, CIN, Remarks, BranchUser,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @BranchCode, @BranchType, @BranchName, @BranchAddress, @PhoneNo, @EmailID,
            @PinCode, @GSTIN, @CIN, @Remarks, @BranchUser,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Branch saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_BankMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_BankMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_BankMaster;
GO
CREATE PROCEDURE dbo.SP_BankMaster
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @BankCode     VARCHAR(10)   = NULL,
    @BankType     VARCHAR(15)   = NULL,
    @BankName     VARCHAR(100)  = NULL,
    @BankID       VARCHAR(20)   = NULL,
    @IFSCCode     VARCHAR(50)   = NULL,
    @BranchName   VARCHAR(50)   = NULL,
    @BranchCode   VARCHAR(20)   = NULL,
    @Location     VARCHAR(100)  = NULL,
    @MICRCode     VARCHAR(20)   = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME      = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60201, 'A valid MTransNo is required to delete a bank.', 1;

            IF EXISTS (SELECT 1 FROM dbo.BankMaster
                        WHERE BankName = (SELECT BankName FROM dbo.BankMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60202, 'A deleted bank with the same name already exists. Rename or purge it first.', 1;

            UPDATE dbo.BankMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60203, 'Bank record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Bank deleted successfully.';
            RETURN;
        END

        IF @BankCode IS NULL OR LTRIM(RTRIM(@BankCode)) = ''
            THROW 60204, 'Bank code is required.', 1;
        IF @BankName IS NULL OR LTRIM(RTRIM(@BankName)) = ''
            THROW 60205, 'Bank name is required.', 1;
        IF @BankType IS NULL OR LTRIM(RTRIM(@BankType)) = ''
            SET @BankType = 'BANK';

        SET @BankCode = LTRIM(RTRIM(@BankCode));
        SET @BankName = LTRIM(RTRIM(@BankName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.BankMaster
                        WHERE BankName = @BankName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60206, 'Another bank already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.BankMaster
                        WHERE BankCode = @BankCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60207, 'Another bank already uses this code.', 1;

            UPDATE dbo.BankMaster
               SET BankCode    = @BankCode,
                   BankType    = @BankType,
                   BankName    = @BankName,
                   BankID      = @BankID,
                   IFSCCode    = @IFSCCode,
                   BranchName  = @BranchName,
                   BranchCode  = @BranchCode,
                   Location    = @Location,
                   MICRCode    = @MICRCode,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60208, 'Bank record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Bank updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.BankMaster
                    WHERE BankName = @BankName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A bank with this name already exists.'; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.BankMaster
                    WHERE BankCode = @BankCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A bank with this code already exists.'; RETURN;
        END

        INSERT INTO dbo.BankMaster
        (
            BankCode, BankType, BankName, BankID, IFSCCode,
            BranchName, BranchCode, Location, MICRCode,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @BankCode, @BankType, @BankName, @BankID, @IFSCCode,
            @BranchName, @BranchCode, @Location, @MICRCode,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Bank saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_CurrencyMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_CurrencyMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_CurrencyMaster;
GO
CREATE PROCEDURE dbo.SP_CurrencyMaster
    @ID            INT           OUTPUT,
    @MTransNo      INT           = 0,
    @MCount        INT           = 0,
    @Country       VARCHAR(50)   = NULL,
    @Currency      VARCHAR(50)   = NULL,
    @CurrencyCode  VARCHAR(10)   = NULL,
    @ExchangeRate  NUMERIC(12,2) = NULL,
    @EffectiveDate DATE          = NULL,
    @SystemIP      VARCHAR(20)   = NULL,
    @LockStatus    CHAR(1)       = 'N',
    @CreatedBy     INT           = 0,
    @CreatedOn     DATETIME      = NULL,
    @DeleteReason  VARCHAR(250)  = NULL,
    @SubscID       INT           = 0,
    @Status        VARCHAR(20)   = 'Insert',
    @Message       NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60301, 'A valid MTransNo is required to delete a currency.', 1;

            IF EXISTS (SELECT 1 FROM dbo.CurrencyMaster
                        WHERE Currency = (SELECT Currency FROM dbo.CurrencyMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60302, 'A deleted currency with the same name already exists. Rename or purge it first.', 1;

            UPDATE dbo.CurrencyMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60303, 'Currency record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Currency deleted successfully.';
            RETURN;
        END

        IF @Currency IS NULL OR LTRIM(RTRIM(@Currency)) = ''
            THROW 60304, 'Currency name is required.', 1;
        IF @CurrencyCode IS NULL OR LTRIM(RTRIM(@CurrencyCode)) = ''
            THROW 60305, 'Currency code is required.', 1;
        IF @ExchangeRate IS NULL OR @ExchangeRate <= 0
            THROW 60306, 'Exchange rate must be greater than zero.', 1;

        SET @Currency     = LTRIM(RTRIM(@Currency));
        SET @CurrencyCode = LTRIM(RTRIM(@CurrencyCode));
        IF @EffectiveDate IS NULL SET @EffectiveDate = CAST(@Now AS DATE);

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.CurrencyMaster
                        WHERE Currency = @Currency AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60307, 'Another currency already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.CurrencyMaster
                        WHERE CurrencyCode = @CurrencyCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60308, 'Another currency already uses this code.', 1;

            UPDATE dbo.CurrencyMaster
               SET Country       = @Country,
                   Currency      = @Currency,
                   CurrencyCode  = @CurrencyCode,
                   ExchangeRate  = @ExchangeRate,
                   EffectiveDate = @EffectiveDate,
                   SystemIP      = @SystemIP,
                   LockStatus    = ISNULL(@LockStatus, 'N'),
                   LockCounter   = @MCount,
                   ModifiedBy    = @CreatedBy,
                   ModifiedOn    = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60309, 'Currency record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Currency updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.CurrencyMaster
                    WHERE Currency = @Currency AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This currency already exists.'; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.CurrencyMaster
                    WHERE CurrencyCode = @CurrencyCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This currency code already exists.'; RETURN;
        END

        INSERT INTO dbo.CurrencyMaster
        (
            Country, Currency, CurrencyCode, ExchangeRate, EffectiveDate,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @Country, @Currency, @CurrencyCode, @ExchangeRate, @EffectiveDate,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Currency saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_StateMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_StateMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_StateMaster;
GO
CREATE PROCEDURE dbo.SP_StateMaster
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @State        VARCHAR(50)   = NULL,
    @StateCode    INT           = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME      = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60401, 'A valid MTransNo is required to delete a state.', 1;

            IF EXISTS (SELECT 1 FROM dbo.StateMaster
                        WHERE State = (SELECT State FROM dbo.StateMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60402, 'A deleted state with the same name already exists. Rename or purge it first.', 1;

            UPDATE dbo.StateMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60403, 'State record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'State deleted successfully.';
            RETURN;
        END

        IF @State IS NULL OR LTRIM(RTRIM(@State)) = ''
            THROW 60404, 'State name is required.', 1;
        IF @StateCode IS NULL
            THROW 60405, 'State code is required.', 1;

        SET @State = LTRIM(RTRIM(@State));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.StateMaster
                        WHERE State = @State AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60406, 'Another state already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.StateMaster
                        WHERE StateCode = @StateCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60407, 'Another state already uses this code.', 1;

            UPDATE dbo.StateMaster
               SET State       = @State,
                   StateCode   = @StateCode,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60408, 'State record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'State updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.StateMaster
                    WHERE State = @State AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This state already exists.'; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.StateMaster
                    WHERE StateCode = @StateCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This state code already exists.'; RETURN;
        END

        INSERT INTO dbo.StateMaster
        (
            State, StateCode, SystemIP, CreatedBy, CreatedOn,
            DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @State, @StateCode, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now),
            'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'State saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_UserMaster
  ------------------------------------------------------------------------------
  @PW arrives ALREADY ENCRYPTED from the API layer (UserMasterController encrypts
  before calling). On Update an empty @PW leaves the stored password untouched.
==============================================================================*/
IF OBJECT_ID('dbo.SP_UserMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_UserMaster;
GO
CREATE PROCEDURE dbo.SP_UserMaster
    @ID             INT           OUTPUT,
    @MTransNo       INT           = 0,
    @MCount         INT           = 0,
    @UserName       VARCHAR(100)  = NULL,
    @UserId         VARCHAR(30)   = NULL,
    @PW             VARCHAR(100)  = NULL,
    @UserType       VARCHAR(15)   = NULL,
    @Designation    VARCHAR(50)   = NULL,
    @Department     VARCHAR(50)   = NULL,
    @ContactNo      VARCHAR(20)   = NULL,
    @EmailId        VARCHAR(80)   = NULL,
    @ImageFile      VARCHAR(100)  = NULL,
    @AccessTemplate VARCHAR(50)   = NULL,
    @Branch         VARCHAR(500)  = NULL,
    @PWExpiry       INT           = NULL,
    @BadLogins      INT           = NULL,
    @HomePage       VARCHAR(50)   = NULL,
    @SystemIP       VARCHAR(20)   = NULL,
    @LockStatus     CHAR(1)       = 'N',
    @CreatedBy      INT           = 0,
    @CreatedOn      DATETIME      = NULL,
    @DeleteReason   VARCHAR(250)  = NULL,
    @SubscID        INT           = 0,
    @Status         VARCHAR(20)   = 'Insert',
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60501, 'A valid MTransNo is required to delete a user.', 1;

            IF EXISTS (SELECT 1 FROM dbo.UserMaster
                        WHERE UserId = (SELECT UserId FROM dbo.UserMaster WHERE MTransNo = @MTransNo)
                          AND DeleteStatus = 'Y'
                          AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60502, 'A deleted user with the same login ID already exists. Rename or purge it first.', 1;

            UPDATE dbo.UserMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60503, 'User record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'User deleted successfully.';
            RETURN;
        END

        IF @UserId IS NULL OR LTRIM(RTRIM(@UserId)) = ''
            THROW 60504, 'Login ID is required.', 1;
        IF @UserName IS NULL OR LTRIM(RTRIM(@UserName)) = ''
            THROW 60505, 'User name is required.', 1;
        IF @UserType IS NULL OR LTRIM(RTRIM(@UserType)) = ''
            SET @UserType = 'USER';

        SET @UserId   = LTRIM(RTRIM(@UserId));
        SET @UserName = LTRIM(RTRIM(@UserName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.UserMaster
                        WHERE UserId = @UserId AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60506, 'Another user already uses this login ID.', 1;

            UPDATE dbo.UserMaster
               SET UserName       = @UserName,
                   UserId         = @UserId,
                   PW             = CASE WHEN @PW IS NULL OR LTRIM(RTRIM(@PW)) = '' THEN PW ELSE @PW END,
                   UserType       = @UserType,
                   Designation    = @Designation,
                   Department     = @Department,
                   ContactNo      = @ContactNo,
                   EmailId        = @EmailId,
                   ImageFile      = @ImageFile,
                   AccessTemplate = @AccessTemplate,
                   Branch         = @Branch,
                   PWExpiry       = @PWExpiry,
                   BadLogins      = @BadLogins,
                   HomePage       = @HomePage,
                   SystemIP       = @SystemIP,
                   LockStatus     = ISNULL(@LockStatus, 'N'),
                   ModifiedBy     = @CreatedBy,
                   ModifiedOn     = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60507, 'User record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'User updated successfully.';
            RETURN;
        END

        IF @PW IS NULL OR LTRIM(RTRIM(@PW)) = ''
            THROW 60508, 'Password is required.', 1;

        IF EXISTS (SELECT 1 FROM dbo.UserMaster
                    WHERE UserId = @UserId AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A user with this login ID already exists.'; RETURN;
        END

        INSERT INTO dbo.UserMaster
        (
            UserName, UserId, PW, UserType, Designation, Department,
            ContactNo, EmailId, ImageFile, AccessTemplate, Branch,
            PWExpiry, BadLogins, LockCounter, HomePage,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, SubscID
        )
        VALUES
        (
            @UserName, @UserId, @PW, @UserType, @Designation, @Department,
            @ContactNo, @EmailId, @ImageFile, @AccessTemplate, @Branch,
            @PWExpiry, @BadLogins, 0, @HomePage,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'User saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_OtherMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_OtherMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_OtherMaster;
GO
CREATE PROCEDURE dbo.SP_OtherMaster
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @MasterType   VARCHAR(50)   = NULL,
    @MasterName   VARCHAR(50)   = NULL,
    @Department   VARCHAR(50)   = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME      = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60601, 'A valid MTransNo is required to delete this record.', 1;

            IF EXISTS (SELECT 1 FROM dbo.OtherMaster o
                        WHERE EXISTS (SELECT 1 FROM dbo.OtherMaster s
                                       WHERE s.MTransNo = @MTransNo
                                         AND s.MasterType = o.MasterType
                                         AND s.MasterName = o.MasterName)
                          AND o.DeleteStatus = 'Y'
                          AND ISNULL(o.SubscID, -1) = ISNULL(@SubscID, -1))
                THROW 60602, 'A deleted entry with the same type and name already exists. Rename or purge it first.', 1;

            UPDATE dbo.OtherMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60603, 'Record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Record deleted successfully.';
            RETURN;
        END

        IF @MasterType IS NULL OR LTRIM(RTRIM(@MasterType)) = ''
            THROW 60604, 'Master type is required.', 1;
        IF @MasterName IS NULL OR LTRIM(RTRIM(@MasterName)) = ''
            THROW 60605, 'Master name is required.', 1;

        SET @MasterType = LTRIM(RTRIM(@MasterType));
        SET @MasterName = LTRIM(RTRIM(@MasterName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.OtherMaster
                        WHERE MasterType = @MasterType AND MasterName = @MasterName
                          AND DeleteStatus = 'N' AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 60606, 'Another entry of this type already uses this name.', 1;

            UPDATE dbo.OtherMaster
               SET MasterType  = @MasterType,
                   MasterName  = @MasterName,
                   Department  = @Department,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 60607, 'Record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Record updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.OtherMaster
                    WHERE MasterType = @MasterType AND MasterName = @MasterName
                      AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This entry already exists.'; RETURN;
        END

        INSERT INTO dbo.OtherMaster
        (
            MasterType, MasterName, Department, SystemIP,
            CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MasterType, @MasterName, @Department, @SystemIP,
            @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Record saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_OtherMasterList  —  catalogue of master types shown by the OtherMaster page
==============================================================================*/
IF OBJECT_ID('dbo.SP_OtherMasterList', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_OtherMasterList;
GO
CREATE PROCEDURE dbo.SP_OtherMasterList
    @ID         INT           OUTPUT,
    @MTransNo   INT           = 0,
    @MasterType VARCHAR(30)   = NULL,
    @Department VARCHAR(50)   = NULL,
    @SubscID    INT           = 0,
    @Status     VARCHAR(20)   = 'Insert',
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60701, 'A valid MTransNo is required to delete this entry.', 1;

            UPDATE dbo.OtherMasterList
               SET DeleteStatus = 'Y'
             WHERE MTransNo = @MTransNo;

            IF @@ROWCOUNT = 0
                THROW 60702, 'Master list entry not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Master list entry deleted successfully.';
            RETURN;
        END

        IF @MasterType IS NULL OR LTRIM(RTRIM(@MasterType)) = ''
            THROW 60703, 'Master type is required.', 1;

        SET @MasterType = LTRIM(RTRIM(@MasterType));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            UPDATE dbo.OtherMasterList
               SET MasterType = @MasterType,
                   Department = @Department,
                   SubscID    = @SubscID
             WHERE MTransNo = @MTransNo;

            IF @@ROWCOUNT = 0
                THROW 60704, 'Master list entry not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Master list entry updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.OtherMasterList
                    WHERE MasterType = @MasterType
                      AND ISNULL(Department, '') = ISNULL(@Department, '')
                      AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1))
        BEGIN
            SET @ID = 0; SET @Message = 'This master type already exists.'; RETURN;
        END

        INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
        VALUES (@MasterType, @Department, 'N', @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Master list entry saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_Menulist
  ------------------------------------------------------------------------------
  MTransNo is the hand-assigned module number (no IDENTITY). On Insert, pass 0
  to have the next free number allocated automatically.
  Delete is a HARD delete: a menu row carries no history, and AccessTemplate
  rows pointing at the module are removed with it.
==============================================================================*/
IF OBJECT_ID('dbo.SP_Menulist', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_Menulist;
GO
CREATE PROCEDURE dbo.SP_Menulist
    @ID         INT           OUTPUT,
    @MTransNo   INT           = 0,
    @Category   VARCHAR(50)   = NULL,
    @CSLNo      INT           = NULL,
    @Section    VARCHAR(50)   = NULL,
    @SSLNo      INT           = NULL,
    @ModuleName VARCHAR(50)   = NULL,
    @MSLNo      INT           = NULL,
    @ModuleIcon VARCHAR(50)   = NULL,
    @ModulePage VARCHAR(100)  = NULL,
    @Status     VARCHAR(20)   = 'Insert',
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 60801, 'A valid MTransNo is required to delete a menu entry.', 1;

            DELETE FROM dbo.AccessTemplate WHERE ModuleNo = @MTransNo;
            DELETE FROM dbo.Menulist       WHERE MTransNo = @MTransNo;

            IF @@ROWCOUNT = 0
                THROW 60802, 'Menu entry not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Menu entry deleted successfully.';
            RETURN;
        END

        IF @ModuleName IS NULL OR LTRIM(RTRIM(@ModuleName)) = ''
            THROW 60803, 'Module name is required.', 1;

        SET @ModuleName = LTRIM(RTRIM(@ModuleName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            UPDATE dbo.Menulist
               SET Category   = @Category,
                   CSLNo      = @CSLNo,
                   Section    = @Section,
                   SSLNo      = @SSLNo,
                   ModuleName = @ModuleName,
                   MSLNo      = @MSLNo,
                   ModuleIcon = @ModuleIcon,
                   ModulePage = @ModulePage
             WHERE MTransNo = @MTransNo;

            IF @@ROWCOUNT = 0
                THROW 60804, 'Menu entry not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Menu entry updated successfully.';
            RETURN;
        END

        /* Allocate the next module number when the caller does not supply one. */
        IF @MTransNo <= 0
            SELECT @MTransNo = ISNULL(MAX(MTransNo), 0) + 1 FROM dbo.Menulist;

        IF EXISTS (SELECT 1 FROM dbo.Menulist WHERE MTransNo = @MTransNo)
        BEGIN
            SET @ID = 0; SET @Message = 'Module number ' + CAST(@MTransNo AS VARCHAR(10)) + ' is already in use.'; RETURN;
        END

        INSERT INTO dbo.Menulist
            (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
        VALUES
            (@MTransNo, @Category, @CSLNo, @Section, @SSLNo, @ModuleName, @MSLNo, @ModuleIcon, @ModulePage);

        SET @ID = @MTransNo;
        SET @Message = 'Menu entry saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_AccessTemplate
  ------------------------------------------------------------------------------
  Grants/revokes one module for one template.
      @AuthType = 'R'  ->  revoke (row removed)
      anything else    ->  grant / update the existing grant
==============================================================================*/
IF OBJECT_ID('dbo.SP_AccessTemplate', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_AccessTemplate;
GO
CREATE PROCEDURE dbo.SP_AccessTemplate
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @TemplateName VARCHAR(50)   = NULL,
    @Department   VARCHAR(50)   = NULL,
    @ModuleNo     INT           = 0,
    @AuthType     CHAR(1)       = NULL,
    @MyFavorite   CHAR(1)       = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;

    BEGIN TRY
        IF @TemplateName IS NULL OR LTRIM(RTRIM(@TemplateName)) = ''
            THROW 60901, 'Template name is required.', 1;

        SET @TemplateName = LTRIM(RTRIM(@TemplateName));

        /*---- Revoke: 'Delete' status, or the sentinel AuthType 'R' ----*/
        IF @Status = 'Delete' OR @AuthType = 'R'
        BEGIN
            DELETE FROM dbo.AccessTemplate
             WHERE TemplateName = @TemplateName
               AND ModuleNo     = @ModuleNo
               AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1);

            SET @ID = CASE WHEN @MTransNo > 0 THEN @MTransNo ELSE @ModuleNo END;
            SET @Message = 'Module access revoked.';
            RETURN;
        END

        IF @ModuleNo <= 0
            THROW 60902, 'A valid module number is required.', 1;

        DECLARE @Existing INT;

        SELECT @Existing = MTransNo
          FROM dbo.AccessTemplate
         WHERE TemplateName = @TemplateName
           AND ModuleNo     = @ModuleNo
           AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1);

        IF @Existing IS NOT NULL
        BEGIN
            UPDATE dbo.AccessTemplate
               SET Department = @Department,
                   AuthType   = @AuthType,
                   MyFavorite = ISNULL(@MyFavorite, MyFavorite)
             WHERE MTransNo = @Existing;

            SET @ID = @Existing;
            SET @Message = 'Module access updated.';
            RETURN;
        END

        INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
        VALUES (@TemplateName, @Department, @ModuleNo, @AuthType, ISNULL(@MyFavorite, 'N'), @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Module access granted.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_GeneralConfig
==============================================================================*/
IF OBJECT_ID('dbo.SP_GeneralConfig', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_GeneralConfig;
GO
CREATE PROCEDURE dbo.SP_GeneralConfig
    @ID               INT           OUTPUT,
    @MTransNo         INT           = 0,
    @MCount           INT           = 0,
    @ConfigKey        VARCHAR(100)  = NULL,
    @ConfigValue      VARCHAR(500)  = NULL,
    @ConfigDepartment VARCHAR(50)   = NULL,
    @CreatedBy        INT           = 0,
    @CreatedOn        DATETIME      = NULL,
    @SubscID          INT           = 0,
    @Status           VARCHAR(20)   = 'Insert',
    @Message          NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 61001, 'A valid MTransNo is required to delete a setting.', 1;

            DELETE FROM dbo.GeneralConfig WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 61002, 'Setting not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Setting deleted successfully.';
            RETURN;
        END

        IF @ConfigKey IS NULL OR LTRIM(RTRIM(@ConfigKey)) = ''
            THROW 61003, 'Config key is required.', 1;
        IF @ConfigValue IS NULL
            THROW 61004, 'Config value is required.', 1;

        SET @ConfigKey = LTRIM(RTRIM(@ConfigKey));

        /* Upsert on the natural key (ConfigKey, SubscID) — matches UQ_GeneralConfig. */
        DECLARE @Existing INT;

        SELECT @Existing = MTransNo
          FROM dbo.GeneralConfig
         WHERE ConfigKey = @ConfigKey
           AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1);

        IF @Existing IS NOT NULL
        BEGIN
            UPDATE dbo.GeneralConfig
               SET ConfigValue      = @ConfigValue,
                   ConfigDepartment = @ConfigDepartment
             WHERE MTransNo = @Existing;

            SET @ID = @Existing;
            SET @Message = 'Setting updated successfully.';
            RETURN;
        END

        INSERT INTO dbo.GeneralConfig (ConfigKey, ConfigValue, ConfigDepartment, CreatedBy, CreatedOn, SubscID)
        VALUES (@ConfigKey, @ConfigValue, @ConfigDepartment, @CreatedBy, ISNULL(@CreatedOn, @Now), @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Setting saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_EinvSetup  —  e-Invoice / e-Way-Bill credentials, one live row per SubscID
==============================================================================*/
IF OBJECT_ID('dbo.SP_EinvSetup', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_EinvSetup;
GO
CREATE PROCEDURE dbo.SP_EinvSetup
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @EUserID      VARCHAR(50)   = NULL,
    @EPassword    VARCHAR(50)   = NULL,
    @AppKey       VARCHAR(200)  = NULL,
    @AuthToken    VARCHAR(200)  = NULL,
    @Sek          VARCHAR(200)  = NULL,
    @TokenExp     VARCHAR(100)  = NULL,
    @EwbAppKey    VARCHAR(200)  = NULL,
    @EwbAuthToken VARCHAR(200)  = NULL,
    @EwbSEK       VARCHAR(200)  = NULL,
    @EwbTokenExp  VARCHAR(100)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 61101, 'A valid MTransNo is required to delete the e-Invoice setup.', 1;

            UPDATE dbo.EinvSetup SET DeleteStatus = 'Y'
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 61102, 'e-Invoice setup not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'e-Invoice setup deleted successfully.';
            RETURN;
        END

        /*---- Token refresh: update only the token fields, leave credentials ----*/
        IF @Status = 'Token'
        BEGIN
            IF @MTransNo <= 0
                SELECT TOP 1 @MTransNo = MTransNo FROM dbo.EinvSetup
                 WHERE SubscID = @SubscID AND DeleteStatus = 'N' ORDER BY MTransNo DESC;

            IF ISNULL(@MTransNo, 0) <= 0
                THROW 61103, 'No e-Invoice setup exists for this subscription.', 1;

            UPDATE dbo.EinvSetup
               SET AuthToken    = ISNULL(@AuthToken,    AuthToken),
                   Sek          = ISNULL(@Sek,          Sek),
                   TokenExp     = ISNULL(@TokenExp,     TokenExp),
                   EwbAuthToken = ISNULL(@EwbAuthToken, EwbAuthToken),
                   EwbSEK       = ISNULL(@EwbSEK,       EwbSEK),
                   EwbTokenExp  = ISNULL(@EwbTokenExp,  EwbTokenExp)
             WHERE MTransNo = @MTransNo;

            SET @ID = @MTransNo;
            SET @Message = 'e-Invoice tokens refreshed.';
            RETURN;
        END

        /*---- Upsert: one live credential row per subscription ----*/
        IF @MTransNo <= 0
            SELECT TOP 1 @MTransNo = MTransNo FROM dbo.EinvSetup
             WHERE SubscID = @SubscID AND DeleteStatus = 'N' ORDER BY MTransNo DESC;

        IF ISNULL(@MTransNo, 0) > 0
        BEGIN
            UPDATE dbo.EinvSetup
               SET EUserID      = @EUserID,
                   EPassword    = @EPassword,
                   AppKey       = @AppKey,
                   AuthToken    = @AuthToken,
                   Sek          = @Sek,
                   TokenExp     = @TokenExp,
                   EwbAppKey    = @EwbAppKey,
                   EwbAuthToken = @EwbAuthToken,
                   EwbSEK       = @EwbSEK,
                   EwbTokenExp  = @EwbTokenExp
             WHERE MTransNo = @MTransNo;

            SET @ID = @MTransNo;
            SET @Message = 'e-Invoice setup updated successfully.';
            RETURN;
        END

        INSERT INTO dbo.EinvSetup
            (EUserID, EPassword, AppKey, AuthToken, Sek, TokenExp,
             EwbAppKey, EwbAuthToken, EwbSEK, EwbTokenExp, DeleteStatus, SubscID)
        VALUES
            (@EUserID, @EPassword, @AppKey, @AuthToken, @Sek, @TokenExp,
             @EwbAppKey, @EwbAuthToken, @EwbSEK, @EwbTokenExp, 'N', @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'e-Invoice setup saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_EinvDtl  —  IRN / e-Way-Bill result recorded against a source transaction
==============================================================================*/
IF OBJECT_ID('dbo.SP_EinvDtl', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_EinvDtl;
GO
CREATE PROCEDURE dbo.SP_EinvDtl
    @ID        INT           OUTPUT,
    @TransSlNo INT           = 0,
    @MTransNo  INT           = 0,
    @IRNNo     VARCHAR(500)  = NULL,
    @ScanQRNo  VARCHAR(2000) = NULL,
    @ACKNo     VARCHAR(50)   = NULL,
    @EWBillNo  VARCHAR(50)   = NULL,
    @EWBillDt  VARCHAR(50)   = NULL,
    @AckDt     VARCHAR(50)   = NULL,
    @SubscID   INT           = 0,
    @Status    VARCHAR(20)   = 'Insert',
    @Message   NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 61201, 'A valid TransSlNo is required to cancel this e-Invoice detail.', 1;

            UPDATE dbo.EinvDtl SET DeleteStatus = 'Y'
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 61202, 'e-Invoice detail not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'e-Invoice detail cancelled.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 61203, 'A source transaction number (MTransNo) is required.', 1;

        /*---- Update by TransSlNo, or by the live row of the source txn ----*/
        IF @TransSlNo <= 0
            SELECT TOP 1 @TransSlNo = TransSlNo FROM dbo.EinvDtl
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N'
               AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1)
             ORDER BY TransSlNo DESC;

        IF ISNULL(@TransSlNo, 0) > 0
        BEGIN
            UPDATE dbo.EinvDtl
               SET MTransNo = @MTransNo,
                   IRNNo    = @IRNNo,
                   ScanQRNo = @ScanQRNo,
                   ACKNo    = @ACKNo,
                   EWBillNo = @EWBillNo,
                   EWBillDt = @EWBillDt,
                   AckDt    = @AckDt
             WHERE TransSlNo = @TransSlNo;

            SET @ID = @TransSlNo;
            SET @Message = 'e-Invoice detail updated successfully.';
            RETURN;
        END

        INSERT INTO dbo.EinvDtl
            (MTransNo, IRNNo, ScanQRNo, ACKNo, EWBillNo, EWBillDt, AckDt, DeleteStatus, SubscID)
        VALUES
            (@MTransNo, @IRNNo, @ScanQRNo, @ACKNo, @EWBillNo, @EWBillDt, @AckDt, 'N', @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'e-Invoice detail saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_AccessLog
      @Status = 'Insert' -> open a session row (login)
                'Logout' -> stamp logout date/time on the given row
==============================================================================*/
IF OBJECT_ID('dbo.SP_AccessLog', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_AccessLog;
GO
CREATE PROCEDURE dbo.SP_AccessLog
    @ID        INT           OUTPUT,
    @MTransNo  INT           = 0,
    @IPAddress VARCHAR(100)  = NULL,
    @LatLong   VARCHAR(100)  = NULL,
    @Location  VARCHAR(500)  = NULL,
    @LoginType VARCHAR(50)   = NULL,
    @LoginId   VARCHAR(30)   = NULL,
    @SubscID   INT           = 0,
    @Status    VARCHAR(20)   = 'Insert',
    @Message   NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Logout'
        BEGIN
            /* Fall back to the newest still-open session for this login. */
            IF @MTransNo <= 0
                SELECT TOP 1 @MTransNo = MTransNo FROM dbo.AccessLog
                 WHERE LoginId = @LoginId AND LogoutDate IS NULL
                 ORDER BY MTransNo DESC;

            IF ISNULL(@MTransNo, 0) <= 0
                THROW 61301, 'No open session found for this login.', 1;

            UPDATE dbo.AccessLog
               SET LogoutDate = CAST(@Now AS DATE),
                   LogoutTime = CONVERT(VARCHAR(8), @Now, 108)
             WHERE MTransNo = @MTransNo;

            SET @ID = @MTransNo;
            SET @Message = 'Logout recorded.';
            RETURN;
        END

        IF @LoginId IS NULL OR LTRIM(RTRIM(@LoginId)) = ''
            THROW 61302, 'Login ID is required.', 1;

        INSERT INTO dbo.AccessLog
            (IPAddress, LoginDate, LoginTime, LatLong, Location, LoginType, LoginId, SubscID)
        VALUES
            (@IPAddress, CAST(@Now AS DATE), CONVERT(VARCHAR(8), @Now, 108),
             @LatLong, @Location, @LoginType, LTRIM(RTRIM(@LoginId)), @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Login recorded.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_ErrorLog  —  append-only application error sink
==============================================================================*/
IF OBJECT_ID('dbo.SP_ErrorLog', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_ErrorLog;
GO
CREATE PROCEDURE dbo.SP_ErrorLog
    @ID         INT           OUTPUT,
    @ErrorMsg   VARCHAR(2000) = NULL,
    @StackTrace VARCHAR(2000) = NULL,
    @LoginType  VARCHAR(50)   = NULL,
    @LoginId    VARCHAR(30)   = NULL,
    @SubscID    INT           = 0,
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME = GETDATE();
    SET @ID = 0;

    BEGIN TRY
        INSERT INTO dbo.ErrorLog
            (ErrorMsg, StackTrace, ErrorDate, ErrorTime, LoginType, LoginId, SubscID)
        VALUES
            (LEFT(ISNULL(@ErrorMsg, ''), 2000), LEFT(ISNULL(@StackTrace, ''), 2000),
             CAST(@Now AS DATE), CONVERT(VARCHAR(8), @Now, 108),
             @LoginType, @LoginId, @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Error logged.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

PRINT '03_StoredProcedures.sql complete — 15 procedures ready.';
GO
