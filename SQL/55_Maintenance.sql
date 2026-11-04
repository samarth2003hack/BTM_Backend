/*==============================================================================
  55. MAINTENANCE  -  PM checklists, PM check sheets, breakdowns

  Pages:
      Modules/Masters/PMChecklistMaster.html           api/PMChecklist/...
      Modules/Maintenance/PMCheckSheet.html            api/PMCheckSheet/...
      Modules/Maintenance/BreakdownMaintenance.html    api/Breakdown/...
      Modules/MaintenanceReports/MachineMaintenanceReport.html
      Modules/MaintenanceReports/MachineHistoryCard.html
                                                       api/MaintenanceReport/...

      PM Checklist --> PM Check Sheet (machine x month) --> Maintenance Report
      Machine Master --> Breakdown ----------------------------------^

  A PM checklist (paper form MNT/F/01) lists check points by frequency
  (Daily / Weekly / Monthly / Half-Yearly / Yearly) and category
  (Mechanical / Electrical / General) and covers one or more machines. A
  machine follows ONE active checklist at a time.

  A check sheet is one machine for one month. It keeps a SNAPSHOT of the
  points that were due when it was opened (PMSheetPoint), exactly like a
  printed form - editing the checklist later never rewrites an old sheet.
  Half-yearly / yearly points appear only when due: last done + 6 / 12 months
  <= month end ("last done" = PMOpeningDone + every sheet entry OK / NOK).
  Results are kept per point x slot (PMSheetEntry): slot = day for Daily,
  week 1-5 for Weekly, 0 for the periodic points. Idle days and the day's
  operator are PMSheetDay rows. Point remarks live on PMSheetPoint.Remarks.
  Approval: Dept (a 'Production Supervisor') then Maint (a 'Maintenance
  Supervisor'), both only after the month is over; a Dept-approved sheet is
  locked. Compliance / due / done / missed are recomputed by the API on read.

  Maintenance staff = OtherMaster rows of type 'Maintenance Supervisor',
  'Production Supervisor', 'Fitter', 'Electrician'; operators = 'Operator
  Name' (SQL 52).

  Numbering, per subscription and financial year (April-March, 2026-10-14
  -> "2627"): PM-<FY>-0001 (FY of the sheet's month), BD-<FY>-0001 (FY of
  the breakdown time). Numbers of deleted documents are not reused.

  Objects:
      PMChecklistMst / PMChecklistMachine / PMChecklistPoint
      PMOpeningDone       last-done history before go-live (optional, no page)
      PMSheetMst / PMSheetPoint / PMSheetEntry / PMSheetDay
      BreakdownMst
      FN_FYCode
      SP_PMChecklist      @Status 'Insert' | 'Update' | 'Delete'
      SP_PMSheet          @Status 'Insert' | 'Update' | 'Delete'
      SP_PMSheetApprove   @Level 'Dept' | 'Maint'
      SP_Breakdown        @Status 'Insert' | 'Update' | 'Delete'

  The API validates the business rules first (it needs the computed due
  points); the procedures re-check every rule that guards the data (locks,
  duplicates, one live breakdown per machine) inside the write.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;     -- filtered indexes below; procedures capture it
GO

/*------------------------------------------------------------------------------
  1. PM CHECKLIST
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.PMChecklistMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMChecklistMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        ChecklistName   VARCHAR(100)    NOT NULL,
        DocNo           VARCHAR(30)     NOT NULL,       -- form no, e.g. MNT/F/01
        RevNo           VARCHAR(10)     NOT NULL,
        RevDate         DATE            NOT NULL,
        Remarks         VARCHAR(200)    NULL,

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_PMChecklistMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_PMChecklistMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_PMChecklistMst_LockStatus   DEFAULT ('Y'),  -- 'Y' active, 'N' inactive
        LockCounter     INT             NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_PMChecklistMst PRIMARY KEY CLUSTERED (MTransNo)
    );
END
GO

IF OBJECT_ID(N'dbo.PMChecklistMachine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMChecklistMachine
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        ChecklistNo     INT             NOT NULL,
        MachineNo       INT             NOT NULL,       -- MachineMst.MTransNo
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_PMChecklistMachine PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_PMChecklistMachine_Mst FOREIGN KEY (ChecklistNo) REFERENCES dbo.PMChecklistMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_PMChecklistMachine_Machine' AND object_id = OBJECT_ID(N'dbo.PMChecklistMachine'))
    CREATE NONCLUSTERED INDEX IX_PMChecklistMachine_Machine
        ON dbo.PMChecklistMachine (MachineNo) INCLUDE (ChecklistNo);
GO

-- A point keeps its TransSlNo for life, so its "last done" history carries
-- on across checklist edits; a removed point is soft-deleted, never reused.
IF OBJECT_ID(N'dbo.PMChecklistPoint', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMChecklistPoint
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        ChecklistNo     INT             NOT NULL,
        Frequency       VARCHAR(20)     NOT NULL,       -- Daily / Weekly / Monthly / Half-Yearly / Yearly
        Category        VARCHAR(20)     NOT NULL,       -- Mechanical / Electrical / General
        SlNo            INT             NOT NULL,       -- 1, 2, 3 ... within the frequency
        [CheckPoint]      VARCHAR(200)    NOT NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_PMChecklistPoint_DeleteStatus DEFAULT ('N'),
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_PMChecklistPoint PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_PMChecklistPoint_Mst FOREIGN KEY (ChecklistNo) REFERENCES dbo.PMChecklistMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_PMChecklistPoint_Checklist' AND object_id = OBJECT_ID(N'dbo.PMChecklistPoint'))
    CREATE NONCLUSTERED INDEX IX_PMChecklistPoint_Checklist
        ON dbo.PMChecklistPoint (ChecklistNo, DeleteStatus);
GO

-- When a periodic point was last done on a machine before this system.
IF OBJECT_ID(N'dbo.PMOpeningDone', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMOpeningDone
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MachineNo       INT             NOT NULL,
        PointNo         INT             NOT NULL,       -- PMChecklistPoint.TransSlNo
        DoneOn          DATE            NOT NULL,

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_PMOpeningDone_CreatedOn    DEFAULT (SYSDATETIME()),
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_PMOpeningDone_DeleteStatus DEFAULT ('N'),
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_PMOpeningDone PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

/*------------------------------------------------------------------------------
  2. PM CHECK SHEET
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.PMSheetMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMSheetMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        SheetNo         VARCHAR(20)     NOT NULL,       -- PM-<FY>-0001
        MachineNo       INT             NOT NULL,
        ChecklistNo     INT             NOT NULL,
        DocNo           VARCHAR(30)     NULL,           -- checklist form block, as printed
        RevNo           VARCHAR(10)     NULL,
        RevDate         DATE            NULL,
        [Year]          SMALLINT        NOT NULL,
        [Month]         TINYINT         NOT NULL,
        Remarks         VARCHAR(500)    NULL,
        DeptSupBy       INT             NULL,           -- OtherMaster 'Production Supervisor'
        DeptSupOn       DATE            NULL,
        MntSupBy        INT             NULL,           -- OtherMaster 'Maintenance Supervisor'
        MntSupOn        DATE            NULL,

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_PMSheetMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_PMSheetMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_PMSheetMst_LockStatus   DEFAULT ('N'),  -- 'Y' once Dept approved
        LockCounter     INT             NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_PMSheetMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT FK_PMSheetMst_Checklist FOREIGN KEY (ChecklistNo) REFERENCES dbo.PMChecklistMst (MTransNo)
    );
END
GO

-- One live sheet per machine per month.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UX_PMSheetMst_MachineMonth' AND object_id = OBJECT_ID(N'dbo.PMSheetMst'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_PMSheetMst_MachineMonth
        ON dbo.PMSheetMst (SubscID, MachineNo, [Year], [Month])
        WHERE DeleteStatus = 'N';
GO

IF OBJECT_ID(N'dbo.PMSheetPoint', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMSheetPoint
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- PMSheetMst
        PointNo         INT             NOT NULL,       -- PMChecklistPoint.TransSlNo
        SlNo            INT             NOT NULL,
        [CheckPoint]      VARCHAR(200)    NOT NULL,
        Frequency       VARCHAR(20)     NOT NULL,
        Category        VARCHAR(20)     NOT NULL,
        Remarks         VARCHAR(1000)   NULL,           -- point remark (required for a Not OK)

        CONSTRAINT PK_PMSheetPoint PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_PMSheetPoint_Mst FOREIGN KEY (MTransNo) REFERENCES dbo.PMSheetMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_PMSheetPoint_MTransNo' AND object_id = OBJECT_ID(N'dbo.PMSheetPoint'))
    CREATE NONCLUSTERED INDEX IX_PMSheetPoint_MTransNo ON dbo.PMSheetPoint (MTransNo);
GO

IF OBJECT_ID(N'dbo.PMSheetEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMSheetEntry
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,
        PointNo         INT             NOT NULL,
        Slot            TINYINT         NOT NULL,       -- day (Daily), week 1-5 (Weekly), 0 (periodic)
        EntryStatus     VARCHAR(3)      NOT NULL,       -- OK / NOK / NA
        DoneOn          DATE            NULL,
        DoneBy          INT             NULL,           -- maintenance staff (non-daily)

        CONSTRAINT PK_PMSheetEntry PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_PMSheetEntry_Mst FOREIGN KEY (MTransNo) REFERENCES dbo.PMSheetMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_PMSheetEntry_MTransNo' AND object_id = OBJECT_ID(N'dbo.PMSheetEntry'))
    CREATE NONCLUSTERED INDEX IX_PMSheetEntry_MTransNo ON dbo.PMSheetEntry (MTransNo, PointNo, Slot);
GO

IF OBJECT_ID(N'dbo.PMSheetDay', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PMSheetDay
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,
        DayNo           TINYINT         NOT NULL,
        IsIdle          BIT             NOT NULL CONSTRAINT DF_PMSheetDay_IsIdle DEFAULT (0),
        OperatorNo      INT             NULL,           -- OtherMaster 'Operator Name'

        CONSTRAINT PK_PMSheetDay PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_PMSheetDay_Mst FOREIGN KEY (MTransNo) REFERENCES dbo.PMSheetMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_PMSheetDay_MTransNo' AND object_id = OBJECT_ID(N'dbo.PMSheetDay'))
    CREATE NONCLUSTERED INDEX IX_PMSheetDay_MTransNo ON dbo.PMSheetDay (MTransNo, DayNo);
GO

/*------------------------------------------------------------------------------
  3. BREAKDOWN
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.BreakdownMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BreakdownMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        BreakdownNo     VARCHAR(20)     NOT NULL,       -- BD-<FY>-0001
        MachineNo       INT             NOT NULL,
        BreakdownOn     DATETIME2(0)    NOT NULL,
        BreakdownType   VARCHAR(20)     NOT NULL,       -- Mechanical / Electrical / Hydraulic / Pneumatic
        ReportedBy      VARCHAR(100)    NOT NULL,       -- name (operator or staff)
        Problem         VARCHAR(500)    NOT NULL,
        AttendedBy      INT             NULL,           -- maintenance staff
        RepairStart     DATETIME2(0)    NULL,
        RepairEnd       DATETIME2(0)    NULL,
        RootCause       VARCHAR(500)    NULL,
        ActionTaken     VARCHAR(500)    NULL,
        SparesUsed      VARCHAR(500)    NULL,
        Remarks         VARCHAR(500)    NULL,
        PMSheetNo       INT             NULL,           -- raised from a PM check sheet
        BreakdownStatus VARCHAR(20)     NOT NULL,       -- Open / In Progress / Closed

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_BreakdownMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_BreakdownMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_BreakdownMst_LockStatus   DEFAULT ('N'),
        LockCounter     INT             NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_BreakdownMst PRIMARY KEY CLUSTERED (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_BreakdownMst_Machine' AND object_id = OBJECT_ID(N'dbo.BreakdownMst'))
    CREATE NONCLUSTERED INDEX IX_BreakdownMst_Machine
        ON dbo.BreakdownMst (SubscID, MachineNo, DeleteStatus) INCLUDE (BreakdownStatus, BreakdownOn);
GO

/*------------------------------------------------------------------------------
  4. FN_FYCode  -  financial-year code of a date: 2026-10-14 -> '2627'
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_FYCode (@d DATE)
RETURNS CHAR(4)
AS
BEGIN
    DECLARE @s INT = YEAR(@d) - CASE WHEN MONTH(@d) >= 4 THEN 0 ELSE 1 END;
    RETURN RIGHT('0' + CONVERT(VARCHAR(2), @s % 100), 2)
         + RIGHT('0' + CONVERT(VARCHAR(2), (@s + 1) % 100), 2);
END
GO

/*------------------------------------------------------------------------------
  5. SP_PMChecklist

  @MachinesJson  [3, 9]
  @PointsJson    [{ "transSlNo": 0, "frequency": "Daily", "category": "General",
                    "slNo": 1, "checkPoint": "Machine cleaning" }, ...]
                 A transSlNo of one of this checklist's live points keeps that
                 point (and its history); 0 / unknown adds a new one; a live
                 point not in the list is soft-deleted.
  Refusals: @ID 0 + @Message; a duplicate name answers 'duplicate fields'.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PMChecklist', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PMChecklist AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PMChecklist
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @ChecklistName  VARCHAR(100),
    @DocNo          VARCHAR(30),
    @RevNo          VARCHAR(10),
    @RevDate        DATE,
    @Remarks        VARCHAR(200),
    @LockStatus     CHAR(1),
    @MachinesJson   NVARCHAR(MAX),
    @PointsJson     NVARCHAR(MAX),
    @SystemIP       VARCHAR(20),
    @UserNo         INT,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Insert' | 'Update' | 'Delete'
    @Message        NVARCHAR(400)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    IF (@Status IN ('Update', 'Delete')
        AND NOT EXISTS (SELECT 1 FROM dbo.PMChecklistMst
                         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'))
    BEGIN SET @Message = N'Checklist not found.'; RETURN; END

    IF (@Status = 'Delete')
    BEGIN
        DECLARE @Used INT, @Name VARCHAR(100);
        SELECT @Name = ChecklistName FROM dbo.PMChecklistMst WHERE MTransNo = @MTransNo;
        SELECT @Used = COUNT(*) FROM dbo.PMSheetMst
         WHERE ChecklistNo = @MTransNo AND DeleteStatus = 'N';
        IF (@Used > 0)
        BEGIN
            SET @Message = NCHAR(8220) + @Name + NCHAR(8221) + N' is used by ' + CONVERT(NVARCHAR(10), @Used)
                         + N' check sheet(s). Mark it Inactive instead.';
            RETURN;
        END

        UPDATE dbo.PMChecklistMst
           SET DeleteStatus = 'Y',
               DeleteReason = ISNULL(NULLIF(LTRIM(RTRIM(@DeleteReason)), ''), 'No reason provided'),
               SystemIP     = @SystemIP,
               ModifiedBy   = @UserNo,
               ModifiedOn   = SYSDATETIME()
         WHERE MTransNo = @MTransNo;

        SET @ID = @MTransNo;
        SET @Message = N'Checklist deleted successfully.';
        RETURN;
    END

    IF (@Status NOT IN ('Insert', 'Update'))
    BEGIN SET @Message = N'Invalid status.'; RETURN; END

    SET @ChecklistName = NULLIF(LTRIM(RTRIM(@ChecklistName)), '');
    SET @LockStatus = CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'Y' END;
    IF (@Status = 'Insert') SET @MTransNo = 0;

    IF (@ChecklistName IS NULL)
    BEGIN SET @Message = N'Checklist Name is required.'; RETURN; END

    DECLARE @Machines TABLE (MachineNo INT PRIMARY KEY);
    INSERT INTO @Machines (MachineNo)
    SELECT DISTINCT TRY_CONVERT(INT, value) FROM OPENJSON(ISNULL(@MachinesJson, N'[]'))
     WHERE TRY_CONVERT(INT, value) > 0;

    IF NOT EXISTS (SELECT 1 FROM @Machines)
    BEGIN SET @Message = N'Select at least one machine for the checklist.'; RETURN; END

    IF EXISTS (SELECT 1 FROM @Machines x
                WHERE NOT EXISTS (SELECT 1 FROM dbo.MachineMst m
                                   WHERE m.MTransNo = x.MachineNo AND m.SubscID = @SubscID
                                     AND ISNULL(m.DeleteStatus, 'N') = 'N'))
    BEGIN SET @Message = N'One of the selected machines no longer exists.'; RETURN; END

    DECLARE @Points TABLE
    (
        TransSlNo INT, Frequency VARCHAR(20), Category VARCHAR(20), SlNo INT, [CheckPoint] VARCHAR(200)
    );
    INSERT INTO @Points (TransSlNo, Frequency, Category, SlNo, [CheckPoint])
    SELECT ISNULL(transSlNo, 0), frequency, category, ISNULL(slNo, 0), LTRIM(RTRIM([checkPoint]))
      FROM OPENJSON(ISNULL(@PointsJson, N'[]'))
      WITH (transSlNo INT, frequency VARCHAR(20), category VARCHAR(20), slNo INT, [checkPoint] VARCHAR(200))
     WHERE NULLIF(LTRIM(RTRIM([checkPoint])), '') IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM @Points)
    BEGIN SET @Message = N'Add at least one check point.'; RETURN; END

    IF EXISTS (SELECT 1 FROM dbo.PMChecklistMst
                WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                  AND ChecklistName = @ChecklistName AND MTransNo <> @MTransNo)
    BEGIN SET @Message = N'duplicate fields'; RETURN; END

    -- A machine follows one active checklist only.
    IF (@LockStatus = 'Y')
    BEGIN
        DECLARE @ClashCode VARCHAR(20), @ClashList VARCHAR(100);
        SELECT TOP (1) @ClashCode = m.MachineCode, @ClashList = c.ChecklistName
          FROM @Machines x
          JOIN dbo.PMChecklistMachine cm ON cm.MachineNo = x.MachineNo
          JOIN dbo.PMChecklistMst c      ON c.MTransNo = cm.ChecklistNo
          JOIN dbo.MachineMst m          ON m.MTransNo = x.MachineNo
         WHERE c.SubscID = @SubscID AND c.DeleteStatus = 'N' AND c.LockStatus = 'Y'
           AND c.MTransNo <> @MTransNo
         ORDER BY m.MachineCode;
        IF (@ClashCode IS NOT NULL)
        BEGIN
            SET @Message = CONVERT(NVARCHAR(20), @ClashCode) + N' is already covered by ' + NCHAR(8220) + @ClashList
                         + NCHAR(8221) + N'. A machine can follow only one active checklist.';
            RETURN;
        END
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            INSERT INTO dbo.PMChecklistMst
                (ChecklistName, DocNo, RevNo, RevDate, Remarks, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, LockStatus, LockCounter, SubscID)
            VALUES
                (@ChecklistName, ISNULL(NULLIF(LTRIM(RTRIM(@DocNo)), ''), 'MNT/F/01'),
                 ISNULL(NULLIF(LTRIM(RTRIM(@RevNo)), ''), '00'), ISNULL(@RevDate, CAST(GETDATE() AS DATE)),
                 NULLIF(LTRIM(RTRIM(@Remarks)), ''), @SystemIP, @UserNo, SYSDATETIME(),
                 'N', @LockStatus, 1, @SubscID);
            SET @MTransNo = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.PMChecklistMst
               SET ChecklistName = @ChecklistName,
                   DocNo         = ISNULL(NULLIF(LTRIM(RTRIM(@DocNo)), ''), 'MNT/F/01'),
                   RevNo         = ISNULL(NULLIF(LTRIM(RTRIM(@RevNo)), ''), '00'),
                   RevDate       = ISNULL(@RevDate, CAST(GETDATE() AS DATE)),
                   Remarks       = NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                   LockStatus    = @LockStatus,
                   SystemIP      = @SystemIP,
                   ModifiedBy    = @UserNo,
                   ModifiedOn    = SYSDATETIME(),
                   LockCounter   = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo;
        END

        -- Machines: replaced.
        DELETE FROM dbo.PMChecklistMachine WHERE ChecklistNo = @MTransNo;
        INSERT INTO dbo.PMChecklistMachine (ChecklistNo, MachineNo, SubscID)
        SELECT @MTransNo, MachineNo, @SubscID FROM @Machines;

        -- Points: kept by id, added, or soft-deleted.
        UPDATE p
           SET Frequency = x.Frequency, Category = x.Category, SlNo = x.SlNo, [CheckPoint] = x.[CheckPoint]
          FROM dbo.PMChecklistPoint p
          JOIN @Points x ON x.TransSlNo = p.TransSlNo
         WHERE p.ChecklistNo = @MTransNo AND p.DeleteStatus = 'N';

        UPDATE p
           SET DeleteStatus = 'Y'
          FROM dbo.PMChecklistPoint p
         WHERE p.ChecklistNo = @MTransNo AND p.DeleteStatus = 'N'
           AND NOT EXISTS (SELECT 1 FROM @Points x WHERE x.TransSlNo = p.TransSlNo);

        INSERT INTO dbo.PMChecklistPoint (ChecklistNo, Frequency, Category, SlNo, [CheckPoint], DeleteStatus, SubscID)
        SELECT @MTransNo, x.Frequency, x.Category, x.SlNo, x.[CheckPoint], 'N', @SubscID
          FROM @Points x
         WHERE NOT EXISTS (SELECT 1 FROM dbo.PMChecklistPoint p
                            WHERE p.TransSlNo = x.TransSlNo AND p.ChecklistNo = @MTransNo
                              AND p.DeleteStatus = 'N');

        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = CASE WHEN @Status = 'Insert' THEN N'Checklist saved successfully.'
                            ELSE N'Checklist updated successfully.' END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6. SP_PMSheet

  Insert: @PointsJson is the snapshot of the points due that month (worked
          out by the API): [{ "pointNo", "slNo", "checkPoint", "frequency",
          "category" }]. The form block (DocNo / RevNo / RevDate) is copied
          from the checklist.
  Insert / Update:
          @EntriesJson  [{ "pointNo", "slot", "status", "doneOn", "doneBy" }]
          @DaysJson     [{ "day", "idle", "operatorNo" }]
          @RemarksJson  [{ "pointNo", "remarks" }]
          The results, days and remarks are replaced. A Dept-approved sheet
          is locked.
  Delete: a draft only; breakdowns raised from it are unlinked.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PMSheet', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PMSheet AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PMSheet
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MachineNo      INT,
    @Year           INT,
    @Month          INT,
    @Remarks        VARCHAR(500),
    @PointsJson     NVARCHAR(MAX),
    @EntriesJson    NVARCHAR(MAX),
    @DaysJson       NVARCHAR(MAX),
    @RemarksJson    NVARCHAR(MAX),
    @SystemIP       VARCHAR(20),
    @UserNo         INT,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Insert' | 'Update' | 'Delete'
    @Message        NVARCHAR(400)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @DeptSupBy INT;

    IF (@Status IN ('Update', 'Delete'))
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.PMSheetMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Check sheet not found.'; RETURN; END

        SELECT @DeptSupBy = DeptSupBy FROM dbo.PMSheetMst WHERE MTransNo = @MTransNo;

        IF (@DeptSupBy IS NOT NULL)
        BEGIN
            SET @Message = CASE WHEN @Status = 'Delete'
                                THEN N'An approved check sheet cannot be deleted.'
                                ELSE N'This check sheet is approved and locked.' END;
            RETURN;
        END
    END

    IF (@Status = 'Delete')
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;
            UPDATE dbo.PMSheetMst
               SET DeleteStatus = 'Y',
                   DeleteReason = ISNULL(NULLIF(LTRIM(RTRIM(@DeleteReason)), ''), 'No reason provided'),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @UserNo,
                   ModifiedOn   = SYSDATETIME()
             WHERE MTransNo = @MTransNo;

            UPDATE dbo.BreakdownMst SET PMSheetNo = NULL WHERE PMSheetNo = @MTransNo;
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH

        SET @ID = @MTransNo;
        SET @Message = N'Check sheet deleted successfully.';
        RETURN;
    END

    IF (@Status NOT IN ('Insert', 'Update'))
    BEGIN SET @Message = N'Invalid status.'; RETURN; END

    DECLARE @ChecklistNo INT, @MachineCode VARCHAR(20);

    IF (@Status = 'Insert')
    BEGIN
        SELECT @MachineCode = MachineCode FROM dbo.MachineMst
         WHERE MTransNo = @MachineNo AND SubscID = @SubscID AND ISNULL(DeleteStatus, 'N') = 'N';
        IF (@MachineCode IS NULL)
        BEGIN SET @Message = N'Select a machine.'; RETURN; END
        IF (@Year IS NULL OR @Month NOT BETWEEN 1 AND 12)
        BEGIN SET @Message = N'Select the month.'; RETURN; END
        IF (DATEFROMPARTS(@Year, @Month, 1) > CAST(GETDATE() AS DATE))
        BEGIN SET @Message = N'A check sheet cannot be opened for a future month.'; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.PMSheetMst
                    WHERE SubscID = @SubscID AND MachineNo = @MachineNo AND [Year] = @Year
                      AND [Month] = @Month AND DeleteStatus = 'N')
        BEGIN SET @Message = N'duplicate fields'; RETURN; END

        SELECT TOP (1) @ChecklistNo = c.MTransNo
          FROM dbo.PMChecklistMst c
          JOIN dbo.PMChecklistMachine cm ON cm.ChecklistNo = c.MTransNo
         WHERE c.SubscID = @SubscID AND c.DeleteStatus = 'N' AND c.LockStatus = 'Y'
           AND cm.MachineNo = @MachineNo
         ORDER BY c.MTransNo;
        IF (@ChecklistNo IS NULL)
        BEGIN SET @Message = N'No active PM checklist covers ' + CONVERT(NVARCHAR(20), @MachineCode) + N'.'; RETURN; END

        IF NOT EXISTS (SELECT 1 FROM OPENJSON(ISNULL(@PointsJson, N'[]')))
        BEGIN SET @Message = N'No check point of the checklist is due this month.'; RETURN; END
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            DECLARE @FY CHAR(4) = dbo.FN_FYCode(DATEFROMPARTS(@Year, @Month, 1));
            DECLARE @Prefix VARCHAR(10) = 'PM-' + @FY + '-';
            DECLARE @Next INT;
            SELECT @Next = ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(SheetNo, LEN(@Prefix) + 1, 10))), 0) + 1
              FROM dbo.PMSheetMst WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID AND SheetNo LIKE @Prefix + '%';

            INSERT INTO dbo.PMSheetMst
                (SheetNo, MachineNo, ChecklistNo, DocNo, RevNo, RevDate, [Year], [Month], Remarks,
                 SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT @Prefix + RIGHT('0000' + CONVERT(VARCHAR(10), @Next), CASE WHEN @Next > 9999 THEN LEN(@Next) ELSE 4 END),
                   @MachineNo, c.MTransNo, c.DocNo, c.RevNo, c.RevDate, @Year, @Month,
                   NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                   @SystemIP, @UserNo, SYSDATETIME(), 'N', 'N', 1, @SubscID
              FROM dbo.PMChecklistMst c
             WHERE c.MTransNo = @ChecklistNo;

            SET @MTransNo = CONVERT(INT, SCOPE_IDENTITY());

            INSERT INTO dbo.PMSheetPoint (MTransNo, PointNo, SlNo, [CheckPoint], Frequency, Category)
            SELECT @MTransNo, pointNo, slNo, [checkPoint], frequency, category
              FROM OPENJSON(@PointsJson)
              WITH (pointNo INT, slNo INT, [checkPoint] VARCHAR(200), frequency VARCHAR(20), category VARCHAR(20));
        END
        ELSE
        BEGIN
            UPDATE dbo.PMSheetMst
               SET Remarks     = NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                   SystemIP    = @SystemIP,
                   ModifiedBy  = @UserNo,
                   ModifiedOn  = SYSDATETIME(),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo;

            DELETE FROM dbo.PMSheetEntry WHERE MTransNo = @MTransNo;
            DELETE FROM dbo.PMSheetDay   WHERE MTransNo = @MTransNo;
        END

        INSERT INTO dbo.PMSheetEntry (MTransNo, PointNo, Slot, EntryStatus, DoneOn, DoneBy)
        SELECT @MTransNo, e.pointNo, e.slot, e.status, e.doneOn, NULLIF(e.doneBy, 0)
          FROM OPENJSON(ISNULL(@EntriesJson, N'[]'))
          WITH (pointNo INT, slot INT, status VARCHAR(3), doneOn DATE, doneBy INT) e
         WHERE EXISTS (SELECT 1 FROM dbo.PMSheetPoint p WHERE p.MTransNo = @MTransNo AND p.PointNo = e.pointNo);

        INSERT INTO dbo.PMSheetDay (MTransNo, DayNo, IsIdle, OperatorNo)
        SELECT @MTransNo, d.[day], ISNULL(d.idle, 0), NULLIF(d.operatorNo, 0)
          FROM OPENJSON(ISNULL(@DaysJson, N'[]'))
          WITH ([day] INT, idle BIT, operatorNo INT) d;

        UPDATE p
           SET Remarks = r.remarks
          FROM dbo.PMSheetPoint p
          LEFT JOIN OPENJSON(ISNULL(@RemarksJson, N'[]'))
               WITH (pointNo INT, remarks VARCHAR(1000)) r ON r.pointNo = p.PointNo
         WHERE p.MTransNo = @MTransNo;

        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = CASE WHEN @Status = 'Insert' THEN N'Check sheet saved successfully.'
                            ELSE N'Check sheet updated successfully.' END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  7. SP_PMSheetApprove

  'Dept' (a Production Supervisor) first, then 'Maint' (a Maintenance
  Supervisor); only once the sheet's month is over. Dept approval locks it.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PMSheetApprove', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PMSheetApprove AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PMSheetApprove
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @Level          VARCHAR(10),    -- 'Dept' | 'Maint'
    @ApprovedBy     INT,
    @SystemIP       VARCHAR(20),
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(400)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Y INT, @M INT, @Dept INT, @Mnt INT, @MonthEnd DATE, @Today DATE = CAST(GETDATE() AS DATE);

    SELECT @Y = [Year], @M = [Month], @Dept = DeptSupBy, @Mnt = MntSupBy
      FROM dbo.PMSheetMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    IF (@Y IS NULL)
    BEGIN SET @Message = N'Check sheet not found.'; RETURN; END

    SET @MonthEnd = EOMONTH(DATEFROMPARTS(@Y, @M, 1));
    IF (@MonthEnd >= @Today)
    BEGIN
        SET @Message = N'The month is not over yet ' + NCHAR(8212) + N' the sheet can be approved after '
                     + CONVERT(NVARCHAR(10), @MonthEnd, 105) + N'.';
        RETURN;
    END

    DECLARE @Role VARCHAR(50);
    SELECT @Role = MasterType FROM dbo.OtherMaster
     WHERE MTransNo = @ApprovedBy AND SubscID = @SubscID AND ISNULL(DeleteStatus, 'N') = 'N';

    IF (@Level = 'Dept')
    BEGIN
        IF (@Dept IS NOT NULL)
        BEGIN SET @Message = N'The department supervisor has already approved this sheet.'; RETURN; END
        IF (ISNULL(@Role, '') <> 'Production Supervisor')
        BEGIN SET @Message = N'Select the department supervisor.'; RETURN; END

        UPDATE dbo.PMSheetMst
           SET DeptSupBy = @ApprovedBy, DeptSupOn = @Today, LockStatus = 'Y',
               SystemIP = @SystemIP, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(),
               LockCounter = ISNULL(LockCounter, 0) + 1
         WHERE MTransNo = @MTransNo AND DeptSupBy IS NULL;
    END
    ELSE IF (@Level = 'Maint')
    BEGIN
        IF (@Dept IS NULL)
        BEGIN SET @Message = N'The department supervisor must approve the sheet first.'; RETURN; END
        IF (@Mnt IS NOT NULL)
        BEGIN SET @Message = N'This sheet is already approved.'; RETURN; END
        IF (ISNULL(@Role, '') <> 'Maintenance Supervisor')
        BEGIN SET @Message = N'Select the maintenance supervisor.'; RETURN; END

        UPDATE dbo.PMSheetMst
           SET MntSupBy = @ApprovedBy, MntSupOn = @Today,
               SystemIP = @SystemIP, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(),
               LockCounter = ISNULL(LockCounter, 0) + 1
         WHERE MTransNo = @MTransNo AND MntSupBy IS NULL;
    END
    ELSE
    BEGIN SET @Message = N'Unknown approval level.'; RETURN; END

    IF @@ROWCOUNT = 0
    BEGIN SET @Message = N'The sheet was changed by someone else, reload it.'; RETURN; END

    SET @ID = @MTransNo;
    SET @Message = N'Check sheet approved.';
END
GO

/*------------------------------------------------------------------------------
  8. SP_Breakdown

  One live (Open / In Progress) breakdown per machine. Only an Open
  breakdown that nobody has attended can be deleted. The field rules of each
  status are checked by the API; the number is allocated here.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_Breakdown', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_Breakdown AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_Breakdown
    @ID              INT             OUTPUT,
    @MTransNo        INT,
    @MachineNo       INT,
    @BreakdownOn     DATETIME2(0),
    @BreakdownType   VARCHAR(20),
    @ReportedBy      VARCHAR(100),
    @Problem         VARCHAR(500),
    @AttendedBy      INT,
    @RepairStart     DATETIME2(0),
    @RepairEnd       DATETIME2(0),
    @RootCause       VARCHAR(500),
    @ActionTaken     VARCHAR(500),
    @SparesUsed      VARCHAR(500),
    @Remarks         VARCHAR(500),
    @PMSheetNo       INT,
    @BreakdownStatus VARCHAR(20),
    @SystemIP        VARCHAR(20),
    @UserNo          INT,
    @DeleteReason    VARCHAR(250),
    @SubscID         INT,
    @Status          VARCHAR(20),    -- 'Insert' | 'Update' | 'Delete'
    @Message         NVARCHAR(400)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @CurStatus VARCHAR(20), @CurAttended INT;

    IF (@Status IN ('Update', 'Delete'))
    BEGIN
        SELECT @CurStatus = BreakdownStatus, @CurAttended = AttendedBy
          FROM dbo.BreakdownMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF (@CurStatus IS NULL)
        BEGIN SET @Message = N'Breakdown not found.'; RETURN; END
    END

    IF (@Status = 'Delete')
    BEGIN
        IF (@CurStatus <> 'Open' OR @CurAttended IS NOT NULL)
        BEGIN SET @Message = N'Only an Open breakdown that nobody has attended can be deleted.'; RETURN; END

        UPDATE dbo.BreakdownMst
           SET DeleteStatus = 'Y',
               DeleteReason = ISNULL(NULLIF(LTRIM(RTRIM(@DeleteReason)), ''), 'No reason provided'),
               SystemIP     = @SystemIP,
               ModifiedBy   = @UserNo,
               ModifiedOn   = SYSDATETIME()
         WHERE MTransNo = @MTransNo;

        SET @ID = @MTransNo;
        SET @Message = N'Breakdown deleted successfully.';
        RETURN;
    END

    IF (@Status NOT IN ('Insert', 'Update'))
    BEGIN SET @Message = N'Invalid status.'; RETURN; END

    DECLARE @MachineCode VARCHAR(20);
    SELECT @MachineCode = MachineCode FROM dbo.MachineMst
     WHERE MTransNo = @MachineNo AND SubscID = @SubscID AND ISNULL(DeleteStatus, 'N') = 'N';
    IF (@MachineCode IS NULL)
    BEGIN SET @Message = N'Select the machine.'; RETURN; END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- One live breakdown per machine.
        IF (@BreakdownStatus <> 'Closed')
        BEGIN
            DECLARE @LiveNo VARCHAR(20);
            SELECT TOP (1) @LiveNo = BreakdownNo
              FROM dbo.BreakdownMst WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID AND MachineNo = @MachineNo AND DeleteStatus = 'N'
               AND BreakdownStatus <> 'Closed' AND MTransNo <> ISNULL(@MTransNo, 0);
            IF (@LiveNo IS NOT NULL)
            BEGIN
                ROLLBACK TRANSACTION;
                SET @Message = CONVERT(NVARCHAR(20), @MachineCode) + N' already has an open breakdown ('
                             + CONVERT(NVARCHAR(20), @LiveNo) + N'). Close it first.';
                RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            DECLARE @Prefix VARCHAR(10) = 'BD-' + dbo.FN_FYCode(CAST(@BreakdownOn AS DATE)) + '-';
            DECLARE @Next INT;
            SELECT @Next = ISNULL(MAX(TRY_CONVERT(INT, SUBSTRING(BreakdownNo, LEN(@Prefix) + 1, 10))), 0) + 1
              FROM dbo.BreakdownMst WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID AND BreakdownNo LIKE @Prefix + '%';

            IF (@PMSheetNo IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.PMSheetMst
                                                        WHERE MTransNo = @PMSheetNo AND SubscID = @SubscID
                                                          AND DeleteStatus = 'N'))
                SET @PMSheetNo = NULL;

            INSERT INTO dbo.BreakdownMst
                (BreakdownNo, MachineNo, BreakdownOn, BreakdownType, ReportedBy, Problem, AttendedBy,
                 RepairStart, RepairEnd, RootCause, ActionTaken, SparesUsed, Remarks, PMSheetNo,
                 BreakdownStatus, SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
            VALUES
                (@Prefix + RIGHT('0000' + CONVERT(VARCHAR(10), @Next), CASE WHEN @Next > 9999 THEN LEN(@Next) ELSE 4 END),
                 @MachineNo, @BreakdownOn, @BreakdownType, @ReportedBy, @Problem, @AttendedBy,
                 @RepairStart, @RepairEnd, @RootCause, @ActionTaken, @SparesUsed, @Remarks, @PMSheetNo,
                 @BreakdownStatus, @SystemIP, @UserNo, SYSDATETIME(), 'N', 'N', 1, @SubscID);

            SET @MTransNo = CONVERT(INT, SCOPE_IDENTITY());
        END
        ELSE
        BEGIN
            UPDATE dbo.BreakdownMst
               SET MachineNo       = @MachineNo,
                   BreakdownOn     = @BreakdownOn,
                   BreakdownType   = @BreakdownType,
                   ReportedBy      = @ReportedBy,
                   Problem         = @Problem,
                   AttendedBy      = @AttendedBy,
                   RepairStart     = @RepairStart,
                   RepairEnd       = @RepairEnd,
                   RootCause       = @RootCause,
                   ActionTaken     = @ActionTaken,
                   SparesUsed      = @SparesUsed,
                   Remarks         = @Remarks,
                   BreakdownStatus = @BreakdownStatus,
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @UserNo,
                   ModifiedOn      = SYSDATETIME(),
                   LockCounter     = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo;
        END

        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = CASE WHEN @Status = 'Insert' THEN N'Breakdown saved successfully.'
                            ELSE N'Breakdown updated successfully.' END;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

PRINT '55_Maintenance.sql applied.';
GO
