/*==============================================================================
  66. HEAT NO SAVE - piece reference numbers no longer time out

  Problem: saving a heat line with a Ref No From / To range timed out
  ("Execution Timeout Expired"). Measured on 200 numbers: 51 seconds; the page
  gives up at about 30. Two causes:
    1. SP_HeatNoDtl inserted the numbers one row at a time in a WHILE loop, so
       the audit trigger on HeatRefNo ran once per number.
    2. SP_AuditLog_Capture (the audit engine) rescans its working set for every
       row, so its time grows with the square of the row count.

  This script:
    1. Replaces the loop with ONE set-based insert (same rows, same rules, same
       10,000-number limit).
    2. Switches the per-row audit OFF for dbo.HeatRefNo only (AuditConfig). The
       heat document (HeatNoMst) and heat lines (HeatNoDtl) stay audited.

  Measured after the change: 10,000 numbers in about 2 seconds.

  NOT fixed here (separate job, affects every table): SP_AuditLog_Capture is
  slow for any save that touches many rows at once. See the hand-over note.

  CHECK BEFORE RUNNING: step 1 re-creates SP_HeatNoDtl in full, from the copy in
  the local database. If your live SP_HeatNoDtl was changed after SQL 32, compare
  first (or apply only the loop replacement by hand).

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 66_HeatRefNoSpeed.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO


ALTER PROCEDURE dbo.SP_HeatNoDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @HeatNo         VARCHAR(50),
    @TCNo           VARCHAR(30),
    @TCDate         DATE,
    @TCFile         VARCHAR(100),
    @Qty            NUMERIC(12,4),
    @ItemRemarks    VARCHAR(500),
    @RefFrom        INT,
    @RefTo          INT,
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @HdrQty NUMERIC(12,6), @HdrLocked CHAR(1), @Other NUMERIC(14,4),
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250), @Unit VARCHAR(10),
                @GradeNo INT, @SlNo NUMERIC(12,2), @NewID INT, @i INT,
                @ClashFrom INT, @ClashTo INT, @ClashHeat VARCHAR(50);

        SELECT @HdrQty = Qty, @HdrLocked = LockStatus
          FROM dbo.HeatNoMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @HdrQty IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @HdrLocked = 'Y'
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document is approved. Unlock it before changing heat numbers.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'All heat numbers deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active heat numbers found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE HTransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @TransSlNo; SET @Message = 'Heat number deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          TCFile: the API has stored the uploaded certificate; record where.
        ------------------------------------------------------------------*/
        IF (@Status = 'TCFile')
        BEGIN
            SET @TCFile = NULLIF(LTRIM(RTRIM(ISNULL(@TCFile, ''))), '');

            IF @TCFile IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'No test certificate file given.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.HeatNoDtl
               SET TCFile = @TCFile, SystemIP = @SystemIP,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Test certificate uploaded successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Heat line not found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update
        ------------------------------------------------------------------*/
        SET @HeatNo = NULLIF(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), '');

        IF @HeatNo IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat number is required.';
            COMMIT TRANSACTION; RETURN;
        END

        IF ISNULL(@Qty, 0) <= 0
        BEGIN
            SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
               @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
               @Unit     = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos'), 10)
          FROM dbo.PartMaster
         WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

        IF @PartCode IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Part not found.';
            COMMIT TRANSACTION; RETURN;
        END

        -- The same heat number cannot describe the same part twice.
        IF EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                      AND HeatNo = @HeatNo AND PartNo = @PartNo
                      AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo))
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat number ' + @HeatNo + ' has already been recorded for this part.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @Other = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
           AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo);

        IF @Other + @Qty > @HdrQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat quantities would total ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @Other + @Qty)) +
                           ', more than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @HdrQty)) + ' on the document.';
            COMMIT TRANSACTION; RETURN;
        END

        /*  A piece reference number is issued once. Ranges within this same
            document are laid out end to end by the screen and re-saved in
            turn, so only OTHER live documents are checked.  */
        IF ISNULL(@RefFrom, 0) > 0 AND ISNULL(@RefTo, 0) >= @RefFrom
        BEGIN
            DECLARE @ClashLine INT;

            SELECT TOP 1 @ClashLine = r.HTransSlNo
              FROM dbo.HeatRefNo r
             WHERE r.SubscID = @SubscID AND r.DeleteStatus = 'N'
               AND r.MTransNo <> @MTransNo
               AND r.RefNo BETWEEN @RefFrom AND @RefTo
             ORDER BY r.RefNo;

            -- Report the whole range the clashing heat line holds.
            IF @ClashLine IS NOT NULL
                SELECT @ClashFrom = MIN(r.RefNo), @ClashTo = MAX(r.RefNo),
                       @ClashHeat = (SELECT HeatNo FROM dbo.HeatNoDtl WHERE TransSlNo = @ClashLine)
                  FROM dbo.HeatRefNo r
                 WHERE r.HTransSlNo = @ClashLine AND r.DeleteStatus = 'N';

            IF @ClashFrom IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Ref No ' + CONVERT(VARCHAR(20), @RefFrom) + '-' + CONVERT(VARCHAR(20), @RefTo) +
                               ' overlaps numbers already issued (' + CONVERT(VARCHAR(20), @ClashFrom) + '-' +
                               CONVERT(VARCHAR(20), @ClashTo) + ', heat ' + ISNULL(@ClashHeat, '?') + ').';
                COMMIT TRANSACTION; RETURN;
            END
        END

        SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
         WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.HeatNoDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.HeatNoDtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, GradeNo, Grade,
             ItemRemarks, Qty, Unit, HeatNo, TCNo, TCDate, TCFile,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
             LTRIM(RTRIM(@ItemRemarks)), @Qty, @Unit, @HeatNo,
             LTRIM(RTRIM(@TCNo)), @TCDate, LTRIM(RTRIM(@TCFile)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @NewID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Heat number saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   ItemRemarks = LTRIM(RTRIM(@ItemRemarks)), Qty = @Qty, Unit = @Unit,
                   HeatNo = @HeatNo, TCNo = LTRIM(RTRIM(@TCNo)), TCDate = @TCDate,
                   TCFile = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@TCFile, ''))), ''), TCFile),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @NewID = @TransSlNo;
            SET @Message = 'Heat number updated successfully.';
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Piece serial numbers: rewritten whole, so an edited range replaces
          the old one instead of merging with it.
        ------------------------------------------------------------------*/
        DELETE FROM dbo.HeatRefNo
         WHERE MTransNo = @MTransNo AND HTransSlNo = @NewID AND SubscID = @SubscID;

        IF ISNULL(@RefFrom, 0) > 0 AND ISNULL(@RefTo, 0) >= @RefFrom
        BEGIN
            IF (@RefTo - @RefFrom) > 10000
            BEGIN
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                SET @ID = 0;
                SET @Message = 'That reference range covers more than 10,000 pieces. Split it across heat lines.';
                RETURN;
            END

            -- One set-based insert (was a WHILE loop, one row at a time).
            ;WITH n AS
            (
                SELECT TOP (@RefTo - @RefFrom + 1)
                       @RefFrom + ROW_NUMBER() OVER (ORDER BY (SELECT 1)) - 1 AS RefNo
                  FROM sys.all_objects a CROSS JOIN sys.all_objects b
            )
            INSERT INTO dbo.HeatRefNo
            (MTransNo, HTransSlNo, RefNo, PartNo, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT @MTransNo, @NewID, n.RefNo, @PartNo, @SystemIP, @CreatedBy, GETDATE(),
                   'N', 'N', 1, @SubscID
              FROM n;
        END

        SET @ID = @NewID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

-- 2. No per-row audit for the generated piece reference numbers.
IF EXISTS (SELECT 1 FROM dbo.AuditConfig WHERE TableName = 'HeatRefNo')
    UPDATE dbo.AuditConfig SET IsActive = 'N' WHERE TableName = 'HeatRefNo';
GO

PRINT '66_HeatRefNoSpeed applied.';
GO
