/*==============================================================================
  42. PURCHASE GST NOT CALCULATED - TAX TYPE TAKEN FROM THE WRONG COLUMN

  Symptom: on a Purchase Order (and a direct GRN) every line saved with
  IGST = CGST = SGST = 0, so the order's GST Value and Net Value were short.

  Cause: SupplierMaster has two columns that were confused.
      SupplierType  Inter State / Intra State / Export          <- GST split
      TaxType       REGULAR / URD / COMPOSITE / SEZ / EXPORT    (registration)
  SP_POMst and SP_GRNMst copied SupplierMaster.TaxType ('REGULAR') onto the
  document. SP_PODtlPrice (and the re-price on Update) only computes GST when
  the document's TaxType is 'Intra State' or 'Inter State', so everything
  else priced at zero GST. The state-code fallback never ran, because the
  value was not NULL.

  Fix:
    FN_PurchaseTaxType  resolves the document tax type for a supplier:
                          1. SupplierType when it is Inter / Intra State / Export
                          2. a legacy TaxType holding one of those (or EXPORT)
                          3. otherwise supplier state vs company state
                        It never returns NULL.
    SP_POMst, SP_GRNMst take the vendor tax type from FN_PurchaseTaxType on
                        Insert / Update / Amend. Update already re-prices every
                        line and re-totals the header, so Save and Update now
                        store the GST amounts. (Their "IF @TaxType IS NULL"
                        fallback is now unreachable but left as it was.)
    BACKFILL            live POs / GRNs whose stored TaxType is not one of
                        Inter State / Intra State / Export get the resolved
                        tax type, their lines re-priced and headers re-totalled.

  'Export' keeps GST at zero, as before.
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
  FN_PurchaseTaxType - 'Inter State' / 'Intra State' / 'Export' for a supplier.
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_PurchaseTaxType
(
    @SupplierType   VARCHAR(20),
    @TaxType        VARCHAR(20),
    @VendStateCode  INT,
    @SubscID        INT
)
RETURNS VARCHAR(20)
AS
BEGIN
    DECLARE @Sup VARCHAR(20) = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(@SupplierType, ''))), ' ', ''), '-', '')),
            @Tax VARCHAR(20) = UPPER(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(@TaxType, ''))), ' ', ''), '-', '')),
            @CompStateCode INT;

    IF @Sup = 'INTERSTATE' RETURN 'Inter State';
    IF @Sup = 'INTRASTATE' RETURN 'Intra State';
    IF @Sup = 'EXPORT'     RETURN 'Export';

    -- Suppliers saved before SupplierType existed may carry it in TaxType.
    IF @Tax = 'INTERSTATE' RETURN 'Inter State';
    IF @Tax = 'INTRASTATE' RETURN 'Intra State';
    IF @Tax = 'EXPORT'     RETURN 'Export';

    SELECT TOP 1 @CompStateCode = C.StateCode
      FROM dbo.Company C
     WHERE C.SubscID = @SubscID AND C.DeleteStatus = 'N'
     ORDER BY C.MTransNo;

    RETURN CASE WHEN @VendStateCode IS NOT NULL AND @CompStateCode IS NOT NULL
                 AND @VendStateCode <> @CompStateCode
                THEN 'Inter State' ELSE 'Intra State' END;
END
GO

/*------------------------------------------------------------------------------
  SP_POMst - vendor tax type from FN_PurchaseTaxType
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_POMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @DocAmdNo       SMALLINT,
    @TargetTransNo  INT,
    @TargetType     VARCHAR(30),
    @VendorNo       INT,
    @VendorNoShip   INT,
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DueDate        DATE,
    @DocNotes       VARCHAR(250),
    @DocAmdNotes    VARCHAR(250),
    @PurSubject     VARCHAR(250),
    @BusinessUnit   VARCHAR(50),
    @Plant          VARCHAR(50),
    @Source         VARCHAR(50),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16,2),
    @RoundOff       NUMERIC(6,4),
    @TxnCurrency    VARCHAR(10),
    @CurrencyRate   NUMERIC(6,2),
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

        DECLARE @VendorCode    VARCHAR(20),
                @VendorName    VARCHAR(100),
                @Address       VARCHAR(500),
                @GSTIN         VARCHAR(20),
                @StateName     VARCHAR(50),
                @PhoneNo       VARCHAR(30),
                @EmailID       VARCHAR(80),
                @TaxType       VARCHAR(20),
                @VendStateCode INT,
                @CompStateCode INT,
                @SupCurrency   VARCHAR(15),
                @TargetDocYear INT,
                @TargetDocNo   INT,
                @TargetAmdNo   INT;

        /*------------------------------------------------------------------
          Vendor snapshot.  Needed by Insert, Update and Amend alike.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update', 'Amend')
        BEGIN
            SELECT @VendorCode    = LTRIM(RTRIM(ISNULL(S.SupplierCode, ''))),
                   @VendorName    = LTRIM(RTRIM(ISNULL(S.SupplierName, ''))),
                   @Address       = LTRIM(RTRIM(ISNULL(S.SupplierAddress, ''))),
                   @GSTIN         = LTRIM(RTRIM(ISNULL(S.GSTIN, ''))),
                   @StateName     = S.StateName,
                   @PhoneNo       = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(S.PhoneNo, ''))), ''), S.MobileNo),
                   @EmailID       = S.EmailID,
                   @TaxType       = dbo.FN_PurchaseTaxType(S.SupplierType, S.TaxType, S.StateCode, @SubscID),
                   @VendStateCode = S.StateCode,
                   @SupCurrency   = S.TxnCurrency
              FROM dbo.SupplierMaster S
             WHERE S.MTransNo = @VendorNo AND S.DeleteStatus = 'N';

            IF @VendorName IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Supplier not found.';
                COMMIT TRANSACTION; RETURN;
            END

            -- No tax type on the supplier: derive it by comparing states.
            IF @TaxType IS NULL
            BEGIN
                SELECT TOP 1 @CompStateCode = C.StateCode
                  FROM dbo.Company C
                 WHERE C.SubscID = @SubscID AND C.DeleteStatus = 'N'
                 ORDER BY C.MTransNo;

                SET @TaxType = CASE
                        WHEN @VendStateCode IS NULL OR @CompStateCode IS NULL THEN 'Intra State'
                        WHEN @VendStateCode <> @CompStateCode                 THEN 'Inter State'
                        ELSE 'Intra State' END;
            END

            SET @TxnCurrency = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@TxnCurrency, ''))), ''), @SupCurrency);

            /*--------------------------------------------------------------
              Resolve the source document's own number, so the order can be
              printed with "against PR 2627-14" on it without a second read.
            --------------------------------------------------------------*/
            IF @TargetTransNo > 0 AND @TargetType = 'PQ'
                SELECT @TargetDocYear = Q.DocYear, @TargetDocNo = Q.DocNo, @TargetAmdNo = Q.DocAmdNo
                  FROM dbo.RFQPriceMst Q
                 WHERE Q.MTransNo = @TargetTransNo AND Q.DeleteStatus = 'N';
            ELSE IF @TargetTransNo > 0 AND @TargetType = 'PR'
                SELECT @TargetDocYear = R.DocYear, @TargetDocNo = R.DocNo, @TargetAmdNo = R.DocAmdNo
                  FROM dbo.PurReqMst R
                 WHERE R.MTransNo = @TargetTransNo AND R.DeleteStatus = 'N';
        END

        /*==================================================================
          INSERT
        ==================================================================*/
        IF (@Status = 'Insert')
        BEGIN
            IF (@DocAmdNo = 0)
            BEGIN
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                  FROM dbo.POMst
                 WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;
            END
            ELSE IF (@DocAmdNo > 0)
            BEGIN
                UPDATE dbo.POMst SET DocAmdStatus = 'Y'
                 WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
                   AND SubscID = @SubscID AND DocAmdNo < @DocAmdNo AND DeleteStatus = 'N';
            END

            SELECT @MCount = COUNT(MTransNo) FROM dbo.POMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Purchase Order already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.POMst
                (DocSeries, DocYear, DocNo, DocDate, DocAmdNo, DocAmdStatus,
                 TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
                 VendorNo, VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
                 RefNo, RefDate, DueDate, DocNotes, DocAmdNotes, DocStatus, TaxType,
                 BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
                 IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt, RoundOff, NetValue,
                 TxnCurrency, CurrencyRate, CurrencyAmount,
                 PurSubject, VendorNoShip, BusinessUnit, Plant, Source,
                 SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
                 LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @DocAmdNo, 'N',
                 NULLIF(@TargetTransNo, 0), @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                 LTRIM(RTRIM(@TargetType)),
                 @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN, @StateName, @PhoneNo, @EmailID,
                 LTRIM(RTRIM(@RefNo)), @RefDate, @DueDate,
                 LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@DocAmdNotes)), 'N', @TaxType,
                 0, 0, 0, 0, 0, 0, 0, 0,
                 LTRIM(RTRIM(@OtherCharges)), ISNULL(@OtherAmt, 0), ISNULL(@RoundOff, 0), 0,
                 @TxnCurrency, @CurrencyRate, 0,
                 LTRIM(RTRIM(@PurSubject)), NULLIF(@VendorNoShip, 0),
                 LTRIM(RTRIM(@BusinessUnit)), LTRIM(RTRIM(@Plant)), LTRIM(RTRIM(@Source)),
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL,
                 ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                EXEC dbo.SP_POMstRecalc @ID, @SubscID;
                SET @Message = 'Purchase Order saved successfully.';
            END
        END

        /*==================================================================
          UPDATE
        ==================================================================*/
        ELSE IF (@Status = 'Update')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.POMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                          AND DeleteStatus = 'N' AND LockStatus = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Purchase Order is approved. Unlock it, or raise an amendment, before editing.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @MCount = COUNT(MTransNo) FROM dbo.POMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N'
               AND SubscID = @SubscID AND MTransNo <> @MTransNo;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Purchase Order already exists.'; END
            ELSE
            BEGIN
                UPDATE dbo.POMst
                   SET DocDate       = @DocDate,
                       TargetTransNo = NULLIF(@TargetTransNo, 0),
                       TargetDocYear = @TargetDocYear,
                       TargetDocNo   = @TargetDocNo,
                       TargetAmdNo   = @TargetAmdNo,
                       TargetType    = LTRIM(RTRIM(@TargetType)),
                       VendorNo      = @VendorNo,
                       VendorCode    = @VendorCode,
                       VendorName    = @VendorName,
                       Address       = @Address,
                       GSTIN         = @GSTIN,
                       StateName     = @StateName,
                       PhoneNo       = @PhoneNo,
                       EmailID       = @EmailID,
                       RefNo         = LTRIM(RTRIM(@RefNo)),
                       RefDate       = @RefDate,
                       DueDate       = @DueDate,
                       DocNotes      = LTRIM(RTRIM(@DocNotes)),
                       DocAmdNotes   = LTRIM(RTRIM(@DocAmdNotes)),
                       TaxType       = @TaxType,
                       OtherCharges  = LTRIM(RTRIM(@OtherCharges)),
                       OtherAmt      = ISNULL(@OtherAmt, 0),
                       RoundOff      = ISNULL(@RoundOff, 0),
                       TxnCurrency   = @TxnCurrency,
                       CurrencyRate  = @CurrencyRate,
                       PurSubject    = LTRIM(RTRIM(@PurSubject)),
                       VendorNoShip  = NULLIF(@VendorNoShip, 0),
                       BusinessUnit  = LTRIM(RTRIM(@BusinessUnit)),
                       Plant         = LTRIM(RTRIM(@Plant)),
                       Source        = LTRIM(RTRIM(@Source)),
                       SystemIP      = @SystemIP,
                       ModifiedBy    = @ModifiedBy,
                       ModifiedOn    = ISNULL(@ModifiedOn, GETDATE()),
                       LockStatus    = ISNULL(@LockStatus, LockStatus),
                       LockCounter   = ISNULL(LockCounter, 0) + 1
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN
                    /*  The tax type may have moved between intra and inter
                        state, which reshuffles CGST/SGST into IGST on every
                        line.  Re-price them before adding the order up.  */
                    UPDATE D
                       SET IGSTAmt = CASE WHEN @TaxType = 'Inter State'
                                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END,
                           CGSTAmt = CASE WHEN @TaxType = 'Intra State'
                                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END,
                           SGSTAmt = CASE WHEN @TaxType = 'Intra State'
                                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END,
                           TotalValue = ISNULL(D.TaxableValue, 0)
                                      + CASE WHEN @TaxType = 'Inter State'
                                             THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END
                                      + CASE WHEN @TaxType = 'Intra State'
                                             THEN 2 * ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END
                      FROM dbo.PODtl D
                     WHERE D.MTransNo = @MTransNo AND D.DeleteStatus = 'N';

                    EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;
                    SET @ID = @MTransNo; SET @Message = 'Purchase Order updated successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        /*==================================================================
          DELETE  -  header and every child, in one soft sweep
        ==================================================================*/
        ELSE IF (@Status = 'Delete')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.GRNMst
                        WHERE TargetType = 'PO' AND TargetTransNo = @MTransNo
                          AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Goods have been received against this order. It cannot be deleted.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.POMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.PODtl    SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.POTerms  SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.POExtras SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                        ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                /*  Deleting a revision hands the "live" flag back to the
                    newest surviving one, or the order would vanish from every
                    list that filters on DocAmdStatus = 'N'.  */
                DECLARE @DelSeries CHAR(10), @DelYear INT, @DelNo INT;
                SELECT @DelSeries = DocSeries, @DelYear = DocYear, @DelNo = DocNo
                  FROM dbo.POMst WHERE MTransNo = @MTransNo;

                IF NOT EXISTS (SELECT 1 FROM dbo.POMst
                                WHERE DocSeries = @DelSeries AND DocYear = @DelYear AND DocNo = @DelNo
                                  AND SubscID = @SubscID AND DeleteStatus = 'N' AND DocAmdStatus = 'N')
                    UPDATE dbo.POMst
                       SET DocAmdStatus = 'N'
                     WHERE MTransNo = (SELECT TOP 1 MTransNo FROM dbo.POMst
                                        WHERE DocSeries = @DelSeries AND DocYear = @DelYear AND DocNo = @DelNo
                                          AND SubscID = @SubscID AND DeleteStatus = 'N'
                                        ORDER BY DocAmdNo DESC);

                SET @ID = @MTransNo; SET @Message = 'Purchase Order deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        /*==================================================================
          LOCK  -  approve.  An order with no lines is not an order.
        ==================================================================*/
        ELSE IF (@Status = 'Lock')
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.PODtl
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Add at least one line before approving.';
                COMMIT TRANSACTION; RETURN;
            END

            EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;

            UPDATE dbo.POMst
               SET LockStatus = 'Y', DocStatus = 'A',
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Order approved successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        /*==================================================================
          UNLOCK  -  back to draft, unless goods have already arrived
        ==================================================================*/
        ELSE IF (@Status = 'Unlock')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.GRNMst
                        WHERE TargetType = 'PO' AND TargetTransNo = @MTransNo
                          AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Goods have been received against this order. Raise an amendment instead of unlocking.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.POMst
               SET LockStatus = 'N', DocStatus = 'N',
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Order unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        /*==================================================================
          AMEND  -  open the next revision as a copy of the current one

          @MTransNo is the revision being amended.  Everything on it is copied
          forward, the old revision is marked superseded, and the new draft is
          returned for editing.
        ==================================================================*/
        ELSE IF (@Status = 'Amend')
        BEGIN
            DECLARE @AmdSeries CHAR(10), @AmdYear INT, @AmdNo INT, @AmdCur SMALLINT, @NewMTransNo INT;

            SELECT @AmdSeries = DocSeries, @AmdYear = DocYear, @AmdNo = DocNo, @AmdCur = DocAmdNo
              FROM dbo.POMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID
               AND DeleteStatus = 'N' AND DocAmdStatus = 'N';

            IF @AmdSeries IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Only the current revision of an existing order can be amended.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @AmdCur = ISNULL(MAX(DocAmdNo), @AmdCur)
              FROM dbo.POMst
             WHERE DocSeries = @AmdSeries AND DocYear = @AmdYear AND DocNo = @AmdNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            INSERT INTO dbo.POMst
            (DocSeries, DocYear, DocNo, DocDate, DocAmdNo, DocAmdStatus,
             TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
             VendorNo, VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
             RefNo, RefDate, DueDate, DocNotes, DocAmdNotes, DocStatus, TaxType,
             BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
             IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt, RoundOff, NetValue,
             TxnCurrency, CurrencyRate, CurrencyAmount,
             PurSubject, VendorNoShip, BusinessUnit, Plant, Source,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT DocSeries, DocYear, DocNo, ISNULL(@DocDate, DocDate), @AmdCur + 1, 'N',
                   TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
                   VendorNo, VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
                   RefNo, RefDate, DueDate, DocNotes, LTRIM(RTRIM(@DocAmdNotes)), 'N', TaxType,
                   BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
                   IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt, RoundOff, NetValue,
                   TxnCurrency, CurrencyRate, CurrencyAmount,
                   PurSubject, VendorNoShip, BusinessUnit, Plant, Source,
                   @SystemIP, @ModifiedBy, GETDATE(), 'N', 'N', 1, SubscID
              FROM dbo.POMst
             WHERE MTransNo = @MTransNo;

            SET @NewMTransNo = CONVERT(INT, SCOPE_IDENTITY());

            INSERT INTO dbo.PODtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, ItemRemarks, HSNCode,
             Qty, Unit, Rate, MRP, BasicRate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt,
             FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
             Qty2, ConvRatio, TxnUnit, Unit2, OANo, OAID, OAYear, DeleveryDate,
             GradeNo, Grade, CoreGrade, Spare, SpareGrade, SpareQty, MtrlStd,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT SlNo, @NewMTransNo, PartNo, PartCode, PartDescription, ItemRemarks, HSNCode,
                   Qty, Unit, Rate, MRP, BasicRate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt,
                   FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
                   Qty2, ConvRatio, TxnUnit, Unit2, OANo, OAID, OAYear, DeleveryDate,
                   GradeNo, Grade, CoreGrade, Spare, SpareGrade, SpareQty, MtrlStd,
                   @SystemIP, @ModifiedBy, GETDATE(), 'N', 'N', 1, SubscID
              FROM dbo.PODtl
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.POTerms
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT @NewMTransNo, SlNo, Title, Description, @SystemIP, @ModifiedBy, GETDATE(),
                   'N', 'N', 1, SubscID
              FROM dbo.POTerms
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.POExtras
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, LockStatus, LockCounter, SubscID)
            SELECT @NewMTransNo, SlNo, Title, Description, @SystemIP, @ModifiedBy, GETDATE(),
                   'N', 'N', 1, SubscID
              FROM dbo.POExtras
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.POMst SET DocAmdStatus = 'Y',
                                 ModifiedBy = @ModifiedBy, ModifiedOn = GETDATE()
             WHERE DocSeries = @AmdSeries AND DocYear = @AmdYear AND DocNo = @AmdNo
               AND SubscID = @SubscID AND DeleteStatus = 'N' AND MTransNo <> @NewMTransNo;

            EXEC dbo.SP_POMstRecalc @NewMTransNo, @SubscID;

            SET @ID = @NewMTransNo;
            SET @Message = 'Amendment ' + CONVERT(VARCHAR(10), @AmdCur + 1) + ' created successfully.';
        END

        /*==================================================================
          RECALC
        ==================================================================*/
        ELSE IF (@Status = 'Recalc')
        BEGIN
            EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;
            SET @ID = @MTransNo; SET @Message = 'Totals recalculated.';
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
  SP_GRNMst - vendor tax type from FN_PurchaseTaxType (direct GRN; a GRN
  against a PO still takes the order's tax type)
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @TargetType     VARCHAR(10),
    @VendorNo       INT,
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @InwardNo       VARCHAR(50),
    @InwardDate     DATE,
    @LRNo           VARCHAR(50),
    @LRDate         DATE,
    @DCNo           VARCHAR(50),
    @DCDate         DATE,
    @VehicleNo      VARCHAR(20),
    @BillValue      NUMERIC(14,2),
    @DocNotes       VARCHAR(250),
    @TDSP           NUMERIC(6,2),
    @TCSP           NUMERIC(6,2),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16,2),
    @RoundOff       NUMERIC(6,2),
    @TxnCurrency    VARCHAR(10),
    @CurrencyRate   NUMERIC(6,2),
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

        DECLARE @VendorCode    VARCHAR(20),
                @VendorName    VARCHAR(100),
                @Address       VARCHAR(500),
                @GSTIN         VARCHAR(20),
                @StateName     VARCHAR(50),
                @PhoneNo       VARCHAR(30),
                @EmailID       VARCHAR(80),
                @TaxType       VARCHAR(20),
                @VendStateCode INT,
                @CompStateCode INT,
                @SupCurrency   VARCHAR(15),
                @TargetDocYear INT,
                @TargetDocNo   INT,
                @TargetAmdNo   INT,
                @CurGRNType    CHAR(1);

        SET @TargetType = NULLIF(LTRIM(RTRIM(ISNULL(@TargetType, ''))), '');
        IF @TargetType IS NULL SET @TargetType = 'Direct';

        /*------------------------------------------------------------------
          A GRN against a purchase order takes the order's identity with it,
          and may only be raised on the live, approved revision.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF @TargetType = 'PO'
            BEGIN
                SELECT @TargetDocYear = P.DocYear,
                       @TargetDocNo   = P.DocNo,
                       @TargetAmdNo   = P.DocAmdNo,
                       @VendorNo      = P.VendorNo,
                       @VendorCode    = P.VendorCode,
                       @VendorName    = P.VendorName,
                       @Address       = P.Address,
                       @GSTIN         = P.GSTIN,
                       @StateName     = P.StateName,
                       @PhoneNo       = P.PhoneNo,
                       @EmailID       = P.EmailID,
                       @TaxType       = LTRIM(RTRIM(P.TaxType)),
                       @SupCurrency   = P.TxnCurrency
                  FROM dbo.POMst P
                 WHERE P.MTransNo = @TargetTransNo AND P.SubscID = @SubscID
                   AND P.DeleteStatus = 'N' AND P.DocAmdStatus = 'N' AND P.LockStatus = 'Y';

                IF @VendorName IS NULL
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Approved purchase order not found. Goods can only be received against the current, approved revision.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
            ELSE
            BEGIN
                SELECT @VendorCode    = LTRIM(RTRIM(ISNULL(S.SupplierCode, ''))),
                       @VendorName    = LTRIM(RTRIM(ISNULL(S.SupplierName, ''))),
                       @Address       = LTRIM(RTRIM(ISNULL(S.SupplierAddress, ''))),
                       @GSTIN         = LTRIM(RTRIM(ISNULL(S.GSTIN, ''))),
                       @StateName     = S.StateName,
                       @PhoneNo       = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(S.PhoneNo, ''))), ''), S.MobileNo),
                       @EmailID       = S.EmailID,
                       @TaxType       = dbo.FN_PurchaseTaxType(S.SupplierType, S.TaxType, S.StateCode, @SubscID),
                       @VendStateCode = S.StateCode,
                       @SupCurrency   = S.TxnCurrency
                  FROM dbo.SupplierMaster S
                 WHERE S.MTransNo = @VendorNo AND S.DeleteStatus = 'N';

                IF @VendorName IS NULL
                BEGIN
                    SET @ID = 0; SET @Message = 'Supplier not found.';
                    COMMIT TRANSACTION; RETURN;
                END

                IF @TaxType IS NULL
                BEGIN
                    SELECT TOP 1 @CompStateCode = C.StateCode
                      FROM dbo.Company C
                     WHERE C.SubscID = @SubscID AND C.DeleteStatus = 'N'
                     ORDER BY C.MTransNo;

                    SET @TaxType = CASE
                            WHEN @VendStateCode IS NULL OR @CompStateCode IS NULL THEN 'Intra State'
                            WHEN @VendStateCode <> @CompStateCode                 THEN 'Inter State'
                            ELSE 'Intra State' END;
                END

                SET @TargetTransNo = NULL;
            END

            SET @TxnCurrency = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@TxnCurrency, ''))), ''), @SupCurrency);
        END

        /*==================================================================
          INSERT
        ==================================================================*/
        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.GRNMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;

            SELECT @MCount = COUNT(MTransNo) FROM dbo.GRNMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'GRN already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.GRNMst
                (DocSeries, DocYear, DocNo, DocDate,
                 TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
                 VendorNo, VendorCode, VendorName, Address, GSTIN, StateName, PhoneNo, EmailID,
                 RefNo, RefDate, InwardNo, InwardDate, LRNo, LRDate, DCNo, DCDate,
                 VehicleNo, BillValue, DocNotes, DocStatus, TaxType, GRNType,
                 BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
                 IGSTAmt, CGSTAmt, SGSTAmt, TDSP, TDSAmt, TCSP, TCSAmt,
                 OtherCharges, OtherAmt, RoundOff, NetValue, DebitValue, CreditValue,
                 TxnCurrency, CurrencyRate, CurrencyAmount,
                 SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
                 LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                 @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo, @TargetType,
                 @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN, @StateName, @PhoneNo, @EmailID,
                 LTRIM(RTRIM(@RefNo)), @RefDate, LTRIM(RTRIM(@InwardNo)), @InwardDate,
                 LTRIM(RTRIM(@LRNo)), @LRDate, LTRIM(RTRIM(@DCNo)), @DCDate,
                 LTRIM(RTRIM(@VehicleNo)), ISNULL(@BillValue, 0),
                 LTRIM(RTRIM(@DocNotes)), 'N', @TaxType, 'N',
                 0, 0, 0, 0, 0, 0, 0, 0,
                 ISNULL(@TDSP, 0), 0, ISNULL(@TCSP, 0), 0,
                 LTRIM(RTRIM(@OtherCharges)), ISNULL(@OtherAmt, 0), ISNULL(@RoundOff, 0), 0, 0, 0,
                 @TxnCurrency, @CurrencyRate, 0,
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL,
                 ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                EXEC dbo.SP_GRNMstRecalc @ID, @SubscID;
                SET @Message = 'GRN saved successfully.';
            END
        END

        /*==================================================================
          UPDATE
        ==================================================================*/
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @CurGRNType = GRNType FROM dbo.GRNMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @CurGRNType IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @CurGRNType IN ('Q', 'C')
            BEGIN
                SET @ID = 0;
                SET @Message = 'This GRN has been inspected. Reopen the inspection before editing it.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNMst
               SET DocDate       = @DocDate,
                   TargetTransNo = @TargetTransNo,
                   TargetDocYear = @TargetDocYear,
                   TargetDocNo   = @TargetDocNo,
                   TargetAmdNo   = @TargetAmdNo,
                   TargetType    = @TargetType,
                   VendorNo      = @VendorNo,
                   VendorCode    = @VendorCode,
                   VendorName    = @VendorName,
                   Address       = @Address,
                   GSTIN         = @GSTIN,
                   StateName     = @StateName,
                   PhoneNo       = @PhoneNo,
                   EmailID       = @EmailID,
                   RefNo         = LTRIM(RTRIM(@RefNo)),
                   RefDate       = @RefDate,
                   InwardNo      = LTRIM(RTRIM(@InwardNo)),
                   InwardDate    = @InwardDate,
                   LRNo          = LTRIM(RTRIM(@LRNo)),
                   LRDate        = @LRDate,
                   DCNo          = LTRIM(RTRIM(@DCNo)),
                   DCDate        = @DCDate,
                   VehicleNo     = LTRIM(RTRIM(@VehicleNo)),
                   BillValue     = ISNULL(@BillValue, 0),
                   DocNotes      = LTRIM(RTRIM(@DocNotes)),
                   TaxType       = @TaxType,
                   TDSP          = ISNULL(@TDSP, 0),
                   TCSP          = ISNULL(@TCSP, 0),
                   OtherCharges  = LTRIM(RTRIM(@OtherCharges)),
                   OtherAmt      = ISNULL(@OtherAmt, 0),
                   RoundOff      = ISNULL(@RoundOff, 0),
                   TxnCurrency   = @TxnCurrency,
                   CurrencyRate  = @CurrencyRate,
                   SystemIP      = @SystemIP,
                   ModifiedBy    = @ModifiedBy,
                   ModifiedOn    = ISNULL(@ModifiedOn, GETDATE()),
                   LockStatus    = ISNULL(@LockStatus, LockStatus),
                   LockCounter   = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                -- As on the order: a change of tax type reshuffles the line GST.
                UPDATE D
                   SET IGSTAmt = CASE WHEN @TaxType = 'Inter State'
                                      THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END,
                       CGSTAmt = CASE WHEN @TaxType = 'Intra State'
                                      THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END,
                       SGSTAmt = CASE WHEN @TaxType = 'Intra State'
                                      THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END,
                       TotalValue = ISNULL(D.TaxableValue, 0)
                                  + CASE WHEN @TaxType = 'Inter State'
                                         THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END
                                  + CASE WHEN @TaxType = 'Intra State'
                                         THEN 2 * ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END
                  FROM dbo.GRNDtl D
                 WHERE D.MTransNo = @MTransNo AND D.DeleteStatus = 'N';

                EXEC dbo.SP_GRNMstRecalc @MTransNo, @SubscID;
                SET @ID = @MTransNo; SET @Message = 'GRN updated successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        /*==================================================================
          DELETE
        ==================================================================*/
        ELSE IF (@Status = 'Delete')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.HeatNoMst
                        WHERE TargetTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Heat numbers have been recorded against this GRN. Delete them first.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.GRNDtl        SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                             ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.GRNNCDtl      SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                             ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.GRNQRCode     SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                             ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.GRNAttachment SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                             ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'GRN deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        /*==================================================================
          LOCK / UNLOCK
        ==================================================================*/
        ELSE IF (@Status = 'Lock')
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Add at least one line before confirming the receipt.';
                COMMIT TRANSACTION; RETURN;
            END

            EXEC dbo.SP_GRNMstRecalc @MTransNo, @SubscID;

            UPDATE dbo.GRNMst
               SET LockStatus = 'Y', DocStatus = 'A',
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'GRN confirmed successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Unlock')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.GRNMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                          AND DeleteStatus = 'N' AND GRNType IN ('Q', 'C'))
            BEGIN
                SET @ID = 0;
                SET @Message = 'This GRN has been inspected. Reopen the inspection before unlocking it.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNMst
               SET LockStatus = 'N', DocStatus = 'N',
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'GRN unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Recalc')
        BEGIN
            EXEC dbo.SP_GRNMstRecalc @MTransNo, @SubscID;
            SET @ID = @MTransNo; SET @Message = 'Totals recalculated.';
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
  BACKFILL - re-price documents saved with a non-GST tax type
