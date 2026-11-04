/*==============================================================================
  EMSSoft — 06_AccountingProcedures.sql
  ------------------------------------------------------------------------------
      SP_GroupHeads      SP_AccountMaster      SP_TaxMaster      SP_AccountConfig

  Ported from LoanDRC and brought onto the EMS contract used by
  03_StoredProcedures.sql:

    [CHANGE 1] The LoanDRC versions end their CATCH block with
               RAISERROR(@ErrMsg, @ErrSeverity, 1). That re-raises the error to
               the client, so ExecuteSqlRawAsync throws and the @Message the
               procedure just set is never read — the caller sees a raw SQL
               exception instead of the intended text. These versions set
               @ID = 0 and @Message = ERROR_MESSAGE() and return normally, which
               is what the Call*SP wrappers expect.

    [CHANGE 2] Added a 'Delete' branch to each procedure so soft deletes go
               through the same path as every other write, instead of the
               controller mutating the entity directly.

    [CHANGE 3] SP_AccountMaster / SP_TaxMaster in LoanDRC do not re-check for a
               duplicate name on Update, so an edit could rename one record onto
               another. Both now check.

    [CHANGE 4] SP_GroupHeads declared @MCount as OUTPUT but the caller passed it
               by value. It is a plain input here, matching every other
               procedure in this database.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  SP_GroupHeads
==============================================================================*/
IF OBJECT_ID('dbo.SP_GroupHeads', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_GroupHeads;
GO
CREATE PROCEDURE dbo.SP_GroupHeads
    @ID           INT           OUTPUT,
    @MCount       INT           = 0,
    @GrpNo        INT           = 0,
    @GrpName      VARCHAR(50)   = NULL,
    @GrpMastNo    INT           = NULL,
    @GrpStatus    CHAR(1)       = 'S',
    @BSStatus     VARCHAR(20)   = NULL,
    @FAStatus     VARCHAR(10)   = NULL,
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
        /*-------------------------------- DELETE --------------------------------*/
        IF @Status = 'Delete'
        BEGIN
            IF @GrpNo <= 0
                THROW 62001, 'A valid GrpNo is required to delete a group.', 1;

            -- A group that still has children or ledgers cannot be removed.
            IF EXISTS (SELECT 1 FROM dbo.GroupHeads
                        WHERE GrpMastNo = @GrpNo AND ISNULL(DeleteStatus, 'N') = 'N')
                THROW 62002, 'This group has sub-groups. Delete or move them first.', 1;

            IF EXISTS (SELECT 1 FROM dbo.AccountsMaster
                        WHERE GroupHeadNo = @GrpNo AND ISNULL(DeleteStatus, 'N') = 'N')
                THROW 62003, 'Ledger accounts are still assigned to this group.', 1;

            UPDATE dbo.GroupHeads
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE GrpNo = @GrpNo;

            IF @@ROWCOUNT = 0
                THROW 62004, 'Group head not found.', 1;

            SET @ID = @GrpNo;
            SET @Message = 'Group head deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        IF @GrpName IS NULL OR LTRIM(RTRIM(@GrpName)) = ''
            THROW 62005, 'Group name is required.', 1;
        IF @BSStatus IS NULL OR LTRIM(RTRIM(@BSStatus)) = ''
            THROW 62006, 'Balance Sheet / P&L status is required.', 1;

        SET @GrpName = LTRIM(RTRIM(@GrpName));

        -- A sub-group must name a parent; a primary group must not.
        IF @GrpStatus = 'S' AND ISNULL(@GrpMastNo, 0) <= 0
            THROW 62007, 'A sub-group must have a parent group.', 1;
        IF @GrpStatus = 'P'
            SET @GrpMastNo = NULL;

        IF @GrpStatus = 'S' AND @GrpMastNo = @GrpNo AND @GrpNo > 0
            THROW 62008, 'A group cannot be its own parent.', 1;

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @GrpNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.GroupHeads                      -- [CHANGE 3]
                        WHERE GrpName = @GrpName AND ISNULL(DeleteStatus, 'N') = 'N'
                          AND SubscID = @SubscID AND GrpNo <> @GrpNo)
                THROW 62009, 'Another group already uses this name.', 1;

            UPDATE dbo.GroupHeads
               SET GrpName     = @GrpName,
                   GrpMastNo   = @GrpMastNo,
                   GrpStatus   = @GrpStatus,
                   BSStatus    = @BSStatus,
                   FAStatus    = @FAStatus,
                   SystemIP    = @SystemIP,
                   LockStatus  = @LockStatus,
                   LockCounter = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = ISNULL(@CreatedOn, @Now),
                   SubscID     = @SubscID
             WHERE GrpNo = @GrpNo AND ISNULL(DeleteStatus, 'N') = 'N';

            IF @@ROWCOUNT = 0
                THROW 62010, 'No group head found to update.', 1;

            SET @ID = @GrpNo;
            SET @Message = 'Group head updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.GroupHeads
                    WHERE GrpName = @GrpName AND ISNULL(DeleteStatus, 'N') = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'A group with this name already exists.';
            RETURN;
        END

        INSERT INTO dbo.GroupHeads
        (
            GrpName, GrpMastNo, GrpStatus, BSStatus, FAStatus,
            SystemIP, LockStatus, DeleteStatus, LockCounter,
            CreatedBy, CreatedOn, SubscID
        )
        VALUES
        (
            @GrpName, @GrpMastNo, @GrpStatus, @BSStatus, @FAStatus,
            @SystemIP, ISNULL(@LockStatus, 'N'), 'N', 1,
            @CreatedBy, ISNULL(@CreatedOn, @Now), @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Group head saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;                                                     -- [CHANGE 1]
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_AccountMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_AccountMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_AccountMaster;
GO
CREATE PROCEDURE dbo.SP_AccountMaster
    @ID           INT           OUTPUT,
    @AcNo         INT           = 0,
    @MCount       INT           = 0,
    @AcName       VARCHAR(100)  = NULL,
    @AcAlias      VARCHAR(100)  = NULL,
    @AcType       VARCHAR(20)   = NULL,
    @VendNo       INT           = NULL,
    @GroupHeadNo  INT           = 0,
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
        /*-------------------------------- DELETE --------------------------------*/
        IF @Status = 'Delete'
        BEGIN
            IF @AcNo <= 0
                THROW 62101, 'A valid AcNo is required to delete an account.', 1;

            -- Refuse to orphan a module that is configured to post here.
            IF EXISTS (SELECT 1 FROM dbo.AccountConfig WHERE AcNo = @AcNo)
                THROW 62102, 'This account is referenced by an account configuration. Reassign it first.', 1;

            IF EXISTS (SELECT 1 FROM dbo.TaxMaster
                        WHERE ISNULL(DeleteStatus, 'N') = 'N'
                          AND @AcNo IN (CGSTAcNo, SGSTAcNo, IGSTAcNo, CessAcNo, PSACNo))
                THROW 62103, 'This account is used by a tax rate. Reassign it first.', 1;

            UPDATE dbo.AccountsMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE AcNo = @AcNo;

            IF @@ROWCOUNT = 0
                THROW 62104, 'Account not found.', 1;

            SET @ID = @AcNo;
            SET @Message = 'Account deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        IF @AcName IS NULL OR LTRIM(RTRIM(@AcName)) = ''
            THROW 62105, 'Account name is required.', 1;
        IF @AcType IS NULL OR LTRIM(RTRIM(@AcType)) = ''
            THROW 62106, 'Account type is required.', 1;
        IF ISNULL(@GroupHeadNo, 0) <= 0
            THROW 62107, 'A group head is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.GroupHeads
                        WHERE GrpNo = @GroupHeadNo AND ISNULL(DeleteStatus, 'N') = 'N')
            THROW 62108, 'The selected group head does not exist.', 1;

        SET @AcName = LTRIM(RTRIM(@AcName));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @AcNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.AccountsMaster                  -- [CHANGE 3]
                        WHERE AcName = @AcName AND ISNULL(DeleteStatus, 'N') = 'N'
                          AND SubscID = @SubscID AND AcNo <> @AcNo)
                THROW 62109, 'Another account already uses this name.', 1;

            UPDATE dbo.AccountsMaster
               SET AcName      = @AcName,
                   AcAlias     = @AcAlias,
                   AcType      = @AcType,
                   VendNo      = @VendNo,
                   GroupHeadNo = @GroupHeadNo,
                   SystemIP    = @SystemIP,
                   LockStatus  = @LockStatus,
                   LockCounter = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = ISNULL(@CreatedOn, @Now)
             WHERE AcNo = @AcNo AND ISNULL(DeleteStatus, 'N') = 'N';

            IF @@ROWCOUNT = 0
                THROW 62110, 'No account found to update.', 1;

            SET @ID = @AcNo;
            SET @Message = 'Account updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.AccountsMaster
                    WHERE AcName = @AcName AND ISNULL(DeleteStatus, 'N') = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'An account with this name already exists.';
            RETURN;
        END

        INSERT INTO dbo.AccountsMaster
        (
            AcName, AcAlias, AcType, VendNo, GroupHeadNo, SystemIP,
            LockStatus, DeleteStatus, LockCounter, CreatedBy, CreatedOn, SubscID, TempNo
        )
        VALUES
        (
            @AcName, @AcAlias, @AcType, @VendNo, @GroupHeadNo, @SystemIP,
            ISNULL(@LockStatus, 'N'), 'N', 1, @CreatedBy, ISNULL(@CreatedOn, @Now), @SubscID, @MCount
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Account saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;                                                     -- [CHANGE 1]
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_TaxMaster
==============================================================================*/
IF OBJECT_ID('dbo.SP_TaxMaster', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_TaxMaster;
GO
CREATE PROCEDURE dbo.SP_TaxMaster
    @ID           INT           OUTPUT,
    @MTransNo     INT           = 0,
    @MCount       INT           = 0,
    @TaxCode      VARCHAR(20)   = NULL,
    @TaxP         NUMERIC(6,2)  = 0,
    @CessP        NUMERIC(6,2)  = NULL,
    @CGSTAcNo     INT           = NULL,
    @SGSTAcNo     INT           = NULL,
    @IGSTAcNo     INT           = NULL,
    @CessAcNo     INT           = NULL,
    @PSACNo       INT           = NULL,
    @UsedIn       VARCHAR(20)   = NULL,
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
        /*-------------------------------- DELETE --------------------------------*/
        IF @Status = 'Delete'
        BEGIN
            IF @MTransNo <= 0
                THROW 62201, 'A valid MTransNo is required to delete a tax rate.', 1;

            UPDATE dbo.TaxMaster
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @CreatedBy,
                   ModifiedOn   = @Now
             WHERE MTransNo = @MTransNo;

            IF @@ROWCOUNT = 0
                THROW 62202, 'Tax rate not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Tax rate deleted successfully.';
            RETURN;
        END

        /*------------------------------ VALIDATION ------------------------------*/
        IF @TaxCode IS NULL OR LTRIM(RTRIM(@TaxCode)) = ''
            THROW 62203, 'Tax code is required.', 1;
        IF @TaxP IS NULL OR @TaxP < 0
            THROW 62204, 'Tax percentage cannot be negative.', 1;
        IF @UsedIn IS NULL OR LTRIM(RTRIM(@UsedIn)) = ''
            SET @UsedIn = 'Both';

        SET @TaxCode = LTRIM(RTRIM(@TaxCode));

        /*-------------------------------- UPDATE --------------------------------*/
        IF @Status = 'Update' AND @MTransNo > 0
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.TaxMaster                       -- [CHANGE 3]
                        WHERE TaxCode = @TaxCode AND ISNULL(DeleteStatus, 'N') = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 62205, 'Another tax rate already uses this code.', 1;

            UPDATE dbo.TaxMaster
               SET TaxCode     = @TaxCode,
                   TaxP        = @TaxP,
                   CessP       = @CessP,
                   CGSTAcNo    = @CGSTAcNo,
                   SGSTAcNo    = @SGSTAcNo,
                   IGSTAcNo    = @IGSTAcNo,
                   CessAcNo    = @CessAcNo,
                   PSACNo      = @PSACNo,
                   UsedIn      = @UsedIn,
                   SystemIP    = @SystemIP,
                   LockStatus  = @LockStatus,
                   LockCounter = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy  = @CreatedBy,
                   ModifiedOn  = ISNULL(@CreatedOn, @Now),
                   SubscID     = @SubscID
             WHERE MTransNo = @MTransNo AND ISNULL(DeleteStatus, 'N') = 'N';

            IF @@ROWCOUNT = 0
                THROW 62206, 'No tax rate found to update.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Tax rate updated successfully.';
            RETURN;
        END

        /*-------------------------------- INSERT --------------------------------*/
        IF EXISTS (SELECT 1 FROM dbo.TaxMaster
                    WHERE TaxCode = @TaxCode AND ISNULL(DeleteStatus, 'N') = 'N' AND SubscID = @SubscID)
        BEGIN
            SET @ID = 0;
            SET @Message = 'A tax rate with this code already exists.';
            RETURN;
        END

        INSERT INTO dbo.TaxMaster
        (
            TaxCode, TaxP, CessP, CGSTAcNo, SGSTAcNo, IGSTAcNo, CessAcNo, PSACNo, UsedIn,
            SystemIP, LockStatus, LockCounter, CreatedBy, CreatedOn, DeleteStatus, SubscID
        )
        VALUES
        (
            @TaxCode, @TaxP, @CessP, @CGSTAcNo, @SGSTAcNo, @IGSTAcNo, @CessAcNo, @PSACNo, @UsedIn,
            @SystemIP, ISNULL(@LockStatus, 'N'), 1, @CreatedBy, ISNULL(@CreatedOn, @Now), 'N', @SubscID
        );

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Tax rate saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;                                                     -- [CHANGE 1]
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO


/*==============================================================================
  SP_AccountConfig
  ------------------------------------------------------------------------------
  Upserts on (ModuleName, ACCType, SubscID). LMSAPI did this with EF read-then-
  write from the controller, which races two concurrent saves into a duplicate;
  doing it in one statement here lets UQ_AccountConfig do its job.
==============================================================================*/
IF OBJECT_ID('dbo.SP_AccountConfig', 'P') IS NOT NULL DROP PROCEDURE dbo.SP_AccountConfig;
GO
CREATE PROCEDURE dbo.SP_AccountConfig
    @ID         INT           OUTPUT,
    @MTransNo   INT           = 0,
    @ModuleName VARCHAR(50)   = NULL,
    @ACCType    VARCHAR(30)   = NULL,
    @AcNo       INT           = NULL,
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
                THROW 62301, 'A valid MTransNo is required to delete a configuration.', 1;

            DELETE FROM dbo.AccountConfig WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

            IF @@ROWCOUNT = 0
                THROW 62302, 'Account configuration not found.', 1;

            SET @ID = @MTransNo;
            SET @Message = 'Account configuration deleted successfully.';
            RETURN;
        END

        IF @ModuleName IS NULL OR LTRIM(RTRIM(@ModuleName)) = ''
            THROW 62303, 'Module name is required.', 1;
        IF @ACCType IS NULL OR LTRIM(RTRIM(@ACCType)) = ''
            THROW 62304, 'Account type is required.', 1;
        IF ISNULL(@AcNo, 0) <= 0
            THROW 62305, 'A ledger account is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.AccountsMaster
                        WHERE AcNo = @AcNo AND ISNULL(DeleteStatus, 'N') = 'N')
            THROW 62306, 'The selected ledger account does not exist.', 1;

        SET @ModuleName = LTRIM(RTRIM(@ModuleName));
        SET @ACCType    = LTRIM(RTRIM(@ACCType));

        DECLARE @Existing INT;

        SELECT @Existing = MTransNo
          FROM dbo.AccountConfig
         WHERE ModuleName = @ModuleName
           AND ACCType    = @ACCType
           AND ISNULL(SubscID, -1) = ISNULL(@SubscID, -1);

        IF @Existing IS NOT NULL
        BEGIN
            UPDATE dbo.AccountConfig SET AcNo = @AcNo WHERE MTransNo = @Existing;

            SET @ID = @Existing;
            SET @Message = 'Account configuration updated successfully.';
            RETURN;
        END

        INSERT INTO dbo.AccountConfig (ModuleName, ACCType, AcNo, SubscID)
        VALUES (@ModuleName, @ACCType, @AcNo, @SubscID);

        SET @ID = SCOPE_IDENTITY();
        SET @Message = 'Account configuration saved successfully.';
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

PRINT '06_AccountingProcedures.sql complete — 4 procedures ready.';
GO
