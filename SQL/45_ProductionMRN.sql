/*==============================================================================
  45. PRODUCTION MRN  -  status changes for MRNMst / MRNDtl

  SQL 44 created the MRN tables and the save procedures (SP_MRNMst allocates
  the DocNo, SP_MRNDtl writes one line). This script adds what the two MRN
  screens need on top of them:

      FN_MRNIssueCount(@MTransNo)     live Store Issues raised against an MRN
      FN_MRNLineIssued(@TransSlNo)    1 when a live Store Issue line draws on
                                      the MRN line (StoreIssueDtl.LinkNo)
      SP_MRNStatus                    Approve / Unlock / Delete / DtlDelete

  LockStatus on MRNMst (as on the legacy screen):
      N  executive's draft
      S  submitted for approval
      Y  approved  -  what Store Issue reads

  Rules SP_MRNStatus enforces (@Message carries the refusal, @ID = 0):
      Approve    S -> Y, by a user who is not an EXECUTIVE, MRN has lines
      Unlock     Y -> S, same user rule, nothing issued yet
      Delete     soft delete header + lines with a reason; ADMIN always,
                 otherwise the creator while not approved, or a non-executive
                 once approved; refused once anything has been issued
      DtlDelete  soft delete one line; refused once that line was issued

  Used by MRNController (MRNType 'PROD') and MRNGController ('GEN').

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures.
  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  FN_MRNIssueCount
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_MRNIssueCount (@MTransNo INT)
RETURNS INT
AS
BEGIN
    RETURN (SELECT COUNT(*)
              FROM dbo.StoreIssueMst
             WHERE TargetTransNo = @MTransNo
               AND DeleteStatus  = 'N');
END
GO

/*------------------------------------------------------------------------------
  FN_MRNLineIssued
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_MRNLineIssued (@TransSlNo INT)
RETURNS BIT
AS
BEGIN
    IF EXISTS (SELECT 1
                 FROM dbo.StoreIssueDtl D
                 JOIN dbo.StoreIssueMst M ON M.MTransNo = D.MTransNo
                WHERE D.LinkNo       = @TransSlNo
                  AND D.DeleteStatus = 'N'
                  AND M.DeleteStatus = 'N')
        RETURN 1;
    RETURN 0;
END
GO

/*------------------------------------------------------------------------------
  SP_MRNStatus
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MRNStatus', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_MRNStatus AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_MRNStatus
    @ID         INT OUTPUT,
    @Action     VARCHAR(20),          -- Approve | Unlock | Delete | DtlDelete
    @MRNType    VARCHAR(30),
    @MTransNo   INT,
    @TransSlNo  INT = 0,              -- DtlDelete only
    @UserNo     INT,
    @Reason     VARCHAR(250) = NULL,  -- Delete only
    @SystemIP   VARCHAR(20) = NULL,
    @SubscID    INT,
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Now DATETIME = GETDATE();
    DECLARE @Lock CHAR(1), @CreatedBy INT, @UserType VARCHAR(50);

    -- For DtlDelete the header comes from the line.
    IF @Action = 'DtlDelete' AND ISNULL(@MTransNo, 0) = 0
        SELECT @MTransNo = MTransNo FROM dbo.MRNDtl WHERE TransSlNo = @TransSlNo;

    SELECT @Lock = LockStatus, @CreatedBy = CreatedBy
      FROM dbo.MRNMst
     WHERE MTransNo = @MTransNo AND MRNType = @MRNType
       AND SubscID = @SubscID AND DeleteStatus = 'N';

    IF @Lock IS NULL
    BEGIN
        SET @Message = N'MRN not found, or it has been deleted.';
        RETURN;
    END

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @UserNo;

    IF @UserType IS NULL
    BEGIN
        SET @Message = N'Invalid user.';
        RETURN;
    END

    DECLARE @Issued INT = dbo.FN_MRNIssueCount(@MTransNo);

    IF @Action = 'Approve'
    BEGIN
        IF @UserType = 'EXECUTIVE'
            SET @Message = N'An executive cannot approve an MRN.';
        ELSE IF @Lock = 'Y'
            SET @Message = N'This MRN is already approved.';
        ELSE IF @Lock <> 'S'
            SET @Message = N'Only a submitted MRN can be approved - this one is still a draft.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MRNDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N')
            SET @Message = N'The MRN has no lines to approve.';
        IF @Message <> N'' RETURN;

        UPDATE dbo.MRNMst
           SET LockStatus = 'Y', LockCounter = LockCounter + 1,
               ModifiedBy = @UserNo, ModifiedOn = @Now, SystemIP = @SystemIP
         WHERE MTransNo = @MTransNo;

        SELECT @ID = @MTransNo, @Message = N'MRN approved.';
        RETURN;
    END

    IF @Action = 'Unlock'
    BEGIN
        IF @UserType = 'EXECUTIVE'
            SET @Message = N'An executive cannot unlock an approved MRN.';
        ELSE IF @Lock <> 'Y'
            SET @Message = N'Only an approved MRN can be unlocked.';
        ELSE IF @Issued > 0
            SET @Message = N'Material has already been issued against this MRN - it cannot be unlocked.';
        IF @Message <> N'' RETURN;

        UPDATE dbo.MRNMst
           SET LockStatus = 'S', LockCounter = LockCounter + 1,
               ModifiedBy = @UserNo, ModifiedOn = @Now, SystemIP = @SystemIP
         WHERE MTransNo = @MTransNo;

        SELECT @ID = @MTransNo, @Message = N'MRN unlocked - it is back to submitted.';
        RETURN;
    END

    IF @Action = 'Delete'
    BEGIN
        IF @Issued > 0
            SET @Message = N'Material has already been issued against this MRN - it cannot be deleted.';
        ELSE IF @UserType <> 'ADMIN'
             AND NOT ((@Lock <> 'Y' AND @CreatedBy = @UserNo)
                   OR (@Lock = 'Y' AND @UserType <> 'EXECUTIVE'))
            SET @Message = N'You cannot delete this MRN.';
        ELSE IF LTRIM(RTRIM(ISNULL(@Reason, ''))) = ''
            SET @Message = N'A delete reason is required.';
        IF @Message <> N'' RETURN;

        BEGIN TRAN;
            UPDATE dbo.MRNDtl
               SET DeleteStatus = 'Y', ModifiedBy = @UserNo, ModifiedOn = @Now
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.MRNMst
               SET DeleteStatus = 'Y', DeleteReason = LEFT(LTRIM(RTRIM(@Reason)), 250),
                   ModifiedBy = @UserNo, ModifiedOn = @Now, SystemIP = @SystemIP
             WHERE MTransNo = @MTransNo;
        COMMIT TRAN;

        SELECT @ID = @MTransNo, @Message = N'MRN deleted.';
        RETURN;
    END

    IF @Action = 'DtlDelete'
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.MRNDtl
                        WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo AND DeleteStatus = 'N')
            SET @Message = N'MRN line not found, or it has been deleted.';
        ELSE IF dbo.FN_MRNLineIssued(@TransSlNo) = 1
            SET @Message = N'Material has already been issued against this line - it cannot be removed.';
        ELSE IF @Lock = 'Y' AND @UserType = 'EXECUTIVE'
            SET @Message = N'An executive cannot change an approved MRN.';
        IF @Message <> N'' RETURN;

        UPDATE dbo.MRNDtl
           SET DeleteStatus = 'Y', ModifiedBy = @UserNo, ModifiedOn = @Now
         WHERE TransSlNo = @TransSlNo;

        SELECT @ID = @TransSlNo, @Message = N'Line removed.';
        RETURN;
    END

    SET @Message = N'Unknown action.';
END
GO
