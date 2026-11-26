/* ============================================================================
   79_ReworkReinspection.sql  (idempotent)

   Rework loop for Job Card Inspection. Before this script pieces marked
   Rework simply disappeared: they were neither OK (FG stock) nor Rejected,
   and nothing let them be inspected again.

   Now an inspection has a type:
     Normal  - first-pass inspection of pieces processed at the inspection
               step (capped by what is awaiting inspection, as before).
     Rework  - re-inspection of reworked pieces, capped by the rework still
               waiting = rework marked on all inspections - pieces already
               re-inspected. Its OK goes to FG stock like any OK (VW_StoreStock
               PRODUCTION reads OKQty of every inspection); its Rework waits
               again; its Rejected is final.
   VW_JobCardSummary gets ReworkInspectedQty and ReworkPendingQty; its
   InspectedQty / ReworkQty stay first-pass figures.
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.JobCardInspection', 'InspType') IS NULL
    ALTER TABLE dbo.JobCardInspection ADD InspType VARCHAR(10) NOT NULL
        CONSTRAINT DF_JobCardInspection_InspType DEFAULT ('Normal') WITH VALUES;
GO
IF OBJECT_ID('dbo.CK_JobCardInspection_InspType', 'C') IS NULL
    ALTER TABLE dbo.JobCardInspection ADD CONSTRAINT CK_JobCardInspection_InspType CHECK (InspType IN ('Normal', 'Rework'));
GO
/* Reworked pieces still waiting for re-inspection (negative = more were
   re-inspected than marked Rework, which the SP refuses). */
CREATE OR ALTER FUNCTION dbo.FN_JobCardReworkBalance(@JobCard INT)
RETURNS NUMERIC(18, 3)
AS
BEGIN
    RETURN ISNULL((SELECT SUM(ReworkQty) - SUM(CASE WHEN InspType = 'Rework' THEN InspectedQty ELSE 0 END)
                     FROM dbo.JobCardInspection
                    WHERE JobCardTransNo = @JobCard AND DeleteStatus = 'N'), 0);
