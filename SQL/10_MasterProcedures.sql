/*==============================================================================
  EMSSoft - 10_MasterProcedures.sql
  ------------------------------------------------------------------------------
  One save/delete procedure per table created by 09_MasterTables.sql. They share
  the contract the API layer (EMSContext.Call*SP) relies on:

      @ID       INT           OUTPUT   -- affected key, or 0 on failure
      @MTransNo INT                    -- 0 / omitted on Insert (parent key on
      @TransSlNo INT                       child tables, where @MTransNo is the
                                           parent it hangs off)
      @MCount   INT                    -- client row counter -> LockCounter
      ... business columns ...
      @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID
      @Status   VARCHAR(20)            -- 'Insert' | 'Update' | 'Delete'
      @Message  NVARCHAR(200) OUTPUT   -- human-readable result

  Every procedure is wrapped in TRY/CATCH: on failure @ID is 0 and @Message
  carries the reason, so the controller can hand it straight to the caller
  rather than surfacing a raw SQL error.

  ------------------------------------------------------------------------------
  Rules these procedures enforce that the schema alone cannot
  ------------------------------------------------------------------------------
  * Child rows are checked against their parent: the parent must exist, be live,
    and belong to the same subscription. The FKs only cover the key itself, so
    without this a contact could be filed under another subscription's customer.
  * Deleting a parent cascades the soft delete to its children, so a deleted
    customer/supplier/part leaves no rows behind that still list in child grids:
        CustomerMaster -> CustomerContacts, CustomerTerm, ShippingAddress
        SupplierMaster -> SupplierContacts, SupplierTerm, PriceList
        PartMaster     -> MaterialGrade, PriceList
    DrawingNos is deliberately NOT cascaded from PartMaster: a drawing is its own
    master record and outlives the part it happens to be tagged to.
  * PriceList rejects a second live rate for the same vendor/part/grade on one
    effective date - the rule the optional unique index in 09_MasterTables.sql
    would otherwise enforce.
  * Sequence numbers (TermSlNo, GradeNo, SlNo) are allocated by the procedure
    when the caller passes 0 or NULL.

  Because the unique keys in 09_MasterTables.sql are filtered indexes
  (WHERE DeleteStatus = 'N'), a soft-deleted row never blocks a new one with the
  same name - so unlike 03_StoredProcedures.sql these Delete branches need no
  collision pre-check.

  Error numbers use the 63001-64599 block; 60001-62306 are taken by
  03_StoredProcedures.sql and 06_AccountingProcedures.sql.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  SP_CustomerMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_CustomerMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_CustomerMaster;
GO
CREATE PROCEDURE dbo.SP_CustomerMaster
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

            /* Children go with the parent - see the header note. */
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
        IF @CustomerCode IS NULL OR LTRIM(RTRIM(@CustomerCode)) = ''
            THROW 63003, 'Customer code is required.', 1;
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

        SET @CustomerCode = LTRIM(RTRIM(@CustomerCode));
        SET @CustomerName = LTRIM(RTRIM(@CustomerName));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE CustomerName = @CustomerName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63007, 'Another customer already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE CustomerCode = @CustomerCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63008, 'Another customer already uses this code.', 1;

            UPDATE dbo.CustomerMaster
               SET CustomerCode      = @CustomerCode,
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

        IF EXISTS (SELECT 1 FROM dbo.CustomerMaster
                    WHERE CustomerCode = @CustomerCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A customer with this code already exists.'; RETURN;
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
        SET @Message = 'Customer saved successfully.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_CustomerContacts   (@MTransNo is the customer; @TransSlNo the contact row)
==============================================================================*/
IF OBJECT_ID('dbo.SP_CustomerContacts', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_CustomerContacts;
GO
CREATE PROCEDURE dbo.SP_CustomerContacts
    @ID           INT           OUTPUT,
    @TransSlNo    INT           = 0,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @Title        VARCHAR(30)   = NULL,
    @CPName       VARCHAR(50)   = NULL,
    @Designation  VARCHAR(30)   = NULL,
    @PhoneNo      VARCHAR(30)   = NULL,
    @MobileNo     VARCHAR(30)   = NULL,
    @Email        VARCHAR(80)   = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME2     = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 63101, 'A valid TransSlNo is required to delete a contact.', 1;

            UPDATE dbo.CustomerContacts
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63102, 'Contact record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Contact deleted successfully.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 63103, 'A customer (MTransNo) is required.', 1;
        IF @CPName IS NULL OR LTRIM(RTRIM(@CPName)) = ''
            THROW 63104, 'Contact person name is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 63105, 'The customer this contact belongs to does not exist.', 1;

        SET @CPName = LTRIM(RTRIM(@CPName));

        IF @Status = 'Update' AND @TransSlNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.CustomerContacts
                        WHERE MTransNo = @MTransNo AND CPName = @CPName
                          AND DeleteStatus = 'N' AND TransSlNo <> @TransSlNo)
                THROW 63106, 'This customer already has a contact with that name.', 1;

            UPDATE dbo.CustomerContacts
               SET MTransNo    = @MTransNo,
                   Title       = @Title,
                   CPName      = @CPName,
                   Designation = @Designation,
                   PhoneNo     = @PhoneNo,
                   MobileNo    = @MobileNo,
                   Email       = @Email,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63107, 'Contact record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Contact updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.CustomerContacts
                    WHERE MTransNo = @MTransNo AND CPName = @CPName AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'This customer already has a contact with that name.'; RETURN;
        END

        INSERT INTO dbo.CustomerContacts
        (
            MTransNo, Title, CPName, Designation, PhoneNo, MobileNo, Email,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MTransNo, @Title, @CPName, @Designation, @PhoneNo, @MobileNo, @Email,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Contact saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_CustomerTerm
      TermSlNo is decimal so terms can be sub-numbered (1.10, 1.20). Passing 0
      or NULL allocates the next whole number for the customer.
==============================================================================*/
IF OBJECT_ID('dbo.SP_CustomerTerm', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_CustomerTerm;
GO
CREATE PROCEDURE dbo.SP_CustomerTerm
    @ID              INT            OUTPUT,
    @TransSlNo       INT            = 0,
    @MTransNo        INT            = 0,
    @MCount          INT            = 0,
    @TermSlNo        NUMERIC(10,2)  = NULL,
    @TermTitle       VARCHAR(30)    = NULL,
    @TermDescription VARCHAR(250)   = NULL,
    @TermType        VARCHAR(30)    = NULL,
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
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 63201, 'A valid TransSlNo is required to delete a term.', 1;

            UPDATE dbo.CustomerTerm
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63202, 'Term record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Term deleted successfully.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 63203, 'A customer (MTransNo) is required.', 1;

        IF (@TermTitle IS NULL OR LTRIM(RTRIM(@TermTitle)) = '')
           AND (@TermDescription IS NULL OR LTRIM(RTRIM(@TermDescription)) = '')
            THROW 63204, 'A term title or description is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 63205, 'The customer this term belongs to does not exist.', 1;

        IF @TermType IS NULL OR LTRIM(RTRIM(@TermType)) = ''
            SET @TermType = 'CUSTOMER';

        IF @Status = 'Update' AND @TransSlNo > 0
        BEGIN
            UPDATE dbo.CustomerTerm
               SET MTransNo        = @MTransNo,
                   TermSlNo        = @TermSlNo,
                   TermTitle       = @TermTitle,
                   TermDescription = @TermDescription,
                   TermType        = @TermType,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63206, 'Term record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Term updated successfully.';
            RETURN;
        END

        IF ISNULL(@TermSlNo, 0) <= 0
            SELECT @TermSlNo = ISNULL(MAX(FLOOR(TermSlNo)), 0) + 1
              FROM dbo.CustomerTerm
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

        INSERT INTO dbo.CustomerTerm
        (
            MTransNo, TermSlNo, TermTitle, TermDescription, TermType,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MTransNo, @TermSlNo, @TermTitle, @TermDescription, @TermType,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Term saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_ShippingAddress
==============================================================================*/
IF OBJECT_ID('dbo.SP_ShippingAddress', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_ShippingAddress;
GO
CREATE PROCEDURE dbo.SP_ShippingAddress
    @ID              INT           OUTPUT,
    @TransSlNo       INT           = 0,
    @MTransNo        INT           = 0,
    @MCount          INT           = 0,
    @ShipName        VARCHAR(100)  = NULL,
    @ShipAlias       VARCHAR(100)  = NULL,
    @ShipAddress     VARCHAR(300)  = NULL,
    @ShipState       VARCHAR(50)   = NULL,
    @ShipStateCode   INT           = NULL,
    @PinCodeLocation VARCHAR(50)   = NULL,
    @PinCode         VARCHAR(30)   = NULL,
    @PhoneNo         VARCHAR(30)   = NULL,
    @MobileNo        VARCHAR(30)   = NULL,
    @EmailID         VARCHAR(80)   = NULL,
    @GSTIN           VARCHAR(15)   = NULL,
    @PAN             VARCHAR(10)   = NULL,
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

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 63301, 'A valid TransSlNo is required to delete a shipping address.', 1;

            UPDATE dbo.ShippingAddress
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63302, 'Shipping address not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Shipping address deleted successfully.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 63303, 'A customer (MTransNo) is required.', 1;
        IF @ShipName IS NULL OR LTRIM(RTRIM(@ShipName)) = ''
            THROW 63304, 'Ship-to name is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.CustomerMaster
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 63305, 'The customer this address belongs to does not exist.', 1;

        SET @ShipName = LTRIM(RTRIM(@ShipName));

        IF @Status = 'Update' AND @TransSlNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ShippingAddress
                        WHERE MTransNo = @MTransNo AND ShipName = @ShipName
                          AND DeleteStatus = 'N' AND TransSlNo <> @TransSlNo)
                THROW 63306, 'This customer already has a shipping address with that name.', 1;

            UPDATE dbo.ShippingAddress
               SET MTransNo        = @MTransNo,
                   ShipName        = @ShipName,
                   ShipAlias       = @ShipAlias,
                   ShipAddress     = @ShipAddress,
                   ShipState       = @ShipState,
                   ShipStateCode   = @ShipStateCode,
                   PinCodeLocation = @PinCodeLocation,
                   PinCode         = @PinCode,
                   PhoneNo         = @PhoneNo,
                   MobileNo        = @MobileNo,
                   EmailID         = @EmailID,
                   GSTIN           = @GSTIN,
                   PAN             = @PAN,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63307, 'Shipping address not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Shipping address updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.ShippingAddress
                    WHERE MTransNo = @MTransNo AND ShipName = @ShipName AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0;
            SET @Message = 'This customer already has a shipping address with that name.';
            RETURN;
        END

        INSERT INTO dbo.ShippingAddress
        (
            MTransNo, ShipName, ShipAlias, ShipAddress, ShipState, ShipStateCode,
            PinCodeLocation, PinCode, PhoneNo, MobileNo, EmailID, GSTIN, PAN,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MTransNo, @ShipName, @ShipAlias, @ShipAddress, @ShipState, @ShipStateCode,
            @PinCodeLocation, @PinCode, @PhoneNo, @MobileNo, @EmailID, @GSTIN, @PAN,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Shipping address saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_StandardTerms
      One library of terms for both sides of the ledger; TermType separates them.
==============================================================================*/
IF OBJECT_ID('dbo.SP_StandardTerms', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_StandardTerms;
GO
CREATE PROCEDURE dbo.SP_StandardTerms
    @ID              INT            OUTPUT,
    @MTransNo        INT            = 0,
    @MCount          INT            = 0,
    @TermType        VARCHAR(30)    = NULL,
    @TermSlNo        NUMERIC(10,2)  = NULL,
    @TermTitle       VARCHAR(30)    = NULL,
    @TermDescription VARCHAR(250)   = NULL,
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
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 63401, 'A valid MTransNo is required to delete a standard term.', 1;

            UPDATE dbo.StandardTerms
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63402, 'Standard term not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Standard term deleted successfully.';
            RETURN;
        END

        IF @TermType IS NULL OR LTRIM(RTRIM(@TermType)) = ''
            THROW 63403, 'Term type is required.', 1;
        IF @TermTitle IS NULL OR LTRIM(RTRIM(@TermTitle)) = ''
            THROW 63404, 'Term title is required.', 1;

        SET @TermType  = UPPER(LTRIM(RTRIM(@TermType)));
        SET @TermTitle = LTRIM(RTRIM(@TermTitle));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.StandardTerms
                        WHERE TermType = @TermType AND TermTitle = @TermTitle
                          AND DeleteStatus = 'N' AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63405, 'Another standard term of this type already uses that title.', 1;

            UPDATE dbo.StandardTerms
               SET TermType        = @TermType,
                   TermSlNo        = @TermSlNo,
                   TermTitle       = @TermTitle,
                   TermDescription = @TermDescription,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63406, 'Standard term not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Standard term updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.StandardTerms
                    WHERE TermType = @TermType AND TermTitle = @TermTitle
                      AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'A standard term of this type already uses that title.';
            RETURN;
        END

        IF ISNULL(@TermSlNo, 0) <= 0
            SELECT @TermSlNo = ISNULL(MAX(FLOOR(TermSlNo)), 0) + 1
              FROM dbo.StandardTerms
             WHERE TermType = @TermType AND SubscID = @SubscID AND DeleteStatus = 'N';

        INSERT INTO dbo.StandardTerms
        (
            TermType, TermSlNo, TermTitle, TermDescription,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @TermType, @TermSlNo, @TermTitle, @TermDescription,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Standard term saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_SupplierMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_SupplierMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_SupplierMaster;
GO
CREATE PROCEDURE dbo.SP_SupplierMaster
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

        IF @SupplierCode IS NULL OR LTRIM(RTRIM(@SupplierCode)) = ''
            THROW 63503, 'Supplier code is required.', 1;
        IF @SupplierName IS NULL OR LTRIM(RTRIM(@SupplierName)) = ''
            THROW 63504, 'Supplier name is required.', 1;
        IF @CreditPeriod IS NOT NULL AND @CreditPeriod < 0
            THROW 63505, 'Credit period cannot be negative.', 1;

        IF @SupplierType IS NULL OR LTRIM(RTRIM(@SupplierType)) = ''
            SET @SupplierType = 'SUPPLIER';
        IF @TxnCurrency IS NULL OR LTRIM(RTRIM(@TxnCurrency)) = ''
            SET @TxnCurrency = 'INR';

        SET @SupplierCode = LTRIM(RTRIM(@SupplierCode));
        SET @SupplierName = LTRIM(RTRIM(@SupplierName));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE SupplierName = @SupplierName AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63506, 'Another supplier already uses this name.', 1;

            IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE SupplierCode = @SupplierCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63507, 'Another supplier already uses this code.', 1;

            UPDATE dbo.SupplierMaster
               SET SupplierCode    = @SupplierCode,
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

        IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                    WHERE SupplierName = @SupplierName AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A supplier with this name already exists.'; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.SupplierMaster
                    WHERE SupplierCode = @SupplierCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A supplier with this code already exists.'; RETURN;
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
        SET @Message = 'Supplier saved successfully.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_SupplierContacts
==============================================================================*/
IF OBJECT_ID('dbo.SP_SupplierContacts', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_SupplierContacts;
GO
CREATE PROCEDURE dbo.SP_SupplierContacts
    @ID           INT           OUTPUT,
    @TransSlNo    INT           = 0,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @Title        VARCHAR(30)   = NULL,
    @CPName       VARCHAR(50)   = NULL,
    @Designation  VARCHAR(30)   = NULL,
    @PhoneNo      VARCHAR(30)   = NULL,
    @MobileNo     VARCHAR(30)   = NULL,
    @Email        VARCHAR(80)   = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME2     = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 63601, 'A valid TransSlNo is required to delete a contact.', 1;

            UPDATE dbo.SupplierContacts
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63602, 'Contact record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Contact deleted successfully.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 63603, 'A supplier (MTransNo) is required.', 1;
        IF @CPName IS NULL OR LTRIM(RTRIM(@CPName)) = ''
            THROW 63604, 'Contact person name is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 63605, 'The supplier this contact belongs to does not exist.', 1;

        SET @CPName = LTRIM(RTRIM(@CPName));

        IF @Status = 'Update' AND @TransSlNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.SupplierContacts
                        WHERE MTransNo = @MTransNo AND CPName = @CPName
                          AND DeleteStatus = 'N' AND TransSlNo <> @TransSlNo)
                THROW 63606, 'This supplier already has a contact with that name.', 1;

            UPDATE dbo.SupplierContacts
               SET MTransNo    = @MTransNo,
                   Title       = @Title,
                   CPName      = @CPName,
                   Designation = @Designation,
                   PhoneNo     = @PhoneNo,
                   MobileNo    = @MobileNo,
                   Email       = @Email,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63607, 'Contact record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Contact updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.SupplierContacts
                    WHERE MTransNo = @MTransNo AND CPName = @CPName AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'This supplier already has a contact with that name.'; RETURN;
        END

        INSERT INTO dbo.SupplierContacts
        (
            MTransNo, Title, CPName, Designation, PhoneNo, MobileNo, Email,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MTransNo, @Title, @CPName, @Designation, @PhoneNo, @MobileNo, @Email,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Contact saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_SupplierTerm
==============================================================================*/
IF OBJECT_ID('dbo.SP_SupplierTerm', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_SupplierTerm;
GO
CREATE PROCEDURE dbo.SP_SupplierTerm
    @ID              INT            OUTPUT,
    @TransSlNo       INT            = 0,
    @MTransNo        INT            = 0,
    @MCount          INT            = 0,
    @TermSlNo        NUMERIC(10,2)  = NULL,
    @TermTitle       VARCHAR(30)    = NULL,
    @TermDescription VARCHAR(250)   = NULL,
    @TermType        VARCHAR(30)    = NULL,
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
        IF @Status = 'Delete'
        BEGIN
            IF @TransSlNo <= 0
                THROW 63701, 'A valid TransSlNo is required to delete a term.', 1;

            UPDATE dbo.SupplierTerm
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63702, 'Term record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Term deleted successfully.';
            RETURN;
        END

        IF @MTransNo <= 0
            THROW 63703, 'A supplier (MTransNo) is required.', 1;

        IF (@TermTitle IS NULL OR LTRIM(RTRIM(@TermTitle)) = '')
           AND (@TermDescription IS NULL OR LTRIM(RTRIM(@TermDescription)) = '')
            THROW 63704, 'A term title or description is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 63705, 'The supplier this term belongs to does not exist.', 1;

        IF @TermType IS NULL OR LTRIM(RTRIM(@TermType)) = ''
            SET @TermType = 'SUPPLIER';

        IF @Status = 'Update' AND @TransSlNo > 0
        BEGIN
            UPDATE dbo.SupplierTerm
               SET MTransNo        = @MTransNo,
                   TermSlNo        = @TermSlNo,
                   TermTitle       = @TermTitle,
                   TermDescription = @TermDescription,
                   TermType        = @TermType,
                   SystemIP        = @SystemIP,
                   LockStatus      = ISNULL(@LockStatus, 'N'),
                   LockCounter     = @MCount,
                   ModifiedBy      = @CreatedBy,
                   ModifiedOn      = @Now
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63706, 'Term record not found.', 1;

            SET @ID = @TransSlNo;
            SET @Message = 'Term updated successfully.';
            RETURN;
        END

        IF ISNULL(@TermSlNo, 0) <= 0
            SELECT @TermSlNo = ISNULL(MAX(FLOOR(TermSlNo)), 0) + 1
              FROM dbo.SupplierTerm
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

        INSERT INTO dbo.SupplierTerm
        (
            MTransNo, TermSlNo, TermTitle, TermDescription, TermType,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @MTransNo, @TermSlNo, @TermTitle, @TermDescription, @TermType,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Term saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_PartMaster
      Only PartCode is unique - two parts may share a PartName, matching
      IX_PartMaster_PartName being non-unique in 09_MasterTables.sql.
==============================================================================*/
IF OBJECT_ID('dbo.SP_PartMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_PartMaster;
GO
CREATE PROCEDURE dbo.SP_PartMaster
    @ID               INT            OUTPUT,
    @MTransNo         INT            = 0,
    @MCount           INT            = 0,
    @PartCode         VARCHAR(30)    = NULL,
    @PartName         VARCHAR(250)   = NULL,
    @CodeDetails      VARCHAR(100)   = NULL,
    @PartDetails      VARCHAR(500)   = NULL,
    @ShortDescription VARCHAR(250)   = NULL,
    @FullDescription  VARCHAR(1000)  = NULL,
    @DrawingNo        VARCHAR(50)    = NULL,
    @DrgRevNo         INT            = NULL,
    @ColorCode        VARCHAR(20)    = NULL,
    @CatNo            INT            = NULL,
    @SubCatNo         INT            = NULL,
    @GroupNo          INT            = NULL,
    @Category         VARCHAR(50)    = NULL,
    @SubCategory      VARCHAR(50)    = NULL,
    @InventoryGroup   VARCHAR(50)    = NULL,
    @Department       VARCHAR(50)    = NULL,
    @Brand            VARCHAR(50)    = NULL,
    @PartType         VARCHAR(30)    = NULL,
    @PartClass        VARCHAR(10)    = NULL,
    @HSNCode          VARCHAR(10)    = NULL,
    @TaxP             NUMERIC(6,2)   = NULL,
    @PrimaryUnit      VARCHAR(10)    = NULL,
    @SecondaryUnit    VARCHAR(10)    = NULL,
    @ConversionRatio  NUMERIC(14,4)  = NULL,
    @TxnUnit          VARCHAR(10)    = NULL,
    @Packing          VARCHAR(50)    = NULL,
    @PackQty          NUMERIC(14,2)  = NULL,
    @PackSize         NUMERIC(14,2)  = NULL,
    @SPart            CHAR(1)        = NULL,
    @PPart            CHAR(1)        = NULL,
    @MPart            CHAR(1)        = NULL,
    @MinStock         NUMERIC(14,2)  = NULL,
    @MaxStock         NUMERIC(14,2)  = NULL,
    @MOQ              NUMERIC(14,2)  = NULL,
    @ShelfLife        NUMERIC(14,2)  = NULL,
    @CostPrice        NUMERIC(18,2)  = NULL,
    @SalesPrice       NUMERIC(18,2)  = NULL,
    @MRP              NUMERIC(18,2)  = NULL,
    @MfrLicNo         VARCHAR(50)    = NULL,
    @MfgStd           VARCHAR(50)    = NULL,
    @StoreLocation    VARCHAR(50)    = NULL,
    @Rack             VARCHAR(10)    = NULL,
    @RackRow          VARCHAR(10)    = NULL,
    @RackCase         VARCHAR(10)    = NULL,
    @PartLength       NUMERIC(14,2)  = NULL,
    @PartWidth        NUMERIC(14,2)  = NULL,
    @PartHeight       NUMERIC(14,2)  = NULL,
    @PartWeight       NUMERIC(14,3)  = NULL,
    @AtchFileName     VARCHAR(250)   = NULL,
    @SystemIP         VARCHAR(20)    = NULL,
    @LockStatus       CHAR(1)        = 'N',
    @CreatedBy        INT            = 0,
    @CreatedOn        DATETIME2      = NULL,
    @DeleteReason     VARCHAR(250)   = NULL,
    @SubscID          INT            = 0,
    @Status           VARCHAR(20)    = 'Insert',
    @Message          NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 63801, 'A valid MTransNo is required to delete a part.', 1;

            BEGIN TRANSACTION;

            UPDATE dbo.PartMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                ROLLBACK TRANSACTION;
                THROW 63802, 'Part record not found.', 1;
            END

            UPDATE dbo.MaterialGrade
               SET DeleteStatus = 'Y', DeleteReason = 'Part deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE PartNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.PriceList
               SET DeleteStatus = 'Y', DeleteReason = 'Part deleted.',
                   ModifiedBy = @CreatedBy, ModifiedOn = @Now
             WHERE PartNo = @MTransNo AND DeleteStatus = 'N';

            /* DrawingNos rows survive on purpose - a drawing is its own master. */

            COMMIT TRANSACTION;

            SET @ID = @MTransNo;
            SET @Message = 'Part deleted successfully.';
            RETURN;
        END

        IF @PartCode IS NULL OR LTRIM(RTRIM(@PartCode)) = ''
            THROW 63803, 'Part code is required.', 1;
        IF @PartName IS NULL OR LTRIM(RTRIM(@PartName)) = ''
            THROW 63804, 'Part name is required.', 1;
        IF @MinStock IS NOT NULL AND @MaxStock IS NOT NULL AND @MaxStock < @MinStock
            THROW 63805, 'Maximum stock cannot be less than minimum stock.', 1;
        IF @TaxP IS NOT NULL AND (@TaxP < 0 OR @TaxP > 100)
            THROW 63806, 'Tax percentage must be between 0 and 100.', 1;

        SET @PartCode = LTRIM(RTRIM(@PartCode));
        SET @PartName = LTRIM(RTRIM(@PartName));

        SET @SPart = ISNULL(@SPart, 'N');
        SET @PPart = ISNULL(@PPart, 'N');
        SET @MPart = ISNULL(@MPart, 'N');

        /* A second unit is meaningless without a ratio to reach it. */
        IF @SecondaryUnit IS NOT NULL AND LTRIM(RTRIM(@SecondaryUnit)) <> ''
           AND ISNULL(@ConversionRatio, 0) <= 0
            SET @ConversionRatio = 1;

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.PartMaster
                        WHERE PartCode = @PartCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63807, 'Another part already uses this code.', 1;

            UPDATE dbo.PartMaster
               SET PartCode         = @PartCode,
                   PartName         = @PartName,
                   CodeDetails      = @CodeDetails,
                   PartDetails      = @PartDetails,
                   ShortDescription = @ShortDescription,
                   FullDescription  = @FullDescription,
                   DrawingNo        = @DrawingNo,
                   DrgRevNo         = @DrgRevNo,
                   ColorCode        = @ColorCode,
                   CatNo            = @CatNo,
                   SubCatNo         = @SubCatNo,
                   GroupNo          = @GroupNo,
                   Category         = @Category,
                   SubCategory      = @SubCategory,
                   InventoryGroup   = @InventoryGroup,
                   Department       = @Department,
                   Brand            = @Brand,
                   PartType         = @PartType,
                   PartClass        = @PartClass,
                   HSNCode          = @HSNCode,
                   TaxP             = @TaxP,
                   PrimaryUnit      = @PrimaryUnit,
                   SecondaryUnit    = @SecondaryUnit,
                   ConversionRatio  = @ConversionRatio,
                   TxnUnit          = @TxnUnit,
                   Packing          = @Packing,
                   PackQty          = @PackQty,
                   PackSize         = @PackSize,
                   SPart            = @SPart,
                   PPart            = @PPart,
                   MPart            = @MPart,
                   MinStock         = @MinStock,
                   MaxStock         = @MaxStock,
                   MOQ              = @MOQ,
                   ShelfLife        = @ShelfLife,
                   CostPrice        = @CostPrice,
                   SalesPrice       = @SalesPrice,
                   MRP              = @MRP,
                   MfrLicNo         = @MfrLicNo,
                   MfgStd           = @MfgStd,
                   StoreLocation    = @StoreLocation,
                   Rack             = @Rack,
                   RackRow          = @RackRow,
                   RackCase         = @RackCase,
                   PartLength       = @PartLength,
                   PartWidth        = @PartWidth,
                   PartHeight       = @PartHeight,
                   PartWeight       = @PartWeight,
                   AtchFileName     = @AtchFileName,
                   SystemIP         = @SystemIP,
                   LockStatus       = ISNULL(@LockStatus, 'N'),
                   LockCounter      = @MCount,
                   ModifiedBy       = @CreatedBy,
                   ModifiedOn       = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63808, 'Part record not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Part updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.PartMaster
                    WHERE PartCode = @PartCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A part with this code already exists.'; RETURN;
        END

        INSERT INTO dbo.PartMaster
        (
            PartCode, PartName, CodeDetails, PartDetails, ShortDescription, FullDescription,
            DrawingNo, DrgRevNo, ColorCode, CatNo, SubCatNo, GroupNo,
            Category, SubCategory, InventoryGroup, Department, Brand, PartType, PartClass,
            HSNCode, TaxP, PrimaryUnit, SecondaryUnit, ConversionRatio, TxnUnit,
            Packing, PackQty, PackSize, SPart, PPart, MPart,
            MinStock, MaxStock, MOQ, ShelfLife, CostPrice, SalesPrice, MRP,
            MfrLicNo, MfgStd, StoreLocation, Rack, RackRow, RackCase,
            PartLength, PartWidth, PartHeight, PartWeight, AtchFileName,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @PartCode, @PartName, @CodeDetails, @PartDetails, @ShortDescription, @FullDescription,
            @DrawingNo, @DrgRevNo, @ColorCode, @CatNo, @SubCatNo, @GroupNo,
            @Category, @SubCategory, @InventoryGroup, @Department, @Brand, @PartType, @PartClass,
            @HSNCode, @TaxP, @PrimaryUnit, @SecondaryUnit, @ConversionRatio, @TxnUnit,
            @Packing, @PackQty, @PackSize, @SPart, @PPart, @MPart,
            @MinStock, @MaxStock, @MOQ, @ShelfLife, @CostPrice, @SalesPrice, @MRP,
            @MfrLicNo, @MfgStd, @StoreLocation, @Rack, @RackRow, @RackCase,
            @PartLength, @PartWidth, @PartHeight, @PartWeight, @AtchFileName,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Part saved successfully.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_PartCodeMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_PartCodeMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_PartCodeMaster;
GO
CREATE PROCEDURE dbo.SP_PartCodeMaster
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @Parameter    VARCHAR(50)   = NULL,
    @Code         VARCHAR(10)   = NULL,
    @Description  VARCHAR(250)  = NULL,
    @ValveType    VARCHAR(50)   = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME2     = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 63901, 'A valid MTransNo is required to delete a part code.', 1;

            UPDATE dbo.PartCodeMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63902, 'Part code not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Part code deleted successfully.';
            RETURN;
        END

        IF @Parameter IS NULL OR LTRIM(RTRIM(@Parameter)) = ''
            THROW 63903, 'Parameter is required.', 1;
        IF @Code IS NULL OR LTRIM(RTRIM(@Code)) = ''
            THROW 63904, 'Code is required.', 1;

        SET @Parameter = LTRIM(RTRIM(@Parameter));
        SET @Code      = LTRIM(RTRIM(@Code));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.PartCodeMaster
                        WHERE Parameter = @Parameter AND Code = @Code AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 63905, 'This parameter already has that code.', 1;

            UPDATE dbo.PartCodeMaster
               SET Parameter   = @Parameter,
                   Code        = @Code,
                   Description = @Description,
                   ValveType   = @ValveType,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 63906, 'Part code not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Part code updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.PartCodeMaster
                    WHERE Parameter = @Parameter AND Code = @Code
                      AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This parameter already has that code.'; RETURN;
        END

        INSERT INTO dbo.PartCodeMaster
        (
            Parameter, Code, Description, ValveType,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @Parameter, @Code, @Description, @ValveType,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Part code saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_PartParameterList
      SlNo is the parameter's position in the generated part code; 0/NULL takes
      the next free slot for the part type.
==============================================================================*/
IF OBJECT_ID('dbo.SP_PartParameterList', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_PartParameterList;
GO
CREATE PROCEDURE dbo.SP_PartParameterList
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @PartType     VARCHAR(30)   = NULL,
    @Parameter    VARCHAR(50)   = NULL,
    @SlNo         INT           = NULL,
    @MaxLength    INT           = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME2     = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64001, 'A valid MTransNo is required to delete a parameter.', 1;

            UPDATE dbo.PartParameterList
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64002, 'Parameter not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Parameter deleted successfully.';
            RETURN;
        END

        IF @PartType IS NULL OR LTRIM(RTRIM(@PartType)) = ''
            THROW 64003, 'Part type is required.', 1;
        IF @Parameter IS NULL OR LTRIM(RTRIM(@Parameter)) = ''
            THROW 64004, 'Parameter is required.', 1;
        IF @MaxLength IS NOT NULL AND @MaxLength <= 0
            THROW 64005, 'Maximum length must be greater than zero.', 1;

        SET @PartType  = LTRIM(RTRIM(@PartType));
        SET @Parameter = LTRIM(RTRIM(@Parameter));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.PartParameterList
                        WHERE PartType = @PartType AND Parameter = @Parameter
                          AND DeleteStatus = 'N' AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 64006, 'This part type already uses that parameter.', 1;

            UPDATE dbo.PartParameterList
               SET PartType    = @PartType,
                   Parameter   = @Parameter,
                   SlNo        = @SlNo,
                   MaxLength   = @MaxLength,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64007, 'Parameter not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Parameter updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.PartParameterList
                    WHERE PartType = @PartType AND Parameter = @Parameter
                      AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This part type already uses that parameter.'; RETURN;
        END

        IF ISNULL(@SlNo, 0) <= 0
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.PartParameterList
             WHERE PartType = @PartType AND SubscID = @SubscID AND DeleteStatus = 'N';

        INSERT INTO dbo.PartParameterList
        (
            PartType, Parameter, SlNo, MaxLength,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @PartType, @Parameter, @SlNo, @MaxLength,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Parameter saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_DrawingNos
      PartNo is optional; 0 is stored as NULL rather than failing the FK.
==============================================================================*/
IF OBJECT_ID('dbo.SP_DrawingNos', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_DrawingNos;
GO
CREATE PROCEDURE dbo.SP_DrawingNos
    @ID                 INT           OUTPUT,
    @MTransNo           INT           = 0,
    @MCount             INT           = 0,
    @DrawingNo          VARCHAR(50)   = NULL,
    @PartNo             INT           = NULL,
    @DrawingType        VARCHAR(50)   = NULL,
    @DrawingGroup       VARCHAR(50)   = NULL,
    @PartGroup          VARCHAR(50)   = NULL,
    @PSlNo              INT           = NULL,
    @DrawingDescription VARCHAR(250)  = NULL,
    @DrawingRemarks     VARCHAR(250)  = NULL,
    @RevNo              INT           = NULL,
    @RevDate            DATE          = NULL,
    @RevStatus          CHAR(1)       = NULL,
    @FileAttach         VARCHAR(250)  = NULL,
    @SystemIP           VARCHAR(20)   = NULL,
    @LockStatus         CHAR(1)       = 'N',
    @CreatedBy          INT           = 0,
    @CreatedOn          DATETIME2     = NULL,
    @DeleteReason       VARCHAR(250)  = NULL,
    @SubscID            INT           = 0,
    @Status             VARCHAR(20)   = 'Insert',
    @Message            NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64101, 'A valid MTransNo is required to delete a drawing.', 1;

            UPDATE dbo.DrawingNos
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64102, 'Drawing not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Drawing deleted successfully.';
            RETURN;
        END

        IF @DrawingNo IS NULL OR LTRIM(RTRIM(@DrawingNo)) = ''
            THROW 64103, 'Drawing number is required.', 1;

        SET @DrawingNo = LTRIM(RTRIM(@DrawingNo));

        IF ISNULL(@PartNo, 0) <= 0
            SET @PartNo = NULL;
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.PartMaster
                             WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 64104, 'The part this drawing points at does not exist.', 1;

        SET @RevNo = ISNULL(@RevNo, 0);

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.DrawingNos
                        WHERE DrawingNo = @DrawingNo AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 64105, 'Another drawing already uses this number.', 1;

            UPDATE dbo.DrawingNos
               SET DrawingNo          = @DrawingNo,
                   PartNo             = @PartNo,
                   DrawingType        = @DrawingType,
                   DrawingGroup       = @DrawingGroup,
                   PartGroup          = @PartGroup,
                   PSlNo              = @PSlNo,
                   DrawingDescription = @DrawingDescription,
                   DrawingRemarks     = @DrawingRemarks,
                   RevNo              = @RevNo,
                   RevDate            = @RevDate,
                   RevStatus          = @RevStatus,
                   FileAttach         = @FileAttach,
                   SystemIP           = @SystemIP,
                   LockStatus         = ISNULL(@LockStatus, 'N'),
                   LockCounter        = @MCount,
                   ModifiedBy         = @CreatedBy,
                   ModifiedOn         = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64106, 'Drawing not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Drawing updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.DrawingNos
                    WHERE DrawingNo = @DrawingNo AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'A drawing with this number already exists.'; RETURN;
        END

        INSERT INTO dbo.DrawingNos
        (
            DrawingNo, PartNo, DrawingType, DrawingGroup, PartGroup, PSlNo,
            DrawingDescription, DrawingRemarks, RevNo, RevDate, RevStatus, FileAttach,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @DrawingNo, @PartNo, @DrawingType, @DrawingGroup, @PartGroup, @PSlNo,
            @DrawingDescription, @DrawingRemarks, @RevNo, @RevDate, @RevStatus, @FileAttach,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Drawing saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_MaterialGrade
      GradeNo is the grade's number within the part; 0/NULL takes the next one.
==============================================================================*/
IF OBJECT_ID('dbo.SP_MaterialGrade', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_MaterialGrade;
GO
CREATE PROCEDURE dbo.SP_MaterialGrade
    @ID           INT            OUTPUT,
    @MTransNo     INT            = 0,
    @MCount       INT            = 0,
    @PartNo       INT            = 0,
    @GradeNo      INT            = 0,
    @Grade        VARCHAR(50)    = NULL,
    @MinStock     NUMERIC(14,2)  = NULL,
    @MaxStock     NUMERIC(14,2)  = NULL,
    @MOQ          NUMERIC(14,2)  = NULL,
    @SystemIP     VARCHAR(20)    = NULL,
    @LockStatus   CHAR(1)        = 'N',
    @CreatedBy    INT            = 0,
    @CreatedOn    DATETIME2      = NULL,
    @DeleteReason VARCHAR(250)   = NULL,
    @SubscID      INT            = 0,
    @Status       VARCHAR(20)    = 'Insert',
    @Message      NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64201, 'A valid MTransNo is required to delete a grade.', 1;

            IF EXISTS (SELECT 1 FROM dbo.PriceList p
                        JOIN dbo.MaterialGrade g ON g.MTransNo = @MTransNo
                       WHERE p.PartNo = g.PartNo AND p.GradeNo = g.GradeNo
                         AND p.DeleteStatus = 'N')
                THROW 64202, 'This grade is priced in the price list. Remove those rates first.', 1;

            UPDATE dbo.MaterialGrade
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64203, 'Grade not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Grade deleted successfully.';
            RETURN;
        END

        IF @PartNo <= 0
            THROW 64204, 'A part (PartNo) is required.', 1;
        IF @Grade IS NULL OR LTRIM(RTRIM(@Grade)) = ''
            THROW 64205, 'Grade is required.', 1;
        IF @MinStock IS NOT NULL AND @MaxStock IS NOT NULL AND @MaxStock < @MinStock
            THROW 64206, 'Maximum stock cannot be less than minimum stock.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.PartMaster
                        WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 64207, 'The part this grade belongs to does not exist.', 1;

        SET @Grade = LTRIM(RTRIM(@Grade));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.MaterialGrade
                        WHERE PartNo = @PartNo AND Grade = @Grade
                          AND DeleteStatus = 'N' AND MTransNo <> @MTransNo)
                THROW 64208, 'This part already has that grade.', 1;

            IF @GradeNo > 0 AND EXISTS (SELECT 1 FROM dbo.MaterialGrade
                                         WHERE PartNo = @PartNo AND GradeNo = @GradeNo
                                           AND DeleteStatus = 'N' AND MTransNo <> @MTransNo)
                THROW 64209, 'This part already has a grade with that number.', 1;

            UPDATE dbo.MaterialGrade
               SET PartNo      = @PartNo,
                   GradeNo     = CASE WHEN @GradeNo > 0 THEN @GradeNo ELSE GradeNo END,
                   Grade       = @Grade,
                   MinStock    = @MinStock,
                   MaxStock    = @MaxStock,
                   MOQ         = @MOQ,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64210, 'Grade not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Grade updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.MaterialGrade
                    WHERE PartNo = @PartNo AND Grade = @Grade AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'This part already has that grade.'; RETURN;
        END

        IF ISNULL(@GradeNo, 0) <= 0
            SELECT @GradeNo = ISNULL(MAX(GradeNo), 0) + 1
              FROM dbo.MaterialGrade
             WHERE PartNo = @PartNo AND DeleteStatus = 'N';

        IF EXISTS (SELECT 1 FROM dbo.MaterialGrade
                    WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'This part already has a grade with that number.'; RETURN;
        END

        INSERT INTO dbo.MaterialGrade
        (
            PartNo, GradeNo, Grade, MinStock, MaxStock, MOQ,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @PartNo, @GradeNo, @Grade, @MinStock, @MaxStock, @MOQ,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Grade saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_MaterialStd
==============================================================================*/
IF OBJECT_ID('dbo.SP_MaterialStd', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_MaterialStd;
GO
CREATE PROCEDURE dbo.SP_MaterialStd
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @Grade        VARCHAR(50)   = NULL,
    @MatrStd      VARCHAR(100)  = NULL,
    @SystemIP     VARCHAR(20)   = NULL,
    @LockStatus   CHAR(1)       = 'N',
    @CreatedBy    INT           = 0,
    @CreatedOn    DATETIME2     = NULL,
    @DeleteReason VARCHAR(250)  = NULL,
    @SubscID      INT           = 0,
    @Status       VARCHAR(20)   = 'Insert',
    @Message      NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64301, 'A valid MTransNo is required to delete a material standard.', 1;

            UPDATE dbo.MaterialStd
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64302, 'Material standard not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Material standard deleted successfully.';
            RETURN;
        END

        IF @Grade IS NULL OR LTRIM(RTRIM(@Grade)) = ''
            THROW 64303, 'Grade is required.', 1;
        IF @MatrStd IS NULL OR LTRIM(RTRIM(@MatrStd)) = ''
            THROW 64304, 'Material standard is required.', 1;

        SET @Grade   = LTRIM(RTRIM(@Grade));
        SET @MatrStd = LTRIM(RTRIM(@MatrStd));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.MaterialStd
                        WHERE Grade = @Grade AND MatrStd = @MatrStd AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 64305, 'This grade is already mapped to that material standard.', 1;

            UPDATE dbo.MaterialStd
               SET Grade       = @Grade,
                   MatrStd     = @MatrStd,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64306, 'Material standard not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Material standard updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.MaterialStd
                    WHERE Grade = @Grade AND MatrStd = @MatrStd
                      AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'This grade is already mapped to that material standard.';
            RETURN;
        END

        INSERT INTO dbo.MaterialStd
        (
            Grade, MatrStd,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @Grade, @MatrStd,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Material standard saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_HSNMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_HSNMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_HSNMaster;
GO
CREATE PROCEDURE dbo.SP_HSNMaster
    @ID             INT           OUTPUT,
    @MTransNo       INT           = 0,
    @MCount         INT           = 0,
    @HSNCode        VARCHAR(10)   = NULL,
    @HSNDescription VARCHAR(250)  = NULL,
    @SystemIP       VARCHAR(20)   = NULL,
    @LockStatus     CHAR(1)       = 'N',
    @CreatedBy      INT           = 0,
    @CreatedOn      DATETIME2     = NULL,
    @DeleteReason   VARCHAR(250)  = NULL,
    @SubscID        INT           = 0,
    @Status         VARCHAR(20)   = 'Insert',
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64401, 'A valid MTransNo is required to delete an HSN code.', 1;

            /* Parts carry HSNCode as text, so check by value before removing it. */
            IF EXISTS (SELECT 1 FROM dbo.PartMaster p
                        JOIN dbo.HSNMaster h ON h.MTransNo = @MTransNo
                       WHERE p.HSNCode = h.HSNCode AND p.SubscID = h.SubscID
                         AND p.DeleteStatus = 'N')
                THROW 64402, 'This HSN code is used by one or more parts.', 1;

            UPDATE dbo.HSNMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64403, 'HSN code not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'HSN code deleted successfully.';
            RETURN;
        END

        IF @HSNCode IS NULL OR LTRIM(RTRIM(@HSNCode)) = ''
            THROW 64404, 'HSN code is required.', 1;

        SET @HSNCode = LTRIM(RTRIM(@HSNCode));

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.HSNMaster
                        WHERE HSNCode = @HSNCode AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 64405, 'Another record already uses this HSN code.', 1;

            UPDATE dbo.HSNMaster
               SET HSNCode        = @HSNCode,
                   HSNDescription = @HSNDescription,
                   SystemIP       = @SystemIP,
                   LockStatus     = ISNULL(@LockStatus, 'N'),
                   LockCounter    = @MCount,
                   ModifiedBy     = @CreatedBy,
                   ModifiedOn     = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64406, 'HSN code not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'HSN code updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.HSNMaster
                    WHERE HSNCode = @HSNCode AND DeleteStatus = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0; SET @Message = 'This HSN code already exists.'; RETURN;
        END

        INSERT INTO dbo.HSNMaster
        (
            HSNCode, HSNDescription,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @HSNCode, @HSNDescription,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'HSN code saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_PriceList
      A vendor/part pair holds many rate rows over time. What is rejected is a
      second live rate for the same vendor/part/grade on the same effective date.
      Unit falls back to the part's primary unit, and GradeNo - which has no FK -
      must belong to the part.
==============================================================================*/
IF OBJECT_ID('dbo.SP_PriceList', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_PriceList;
GO
CREATE PROCEDURE dbo.SP_PriceList
    @ID           INT            OUTPUT,
    @MTransNo     INT            = 0,
    @MCount       INT            = 0,
    @VendorNo     INT            = 0,
    @PartNo       INT            = 0,
    @GradeNo      INT            = NULL,
    @Grade        VARCHAR(50)    = NULL,
    @Unit         VARCHAR(10)    = NULL,
    @Rate         NUMERIC(18,4)  = NULL,
    @EffectiveDt  DATE           = NULL,
    @ItemRemarks  VARCHAR(250)   = NULL,
    @LeadTime     INT            = NULL,
    @SystemIP     VARCHAR(20)    = NULL,
    @LockStatus   CHAR(1)        = 'N',
    @CreatedBy    INT            = 0,
    @CreatedOn    DATETIME2      = NULL,
    @DeleteReason VARCHAR(250)   = NULL,
    @SubscID      INT            = 0,
    @Status       VARCHAR(20)    = 'Insert',
    @Message      NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now DATETIME2(3) = SYSDATETIME();
    SET @ID = 0;

    BEGIN TRY
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 64501, 'A valid MTransNo is required to delete a rate.', 1;

            UPDATE dbo.PriceList
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64502, 'Rate not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Rate deleted successfully.';
            RETURN;
        END

        IF @VendorNo <= 0
            THROW 64503, 'A vendor (VendorNo) is required.', 1;
        IF @PartNo <= 0
            THROW 64504, 'A part (PartNo) is required.', 1;
        IF @Rate IS NULL
            THROW 64505, 'Rate is required.', 1;
        IF @Rate < 0
            THROW 64506, 'Rate cannot be negative.', 1;
        IF @LeadTime IS NOT NULL AND @LeadTime < 0
            THROW 64507, 'Lead time cannot be negative.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.SupplierMaster
                        WHERE MTransNo = @VendorNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 64508, 'The vendor on this rate does not exist.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.PartMaster
                        WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            THROW 64509, 'The part on this rate does not exist.', 1;

        IF ISNULL(@GradeNo, 0) <= 0
            SET @GradeNo = NULL;
        ELSE
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.MaterialGrade
                            WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND DeleteStatus = 'N')
                THROW 64510, 'That grade does not belong to this part.', 1;

            IF @Grade IS NULL OR LTRIM(RTRIM(@Grade)) = ''
                SELECT @Grade = Grade FROM dbo.MaterialGrade
                 WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND DeleteStatus = 'N';
        END

        SET @EffectiveDt = ISNULL(@EffectiveDt, CAST(@Now AS DATE));

        IF @Unit IS NULL OR LTRIM(RTRIM(@Unit)) = ''
            SELECT @Unit = PrimaryUnit FROM dbo.PartMaster WHERE MTransNo = @PartNo;

        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.PriceList
                        WHERE VendorNo = @VendorNo AND PartNo = @PartNo
                          AND ISNULL(GradeNo, 0) = ISNULL(@GradeNo, 0)
                          AND EffectiveDt = @EffectiveDt
                          AND DeleteStatus = 'N' AND MTransNo <> @MTransNo)
                THROW 64511, 'This vendor already has a rate for that part and date.', 1;

            UPDATE dbo.PriceList
               SET VendorNo    = @VendorNo,
                   PartNo      = @PartNo,
                   GradeNo     = @GradeNo,
                   Grade       = @Grade,
                   Unit        = @Unit,
                   Rate        = @Rate,
                   EffectiveDt = @EffectiveDt,
                   ItemRemarks = @ItemRemarks,
                   LeadTime    = @LeadTime,
                   SystemIP    = @SystemIP,
                   LockStatus  = ISNULL(@LockStatus, 'N'),
                   LockCounter = @MCount,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = @Now
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
                THROW 64512, 'Rate not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Rate updated successfully.';
            RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.PriceList
                    WHERE VendorNo = @VendorNo AND PartNo = @PartNo
                      AND ISNULL(GradeNo, 0) = ISNULL(@GradeNo, 0)
                      AND EffectiveDt = @EffectiveDt
                      AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0;
            SET @Message = 'This vendor already has a rate for that part and date.';
            RETURN;
        END

        INSERT INTO dbo.PriceList
        (
            VendorNo, PartNo, GradeNo, Grade, Unit, Rate, EffectiveDt, ItemRemarks, LeadTime,
            SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID
        )
        VALUES
        (
            @VendorNo, @PartNo, @GradeNo, @Grade, @Unit, @Rate, @EffectiveDt, @ItemRemarks, @LeadTime,
            @SystemIP, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', ISNULL(@LockStatus, 'N'), @MCount, @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Rate saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


PRINT '10_MasterProcedures.sql complete - 16 procedures ready.';
GO
