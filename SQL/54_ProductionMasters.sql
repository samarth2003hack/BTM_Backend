/*==============================================================================
  54. PRODUCTION MASTERS  -  Masters/MachineMaster.html, DieMaster.html,
                             RoutingChart.html, Production/ConvBOM.html

  Takes the four production masters off the dummy store and onto real tables.
  The business rules the dummy store (Config/js/production-mock-store.js)
  enforced now live in these procedures, so every caller gets them.

  MACHINE  (MachineMst, existing)
      Operation = comma-separated OtherMaster.MTransNo of type 'Operation'.
      Save:   code + name required; at least one operation, each a live
              'Operation' row; Machine Code unique per SubscID; an operation
              a live route step runs on this machine cannot be unmapped.
      Delete: refused while a live route step, a live JobCardEntry or any
              maintenance record (PMChecklistMachine / PMSheetMst /
              BreakdownMst) points at the machine.

  DIE  (DieMst, existing)
      A die = a Part Master part + one of its MaterialGrade grades (one die
      per part + grade). DieType holds the 'Die Type' OtherMaster NAME.
      UsedQty     = live JobCardEntry.Qty against the die (SP_DieUsageRefresh,
                    SQL 53, keeps it current; the save re-reads it too).
      ProcessQty  = OpeningQty + UsedQty
      RemainingQty= MAX(0, MaxQty - ProcessQty)
      Save:   refused when OpeningQty + UsedQty > MaxQty; a die with live
              job-card entries keeps its part and grade.
      Delete: refused while live job-card entries use it.

  ROUTING CHART  (RoutingChartMst / RoutingChartDtl, existing)
      Route name unique per part + grade. DieType is copied from the die of
      the same part + grade when one exists.
      Steps (SP_RoutingChartSteps, new): replaces the whole list atomically;
      every step needs a live operation, a live machine mapped to that
      operation and a time > 0. While live job cards use the route its part,
      grade and steps are locked (an identical re-save is accepted).
      Delete: refused while live job cards use the route.

  CONVERSION BOM  (ConvBOM, new)
      One row per finished part (+ optional FG grade) and raw-material line:
      RM part + RM grade, qty per finished piece, RM unit. A blank FG grade
      means the line applies to every grade of the finished part.
      Unique on FG part + FG grade + RM part.

  Objects:
      ConvBOM                 new table
      SP_ConvBOM              @Status 'Insert' | 'Update' | 'Delete'
      SP_MachineMst           ALTER - same signature, rules above
      SP_DieMst               ALTER - same signature, rules above
      SP_RoutingChartMst      ALTER - same signature, rules above
      SP_RoutingChartSteps    new   - JSON step list, replace-all

  JobCardMst / JobCardEntry (SQL 53) and the maintenance tables (SQL 55) are
  only reached through OBJECT_ID-guarded dynamic SQL, so this script and its
  procedures work whether or not those scripts have run yet.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Run this BEFORE deploying the API build that calls SP_ConvBOM /
  SP_RoutingChartSteps. No menu rows here (the lead seeds the menu).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. TABLE  ConvBOM
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.ConvBOM', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConvBOM
    (
        MTransNo      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConvBOM PRIMARY KEY,
        FPartNo       INT            NOT NULL,          -- finished part (PartMaster.MTransNo)
        FGradeNo      INT            NULL,              -- MaterialGrade.GradeNo of the FG part; NULL = any grade
        FGrade        VARCHAR(50)    NULL,
        PartNo        INT            NOT NULL,          -- raw material (PartMaster.MTransNo)
        GradeNo       INT            NULL,              -- MaterialGrade.GradeNo of the RM part
        Grade         VARCHAR(50)    NULL,
        Qty           NUMERIC(12,4)  NOT NULL,          -- RM qty per finished piece
        Unit          VARCHAR(20)    NULL,              -- RM PrimaryUnit at save time
        Remarks       VARCHAR(250)   NULL,
        SystemIP      VARCHAR(20)    NULL,
        CreatedBy     INT            NOT NULL,
        CreatedOn     DATETIME2      NOT NULL,
        ModifiedBy    INT            NULL,
        ModifiedOn    DATETIME2      NULL,
        DeleteStatus  CHAR(1)        NOT NULL CONSTRAINT DF_ConvBOM_DeleteStatus DEFAULT ('N'),
        DeleteReason  VARCHAR(250)   NULL,
        LockStatus    CHAR(1)        NOT NULL CONSTRAINT DF_ConvBOM_LockStatus DEFAULT ('N'),
        LockCounter   INT            NOT NULL CONSTRAINT DF_ConvBOM_LockCounter DEFAULT (0),
        SubscID       INT            NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvBOM_FPart' AND object_id = OBJECT_ID(N'dbo.ConvBOM'))
    CREATE INDEX IX_ConvBOM_FPart ON dbo.ConvBOM (SubscID, FPartNo, DeleteStatus);
GO

/*------------------------------------------------------------------------------
  2. SP_ConvBOM
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ConvBOM', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ConvBOM AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ConvBOM
    @ID            INT            OUTPUT,
    @MTransNo      INT,
    @FPartNo       INT,
    @FGradeNo      INT,
    @PartNo        INT,
    @GradeNo       INT,
    @Qty           NUMERIC(12,4),
    @Remarks       VARCHAR(250),
    @LockStatus    CHAR(1),
    @UserNo        INT,
    @SystemIP      VARCHAR(20),
    @DeleteReason  VARCHAR(250),
    @SubscID       INT,
    @Status        VARCHAR(20),
    @Message       NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @FCode VARCHAR(50), @FName VARCHAR(250), @RCode VARCHAR(50), @Unit VARCHAR(20),
            @FGrade VARCHAR(50), @Grade VARCHAR(50);

    BEGIN TRY
        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @FCode = PartCode, @FName = PartName
              FROM dbo.PartMaster
             WHERE MTransNo = @FPartNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @FCode IS NULL
            BEGIN SET @Message = N'Select a valid FG Part.'; RETURN; END

            SELECT @RCode = PartCode, @Unit = PrimaryUnit
              FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @RCode IS NULL
            BEGIN SET @Message = N'Select a valid RM Part.'; RETURN; END

            IF @FPartNo = @PartNo
            BEGIN SET @Message = N'The FG Part and the RM Part cannot be the same part.'; RETURN; END

            IF ISNULL(@Qty, 0) <= 0
            BEGIN SET @Message = N'Qty must be greater than zero.'; RETURN; END

            IF ISNULL(@FGradeNo, 0) > 0
            BEGIN
                SELECT @FGrade = Grade FROM dbo.MaterialGrade
                 WHERE PartNo = @FPartNo AND GradeNo = @FGradeNo AND SubscID = @SubscID AND DeleteStatus = 'N';
                IF @FGrade IS NULL
                BEGIN SET @Message = N'The FG grade is not a grade of ' + @FCode + N'.'; RETURN; END
            END
            ELSE SET @FGradeNo = NULL;

            IF ISNULL(@GradeNo, 0) > 0
            BEGIN
                SELECT @Grade = Grade FROM dbo.MaterialGrade
                 WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N';
                IF @Grade IS NULL
                BEGIN SET @Message = N'The RM grade is not a grade of ' + @RCode + N'.'; RETURN; END
            END
            ELSE SET @GradeNo = NULL;

            IF EXISTS (SELECT 1 FROM dbo.ConvBOM
                        WHERE FPartNo = @FPartNo AND ISNULL(FGradeNo, 0) = ISNULL(@FGradeNo, 0)
                          AND PartNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN
                SET @Message = N'This FG Part / RM Part combination already exists: ' + @FCode + N' - ' + ISNULL(@FName, N'');
                RETURN;
            END

            IF @Status = 'Update' AND NOT EXISTS (SELECT 1 FROM dbo.ConvBOM
                                                   WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN SET @Message = N'Record not found.'; RETURN; END
        END

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            INSERT INTO dbo.ConvBOM
                (FPartNo, FGradeNo, FGrade, PartNo, GradeNo, Grade, Qty, Unit, Remarks,
                 SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
            VALUES
                (@FPartNo, @FGradeNo, @FGrade, @PartNo, @GradeNo, @Grade, @Qty, @Unit, NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                 @SystemIP, @UserNo, GETDATE(), 'N', ISNULL(NULLIF(@LockStatus, ''), 'N'), 0, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Conversion BOM saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.ConvBOM
               SET FPartNo = @FPartNo, FGradeNo = @FGradeNo, FGrade = @FGrade,
                   PartNo = @PartNo, GradeNo = @GradeNo, Grade = @Grade,
                   Qty = @Qty, Unit = @Unit, Remarks = NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                   LockStatus = ISNULL(NULLIF(@LockStatus, ''), LockStatus),
                   LockCounter = LockCounter + 1,
                   SystemIP = @SystemIP, ModifiedBy = @UserNo, ModifiedOn = GETDATE()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Conversion BOM updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.ConvBOM
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   SystemIP = @SystemIP, ModifiedBy = @UserNo, ModifiedOn = GETDATE()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Conversion BOM deleted successfully.'; END
            ELSE SET @Message = N'Record not found.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  3. SP_MachineMst  (same signature as before)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MachineMst', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_MachineMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_MachineMst
    @ID            INT            OUTPUT,
    @MTransNo      INT,
    @MCount        INT,
    @MachineCode   VARCHAR(20),
    @MachineName   VARCHAR(100),
    @DateofInst    DATETIME2,
    @Capacity      NUMERIC(12, 2),
    @Operation     VARCHAR(5000),   -- comma-separated OtherMaster.MTransNo ('Operation')
    @Remarks       VARCHAR(200),
    @LockStatus    CHAR(1),
    @CreatedBy     INT,
    @CreatedOn     DATETIME2,
    @ModifiedBy    INT,
    @ModifiedOn    DATETIME2,
    @DeleteReason  VARCHAR(250),
    @SubscID       INT,
    @Status        VARCHAR(20),
    @Message       NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Code VARCHAR(20) = UPPER(LTRIM(RTRIM(ISNULL(@MachineCode, ''))));
    DECLARE @Ops TABLE (OpNo INT PRIMARY KEY);
    DECLARE @Txt NVARCHAR(200), @RouteUse INT = 0, @EntryUse INT = 0, @MntUse INT = 0, @n INT;

    BEGIN TRY
        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF @Code = '' OR LTRIM(RTRIM(ISNULL(@MachineName, ''))) = ''
            BEGIN SET @Message = N'Machine Code and Machine Name are required.'; RETURN; END

            INSERT INTO @Ops (OpNo)
            SELECT DISTINCT TRY_CONVERT(INT, LTRIM(RTRIM(value)))
              FROM STRING_SPLIT(ISNULL(@Operation, ''), ',')
             WHERE TRY_CONVERT(INT, LTRIM(RTRIM(value))) > 0;

            IF NOT EXISTS (SELECT 1 FROM @Ops)
            BEGIN SET @Message = N'Select at least one operation for the machine.'; RETURN; END

            IF EXISTS (SELECT 1 FROM @Ops o
                        WHERE NOT EXISTS (SELECT 1 FROM dbo.OtherMaster om
                                           WHERE om.MTransNo = o.OpNo AND om.MasterType = 'Operation'
                                             AND om.SubscID = @SubscID AND om.DeleteStatus = 'N'))
            BEGIN SET @Message = N'One of the selected operations no longer exists.'; RETURN; END

            SELECT @Operation = STRING_AGG(CONVERT(VARCHAR(12), OpNo), ',') WITHIN GROUP (ORDER BY OpNo) FROM @Ops;

            IF EXISTS (SELECT 1 FROM dbo.MachineMst
                        WHERE MachineCode = @Code AND SubscID = @SubscID AND DeleteStatus = 'N'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'Machine Code ' + @Code + N' already exists.'; RETURN; END

            IF @Status = 'Update'
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.MachineMst
                                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
                BEGIN SET @Message = N'Machine not found.'; RETURN; END

                -- An operation a live route already runs on this machine cannot be unmapped.
                SELECT TOP (1)
                       @Txt = N'"' + ISNULL(om.MasterName, N'#' + CONVERT(NVARCHAR(12), d.Operation))
                            + N'" is used on this machine by route "' + h.RouteName
                            + N'" (step ' + CONVERT(NVARCHAR(12), d.SlNo) + N'). Change that route first.'
                  FROM dbo.RoutingChartDtl d
                  JOIN dbo.RoutingChartMst h ON h.MTransNo = d.MTransNo AND h.DeleteStatus = 'N'
                  LEFT JOIN dbo.OtherMaster om ON om.MTransNo = d.Operation
                 WHERE d.MachineNo = @MTransNo AND d.DeleteStatus = 'N'
                   AND NOT EXISTS (SELECT 1 FROM @Ops o WHERE o.OpNo = d.Operation)
                 ORDER BY h.RouteName, d.SlNo;

                IF @Txt IS NOT NULL
                BEGIN SET @Message = @Txt; RETURN; END
            END
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            SELECT @Code = MachineCode FROM dbo.MachineMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @@ROWCOUNT = 0
            BEGIN SET @Message = N'Machine not found.'; RETURN; END

            SELECT @RouteUse = COUNT(*)
              FROM dbo.RoutingChartDtl d
              JOIN dbo.RoutingChartMst h ON h.MTransNo = d.MTransNo AND h.DeleteStatus = 'N'
             WHERE d.MachineNo = @MTransNo AND d.DeleteStatus = 'N';

            IF OBJECT_ID(N'dbo.JobCardEntry', N'U') IS NOT NULL
                EXEC sp_executesql
                     N'SELECT @n = COUNT(*) FROM dbo.JobCardEntry WHERE MachineNo = @m AND DeleteStatus = ''N'';',
                     N'@m INT, @n INT OUTPUT', @m = @MTransNo, @n = @EntryUse OUTPUT;

            IF @RouteUse > 0 OR @EntryUse > 0
            BEGIN
                SET @Message = @Code + N' is used in ' + CONVERT(NVARCHAR(12), @RouteUse) + N' route step(s) and '
                             + CONVERT(NVARCHAR(12), @EntryUse) + N' production entr(ies). Mark it Inactive instead.';
                RETURN;
            END

            -- Maintenance records (SQL 55) - each table only when it exists.
            DECLARE @t SYSNAME, @sql NVARCHAR(400);
            DECLARE mnt CURSOR LOCAL FAST_FORWARD FOR
                SELECT name FROM (VALUES (N'PMChecklistMachine'), (N'PMSheetMst'), (N'BreakdownMst')) v(name);
            OPEN mnt;
            FETCH NEXT FROM mnt INTO @t;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF OBJECT_ID(N'dbo.' + @t, N'U') IS NOT NULL AND COL_LENGTH(N'dbo.' + @t, N'MachineNo') IS NOT NULL
                BEGIN
                    SET @sql = N'SELECT @n = COUNT(*) FROM dbo.' + QUOTENAME(@t) + N' WHERE MachineNo = @m'
                             + CASE WHEN COL_LENGTH(N'dbo.' + @t, N'DeleteStatus') IS NOT NULL
                                    THEN N' AND DeleteStatus = ''N''' ELSE N'' END + N';';
                    SET @n = 0;
                    EXEC sp_executesql @sql, N'@m INT, @n INT OUTPUT', @m = @MTransNo, @n = @n OUTPUT;
                    SET @MntUse += ISNULL(@n, 0);
                END
                FETCH NEXT FROM mnt INTO @t;
            END
            CLOSE mnt;
            DEALLOCATE mnt;

            IF @MntUse > 0
            BEGIN
                SET @Message = @Code + N' has maintenance records (checklist, check sheets or breakdowns). Mark it Inactive instead.';
                RETURN;
            END
        END

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            INSERT INTO dbo.MachineMst
                (MachineCode, MachineName, DateofInst, Capacity, Operation, Remarks,
                 LockStatus, LockCounter, CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
                (@Code, LTRIM(RTRIM(@MachineName)), @DateofInst, @Capacity, @Operation, LTRIM(RTRIM(@Remarks)),
                 ISNULL(NULLIF(@LockStatus, ''), 'Y'), 1, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Machine saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.MachineMst
               SET MachineCode = @Code,
                   MachineName = LTRIM(RTRIM(@MachineName)),
                   DateofInst  = ISNULL(@DateofInst, DateofInst),
                   Capacity    = ISNULL(@Capacity, Capacity),
                   Operation   = @Operation,
                   Remarks     = LTRIM(RTRIM(@Remarks)),
                   LockStatus  = ISNULL(NULLIF(@LockStatus, ''), LockStatus),
                   LockCounter = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Machine updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.MachineMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Machine deleted successfully.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  4. SP_DieMst  (same signature as before; @DieType = 'Die Type' NAME)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_DieMst', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_DieMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_DieMst
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
    @Message       NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Code VARCHAR(50), @CurCode VARCHAR(50), @Grade VARCHAR(50),
            @CurPart INT, @CurGrade INT, @Entries INT = 0,
            @Used NUMERIC(12,2) = 0,
            @Max  NUMERIC(12,2) = ISNULL(@MaxQty, 0),
            @Open NUMERIC(12,2) = ISNULL(@OpeningQty, 0);

    BEGIN TRY
        -- Current row + its live production (needed by Update and Delete).
        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @CurPart = d.PartNo, @CurGrade = d.GradeNo, @Used = d.UsedQty,
                   @CurCode = ISNULL(p.PartCode, 'This die')
              FROM dbo.DieMst d
              LEFT JOIN dbo.PartMaster p ON p.MTransNo = d.PartNo
             WHERE d.MTransNo = @MTransNo AND d.SubscID = @SubscID AND d.DeleteStatus = 'N';
            IF @CurPart IS NULL
            BEGIN SET @Message = N'Die not found.'; RETURN; END

            IF OBJECT_ID(N'dbo.JobCardEntry', N'U') IS NOT NULL
                EXEC sp_executesql
                     N'SELECT @n = COUNT(*), @q = ISNULL(SUM(Qty), 0) FROM dbo.JobCardEntry WHERE DieNo = @d AND DeleteStatus = ''N'';',
                     N'@d INT, @n INT OUTPUT, @q NUMERIC(12,2) OUTPUT',
                     @d = @MTransNo, @n = @Entries OUTPUT, @q = @Used OUTPUT;
        END

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @Code = PartCode FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @Code IS NULL
            BEGIN SET @Message = N'Select a die from the Part Master list.'; RETURN; END

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

            -- One die per part AND grade.
            IF EXISTS (SELECT 1 FROM dbo.DieMst
                        WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'A die for ' + @Code + N' in grade ' + @Grade + N' already exists.'; RETURN; END

            IF @Status = 'Insert' SET @Used = 0;

            -- A die with production recorded keeps its part and grade.
            IF @Status = 'Update' AND @Entries > 0 AND @CurPart <> @PartNo
            BEGIN SET @Message = @CurCode + N' has production recorded against it, so its die cannot be changed.'; RETURN; END
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
                 Remarks, LockStatus, LockCounter, CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
                (@PartNo, @GradeNo, LTRIM(RTRIM(@DieType)), @Max, @Open, 0, @Open,
                 CASE WHEN @Max - @Open > 0 THEN @Max - @Open ELSE 0 END,
                 LTRIM(RTRIM(@Remarks)), ISNULL(NULLIF(@LockStatus, ''), 'Y'), 1,
                 @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Die saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.DieMst
               SET PartNo       = @PartNo,
                   GradeNo      = @GradeNo,
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
END
GO

/*------------------------------------------------------------------------------
  5. SP_RoutingChartMst  (same signature as before)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RoutingChartMst', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_RoutingChartMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RoutingChartMst
    @ID            INT            OUTPUT,
    @MTransNo      INT,
    @MCount        INT,
    @PartNo        INT,
    @GradeNo       INT,
    @RouteName     VARCHAR(100),
    @BatchQty      INT,
    @DieType       VARCHAR(50),
    @LockStatus    CHAR(1),
    @CreatedBy     INT,
    @CreatedOn     DATETIME2,
    @ModifiedBy    INT,
    @ModifiedOn    DATETIME2,
    @DeleteReason  VARCHAR(250),
    @SubscID       INT,
    @Status        VARCHAR(20),
    @Message       NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Code VARCHAR(50), @Grade VARCHAR(50), @Name VARCHAR(100) = LTRIM(RTRIM(ISNULL(@RouteName, ''))),
            @CurPart INT, @CurGrade INT, @CurName VARCHAR(100), @JobCards INT = 0, @DieT VARCHAR(50);

    BEGIN TRY
        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @CurPart = PartNo, @CurGrade = GradeNo, @CurName = RouteName
              FROM dbo.RoutingChartMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @CurPart IS NULL
            BEGIN SET @Message = N'Route not found.'; RETURN; END

            IF OBJECT_ID(N'dbo.JobCardMst', N'U') IS NOT NULL
                EXEC sp_executesql
                     N'SELECT @n = COUNT(*) FROM dbo.JobCardMst WHERE RouteNo = @r AND DeleteStatus = ''N'';',
                     N'@r INT, @n INT OUTPUT', @r = @MTransNo, @n = @JobCards OUTPUT;
        END

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @Code = PartCode FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @Code IS NULL
            BEGIN SET @Message = N'Select a valid finished product.'; RETURN; END

            IF ISNULL(@GradeNo, 0) <= 0
            BEGIN SET @Message = N'Grade is required.'; RETURN; END

            SELECT @Grade = Grade FROM dbo.MaterialGrade
             WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @Grade IS NULL
            BEGIN SET @Message = N'The selected grade is not a grade of ' + @Code + N'.'; RETURN; END

            IF @Name = ''
            BEGIN SET @Message = N'Route Name is required.'; RETURN; END

            IF ISNULL(@BatchQty, 0) <= 0
            BEGIN SET @Message = N'Batch Quantity must be greater than zero.'; RETURN; END

            -- A product may carry the same route name in different grades.
            IF EXISTS (SELECT 1 FROM dbo.RoutingChartMst
                        WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND RouteName = @Name
                          AND SubscID = @SubscID AND DeleteStatus = 'N'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'This product already has a ' + @Grade + N' route named "' + @Name + N'".'; RETURN; END

            IF @Status = 'Update' AND @JobCards > 0 AND @CurPart <> @PartNo
            BEGIN SET @Message = N'This route is used by job cards - its product cannot change.'; RETURN; END
            IF @Status = 'Update' AND @JobCards > 0 AND @CurGrade <> @GradeNo
            BEGIN SET @Message = N'This route is used by job cards - its grade cannot change.'; RETURN; END

            -- The die type follows the die of the same part + grade.
            SELECT TOP (1) @DieT = DieType FROM dbo.DieMst
             WHERE PartNo = @PartNo AND GradeNo = @GradeNo AND SubscID = @SubscID AND DeleteStatus = 'N'
             ORDER BY MTransNo;
            SET @DieType = COALESCE(@DieT, NULLIF(LTRIM(RTRIM(@DieType)), ''), '');
        END
        ELSE IF @Status = 'Delete' AND @JobCards > 0
        BEGIN
            SET @Message = N'"' + @CurName + N'" is used by job cards and cannot be deleted.';
            RETURN;
        END

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            INSERT INTO dbo.RoutingChartMst
                (PartNo, GradeNo, RouteName, BatchQty, DieType, LockStatus, LockCounter,
                 CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
                (@PartNo, @GradeNo, @Name, @BatchQty, @DieType, ISNULL(NULLIF(@LockStatus, ''), 'Y'), 1,
                 @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Routing Chart saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.RoutingChartMst
               SET PartNo      = @PartNo,
                   GradeNo     = @GradeNo,
                   RouteName   = @Name,
                   BatchQty    = @BatchQty,
                   DieType     = @DieType,
                   LockStatus  = ISNULL(NULLIF(@LockStatus, ''), LockStatus),
                   LockCounter = ISNULL(LockCounter, 0) + 1,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Routing Chart updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.RoutingChartDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            UPDATE dbo.RoutingChartMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = N'Routing Chart deleted successfully.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6. SP_RoutingChartSteps  -  replace a route's whole step list
     @Steps = JSON array [{ "operationNo": 1, "machineNo": 2, "timeMin": 3.5 }, ...]
     in serial order (SlNo = array position + 1).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RoutingChartSteps', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_RoutingChartSteps AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RoutingChartSteps
    @ID        INT            OUTPUT,
    @MTransNo  INT,
    @Steps     NVARCHAR(MAX),
    @UserNo    INT,
    @SubscID   INT,
    @Message   NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @S TABLE (Pos INT PRIMARY KEY, OperationNo INT, MachineNo INT, TimeMin NUMERIC(8,2));
    DECLARE @JobCards INT = 0, @Same BIT = 0, @Txt NVARCHAR(200);

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.RoutingChartMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Route not found.'; RETURN; END

        IF ISJSON(@Steps) = 1
            INSERT INTO @S (Pos, OperationNo, MachineNo, TimeMin)
            SELECT CONVERT(INT, [key]) + 1,
                   TRY_CONVERT(INT,          JSON_VALUE(value, '$.operationNo')),
                   TRY_CONVERT(INT,          JSON_VALUE(value, '$.machineNo')),
                   TRY_CONVERT(NUMERIC(8,2), JSON_VALUE(value, '$.timeMin'))
              FROM OPENJSON(@Steps);

        IF NOT EXISTS (SELECT 1 FROM @S)
        BEGIN SET @Message = N'Add at least one operation to the route.'; RETURN; END

        SELECT TOP (1) @Txt =
               N'Step ' + CONVERT(NVARCHAR(12), s.Pos) +
               CASE WHEN om.MTransNo IS NULL
                         THEN N': select a valid operation.'
                    WHEN m.MTransNo IS NULL
                      OR NOT EXISTS (SELECT 1 FROM STRING_SPLIT(ISNULL(m.Operation, ''), ',') x
                                      WHERE TRY_CONVERT(INT, LTRIM(RTRIM(x.value))) = s.OperationNo)
                         THEN N': the machine is not mapped to ' + om.MasterName + N'.'
                    ELSE N': time must be greater than zero.'
               END
          FROM @S s
          LEFT JOIN dbo.OtherMaster om ON om.MTransNo = s.OperationNo AND om.MasterType = 'Operation'
                                      AND om.SubscID = @SubscID AND om.DeleteStatus = 'N'
          LEFT JOIN dbo.MachineMst  m  ON m.MTransNo = s.MachineNo AND m.SubscID = @SubscID AND m.DeleteStatus = 'N'
         WHERE om.MTransNo IS NULL
            OR m.MTransNo IS NULL
            OR NOT EXISTS (SELECT 1 FROM STRING_SPLIT(ISNULL(m.Operation, ''), ',') x
                            WHERE TRY_CONVERT(INT, LTRIM(RTRIM(x.value))) = s.OperationNo)
            OR ISNULL(s.TimeMin, 0) <= 0
         ORDER BY s.Pos;

        IF @Txt IS NOT NULL
        BEGIN SET @Message = @Txt; RETURN; END

        -- Is the new list identical to the live one?
        IF NOT EXISTS (
            SELECT 1
              FROM (SELECT ROW_NUMBER() OVER (ORDER BY SlNo, TransSlNo) AS rn, Operation, MachineNo, TimeMin
                      FROM dbo.RoutingChartDtl
                     WHERE MTransNo = @MTransNo AND DeleteStatus = 'N') c
              FULL JOIN @S s ON s.Pos = c.rn
             WHERE c.rn IS NULL OR s.Pos IS NULL
                OR c.Operation <> s.OperationNo OR c.MachineNo <> s.MachineNo
                OR ISNULL(c.TimeMin, 0) <> ISNULL(s.TimeMin, 0))
            SET @Same = 1;

        IF @Same = 1
        BEGIN SET @ID = @MTransNo; SET @Message = N'Steps unchanged.'; RETURN; END

        IF OBJECT_ID(N'dbo.JobCardMst', N'U') IS NOT NULL
            EXEC sp_executesql
                 N'SELECT @n = COUNT(*) FROM dbo.JobCardMst WHERE RouteNo = @r AND DeleteStatus = ''N'';',
                 N'@r INT, @n INT OUTPUT', @r = @MTransNo, @n = @JobCards OUTPUT;

        IF @JobCards > 0
        BEGIN SET @Message = N'This route is used by job cards - its operations are locked.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.RoutingChartDtl
           SET DeleteStatus = 'Y', DeleteReason = 'Replaced by new step list',
               ModifiedBy = @UserNo, ModifiedOn = GETDATE()
         WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

        INSERT INTO dbo.RoutingChartDtl
            (MTransNo, SlNo, Operation, MachineNo, TimeMin, OperCost, LockStatus, LockCounter,
             CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
        SELECT @MTransNo, s.Pos, s.OperationNo, s.MachineNo, s.TimeMin, 0, 'N', 1,
               @UserNo, GETDATE(), 'N', NULL, @SubscID
          FROM @S s
         ORDER BY s.Pos;

        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = N'Steps saved successfully.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

PRINT '54_ProductionMasters.sql applied.';
GO
