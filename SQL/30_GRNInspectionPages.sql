/*==============================================================================
  30. GRN INSPECTION PAGES  -  what GRNQC.html and GRNNCClosure.html need
      beyond 15_GRNProcedures.sql

  Run after 13_PurchaseOrderGRNTables.sql and 15_GRNProcedures.sql.
  Idempotent - safe to re-run.

  ----------------------------------------------------------------------------
  WHAT CHANGES
  ----------------------------------------------------------------------------
  1. OtherMasterList gets the 'QC Verify By' master type, per subscription,
     so the inspection form's QC Verify By dropdown can be maintained from
     Other Master (the legacy screen read the same master type).

  2. SP_GRNInspPost
       * Reopen no longer clears InspNo.  Editing a posted inspection is
         Reopen -> rewrite -> Post; clearing the number meant the edit came
         back with a NEW number (MAX + 1) whenever another GRN had been
         inspected in the meantime.
       * Post re-uses the GRN's InspNo when it already holds one, and only
         allocates MAX + 1 for a GRN that has never been inspected.
     A withdrawn inspection therefore keeps its number too, as the legacy
     application did (RecDeleteQC left InspNo alone).

  3. SP_GRNNCDtl
       * A heat-traced part (PartMaster.MfrLicNo = 'Y', the flag the legacy
         application used for "heat no required") may only be rejected /
         reworked / accepted-on-deviation against a heat number that was
         recorded for that part and grade on THIS GRN (HeatNoMst.TargetTransNo).
         This is the legacy CheckHeatNo rule, moved into the procedure so it
         holds however the endpoint is called.

  SP_GRNInspDtl and SP_GRNNCClose are unchanged.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. 'QC Verify By' master type
------------------------------------------------------------------------------*/
INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  'QC Verify By', 'Admin', 'N', S.SubscID
  FROM  (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE  NOT EXISTS (
            SELECT 1 FROM dbo.OtherMasterList L
             WHERE L.MasterType   = 'QC Verify By'
               AND L.SubscID      = S.SubscID
               AND L.DeleteStatus = 'N');
GO


/*------------------------------------------------------------------------------
  2. SP_GRNInspPost  -  post the inward inspection

  @Status
      Post     InspNo (kept, or next in the series), QCDate, QCverifyBy,
               GRNType = 'Q'
      Reopen   back to GRNType = 'N'; QCDate and QCverifyBy cleared,
               InspNo KEPT so a re-post carries the same number

  Post checks the whole GRN:
      * every line inspected  (Acc + Rej + Rew + AOD = RecQty)
      * every non-zero Rej / Rew / AOD quantity backed by NC lines of the
        matching type whose NCQty adds up to exactly that quantity
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNInspPost', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNInspPost AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNInspPost
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @QCDate         DATE,
    @QCverifyBy     VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @GRNType   CHAR(1),
                @DocSeries CHAR(10),
                @DocYear   INT,
                @CurInsp   VARCHAR(50),
                @NextInsp  INT,
                @BadLine   NUMERIC(12,2),
                @BadType   VARCHAR(10),
                @BadQty    NUMERIC(14,4),
                @BadNC     NUMERIC(14,4);

        SELECT @GRNType = GRNType, @DocSeries = DocSeries, @DocYear = DocYear,
               @CurInsp = NULLIF(LTRIM(RTRIM(ISNULL(InspNo, ''))), '')
          FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        /*==================================================================
          POST
        ==================================================================*/
        IF (@Status = 'Post')
        BEGIN
            IF @GRNType IN ('Q', 'C')
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has already been inspected.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has no lines to inspect.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Everything received has to be accounted for.
            SELECT TOP 1 @BadLine = SlNo
              FROM dbo.GRNDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND AccQty + RejQty + RewQty + AODQty <> RecQty
             ORDER BY SlNo;

            IF @BadLine IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Line ' + CONVERT(VARCHAR(20), @BadLine) +
                               ' has not been fully inspected. Accepted + rejected + rework + AOD must equal the received quantity.';
                COMMIT TRANSACTION; RETURN;
            END

            /*  Every non-conforming quantity must be explained by NC lines of
                the matching type, and they must add up.  */
            SELECT TOP 1 @BadLine = X.SlNo, @BadType = X.NCType,
                         @BadQty = X.Qty, @BadNC = X.NCQty
              FROM (
                    SELECT D.SlNo, 'Reject' AS NCType, D.RejQty AS Qty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'Reject'
                                      AND N.DeleteStatus = 'N'), 0) AS NCQty
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.RejQty > 0
                    UNION ALL
                    SELECT D.SlNo, 'Rework', D.RewQty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'Rework'
                                      AND N.DeleteStatus = 'N'), 0)
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.RewQty > 0
                    UNION ALL
                    SELECT D.SlNo, 'AOD', D.AODQty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'AOD'
                                      AND N.DeleteStatus = 'N'), 0)
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.AODQty > 0
                   ) X
             WHERE X.Qty <> X.NCQty
             ORDER BY X.SlNo, X.NCType;

            IF @BadLine IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Line ' + CONVERT(VARCHAR(20), @BadLine) + ': ' + @BadType +
                               ' quantity is ' + CONVERT(VARCHAR(20), @BadQty) +
                               ' but the non-conformance detail adds up to ' + CONVERT(VARCHAR(20), @BadNC) + '.';
                COMMIT TRANSACTION; RETURN;
            END

            /*  A GRN that was inspected before (then reopened) keeps its
                number.  Otherwise the next number in the series; TRY_CONVERT
                keeps a hand-typed, non-numeric legacy value from breaking MAX.  */
            IF @CurInsp IS NULL
            BEGIN
                SELECT @NextInsp = ISNULL(MAX(TRY_CONVERT(INT, InspNo)), 0) + 1
                  FROM dbo.GRNMst
                 WHERE DocSeries = @DocSeries AND DocYear = @DocYear
                   AND SubscID = @SubscID AND DeleteStatus = 'N'
                   AND InspNo IS NOT NULL;

                SET @CurInsp = CONVERT(VARCHAR(50), @NextInsp);
            END

            UPDATE dbo.GRNMst
               SET InspNo     = @CurInsp,
                   QCDate     = ISNULL(@QCDate, CONVERT(DATE, GETDATE())),
                   QCverifyBy = LTRIM(RTRIM(@QCverifyBy)),
                   GRNType    = 'Q',
                   SystemIP   = @SystemIP,
                   ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = 'Inspection posted. Inspection No. ' + @CurInsp + '.';
        END

        /*==================================================================
          REOPEN
        ==================================================================*/
        ELSE IF (@Status = 'Reopen')
        BEGIN
            IF @GRNType = 'N'
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has not been inspected yet.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @GRNType = 'C'
            BEGIN
                SET @ID = 0;
                SET @Message = 'The non-conformance has been closed. Reopen the closure first.';
                COMMIT TRANSACTION; RETURN;
            END

            -- InspNo is deliberately kept: see the header of this script.
            UPDATE dbo.GRNMst
               SET GRNType    = 'N',
                   QCDate     = NULL,
                   QCverifyBy = NULL,
                   SystemIP   = @SystemIP,
                   ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo; SET @Message = 'Inspection reopened.';
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
  3. SP_GRNNCDtl  -  non-conformance lines raised during inspection

  @Status:  Insert / Update / Delete / DeleteAll

  One row per defect found on a receipt line.  NCType is 'Reject', 'Rework' or
  'AOD'; the part, grade and unit are copied from the receipt line rather than
  taken from the caller, so an NC cannot describe a part that was not received.

  Total NCQty on a line may not exceed what was received on it.

  A heat-traced part (PartMaster.MfrLicNo = 'Y') needs a heat number that was
  recorded against this GRN, for that part and grade, on the Heat No. screen.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNNCDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNNCDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNNCDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @GRNTransSlNo   INT,
    @NCType         VARCHAR(10),
    @HeatNo         VARCHAR(10),
    @NCRemarks      VARCHAR(500),
    @NCQty          NUMERIC(12,4),
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

        DECLARE @MTransNo INT, @PartNo INT, @GradeNo INT, @Grade VARCHAR(50),
                @Unit VARCHAR(20), @RecQty NUMERIC(12,4), @GRNType CHAR(1),
                @Existing NUMERIC(14,4), @SlNo NUMERIC(12,2);

        SET @NCType = LTRIM(RTRIM(ISNULL(@NCType, '')));
        SET @HeatNo = LEFT(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), 10);

        IF (@Status = 'DeleteAll')
        BEGIN
            /*  DeleteAll works on the whole GRN, addressed by @GRNTransSlNo
                carrying the GRN's MTransNo - so guard it separately.  */
            UPDATE dbo.GRNNCDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @GRNTransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @GRNTransSlNo; SET @Message = 'All non-conformance lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active non-conformance lines found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Delete')
        BEGIN
            SELECT @MTransNo = MTransNo FROM dbo.GRNNCDtl
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @MTransNo IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GRNType = GRNType FROM dbo.GRNMst WHERE MTransNo = @MTransNo;
            IF @GRNType IN ('Q', 'C')
            BEGIN
                SET @ID = 0;
                SET @Message = 'The inspection has been posted. Reopen it before changing non-conformance lines.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNNCDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            SET @ID = @TransSlNo; SET @Message = 'Non-conformance line deleted successfully.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update
        ------------------------------------------------------------------*/
        IF @NCType NOT IN ('Reject', 'Rework', 'AOD')
        BEGIN
            SET @ID = 0; SET @Message = 'NC Type must be Reject, Rework or AOD.';
            COMMIT TRANSACTION; RETURN;
        END

        IF ISNULL(@NCQty, 0) <= 0
        BEGIN
            SET @ID = 0; SET @Message = 'NC quantity must be greater than zero.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @MTransNo = D.MTransNo, @PartNo = D.PartNo, @GradeNo = D.GradeNo,
               @Grade = D.Grade, @Unit = D.Unit, @RecQty = D.RecQty
          FROM dbo.GRNDtl D
         WHERE D.TransSlNo = @GRNTransSlNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N';

        IF @MTransNo IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Receipt line not found.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @GRNType = GRNType FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IN ('Q', 'C')
        BEGIN
            SET @ID = 0;
            SET @Message = 'The inspection has been posted. Reopen it before adding non-conformance lines.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @Existing = ISNULL(SUM(NCQty), 0)
          FROM dbo.GRNNCDtl
         WHERE GRNTransSlNo = @GRNTransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N'
           AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo);

        IF @Existing + @NCQty > @RecQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Non-conformance of ' + CONVERT(VARCHAR(20), @Existing + @NCQty) +
                           ' exceeds the received quantity of ' + CONVERT(VARCHAR(20), @RecQty) + '.';
            COMMIT TRANSACTION; RETURN;
        END

        /*  Heat-traced part: the heat number must have come in on this GRN.  */
        IF EXISTS (SELECT 1 FROM dbo.PartMaster P
                    WHERE P.MTransNo = @PartNo
                      AND UPPER(LTRIM(RTRIM(ISNULL(P.MfrLicNo, '')))) = 'Y')
        BEGIN
            IF @HeatNo = ''
            BEGIN
                SET @ID = 0; SET @Message = 'This part is heat traced. Enter the heat number.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1
                             FROM dbo.HeatNoDtl HD
                             JOIN dbo.HeatNoMst HM ON HM.MTransNo = HD.MTransNo
                            WHERE HM.TargetTransNo = @MTransNo
                              AND HM.SubscID       = @SubscID
                              AND HM.DeleteStatus  = 'N'
                              AND HD.DeleteStatus  = 'N'
                              AND HD.PartNo        = @PartNo
                              AND LTRIM(RTRIM(ISNULL(HD.Grade, ''))) = LTRIM(RTRIM(ISNULL(@Grade, '')))
                              AND HD.HeatNo        = @HeatNo)
            BEGIN
                SET @ID = 0;
                SET @Message = 'Heat number ' + @HeatNo + ' was not received on this GRN for this part.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.GRNNCDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.GRNNCDtl
            (SlNo, MTransNo, PartNo, GradeNo, Grade, NCType, HeatNo, NCRemarks,
             NCQty, Unit, GRNTransSlNo, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @GradeNo, @Grade, @NCType,
             @HeatNo, LTRIM(RTRIM(@NCRemarks)),
             @NCQty, @Unit, @GRNTransSlNo, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Non-conformance line saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNNCDtl
               SET NCType = @NCType,
                   HeatNo = @HeatNo,
                   NCRemarks = LTRIM(RTRIM(@NCRemarks)),
                   NCQty = @NCQty,
                   PartNo = @PartNo, GradeNo = @GradeNo, Grade = @Grade, Unit = @Unit,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND GRNTransSlNo = @GRNTransSlNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Non-conformance line updated successfully.'; END
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

-- What was written.
SELECT MTransNo, MasterType, Department, SubscID
  FROM dbo.OtherMasterList
 WHERE MasterType = 'QC Verify By' AND DeleteStatus = 'N';

SELECT name, modify_date
  FROM sys.procedures
 WHERE name IN ('SP_GRNInspPost', 'SP_GRNNCDtl');
GO
