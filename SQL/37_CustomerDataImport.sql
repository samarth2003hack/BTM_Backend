/*==============================================================================
  37. CUSTOMER DATA IMPORT

  1. SP_CustomerMaster now allocates CustomerCode itself on Insert when the
     caller leaves it blank - same rule as LeotechAfricaK.dbo.SP_CustomerMaster:

         prefix = first letter of each word of the name (dots ignored),
                  first 3 of them, padded with X        "Bhuvan Info Systems" -> BIS
                                                         "Tata"                -> TXX
         code   = prefix + running number, 3 digits     BIS001, BIS002 ...

     Differences from the Leotech version, all deliberate:
       * the running number is MAX(numeric suffix) for the prefix, not the
         suffix of the alphabetically last code - "BIS1000" sorts before
         "BIS999", and Leotech's RIGHT('000'+n, 3) also wraps 1000 back to 000.
         Past 999 the number simply grows (BIS1000, column is VARCHAR(10)).
       * deleted rows still count, so a code is never handed out twice.
       * scoped to SubscID, matching the SP's own duplicate-code rule.
       * UPDLOCK/HOLDLOCK inside a transaction, so two saves at the same moment
         cannot both get BIS003.
       * punctuation other than '.' separates words ("A&B-Traders" -> ABT).
     A code the caller DOES send is still accepted (checked for duplicates).
     On Update a blank code keeps the stored one.

  2. OtherMasterList rows for 'Vendor Category' and 'Sales Executive' - the two
     Other Master types CustomerMaster.html reads Category and Sales Exec from.
     Without them the Other Master page cannot maintain those lists.

  3. Menu row 124 'Customer Data Import' under Masters + AccessTemplate grants
     (a Menulist row alone is invisible - see 25_SalesMenuAndLookups.sql).

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the page is in 'EMSSoft/Modules/Masters/'.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops row 124 - re-run this script afterwards.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SP_CustomerMaster
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_CustomerMaster', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_CustomerMaster AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_CustomerMaster
    @ID                INT            OUTPUT,
    @MTransNo          INT            = 0,
    @MCount            INT            = 0,
    @CustomerCode      VARCHAR(10)    = NULL,
    @CustomerType      VARCHAR(15)    = NULL,
    @CustomerName      VARCHAR(100)   = NULL,
    @AliasName         VARCHAR(100)   = NULL,
    @Category          VARCHAR(50)    = NULL,
    @CustomerAddress   VARCHAR(300)   = NULL,
    @PinCode           VARCHAR(30)    = NULL,
    @PinCodeLocation   VARCHAR(50)    = NULL,
    @StateName         VARCHAR(50)    = NULL,
    @StateCode         INT            = NULL,
    @PhoneNo           VARCHAR(30)    = NULL,
    @MobileNo          VARCHAR(30)    = NULL,
    @EmailID           VARCHAR(80)    = NULL,
    @Website           VARCHAR(80)    = NULL,
    @TaxType           VARCHAR(20)    = NULL,
    @GSTIN             VARCHAR(15)    = NULL,
    @PAN               VARCHAR(10)    = NULL,
    @TxnCurrency       VARCHAR(15)    = NULL,
    @SalesExec         VARCHAR(50)    = NULL,
    @CreditPeriod      INT            = NULL,
    @CreditLimit       NUMERIC(18,2)  = NULL,
    @CustomerPriceList VARCHAR(50)    = NULL,
    @VendorCode        VARCHAR(30)    = NULL,
    @Remarks           VARCHAR(500)   = NULL,
    @SystemIP          VARCHAR(20)    = NULL,
    @LockStatus        CHAR(1)        = 'N',
    @CreatedBy         INT            = 0,
    @CreatedOn         DATETIME2      = NULL,
    @DeleteReason      VARCHAR(250)   = NULL,
    @SubscID           INT            = 0,
    @Status            VARCHAR(20)    = 'Insert',
    @Message           NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        /*-------------------------------- DELETE --------------------------------*/
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 63001, 'A valid MTransNo is required to delete a customer.', 1;

            BEGIN TRANSACTION;

            UPDATE dbo.CustomerMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo
               AND SubscID  = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                ROLLBACK TRANSACTION;
                THROW 63002, 'Customer record not found.', 1;
            END

            /* Children go with the parent. */
            UPDATE dbo.CustomerContacts
               SET DeleteStatus = 'Y', DeleteReason = 'Customer deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.CustomerTerm
               SET DeleteStatus = 'Y', DeleteReason = 'Customer deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.ShippingAddress
               SET DeleteStatus = 'Y', DeleteReason = 'Customer deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            COMMIT TRANSACTION;

            SET @ID = @MTransNo;
            SET @Message = 'Customer deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        /* CustomerCode is no longer required - blank means "allocate one" on
           Insert and "keep the stored one" on Update. */
        IF @CustomerName IS NULL OR LTRIM(RTRIM(@CustomerName)) = ''
            THROW 63004, 'Customer name is required.', 1;
        IF @CreditLimit IS NOT NULL AND @CreditLimit < 0
            THROW 63005, 'Credit limit cannot be negative.', 1;
        IF @CreditPeriod IS NOT NULL AND @CreditPeriod < 0
            THROW 63006, 'Credit period cannot be negative.', 1;

        IF @CustomerType IS NULL OR LTRIM(RTRIM(@CustomerType)) = ''
            SET @CustomerType = 'CUSTOMER';
        IF @TxnCurrency IS NULL OR LTRIM(RTRIM(@TxnCurrency)) = ''
            SET @TxnCurrency = 'INR';

        SET @CustomerCode = NULLIF(LTRIM(RTRIM(@CustomerCode)), '');
        SET @CustomerName = LTRIM(RTRIM(@CustomerName));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE CustomerName = @CustomerName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63007, 'Another customer already uses this name.', 1;

            IF @CustomerCode IS NOT NULL
               AND EXISTS (SELECT 1 FROM dbo.CustomerMaster
                            WHERE CustomerCode = @CustomerCode AND DeleteStatus = 'N'
                              AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63008, 'Another customer already uses this code.', 1;

            UPDATE dbo.CustomerMaster
               SET CustomerCode      = ISNULL(@CustomerCode, CustomerCode),
                   CustomerType      = @CustomerType,
                   CustomerName      = @CustomerName,
                   AliasName         = @AliasName,
                   Category          = @Category,
                   CustomerAddress   = @CustomerAddress,
                   PinCode           = @PinCode,
                   PinCodeLocation   = @PinCodeLocation,
                   StateName         = @StateName,
                   StateCode         = @StateCode,
                   PhoneNo           = @PhoneNo,
                   MobileNo          = @MobileNo,
                   EmailID           = @EmailID,
                   Website           = @Website,
                   TaxType           = @TaxType,
                   GSTIN             = @GSTIN,
                   PAN               = @PAN,
                   TxnCurrency       = @TxnCurrency,
                   SalesExec         = @SalesExec,
                   CreditPeriod      = @CreditPeriod,
                   CreditLimit       = @CreditLimit,
                   CustomerPriceList = @CustomerPriceList,
                   VendorCode        = @VendorCode,
                   Remarks           = @Remarks,
                   SystemIP          = @SystemIP,
                   LockStatus        = ISNULL(@LockStatus, 'N'),
                   LockCounter       = @MCount,
                   ModifiedBy        = @CreatedBy,
                   ModifiedOn        = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63009, 'Customer record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Customer updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.CustomerMaster
                    WHERE CustomerName = @CustomerName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A customer with this name already exists.'; RETURN;
        END

        IF @CustomerCode IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE CustomerCode = @CustomerCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A customer with this code already exists.'; RETURN;
        END

        BEGIN TRANSACTION;

        IF @CustomerCode IS NULL
        BEGIN
            /* Step 1 - prefix: first letter of each word, 3 of them, X-padded. */
            DECLARE @Clean   VARCHAR(100) = UPPER(REPLACE(@CustomerName, '.', '')),
                    @Prefix  VARCHAR(3)   = '',
                    @Pos     INT          = 1,
                    @Ch      CHAR(1),
                    @InWord  BIT          = 0,
                    @NextNo  INT;

            WHILE @Pos <= LEN(@Clean) AND LEN(@Prefix) < 3
            BEGIN
                SET @Ch = SUBSTRING(@Clean, @Pos, 1);
                IF @Ch LIKE '[A-Z0-9]'
                BEGIN
                    IF @InWord = 0 SET @Prefix = @Prefix + @Ch;
                    SET @InWord = 1;
                END
                ELSE
                    SET @InWord = 0;
                SET @Pos = @Pos + 1;
            END

            SET @Prefix = LEFT(@Prefix + 'XXX', 3);

            /* Step 2 - next number for this prefix. Deleted rows count too, and
               the range lock holds until COMMIT so a concurrent save waits. */
            SELECT @NextNo = ISNULL(MAX(TRY_CAST(SUBSTRING(CustomerCode, 4, 7) AS INT)), 0) + 1
              FROM dbo.CustomerMaster WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID
               AND CustomerCode LIKE @Prefix + '[0-9]%';

            /* Step 3 - BIS001 ... BIS999, then BIS1000. */
            SET @CustomerCode = @Prefix
                + CASE WHEN @NextNo < 1000 THEN RIGHT('00' + CAST(@NextNo AS VARCHAR(7)), 3)
                       ELSE CAST(@NextNo AS VARCHAR(7)) END;
        END

        INSERT INTO dbo.CustomerMaster
        (
            CustomerCode, CustomerType, CustomerName, AliasName, Category, CustomerAddress,
            PinCode, PinCodeLocation, StateName, StateCode, PhoneNo, MobileNo, EmailID, Website,
            TaxType, GSTIN, PAN, TxnCurrency, SalesExec, CreditPeriod, CreditLimit,
            CustomerPriceList, VendorCode, Remarks,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @CustomerCode, @CustomerType, @CustomerName, @AliasName, @Category, @CustomerAddress,
            @PinCode, @PinCodeLocation, @StateName, @StateCode, @PhoneNo, @MobileNo, @EmailID, @Website,
            @TaxType, @GSTIN, @PAN, @TxnCurrency, @SalesExec, @CreditPeriod, @CreditLimit,
            @CustomerPriceList, @VendorCode, @Remarks,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();

        COMMIT TRANSACTION;

        SET @Message = 'Customer saved successfully. Code: ' + @CustomerCode;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  2. OTHER MASTER TYPES used by the customer Category / Sales Exec dropdowns
------------------------------------------------------------------------------*/
INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, 'Sales', 'N', S.SubscID
  FROM  (VALUES ('Vendor Category'), ('Sales Executive')) T(MasterType)
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OtherMasterList L
                    WHERE L.MasterType = T.MasterType
                      AND L.SubscID    = S.SubscID
                      AND L.DeleteStatus = 'N');
GO

/*------------------------------------------------------------------------------
  3. MENU ROW + GRANTS

  MTransNo 124 is unused (Masters runs 101-123). Placed straight after Sales
  Team at the foot of the Masters section.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/Masters/';   -- <- check, see header

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
    (124, 'Masters', 1, 'Customer Data Import', 24, 'bi-file-earmark-arrow-up', @Folder + 'CustomerDataImport.html');

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
