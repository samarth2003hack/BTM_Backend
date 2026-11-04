/*==============================================================================
  14. PURCHASE ORDER  -  STORED PROCEDURES

  Run 13_PurchaseOrderGRNTables.sql first.

  ----------------------------------------------------------------------------
  HOW A PURCHASE ORDER IS RAISED
  ----------------------------------------------------------------------------

  The order always starts from an approved Purchase Requisition.  What happens
  next depends on whether that requisition went out to quote:

      PurReqMst (LockStatus = 'Y')
            |
            +--  requisition HAS an approved supplier quote
            |         the buyer picks PR No. + RFQ No., and the order is
            |         GENERATED WHOLE from the quote - vendor, lines, rates and
            |         terms all copied.  No item picker: there is nothing left
            |         to choose.                     -> SP_POGenerateFromQuote
            |                                           TargetType = 'PQ'
            |
            +--  requisition has NO approved quote
                      the buyer picks PR No., then picks the items off the
                      requisition in the item popup and types the rates.
                                                     -> SP_POMst + SP_PODtl
                                                        TargetType = 'PR'

  Only APPROVED quotes are offered.  A quote is approved when its RFQPriceMst
  row is the live revision (DocAmdStatus = 'N') and locked (LockStatus = 'Y').
  An unlocked quote is still being negotiated and must not become an order.

  The requisition is reachable from either kind of order without a column of
  its own:
      TargetType = 'PQ' :  POMst -> RFQPriceMst -> RFQMst.PRTransNo -> PurReqMst
                           (RFQPriceMst also carries PRYear / PRNo directly)
      TargetType = 'PR' :  POMst.TargetTransNo -> PurReqMst
  POMst.Source additionally records a readable breadcrumb such as 'PR 2627-14'.

  ----------------------------------------------------------------------------
  CONVENTIONS EVERY PROCEDURE HERE FOLLOWS  (same as SP_RFQPriceMst and friends)
  ----------------------------------------------------------------------------
      * @ID INT OUTPUT and @Message NVARCHAR(200) OUTPUT on every procedure.
        @ID = 0 means "refused, and @Message says why" - the caller turns that
        into an HTTP 409.  A thrown error means something unexpected.
      * @Status drives the branch: Insert / Update / Delete / Lock / Unlock,
        plus DeleteAll on the child tables and Amend / Recalc on the header.
      * Everything runs inside BEGIN TRANSACTION with a rollback in CATCH.
      * Deletes are soft: DeleteStatus = 'Y' plus a reason.
      * The header never takes its own totals from the caller.  They are
        recomputed from the lines - see SP_POMstRecalc - so the printed order
        can never disagree with what is on it.
      * The vendor block (code, name, address, GSTIN, state, tax type) is
        snapshotted from SupplierMaster inside the procedure, not passed in.
        An order printed last year must keep last year's address.

  ----------------------------------------------------------------------------
  WHY "CREATE STUB, THEN ALTER" AND NOT "CREATE OR ALTER"
  ----------------------------------------------------------------------------
  Every procedure here is created as an empty stub if it is missing, and then
  ALTERed into its real body.  That is two statements where CREATE OR ALTER
  would be one, and it is deliberate.

  A name beginning with "sp_" - which SP_POMst does, as far as SQL Server's
  parser is concerned, because the check is case-insensitive - is resolved
  against master before the current database.  This server has stray
  SP_POMst, SP_PODTL, SP_POExtras, SP_GRNMst and SP_GRNDtl procedures sitting
  in master, left behind by an older script that ran without a USE statement.
  With those present, CREATE OR ALTER PROCEDURE dbo.SP_POMst inside EMSSoft
  fails outright:

      Msg 208 ... Invalid object name 'dbo.SP_POMst'.

  CREATE followed by ALTER is not affected, so the pattern below installs
  cleanly whether or not master has been tidied up.  Calls are unaffected
  either way: every EXEC in this schema is schema-qualified (EXEC dbo.SP_...),
  and a qualified name resolves in the current database.

  Tidying master is still worth doing, separately and deliberately:
      USE master;
      DROP PROCEDURE dbo.SP_POMst, dbo.SP_PODTL, dbo.SP_POExtras,
                     dbo.SP_GRNMst, dbo.SP_GRNDtl;

  Idempotent: safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  SP_POMstRecalc  -  rebuild a purchase order's totals from its lines

  Called at the end of every operation that can change what is on the order.
  Kept separate so there is exactly one definition of what a PO adds up to.

      BasicValue    net of line discount
      ExemptValue   the part of the taxable value carrying no GST
      TaxableValue  basic - discount + packing & forwarding + freight
      NetValue      taxable + GST + other charges + round-off

  CurrencyAmount is the net restated in the transaction currency; with no rate
  on file it mirrors NetValue.
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_POMstRecalc', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POMstRecalc AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POMstRecalc
    @MTransNo INT,
    @SubscID  INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE M
       SET BasicValue     = ISNULL(L.BasicValue, 0),
           ExemptValue    = ISNULL(L.ExemptValue, 0),
           TaxableValue   = ISNULL(L.TaxableValue, 0),
           PnFAmt         = ISNULL(L.PnFAmt, 0),
           FreightAmt     = ISNULL(L.FreightAmt, 0),
           IGSTAmt        = ISNULL(L.IGSTAmt, 0),
           CGSTAmt        = ISNULL(L.CGSTAmt, 0),
           SGSTAmt        = ISNULL(L.SGSTAmt, 0),
           NetValue       = ISNULL(L.TaxableValue, 0) + ISNULL(L.IGSTAmt, 0)
                          + ISNULL(L.CGSTAmt, 0) + ISNULL(L.SGSTAmt, 0)
                          + ISNULL(M.OtherAmt, 0) + ISNULL(M.RoundOff, 0),
           CurrencyAmount = CASE WHEN ISNULL(M.CurrencyRate, 0) > 0
                                 THEN ROUND((ISNULL(L.TaxableValue, 0) + ISNULL(L.IGSTAmt, 0)
                                           + ISNULL(L.CGSTAmt, 0) + ISNULL(L.SGSTAmt, 0)
                                           + ISNULL(M.OtherAmt, 0) + ISNULL(M.RoundOff, 0))
                                           / M.CurrencyRate, 2)
                                 ELSE ISNULL(L.TaxableValue, 0) + ISNULL(L.IGSTAmt, 0)
                                    + ISNULL(L.CGSTAmt, 0) + ISNULL(L.SGSTAmt, 0)
                                    + ISNULL(M.OtherAmt, 0) + ISNULL(M.RoundOff, 0)
                            END
      FROM dbo.POMst M
     OUTER APPLY
          (
              SELECT SUM(D.BasicValue - ISNULL(D.DiscAmt, 0))                         AS BasicValue,
                     SUM(CASE WHEN ISNULL(D.GSTP, 0) = 0 THEN ISNULL(D.TaxableValue, 0)
                              ELSE 0 END)                                             AS ExemptValue,
                     SUM(ISNULL(D.TaxableValue, 0))                                   AS TaxableValue,
                     SUM(ISNULL(D.PnFAmt, 0))                                         AS PnFAmt,
                     SUM(ISNULL(D.FreightAmt, 0))                                     AS FreightAmt,
                     SUM(ISNULL(D.IGSTAmt, 0))                                        AS IGSTAmt,
                     SUM(ISNULL(D.CGSTAmt, 0))                                        AS CGSTAmt,
                     SUM(ISNULL(D.SGSTAmt, 0))                                        AS SGSTAmt
                FROM dbo.PODtl D
               WHERE D.MTransNo = M.MTransNo AND D.DeleteStatus = 'N'
          ) L
     WHERE M.MTransNo = @MTransNo AND M.SubscID = @SubscID;
END
GO


/*==============================================================================
  SP_PODtlPrice  -  price one purchase order line

  The whole of the line arithmetic, in one place, so SP_PODtl and
  SP_POGenerateFromQuote cannot drift apart.

      BasicValue   = Qty x Rate
      DiscAmt      = BasicValue x DiscP%
      BasicRate    = the unit rate after discount
      PnFAmt       = (BasicValue - DiscAmt) x PnFP%
      TaxableValue = BasicValue - DiscAmt + PnFAmt + Freight
      GST          = Intra State -> CGST and SGST at half the rate each
                     Inter State -> IGST at the full rate
                     anything else (export, exempt) -> no GST at all
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_PODtlPrice', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PODtlPrice AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PODtlPrice
    @Qty          NUMERIC(12,4),
    @Rate         NUMERIC(14,2),
    @DiscP        NUMERIC(6,2),
    @PnFP         NUMERIC(6,2),
    @FreightAmt   NUMERIC(14,2),
    @GSTP         NUMERIC(6,2),
    @TaxType      VARCHAR(20),
    @BasicValue   NUMERIC(16,2) OUTPUT,
    @DiscAmt      NUMERIC(12,2) OUTPUT,
    @BasicRate    NUMERIC(14,2) OUTPUT,
    @PnFAmt       NUMERIC(14,2) OUTPUT,
    @TaxableValue NUMERIC(14,2) OUTPUT,
    @IGSTAmt      NUMERIC(14,2) OUTPUT,
    @CGSTAmt      NUMERIC(14,2) OUTPUT,
    @SGSTAmt      NUMERIC(14,2) OUTPUT,
    @TotalValue   NUMERIC(16,2) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @Qty        = ISNULL(@Qty, 0);
    SET @Rate       = ISNULL(@Rate, 0);
    SET @DiscP      = ISNULL(@DiscP, 0);
    SET @PnFP       = ISNULL(@PnFP, 0);
    SET @FreightAmt = ISNULL(@FreightAmt, 0);
    SET @GSTP       = ISNULL(@GSTP, 0);
    SET @TaxType    = LTRIM(RTRIM(ISNULL(@TaxType, '')));

    SET @BasicValue   = ROUND(@Qty * @Rate, 2);
    SET @DiscAmt      = ROUND(@BasicValue * @DiscP / 100.0, 2);
    SET @BasicRate    = ROUND(@Rate - (@Rate * @DiscP / 100.0), 2);
    SET @PnFAmt       = ROUND((@BasicValue - @DiscAmt) * @PnFP / 100.0, 2);
    SET @TaxableValue = @BasicValue - @DiscAmt + @PnFAmt + @FreightAmt;

    -- A GST percentage only means anything on a domestic supply.
    IF @TaxType = 'Intra State'
    BEGIN
        SET @IGSTAmt = 0;
        SET @CGSTAmt = ROUND(@TaxableValue * @GSTP / 200.0, 2);
        SET @SGSTAmt = ROUND(@TaxableValue * @GSTP / 200.0, 2);
    END
    ELSE IF @TaxType = 'Inter State'
    BEGIN
        SET @IGSTAmt = ROUND(@TaxableValue * @GSTP / 100.0, 2);
        SET @CGSTAmt = 0;
        SET @SGSTAmt = 0;
    END
    ELSE
    BEGIN
        SET @IGSTAmt = 0;
        SET @CGSTAmt = 0;
        SET @SGSTAmt = 0;
    END

    SET @TotalValue = @TaxableValue + @IGSTAmt + @CGSTAmt + @SGSTAmt;
END
GO


/*==============================================================================
  SP_POMst  -  purchase order header

  @Status
      Insert   allocates DocNo for a first revision (DocAmdNo = 0); for
               DocAmdNo > 0 it supersedes the earlier revisions of the same
               DocNo instead, flipping their DocAmdStatus to 'Y'.
      Update   header fields only; the vendor block is re-snapshotted.
      Delete   soft delete, header and all its children.
      Lock     LockStatus = 'Y' - approved; only now can a GRN be raised.
      Unlock   LockStatus = 'N' - back to draft.  Refused once anything has
               been received against the order.
      Amend    opens the next revision: copies the live revision's lines, terms
               and extras into DocAmdNo + 1 and supersedes the old one.
      Recalc   totals only.

  The caller never sends the vendor's name, address, GSTIN or the tax type;
  they are read from SupplierMaster here.  Tax type falls back to comparing the
  supplier's state against the company's when the supplier record does not say.
==============================================================================*/
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
                   @TaxType       = NULLIF(LTRIM(RTRIM(ISNULL(S.TaxType, ''))), ''),
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


