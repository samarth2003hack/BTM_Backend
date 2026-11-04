/*==============================================================================
  36. OA LINK ON PURCHASE LINES  -  OA Year / OA No / OA ID

  A purchase requisition or purchase order line may name the Order Acceptance
  line it is bought for:
      OAYear = OAMst.DocYear,  OANo = OAMst.DocNo,  OAID = OADtl.SlNo
  taken only from the live, current revision of the OA
  (OAMst.DeleteStatus = 'N' AND OAMst.DocAmdStatus = 'N', OADtl.DeleteStatus = 'N').

  The link is OPTIONAL, but it is all three or none: leave all three blank, or
  fill all three. dbo.FN_OALinkCheck holds that rule once, and both line
  procedures call it on Insert / Update:
      SP_PurReqDtl   - PurReqDtl gains OAYear / OANo / OAID (it had none, so the
                       OA picked on the requisition screen was being dropped).
                       The three parameters are new, optional and LAST, so an
                       older caller that does not send them still works.
      SP_PODtl       - already stored them; now it also checks them. Body is
                       the live definition with only the check added.

  0 and NULL both mean "blank".

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
IF COL_LENGTH('dbo.PurReqDtl', 'OAYear') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OAYear INT NULL;
IF COL_LENGTH('dbo.PurReqDtl', 'OANo') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OANo INT NULL;
IF COL_LENGTH('dbo.PurReqDtl', 'OAID') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OAID INT NULL;
GO

/*------------------------------------------------------------------------------
  FN_OALinkCheck - NULL when the link is acceptable, otherwise the message.
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_OALinkCheck
(
    @OAYear     INT,
    @OANo       INT,
    @OAID       INT,
    @SubscID    INT
)
RETURNS NVARCHAR(200)
AS
BEGIN
    DECLARE @Y INT = NULLIF(@OAYear, 0),
            @N INT = NULLIF(@OANo, 0),
            @L INT = NULLIF(@OAID, 0);

    IF @Y IS NULL AND @N IS NULL AND @L IS NULL
        RETURN NULL;

    IF @Y IS NULL OR @N IS NULL OR @L IS NULL
        RETURN N'OA Year, OA No and OA ID go together - select all three, or leave all three blank.';

    IF NOT EXISTS (SELECT 1
                     FROM dbo.OAMst M
                    WHERE M.DocYear      = @Y
                      AND M.DocNo        = @N
                      AND M.SubscID      = @SubscID
                      AND M.DeleteStatus = 'N'
                      AND M.DocAmdStatus = 'N')
        RETURN N'OA ' + CAST(@Y AS NVARCHAR(10)) + N' / ' + CAST(@N AS NVARCHAR(10))
             + N' was not found, or is not the current revision.';

    IF NOT EXISTS (SELECT 1
                     FROM dbo.OAMst M
                     JOIN dbo.OADtl D ON D.MTransNo = M.MTransNo
                    WHERE M.DocYear      = @Y
                      AND M.DocNo        = @N
                      AND M.SubscID      = @SubscID
                      AND M.DeleteStatus = 'N'
                      AND M.DocAmdStatus = 'N'
                      AND D.DeleteStatus = 'N'
                      AND D.SlNo         = @L)
        RETURN N'OA ID ' + CAST(@L AS NVARCHAR(10)) + N' is not a line of OA '
             + CAST(@Y AS NVARCHAR(10)) + N' / ' + CAST(@N AS NVARCHAR(10)) + N'.';

    RETURN NULL;
END
GO

/*------------------------------------------------------------------------------
  SP_PurReqDtl
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
    @OAID           INT = NULL
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
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.PurReqDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.PurReqDtl
            (SlNo, MTransNo, PartNo, ItemRemarks, Qty, Unit, OAYear, OANo, OAID, SystemIP,
             CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, LTRIM(RTRIM(@ItemRemarks)), @Qty,
             LTRIM(RTRIM(@Unit)), NULLIF(@OAYear, 0), NULLIF(@OANo, 0), NULLIF(@OAID, 0),
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
  SP_PODtl - live definition, plus the FN_OALinkCheck call after the quantity
  check (Insert / Update only).
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
    @Message        NVARCHAR(200)   OUTPUT
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
                @GradeNo      INT,
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

            SELECT @GradeNo = MTransNo
              FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

            EXEC dbo.SP_PODtlPrice
                 @Qty, @Rate, @DiscP, @PnFP, @FreightAmt, @GSTP, @TaxType,
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
