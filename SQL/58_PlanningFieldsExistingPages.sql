/*==============================================================================
  58. PLANNING FIELDS ON EXISTING PAGES  -  Production Planning, part 1

  The existing masters and transactions already send (and read back) a few
  planning fields. This script adds the columns, the small "companion"
  procedures the API calls right after each page's existing save procedure
  (inside the same transaction), and two Other Master lookup types.

  Every column is additive and NULLable; the ones with a default were filled
  for existing rows (WITH VALUES). An older caller that does not send a field
  leaves it unchanged (companion parameters: NULL = keep).

  COLUMNS
      MachineMst        CapacityUnit, ShiftsPerDay (1), EfficiencyPct (100),
                        MinPieceWt / MaxPieceWt (kg, 0 = no limit)
      RoutingChartMst   PostForgeDays (0)   working days after forging until
                                            ready to pack
      RoutingChartDtl   SetupMin (0)        die-setup minutes of the step
                                            (TimeMin stays minutes PER BATCH)
      ConvBOM           BarDia, CutLength (mm), YieldPct   (Qty = cut weight)
      DieMst            DieStatus ('Ready')  Ready / In Repair / Scrapped
      OADtl             CommittedDate (NULL = delivery date), Priority
                        ('Normal' | 'Urgent'), CustomerMaterial ('N' | 'Y')
      JobCardMst        PlanID, PlanJobSeq, PlanLineSlNo, PlannedStart,
                        PlannedEnd, MachineNo, DieNo  (plan link; NULL = made
                        by hand). ProductionPlanMst is created by SQL 59.
      JobCardEntry      Operator, Shift, IdleReason (Other Master names),
                        RejectedQty (0)
      JobCardInspection RejectionReason (Other Master 'Rejection Reason' name)
      MaterialGrade     SafetyStock (0), LeadTimeDays (0)
      PurReqDtl         RequiredBy (NULL = header due date)
      StoreReturnsDtl   ReturnType ('Unused')  Unused / End Piece / Scrap
      StoreIssueDtl     HeatNo  (an approved heat of the line's part)

  COMPANION PROCEDURES  (@ID OUTPUT = the row's id, 0 + @Message = refused)
      SP_MachinePlanFields           MachineMst row
      SP_RoutingChartPlanFields      header PostForgeDays and/or step SetupMin
                                     (JSON [{ "setupMin": 15 }, ...] in step
                                     order = live steps by SlNo)
      SP_ConvBOMPlanFields           ConvBOM row
      SP_DiePlanFields               DieMst.DieStatus
      SP_OADtlPlanFields             OADtl line
      SP_JobCardPlanFields           JobCardMst plan link, machine, die, dates
      SP_JobCardEntryPlanFields      JobCardEntry live set, JSON in the same
                                     order SP_JobCardEntrySave inserted it
                                     (SlNo = array position + 1)
      SP_JobCardInspectionPlanFields JobCardInspection.RejectionReason
      SP_MaterialGradePlanFields     MaterialGrade row
      SP_PurReqDtlPlanFields         PurReqDtl.RequiredBy
      SP_StoreReturnsDtlPlanFields   StoreReturnsDtl.ReturnType
      SP_StoreIssueDtlPlanFields     StoreIssueDtl.HeatNo - must be a heat
                                     number of an APPROVED heat document
                                     (HeatNoMst.LockStatus 'Y') for the line's
                                     part (and grade, when both carry one)

  LOOKUPS (Other Master types, seeded like SQL 52)
      Idle Reason       Die Change, Waiting For Material, Breakdown,
                        No Operator, Power Cut
      Rejection Reason  Crack, Under Fill, Lap, Mismatch, Dimension
      Defaults go only to a subscription with none of that type.

  SP_ names: create-stub-then-ALTER (master shadows SP_ procedures).
  Run with QUOTED_IDENTIFIER ON (sqlcmd -I). Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. COLUMNS
------------------------------------------------------------------------------*/
-- MachineMst
IF COL_LENGTH('dbo.MachineMst', 'CapacityUnit') IS NULL
    ALTER TABLE dbo.MachineMst ADD CapacityUnit VARCHAR(20) NULL;
