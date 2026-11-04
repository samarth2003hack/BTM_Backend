/*==============================================================================
  40. PURCHASE CHAIN - GRADE ON EVERY LINE, TXN-UNIT PRICING, RFQ DELETE GUARD

  1. GRADE  (Purchase Requisition -> RFQ -> RFQ Price Update -> Purchase Order)
     The grade of a line is picked from dbo.MaterialGrade - the grades kept for
     that one part (MaterialGrade.PartNo = the line's PartNo). Both the number
     and the name are stored:
         GradeNo = MaterialGrade.GradeNo     Grade = MaterialGrade.Grade
     PurReqDtl / RFQDtl / RFQPriceDtl gain GradeNo + Grade (PODtl had them).

       SP_PurReqDtl    grade is MANDATORY on Insert / Update, and must be one of
                       the part's grades. The name is re-read from the master,
                       so the stored name can never disagree with the number.
       SP_RFQDtl       grade is carried from the requisition line - optional
       SP_RFQPriceDtl  here, because lines raised before this script have none.
                       When a GradeNo is sent it must belong to the part.
       SP_PODtl        new optional @GradeNo (LAST). GradeNo now means
                       MaterialGrade.GradeNo; before, it was looked up in
                       MaterialStd by name, which was a different number.
       SP_POGenerateFromQuote copies the quote line's grade onto the PO line.
       VW_PurReqItemsForPO    exposes GradeNo / Grade / TxnUnit / ConvRatio
                              so a PR-only order carries the grade too.

     The new parameters are optional and LAST, so an older caller that does not
     send them still binds.

  2. TXN UNIT  (Purchase Order)
     Part Master's TxnUnit says which quantity the rate is quoted on:
     'P' primary (Qty) or 'S' secondary (Qty2 = Qty x ConversionRatio).
     SP_PODtl priced every line on Qty, so a part bought per kg but counted in
     Nos was valued at Nos x rate-per-kg. It now prices on Qty2 when TxnUnit is
     'S', and always stores Qty2 = Qty x ConvRatio. SP_PODtlPrice itself is
     unchanged (GRN still calls it with its own quantity).

  3. RFQ DELETE  (SP_RFQMst 'Delete')
     - refused once any live supplier price (RFQPriceMst) has been entered
       against the revision - the prices would be orphaned;
     - refused on a superseded revision (only the current one may be deleted);
     - deleting an amendment re-instates the previous live revision as the
       current one (DocAmdStatus 'N'). Before, deleting Amd 1 left Amd 0 marked
       superseded, so the RFQ vanished from every later step.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures.
  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  COLUMNS
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.PurReqDtl', 'GradeNo') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD GradeNo INT NULL;
IF COL_LENGTH('dbo.PurReqDtl', 'Grade') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD Grade VARCHAR(50) NULL;
IF COL_LENGTH('dbo.RFQDtl', 'GradeNo') IS NULL
    ALTER TABLE dbo.RFQDtl ADD GradeNo INT NULL;
IF COL_LENGTH('dbo.RFQDtl', 'Grade') IS NULL
    ALTER TABLE dbo.RFQDtl ADD Grade VARCHAR(50) NULL;
IF COL_LENGTH('dbo.RFQPriceDtl', 'GradeNo') IS NULL
    ALTER TABLE dbo.RFQPriceDtl ADD GradeNo INT NULL;
IF COL_LENGTH('dbo.RFQPriceDtl', 'Grade') IS NULL
    ALTER TABLE dbo.RFQPriceDtl ADD Grade VARCHAR(50) NULL;
GO

/*------------------------------------------------------------------------------
  FN_PartGradeName - the grade's name when GradeNo is one of the part's live
  grades, otherwise NULL.
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_PartGradeName
(
    @PartNo     INT,
    @GradeNo    INT,
    @SubscID    INT
)
RETURNS VARCHAR(50)
AS
BEGIN
    RETURN (SELECT TOP 1 LTRIM(RTRIM(Grade))
              FROM dbo.MaterialGrade
             WHERE PartNo = @PartNo AND GradeNo = @GradeNo
               AND SubscID = @SubscID AND DeleteStatus = 'N'
             ORDER BY MTransNo DESC);
END
GO

/*------------------------------------------------------------------------------
  SP_PurReqDtl - grade mandatory
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PurReqDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PurReqDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PurReqDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12, 2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(14, 4),
    @Unit           VARCHAR(10),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @OAYear         INT = NULL,
    @OANo           INT = NULL,
    @OAID           INT = NULL,
    @GradeNo        INT = NULL,
    @Grade          VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SET @Message = dbo.FN_OALinkCheck(@OAYear, @OANo, @OAID, @SubscID);
            IF @Message IS NOT NULL
            BEGIN
                SET @ID = 0;
                COMMIT TRANSACTION; RETURN;
            END

            IF ISNULL(@GradeNo, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Grade is required.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @Grade = dbo.FN_PartGradeName(@PartNo, @GradeNo, @SubscID);
            IF @Grade IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'The selected grade does not belong to this item.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.PurReqDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.PurReqDtl
            (SlNo, MTransNo, PartNo, ItemRemarks, Qty, Unit, OAYear, OANo, OAID,
             GradeNo, Grade, SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, LTRIM(RTRIM(@ItemRemarks)), @Qty,
             LTRIM(RTRIM(@Unit)), NULLIF(@OAYear, 0), NULLIF(@OANo, 0), NULLIF(@OAID, 0),
             @GradeNo, @Grade,
             @SystemIP, @CreatedBy, @CreatedOn, 'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET PartNo = @PartNo, ItemRemarks = LTRIM(RTRIM(@ItemRemarks)),
                Qty = @Qty, Unit = LTRIM(RTRIM(@Unit)),
                OAYear = NULLIF(@OAYear, 0), OANo = NULLIF(@OANo, 0), OAID = NULLIF(@OAID, 0),
                GradeNo = @GradeNo, Grade = @Grade,
                SystemIP = @SystemIP,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
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
  SP_RFQDtl - grade carried from the requisition (optional)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RFQDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RFQDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RFQDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SLNo           NUMERIC(12, 2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(12, 4),
    @Unit           VARCHAR(10),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @GradeNo        INT = NULL,
    @Grade          VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SET @GradeNo = NULLIF(@GradeNo, 0);
            SET @Grade   = NULL;
            IF @GradeNo IS NOT NULL
            BEGIN
                SET @Grade = dbo.FN_PartGradeName(@PartNo, @GradeNo, @SubscID);
                IF @Grade IS NULL
                BEGIN
                    SET @ID = 0; SET @Message = 'The selected grade does not belong to this item.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SLNo = ISNULL(MAX(SLNo), 0) + 1
            FROM dbo.RFQDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.RFQDtl
            (SLNo, MTransNo, PartNo, ItemRemarks, Qty, Unit, GradeNo, Grade, SystemIP,
             CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
            (@SLNo, @MTransNo, @PartNo, LTRIM(RTRIM(@ItemRemarks)), @Qty,
             LTRIM(RTRIM(@Unit)), @GradeNo, @Grade, @SystemIP, @CreatedBy, @CreatedOn,
             'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RFQDtl
            SET PartNo = @PartNo, ItemRemarks = LTRIM(RTRIM(@ItemRemarks)),
                Qty = @Qty, Unit = LTRIM(RTRIM(@Unit)),
                GradeNo = @GradeNo, Grade = @Grade, SystemIP = @SystemIP,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RFQDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.RFQDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
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
  SP_RFQPriceDtl - grade carried from the RFQ line (optional)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RFQPriceDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RFQPriceDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RFQPriceDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SLNo           NUMERIC(12, 2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(12, 4),
    @Unit           VARCHAR(10),
    @Rate           NUMERIC(12, 4),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @GradeNo        INT = NULL,
    @Grade          VARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SET @GradeNo = NULLIF(@GradeNo, 0);
            SET @Grade   = NULL;
            IF @GradeNo IS NOT NULL
            BEGIN
                SET @Grade = dbo.FN_PartGradeName(@PartNo, @GradeNo, @SubscID);
                IF @Grade IS NULL
                BEGIN
                    SET @ID = 0; SET @Message = 'The selected grade does not belong to this item.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SLNo = ISNULL(MAX(SLNo), 0) + 1
            FROM dbo.RFQPriceDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.RFQPriceDtl
            (SLNo, MTransNo, PartNo, ItemRemarks, Qty, Unit, Rate, GradeNo, Grade, SystemIP,
             CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
            (@SLNo, @MTransNo, @PartNo, LTRIM(RTRIM(@ItemRemarks)), @Qty,
             LTRIM(RTRIM(@Unit)), @Rate, @GradeNo, @Grade, @SystemIP, @CreatedBy, @CreatedOn,
             'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RFQPriceDtl
            SET PartNo = @PartNo, ItemRemarks = LTRIM(RTRIM(@ItemRemarks)),
                Qty = @Qty, Unit = LTRIM(RTRIM(@Unit)), Rate = @Rate,
                GradeNo = @GradeNo, Grade = @Grade,
                SystemIP = @SystemIP, ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RFQPriceDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.RFQPriceDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
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
  SP_RFQMst - Delete guard + re-instate the previous revision.
  Everything except the 'Delete' branch is the live definition unchanged.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RFQMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RFQMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RFQMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @ValidTill      DATE,
    @DocAmdNo       SMALLINT,
    @PRTransNo      INT,
    @DocNotes       VARCHAR(250),
    @DocAmdNotes    VARCHAR(250),
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

        IF (@Status = 'Insert')
        BEGIN
            IF (@DocAmdNo = 0)
            BEGIN
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.RFQMst
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;
            END
            ELSE IF (@DocAmdNo > 0)
            BEGIN
                UPDATE dbo.RFQMst SET DocAmdStatus = 'Y'
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
                  AND SubscID = @SubscID AND DocAmdNo < @DocAmdNo AND DeleteStatus = 'N';
            END

            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'RFQ already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.RFQMst
                (DocSeries, DocYear, DocNo, DocDate, ValidTill, DocAmdNo, DocAmdStatus,
                 PRTransNo, DocNotes, DocAmdNotes, DocStatus, SystemIP,
                 CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
                 LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @ValidTill, @DocAmdNo, 'N',
                 @PRTransNo, LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@DocAmdNotes)),
                 'N', @SystemIP, @CreatedBy, @CreatedOn,
                 'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'RFQ saved successfully.';
            END
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N'
              AND SubscID = @SubscID AND MTransNo <> @MTransNo;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'RFQ already exists.'; END
            ELSE
            BEGIN
                UPDATE dbo.RFQMst
                SET DocDate = @DocDate, ValidTill = @ValidTill, PRTransNo = @PRTransNo,
                    DocNotes = LTRIM(RTRIM(@DocNotes)), DocAmdNotes = LTRIM(RTRIM(@DocAmdNotes)),
                    SystemIP = @SystemIP, ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                    LockStatus = ISNULL(@LockStatus, LockStatus),
                    LockCounter = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            DECLARE @dSeries CHAR(10), @dYear INT, @dNo INT, @dAmdNo SMALLINT, @dAmdStatus CHAR(1);

            SELECT @dSeries = DocSeries, @dYear = DocYear, @dNo = DocNo,
                   @dAmdNo = DocAmdNo, @dAmdStatus = DocAmdStatus
              FROM dbo.RFQMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @dNo IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @dAmdStatus = 'Y'
            BEGIN
                SET @ID = 0;
                SET @Message = 'This is a superseded revision of the RFQ. Only the current revision can be deleted.';
                COMMIT TRANSACTION; RETURN;
            END

            IF EXISTS (SELECT 1 FROM dbo.RFQPriceMst
                        WHERE TargetTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Supplier prices have already been updated against this RFQ. Delete the price updates first.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.RFQMst
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            -- Deleting an amendment hands "current" back to the latest live
            -- revision before it.
            IF @dAmdNo > 0
                UPDATE dbo.RFQMst SET DocAmdStatus = 'N'
                 WHERE MTransNo = (SELECT TOP 1 MTransNo FROM dbo.RFQMst
                                    WHERE DocSeries = @dSeries AND DocYear = @dYear AND DocNo = @dNo
                                      AND SubscID = @SubscID AND DeleteStatus = 'N'
                                      AND DocAmdNo < @dAmdNo
                                    ORDER BY DocAmdNo DESC);

            SET @ID = @MTransNo; SET @Message = 'RFQ deleted successfully.';
        END
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.RFQMst
            SET LockStatus = 'Y', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.RFQMst
            SET LockStatus = 'N', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
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
  SP_PODtl - MaterialGrade grade (new optional @GradeNo, LAST) and pricing on
  the secondary quantity when Part Master's TxnUnit is 'S'.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PODtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PODtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PODtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12,2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(12,4),
    @Qty2           NUMERIC(12,2),
    @Rate           NUMERIC(14,2),
    @MRP            NUMERIC(14,2),
    @DiscP          NUMERIC(6,2),
    @PnFP           NUMERIC(6,2),
    @FreightAmt     NUMERIC(14,2),
    @DeleveryDate   DATE,
    @Grade          VARCHAR(50),
    @MtrlStd        VARCHAR(50),
    @CoreGrade      VARCHAR(50),
    @Spare          VARCHAR(50),
    @SpareGrade     VARCHAR(50),
    @SpareQty       NUMERIC(12,2),
    @OAYear         INT,
    @OANo           INT,
    @OAID           INT,
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @GradeNo        INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @TaxType      VARCHAR(20),
                @Locked       CHAR(1),
                @PartCode     VARCHAR(30),
                @PartDesc     VARCHAR(100),
                @HSNCode      VARCHAR(10),
                @Unit         VARCHAR(20),
                @Unit2        VARCHAR(20),
                @ConvRatio    NUMERIC(12,2),
                @TxnUnit      CHAR(1),
                @GSTP         NUMERIC(6,2),
                @PriceQty     NUMERIC(12,4),
                @BasicValue   NUMERIC(16,2),
                @DiscAmt      NUMERIC(12,2),
                @BasicRate    NUMERIC(14,2),
                @PnFAmt       NUMERIC(14,2),
                @TaxableValue NUMERIC(14,2),
                @IGSTAmt      NUMERIC(14,2),
                @CGSTAmt      NUMERIC(14,2),
                @SGSTAmt      NUMERIC(14,2),
                @TotalValue   NUMERIC(16,2);

        SELECT @TaxType = LTRIM(RTRIM(TaxType)), @Locked = LockStatus
          FROM dbo.POMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @TaxType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Purchase Order not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Locked = 'Y'
        BEGIN
            SET @ID = 0;
            SET @Message = 'Purchase Order is approved. Unlock it, or raise an amendment, before changing lines.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Part snapshot, for Insert and Update.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @PartCode  = LEFT(LTRIM(RTRIM(ISNULL(P.PartCode, ''))), 30),
                   @PartDesc  = LEFT(LTRIM(RTRIM(ISNULL(P.PartName, ''))), 100),
                   @HSNCode   = P.HSNCode,
                   @Unit      = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos'),
                   @Unit2     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.SecondaryUnit, ''))), ''),
                                ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos')),
                   @ConvRatio = ISNULL(NULLIF(P.ConversionRatio, 0), 1),
                   @TxnUnit   = LEFT(ISNULL(P.TxnUnit, 'P'), 1),
                   @GSTP      = ISNULL(P.TaxP, 0)
              FROM dbo.PartMaster P
             WHERE P.MTransNo = @PartNo AND P.DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF ISNULL(@Qty, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
                COMMIT TRANSACTION; RETURN;
            END

            -- OA link: optional, but all three or none (SQL/36).
            SET @Message = dbo.FN_OALinkCheck(@OAYear, @OANo, @OAID, @SubscID);
            IF @Message IS NOT NULL
            BEGIN
                SET @ID = 0;
                COMMIT TRANSACTION; RETURN;
            END

            -- Grade: MaterialGrade of this part. A GradeNo wins and fixes the
            -- name; a bare name (older callers) is matched to its number.
            SET @GradeNo = NULLIF(@GradeNo, 0);
            IF @GradeNo IS NOT NULL
            BEGIN
                SET @Grade = dbo.FN_PartGradeName(@PartNo, @GradeNo, @SubscID);
                IF @Grade IS NULL
                BEGIN
                    SET @ID = 0; SET @Message = 'The selected grade does not belong to this item.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
            ELSE IF NULLIF(LTRIM(RTRIM(ISNULL(@Grade, ''))), '') IS NOT NULL
                SELECT TOP 1 @GradeNo = GradeNo
                  FROM dbo.MaterialGrade
                 WHERE PartNo = @PartNo AND Grade = LTRIM(RTRIM(@Grade))
                   AND SubscID = @SubscID AND DeleteStatus = 'N'
                 ORDER BY MTransNo DESC;

            -- Qty2 is always Qty x ConvRatio, and the rate applies to whichever
            -- quantity Part Master's TxnUnit names.
            SET @Qty2     = ROUND(@Qty * @ConvRatio, 2);
            SET @PriceQty = CASE WHEN @TxnUnit = 'S' THEN @Qty2 ELSE @Qty END;

            EXEC dbo.SP_PODtlPrice
                 @PriceQty, @Rate, @DiscP, @PnFP, @FreightAmt, @GSTP, @TaxType,
                 @BasicValue   OUTPUT, @DiscAmt      OUTPUT, @BasicRate OUTPUT,
                 @PnFAmt       OUTPUT, @TaxableValue OUTPUT, @IGSTAmt   OUTPUT,
                 @CGSTAmt      OUTPUT, @SGSTAmt      OUTPUT, @TotalValue OUTPUT;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.PODtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.PODtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, ItemRemarks, HSNCode,
             Qty, Unit, Rate, MRP, BasicRate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt,
             FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
             Qty2, ConvRatio, TxnUnit, Unit2, OANo, OAID, OAYear, DeleveryDate,
             GradeNo, Grade, CoreGrade, Spare, SpareGrade, SpareQty, MtrlStd,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc, LTRIM(RTRIM(@ItemRemarks)), @HSNCode,
             @Qty, @Unit, @Rate, @MRP, @BasicRate, @BasicValue, ISNULL(@DiscP, 0), @DiscAmt,
             ISNULL(@PnFP, 0), @PnFAmt, ISNULL(@FreightAmt, 0), @TaxableValue, @GSTP,
             @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
             ISNULL(@Qty2, 0), @ConvRatio, @TxnUnit, @Unit2,
             NULLIF(@OANo, 0), NULLIF(@OAID, 0), NULLIF(@OAYear, 0), @DeleveryDate,
             @GradeNo, LTRIM(RTRIM(@Grade)), LTRIM(RTRIM(@CoreGrade)), LTRIM(RTRIM(@Spare)),
             LTRIM(RTRIM(@SpareGrade)), ISNULL(@SpareQty, 0), LTRIM(RTRIM(@MtrlStd)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.PODtl
               SET PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDesc,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   Qty             = @Qty,
                   Unit            = @Unit,
                   Rate            = @Rate,
                   MRP             = @MRP,
                   BasicRate       = @BasicRate,
                   BasicValue      = @BasicValue,
                   DiscP           = ISNULL(@DiscP, 0),
                   DiscAmt         = @DiscAmt,
                   PnFP            = ISNULL(@PnFP, 0),
                   PnFAmt          = @PnFAmt,
                   FreightAmt      = ISNULL(@FreightAmt, 0),
                   TaxableValue    = @TaxableValue,
                   GSTP            = @GSTP,
                   IGSTAmt         = @IGSTAmt,
                   CGSTAmt         = @CGSTAmt,
                   SGSTAmt         = @SGSTAmt,
                   TotalValue      = @TotalValue,
                   Qty2            = ISNULL(@Qty2, 0),
                   ConvRatio       = @ConvRatio,
                   TxnUnit         = @TxnUnit,
                   Unit2           = @Unit2,
                   OANo            = NULLIF(@OANo, 0),
                   OAID            = NULLIF(@OAID, 0),
                   OAYear          = NULLIF(@OAYear, 0),
                   DeleveryDate    = @DeleveryDate,
                   GradeNo         = @GradeNo,
                   Grade           = LTRIM(RTRIM(@Grade)),
                   CoreGrade       = LTRIM(RTRIM(@CoreGrade)),
                   Spare           = LTRIM(RTRIM(@Spare)),
                   SpareGrade      = LTRIM(RTRIM(@SpareGrade)),
                   SpareQty        = ISNULL(@SpareQty, 0),
                   MtrlStd         = LTRIM(RTRIM(@MtrlStd)),
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PODtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PODtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        IF @ID > 0 EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  SP_POGenerateFromQuote - copy the quote line's grade onto the PO line.
  Live definition with only the grade columns added to the cursor.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_POGenerateFromQuote', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POGenerateFromQuote AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POGenerateFromQuote
    @ID             INT             OUTPUT,
    @QuoteTransNo   INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocDate        DATE,
    @DueDate        DATE,
    @PurSubject     VARCHAR(250),
    @BusinessUnit   VARCHAR(50),
    @Plant          VARCHAR(50),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(250),
    @TxnCurrency    VARCHAR(10),
    @CurrencyRate   NUMERIC(6,2),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @VendorNo   INT,
                @QDocYear   INT,
                @QDocNo     INT,
                @QRefNo     VARCHAR(50),
                @QRefDate   DATE,
                @PRTransNo  INT,
                @PRYear     INT,
                @PRNo       INT,
                @SourceTxt  VARCHAR(50),
                @NewID      INT,
                @LineCount  INT = 0;

        SELECT @VendorNo = Q.VendorNo,
               @QDocYear = Q.DocYear,
               @QDocNo   = Q.DocNo,
               @QRefNo   = Q.RefNo,
               @QRefDate = Q.RefDate,
               @PRYear   = Q.PRYear,
               @PRNo     = Q.PRNo
          FROM dbo.RFQPriceMst Q
         WHERE Q.MTransNo = @QuoteTransNo AND Q.SubscID = @SubscID
           AND Q.DeleteStatus = 'N' AND Q.DocAmdStatus = 'N' AND Q.LockStatus = 'Y';

        IF @VendorNo IS NULL
        BEGIN
            SET @ID = 0;
            SET @Message = 'Approved supplier quote not found. Only the current, approved revision can be ordered.';
            COMMIT TRANSACTION; RETURN;
        END

        IF NOT EXISTS (SELECT 1 FROM dbo.RFQPriceDtl
                        WHERE MTransNo = @QuoteTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'The selected quote has no line items.';
            COMMIT TRANSACTION; RETURN;
        END

        IF EXISTS (SELECT 1 FROM dbo.POMst
                    WHERE TargetType = 'PQ' AND TargetTransNo = @QuoteTransNo
                      AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0;
            SET @Message = 'A purchase order already exists against this quote.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @PRNo IS NULL
            SELECT @PRTransNo = R.PRTransNo, @PRYear = P.DocYear, @PRNo = P.DocNo
              FROM dbo.RFQPriceMst Q
              JOIN dbo.RFQMst      R ON R.MTransNo = Q.TargetTransNo
              LEFT JOIN dbo.PurReqMst P ON P.MTransNo = R.PRTransNo
             WHERE Q.MTransNo = @QuoteTransNo;

        SET @SourceTxt = CASE WHEN @PRNo IS NOT NULL
                              THEN LEFT('PR ' + CONVERT(VARCHAR(10), ISNULL(@PRYear, 0)) + '-' + CONVERT(VARCHAR(10), @PRNo), 50)
                              ELSE LEFT('Quote ' + CONVERT(VARCHAR(10), @QDocYear) + '-' + CONVERT(VARCHAR(10), @QDocNo), 50)
                         END;

        -- The supplier's own quotation number and date, unless the caller
        -- typed something else.
        SET @RefNo   = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@RefNo, ''))), ''), @QRefNo);
        SET @RefDate = ISNULL(@RefDate, @QRefDate);

        EXEC dbo.SP_POMst
             @ID            = @NewID OUTPUT,
             @MTransNo      = 0,
             @MCount        = 0,
             @DocSeries     = @DocSeries,
             @DocYear       = @DocYear,
             @DocNo         = 0,
             @DocDate       = @DocDate,
             @DocAmdNo      = 0,
             @TargetTransNo = @QuoteTransNo,
             @TargetType    = 'PQ',
             @VendorNo      = @VendorNo,
             @VendorNoShip  = 0,
             @RefNo         = @RefNo,
             @RefDate       = @RefDate,
             @DueDate       = @DueDate,
             @DocNotes      = @DocNotes,
             @DocAmdNotes   = NULL,
             @PurSubject    = @PurSubject,
             @BusinessUnit  = @BusinessUnit,
             @Plant         = @Plant,
             @Source        = @SourceTxt,
             @OtherCharges  = NULL,
             @OtherAmt      = 0,
             @RoundOff      = 0,
             @TxnCurrency   = @TxnCurrency,
             @CurrencyRate  = @CurrencyRate,
             @SystemIP      = @SystemIP,
             @CreatedBy     = @CreatedBy,
             @CreatedOn     = NULL,
             @ModifiedBy    = NULL,
             @ModifiedOn    = NULL,
             @DeleteReason  = NULL,
             @LockStatus    = 'N',
             @SubscID       = @SubscID,
             @Status        = 'Insert',
             @Message       = @Message OUTPUT;

        IF ISNULL(@NewID, 0) = 0
        BEGIN
            SET @ID = 0;
            SET @Message = ISNULL(@Message, 'Could not create the purchase order header.');
            COMMIT TRANSACTION; RETURN;
        END

        DECLARE @qPartNo  INT,
                @qQty     NUMERIC(12,4),
                @qRate    NUMERIC(14,2),
                @qRemarks VARCHAR(500),
                @qGradeNo INT,
                @qGrade   VARCHAR(50),
                @lineID   INT,
                @lineMsg  NVARCHAR(200);

        DECLARE QuoteLines CURSOR LOCAL FAST_FORWARD FOR
            SELECT PartNo, Qty, Rate, ItemRemarks, GradeNo, Grade
              FROM dbo.RFQPriceDtl
             WHERE MTransNo = @QuoteTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
             ORDER BY SLNo, TransSlNo;

        OPEN QuoteLines;
        FETCH NEXT FROM QuoteLines INTO @qPartNo, @qQty, @qRate, @qRemarks, @qGradeNo, @qGrade;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC dbo.SP_PODtl
                 @ID           = @lineID OUTPUT,
                 @TransSlNo    = 0,
                 @MTransNo     = @NewID,
                 @SlNo         = 0,
                 @PartNo       = @qPartNo,
                 @ItemRemarks  = @qRemarks,
                 @Qty          = @qQty,
                 @Qty2         = 0,
                 @Rate         = @qRate,
                 @MRP          = NULL,
                 @DiscP        = 0,
                 @PnFP         = 0,
                 @FreightAmt   = 0,
                 @DeleveryDate = @DueDate,
                 @Grade        = @qGrade,
                 @MtrlStd      = NULL,
                 @CoreGrade    = NULL,
                 @Spare        = NULL,
                 @SpareGrade   = NULL,
                 @SpareQty     = 0,
                 @OAYear       = 0,
                 @OANo         = 0,
                 @OAID         = 0,
                 @SystemIP     = @SystemIP,
                 @CreatedBy    = @CreatedBy,
                 @CreatedOn    = NULL,
                 @ModifiedBy   = NULL,
                 @ModifiedOn   = NULL,
                 @DeleteReason = NULL,
                 @SubscID      = @SubscID,
                 @Status       = 'Insert',
                 @Message      = @lineMsg OUTPUT,
                 @GradeNo      = @qGradeNo;

            IF ISNULL(@lineID, 0) > 0 SET @LineCount = @LineCount + 1;

            FETCH NEXT FROM QuoteLines INTO @qPartNo, @qQty, @qRate, @qRemarks, @qGradeNo, @qGrade;
        END

        CLOSE QuoteLines;
        DEALLOCATE QuoteLines;

        IF @LineCount = 0
        BEGIN
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            SET @ID = 0;
            SET @Message = 'No line item on the quote could be ordered. Check the parts still exist in the part master.';
            RETURN;
        END

        INSERT INTO dbo.POTerms
        (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
         DeleteStatus, LockStatus, LockCounter, SubscID)
        SELECT @NewID, SlNo, Title, LEFT(Description, 250), @SystemIP, @CreatedBy, GETDATE(),
               'N', 'N', 1, @SubscID
          FROM dbo.RFQPriceTerms
         WHERE MTransNo = @QuoteTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
         ORDER BY SlNo;

        EXEC dbo.SP_POMstRecalc @NewID, @SubscID;

        SET @ID = @NewID;
        SET @Message = 'Purchase Order generated from quote ' +
                       CONVERT(VARCHAR(10), @QDocYear) + '-' + CONVERT(VARCHAR(10), @QDocNo) +
                       ' with ' + CONVERT(VARCHAR(10), @LineCount) + ' line(s).';

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local', 'QuoteLines') >= 0
        BEGIN
            CLOSE QuoteLines;
            DEALLOCATE QuoteLines;
        END
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  VW_PurReqItemsForPO - adds GradeNo / Grade / TxnUnit
------------------------------------------------------------------------------*/
CREATE OR ALTER VIEW dbo.VW_PurReqItemsForPO
AS
SELECT  PR.MTransNo   AS PRTransNo,
        PR.DocSeries  AS PRDocSeries,
        PR.DocYear    AS PRDocYear,
        PR.DocNo      AS PRDocNo,
        PR.DocDate    AS PRDocDate,
        PR.DueDate,
        PR.SubscID,
        PD.TransSlNo  AS PRTransSlNo,
        PD.SlNo       AS PRSlNo,
        PD.PartNo,
        PM.PartCode,
        PM.PartName   AS PartDescription,
        PM.HSNCode,
        PM.TaxP       AS GSTP,
        PM.PrimaryUnit,
        PM.SecondaryUnit,
        PM.ConversionRatio,
        PM.TxnUnit,
        PD.GradeNo,
        PD.Grade,
        PD.ItemRemarks,
        PD.Qty        AS ReqQty,
        ISNULL(PD.Unit, PM.PrimaryUnit) AS Unit,
        ISNULL(O.OrderedQty, 0)         AS OrderedQty,
        PD.Qty - ISNULL(O.OrderedQty, 0) AS BalanceQty
  FROM  dbo.PurReqDtl PD
  JOIN  dbo.PurReqMst PR ON PR.MTransNo = PD.MTransNo
  LEFT JOIN dbo.PartMaster PM ON PM.MTransNo = PD.PartNo
 OUTER APPLY
       (
           SELECT SUM(OD.Qty) AS OrderedQty
             FROM dbo.PODtl OD
             JOIN dbo.POMst OM ON OM.MTransNo = OD.MTransNo
             LEFT JOIN dbo.RFQPriceMst QQ ON QQ.MTransNo = OM.TargetTransNo
             LEFT JOIN dbo.RFQMst      RR ON RR.MTransNo = QQ.TargetTransNo
            WHERE OD.DeleteStatus = 'N'
              AND OM.DeleteStatus = 'N'
              AND OM.DocAmdStatus = 'N'
              AND OD.PartNo       = PD.PartNo
              AND (
                    (OM.TargetType = 'PR' AND OM.TargetTransNo = PR.MTransNo)
                 OR (OM.TargetType = 'PQ' AND RR.PRTransNo     = PR.MTransNo)
                  )
       ) O
 WHERE  PD.DeleteStatus = 'N'
   AND  PR.DeleteStatus = 'N'
   AND  PR.DocAmdStatus = 'N';
GO

PRINT '40_PurchaseGradeAndFixes applied.';
GO