------------------------------------------------------------------------------*/
DECLARE @Fix TABLE (Kind CHAR(3), MTransNo INT, SubscID INT);

UPDATE M
   SET TaxType = dbo.FN_PurchaseTaxType(S.SupplierType, S.TaxType, S.StateCode, M.SubscID)
OUTPUT 'PO', inserted.MTransNo, inserted.SubscID INTO @Fix
  FROM dbo.POMst M
  JOIN dbo.SupplierMaster S ON S.MTransNo = M.VendorNo
 WHERE M.DeleteStatus = 'N'
   AND ISNULL(LTRIM(RTRIM(M.TaxType)), '') NOT IN ('Inter State', 'Intra State', 'Export');

UPDATE M
   SET TaxType = dbo.FN_PurchaseTaxType(S.SupplierType, S.TaxType, S.StateCode, M.SubscID)
OUTPUT 'GRN', inserted.MTransNo, inserted.SubscID INTO @Fix
  FROM dbo.GRNMst M
  JOIN dbo.SupplierMaster S ON S.MTransNo = M.VendorNo
 WHERE M.DeleteStatus = 'N'
   AND ISNULL(LTRIM(RTRIM(M.TaxType)), '') NOT IN ('Inter State', 'Intra State', 'Export');

UPDATE D
   SET IGSTAmt    = X.IGST, CGSTAmt = X.HalfGST, SGSTAmt = X.HalfGST,
       TotalValue = ISNULL(D.TaxableValue, 0) + X.IGST + 2 * X.HalfGST
  FROM dbo.PODtl D
  JOIN dbo.POMst M ON M.MTransNo = D.MTransNo
  JOIN @Fix F ON F.Kind = 'PO' AND F.MTransNo = M.MTransNo
 CROSS APPLY (SELECT CASE WHEN M.TaxType = 'Inter State'
                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END AS IGST,
                     CASE WHEN M.TaxType = 'Intra State'
                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END AS HalfGST) X
 WHERE D.DeleteStatus = 'N';

