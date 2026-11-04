/*==============================================================================
  53. JOB CARD  -  Modules/Production/JobCard.html, JobCardStatus.html,
                   JobCardInspection.html (+ Quality/JobCardInspection.html)

      Routing Chart ─► Job Card ─► Job Card Status (production entries)
                         │                 └─► Job Card Inspection
                         └─► Store Issue (Issue Type 'Job Card' - the RM)

  A job card makes one OA line's finished part, in one grade, along one
  route, from one raw material (Conversion BOM). Its progress is never
  stored as a running total - every figure is recomputed from the entries:

      step processed qty  = Σ live JobCardEntry.Qty for (job card, step SlNo)
      status              = Open (no entries) / Completed (last step >= Qty)
                            / In Progress
      inspection step     = the route's last 'Inspection' operation step,
                            otherwise its last step
      awaiting inspection = processed at the inspection step - Σ inspected
      die usage           = DieMst.UsedQty = Σ live entry qty on the die
                            (kept in step by SP_DieUsageRefresh)

  Numbering: per subscription and financial year (April start; 2026-10-14
  is year 2627).  JC-2627-00001, INS-2627-0001.  The SP allocates the
  number at save, under UPDLOCK, so two users never collide.

  Objects:
      JobCardMst, JobCardEntry, JobCardInspection
      FN_FYCode                      financial-year code of a date
      VW_JobCardStep                 one row per job card x route step
      VW_JobCardSummary              one row per job card (status, progress,
                                     inspection and RM-issue roll-ups)
      SP_DieUsageRefresh             recompute one die's used/processed/remaining
      SP_JobCardMst                  'Insert' | 'Update' | 'Delete'
      SP_JobCardEntrySave            replace / clear a job card's entries
      SP_JobCardInspection           'Insert' | 'Update' | 'Delete'

  Business validation (routes, OA line, BOM, partial-flow caps, die
  capacity, one product in progress at a time, inspection eligibility) is
  done by JobCardController / JobCardStatusController /
  JobCardInspectionController before the SP runs, inside a transaction.
  The SPs re-check the guards that protect stored data.

  SP_ names: create-stub-then-ALTER (master shadows SP_ procedures).
  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. TABLES
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.JobCardMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobCardMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        DocYear         INT             NOT NULL,       -- financial-year code, 2627
        DocNo           INT             NOT NULL,       -- 1, 2, 3 ... per SubscID + DocYear
        JobCardNo       VARCHAR(30)     NOT NULL,       -- JC-2627-00001
        JobDate         DATE            NOT NULL,

        OAYear          INT             NOT NULL,       -- OAMst.DocYear
        OANo            INT             NOT NULL,       -- OAMst.DocNo
        OAID            INT             NOT NULL,       -- OADtl.SlNo
        OALinkNo        INT             NULL,           -- OADtl.TransSlNo at save

        PartNo          INT             NOT NULL,       -- finished part (PartMaster.MTransNo)
        GradeNo         INT             NULL,           -- MaterialGrade.GradeNo of PartNo
        Grade           VARCHAR(50)     NOT NULL,
        RouteNo         INT             NOT NULL,       -- RoutingChartMst.MTransNo
        Qty             NUMERIC(18, 3)  NOT NULL,

        RMPartNo        INT             NOT NULL,       -- raw material (Conversion BOM)
        RMGradeNo       INT             NULL,
        RMGrade         VARCHAR(50)     NOT NULL,
        RMQty           NUMERIC(18, 3)  NOT NULL,
        RMUnit          VARCHAR(20)     NULL,

        Operator        VARCHAR(50)     NOT NULL,       -- Other Master 'Operator Name'
        Shift           VARCHAR(50)     NOT NULL,       -- Other Master 'Shift'
        Remarks         VARCHAR(250)    NULL,

        SystemIP        VARCHAR(20)     NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_JobCardMst_LockStatus   DEFAULT ('N'),
        LockCounter     INT             NOT NULL CONSTRAINT DF_JobCardMst_LockCounter  DEFAULT (0),
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_JobCardMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_JobCardMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_JobCardMst PRIMARY KEY CLUSTERED (MTransNo)
    );

    CREATE UNIQUE INDEX UQ_JobCardMst_No ON dbo.JobCardMst (SubscID, DocYear, DocNo);
    CREATE INDEX IX_JobCardMst_Part  ON dbo.JobCardMst (SubscID, PartNo) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_JobCardMst_Route ON dbo.JobCardMst (RouteNo) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.JobCardEntry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobCardEntry
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- JobCardMst.MTransNo
        SlNo            INT             NOT NULL,       -- order within the saved set
        StepSlNo        INT             NOT NULL,       -- RoutingChartDtl.SlNo of the job card's route
        OperationNo     INT             NOT NULL,       -- OtherMaster 'Operation' (copied from the step)
        MachineNo       INT             NOT NULL,       -- MachineMst.MTransNo
        DieNo           INT             NULL,           -- DieMst.MTransNo, NULL = no die
        EntryDate       DATE            NOT NULL,
        StartTime       CHAR(5)         NOT NULL,       -- HH:MM
        EndTime         CHAR(5)         NOT NULL,
        SettingMin      NUMERIC(9, 2)   NOT NULL CONSTRAINT DF_JobCardEntry_SettingMin DEFAULT (0),
        IdleMin         NUMERIC(9, 2)   NOT NULL CONSTRAINT DF_JobCardEntry_IdleMin    DEFAULT (0),
        Qty             NUMERIC(18, 3)  NOT NULL,
        Remarks         VARCHAR(250)    NULL,

        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_JobCardEntry_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_JobCardEntry_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_JobCardEntry PRIMARY KEY CLUSTERED (TransSlNo)
    );

    CREATE INDEX IX_JobCardEntry_Job     ON dbo.JobCardEntry (MTransNo, StepSlNo) INCLUDE (Qty) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_JobCardEntry_Die     ON dbo.JobCardEntry (DieNo) INCLUDE (Qty) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_JobCardEntry_Machine ON dbo.JobCardEntry (MachineNo) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.JobCardInspection', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobCardInspection
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        DocYear         INT             NOT NULL,
        DocNo           INT             NOT NULL,
        InspectionNo    VARCHAR(30)     NOT NULL,       -- INS-2627-0001
        InspDate        DATE            NOT NULL,
        JobCardTransNo  INT             NOT NULL,       -- JobCardMst.MTransNo
        InspectedBy     VARCHAR(100)    NOT NULL,       -- UserMaster user name
        InspectedQty    NUMERIC(18, 3)  NOT NULL,
        OKQty           NUMERIC(18, 3)  NOT NULL,
        ReworkQty       NUMERIC(18, 3)  NOT NULL,
        RejectedQty     NUMERIC(18, 3)  NOT NULL,
        Remarks         VARCHAR(250)    NULL,

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_JobCardInspection_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_JobCardInspection_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_JobCardInspection PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT CK_JobCardInspection_Split CHECK (OKQty + ReworkQty + RejectedQty = InspectedQty)
    );

    CREATE UNIQUE INDEX UQ_JobCardInspection_No ON dbo.JobCardInspection (SubscID, DocYear, DocNo);
    CREATE INDEX IX_JobCardInspection_Job ON dbo.JobCardInspection (JobCardTransNo) WHERE DeleteStatus = 'N';
END;
GO

/*------------------------------------------------------------------------------
  2. FN_FYCode - financial-year code of a date: 2026-10-14 -> '2627'
     Shared with SQL 55 (maintenance numbering), which carries the identical
     definition, so the two scripts can run in either order. Job card code
     reads it into INT variables (implicit CHAR -> INT).
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
  3. VW_JobCardStep - one row per live job card x route step, with progress

      ProcessedQty  Σ entries at the step
      PrevQty       what reached the step: the previous step's processed qty,
                    or the job card qty for step 1
      AvailableQty  PrevQty - ProcessedQty (never below 0)
      StepState     Completed / In Progress / Ready / Pending
      IsInspection  1 when the step is the job card's inspection step
------------------------------------------------------------------------------*/
CREATE OR ALTER VIEW dbo.VW_JobCardStep
AS
WITH s AS (
    SELECT  j.MTransNo                          AS JobCardTransNo,
            j.SubscID,
            j.Qty                               AS JobQty,
            d.SlNo,
            d.Operation                         AS OperationNo,
            LTRIM(RTRIM(ISNULL(op.MasterName, ''))) AS OperationName,
            d.MachineNo,
            ISNULL(m.MachineCode, '')           AS MachineCode,
            ISNULL(m.MachineName, '')           AS MachineName,
            ISNULL(d.TimeMin, 0)                AS TimeMin,
            ISNULL(e.ProcessedQty, 0)           AS ProcessedQty,
            ISNULL(e.EntryCount, 0)             AS EntryCount,
            CASE WHEN LTRIM(RTRIM(ISNULL(op.MasterName, ''))) = 'Inspection' THEN 1 ELSE 0 END AS IsInspOp
      FROM  dbo.JobCardMst j
      JOIN  dbo.RoutingChartDtl d ON d.MTransNo = j.RouteNo AND d.DeleteStatus = 'N'
      LEFT JOIN dbo.OtherMaster op ON op.MTransNo = d.Operation
      LEFT JOIN dbo.MachineMst  m  ON m.MTransNo  = d.MachineNo
     OUTER APPLY (
            SELECT SUM(x.Qty) AS ProcessedQty, COUNT(*) AS EntryCount
              FROM dbo.JobCardEntry x
             WHERE x.MTransNo = j.MTransNo AND x.StepSlNo = d.SlNo AND x.DeleteStatus = 'N'
          ) e
     WHERE  j.DeleteStatus = 'N'
),
r AS (
    SELECT  s.*,
            LAG(s.ProcessedQty, 1, s.JobQty) OVER (PARTITION BY s.JobCardTransNo ORDER BY s.SlNo) AS PrevQty,
            MAX(s.SlNo) OVER (PARTITION BY s.JobCardTransNo)                                       AS LastSlNo,
            MAX(CASE WHEN s.IsInspOp = 1 THEN s.SlNo END) OVER (PARTITION BY s.JobCardTransNo)    AS InspOpSlNo
      FROM  s
)
SELECT  r.JobCardTransNo, r.SubscID, r.SlNo, r.OperationNo, r.OperationName,
        r.MachineNo, r.MachineCode, r.MachineName, r.TimeMin,
        r.ProcessedQty, r.EntryCount, r.PrevQty,
        CASE WHEN r.PrevQty - r.ProcessedQty > 0 THEN r.PrevQty - r.ProcessedQty ELSE 0 END AS AvailableQty,
        CASE WHEN r.JobQty  - r.ProcessedQty > 0 THEN r.JobQty  - r.ProcessedQty ELSE 0 END AS BalanceQty,
        CASE WHEN r.ProcessedQty >= r.JobQty THEN 'Completed'
             WHEN r.ProcessedQty > 0         THEN 'In Progress'
             WHEN r.PrevQty > 0              THEN 'Ready'
             ELSE 'Pending' END                                                             AS StepState,
        CASE WHEN r.SlNo = r.LastSlNo THEN 1 ELSE 0 END                                     AS IsLast,
        CASE WHEN r.SlNo = ISNULL(r.InspOpSlNo, r.LastSlNo) THEN 1 ELSE 0 END               AS IsInspection
  FROM  r;
