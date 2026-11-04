/*==============================================================================
  29. GRN PAGE  -  what the live Goods Receipt Note screen needs

  GRN.html has moved off its in-page mock data and onto GRNController
  (/api/GRN/...). Tables, views and procedures come from 13_ / 15_. The screen
  adds three endpoints in GRNController (no schema change):
      GET  Mst/GetNextDocNo/{subscID}   DocNo preview (SP_GRNMst allocates)
      GET  Mst/GetRegister/{subscID}    list incl. TxnCurrency / CurrencyRate
      POST Mst/SaveFull                 header + lines + drops + Lock, in ONE
                                        transaction (a refused line rolls the
                                        whole receipt back)

  The only database change:

    SP_GRNDtl - on a PO-backed line the GST% now comes from the PO line
                (PODtl.GSTP), like the rate, discount and packing already do,
                instead of the part master's current TaxP. A receipt is valued
                on the terms the order was placed on; the screen shows the PO
                line's GST% and checks Bill Value against that total. Direct
                lines (POTransSlNo 0) still use PartMaster.TaxP. Parameters,
                statuses, messages and the GRNType stage rule are unchanged.

  SP_* names are shadowed by master, so CREATE OR ALTER fails: create a stub
  if missing, then ALTER.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.SP_GRNDtl', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_GRNDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @POTransSlNo    INT,
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @BillQty        NUMERIC(12,4),
    @RecQty         NUMERIC(12,4),
    @Weight         NUMERIC(12,4),
    @Rate           NUMERIC(14,2),
    @DiscP          NUMERIC(6,2),
    @PnFP           NUMERIC(6,2),
    @FreightAmt     NUMERIC(14,2),
    @Grade          VARCHAR(50),
    @Store          VARCHAR(50),
    @Location       VARCHAR(50),
    @BarCode        VARCHAR(30),
    @ChassisNo      VARCHAR(30),
    @EngineNo       VARCHAR(30),
    @RegdNo         VARCHAR(30),
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
                @GRNType      CHAR(1),
                @GDocNo       INT,
                @GDocDate     DATE,
                @GVendorNo    INT,
                @GTargetType  VARCHAR(10),
                @GTargetNo    INT,
                @SlNo         NUMERIC(12,2),
                @POQty        NUMERIC(12,2),
                @Balance      NUMERIC(14,4),
                @AlreadyOnMe  NUMERIC(14,4),
                @PartCode     VARCHAR(30),
                @PartDesc     VARCHAR(100),
                @HSNCode      VARCHAR(10),
                @Unit         VARCHAR(20),
                @Unit2        VARCHAR(30),
                @ConvRatio    NUMERIC(14,4),
                @GSTP         NUMERIC(6,2),
                @GradeNo      INT,
                @OAYear       INT,
                @OANo         INT,
                @OAID         INT,
                @BasicValue   NUMERIC(16,2),
                @DiscAmt      NUMERIC(12,2),
                @BasicRate    NUMERIC(14,2),
                @PnFAmt       NUMERIC(14,2),
                @TaxableValue NUMERIC(14,2),
                @IGSTAmt      NUMERIC(14,2),
                @CGSTAmt      NUMERIC(14,2),
                @SGSTAmt      NUMERIC(14,2),
                @TotalValue   NUMERIC(16,2);

        SELECT @TaxType     = LTRIM(RTRIM(TaxType)),
               @GRNType     = GRNType,
               @GDocNo      = DocNo,
               @GDocDate    = DocDate,
               @GVendorNo   = VendorNo,
               @GTargetType = TargetType,
               @GTargetNo   = TargetTransNo
          FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @TaxType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @GRNType IN ('Q', 'C')
        BEGIN
            SET @ID = 0;
            SET @Message = 'This GRN has been inspected. Reopen the inspection before changing lines.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update: resolve the part, the PO line and the price, then
          check the quantity against what the order still has outstanding.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF ISNULL(@BillQty, 0) <= 0 AND ISNULL(@RecQty, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Enter a billed or a received quantity.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @BillQty = ISNULL(@BillQty, 0);
            SET @RecQty  = ISNULL(NULLIF(@RecQty, 0), @BillQty);

            SELECT @PartCode  = LEFT(LTRIM(RTRIM(ISNULL(P.PartCode, ''))), 30),
                   @PartDesc  = LEFT(LTRIM(RTRIM(ISNULL(P.PartName, ''))), 100),
                   @HSNCode   = P.HSNCode,
                   @Unit      = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos'),
                   @Unit2     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.SecondaryUnit, ''))), ''),
                                ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos')),
                   @ConvRatio = ISNULL(NULLIF(P.ConversionRatio, 0), 1),
                   @GSTP      = ISNULL(P.TaxP, 0)
              FROM dbo.PartMaster P
             WHERE P.MTransNo = @PartNo AND P.DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @GTargetType = 'PO' AND ISNULL(@POTransSlNo, 0) > 0
            BEGIN
                SELECT @SlNo      = PD.SlNo,
                       @POQty     = PD.Qty,
                       @Rate      = PD.Rate,
                       @DiscP     = PD.DiscP,
                       @PnFP      = PD.PnFP,
                       @Grade     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@Grade, ''))), ''), PD.Grade),
                       -- GST as ordered (29_GRNPage.sql), falling back to the part master.
                       @GSTP      = ISNULL(PD.GSTP, @GSTP),
                       @OAYear    = PD.OAYear,
                       @OANo      = PD.OANo,
                       @OAID      = PD.OAID
                  FROM dbo.PODtl PD
                 WHERE PD.TransSlNo = @POTransSlNo AND PD.MTransNo = @GTargetNo
                   AND PD.SubscID = @SubscID AND PD.DeleteStatus = 'N'
                   AND PD.PartNo = @PartNo;

                IF @SlNo IS NULL
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'That purchase order line was not found, or it is for a different part.';
                    COMMIT TRANSACTION; RETURN;
                END

                /*  Outstanding on the PO line = ordered, minus everything
                    already billed on it, EXCLUDING this line when it is being
                    edited rather than added.  */
                SELECT @AlreadyOnMe = ISNULL(SUM(GD.BillQty), 0)
                  FROM dbo.GRNDtl GD
                  JOIN dbo.GRNMst GM ON GM.MTransNo = GD.MTransNo
                 WHERE GD.DeleteStatus = 'N' AND GM.DeleteStatus = 'N'
                   AND GM.TargetType = 'PO' AND GM.TargetTransNo = @GTargetNo
                   AND GD.SlNo = @SlNo
                   AND (@Status = 'Insert' OR GD.TransSlNo <> @TransSlNo);

                SET @Balance = @POQty - @AlreadyOnMe;

                IF @BillQty > @Balance
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Billed quantity ' + CONVERT(VARCHAR(20), @BillQty) +
                                   ' exceeds the balance of ' + CONVERT(VARCHAR(20), @Balance) +
                                   ' on purchase order line ' + CONVERT(VARCHAR(20), @SlNo) + '.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
            ELSE IF @Status = 'Insert'
            BEGIN
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                  FROM dbo.GRNDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
                SET @POQty = 0;
            END
            ELSE
            BEGIN
                SELECT @SlNo = SlNo, @POQty = POQty
                  FROM dbo.GRNDtl WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo;
            END

            SELECT @GradeNo = MTransNo
              FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

            /*  Value follows the BILLED quantity - that is what the supplier
                is owed for.  What was physically received, and what survives
                inspection, are tracked separately and settle later.  */
            EXEC dbo.SP_PODtlPrice
                 @BillQty, @Rate, @DiscP, @PnFP, @FreightAmt, @GSTP, @TaxType,
                 @BasicValue   OUTPUT, @DiscAmt      OUTPUT, @BasicRate OUTPUT,
                 @PnFAmt       OUTPUT, @TaxableValue OUTPUT, @IGSTAmt   OUTPUT,
                 @CGSTAmt      OUTPUT, @SGSTAmt      OUTPUT, @TotalValue OUTPUT;
        END

        IF (@Status = 'Insert')
        BEGIN
            INSERT INTO dbo.GRNDtl
            (DocNo, VendorNo, DocDate, SlNo, MTransNo, PartNo, PartCode, PartDescription,
             ItemRemarks, HSNCode, BillQty, RecQty, AccQty, RejQty, RewQty, AODQty,
             Weight, Unit, Unit2, ConvQty, RateType, Rate, BasicValue, DiscP, DiscAmt,
             PnFP, PnFAmt, FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt,
             TotalValue, BarCode, ScanStatus, POQty, ChassisNo, EngineNo, RegdNo,
             Store, Location, OAYear, OANo, OAID, GradeNo, Grade,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@GDocNo, @GVendorNo, @GDocDate, @SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc,
             LTRIM(RTRIM(@ItemRemarks)), @HSNCode, @BillQty, @RecQty, 0, 0, 0, 0,
             ISNULL(@Weight, 0), @Unit, @Unit2, ROUND(@RecQty * @ConvRatio, 4), 'Basic',
             @Rate, @BasicValue, ISNULL(@DiscP, 0), @DiscAmt,
             ISNULL(@PnFP, 0), @PnFAmt, ISNULL(@FreightAmt, 0), @TaxableValue, @GSTP,
             @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
             LTRIM(RTRIM(@BarCode)), 'N', ISNULL(@POQty, 0),
             LTRIM(RTRIM(@ChassisNo)), LTRIM(RTRIM(@EngineNo)), LTRIM(RTRIM(@RegdNo)),
             NULLIF(LTRIM(RTRIM(ISNULL(@Store, ''))), ''),
             NULLIF(LTRIM(RTRIM(ISNULL(@Location, ''))), ''),
             @OAYear, @OANo, @OAID, @GradeNo, LTRIM(RTRIM(@Grade)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Receipt line saved successfully.';
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNDtl
               SET PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDesc,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   BillQty         = @BillQty,
                   RecQty          = @RecQty,
                   Weight          = ISNULL(@Weight, 0),
                   Unit            = @Unit,
                   Unit2           = @Unit2,
                   ConvQty         = ROUND(@RecQty * @ConvRatio, 4),
                   Rate            = @Rate,
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
                   BarCode         = LTRIM(RTRIM(@BarCode)),
                   POQty           = ISNULL(@POQty, 0),
                   ChassisNo       = LTRIM(RTRIM(@ChassisNo)),
                   EngineNo        = LTRIM(RTRIM(@EngineNo)),
                   RegdNo          = LTRIM(RTRIM(@RegdNo)),
                   GradeNo         = @GradeNo,
                   Grade           = LTRIM(RTRIM(@Grade)),
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Receipt line updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.GRNDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                -- The NC lines hanging off this receipt line go with it.
                UPDATE dbo.GRNNCDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE GRNTransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @TransSlNo; SET @Message = 'Receipt line deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.GRNDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.GRNNCDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'All receipt lines deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active receipt lines found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        IF @ID > 0 EXEC dbo.SP_GRNMstRecalc @MTransNo, @SubscID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