IF COL_LENGTH('dbo.MachineMst', 'ShiftsPerDay') IS NULL
    ALTER TABLE dbo.MachineMst ADD ShiftsPerDay INT NULL
        CONSTRAINT DF_MachineMst_ShiftsPerDay DEFAULT (1) WITH VALUES;
IF COL_LENGTH('dbo.MachineMst', 'EfficiencyPct') IS NULL
    ALTER TABLE dbo.MachineMst ADD EfficiencyPct NUMERIC(5, 2) NULL
        CONSTRAINT DF_MachineMst_EfficiencyPct DEFAULT (100) WITH VALUES;
IF COL_LENGTH('dbo.MachineMst', 'MinPieceWt') IS NULL
    ALTER TABLE dbo.MachineMst ADD MinPieceWt NUMERIC(10, 3) NULL
        CONSTRAINT DF_MachineMst_MinPieceWt DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.MachineMst', 'MaxPieceWt') IS NULL
    ALTER TABLE dbo.MachineMst ADD MaxPieceWt NUMERIC(10, 3) NULL
        CONSTRAINT DF_MachineMst_MaxPieceWt DEFAULT (0) WITH VALUES;
GO

-- Routing Chart
IF COL_LENGTH('dbo.RoutingChartMst', 'PostForgeDays') IS NULL
    ALTER TABLE dbo.RoutingChartMst ADD PostForgeDays INT NULL
        CONSTRAINT DF_RoutingChartMst_PostForgeDays DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.RoutingChartDtl', 'SetupMin') IS NULL
    ALTER TABLE dbo.RoutingChartDtl ADD SetupMin NUMERIC(9, 2) NULL
        CONSTRAINT DF_RoutingChartDtl_SetupMin DEFAULT (0) WITH VALUES;
GO

-- Conversion BOM
IF COL_LENGTH('dbo.ConvBOM', 'BarDia') IS NULL
    ALTER TABLE dbo.ConvBOM ADD BarDia NUMERIC(8, 2) NULL;
IF COL_LENGTH('dbo.ConvBOM', 'CutLength') IS NULL
    ALTER TABLE dbo.ConvBOM ADD CutLength NUMERIC(8, 2) NULL;
IF COL_LENGTH('dbo.ConvBOM', 'YieldPct') IS NULL
    ALTER TABLE dbo.ConvBOM ADD YieldPct NUMERIC(5, 2) NULL;
GO

-- Die
IF COL_LENGTH('dbo.DieMst', 'DieStatus') IS NULL
    ALTER TABLE dbo.DieMst ADD DieStatus VARCHAR(20) NULL
        CONSTRAINT DF_DieMst_DieStatus DEFAULT ('Ready') WITH VALUES;
GO

-- OA line
IF COL_LENGTH('dbo.OADtl', 'CommittedDate') IS NULL
    ALTER TABLE dbo.OADtl ADD CommittedDate DATE NULL;
IF COL_LENGTH('dbo.OADtl', 'Priority') IS NULL
    ALTER TABLE dbo.OADtl ADD Priority VARCHAR(10) NULL
        CONSTRAINT DF_OADtl_Priority DEFAULT ('Normal') WITH VALUES;
IF COL_LENGTH('dbo.OADtl', 'CustomerMaterial') IS NULL
    ALTER TABLE dbo.OADtl ADD CustomerMaterial CHAR(1) NULL
        CONSTRAINT DF_OADtl_CustomerMaterial DEFAULT ('N') WITH VALUES;
GO

-- Job card plan link
IF COL_LENGTH('dbo.JobCardMst', 'PlanID') IS NULL
    ALTER TABLE dbo.JobCardMst ADD PlanID INT NULL;
IF COL_LENGTH('dbo.JobCardMst', 'PlanJobSeq') IS NULL
    ALTER TABLE dbo.JobCardMst ADD PlanJobSeq INT NULL;
IF COL_LENGTH('dbo.JobCardMst', 'PlanLineSlNo') IS NULL
    ALTER TABLE dbo.JobCardMst ADD PlanLineSlNo INT NULL;
