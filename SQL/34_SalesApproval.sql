/*==============================================================================
  34. SALES APPROVAL  -  approve / reject for Quotation and Order Acceptance

  LockStatus on QtnMst and OAMst:
      N  draft (an EXECUTIVE's save)
      S  submitted, waiting for approval
      Y  approved
      R  rejected   <- new; the reason is kept in RejectReason

  Adds RejectReason / RejectedBy / RejectedOn to both headers, and one
  procedure per document:
      SP_QtnApproval   @Status 'Approve' | 'Reject'
      SP_OAApproval    @Status 'Approve' | 'Reject'

  Rules (both documents):
    * only the live, current revision (DeleteStatus 'N', DocAmdStatus 'N')
    * an EXECUTIVE can neither approve nor reject
    * Approve  - refused when already approved; clears any earlier rejection
    * Reject   - reason required; refused when approved (unlock it first)
                 or already rejected
  A rejected document stays editable, so the author can correct it and
  submit again; the reason stays on the row until it is approved.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  COLUMNS
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.QtnMst', 'RejectReason') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectReason VARCHAR(250) NULL;
IF COL_LENGTH('dbo.QtnMst', 'RejectedBy') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectedBy INT NULL;
IF COL_LENGTH('dbo.QtnMst', 'RejectedOn') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectedOn DATETIME2 NULL;

IF COL_LENGTH('dbo.OAMst', 'RejectReason') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectReason VARCHAR(250) NULL;
IF COL_LENGTH('dbo.OAMst', 'RejectedBy') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectedBy INT NULL;
IF COL_LENGTH('dbo.OAMst', 'RejectedOn') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectedOn DATETIME2 NULL;
GO

/*------------------------------------------------------------------------------
  SP_QtnApproval
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_QtnApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnApproval
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @RejectReason   VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Approve' | 'Reject'
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @CurLock CHAR(1), @CurAmd CHAR(1), @UserType VARCHAR(50);

    SELECT @CurLock = ISNULL(LockStatus, 'N'), @CurAmd = ISNULL(DocAmdStatus, 'N')
      FROM dbo.QtnMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @MTransNo * 0 + @ModifiedBy;

    SET @RejectReason = NULLIF(LTRIM(RTRIM(ISNULL(@RejectReason, ''))), '');

    IF @CurLock IS NULL
        SET @Message = N'Quotation not found.';
    ELSE IF @CurAmd = 'Y'
        SET @Message = N'This is a superseded revision. Only the current revision can be approved or rejected.';
    ELSE IF @UserType = 'EXECUTIVE'
        SET @Message = N'An executive cannot approve or reject a quotation.';
    ELSE IF @Status = 'Approve'
    BEGIN
        IF @CurLock = 'Y'
            SET @Message = N'Quotation is already approved.';
        ELSE
        BEGIN
            UPDATE dbo.QtnMst
               SET LockStatus   = 'Y',
                   RejectReason = NULL,
                   RejectedBy   = NULL,
                   RejectedOn   = NULL,
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Quotation approved successfully.'; END
        END
    END
    ELSE IF @Status = 'Reject'
    BEGIN
        IF @RejectReason IS NULL
            SET @Message = N'Rejection reason is required.';
        ELSE IF @CurLock = 'Y'
            SET @Message = N'Quotation is approved. Unlock it before rejecting.';
        ELSE IF @CurLock = 'R'
            SET @Message = N'Quotation is already rejected.';
        ELSE
        BEGIN
            UPDATE dbo.QtnMst
               SET LockStatus   = 'R',
                   RejectReason = @RejectReason,
                   RejectedBy   = @ModifiedBy,
                   RejectedOn   = GETDATE(),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Quotation rejected.'; END
        END
    END
    ELSE
        SET @Message = N'Invalid status. Use Approve or Reject.';
END
GO

/*------------------------------------------------------------------------------
  SP_OAApproval
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_OAApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_OAApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_OAApproval
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @RejectReason   VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Approve' | 'Reject'
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @CurLock CHAR(1), @CurAmd CHAR(1), @UserType VARCHAR(50);

    SELECT @CurLock = ISNULL(LockStatus, 'N'), @CurAmd = ISNULL(DocAmdStatus, 'N')
      FROM dbo.OAMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @ModifiedBy;

    SET @RejectReason = NULLIF(LTRIM(RTRIM(ISNULL(@RejectReason, ''))), '');

    IF @CurLock IS NULL
        SET @Message = N'Order Acceptance not found.';
    ELSE IF @CurAmd = 'Y'
        SET @Message = N'This is a superseded revision. Only the current revision can be approved or rejected.';
    ELSE IF @UserType = 'EXECUTIVE'
        SET @Message = N'An executive cannot approve or reject an order acceptance.';
    ELSE IF @Status = 'Approve'
    BEGIN
        IF @CurLock = 'Y'
            SET @Message = N'Order Acceptance is already approved.';
        ELSE
        BEGIN
            UPDATE dbo.OAMst
               SET LockStatus   = 'Y',
                   RejectReason = NULL,
                   RejectedBy   = NULL,
                   RejectedOn   = NULL,
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Order Acceptance approved successfully.'; END
        END
    END
    ELSE IF @Status = 'Reject'
    BEGIN
        IF @RejectReason IS NULL
            SET @Message = N'Rejection reason is required.';
        ELSE IF @CurLock = 'Y'
            SET @Message = N'Order Acceptance is approved. Unlock it before rejecting.';
        ELSE IF @CurLock = 'R'
            SET @Message = N'Order Acceptance is already rejected.';
        ELSE
        BEGIN
            UPDATE dbo.OAMst
               SET LockStatus   = 'R',
                   RejectReason = @RejectReason,
                   RejectedBy   = @ModifiedBy,
                   RejectedOn   = GETDATE(),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Order Acceptance rejected.'; END
        END
    END
    ELSE
        SET @Message = N'Invalid status. Use Approve or Reject.';
END
GO