UPDATE D
   SET IGSTAmt    = X.IGST, CGSTAmt = X.HalfGST, SGSTAmt = X.HalfGST,
       TotalValue = ISNULL(D.TaxableValue, 0) + X.IGST + 2 * X.HalfGST
  FROM dbo.GRNDtl D
  JOIN dbo.GRNMst M ON M.MTransNo = D.MTransNo
  JOIN @Fix F ON F.Kind = 'GRN' AND F.MTransNo = M.MTransNo
 CROSS APPLY (SELECT CASE WHEN M.TaxType = 'Inter State'
                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 100.0, 2) ELSE 0 END AS IGST,
                     CASE WHEN M.TaxType = 'Intra State'
                          THEN ROUND(ISNULL(D.TaxableValue, 0) * ISNULL(D.GSTP, 0) / 200.0, 2) ELSE 0 END AS HalfGST) X
 WHERE D.DeleteStatus = 'N';

DECLARE @Kind CHAR(3), @No INT, @Sub INT;
DECLARE fix_cur CURSOR LOCAL FAST_FORWARD FOR SELECT Kind, MTransNo, SubscID FROM @Fix;
OPEN fix_cur;
FETCH NEXT FROM fix_cur INTO @Kind, @No, @Sub;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @Kind = 'PO' EXEC dbo.SP_POMstRecalc  @No, @Sub;
    ELSE            EXEC dbo.SP_GRNMstRecalc @No, @Sub;
    FETCH NEXT FROM fix_cur INTO @Kind, @No, @Sub;
END
CLOSE fix_cur; DEALLOCATE fix_cur;

SELECT Kind, COUNT(*) AS DocumentsRepriced FROM @Fix GROUP BY Kind;
GO
