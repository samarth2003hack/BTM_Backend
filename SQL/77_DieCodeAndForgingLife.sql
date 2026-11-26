/*==============================================================================
  77. DIE CODE, SEVERAL DIES PER PART, DIE LIFE ON THE FORGING STEP ONLY

  Why
    - A die had no number of its own: it was "the forged part + grade", so a
      part could have only one die and could never run on two hammers at once.
    - Die life counted the process qty of EVERY job card entry that named the
      die, so a die picked on two steps of a card counted twice.

  What this script does
    1. DieMst.DieCode (VARCHAR 30) — the die's own number, e.g. D-114-A.
       Unique per company among live dies (filtered unique index).
    2. dbo.FN_JobCardForgeStep(@JobCard) — the route step a job card forges
       on: the first step whose operation name contains "forg", else the last
       step (the same rule the planning module uses for "forged qty").
    3. SP_DieMst — new optional @DieCode (old callers still work):
         * a code already used by another live die is refused;
         * one part + grade may have several dies, as long as each has its own
           code; two dies of the same part + grade without codes are refused;
         * die usage = qty of FORGING-STEP entries only.
    4. SP_DieUsageRefresh — forging-step entries only.
    5. Recounts every live die once, so stored Used / Processed / Remaining
       follow the new rule.

  Effect on existing data: no row is deleted. Existing dies keep working with
  no code (screens show the part code until a code is entered). Stored usage
  can go DOWN for dies that were picked on non-forging steps.

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 77_DieCodeAndForgingLife.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

/*----------------------------------------------------------------------------
  1. DieMst.DieCode
----------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.DieMst', 'DieCode') IS NULL
    ALTER TABLE dbo.DieMst ADD DieCode VARCHAR(30) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DieMst_DieCode' AND object_id = OBJECT_ID('dbo.DieMst'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_DieMst_DieCode ON dbo.DieMst (SubscID, DieCode)
        WHERE DieCode IS NOT NULL AND DeleteStatus = 'N';
GO

/*----------------------------------------------------------------------------
  2. FN_JobCardForgeStep
----------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_JobCardForgeStep (@JobCard INT)
RETURNS INT
AS
BEGIN
    RETURN (
        SELECT ISNULL(MIN(CASE WHEN o.MasterName LIKE '%forg%' THEN rd.SlNo END), MAX(rd.SlNo))
          FROM dbo.JobCardMst j
          JOIN dbo.RoutingChartDtl rd ON rd.MTransNo = j.RouteNo AND ISNULL(rd.DeleteStatus, 'N') = 'N'
          LEFT JOIN dbo.OtherMaster o ON o.MTransNo = rd.Operation
         WHERE j.MTransNo = @JobCard);
END;
GO

/*----------------------------------------------------------------------------
  3. SP_DieMst
----------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE dbo.SP_DieMst
    @ID            INT            OUTPUT,
    @MTransNo      INT,
    @MCount        INT,
    @PartNo        INT,
    @GradeNo       INT,
    @DieType       VARCHAR(50),
    @MaxQty        NUMERIC(12, 2),
    @OpeningQty    NUMERIC(12, 2),
    @Remarks       VARCHAR(200),
    @LockStatus    CHAR(1),
    @CreatedBy     INT,
    @CreatedOn     DATETIME2,
    @ModifiedBy    INT,
    @ModifiedOn    DATETIME2,
    @DeleteReason  VARCHAR(250),
    @SubscID       INT,
    @Status        VARCHAR(20),
    @Message       NVARCHAR(200)  OUTPUT,
    @DieCode       VARCHAR(30)    = NULL      -- SQL 77; NULL on Update = keep the stored code
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Code VARCHAR(50), @CurCode VARCHAR(50), @Grade VARCHAR(50),
            @CurPart INT, @CurGrade INT, @CurDieCode VARCHAR(30), @Entries INT = 0,
            @Used NUMERIC(12,2) = 0,
            @Max  NUMERIC(12,2) = ISNULL(@MaxQty, 0),
            @Open NUMERIC(12,2) = ISNULL(@OpeningQty, 0),
            @NewCode VARCHAR(30) = NULLIF(UPPER(LTRIM(RTRIM(ISNULL(@DieCode, '')))), '');

    BEGIN TRY
        -- Current row + its live production (needed by Update and Delete).
        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @CurPart = d.PartNo, @CurGrade = d.GradeNo, @CurDieCode = d.DieCode,
                   @CurCode = ISNULL(NULLIF(d.DieCode, ''), ISNULL(p.PartCode, 'This die'))
              FROM dbo.DieMst d
              LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
             WHERE d.MTransNo = @MTransNo AND d.SubscID = @SubscID AND d.DeleteStatus = 'N';
            IF @CurPart IS NULL
            BEGIN SET @Message = N'Die not found.'; RETURN; END

            -- Die life counts forging-step entries only.
            SELECT @Entries = COUNT(*), @Used = ISNULL(SUM(e.Qty), 0)
              FROM dbo.JobCardEntry e
             WHERE e.DieNo = @MTransNo AND e.DeleteStatus = 'N'
               AND e.StepSlNo = dbo.FN_JobCardForgeStep(e.MTransNo);
        END

        -- On Update a missing code keeps the stored one.
        IF @Status = 'Update' AND @DieCode IS NULL SET @NewCode = @CurDieCode;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @Code = PartCode FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @Code IS NULL
            BEGIN SET @Message = N'Select the forged part from the Part Master list.'; RETURN; END

            IF ISNULL(@GradeNo, 0) <= 0
            BEGIN SET @Message = N'Grade is required.'; RETURN; END

            SELECT @Grade = Grade FROM dbo.MaterialGrade
             WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @Grade IS NULL
            BEGIN SET @Message = N'The selected grade is not a grade of ' + @Code + N'.'; RETURN; END

            IF LTRIM(RTRIM(ISNULL(@DieType, ''))) = ''
            BEGIN SET @Message = N'Die Type is required.'; RETURN; END

            IF @Max < 0 OR @Open < 0
            BEGIN SET @Message = N'Quantities cannot be negative.'; RETURN; END

            -- A die code belongs to one die.
            IF @NewCode IS NOT NULL AND EXISTS (
                   SELECT 1 FROM dbo.DieMst
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND DieCode = @NewCode
                      AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'Die Code ' + @NewCode + N' is already used by another die.'; RETURN; END

            -- Several dies per part + grade are allowed, each with its own code.
            IF EXISTS (SELECT 1 FROM dbo.DieMst
                        WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END
                          AND (@NewCode IS NULL OR NULLIF(DieCode, '') IS NULL))
            BEGIN
                SET @Message = N'A die for ' + @Code + N' in grade ' + @Grade + N' already exists. '
                             + N'To add another die for it, give both dies their own Die Code.';
                RETURN;
            END

            IF @Status = 'Insert' SET @Used = 0;

            -- A die with production recorded keeps its part and grade.
            IF @Status = 'Update' AND @Entries > 0 AND @CurPart <> @PartNo
            BEGIN SET @Message = @CurCode + N' has production recorded against it, so its forged part cannot be changed.'; RETURN; END
            IF @Status = 'Update' AND @Entries > 0 AND @CurGrade <> @GradeNo
            BEGIN SET @Message = @CurCode + N' has production recorded against it, so its grade cannot be changed.'; RETURN; END

            IF @Open + @Used > @Max
            BEGIN
                SET @Message = N'Processed quantity (' + FORMAT(@Open + @Used, '0.##') + N' = opening '
                             + FORMAT(@Open, '0.##') + N' + production ' + FORMAT(@Used, '0.##')
                             + N') cannot exceed the Maximum Process Qty (' + FORMAT(@Max, '0.##') + N').';
                RETURN;
            END
        END
        ELSE IF @Status = 'Delete' AND @Entries > 0
        BEGIN
            SET @Message = @CurCode + N' has production recorded against it. Mark it Inactive instead.';
            RETURN;
        END

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            INSERT INTO dbo.DieMst
                (PartNo, GradeNo, DieType, MaxQty, OpeningQty, UsedQty, ProcessQty, RemainingQty,
                 Remarks, LockStatus, LockCounter, CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID, DieCode)
            VALUES
                (@PartNo, @GradeNo, LTRIM(RTRIM(@DieType)), @Max, @Open, 0, @Open,
                 CASE WHEN @Max - @Open > 0 THEN @Max - @Open ELSE 0 END,
                 LTRIM(RTRIM(@Remarks)), ISNULL(NULLIF(@LockStatus, ''), 'Y'), 1,
                 @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, @SubscID, @NewCode);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Die saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.DieMst
               SET PartNo       = @PartNo,
                   GradeNo      = @GradeNo,
                   DieCode      = @NewCode,
                   DieType      = LTRIM(RTRIM(@DieType)),
                   MaxQty       = @Max,
                   OpeningQty   = @Open,
                   UsedQty      = @Used,
                   ProcessQty   = @Open + @Used,
                   RemainingQty = CASE WHEN @Max - (@Open + @Used) > 0 THEN @Max - (@Open + @Used) ELSE 0 END,
                   Remarks      = LTRIM(RTRIM(@Remarks)),
                   LockStatus   = ISNULL(NULLIF(@LockStatus, ''), LockStatus),
                   LockCounter  = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Die updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.DieMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Die deleted successfully.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END;
GO

/*----------------------------------------------------------------------------
  4. SP_DieUsageRefresh — forging-step entries only
----------------------------------------------------------------------------*/
CREATE OR ALTER PROCEDURE dbo.SP_DieUsageRefresh
    @DieNo INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE d
       SET UsedQty      = u.UsedQty,
           ProcessQty   = ISNULL(d.OpeningQty, 0) + u.UsedQty,
           RemainingQty = CASE WHEN ISNULL(d.MaxQty, 0) - ISNULL(d.OpeningQty, 0) - u.UsedQty > 0
                               THEN ISNULL(d.MaxQty, 0) - ISNULL(d.OpeningQty, 0) - u.UsedQty ELSE 0 END
      FROM dbo.DieMst d
     CROSS APPLY (SELECT ISNULL(SUM(e.Qty), 0) AS UsedQty
                    FROM dbo.JobCardEntry e
                   WHERE e.DieNo = d.MTransNo AND e.DeleteStatus = 'N'
                     AND e.StepSlNo = dbo.FN_JobCardForgeStep(e.MTransNo)) u
     WHERE d.MTransNo = @DieNo;
