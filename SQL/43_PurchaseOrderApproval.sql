/*==============================================================================
  43. PURCHASE ORDER APPROVAL  -  approve / reject, same contract as 34 (OA)

  LockStatus on POMst:
      N  draft
      Y  approved (DocStatus 'A')  - the only state goods can be received on
      R  rejected   <- new; the reason is kept in RejectReason

  Adds RejectReason / RejectedBy / RejectedOn to POMst, and
      SP_POApproval    @Status 'Approve' | 'Reject'

  Rules:
    * only the live, current revision (DeleteStatus 'N', DocAmdStatus 'N')
    * an EXECUTIVE can neither approve nor reject
    * Approve  - refused when already approved or when the order has no
                 lines; recalculates totals, sets DocStatus 'A', clears any
                 earlier rejection (same as SP_POMst 'Lock')
    * Reject   - reason required; refused when approved (unlock it first)
                 or already rejected
  A rejected order stays editable (SP_POMst / SP_PODtl only block 'Y'), so
  the author can correct it; the reason stays on the row until approval.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run. Run BEFORE deploying the API: POMst is read
  as whole rows, so the new columns must exist.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  COLUMNS
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.POMst', 'RejectReason') IS NULL
    ALTER TABLE dbo.POMst ADD RejectReason VARCHAR(250) NULL;
IF COL_LENGTH('dbo.POMst', 'RejectedBy') IS NULL
    ALTER TABLE dbo.POMst ADD RejectedBy INT NULL;
IF COL_LENGTH('dbo.POMst', 'RejectedOn') IS NULL
    ALTER TABLE dbo.POMst ADD RejectedOn DATETIME2 NULL;
GO

/*------------------------------------------------------------------------------
  SP_POApproval
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_POApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POApproval
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
      FROM dbo.POMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @ModifiedBy;

    SET @RejectReason = NULLIF(LTRIM(RTRIM(ISNULL(@RejectReason, ''))), '');

    IF @CurLock IS NULL
        SET @Message = N'Purchase Order not found.';
    ELSE IF @CurAmd = 'Y'
        SET @Message = N'This is a superseded revision. Only the current revision can be approved or rejected.';
    ELSE IF @UserType = 'EXECUTIVE'
        SET @Message = N'An executive cannot approve or reject a purchase order.';
    ELSE IF @Status = 'Approve'
    BEGIN
        IF @CurLock = 'Y'
            SET @Message = N'Purchase Order is already approved.';
        ELSE IF NOT EXISTS (SELECT 1 FROM dbo.PODtl
                             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            SET @Message = N'Add at least one line before approving.';
        ELSE
        BEGIN
            EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;

            UPDATE dbo.POMst
               SET LockStatus   = 'Y',
                   DocStatus    = 'A',
                   RejectReason = NULL,
                   RejectedBy   = NULL,
                   RejectedOn   = NULL,
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Purchase Order approved successfully.'; END
        END
    END
    ELSE IF @Status = 'Reject'
    BEGIN
        IF @RejectReason IS NULL
            SET @Message = N'Rejection reason is required.';
        ELSE IF @CurLock = 'Y'
            SET @Message = N'Purchase Order is approved. Unlock it before rejecting.';
        ELSE IF @CurLock = 'R'
            SET @Message = N'Purchase Order is already rejected.';
        ELSE
        BEGIN
            UPDATE dbo.POMst
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
            BEGIN SET @ID = @MTransNo; SET @Message = N'Purchase Order rejected.'; END
        END
    END
    ELSE
        SET @Message = N'Invalid status. Use Approve or Reject.';
END
GO
