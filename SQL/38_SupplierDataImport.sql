/*==============================================================================
  38. SUPPLIER DATA IMPORT

  1. SP_SupplierMaster now allocates SupplierCode itself on Insert when the
     caller leaves it blank - the same rule 37_CustomerDataImport.sql gave
     SP_CustomerMaster (from LeotechAfricaK.dbo.SP_CustomerMaster):

         prefix = first letter of each word of the name (dots ignored),
                  first 3 of them, padded with X        "Bharat Steel Traders" -> BST
         code   = prefix + running number, 3 digits     BST001, BST002 ... BST1000

     The number is MAX(numeric suffix) for the prefix within the subscription,
     deleted rows included, under UPDLOCK/HOLDLOCK - see 37 for the reasons.
     Supplier and customer codes are numbered independently: a supplier and a
     customer of the same name can both be BST001.
     A code the caller DOES send is still accepted (checked for duplicates).
     On Update a blank code keeps the stored one.

  2. Menu row 125 'Supplier Data Import' under Masters + AccessTemplate grants.
     ('Vendor Category' is already in OtherMasterList - script 37.)

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * Run 37_CustomerDataImport.sql first.
  * @Folder - on the dev box the page is in 'EMSSoft/Modules/Masters/'.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops row 125 - re-run this script afterwards.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SP_SupplierMaster
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_SupplierMaster', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_SupplierMaster AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_SupplierMaster
    @ID              INT            OUTPUT,
    @MTransNo        INT            = 0,
    @MCount          INT            = 0,
    @SupplierCode    VARCHAR(10)    = NULL,
    @SupplierType    VARCHAR(15)    = NULL,
    @SupplierName    VARCHAR(100)   = NULL,
    @AliasName       VARCHAR(100)   = NULL,
    @Category        VARCHAR(50)    = NULL,
    @SupplierAddress VARCHAR(300)   = NULL,
    @StateName       VARCHAR(50)    = NULL,
    @StateCode       INT            = NULL,
    @PinCode         VARCHAR(30)    = NULL,
    @PinCodeLocation VARCHAR(50)    = NULL,
    @PhoneNo         VARCHAR(30)    = NULL,
    @MobileNo        VARCHAR(30)    = NULL,
    @EmailID         VARCHAR(80)    = NULL,
    @Website         VARCHAR(80)    = NULL,
    @TaxType         VARCHAR(20)    = NULL,
    @GSTIN           VARCHAR(15)    = NULL,
    @PAN             VARCHAR(10)    = NULL,
    @TxnCurrency     VARCHAR(15)    = NULL,
    @CreditPeriod    INT            = NULL,
    @BankName        VARCHAR(50)    = NULL,
    @BankAccountNo   VARCHAR(30)    = NULL,
    @IFSC            VARCHAR(11)    = NULL,
    @Remarks         VARCHAR(500)   = NULL,
    @SystemIP        VARCHAR(20)    = NULL,
    @LockStatus      CHAR(1)        = 'N',
    @CreatedBy       INT            = 0,
    @CreatedOn       DATETIME2      = NULL,
    @DeleteReason    VARCHAR(250)   = NULL,
    @SubscID         INT            = 0,
    @Status          VARCHAR(20)    = 'Insert',
    @Message         NVARCHAR(200)  OUTPUT
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
                THROW 63501, 'A valid MTransNo is required to delete a supplier.', 1;

            BEGIN TRANSACTION;

            UPDATE dbo.SupplierMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                ROLLBACK TRANSACTION;
                THROW 63502, 'Supplier record not found.', 1;
            END

            UPDATE dbo.SupplierContacts
               SET DeleteStatus = 'Y', DeleteReason = 'Supplier deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.SupplierTerm
               SET DeleteStatus = 'Y', DeleteReason = 'Supplier deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            /* The vendor's rate history goes too - it prices nothing now. */
            UPDATE dbo.PriceList
               SET DeleteStatus = 'Y', DeleteReason = 'Supplier deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE VendorNo = @MTransNo AND DeleteStatus = 'N';

            COMMIT TRANSACTION;

            SET @ID = @MTransNo;
            SET @Message = 'Supplier deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        /* SupplierCode is no longer required - blank means "allocate one" on
           Insert and "keep the stored one" on Update. */
        IF @SupplierName IS NULL OR LTRIM(RTRIM(@SupplierName)) = ''
            THROW 63504, 'Supplier name is required.', 1;
        IF @CreditPeriod IS NOT NULL AND @CreditPeriod < 0
            THROW 63505, 'Credit period cannot be negative.', 1;

        IF @SupplierType IS NULL OR LTRIM(RTRIM(@SupplierType)) = ''
            SET @SupplierType = 'SUPPLIER';
        IF @TxnCurrency IS NULL OR LTRIM(RTRIM(@TxnCurrency)) = ''
            SET @TxnCurrency = 'INR';

        SET @SupplierCode = NULLIF(LTRIM(RTRIM(@SupplierCode)), '');
        SET @SupplierName = LTRIM(RTRIM(@SupplierName));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE SupplierName = @SupplierName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63506, 'Another supplier already uses this name.', 1;

            IF @SupplierCode IS NOT NULL
               AND EXISTS (SELECT 1 FROM dbo.SupplierMaster
                            WHERE SupplierCode = @SupplierCode AND DeleteStatus = 'N'
                              AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63507, 'Another supplier already uses this code.', 1;

            UPDATE dbo.SupplierMaster
               SET SupplierCode    = ISNULL(@SupplierCode, SupplierCode),
                   SupplierType    = @SupplierType,
                   SupplierName    = @SupplierName,
                   AliasName       = @AliasName,
                   Category        = @Category,
                   SupplierAddress = @SupplierAddress,
                   StateName       = @StateName,
                   StateCode       = @StateCode,
                   PinCode         = @PinCode,
                   PinCodeLocation = @PinCodeLocation,
                   PhoneNo         = @PhoneNo,
                   MobileNo        = @MobileNo,
                   EmailID         = @EmailID,
                   Website         = @Website,
                   TaxType         = @TaxType,
                   GSTIN           = @GSTIN,
                   PAN             = @PAN,
                   TxnCurrency     = @TxnCurrency,
                   CreditPeriod    = @CreditPeriod,
                   BankName        = @BankName,
                   BankAccountNo   = @BankAccountNo,
                   IFSC            = @IFSC,
                   Remarks         = @Remarks,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63508, 'Supplier record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Supplier updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                    WHERE SupplierName = @SupplierName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A supplier with this name already exists.'; RETURN;
        END

        IF @SupplierCode IS NOT NULL
           AND EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE SupplierCode = @SupplierCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A supplier with this code already exists.'; RETURN;
        END

        BEGIN TRANSACTION;

        IF @SupplierCode IS NULL
        BEGIN
            /* Step 1 - prefix: first letter of each word, 3 of them, X-padded. */
            DECLARE @Clean   VARCHAR(100) = UPPER(REPLACE(@SupplierName, '.', '')),
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

            /* Step 2 - next number for this prefix (deleted rows count too). */
            SELECT @NextNo = ISNULL(MAX(TRY_CAST(SUBSTRING(SupplierCode, 4, 7) AS INT)), 0) + 1
              FROM dbo.SupplierMaster WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID
               AND SupplierCode LIKE @Prefix + '[0-9]%';

            /* Step 3 - BST001 ... BST999, then BST1000. */
            SET @SupplierCode = @Prefix
                + CASE WHEN @NextNo < 1000 THEN RIGHT('00' + CAST(@NextNo AS VARCHAR(7)), 3)
                       ELSE CAST(@NextNo AS VARCHAR(7)) END;
        END

        INSERT INTO dbo.SupplierMaster
        (
            SupplierCode, SupplierType, SupplierName, AliasName, Category, SupplierAddress,
            StateName, StateCode, PinCode, PinCodeLocation, PhoneNo, MobileNo, EmailID, Website,
            TaxType, GSTIN, PAN, TxnCurrency, CreditPeriod, BankName, BankAccountNo, IFSC, Remarks,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @SupplierCode, @SupplierType, @SupplierName, @AliasName, @Category, @SupplierAddress,
            @StateName, @StateCode, @PinCode, @PinCodeLocation, @PhoneNo, @MobileNo, @EmailID, @Website,
            @TaxType, @GSTIN, @PAN, @TxnCurrency, @CreditPeriod, @BankName, @BankAccountNo, @IFSC, @Remarks,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();

        COMMIT TRANSACTION;

        SET @Message = 'Supplier saved successfully. Code: ' + @SupplierCode;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  2. MENU ROW + GRANTS  (MTransNo 125 is unused; right after Customer Data Import)
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
    (125, 'Masters', 1, 'Supplier Data Import', 25, 'bi-file-earmark-arrow-up', @Folder + 'SupplierDataImport.html');

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