GO

/*------------------------------------------------------------------------------
  4. VW_JobCardSummary - one row per live job card
------------------------------------------------------------------------------*/
CREATE OR ALTER VIEW dbo.VW_JobCardSummary
AS
SELECT  j.MTransNo                                      AS JobCardTransNo,
        j.SubscID,
        ISNULL(st.StepCount, 0)                         AS StepCount,
        ISNULL(st.TotalTimeMin, 0)                      AS TotalTimeMin,
        ISNULL(ec.EntryCount, 0)                        AS EntryCount,
        ec.LastEntryDate,
        ISNULL(lst.ProcessedQty, 0)                     AS CompletedQty,
        CASE WHEN j.Qty - ISNULL(lst.ProcessedQty, 0) > 0
             THEN j.Qty - ISNULL(lst.ProcessedQty, 0) ELSE 0 END AS BalanceQty,
        CASE WHEN ISNULL(ec.EntryCount, 0) = 0         THEN 'Open'
             WHEN ISNULL(lst.ProcessedQty, 0) >= j.Qty THEN 'Completed'
             ELSE 'In Progress' END                     AS JobStatus,
        cur.SlNo                                        AS CurrentSlNo,
        CASE WHEN cur.SlNo IS NOT NULL THEN CONCAT(cur.SlNo, N' – ', cur.OperationName)
             WHEN ISNULL(st.StepCount, 0) > 0 THEN N'All operations done'
             ELSE N'' END                               AS CurrentStep,
        ins.SlNo                                        AS InspectionSlNo,
        CASE WHEN ins.SlNo IS NOT NULL THEN CONCAT(ins.SlNo, N' – ', ins.OperationName) ELSE N'' END
                                                        AS InspectionStep,
        ISNULL(ins.ProcessedQty, 0)                     AS InspectionEligibleQty,
        ISNULL(iq.InspectedQty, 0)                      AS InspectedQty,
        ISNULL(iq.OKQty, 0)                             AS OKQty,
        ISNULL(iq.ReworkQty, 0)                         AS ReworkQty,
        ISNULL(iq.RejectedQty, 0)                       AS RejectedQty,
        ISNULL(iq.InspectionCount, 0)                   AS InspectionCount,
        iq.LastInspDate,
        CASE WHEN ISNULL(ins.ProcessedQty, 0) - ISNULL(iq.InspectedQty, 0) > 0
             THEN ISNULL(ins.ProcessedQty, 0) - ISNULL(iq.InspectedQty, 0) ELSE 0 END
                                                        AS AwaitingInspectionQty,
        ISNULL(iss.IssuedQty, 0)                        AS RMIssuedQty,
        CASE WHEN j.RMQty - ISNULL(iss.IssuedQty, 0) > 0
             THEN j.RMQty - ISNULL(iss.IssuedQty, 0) ELSE 0 END AS RMBalanceQty
  FROM  dbo.JobCardMst j
 OUTER APPLY (SELECT COUNT(*) AS StepCount, SUM(v.TimeMin) AS TotalTimeMin
                FROM dbo.VW_JobCardStep v WHERE v.JobCardTransNo = j.MTransNo) st
 OUTER APPLY (SELECT COUNT(*) AS EntryCount, MAX(e.EntryDate) AS LastEntryDate
                FROM dbo.JobCardEntry e WHERE e.MTransNo = j.MTransNo AND e.DeleteStatus = 'N') ec
 OUTER APPLY (SELECT TOP (1) v.ProcessedQty
                FROM dbo.VW_JobCardStep v WHERE v.JobCardTransNo = j.MTransNo AND v.IsLast = 1) lst
 OUTER APPLY (SELECT TOP (1) v.SlNo, v.OperationName
                FROM dbo.VW_JobCardStep v
               WHERE v.JobCardTransNo = j.MTransNo AND v.ProcessedQty < j.Qty
               ORDER BY v.SlNo) cur
 OUTER APPLY (SELECT TOP (1) v.SlNo, v.OperationName, v.ProcessedQty
                FROM dbo.VW_JobCardStep v WHERE v.JobCardTransNo = j.MTransNo AND v.IsInspection = 1) ins
 OUTER APPLY (SELECT SUM(x.InspectedQty) AS InspectedQty, SUM(x.OKQty) AS OKQty,
                     SUM(x.ReworkQty) AS ReworkQty, SUM(x.RejectedQty) AS RejectedQty,
                     COUNT(*) AS InspectionCount, MAX(x.InspDate) AS LastInspDate
                FROM dbo.JobCardInspection x
               WHERE x.JobCardTransNo = j.MTransNo AND x.DeleteStatus = 'N') iq
 OUTER APPLY (SELECT SUM(d.Qty) AS IssuedQty
                FROM dbo.StoreIssueMst m
                JOIN dbo.StoreIssueDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
               WHERE m.TargetType = 'Job Card' AND m.TargetTransNo = j.MTransNo
                 AND m.DeleteStatus = 'N') iss
 WHERE  j.DeleteStatus = 'N';