IF COL_LENGTH('dbo.JobCardMst', 'PlannedStart') IS NULL
    ALTER TABLE dbo.JobCardMst ADD PlannedStart DATE NULL;
IF COL_LENGTH('dbo.JobCardMst', 'PlannedEnd') IS NULL
    ALTER TABLE dbo.JobCardMst ADD PlannedEnd DATE NULL;
IF COL_LENGTH('dbo.JobCardMst', 'MachineNo') IS NULL
    ALTER TABLE dbo.JobCardMst ADD MachineNo INT NULL;
IF COL_LENGTH('dbo.JobCardMst', 'DieNo') IS NULL
    ALTER TABLE dbo.JobCardMst ADD DieNo INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_JobCardMst_Plan' AND object_id = OBJECT_ID(N'dbo.JobCardMst'))
    CREATE INDEX IX_JobCardMst_Plan ON dbo.JobCardMst (PlanID, PlanJobSeq, PlanLineSlNo)
        WHERE PlanID IS NOT NULL AND DeleteStatus = 'N';
GO

-- Job card entries / inspection
IF COL_LENGTH('dbo.JobCardEntry', 'Operator') IS NULL
    ALTER TABLE dbo.JobCardEntry ADD Operator VARCHAR(50) NULL;
IF COL_LENGTH('dbo.JobCardEntry', 'Shift') IS NULL
    ALTER TABLE dbo.JobCardEntry ADD Shift VARCHAR(50) NULL;
IF COL_LENGTH('dbo.JobCardEntry', 'IdleReason') IS NULL
    ALTER TABLE dbo.JobCardEntry ADD IdleReason VARCHAR(100) NULL;
IF COL_LENGTH('dbo.JobCardEntry', 'RejectedQty') IS NULL
    ALTER TABLE dbo.JobCardEntry ADD RejectedQty NUMERIC(14, 2) NULL
        CONSTRAINT DF_JobCardEntry_RejectedQty DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.JobCardInspection', 'RejectionReason') IS NULL
    ALTER TABLE dbo.JobCardInspection ADD RejectionReason VARCHAR(100) NULL;
GO

-- Part Master grade rows
IF COL_LENGTH('dbo.MaterialGrade', 'SafetyStock') IS NULL
    ALTER TABLE dbo.MaterialGrade ADD SafetyStock NUMERIC(14, 2) NULL
        CONSTRAINT DF_MaterialGrade_SafetyStock DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.MaterialGrade', 'LeadTimeDays') IS NULL
    ALTER TABLE dbo.MaterialGrade ADD LeadTimeDays INT NULL
        CONSTRAINT DF_MaterialGrade_LeadTimeDays DEFAULT (0) WITH VALUES;
GO

-- Purchase requisition / store lines
IF COL_LENGTH('dbo.PurReqDtl', 'RequiredBy') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD RequiredBy DATE NULL;
IF COL_LENGTH('dbo.StoreReturnsDtl', 'ReturnType') IS NULL
    ALTER TABLE dbo.StoreReturnsDtl ADD ReturnType VARCHAR(20) NULL
        CONSTRAINT DF_StoreReturnsDtl_ReturnType DEFAULT ('Unused') WITH VALUES;
IF COL_LENGTH('dbo.StoreIssueDtl', 'HeatNo') IS NULL
    ALTER TABLE dbo.StoreIssueDtl ADD HeatNo VARCHAR(50) NULL;
GO