/*==============================================================================
  SP_PODtl  -  purchase order lines

  @Status:  Insert / Update / Delete / DeleteAll

  Part code, description, HSN, units, conversion ratio and the GST percentage
  are taken from PartMaster here rather than trusted from the caller - the
  order must show what the part master says it is.  The caller supplies only
  what is negotiable: quantity, rate, discount, packing, freight, delivery
  date, grade and remarks.

  Every branch re-prices the line through SP_PODtlPrice and then re-adds the
  header through SP_POMstRecalc, so the two can never fall out of step.
==============================================================================*/
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


/*==============================================================================
  SP_POTerms  -  purchase order terms and conditions
  @Status:  Insert / Update / Delete / DeleteAll
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_POTerms', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POTerms AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POTerms
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6,2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
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

        IF NOT EXISTS (SELECT 1 FROM dbo.POMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'Purchase Order not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.POTerms WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.POTerms
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Term saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.POTerms
               SET Title = LTRIM(RTRIM(@Title)), Description = LTRIM(RTRIM(@Description)),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.POTerms
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.POTerms
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All terms deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active terms found.'; END
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


/*==============================================================================
  SP_POExtras  -  purchase order user-defined fields
  @Status:  Insert / Update / Delete / DeleteAll
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_POExtras', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_POExtras AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_POExtras
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6,2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(100),
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

        IF NOT EXISTS (SELECT 1 FROM dbo.POMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'Purchase Order not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.POExtras WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.POExtras
            (MTransNo, SlNo, Title, Description, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Field saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.POExtras
               SET Title = LTRIM(RTRIM(@Title)), Description = LTRIM(RTRIM(@Description)),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Field updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.POExtras
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Field deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.POExtras
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All fields deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active fields found.'; END
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


/*==============================================================================
  SP_POGenerateFromQuote  -  raise a whole purchase order from an approved quote

  This is the "no item picker" path.  The buyer has chosen a purchase
  requisition and one of its APPROVED supplier quotes; everything the order
  needs is already on that quote, so the order is generated in one call and
  handed back for display:

      header  <- RFQPriceMst   vendor, reference, currency
      lines   <- RFQPriceDtl   part, quantity, rate  (priced here)
      terms   <- RFQPriceTerms copied as they stand

  Refused when the quote is missing, superseded, or not yet approved, and when
  a live order already exists against it - a quote is ordered once.

  @ID returns the new POMst.MTransNo.
==============================================================================*/
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

        /*------------------------------------------------------------------
          The quote must be live and approved.  LockStatus = 'Y' is what
          approval means for RFQPriceMst; DocAmdStatus = 'N' is the current
          revision of it.
        ------------------------------------------------------------------*/
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

        /*  Breadcrumb back to the requisition, for the printed order.  The
            quote carries PRYear/PRNo; fall back to walking the RFQ.  */
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

        /*------------------------------------------------------------------
          Header, through SP_POMst so numbering, the vendor snapshot and the
          tax-type derivation all behave exactly as they do on a typed order.
        ------------------------------------------------------------------*/
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

        /*------------------------------------------------------------------
          Lines.  A cursor, not a set insert: every line has to go through
          SP_PODtl so that the part snapshot and the tax arithmetic are the
          same ones a typed line would get.
        ------------------------------------------------------------------*/
        DECLARE @qPartNo  INT,
                @qQty     NUMERIC(12,4),
                @qRate    NUMERIC(14,2),
                @qRemarks VARCHAR(500),
                @lineID   INT,
                @lineMsg  NVARCHAR(200);

        DECLARE QuoteLines CURSOR LOCAL FAST_FORWARD FOR
            SELECT PartNo, Qty, Rate, ItemRemarks
              FROM dbo.RFQPriceDtl
             WHERE MTransNo = @QuoteTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
             ORDER BY SLNo, TransSlNo;

        OPEN QuoteLines;
        FETCH NEXT FROM QuoteLines INTO @qPartNo, @qQty, @qRate, @qRemarks;

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
                 @Grade        = NULL,
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
                 @Message      = @lineMsg OUTPUT;

            IF ISNULL(@lineID, 0) > 0 SET @LineCount = @LineCount + 1;

            FETCH NEXT FROM QuoteLines INTO @qPartNo, @qQty, @qRate, @qRemarks;
        END

        CLOSE QuoteLines;
        DEALLOCATE QuoteLines;

        IF @LineCount = 0
        BEGIN
            -- Nothing copied across: roll the header back rather than leave an
            -- empty order behind.
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            SET @ID = 0;
            SET @Message = 'No line item on the quote could be ordered. Check the parts still exist in the part master.';
            RETURN;
        END

        /*------------------------------------------------------------------
          Terms, copied as they stand.
        ------------------------------------------------------------------*/
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