END;
GO

/*----------------------------------------------------------------------------
  5. Recount every live die once
----------------------------------------------------------------------------*/
DECLARE @d INT;
DECLARE dies CURSOR LOCAL FAST_FORWARD FOR SELECT MTransNo FROM dbo.DieMst WHERE DeleteStatus = 'N';
OPEN dies;
FETCH NEXT FROM dies INTO @d;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC dbo.SP_DieUsageRefresh @DieNo = @d;
    FETCH NEXT FROM dies INTO @d;
END
CLOSE dies;
DEALLOCATE dies;

SELECT d.MTransNo AS dieNo, ISNULL(d.DieCode, '(no code)') AS dieCode, p.PartCode AS forgedPart,
       d.MaxQty, d.OpeningQty, d.UsedQty, d.ProcessQty, d.RemainingQty
  FROM dbo.DieMst d LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
 WHERE d.DeleteStatus = 'N'
 ORDER BY d.MTransNo;
GO

/* The old unique key on (PartNo, DieType, DeleteStatus, SubscID) allowed only
   one die per part + die type (and only one deleted one). Several dies of a
   part are now allowed when each has its own Die Code: SP_DieMst enforces
   that rule and UX_DieMst_DieCode keeps the codes unique. */
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_DieMst_Code' AND parent_object_id = OBJECT_ID('dbo.DieMst'))
    ALTER TABLE dbo.DieMst DROP CONSTRAINT UQ_DieMst_Code;
GO
