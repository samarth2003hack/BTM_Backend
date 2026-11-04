/*==============================================================================
  32. HEAT NO PAGE  -  what the live Heat No. Entry screen needs

  HeatNo.html has moved off its in-page mock data and onto HeatNoController
  (/api/HeatNo/...). Tables HeatNoMst / HeatNoDtl / HeatRefNo already exist
  (13_), and HeatNoDtl.TCFile already holds the test-certificate path, so there
  is NO NEW COLUMN. What changes:

    1. SP_HeatNoMst  - the page's three-state workflow
         LockStatus  N = draft (an EXECUTIVE's own)
                     S = submitted for approval
                     Y = approved              (DocStatus 'A')
       * Insert / Update take N or S. Y is only ever set by 'Approve' / 'Lock'.
         Updating an approved document therefore re-submits it (S) - the page
         re-approves it after the heat lines have been rewritten, because
         SP_HeatNoDtl refuses to touch the lines of an approved document.
       * New Status 'Approve': S -> Y only, needs at least one heat line, and
         an EXECUTIVE user may not approve.
       * 'Unlock' now returns an approved document to S (submitted), not N.
       * An EXECUTIVE may not change or delete an approved document.
       * One heat document per GRN + part (the page never offers a pair that
         already has one; the procedure now refuses it too).
       * The document quantity may not exceed what was received of that part
         on the GRN.
    2. SP_HeatNoDtl
       * A piece reference range may not overlap a range already issued on
         another live heat document (same subscription).
       * New Status 'TCFile': records the uploaded test certificate's path on
         one heat line (the file itself is written by the API under
         wwwroot/Uploads/HeatTC/{MTransNo}/).
       Everything else is unchanged: heat qty <= document qty, one heat number
       per part, 10,000-piece range limit, ranges rewritten whole.
    3. Index on HeatRefNo (SubscID, DeleteStatus, RefNo) for the "last
       reference number issued" preview and the overlap check.

  The procedures are shadowed by master's SP_ prefix, so CREATE OR ALTER
  fails: each is created as a stub if missing, then ALTERed with its full body.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SP_HeatNoMst
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoMst', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_HeatNoMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @Qty            NUMERIC(12,6),
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
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @TargetDocYear INT, @TargetDocNo INT,
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250),
                @Unit VARCHAR(20), @GradeNo INT, @DtlQty NUMERIC(14,4),
                @RecQty NUMERIC(14,4), @CurLock CHAR(1), @UserType VARCHAR(50),
                @OtherDocNo INT;

        -- Who is acting: an EXECUTIVE saves drafts, never approves, and never
        -- touches an approved document.
        SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
          FROM dbo.UserMaster
         WHERE MTransNo = ISNULL(@ModifiedBy, @CreatedBy);

        -- Draft or submitted only; approval has its own status.
        SET @LockStatus = CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'S' END;

        IF @Status IN ('Update', 'Delete', 'Lock', 'Unlock', 'Approve')
            SELECT @CurLock = LockStatus FROM dbo.HeatNoMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @Status IN ('Update', 'Delete') AND @CurLock = 'Y' AND @UserType = 'EXECUTIVE'
        BEGIN
            SET @ID = 0;
            SET @Message = 'This heat no entry is approved. Only a non-executive user can change or delete it.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @TargetDocYear = DocYear, @TargetDocNo = DocNo
              FROM dbo.GRNMst
             WHERE MTransNo = @TargetTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @TargetDocYear IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'GRN not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @TargetTransNo AND PartNo = @PartNo
                              AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'That part was not received on this GRN.';
                COMMIT TRANSACTION; RETURN;
            END

            -- The heats describe what arrived, so the document cannot cover
            -- more than the GRN received of that part.
            SELECT @RecQty = ISNULL(SUM(RecQty), 0) FROM dbo.GRNDtl
             WHERE MTransNo = @TargetTransNo AND PartNo = @PartNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF ISNULL(@Qty, 0) > @RecQty
            BEGIN
                SET @ID = 0;
                SET @Message = 'Quantity ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, ISNULL(@Qty, 0))) +
                               ' is more than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @RecQty)) +
                               ' received of this part on the GRN.';
                COMMIT TRANSACTION; RETURN;
            END

            -- One heat document per GRN + part.
            SELECT TOP 1 @OtherDocNo = DocNo FROM dbo.HeatNoMst
             WHERE TargetTransNo = @TargetTransNo AND PartNo = @PartNo
               AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND (@Status = 'Insert' OR MTransNo <> @MTransNo);

            IF @OtherDocNo IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'This GRN and part already have a Heat No entry (Sl No ' +
                               CONVERT(VARCHAR(20), @OtherDocNo) + ').';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
                   @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
                   @Unit     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos')
              FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;

            SELECT @MCount = COUNT(MTransNo) FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Heat document already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.HeatNoMst
                (DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo,
                 PartNo, PartCode, PartDescription, GradeNo, Grade, Qty, Unit,
                 DocNotes, DocStatus, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                 @TargetTransNo, @TargetDocYear, @TargetDocNo,
                 @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
                 ISNULL(@Qty, 0), @Unit, LTRIM(RTRIM(@DocNotes)), 'N',
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                 'N', NULL, @LockStatus, 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Heat document saved successfully.';
            END
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            /*  The header quantity is what the heat lines have to add up to,
                so it cannot be pulled below what has already been entered.  */
            SELECT @DtlQty = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF ISNULL(@Qty, 0) < @DtlQty
            BEGIN
                SET @ID = 0;
                SET @Message = 'Quantity cannot be less than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @DtlQty)) +
                               ' already entered against heat numbers.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Saving an edit (re)submits it: an approved document goes back
            -- to S so its heat lines can be rewritten, and is re-approved.
            UPDATE dbo.HeatNoMst
               SET DocDate = @DocDate, TargetTransNo = @TargetTransNo,
                   TargetDocYear = @TargetDocYear, TargetDocNo = @TargetDocNo,
                   PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   Qty = ISNULL(@Qty, 0), Unit = @Unit,
                   DocNotes = LTRIM(RTRIM(@DocNotes)),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockStatus = @LockStatus,
                   DocStatus = 'N',
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Heat document updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatNoDtl SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.HeatRefNo SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'Heat document deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status IN ('Lock', 'Unlock', 'Approve'))
        BEGIN
            IF @CurLock IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status IN ('Lock', 'Approve') AND @UserType = 'EXECUTIVE'
            BEGIN
                SET @ID = 0; SET @Message = 'An executive user cannot approve a heat no entry.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status = 'Approve' AND @CurLock <> 'S'
            BEGIN
                SET @ID = 0;
                SET @Message = CASE WHEN @CurLock = 'Y'
                                    THEN 'This heat no entry is already approved.'
                                    ELSE 'Only a submitted heat no entry can be approved. Submit the draft first.' END;
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status IN ('Lock', 'Approve') AND NOT EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                                                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                                                   AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Enter at least one heat number before approving.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Unlocking an approved document puts it back up for approval.
            UPDATE dbo.HeatNoMst
               SET LockStatus = CASE WHEN @Status = 'Unlock' THEN 'S' ELSE 'Y' END,
                   DocStatus  = CASE WHEN @Status = 'Unlock' THEN 'N' ELSE 'A' END,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = 'Heat document ' + CASE WHEN @Status = 'Unlock' THEN 'unlocked' ELSE 'approved' END + ' successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  2. SP_HeatNoDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoDtl', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoDtl AS BEGIN SET NOCOUNT ON; END');
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

            SET @i = @RefFrom;
            WHILE @i <= @RefTo
            BEGIN
                INSERT INTO dbo.HeatRefNo
                (MTransNo, HTransSlNo, RefNo, PartNo, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, LockStatus, LockCounter, SubscID)
                VALUES
                (@MTransNo, @NewID, @i, @PartNo, @SystemIP, @CreatedBy, GETDATE(),
                 'N', 'N', 1, @SubscID);

                SET @i = @i + 1;
            END
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

/*------------------------------------------------------------------------------
  3. INDEX - last reference number issued / overlap check
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = N'IX_HeatRefNo_SubscID_RefNo' AND object_id = OBJECT_ID(N'dbo.HeatRefNo'))
    CREATE NONCLUSTERED INDEX IX_HeatRefNo_SubscID_RefNo
        ON dbo.HeatRefNo (SubscID, DeleteStatus, RefNo)
        INCLUDE (MTransNo, HTransSlNo);
GO

PRINT '32_HeatNoPage.sql applied.';
GO