/*==============================================================================
  VIEWS THAT DRIVE THE SOURCE PICKER ON THE PURCHASE ORDER SCREEN
==============================================================================*/

/*------------------------------------------------------------------------------
  VW_PurReqForPO  -  approved requisitions, each flagged with what it has

  One row per approved purchase requisition, answering in a single read the
  question the PO screen opens with: "does this requisition have an approved
  quote, or does the buyer have to pick items by hand?"

      HasRFQ           an RFQ was raised against the requisition
      ApprovedQuotes   how many of the quotes that came back are approved
      SourceMode       'Quote' -> pick an RFQ No., order generates itself
                       'Items' -> no approved quote, use the item picker
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.VW_PurReqForPO', 'V') IS NOT NULL
    DROP VIEW dbo.VW_PurReqForPO;
GO

CREATE VIEW dbo.VW_PurReqForPO
AS
SELECT  PR.MTransNo   AS PRTransNo,
        PR.DocSeries  AS PRDocSeries,
        PR.DocYear    AS PRDocYear,
        PR.DocNo      AS PRDocNo,
        PR.DocDate    AS PRDocDate,
        PR.DocAmdNo   AS PRDocAmdNo,
        PR.ReqBy,
        PR.ReqDept,
        PR.Project,
        PR.DueDate,
        PR.DocNotes,
        PR.RFQReq,
        PR.LockStatus,
        PR.SubscID,
        ISNULL(D.LineCount, 0)   AS LineCount,
        ISNULL(Q.RFQCount, 0)    AS RFQCount,
        ISNULL(Q.QuoteCount, 0)  AS QuoteCount,
        ISNULL(Q.ApprovedQuotes, 0) AS ApprovedQuotes,
        CASE WHEN ISNULL(Q.RFQCount, 0) > 0 THEN 'Y' ELSE 'N' END AS HasRFQ,
        CASE WHEN ISNULL(Q.ApprovedQuotes, 0) > 0 THEN 'Quote' ELSE 'Items' END AS SourceMode
  FROM  dbo.PurReqMst PR
 OUTER APPLY
       (
           SELECT COUNT(*) AS LineCount
             FROM dbo.PurReqDtl PD
            WHERE PD.MTransNo = PR.MTransNo AND PD.DeleteStatus = 'N'
       ) D
 OUTER APPLY
       (
           SELECT COUNT(DISTINCT RQ.MTransNo) AS RFQCount,
                  COUNT(QP.MTransNo)          AS QuoteCount,
                  SUM(CASE WHEN QP.LockStatus = 'Y' AND QP.DocAmdStatus = 'N'
                           THEN 1 ELSE 0 END) AS ApprovedQuotes
             FROM dbo.RFQMst RQ
             LEFT JOIN dbo.RFQPriceMst QP
                    ON QP.TargetTransNo = RQ.MTransNo
                   AND QP.DeleteStatus  = 'N'
            WHERE RQ.PRTransNo    = PR.MTransNo
              AND RQ.DeleteStatus = 'N'
              AND RQ.DocAmdStatus = 'N'
       ) Q
 WHERE  PR.DeleteStatus = 'N'
   AND  PR.DocAmdStatus = 'N'
   AND  PR.LockStatus   = 'Y';
GO

/*------------------------------------------------------------------------------
  VW_QuoteForPO  -  the approved quotes a requisition can be ordered from

  The RFQ No. dropdown on the PO screen.  Only approved, current-revision
  quotes appear, and each already carries the count and value it would turn
  into so the buyer can compare suppliers without opening them.

  POTransNo is non-null once an order has been raised from the quote - the
  screen greys those out rather than hiding them, so it is clear what happened.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.VW_QuoteForPO', 'V') IS NOT NULL
    DROP VIEW dbo.VW_QuoteForPO;
GO

CREATE VIEW dbo.VW_QuoteForPO
AS
SELECT  RQ.PRTransNo,
        RQ.MTransNo    AS RFQTransNo,
        RQ.DocSeries   AS RFQDocSeries,
        RQ.DocYear     AS RFQDocYear,
        RQ.DocNo       AS RFQDocNo,
        RQ.DocDate     AS RFQDocDate,
        QP.MTransNo    AS QuoteTransNo,
        QP.DocSeries   AS QuoteDocSeries,
        QP.DocYear     AS QuoteDocYear,
        QP.DocNo       AS QuoteDocNo,
        QP.DocDate     AS QuoteDocDate,
        QP.ValidTill,
        QP.DocAmdNo    AS QuoteDocAmdNo,
        QP.VendorNo,
        QP.VendorName,
        QP.ContactNo,
        QP.VendorAddress,
        QP.RefNo,
        QP.RefDate,
        QP.PRYear,
        QP.PRNo,
        QP.LockStatus,
        QP.SubscID,
        ISNULL(L.LineCount, 0)  AS LineCount,
        ISNULL(L.QuoteValue, 0) AS QuoteValue,
        PO.MTransNo    AS POTransNo,
        PO.DocYear     AS PODocYear,
        PO.DocNo       AS PODocNo
  FROM  dbo.RFQMst RQ
  JOIN  dbo.RFQPriceMst QP
    ON  QP.TargetTransNo = RQ.MTransNo
 OUTER APPLY
       (
           SELECT COUNT(*)              AS LineCount,
                  SUM(QD.Qty * QD.Rate) AS QuoteValue
             FROM dbo.RFQPriceDtl QD
            WHERE QD.MTransNo = QP.MTransNo AND QD.DeleteStatus = 'N'
       ) L
  LEFT JOIN dbo.POMst PO
         ON PO.TargetType    = 'PQ'
        AND PO.TargetTransNo = QP.MTransNo
        AND PO.DeleteStatus  = 'N'
        AND PO.DocAmdStatus  = 'N'
 WHERE  RQ.DeleteStatus = 'N'
   AND  RQ.DocAmdStatus = 'N'
   AND  QP.DeleteStatus = 'N'
   AND  QP.DocAmdStatus = 'N'
   AND  QP.LockStatus   = 'Y';
GO

/*------------------------------------------------------------------------------
  VW_PurReqItemsForPO  -  the item picker's rows

  Requisition lines with what has already been ordered subtracted, so the
  popup only ever offers what is genuinely still outstanding.  Used on the
  "no approved quote" path.

  OrderedQty counts every live purchase order raised directly against the
  requisition (TargetType = 'PR') as well as every one raised through a quote
  on it (TargetType = 'PQ'), so a part cannot be ordered twice by taking two
  different routes to it.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.VW_PurReqItemsForPO', 'V') IS NOT NULL
    DROP VIEW dbo.VW_PurReqItemsForPO;
GO

CREATE VIEW dbo.VW_PurReqItemsForPO
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


PRINT '14_PurchaseOrderProcedures.sql completed.';
GO
