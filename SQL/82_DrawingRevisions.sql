/* ============================================================================
   82_DrawingRevisions.sql  (idempotent)

   Drawing Register could not hold a new revision: SP_DrawingNos refused any
   second row with the same Drawing No, and the page locks Drawing No / Rev No
   when editing. So a changed customer drawing had to overwrite the old one.

   Now (normal revision control):
     * Drawing No + Rev No is the unique pair (one live row each).
     * Adding a row with an existing Drawing No adds a NEW REVISION; it must
       be above the highest revision on record.
     * RevStatus: 'C' = current (the highest live revision), 'S' = superseded.
       Part Master already shows the highest revision as the part's drawing.
     * Deleting the current revision makes the next-highest one current.
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
/* Marks the revisions of one drawing number: highest live = 'C', rest = 'S'. */
CREATE OR ALTER PROCEDURE dbo.SP_DrawingRevMark
    @DrawingNo VARCHAR(50),
    @SubscID   INT
AS
BEGIN
    SET NOCOUNT ON;
    ;WITH r AS (
        SELECT MTransNo, ROW_NUMBER() OVER (ORDER BY ISNULL(RevNo, 0) DESC, MTransNo DESC) AS rn
          FROM dbo.DrawingNos
         WHERE DrawingNo = @DrawingNo AND SubscID = @SubscID AND DeleteStatus = 'N')
    UPDATE d SET d.RevStatus = CASE WHEN r.rn = 1 THEN 'C' ELSE 'S' END
      FROM dbo.DrawingNos d JOIN r ON r.MTransNo = d.MTransNo;
END;
GO
CREATE OR ALTER PROCEDURE dbo.SP_DrawingNos
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

            -- SQL 82: the highest live revision left is the current one.
            DECLARE @DelNo VARCHAR(50) = (SELECT DrawingNo FROM dbo.DrawingNos WHERE MTransNo = @MTransNo);
            EXEC dbo.SP_DrawingRevMark @DelNo, @SubscID;

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
            -- SQL 82: Drawing No + Rev No is the unique pair.
            IF EXISTS (SELECT 1 FROM dbo.DrawingNos
                        WHERE DrawingNo = @DrawingNo AND ISNULL(RevNo, 0) = @RevNo AND DeleteStatus = 'N'
                          AND SubscID = @SubscID AND MTransNo <> @MTransNo)
                THROW 64105, 'Another row already holds this drawing number and revision.', 1;

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

            EXEC dbo.SP_DrawingRevMark @DrawingNo, @SubscID;
            SET @ID = @MTransNo;
            SET @Message = 'Drawing updated successfully.';
            RETURN;
        END

        -- SQL 82: a drawing number may hold several revisions. A new row is
        -- a new revision: it must be above the highest one on record, and it
        -- becomes the current revision (the older ones are Superseded).
        DECLARE @TopRev INT = (SELECT MAX(ISNULL(RevNo, 0)) FROM dbo.DrawingNos
                                WHERE DrawingNo = @DrawingNo AND DeleteStatus = 'N' AND SubscID = @SubscID);
        IF @TopRev IS NOT NULL AND @RevNo <= @TopRev
        BEGIN
            SET @ID = 0;
            SET @Message = CONCAT('Drawing ', @DrawingNo, ' is already at revision ', @TopRev,
                                  '. A new revision must be above ', @TopRev, '.');
            RETURN;
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
        EXEC dbo.SP_DrawingRevMark @DrawingNo, @SubscID;
        SET @Message = CASE WHEN @TopRev IS NULL THEN 'Drawing saved successfully.'
                            ELSE CONCAT('Revision ', @RevNo, ' of drawing ', @DrawingNo, ' saved. Older revisions are now Superseded.') END;
    END TRY
    BEGIN CATCH
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

/* Drawing No + Rev No unique (replaces the index that also keyed on type,
   group and part, which let the same number + revision be entered twice). */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DrawingNos_DrawingNo' AND object_id = OBJECT_ID('dbo.DrawingNos'))
    DROP INDEX UX_DrawingNos_DrawingNo ON dbo.DrawingNos;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DrawingNos_NoRev' AND object_id = OBJECT_ID('dbo.DrawingNos'))
    CREATE UNIQUE INDEX UX_DrawingNos_NoRev ON dbo.DrawingNos (SubscID, DrawingNo, RevNo) WHERE DeleteStatus = 'N';
GO

/* Mark the revisions already on record. */
DECLARE @n VARCHAR(50), @s INT;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT DISTINCT DrawingNo, SubscID FROM dbo.DrawingNos WHERE DeleteStatus = 'N';
OPEN c;
FETCH NEXT FROM c INTO @n, @s;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC dbo.SP_DrawingRevMark @n, @s;
    FETCH NEXT FROM c INTO @n, @s;
END;
CLOSE c; DEALLOCATE c;
GO