END;
GO
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
        CASE WHEN cur.SlNo IS NOT NULL THEN CONCAT(cur.SlNo, N' - ', cur.OperationName)
             WHEN ISNULL(st.StepCount, 0) > 0 THEN N'All operations done'
             ELSE N'' END                               AS CurrentStep,
        ins.SlNo                                        AS InspectionSlNo,
        CASE WHEN ins.SlNo IS NOT NULL THEN CONCAT(ins.SlNo, N' - ', ins.OperationName) ELSE N'' END
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
        -- SQL 79: rework loop. Pieces marked Rework (on any inspection) wait
        -- for a Rework re-inspection; ReworkPendingQty is what still waits.
        ISNULL(iq.ReworkInspectedQty, 0)                AS ReworkInspectedQty,
        CASE WHEN ISNULL(iq.ReworkAllQty, 0) - ISNULL(iq.ReworkInspectedQty, 0) > 0
             THEN ISNULL(iq.ReworkAllQty, 0) - ISNULL(iq.ReworkInspectedQty, 0) ELSE 0 END
                                                        AS ReworkPendingQty,
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
 -- SQL 79: InspectedQty / ReworkQty are first-pass (Normal) inspections only,
 -- so AwaitingInspectionQty is not reduced by re-inspections; OK and Rejected
 -- count every inspection (a re-inspected piece ends OK or Rejected).
 OUTER APPLY (SELECT SUM(CASE WHEN x.InspType = 'Rework' THEN 0 ELSE x.InspectedQty END) AS InspectedQty,
                     SUM(x.OKQty) AS OKQty,
                     SUM(CASE WHEN x.InspType = 'Rework' THEN 0 ELSE x.ReworkQty END) AS ReworkQty,
                     SUM(x.ReworkQty) AS ReworkAllQty,
                     SUM(CASE WHEN x.InspType = 'Rework' THEN x.InspectedQty ELSE 0 END) AS ReworkInspectedQty,
                     SUM(x.RejectedQty) AS RejectedQty,
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

CREATE OR ALTER PROCEDURE dbo.SP_JobCardInspection
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
    @SubscID        INT,
    @InspType       VARCHAR(10)    = 'Normal'   -- SQL 79: 'Normal' | 'Rework' (re-inspection of reworked pieces)
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
        BEGIN TRAN;
            DECLARE @DelJob INT = (SELECT JobCardTransNo FROM dbo.JobCardInspection WHERE MTransNo = @MTransNo);
            SELECT @DelJob = j.MTransNo FROM dbo.JobCardMst j WITH (UPDLOCK, HOLDLOCK) WHERE j.MTransNo = @DelJob;
            UPDATE dbo.JobCardInspection
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, SystemIP = @SystemIP,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID;
            -- SQL 79: rework already re-inspected must stay covered by rework marked.
            IF dbo.FN_JobCardReworkBalance(@DelJob) < 0
            BEGIN
                ROLLBACK;
                SET @Message = 'Reworked pieces of this inspection have already been re-inspected. Delete that Rework re-inspection first.';
                RETURN;
            END;
        COMMIT;
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

        -- SQL 79: an inspection keeps its type; an Update cannot switch it.
        IF @Status = 'Update'
            SELECT @InspType = ISNULL(InspType, 'Normal') FROM dbo.JobCardInspection WHERE MTransNo = @MTransNo;
        SET @InspType = CASE WHEN @InspType = 'Rework' THEN 'Rework' ELSE 'Normal' END;

        DECLARE @Already NUMERIC(18, 3) =
            ISNULL((SELECT SUM(InspectedQty) FROM dbo.JobCardInspection
                     WHERE JobCardTransNo = @JobCardTransNo AND DeleteStatus = 'N' AND ISNULL(InspType, 'Normal') = 'Normal'
                       AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END), 0);

        -- Rework re-inspection: capped by the reworked pieces still waiting
        -- (rework marked on the other inspections less what was re-inspected).
        IF @InspType = 'Rework'
        BEGIN
            DECLARE @ReworkWaiting NUMERIC(18, 3) =
                ISNULL((SELECT SUM(ReworkQty) - SUM(CASE WHEN ISNULL(InspType, 'Normal') = 'Rework' THEN InspectedQty ELSE 0 END)
                          FROM dbo.JobCardInspection
                         WHERE JobCardTransNo = @JobCardTransNo AND DeleteStatus = 'N'
                           AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END), 0);
            IF @InspectedQty > @ReworkWaiting
            BEGIN
                ROLLBACK;
                SET @Message = CONCAT('Only ', FORMAT(CASE WHEN @ReworkWaiting > 0 THEN @ReworkWaiting ELSE 0 END, '0.###'),
                                      ' reworked pieces of ', @No, ' are waiting for re-inspection.');
                RETURN;
            END;
        END
        ELSE IF @Already + @InspectedQty > @Eligible
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
                    InspectedQty, OKQty, ReworkQty, RejectedQty, Remarks, SystemIP, CreatedBy, CreatedOn, SubscID, InspType)
            VALUES (@Year, @DocNo, CONCAT('INS-', @Year, '-', RIGHT('0000' + CAST(@DocNo AS VARCHAR(10)), 4)),
                    @InspDate, @JobCardTransNo, @InspectedBy,
                    @InspectedQty, @OKQty, @ReworkQty, @RejectedQty, @Remarks, @SystemIP, @UserNo, SYSDATETIME(), @SubscID, @InspType);
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

        -- Lowering the rework of an inspection may not undercut re-inspections already made.
        IF dbo.FN_JobCardReworkBalance(@JobCardTransNo) < 0
        BEGIN
            ROLLBACK;
            SET @ID = 0;
            SET @Message = CONCAT('More reworked pieces of ', @No, ' have already been re-inspected than this leaves marked Rework.');
            RETURN;
        END;
    COMMIT;
END;
GO