GO

/*------------------------------------------------------------------------------
  5. SP_DieUsageRefresh - DieMst used / processed / remaining from the entries
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_DieUsageRefresh', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_DieUsageRefresh AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_DieUsageRefresh
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
                   WHERE e.DieNo = d.MTransNo AND e.DeleteStatus = 'N') u
     WHERE d.MTransNo = @DieNo;
END;
GO

/*------------------------------------------------------------------------------
  6. SP_JobCardMst - @Status 'Insert' | 'Update' | 'Delete'
     Returns @ID = MTransNo (0 + @Message when refused).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_JobCardMst AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_JobCardMst
    @ID          INT            OUTPUT,
    @Message     VARCHAR(250)   OUTPUT,
    @Status      VARCHAR(10),
    @MTransNo    INT,
    @JobDate     DATE,
    @OAYear      INT,
    @OANo        INT,
    @OAID        INT,
    @OALinkNo    INT,
    @PartNo      INT,
    @GradeNo     INT,
    @Grade       VARCHAR(50),
    @RouteNo     INT,
    @Qty         NUMERIC(18, 3),
    @RMPartNo    INT,
    @RMGradeNo   INT,
    @RMGrade     VARCHAR(50),
    @RMQty       NUMERIC(18, 3),
    @RMUnit      VARCHAR(20),
    @Operator    VARCHAR(50),
    @Shift       VARCHAR(50),
    @Remarks     VARCHAR(250),
    @SystemIP    VARCHAR(20),
    @UserNo      INT,
    @DeleteReason VARCHAR(250),
    @SubscID     INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = '';

    IF @Status IN ('Update', 'Delete')
    BEGIN
        DECLARE @No VARCHAR(30);
        SELECT @No = JobCardNo FROM dbo.JobCardMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @No IS NULL
        BEGIN
            SET @Message = 'Job card not found.';
            RETURN;
        END;
        IF EXISTS (SELECT 1 FROM dbo.JobCardEntry WHERE MTransNo = @MTransNo AND DeleteStatus = 'N')
        BEGIN
            SET @Message = @No + CASE WHEN @Status = 'Update'
                                      THEN ' is already in production and can no longer be edited.'
                                      ELSE ' already has production recorded and cannot be deleted.' END;
            RETURN;
        END;
        IF @Status = 'Delete' AND EXISTS (
               SELECT 1 FROM dbo.StoreIssueMst
                WHERE TargetType = 'Job Card' AND TargetTransNo = @MTransNo AND DeleteStatus = 'N')
        BEGIN
            SET @Message = @No + ' has raw material issued against it. Delete the store issue first.';
            RETURN;
        END;
    END;

    IF @Status = 'Insert'
    BEGIN
        BEGIN TRAN;
            DECLARE @Year INT = dbo.FN_FYCode(@JobDate);
            DECLARE @DocNo INT =
                ISNULL((SELECT MAX(DocNo) FROM dbo.JobCardMst WITH (UPDLOCK, HOLDLOCK)
                         WHERE SubscID = @SubscID AND DocYear = @Year), 0) + 1;

            INSERT INTO dbo.JobCardMst
                   (DocYear, DocNo, JobCardNo, JobDate, OAYear, OANo, OAID, OALinkNo,
                    PartNo, GradeNo, Grade, RouteNo, Qty, RMPartNo, RMGradeNo, RMGrade, RMQty, RMUnit,
                    Operator, Shift, Remarks, SystemIP, CreatedBy, CreatedOn, SubscID)
            VALUES (@Year, @DocNo, CONCAT('JC-', @Year, '-', RIGHT('00000' + CAST(@DocNo AS VARCHAR(10)), 5)),
                    @JobDate, @OAYear, @OANo, @OAID, @OALinkNo,
                    @PartNo, NULLIF(@GradeNo, 0), @Grade, @RouteNo, @Qty,
                    @RMPartNo, NULLIF(@RMGradeNo, 0), @RMGrade, @RMQty, @RMUnit,
                    @Operator, @Shift, @Remarks, @SystemIP, @UserNo, SYSDATETIME(), @SubscID);
            SET @ID = SCOPE_IDENTITY();
        COMMIT;
        RETURN;
    END;

    IF @Status = 'Update'
    BEGIN
        -- The number keeps its year even if the date moves across 1 April.
        UPDATE dbo.JobCardMst
           SET JobDate = @JobDate, OAYear = @OAYear, OANo = @OANo, OAID = @OAID, OALinkNo = @OALinkNo,
               PartNo = @PartNo, GradeNo = NULLIF(@GradeNo, 0), Grade = @Grade, RouteNo = @RouteNo, Qty = @Qty,
               RMPartNo = @RMPartNo, RMGradeNo = NULLIF(@RMGradeNo, 0), RMGrade = @RMGrade,
               RMQty = @RMQty, RMUnit = @RMUnit, Operator = @Operator, Shift = @Shift, Remarks = @Remarks,
               SystemIP = @SystemIP, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(),
               LockCounter = LockCounter + 1
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        SET @ID = @MTransNo;
        RETURN;
    END;

    IF @Status = 'Delete'
    BEGIN
        UPDATE dbo.JobCardMst
           SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, SystemIP = @SystemIP,
               ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        SET @ID = @MTransNo;
        RETURN;
    END;

    SET @Message = 'Unknown status.';
END;
GO

/*------------------------------------------------------------------------------
  7. SP_JobCardEntrySave - replace (@Clear 0) or clear (@Clear 1) the live
     entry set of one job card. @Entries is a JSON array:
       [{ "stepSlNo":1, "operationNo":3, "machineNo":2, "dieNo":0,
          "entryDate":"2026-10-14", "startTime":"09:00", "endTime":"12:00",
          "settingMin":10, "idleMin":0, "qty":60, "remarks":"" }, ...]
     The old set is soft-deleted, so history stays; die usage is refreshed
     for every die in the old or new set.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardEntrySave', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_JobCardEntrySave AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_JobCardEntrySave
    @ID         INT            OUTPUT,
    @Message    VARCHAR(250)   OUTPUT,
    @MTransNo   INT,
    @Clear      BIT,
    @Entries    NVARCHAR(MAX),
    @Reason     VARCHAR(250),
    @UserNo     INT,
    @SubscID    INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = '';

    DECLARE @No VARCHAR(30);
    SELECT @No = JobCardNo FROM dbo.JobCardMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
    IF @No IS NULL
    BEGIN
        SET @Message = 'Job card not found.';
        RETURN;
    END;

    IF @Clear = 1 AND EXISTS (SELECT 1 FROM dbo.JobCardInspection
                               WHERE JobCardTransNo = @MTransNo AND DeleteStatus = 'N')
    BEGIN
        SET @Message = @No + ' has inspection records. Delete those first.';
        RETURN;
    END;

    DECLARE @Dies TABLE (DieNo INT PRIMARY KEY);

    BEGIN TRAN;
        INSERT INTO @Dies (DieNo)
        SELECT DISTINCT DieNo FROM dbo.JobCardEntry
         WHERE MTransNo = @MTransNo AND DeleteStatus = 'N' AND DieNo IS NOT NULL;

        UPDATE dbo.JobCardEntry
           SET DeleteStatus = 'Y',
               DeleteReason = CASE WHEN @Clear = 1 THEN ISNULL(NULLIF(@Reason, ''), 'Cleared') ELSE 'Superseded' END,
               ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

        IF @Clear = 0
        BEGIN
            INSERT INTO dbo.JobCardEntry
                   (MTransNo, SlNo, StepSlNo, OperationNo, MachineNo, DieNo, EntryDate, StartTime, EndTime,
                    SettingMin, IdleMin, Qty, Remarks, CreatedBy, CreatedOn, SubscID)
            SELECT @MTransNo,
                   ROW_NUMBER() OVER (ORDER BY j.[key]),
                   j.stepSlNo, j.operationNo, j.machineNo, NULLIF(j.dieNo, 0), j.entryDate, j.startTime, j.endTime,
                   ISNULL(j.settingMin, 0), ISNULL(j.idleMin, 0), j.qty, j.remarks,
                   @UserNo, SYSDATETIME(), @SubscID
              FROM (SELECT CAST(a.[key] AS INT) AS [key], v.*
                      FROM OPENJSON(@Entries) a
                     CROSS APPLY OPENJSON(a.[value]) WITH (
                            stepSlNo    INT,
                            operationNo INT,
                            machineNo   INT,
                            dieNo       INT,
                            entryDate   DATE,
                            startTime   CHAR(5),
                            endTime     CHAR(5),
                            settingMin  NUMERIC(9, 2),
                            idleMin     NUMERIC(9, 2),
                            qty         NUMERIC(18, 3),
                            remarks     VARCHAR(250)) v) j;

            INSERT INTO @Dies (DieNo)
            SELECT DISTINCT e.DieNo FROM dbo.JobCardEntry e
             WHERE e.MTransNo = @MTransNo AND e.DeleteStatus = 'N' AND e.DieNo IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM @Dies d WHERE d.DieNo = e.DieNo);
        END;

        DECLARE @Die INT;
        DECLARE die_cur CURSOR LOCAL FAST_FORWARD FOR SELECT DieNo FROM @Dies;
        OPEN die_cur;
        FETCH NEXT FROM die_cur INTO @Die;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC dbo.SP_DieUsageRefresh @DieNo = @Die;
            FETCH NEXT FROM die_cur INTO @Die;
        END;
        CLOSE die_cur;
        DEALLOCATE die_cur;
    COMMIT;

    SET @ID = @MTransNo;
END;
GO

/*------------------------------------------------------------------------------
  8. SP_JobCardInspection - @Status 'Insert' | 'Update' | 'Delete'
     Re-checks that the inspected total stays within what reached the
     inspection step (VW_JobCardSummary.InspectionEligibleQty).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardInspection', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_JobCardInspection AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_JobCardInspection
    @ID             INT            OUTPUT,
    @Message        VARCHAR(250)   OUTPUT,
    @Status         VARCHAR(10),
    @MTransNo       INT,
    @InspDate       DATE,
    @JobCardTransNo INT,
    @InspectedBy    VARCHAR(100),
    @InspectedQty   NUMERIC(18, 3),
    @OKQty          NUMERIC(18, 3),
    @ReworkQty      NUMERIC(18, 3),
    @RejectedQty    NUMERIC(18, 3),
    @Remarks        VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @UserNo         INT,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = '';

    IF @Status IN ('Update', 'Delete') AND NOT EXISTS (
           SELECT 1 FROM dbo.JobCardInspection
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
    BEGIN
        SET @Message = 'Inspection not found.';
        RETURN;
    END;

    IF @Status = 'Delete'
    BEGIN
        UPDATE dbo.JobCardInspection
           SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, SystemIP = @SystemIP,
               ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID;
        SET @ID = @MTransNo;
        RETURN;
    END;

    IF @Status NOT IN ('Insert', 'Update')
    BEGIN
        SET @Message = 'Unknown status.';
        RETURN;
    END;

    IF @OKQty + @ReworkQty + @RejectedQty <> @InspectedQty
    BEGIN
        SET @Message = 'OK + Rework + Rejected must equal the Inspected Quantity.';
        RETURN;
    END;

    BEGIN TRAN;
        DECLARE @Eligible NUMERIC(18, 3), @No VARCHAR(30);
        SELECT @Eligible = s.InspectionEligibleQty, @No = j.JobCardNo
          FROM dbo.JobCardMst j WITH (UPDLOCK, HOLDLOCK)
          JOIN dbo.VW_JobCardSummary s ON s.JobCardTransNo = j.MTransNo
         WHERE j.MTransNo = @JobCardTransNo AND j.SubscID = @SubscID AND j.DeleteStatus = 'N';

        IF @No IS NULL
        BEGIN
            ROLLBACK;
            SET @Message = 'Select a job card.';
            RETURN;
        END;

        DECLARE @Already NUMERIC(18, 3) =
            ISNULL((SELECT SUM(InspectedQty) FROM dbo.JobCardInspection
                     WHERE JobCardTransNo = @JobCardTransNo AND DeleteStatus = 'N'
                       AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END), 0);

        IF @Already + @InspectedQty > @Eligible
        BEGIN
            ROLLBACK;
            SET @Message = CONCAT('Only ', FORMAT(CASE WHEN @Eligible - @Already > 0 THEN @Eligible - @Already ELSE 0 END, '0.###'),
                                  ' pieces of ', @No, ' are awaiting inspection.');
            RETURN;
        END;

        IF @Status = 'Insert'
        BEGIN
            DECLARE @Year INT = dbo.FN_FYCode(@InspDate);
            DECLARE @DocNo INT =
                ISNULL((SELECT MAX(DocNo) FROM dbo.JobCardInspection WITH (UPDLOCK, HOLDLOCK)
                         WHERE SubscID = @SubscID AND DocYear = @Year), 0) + 1;

            INSERT INTO dbo.JobCardInspection
                   (DocYear, DocNo, InspectionNo, InspDate, JobCardTransNo, InspectedBy,
                    InspectedQty, OKQty, ReworkQty, RejectedQty, Remarks, SystemIP, CreatedBy, CreatedOn, SubscID)
            VALUES (@Year, @DocNo, CONCAT('INS-', @Year, '-', RIGHT('0000' + CAST(@DocNo AS VARCHAR(10)), 4)),
                    @InspDate, @JobCardTransNo, @InspectedBy,
                    @InspectedQty, @OKQty, @ReworkQty, @RejectedQty, @Remarks, @SystemIP, @UserNo, SYSDATETIME(), @SubscID);
            SET @ID = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.JobCardInspection
               SET InspDate = @InspDate, JobCardTransNo = @JobCardTransNo, InspectedBy = @InspectedBy,
                   InspectedQty = @InspectedQty, OKQty = @OKQty, ReworkQty = @ReworkQty,
                   RejectedQty = @RejectedQty, Remarks = @Remarks, SystemIP = @SystemIP,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;
            SET @ID = @MTransNo;
        END;
    COMMIT;
END;
GO

-- What was written.
SELECT name, type_desc FROM sys.objects
 WHERE name IN ('JobCardMst', 'JobCardEntry', 'JobCardInspection', 'FN_FYCode', 'VW_JobCardStep',
                'VW_JobCardSummary', 'SP_DieUsageRefresh', 'SP_JobCardMst', 'SP_JobCardEntrySave',
                'SP_JobCardInspection')
 ORDER BY type_desc, name;
GO