/*------------------------------------------------------------------------------
  2. SP_MachinePlanFields
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MachinePlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_MachinePlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_MachinePlanFields
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @CapacityUnit   VARCHAR(20),
    @ShiftsPerDay   INT,
    @EfficiencyPct  NUMERIC(5, 2),
    @MinPieceWt     NUMERIC(10, 3),
    @MaxPieceWt     NUMERIC(10, 3),
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Min NUMERIC(10, 3), @Max NUMERIC(10, 3);

    BEGIN TRY
        SELECT @Min = ISNULL(@MinPieceWt, ISNULL(MinPieceWt, 0)),
               @Max = ISNULL(@MaxPieceWt, ISNULL(MaxPieceWt, 0))
          FROM dbo.MachineMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @@ROWCOUNT = 0
        BEGIN SET @Message = N'Machine not found.'; RETURN; END

        IF @ShiftsPerDay IS NOT NULL AND (@ShiftsPerDay < 0 OR @ShiftsPerDay > 3)
        BEGIN SET @Message = N'Shifts per day must be between 0 and 3.'; RETURN; END
        IF @EfficiencyPct IS NOT NULL AND (@EfficiencyPct <= 0 OR @EfficiencyPct > 100)
        BEGIN SET @Message = N'Efficiency must be above 0% and at most 100%.'; RETURN; END
        IF @Min < 0 OR @Max < 0
        BEGIN SET @Message = N'Piece weights cannot be negative.'; RETURN; END
        IF @Max > 0 AND @Min > @Max
        BEGIN SET @Message = N'Min piece weight cannot be above the max piece weight.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.MachineMst
           SET CapacityUnit  = CASE WHEN @CapacityUnit IS NULL THEN CapacityUnit
                                    ELSE NULLIF(LTRIM(RTRIM(@CapacityUnit)), '') END,
               ShiftsPerDay  = ISNULL(@ShiftsPerDay, ISNULL(ShiftsPerDay, 1)),
               EfficiencyPct = ISNULL(@EfficiencyPct, ISNULL(EfficiencyPct, 100)),
               MinPieceWt    = @Min,
               MaxPieceWt    = @Max
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Machine planning fields saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  3. SP_RoutingChartPlanFields
     @PostForgeDays NULL = keep. @Steps NULL = leave the steps alone; else a
     JSON array in step order: [{ "setupMin": 15 }, { "setupMin": 0 }, ...]
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RoutingChartPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_RoutingChartPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RoutingChartPlanFields
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @PostForgeDays  INT,
    @Steps          NVARCHAR(MAX),
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @S TABLE (Pos INT PRIMARY KEY, SetupMin NUMERIC(9, 2));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.RoutingChartMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Route not found.'; RETURN; END

        IF @PostForgeDays IS NOT NULL AND (@PostForgeDays < 0 OR @PostForgeDays > 90)
        BEGIN SET @Message = N'Post-forge days must be between 0 and 90.'; RETURN; END

        IF @Steps IS NOT NULL AND ISJSON(@Steps) = 1
            INSERT INTO @S (Pos, SetupMin)
            SELECT CONVERT(INT, [key]) + 1,
                   ISNULL(TRY_CONVERT(NUMERIC(9, 2), JSON_VALUE(value, '$.setupMin')), 0)
              FROM OPENJSON(@Steps);

        IF EXISTS (SELECT 1 FROM @S WHERE SetupMin < 0 OR SetupMin > 1440)
        BEGIN
            SELECT TOP (1) @Message = N'Step ' + CONVERT(NVARCHAR(12), Pos)
                                    + N': setup time must be between 0 and 1440 minutes.'
              FROM @S WHERE SetupMin < 0 OR SetupMin > 1440 ORDER BY Pos;
            RETURN;
        END

        BEGIN TRANSACTION;

        IF @PostForgeDays IS NOT NULL
            UPDATE dbo.RoutingChartMst
               SET PostForgeDays = @PostForgeDays
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @Steps IS NOT NULL
            UPDATE d
               SET SetupMin = ISNULL(s.SetupMin, 0)
              FROM (SELECT TransSlNo, SetupMin,
                           ROW_NUMBER() OVER (ORDER BY SlNo, TransSlNo) AS rn
                      FROM dbo.RoutingChartDtl
                     WHERE MTransNo = @MTransNo AND DeleteStatus = 'N') c
              JOIN dbo.RoutingChartDtl d ON d.TransSlNo = c.TransSlNo
              LEFT JOIN @S s ON s.Pos = c.rn;

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Route planning fields saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  4. SP_ConvBOMPlanFields   (NULL = keep)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_ConvBOMPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_ConvBOMPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_ConvBOMPlanFields
    @ID         INT             OUTPUT,
    @MTransNo   INT,
    @BarDia     NUMERIC(8, 2),
    @CutLength  NUMERIC(8, 2),
    @YieldPct   NUMERIC(5, 2),
    @UserNo     INT,
    @SubscID    INT,
    @Message    NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.ConvBOM
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Record not found.'; RETURN; END

        IF ISNULL(@BarDia, 0) < 0 OR ISNULL(@CutLength, 0) < 0
        BEGIN SET @Message = N'Bar diameter and cut length cannot be negative.'; RETURN; END
        IF ISNULL(@YieldPct, 0) < 0 OR ISNULL(@YieldPct, 0) > 100
        BEGIN SET @Message = N'Yield must be between 0 and 100%.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.ConvBOM
           SET BarDia    = CASE WHEN @BarDia    IS NULL THEN BarDia    ELSE NULLIF(@BarDia, 0)    END,
               CutLength = CASE WHEN @CutLength IS NULL THEN CutLength ELSE NULLIF(@CutLength, 0) END,
               YieldPct  = CASE WHEN @YieldPct  IS NULL THEN YieldPct  ELSE NULLIF(@YieldPct, 0)  END
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Bar details saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  5. SP_DiePlanFields   (@DieStatus NULL = keep)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_DiePlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_DiePlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_DiePlanFields
    @ID         INT             OUTPUT,
    @MTransNo   INT,
    @DieStatus  VARCHAR(20),
    @UserNo     INT,
    @SubscID    INT,
    @Message    NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @St VARCHAR(20) = LTRIM(RTRIM(@DieStatus));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.DieMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Die not found.'; RETURN; END

        IF @St IS NOT NULL AND @St NOT IN ('Ready', 'In Repair', 'Scrapped')
        BEGIN SET @Message = N'Die Status must be Ready, In Repair or Scrapped.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.DieMst
           SET DieStatus = ISNULL(@St, ISNULL(DieStatus, 'Ready'))
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Die status saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6. SP_OADtlPlanFields
     @CommittedDate is always written (NULL = use the delivery date);
     @Priority / @CustomerMaterial NULL = keep.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_OADtlPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_OADtlPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_OADtlPlanFields
    @ID                INT             OUTPUT,
    @TransSlNo         INT,
    @CommittedDate     DATE,
    @Priority          VARCHAR(10),
    @CustomerMaterial  CHAR(1),
    @UserNo            INT,
    @SubscID           INT,
    @Message           NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Pr VARCHAR(10) = LTRIM(RTRIM(@Priority)), @CM CHAR(1) = UPPER(LTRIM(RTRIM(@CustomerMaterial)));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.OADtl
                        WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Line item not found.'; RETURN; END

        IF @Pr = '' SET @Pr = NULL;
        IF @CM = '' SET @CM = NULL;
        IF @Pr IS NOT NULL AND @Pr NOT IN ('Normal', 'Urgent')
        BEGIN SET @Message = N'Priority must be Normal or Urgent.'; RETURN; END
        IF @CM IS NOT NULL AND @CM NOT IN ('Y', 'N')
        BEGIN SET @Message = N'Customer Material must be Y or N.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.OADtl
           SET CommittedDate    = @CommittedDate,
               Priority         = ISNULL(@Pr, ISNULL(Priority, 'Normal')),
               CustomerMaterial = ISNULL(@CM, ISNULL(CustomerMaterial, 'N'))
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @TransSlNo;
        SET @Message = N'Line planning fields saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  7. SP_JobCardPlanFields  -  plan link, hammer, die and planned dates of a
     job card (0 / NULL = none). Called after SP_JobCardMst Insert / Update,
     and by the plan release (SQL 59) for the job cards it creates.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_JobCardPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_JobCardPlanFields
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @PlanID         INT,
    @PlanJobSeq     INT,
    @PlanLineSlNo   INT,
    @PlannedStart   DATE,
    @PlannedEnd     DATE,
    @MachineNo      INT,
    @DieNo          INT,
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.JobCardMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Job card not found.'; RETURN; END

        SET @MachineNo = NULLIF(@MachineNo, 0);
        SET @DieNo     = NULLIF(@DieNo, 0);
        SET @PlanID    = NULLIF(@PlanID, 0);

        IF @MachineNo IS NOT NULL AND NOT EXISTS (
               SELECT 1 FROM dbo.MachineMst
                WHERE MTransNo = @MachineNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'The planned machine no longer exists.'; RETURN; END

        IF @DieNo IS NOT NULL AND NOT EXISTS (
               SELECT 1 FROM dbo.DieMst
                WHERE MTransNo = @DieNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'The planned die no longer exists.'; RETURN; END

        IF @PlannedStart IS NOT NULL AND @PlannedEnd IS NOT NULL AND @PlannedEnd < @PlannedStart
        BEGIN SET @Message = N'Planned end cannot be before the planned start.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.JobCardMst
           SET PlanID       = @PlanID,
               PlanJobSeq   = CASE WHEN @PlanID IS NULL THEN NULL ELSE NULLIF(@PlanJobSeq, 0) END,
               PlanLineSlNo = CASE WHEN @PlanID IS NULL THEN NULL ELSE NULLIF(@PlanLineSlNo, 0) END,
               PlannedStart = @PlannedStart,
               PlannedEnd   = @PlannedEnd,
               MachineNo    = @MachineNo,
               DieNo        = @DieNo
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Job card plan link saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  8. SP_JobCardEntryPlanFields  -  operator / shift / idle reason / rejected
     qty of the live entry set just written by SP_JobCardEntrySave.
     @Entries = the same JSON array, same order (SlNo = position + 1):
       [{ "operator":"", "shift":"", "idleReason":"", "rejectedQty":0 }, ...]
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardEntryPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_JobCardEntryPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_JobCardEntryPlanFields
    @ID         INT             OUTPUT,
    @MTransNo   INT,
    @Entries    NVARCHAR(MAX),
    @UserNo     INT,
    @SubscID    INT,
    @Message    NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @E TABLE (SlNo INT PRIMARY KEY, Operator VARCHAR(50), Shift VARCHAR(50),
                      IdleReason VARCHAR(100), RejectedQty NUMERIC(14, 2));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.JobCardMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Job card not found.'; RETURN; END

        IF ISJSON(@Entries) = 1
            INSERT INTO @E (SlNo, Operator, Shift, IdleReason, RejectedQty)
            SELECT CONVERT(INT, a.[key]) + 1,
                   NULLIF(LTRIM(RTRIM(v.operator)), ''),
                   NULLIF(LTRIM(RTRIM(v.shift)), ''),
                   NULLIF(LTRIM(RTRIM(v.idleReason)), ''),
                   ISNULL(v.rejectedQty, 0)
              FROM OPENJSON(@Entries) a
             CROSS APPLY OPENJSON(a.[value]) WITH (
                    operator    VARCHAR(50),
                    shift       VARCHAR(50),
                    idleReason  VARCHAR(100),
                    rejectedQty NUMERIC(14, 2)) v;

        IF EXISTS (SELECT 1 FROM @E e
                     JOIN dbo.JobCardEntry x ON x.MTransNo = @MTransNo AND x.DeleteStatus = 'N' AND x.SlNo = e.SlNo
                    WHERE e.RejectedQty < 0 OR e.RejectedQty > x.Qty)
        BEGIN
            SELECT TOP (1) @Message = N'Entry ' + CONVERT(NVARCHAR(12), e.SlNo)
                                    + N': rejected qty must be between 0 and the processed qty.'
              FROM @E e
              JOIN dbo.JobCardEntry x ON x.MTransNo = @MTransNo AND x.DeleteStatus = 'N' AND x.SlNo = e.SlNo
             WHERE e.RejectedQty < 0 OR e.RejectedQty > x.Qty
             ORDER BY e.SlNo;
            RETURN;
        END

        BEGIN TRANSACTION;

        UPDATE x
           SET Operator    = e.Operator,
               Shift       = e.Shift,
               IdleReason  = e.IdleReason,
               RejectedQty = e.RejectedQty
          FROM dbo.JobCardEntry x
          JOIN @E e ON e.SlNo = x.SlNo
         WHERE x.MTransNo = @MTransNo AND x.DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Entry details saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  9. SP_JobCardInspectionPlanFields  -  rejection reason (NULL / '' = none)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_JobCardInspectionPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_JobCardInspectionPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_JobCardInspectionPlanFields
    @ID               INT             OUTPUT,
    @MTransNo         INT,
    @RejectionReason  VARCHAR(100),
    @UserNo           INT,
    @SubscID          INT,
    @Message          NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.JobCardInspection
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Inspection not found.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.JobCardInspection
           SET RejectionReason = CASE WHEN RejectedQty > 0 THEN NULLIF(LTRIM(RTRIM(@RejectionReason)), '') END
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Rejection reason saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  10. SP_MaterialGradePlanFields   (NULL = keep)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_MaterialGradePlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_MaterialGradePlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_MaterialGradePlanFields
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @SafetyStock    NUMERIC(14, 2),
    @LeadTimeDays   INT,
    @UserNo         INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.MaterialGrade
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Grade not found.'; RETURN; END

        IF ISNULL(@SafetyStock, 0) < 0
        BEGIN SET @Message = N'Safety stock cannot be negative.'; RETURN; END
        IF ISNULL(@LeadTimeDays, 0) < 0 OR ISNULL(@LeadTimeDays, 0) > 365
        BEGIN SET @Message = N'Lead time must be between 0 and 365 days.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.MaterialGrade
           SET SafetyStock  = ISNULL(@SafetyStock, ISNULL(SafetyStock, 0)),
               LeadTimeDays = ISNULL(@LeadTimeDays, ISNULL(LeadTimeDays, 0))
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Grade planning fields saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  11. SP_PurReqDtlPlanFields   (@RequiredBy NULL = header due date)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PurReqDtlPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_PurReqDtlPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PurReqDtlPlanFields
    @ID          INT             OUTPUT,
    @TransSlNo   INT,
    @RequiredBy  DATE,
    @UserNo      INT,
    @SubscID     INT,
    @Message     NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.PurReqDtl
                        WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Line item not found.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.PurReqDtl
           SET RequiredBy = @RequiredBy
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @TransSlNo;
        SET @Message = N'Required-by date saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  12. SP_StoreReturnsDtlPlanFields   (@ReturnType NULL = keep)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreReturnsDtlPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_StoreReturnsDtlPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_StoreReturnsDtlPlanFields
    @ID          INT             OUTPUT,
    @TransSlNo   INT,
    @ReturnType  VARCHAR(20),
    @UserNo      INT,
    @SubscID     INT,
    @Message     NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @T VARCHAR(20) = NULLIF(LTRIM(RTRIM(@ReturnType)), '');

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM dbo.StoreReturnsDtl
                        WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN SET @Message = N'Return line not found.'; RETURN; END

        IF @T IS NOT NULL AND @T NOT IN ('Unused', 'End Piece', 'Scrap')
        BEGIN SET @Message = N'Return Type must be Unused, End Piece or Scrap.'; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.StoreReturnsDtl
           SET ReturnType = ISNULL(@T, ISNULL(ReturnType, 'Unused'))
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @TransSlNo;
        SET @Message = N'Return type saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  13. SP_StoreIssueDtlPlanFields  -  heat number of an issue line
      NULL / '' = none. Otherwise it must be a heat number on an approved
      heat document of the line's part (and of its grade when both the line
      and the heat carry one).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreIssueDtlPlanFields', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_StoreIssueDtlPlanFields AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_StoreIssueDtlPlanFields
    @ID          INT             OUTPUT,
    @TransSlNo   INT,
    @HeatNo      VARCHAR(50),
    @UserNo      INT,
    @SubscID     INT,
    @Message     NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @H VARCHAR(50) = NULLIF(UPPER(LTRIM(RTRIM(@HeatNo))), ''),
            @PartNo INT, @Grade VARCHAR(50), @Code VARCHAR(50);

    BEGIN TRY
        SELECT @PartNo = PartNo, @Grade = LTRIM(RTRIM(ISNULL(Grade, ''))),
               @Code = ISNULL(NULLIF(LTRIM(RTRIM(PartCode)), ''), PartDescription)
          FROM dbo.StoreIssueDtl
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @PartNo IS NULL
        BEGIN SET @Message = N'Issue line not found.'; RETURN; END

        IF @H IS NOT NULL AND NOT EXISTS (
               SELECT 1
                 FROM dbo.HeatNoDtl d
                 JOIN dbo.HeatNoMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.PartNo = @PartNo
                  AND UPPER(LTRIM(RTRIM(d.HeatNo))) = @H
                  AND (@Grade = ''
                       OR LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(d.Grade)), ''), ISNULL(m.Grade, '')))) IN ('', @Grade)))
        BEGIN
            SET @Message = N'Heat No ' + @H + N' is not an approved heat of ' + ISNULL(@Code, N'this part')
                         + CASE WHEN @Grade <> '' THEN N' (grade ' + @Grade + N')' ELSE N'' END + N'.';
            RETURN;
        END

        BEGIN TRANSACTION;

        UPDATE dbo.StoreIssueDtl
           SET HeatNo = @H
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        COMMIT TRANSACTION;
        SET @ID = @TransSlNo;
        SET @Message = N'Heat number saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  14. LOOKUPS  -  'Idle Reason', 'Rejection Reason' (same pattern as SQL 52)
------------------------------------------------------------------------------*/
DECLARE @Types TABLE (MasterType VARCHAR(50) NOT NULL, Department VARCHAR(50) NOT NULL);
INSERT INTO @Types (MasterType, Department) VALUES
    ('Idle Reason',      'Production'),
    ('Rejection Reason', 'Production');

INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, T.Department, 'N', S.SubscID
  FROM  @Types T
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMasterList L
            WHERE L.MasterType   = T.MasterType
              AND L.SubscID      = S.SubscID
              AND L.DeleteStatus = 'N');
GO

DECLARE @Defaults TABLE (MasterType VARCHAR(50) NOT NULL, MasterName VARCHAR(50) NOT NULL, Ord INT NOT NULL);
INSERT INTO @Defaults (MasterType, MasterName, Ord) VALUES
    ('Idle Reason',      'Die Change',           1),
    ('Idle Reason',      'Waiting For Material', 2),
    ('Idle Reason',      'Breakdown',            3),
    ('Idle Reason',      'No Operator',          4),
    ('Idle Reason',      'Power Cut',            5),
    ('Rejection Reason', 'Crack',                1),
    ('Rejection Reason', 'Under Fill',           2),
    ('Rejection Reason', 'Lap',                  3),
    ('Rejection Reason', 'Mismatch',             4),
    ('Rejection Reason', 'Dimension',            5);

INSERT INTO dbo.OtherMaster
       (MasterType, MasterName, Department, SystemIP, CreatedBy, CreatedOn,
        DeleteStatus, LockStatus, LockCounter, SubscID)
SELECT  D.MasterType, D.MasterName, 'Production', 'SQL58', 1, GETDATE(),
        'N', 'Y', 0, S.SubscID
  FROM  @Defaults D
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMaster O
            WHERE O.MasterType   = D.MasterType
              AND O.SubscID      = S.SubscID
              AND O.DeleteStatus = 'N')
 ORDER BY S.SubscID, D.MasterType, D.Ord;
GO

-- What exists now.
SELECT L.SubscID, L.MasterType,
       (SELECT COUNT(*) FROM dbo.OtherMaster O
         WHERE O.MasterType = L.MasterType AND O.SubscID = L.SubscID AND O.DeleteStatus = 'N') AS Rows_
  FROM dbo.OtherMasterList L
 WHERE L.DeleteStatus = 'N' AND L.MasterType IN ('Idle Reason', 'Rejection Reason')
 ORDER BY L.SubscID, L.MasterType;
GO

PRINT '58_PlanningFieldsExistingPages.sql applied.';
GO
