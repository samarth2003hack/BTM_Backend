/*==============================================================================
  59. PRODUCTION PLANNING  -  Masters/WorkCalendar.html,
                              Production/ProductionPlan.html,
                              Production/ProductionSchedule.html,
                              PurchaseReports/PlanVsActualReport.html,
                              PurchaseReports/RMRequirementReport.html,
                              PurchaseReports/RMStockStatement.html,
                              SalesReports/PendingOrderReport.html

  Takes the planning pages off their dummy store
  (Config/js/planning-mock-store.js) and onto real tables.

  WORK CALENDAR  (WorkCalendarMst + WorkCalendarHoliday)
      One row per month + shift team: shift start/end, break, weekly off and
      the month's holidays. The calendar holds NO machines - the hammers and
      their shifts / efficiency come from Machine Master (SQL 58).
      Unique on month + shift team (live rows). A month a live production
      plan uses cannot be deleted (mark it Inactive instead).

  PRODUCTION PLAN  (ProductionPlanMst / Week / Job / Line / Day / Reschedule)
      Mst      one plan per month (PP/26-27/0001), RevNo, PlanStatus
               Draft -> Approved -> Released -> Closed.
      Week     Open -> Firm -> Released, one row per week touched.
      Job      one die setup on one hammer (Main | Alternate), days required
               in standard shift-days, optional fixed start.
      Line     an OA line (OAYear / OANo / OAID = OADtl.SlNo, OALinkNo =
               OADtl.TransSlNo) or a STOCK line, with its plan qty and route.
      Day      the day-wise split, worked out by the API with the same rules
               as UI_Components/widgets/plan-schedule.js and stored here, so
               every report reads one schedule.
      Reschedule  the log of every change to an approved / released plan.

      Rules (procedures):
        - header: one live plan per month (not Closed); a Released / Closed
          plan changes only by Reschedule; a new RevNo puts the plan back to
          Draft and re-opens every week.
        - details: replace-all, Draft only.
        - Approve: Draft -> Approved.   FirmWeek: Open -> Firm (Approved+).
        - ReleaseWeek: Firm -> Released; creates one JobCardMst per MAIN plan
          line of the jobs starting in the week (the API passes the rows) and
          links it back (JobCardMst.PlanID / PlanJobSeq / PlanLineSlNo /
          PlannedStart / PlannedEnd / MachineNo / DieNo - columns from SQL 58).
          A STOCK line's job card carries OANo 0 (no customer order).
        - Reschedule: Approved / Released only; fixed start + days; logged.
        - Delete: Draft only (soft).

  Objects:
      WorkCalendarMst, WorkCalendarHoliday        new tables
      ProductionPlanMst, ProductionPlanWeek,
      ProductionPlanJob, ProductionPlanLine,
      ProductionPlanDay, ProductionPlanReschedule new tables
      SP_WorkCalendar             @Status 'Insert' | 'Update' | 'Delete'
      SP_ProductionPlanMst        @Status 'Insert' | 'Update' | 'Delete'
      SP_ProductionPlanDtl        jobs + lines JSON, replace-all
      SP_ProductionPlanDays       day split JSON, replace-all
      SP_ProductionPlanAction     @Action 'Approve' | 'FirmWeek'
      SP_ProductionPlanRelease    week release + job cards
      SP_ProductionPlanReschedule fixed start + days, logged

  Run AFTER 58_PlanningFieldsExistingPages.sql (JobCardMst plan columns,
  MachineMst shifts / efficiency) and BEFORE deploying the API build with
  WorkCalendarController / ProductionPlanController.
  No menu rows here (the menu is seeded by hand).

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/
USE EMSSoft;
GO
SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. WORK CALENDAR
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.WorkCalendarMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkCalendarMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        CalMonth        CHAR(7)         NOT NULL,       -- 2026-11
        ShiftName       VARCHAR(50)     NOT NULL,       -- shift team, e.g. 1st Team
        ShiftStart      CHAR(5)         NOT NULL,       -- 08:00
        ShiftEnd        CHAR(5)         NOT NULL,       -- 19:30
        BreakMin        INT             NOT NULL CONSTRAINT DF_WorkCalendarMst_BreakMin     DEFAULT (0),
        WeeklyOff       VARCHAR(10)     NOT NULL CONSTRAINT DF_WorkCalendarMst_WeeklyOff    DEFAULT ('Sunday'),
        Remarks         VARCHAR(250)    NULL,
        SystemIP        VARCHAR(20)     NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_WorkCalendarMst_LockStatus   DEFAULT ('Y'),
        LockCounter     INT             NOT NULL CONSTRAINT DF_WorkCalendarMst_LockCounter  DEFAULT (0),
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_WorkCalendarMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_WorkCalendarMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_WorkCalendarMst PRIMARY KEY CLUSTERED (MTransNo)
    );
    CREATE UNIQUE INDEX UQ_WorkCalendarMst_MonthShift
        ON dbo.WorkCalendarMst (SubscID, CalMonth, ShiftName) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.WorkCalendarHoliday', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkCalendarHoliday
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- WorkCalendarMst
        HoliDate        DATE            NOT NULL,
        Reason          VARCHAR(100)    NULL,
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_WorkCalendarHoliday_CreatedOn    DEFAULT (SYSDATETIME()),
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_WorkCalendarHoliday_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_WorkCalendarHoliday PRIMARY KEY CLUSTERED (TransSlNo)
    );
    CREATE INDEX IX_WorkCalendarHoliday_Cal ON dbo.WorkCalendarHoliday (MTransNo) WHERE DeleteStatus = 'N';
END;
GO

/*------------------------------------------------------------------------------
  2. PRODUCTION PLAN
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.ProductionPlanMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        DocYear         INT             NOT NULL,       -- financial-year code, 2627
        DocNo           INT             NOT NULL,       -- 1, 2, 3 ... per SubscID + DocYear
        PlanNo          VARCHAR(30)     NOT NULL,       -- PP/26-27/0001
        PlanDate        DATE            NOT NULL,
        PlanMonth       CHAR(7)         NOT NULL,       -- 2026-11
        RevNo           INT             NOT NULL CONSTRAINT DF_ProductionPlanMst_RevNo        DEFAULT (0),
        PlanStatus      VARCHAR(12)     NOT NULL CONSTRAINT DF_ProductionPlanMst_PlanStatus   DEFAULT ('Draft'),
        Remarks         VARCHAR(250)    NULL,
        SystemIP        VARCHAR(20)     NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_ProductionPlanMst_LockStatus   DEFAULT ('Y'),
        LockCounter     INT             NOT NULL CONSTRAINT DF_ProductionPlanMst_LockCounter  DEFAULT (0),
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_ProductionPlanMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_ProductionPlanMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_ProductionPlanMst_Status CHECK (PlanStatus IN ('Draft', 'Approved', 'Released', 'Closed'))
    );
    CREATE UNIQUE INDEX UQ_ProductionPlanMst_No ON dbo.ProductionPlanMst (SubscID, DocYear, DocNo);
    CREATE INDEX IX_ProductionPlanMst_Month ON dbo.ProductionPlanMst (SubscID, PlanMonth) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.ProductionPlanWeek', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanWeek
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- ProductionPlanMst
        WeekNo          INT             NOT NULL,       -- W1, W2 ... of the plan month
        WeekStatus      VARCHAR(10)     NOT NULL CONSTRAINT DF_ProductionPlanWeek_Status DEFAULT ('Open'),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanWeek PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT CK_ProductionPlanWeek_Status CHECK (WeekStatus IN ('Open', 'Firm', 'Released'))
    );
    CREATE UNIQUE INDEX UQ_ProductionPlanWeek ON dbo.ProductionPlanWeek (MTransNo, WeekNo);
END;
GO

IF OBJECT_ID(N'dbo.ProductionPlanJob', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanJob
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- ProductionPlanMst
        JobSeq          INT             NOT NULL,       -- forging sequence, 1, 2, 3 ...
        PlanType        VARCHAR(10)     NOT NULL,       -- Main | Alternate
        PartNo          INT             NOT NULL,       -- finished part
        DieNo           INT             NULL,           -- DieMst
        MachineNo       INT             NULL,           -- the hammer (MachineMst)
        DaysRequired    NUMERIC(9, 2)   NOT NULL CONSTRAINT DF_ProductionPlanJob_Days DEFAULT (0),
        FixedStart      DATE            NULL,
        Remarks         VARCHAR(250)    NULL,
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_ProductionPlanJob_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_ProductionPlanJob_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanJob PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT CK_ProductionPlanJob_Type CHECK (PlanType IN ('Main', 'Alternate'))
    );
    CREATE INDEX IX_ProductionPlanJob_Plan ON dbo.ProductionPlanJob (MTransNo, JobSeq) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.ProductionPlanLine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanLine
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- ProductionPlanMst
        JobSeq          INT             NOT NULL,
        LineSlNo        INT             NOT NULL,       -- 1, 2, 3 ... inside the job
        Source          VARCHAR(5)      NOT NULL,       -- OA | STOCK
        OAYear          INT             NULL,           -- OAMst.DocYear
        OANo            INT             NULL,           -- OAMst.DocNo
        OAID            INT             NULL,           -- OADtl.SlNo
        OALinkNo        INT             NULL,           -- OADtl.TransSlNo (current revision)
        Grade           VARCHAR(50)     NOT NULL,
        PlanQty         NUMERIC(14, 2)  NOT NULL,
        Rate            NUMERIC(14, 2)  NOT NULL CONSTRAINT DF_ProductionPlanLine_Rate DEFAULT (0),
        RouteNo         INT             NULL,           -- RoutingChartMst
        Reason          VARCHAR(50)     NULL,           -- why a STOCK line has no order
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_ProductionPlanLine_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_ProductionPlanLine_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanLine PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT CK_ProductionPlanLine_Source CHECK (Source IN ('OA', 'STOCK'))
    );
    CREATE INDEX IX_ProductionPlanLine_Plan ON dbo.ProductionPlanLine (MTransNo, JobSeq, LineSlNo) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_ProductionPlanLine_OA   ON dbo.ProductionPlanLine (SubscID, OAYear, OANo, OAID) WHERE DeleteStatus = 'N';
END;
GO

-- Derived data: rewritten whole on every save / reschedule, so no audit trail.
IF OBJECT_ID(N'dbo.ProductionPlanDay', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanDay
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- ProductionPlanMst
        JobSeq          INT             NOT NULL,
        LineSlNo        INT             NOT NULL,
        PlanDate        DATE            NOT NULL,
        PlanQty         NUMERIC(14, 2)  NOT NULL,
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanDay PRIMARY KEY CLUSTERED (TransSlNo)
    );
    CREATE INDEX IX_ProductionPlanDay_Plan ON dbo.ProductionPlanDay (MTransNo, JobSeq, LineSlNo, PlanDate);
END;
GO

IF OBJECT_ID(N'dbo.ProductionPlanReschedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionPlanReschedule
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- ProductionPlanMst
        JobSeq          INT             NOT NULL,
        FromText        VARCHAR(100)    NULL,           -- "2026-11-10 · 2 d"
        ToText          VARCHAR(100)    NULL,
        Reason          VARCHAR(250)    NOT NULL,
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_ProductionPlanReschedule_CreatedOn DEFAULT (SYSDATETIME()),
        SubscID         INT             NOT NULL,
        CONSTRAINT PK_ProductionPlanReschedule PRIMARY KEY CLUSTERED (TransSlNo)
    );
    CREATE INDEX IX_ProductionPlanReschedule_Plan ON dbo.ProductionPlanReschedule (MTransNo);
END;
GO

/*------------------------------------------------------------------------------
  3. SP_WorkCalendar
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_WorkCalendar', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_WorkCalendar AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_WorkCalendar
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @CalMonth       CHAR(7),
    @ShiftName      VARCHAR(50),
    @ShiftStart     CHAR(5),
    @ShiftEnd       CHAR(5),
    @BreakMin       INT,
    @WeeklyOff      VARCHAR(10),
    @Holidays       NVARCHAR(MAX),          -- JSON [{ "holiDate": "2026-11-10", "reason": "..." }]
    @Remarks        VARCHAR(250),
    @LockStatus     CHAR(1),
    @UserNo         INT,
    @SystemIP       VARCHAR(20),
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF @CalMonth IS NULL OR @CalMonth NOT LIKE '[12][0-9][0-9][0-9]-[01][0-9]'
            BEGIN SET @Message = N'Select the month.'; RETURN; END;
            IF LTRIM(RTRIM(ISNULL(@ShiftName, ''))) = ''
            BEGIN SET @Message = N'Enter the shift team name.'; RETURN; END;
            IF @ShiftStart NOT LIKE '[0-2][0-9]:[0-5][0-9]' OR @ShiftEnd NOT LIKE '[0-2][0-9]:[0-5][0-9]'
            BEGIN SET @Message = N'Enter the shift start and end time (HH:MM).'; RETURN; END;
            IF ISNULL(@BreakMin, 0) < 0
            BEGIN SET @Message = N'Break minutes cannot be negative.'; RETURN; END;
            IF @WeeklyOff NOT IN ('Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday')
            BEGIN SET @Message = N'Select the weekly off day.'; RETURN; END;
            IF @Status = 'Update' AND NOT EXISTS (SELECT 1 FROM dbo.WorkCalendarMst
                                                   WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN SET @Message = N'Work calendar not found.'; RETURN; END;
            -- The page reads "duplicate fields" to say the month + team exists.
            IF EXISTS (SELECT 1 FROM dbo.WorkCalendarMst
                        WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND CalMonth = @CalMonth
                          AND ShiftName = LTRIM(RTRIM(@ShiftName))
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'duplicate fields'; RETURN; END;
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            DECLARE @DelMonth CHAR(7);
            SELECT @DelMonth = CalMonth FROM dbo.WorkCalendarMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @DelMonth IS NULL BEGIN SET @Message = N'Work calendar not found.'; RETURN; END;
            DECLARE @UsedBy VARCHAR(30);
            SELECT TOP (1) @UsedBy = PlanNo FROM dbo.ProductionPlanMst
             WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND PlanMonth = @DelMonth AND PlanStatus <> 'Closed';
            IF @UsedBy IS NOT NULL
            BEGIN
                SET @Message = N'Production plan ' + @UsedBy + N' is planned on this month. Mark the calendar Inactive instead.';
                RETURN;
            END;
        END;

        DECLARE @Hol TABLE (HoliDate DATE NOT NULL PRIMARY KEY, Reason VARCHAR(100) NULL);
        IF @Status IN ('Insert', 'Update') AND ISJSON(ISNULL(@Holidays, N'[]')) = 1
            INSERT INTO @Hol (HoliDate, Reason)
            SELECT DISTINCT TRY_CONVERT(DATE, JSON_VALUE(value, '$.holiDate')), LEFT(MAX(JSON_VALUE(value, '$.reason')), 100)
              FROM OPENJSON(ISNULL(@Holidays, N'[]'))
             WHERE TRY_CONVERT(DATE, JSON_VALUE(value, '$.holiDate')) IS NOT NULL
             GROUP BY TRY_CONVERT(DATE, JSON_VALUE(value, '$.holiDate'));

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            INSERT INTO dbo.WorkCalendarMst
                   (CalMonth, ShiftName, ShiftStart, ShiftEnd, BreakMin, WeeklyOff, Remarks, SystemIP,
                    LockStatus, CreatedBy, CreatedOn, SubscID)
            VALUES (@CalMonth, LTRIM(RTRIM(@ShiftName)), @ShiftStart, @ShiftEnd, ISNULL(@BreakMin, 0), @WeeklyOff,
                    NULLIF(LTRIM(RTRIM(@Remarks)), ''), @SystemIP,
                    CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'Y' END, @UserNo, SYSDATETIME(), @SubscID);
            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Work calendar saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            UPDATE dbo.WorkCalendarMst
               SET CalMonth = @CalMonth, ShiftName = LTRIM(RTRIM(@ShiftName)), ShiftStart = @ShiftStart,
                   ShiftEnd = @ShiftEnd, BreakMin = ISNULL(@BreakMin, 0), WeeklyOff = @WeeklyOff,
                   Remarks = NULLIF(LTRIM(RTRIM(@Remarks)), ''), SystemIP = @SystemIP,
                   LockStatus = CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'Y' END,
                   LockCounter = LockCounter + 1, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            SET @ID = @MTransNo;
            SET @Message = N'Work calendar updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.WorkCalendarMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            UPDATE dbo.WorkCalendarHoliday
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            SET @ID = @MTransNo;
            SET @Message = N'Work calendar deleted successfully.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        -- Holidays: replace the whole list.
        IF @Status IN ('Insert', 'Update') AND @ID > 0
        BEGIN
            UPDATE dbo.WorkCalendarHoliday
               SET DeleteStatus = 'Y', DeleteReason = 'Replaced by new holiday list'
             WHERE MTransNo = @ID AND DeleteStatus = 'N';
            INSERT INTO dbo.WorkCalendarHoliday (MTransNo, HoliDate, Reason, CreatedBy, CreatedOn, SubscID)
            SELECT @ID, h.HoliDate, NULLIF(LTRIM(RTRIM(h.Reason)), ''), @UserNo, SYSDATETIME(), @SubscID
              FROM @Hol h
             WHERE CONVERT(CHAR(7), h.HoliDate, 126) = @CalMonth;
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  4. SP_ProductionPlanMst  - header
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanMst', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanMst AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @PlanDate       DATE,
    @PlanMonth      CHAR(7),
    @RevNo          INT,
    @PlanStatus     VARCHAR(12),
    @Remarks        VARCHAR(250),
    @UserNo         INT,
    @SystemIP       VARCHAR(20),
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        DECLARE @CurNo VARCHAR(30), @CurStatus VARCHAR(12), @CurRev INT;
        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @CurNo = PlanNo, @CurStatus = PlanStatus, @CurRev = RevNo
              FROM dbo.ProductionPlanMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @CurNo IS NULL BEGIN SET @Message = N'Production plan not found.'; RETURN; END;
        END;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF @PlanDate IS NULL BEGIN SET @Message = N'Enter the plan date.'; RETURN; END;
            IF @PlanMonth IS NULL OR @PlanMonth NOT LIKE '[12][0-9][0-9][0-9]-[01][0-9]'
            BEGIN SET @Message = N'Select the plan month.'; RETURN; END;
            IF @Status = 'Update' AND @CurStatus IN ('Released', 'Closed') AND ISNULL(@RevNo, 0) = @CurRev
            BEGIN
                SET @Message = @CurNo + N' is ' + @CurStatus + N' - change it with Reschedule.';
                RETURN;
            END;
            -- One live plan per month (a Closed plan frees its month). The
            -- page reads "duplicate fields".
            IF EXISTS (SELECT 1 FROM dbo.ProductionPlanMst
                        WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND PlanMonth = @PlanMonth
                          AND PlanStatus <> 'Closed'
                          AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
            BEGIN SET @Message = N'duplicate fields'; RETURN; END;
        END
        ELSE IF @Status = 'Delete' AND @CurStatus <> 'Draft'
        BEGIN
            SET @Message = @CurNo + N' is ' + @CurStatus + N' and cannot be deleted.';
            RETURN;
        END;

        BEGIN TRANSACTION;

        IF @Status = 'Insert'
        BEGIN
            DECLARE @Year INT = dbo.FN_FYCode(@PlanDate);
            DECLARE @DocNo INT =
                ISNULL((SELECT MAX(DocNo) FROM dbo.ProductionPlanMst WITH (UPDLOCK, HOLDLOCK)
                         WHERE SubscID = @SubscID AND DocYear = @Year), 0) + 1;
            INSERT INTO dbo.ProductionPlanMst
                   (DocYear, DocNo, PlanNo, PlanDate, PlanMonth, RevNo, PlanStatus, Remarks, SystemIP,
                    CreatedBy, CreatedOn, SubscID)
            VALUES (@Year, @DocNo,
                    CONCAT('PP/', LEFT(CAST(@Year AS VARCHAR(4)), 2), '-', RIGHT(CAST(@Year AS VARCHAR(4)), 2), '/',
                           RIGHT('0000' + CAST(@DocNo AS VARCHAR(10)), 4)),
                    @PlanDate, @PlanMonth, 0, 'Draft', NULLIF(LTRIM(RTRIM(@Remarks)), ''), @SystemIP,
                    @UserNo, SYSDATETIME(), @SubscID);
            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Production plan saved successfully.';
        END
        ELSE IF @Status = 'Update'
        BEGIN
            -- A new revision (amendment) goes back to Draft and re-opens its weeks.
            DECLARE @IsAmend BIT = CASE WHEN ISNULL(@RevNo, 0) <> @CurRev THEN 1 ELSE 0 END;
            UPDATE dbo.ProductionPlanMst
               SET PlanDate = @PlanDate, PlanMonth = @PlanMonth, RevNo = ISNULL(@RevNo, 0),
                   PlanStatus = CASE WHEN @IsAmend = 1 THEN 'Draft' ELSE PlanStatus END,
                   Remarks = NULLIF(LTRIM(RTRIM(@Remarks)), ''), SystemIP = @SystemIP,
                   LockCounter = LockCounter + 1, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @IsAmend = 1
                UPDATE dbo.ProductionPlanWeek
                   SET WeekStatus = 'Open', ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
                 WHERE MTransNo = @MTransNo AND WeekStatus <> 'Released';
            SET @ID = @MTransNo;
            SET @Message = N'Production plan updated successfully.';
        END
        ELSE IF @Status = 'Delete'
        BEGIN
            UPDATE dbo.ProductionPlanMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            UPDATE dbo.ProductionPlanJob  SET DeleteStatus = 'Y', DeleteReason = @DeleteReason
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            UPDATE dbo.ProductionPlanLine SET DeleteStatus = 'Y', DeleteReason = @DeleteReason
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            DELETE FROM dbo.ProductionPlanDay  WHERE MTransNo = @MTransNo;
            DELETE FROM dbo.ProductionPlanWeek WHERE MTransNo = @MTransNo;
            SET @ID = @MTransNo;
            SET @Message = N'Production plan deleted successfully.';
        END
        ELSE
            SET @Message = N'Unknown status.';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  5. SP_ProductionPlanDtl  - jobs + lines, replace-all (Draft only)
     @Jobs = [{ "seq":1, "planType":"Main", "partNo":12, "dieNo":3, "machineNo":13,
                "daysRequired":1.5, "fixedStart":"", "remarks":"",
                "lines":[{ "slNo":1, "source":"OA", "oaYear":2627, "oaNo":41,
                           "oaId":1, "grade":"A105", "planQty":100, "rate":588,
                           "routeNo":4, "reason":"" }] }]
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanDtl', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanDtl
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @Jobs           NVARCHAR(MAX),
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        DECLARE @PlanNo VARCHAR(30), @PlanStatus VARCHAR(12);
        SELECT @PlanNo = PlanNo, @PlanStatus = PlanStatus FROM dbo.ProductionPlanMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @PlanNo IS NULL BEGIN SET @Message = N'Production plan not found.'; RETURN; END;
        IF @PlanStatus <> 'Draft'
        BEGIN
            SET @Message = @PlanNo + N' is ' + @PlanStatus + N' - its jobs change only by Reschedule.';
            RETURN;
        END;
        IF ISJSON(ISNULL(@Jobs, N'[]')) = 0 BEGIN SET @Message = N'Invalid job list.'; RETURN; END;

        DECLARE @J TABLE (JobSeq INT PRIMARY KEY, PlanType VARCHAR(10), PartNo INT, DieNo INT, MachineNo INT,
                          DaysRequired NUMERIC(9, 2), FixedStart DATE, Remarks VARCHAR(250), Lines NVARCHAR(MAX));
        INSERT INTO @J
        SELECT TRY_CONVERT(INT, JSON_VALUE(value, '$.seq')),
               CASE WHEN JSON_VALUE(value, '$.planType') = 'Alternate' THEN 'Alternate' ELSE 'Main' END,
               TRY_CONVERT(INT, JSON_VALUE(value, '$.partNo')),
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.dieNo')), 0),
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.machineNo')), 0),
               ISNULL(TRY_CONVERT(NUMERIC(9, 2), JSON_VALUE(value, '$.daysRequired')), 0),
               TRY_CONVERT(DATE, NULLIF(JSON_VALUE(value, '$.fixedStart'), '')),
               LEFT(JSON_VALUE(value, '$.remarks'), 250),
               JSON_QUERY(value, '$.lines')
          FROM OPENJSON(@Jobs);

        DECLARE @L TABLE (JobSeq INT, LineSlNo INT, Source VARCHAR(5), OAYear INT, OANo INT, OAID INT,
                          Grade VARCHAR(50), PlanQty NUMERIC(14, 2), Rate NUMERIC(14, 2), RouteNo INT, Reason VARCHAR(50));
        INSERT INTO @L
        SELECT j.JobSeq,
               TRY_CONVERT(INT, JSON_VALUE(l.value, '$.slNo')),
               CASE WHEN JSON_VALUE(l.value, '$.source') = 'STOCK' THEN 'STOCK' ELSE 'OA' END,
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(l.value, '$.oaYear')), 0),
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(l.value, '$.oaNo')), 0),
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(l.value, '$.oaId')), 0),
               LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(l.value, '$.grade'), ''))), 50),
               ISNULL(TRY_CONVERT(NUMERIC(14, 2), JSON_VALUE(l.value, '$.planQty')), 0),
               ISNULL(TRY_CONVERT(NUMERIC(14, 2), JSON_VALUE(l.value, '$.rate')), 0),
               NULLIF(TRY_CONVERT(INT, JSON_VALUE(l.value, '$.routeNo')), 0),
               LEFT(JSON_VALUE(l.value, '$.reason'), 50)
          FROM @J j
         CROSS APPLY OPENJSON(ISNULL(j.Lines, N'[]')) l;

        IF EXISTS (SELECT 1 FROM @J WHERE JobSeq IS NULL OR ISNULL(PartNo, 0) <= 0)
        BEGIN SET @Message = N'Every job needs its sequence and part.'; RETURN; END;
        IF EXISTS (SELECT 1 FROM @J WHERE DaysRequired < 0)
        BEGIN SET @Message = N'Days required cannot be negative.'; RETURN; END;
        IF EXISTS (SELECT 1 FROM @J j WHERE NOT EXISTS (SELECT 1 FROM @L l WHERE l.JobSeq = j.JobSeq))
        BEGIN SET @Message = N'Every job needs at least one line.'; RETURN; END;
        IF EXISTS (SELECT 1 FROM @L WHERE LineSlNo IS NULL OR PlanQty <= 0 OR Grade = '')
        BEGIN SET @Message = N'Every line needs its grade and a plan qty above zero.'; RETURN; END;
        IF EXISTS (SELECT 1 FROM @L WHERE Source = 'OA' AND (OAYear IS NULL OR OANo IS NULL OR OAID IS NULL))
        BEGIN SET @Message = N'An order line needs its OA year, number and line.'; RETURN; END;

        -- Each order line must be a live, current-revision OA line of the job's part.
        DECLARE @BadOA VARCHAR(60);
        SELECT TOP (1) @BadOA = CONCAT(l.OAYear, '/', l.OANo, '/', l.OAID)
          FROM @L l
          JOIN @J j ON j.JobSeq = l.JobSeq
         WHERE l.Source = 'OA'
           AND NOT EXISTS (SELECT 1 FROM dbo.OAMst m
                             JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                            WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                              AND m.DocYear = l.OAYear AND m.DocNo = l.OANo AND d.SlNo = l.OAID AND d.PartNo = j.PartNo);
        IF @BadOA IS NOT NULL
        BEGIN SET @Message = N'OA line ' + @BadOA + N' is not a live order line of its job''s part.'; RETURN; END;

        BEGIN TRANSACTION;
            UPDATE dbo.ProductionPlanJob  SET DeleteStatus = 'Y', DeleteReason = 'Replaced by new job list',
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            UPDATE dbo.ProductionPlanLine SET DeleteStatus = 'Y', DeleteReason = 'Replaced by new job list',
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.ProductionPlanJob
                   (MTransNo, JobSeq, PlanType, PartNo, DieNo, MachineNo, DaysRequired, FixedStart, Remarks,
                    CreatedBy, CreatedOn, SubscID)
            SELECT @MTransNo, JobSeq, PlanType, PartNo, DieNo, MachineNo, DaysRequired, FixedStart,
                   NULLIF(LTRIM(RTRIM(Remarks)), ''), @UserNo, SYSDATETIME(), @SubscID
              FROM @J;

            INSERT INTO dbo.ProductionPlanLine
                   (MTransNo, JobSeq, LineSlNo, Source, OAYear, OANo, OAID, OALinkNo, Grade, PlanQty, Rate,
                    RouteNo, Reason, CreatedBy, CreatedOn, SubscID)
            SELECT @MTransNo, l.JobSeq, l.LineSlNo, l.Source, l.OAYear, l.OANo, l.OAID,
                   (SELECT TOP (1) d.TransSlNo FROM dbo.OAMst m
                      JOIN dbo.OADtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
                     WHERE m.SubscID = @SubscID AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
                       AND m.DocYear = l.OAYear AND m.DocNo = l.OANo AND d.SlNo = l.OAID
                     ORDER BY m.MTransNo DESC),
                   l.Grade, l.PlanQty, l.Rate, l.RouteNo,
                   CASE WHEN l.Source = 'STOCK' THEN ISNULL(NULLIF(LTRIM(RTRIM(l.Reason)), ''), 'Stock') END,
                   @UserNo, SYSDATETIME(), @SubscID
              FROM @L l;

            UPDATE dbo.ProductionPlanMst
               SET LockCounter = LockCounter + 1, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo;
        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = N'Plan jobs saved successfully.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6. SP_ProductionPlanDays  - the day-wise split, replace-all
     @Days = [{ "jobSeq":1, "lineSlNo":1, "planDate":"2026-11-02", "planQty":40 }]
     Worked out by the API from the jobs, the work calendar and the hammers.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanDays', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanDays AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanDays
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @Days           NVARCHAR(MAX),
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.ProductionPlanMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Production plan not found.'; RETURN; END;
        IF ISJSON(ISNULL(@Days, N'[]')) = 0 BEGIN SET @Message = N'Invalid day list.'; RETURN; END;

        BEGIN TRANSACTION;
            DELETE FROM dbo.ProductionPlanDay WHERE MTransNo = @MTransNo;
            INSERT INTO dbo.ProductionPlanDay (MTransNo, JobSeq, LineSlNo, PlanDate, PlanQty, SubscID)
            SELECT @MTransNo,
                   TRY_CONVERT(INT, JSON_VALUE(value, '$.jobSeq')),
                   TRY_CONVERT(INT, JSON_VALUE(value, '$.lineSlNo')),
                   TRY_CONVERT(DATE, JSON_VALUE(value, '$.planDate')),
                   TRY_CONVERT(NUMERIC(14, 2), JSON_VALUE(value, '$.planQty')),
                   @SubscID
              FROM OPENJSON(ISNULL(@Days, N'[]'))
             WHERE TRY_CONVERT(DATE, JSON_VALUE(value, '$.planDate')) IS NOT NULL
               AND TRY_CONVERT(NUMERIC(14, 2), JSON_VALUE(value, '$.planQty')) > 0;
        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = N'Plan days saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  7. SP_ProductionPlanAction  - Approve | FirmWeek
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanAction', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanAction AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanAction
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @Action         VARCHAR(20),            -- Approve | FirmWeek
    @WeekNo         INT,
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        DECLARE @PlanNo VARCHAR(30), @PlanStatus VARCHAR(12);
        SELECT @PlanNo = PlanNo, @PlanStatus = PlanStatus FROM dbo.ProductionPlanMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @PlanNo IS NULL BEGIN SET @Message = N'Production plan not found.'; RETURN; END;

        IF @Action = 'Approve'
        BEGIN
            IF @PlanStatus <> 'Draft'
            BEGIN SET @Message = @PlanNo + N' is already ' + @PlanStatus + N'.'; RETURN; END;
            IF NOT EXISTS (SELECT 1 FROM dbo.ProductionPlanJob
                            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N' AND PlanType = 'Main')
            BEGIN SET @Message = N'Add at least one main job before approving the plan.'; RETURN; END;

            BEGIN TRANSACTION;
                UPDATE dbo.ProductionPlanMst
                   SET PlanStatus = 'Approved', LockCounter = LockCounter + 1,
                       ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
                 WHERE MTransNo = @MTransNo;
            COMMIT TRANSACTION;
            SET @ID = @MTransNo;
            SET @Message = @PlanNo + N' approved.';
        END
        ELSE IF @Action = 'FirmWeek'
        BEGIN
            IF @PlanStatus = 'Draft'
            BEGIN SET @Message = N'Approve the plan before firming its weeks.'; RETURN; END;
            IF @PlanStatus = 'Closed'
            BEGIN SET @Message = @PlanNo + N' is Closed.'; RETURN; END;
            IF ISNULL(@WeekNo, 0) <= 0 BEGIN SET @Message = N'Select the week.'; RETURN; END;
            DECLARE @WeekStatus VARCHAR(10) =
                (SELECT WeekStatus FROM dbo.ProductionPlanWeek WHERE MTransNo = @MTransNo AND WeekNo = @WeekNo);
            IF ISNULL(@WeekStatus, 'Open') <> 'Open'
            BEGIN SET @Message = N'Week ' + CAST(@WeekNo AS NVARCHAR(5)) + N' is already ' + @WeekStatus + N'.'; RETURN; END;

            BEGIN TRANSACTION;
                IF @WeekStatus IS NULL
                    INSERT INTO dbo.ProductionPlanWeek (MTransNo, WeekNo, WeekStatus, ModifiedBy, ModifiedOn, SubscID)
                    VALUES (@MTransNo, @WeekNo, 'Firm', @UserNo, SYSDATETIME(), @SubscID);
                ELSE
                    UPDATE dbo.ProductionPlanWeek
                       SET WeekStatus = 'Firm', ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
                     WHERE MTransNo = @MTransNo AND WeekNo = @WeekNo;
            COMMIT TRANSACTION;
            SET @ID = @WeekNo;
            SET @Message = N'Week ' + CAST(@WeekNo AS NVARCHAR(5)) + N' firmed.';
        END
        ELSE
            SET @Message = N'Unknown action.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  8. SP_ProductionPlanRelease  - Firm -> Released, makes the job cards
     @Cards = one row per MAIN plan line of the jobs starting in the week
              (worked out by the API from the plan's days):
       [{ "jobSeq":1, "lineSlNo":1, "jobDate":"2026-11-04", "oaYear":2627,
          "oaNo":41, "oaId":1, "oaLinkNo":901, "partNo":12, "gradeNo":5,
          "grade":"A105", "routeNo":4, "qty":100, "rmPartNo":88, "rmGradeNo":7,
          "rmGrade":"SAE1018", "rmQty":602, "rmUnit":"KG",
          "plannedStart":"2026-11-04", "plannedEnd":"2026-11-05",
          "machineNo":13, "dieNo":3 }]
     A plan line that already has a live job card is skipped. Returns
     @ID = job cards made (0 is still a success when @Message says so).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanRelease', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanRelease AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanRelease
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @WeekNo         INT,
    @Cards          NVARCHAR(MAX),
    @UserNo         INT,
    @SystemIP       VARCHAR(20),
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @PlanNo VARCHAR(30), @PlanStatus VARCHAR(12);
    SELECT @PlanNo = PlanNo, @PlanStatus = PlanStatus FROM dbo.ProductionPlanMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
    IF @PlanNo IS NULL BEGIN SET @Message = N'Production plan not found.'; RETURN; END;
    IF ISNULL((SELECT WeekStatus FROM dbo.ProductionPlanWeek WHERE MTransNo = @MTransNo AND WeekNo = @WeekNo), 'Open') <> 'Firm'
    BEGIN SET @Message = N'Firm week ' + CAST(@WeekNo AS NVARCHAR(5)) + N' before releasing it.'; RETURN; END;
    IF ISJSON(ISNULL(@Cards, N'[]')) = 0 BEGIN SET @Message = N'Invalid job card list.'; RETURN; END;

    DECLARE @C TABLE (RowNo INT IDENTITY(1, 1), JobSeq INT, LineSlNo INT, JobDate DATE, OAYear INT, OANo INT, OAID INT,
                      OALinkNo INT, PartNo INT, GradeNo INT, Grade VARCHAR(50), RouteNo INT, Qty NUMERIC(18, 3),
                      RMPartNo INT, RMGradeNo INT, RMGrade VARCHAR(50), RMQty NUMERIC(18, 3), RMUnit VARCHAR(20),
                      PlannedStart DATE, PlannedEnd DATE, MachineNo INT, DieNo INT);
    INSERT INTO @C (JobSeq, LineSlNo, JobDate, OAYear, OANo, OAID, OALinkNo, PartNo, GradeNo, Grade, RouteNo, Qty,
                    RMPartNo, RMGradeNo, RMGrade, RMQty, RMUnit, PlannedStart, PlannedEnd, MachineNo, DieNo)
    SELECT TRY_CONVERT(INT, JSON_VALUE(value, '$.jobSeq')), TRY_CONVERT(INT, JSON_VALUE(value, '$.lineSlNo')),
           ISNULL(TRY_CONVERT(DATE, JSON_VALUE(value, '$.jobDate')), CAST(GETDATE() AS DATE)),
           ISNULL(TRY_CONVERT(INT, JSON_VALUE(value, '$.oaYear')), 0), ISNULL(TRY_CONVERT(INT, JSON_VALUE(value, '$.oaNo')), 0),
           ISNULL(TRY_CONVERT(INT, JSON_VALUE(value, '$.oaId')), 0), NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.oaLinkNo')), 0),
           TRY_CONVERT(INT, JSON_VALUE(value, '$.partNo')), NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.gradeNo')), 0),
           LEFT(ISNULL(JSON_VALUE(value, '$.grade'), ''), 50), TRY_CONVERT(INT, JSON_VALUE(value, '$.routeNo')),
           TRY_CONVERT(NUMERIC(18, 3), JSON_VALUE(value, '$.qty')),
           TRY_CONVERT(INT, JSON_VALUE(value, '$.rmPartNo')), NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.rmGradeNo')), 0),
           LEFT(ISNULL(JSON_VALUE(value, '$.rmGrade'), ''), 50), TRY_CONVERT(NUMERIC(18, 3), JSON_VALUE(value, '$.rmQty')),
           LEFT(JSON_VALUE(value, '$.rmUnit'), 20),
           TRY_CONVERT(DATE, JSON_VALUE(value, '$.plannedStart')), TRY_CONVERT(DATE, JSON_VALUE(value, '$.plannedEnd')),
           NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.machineNo')), 0), NULLIF(TRY_CONVERT(INT, JSON_VALUE(value, '$.dieNo')), 0)
      FROM OPENJSON(ISNULL(@Cards, N'[]'));

    -- Already released lines keep their job card.
    DELETE c FROM @C c
     WHERE EXISTS (SELECT 1 FROM dbo.JobCardMst j
                    WHERE j.SubscID = @SubscID AND j.DeleteStatus = 'N' AND j.PlanID = @MTransNo
                      AND j.PlanJobSeq = c.JobSeq AND j.PlanLineSlNo = c.LineSlNo);

    DECLARE @Bad NVARCHAR(120);
    SELECT TOP (1) @Bad = CONCAT(N'Job ', JobSeq, N' line ', LineSlNo) FROM @C
     WHERE ISNULL(PartNo, 0) <= 0 OR ISNULL(RouteNo, 0) <= 0 OR ISNULL(Qty, 0) <= 0;
    IF @Bad IS NOT NULL BEGIN SET @Message = @Bad + N' has no route - add it in Routing Chart before releasing.'; RETURN; END;
    SELECT TOP (1) @Bad = CONCAT(N'Job ', JobSeq, N' line ', LineSlNo, N' (', Grade, N')') FROM @C
     WHERE ISNULL(RMPartNo, 0) <= 0 OR RMGrade = '' OR ISNULL(RMQty, 0) <= 0;
    IF @Bad IS NOT NULL BEGIN SET @Message = @Bad + N' has no raw material - add it in the Conversion BOM before releasing.'; RETURN; END;

    BEGIN TRAN;
        DECLARE @Made INT = (SELECT COUNT(*) FROM @C);
        IF @Made > 0
        BEGIN
            DECLARE @Year INT = dbo.FN_FYCode(CAST(GETDATE() AS DATE));
            DECLARE @Base INT =
                ISNULL((SELECT MAX(DocNo) FROM dbo.JobCardMst WITH (UPDLOCK, HOLDLOCK)
                         WHERE SubscID = @SubscID AND DocYear = @Year), 0);

            INSERT INTO dbo.JobCardMst
                   (DocYear, DocNo, JobCardNo, JobDate, OAYear, OANo, OAID, OALinkNo,
                    PartNo, GradeNo, Grade, RouteNo, Qty, RMPartNo, RMGradeNo, RMGrade, RMQty, RMUnit,
                    Operator, Shift, Remarks, SystemIP, CreatedBy, CreatedOn, SubscID,
                    PlanID, PlanJobSeq, PlanLineSlNo, PlannedStart, PlannedEnd, MachineNo, DieNo)
            SELECT @Year, @Base + c.RowNo,
                   CONCAT('JC-', @Year, '-', RIGHT('00000' + CAST(@Base + c.RowNo AS VARCHAR(10)), 5)),
                   c.JobDate, c.OAYear, c.OANo, c.OAID, c.OALinkNo,
                   c.PartNo, c.GradeNo, c.Grade, c.RouteNo, c.Qty, c.RMPartNo, c.RMGradeNo, c.RMGrade, c.RMQty, c.RMUnit,
                   '', '', 'Released from ' + @PlanNo, @SystemIP, @UserNo, SYSDATETIME(), @SubscID,
                   @MTransNo, c.JobSeq, c.LineSlNo, c.PlannedStart, c.PlannedEnd, c.MachineNo, c.DieNo
              FROM @C c
             ORDER BY c.RowNo;
        END;

        UPDATE dbo.ProductionPlanWeek
           SET WeekStatus = 'Released', ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND WeekNo = @WeekNo;
        UPDATE dbo.ProductionPlanMst
           SET PlanStatus = 'Released', LockCounter = LockCounter + 1, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND PlanStatus IN ('Approved', 'Released');
    COMMIT TRAN;

    SET @ID = @Made;
    SET @Message = CONCAT(N'Week ', @WeekNo, N' released - ', @Made, N' job card(s) made.');
END
GO

/*------------------------------------------------------------------------------
  9. SP_ProductionPlanReschedule  - a job's fixed start and days, logged
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ProductionPlanReschedule', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ProductionPlanReschedule AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_ProductionPlanReschedule
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @JobSeq         INT,
    @FixedStart     DATE,
    @DaysRequired   NUMERIC(9, 2),
    @Reason         VARCHAR(250),
    @FromText       VARCHAR(100),
    @ToText         VARCHAR(100),
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        DECLARE @PlanNo VARCHAR(30), @PlanStatus VARCHAR(12);
        SELECT @PlanNo = PlanNo, @PlanStatus = PlanStatus FROM dbo.ProductionPlanMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @PlanNo IS NULL BEGIN SET @Message = N'Production plan not found.'; RETURN; END;
        IF @PlanStatus NOT IN ('Approved', 'Released')
        BEGIN SET @Message = N'Only an approved or released plan is rescheduled.'; RETURN; END;
        IF NOT EXISTS (SELECT 1 FROM dbo.ProductionPlanJob
                        WHERE MTransNo = @MTransNo AND JobSeq = @JobSeq AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Job not found.'; RETURN; END;
        IF LTRIM(RTRIM(ISNULL(@Reason, ''))) = ''
        BEGIN SET @Message = N'Give the reason for the reschedule.'; RETURN; END;
        IF @FixedStart IS NOT NULL AND @FixedStart < CAST(GETDATE() AS DATE)
        BEGIN SET @Message = N'A job cannot be moved into a day that has passed.'; RETURN; END;
        IF ISNULL(@DaysRequired, 0) < 0
        BEGIN SET @Message = N'Days required cannot be negative.'; RETURN; END;

        BEGIN TRANSACTION;
            UPDATE dbo.ProductionPlanJob
               SET FixedStart = @FixedStart,
                   DaysRequired = CASE WHEN ISNULL(@DaysRequired, 0) > 0 THEN @DaysRequired ELSE DaysRequired END,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND JobSeq = @JobSeq AND DeleteStatus = 'N';
            INSERT INTO dbo.ProductionPlanReschedule (MTransNo, JobSeq, FromText, ToText, Reason, CreatedBy, CreatedOn, SubscID)
            VALUES (@MTransNo, @JobSeq, @FromText, @ToText, LTRIM(RTRIM(@Reason)), @UserNo, SYSDATETIME(), @SubscID);
            UPDATE dbo.ProductionPlanMst
               SET LockCounter = LockCounter + 1, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo;
        COMMIT TRANSACTION;

        SET @ID = @JobSeq;
        SET @Message = N'Job ' + CAST(@JobSeq AS NVARCHAR(10)) + N' rescheduled.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0;
        SET @Message = ERROR_MESSAGE();
        THROW;
    END CATCH
END
GO

PRINT '59_ProductionPlanning.sql applied.';
GO
