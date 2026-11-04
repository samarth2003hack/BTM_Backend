/*==============================================================================
  15. GOODS RECEIPT, STORE ALLOCATION, INWARD INSPECTION, HEAT No.
      STORED PROCEDURES

  Run 13_PurchaseOrderGRNTables.sql and 14_PurchaseOrderProcedures.sql first.

  ----------------------------------------------------------------------------
  THE INWARD FLOW, AND WHAT EACH STAGE IS ALLOWED TO TOUCH
  ----------------------------------------------------------------------------

      STAGE                 PROCEDURE              WRITES            GRNType
      --------------------  ---------------------  ----------------  -------
      Goods receipt (GRN)   SP_GRNMst              header            'N'
                            SP_GRNDtl              BillQty, RecQty
      Store allocation      SP_GRNStoreAlloc       Store, Location   unchanged
      Heat No. entry        SP_HeatNoMst/Dtl       HeatNo* tables    unchanged
      Inward inspection     SP_GRNNCDtl            NC lines
        (PO)                SP_GRNInspDtl          Acc/Rej/Rew/AOD
                            SP_GRNInspPost         InspNo, QCDate    -> 'Q'
      Inward inspection     SP_GRNNCClose          NCClose*          -> 'C'
        (closure)

  Each stage owns its own columns.  Goods receipt never writes an inspection
  quantity; inspection never writes a received quantity.  That is what keeps
  the three screens from overwriting one another when two people have the same
  GRN open, and it is why inspection has its own procedure rather than being
  folded into SP_GRNDtl.

  ----------------------------------------------------------------------------
  THE RULES THAT ARE ENFORCED HERE, NOT IN THE BROWSER
  ----------------------------------------------------------------------------
      1. A GRN against a PO may only be raised on the current, approved
         revision of that PO  (DocAmdStatus = 'N' and LockStatus = 'Y').
      2. BillQty on a PO-backed line may not exceed what is still outstanding
         on that PO line - see VW_POPendingGRN.
      3. Inspection must account for everything received:
            AccQty + RejQty + RewQty + AODQty = RecQty
      4. A rejected, reworked or deviation-accepted quantity must be backed by
         non-conformance lines of the matching NCType whose NCQty adds up to
         it.  (The legacy application only checked that at least one NC line
         existed; the quantity is now checked too.)
      5. Inspection cannot be posted twice, and cannot be posted on a GRN with
         no lines.
      6. Closure only applies to an inspected GRN that actually has
         non-conformance against it.
      7. A GRN that has been inspected cannot have its quantities edited; it
         must be reopened first, which is an audited act of its own.

  Every procedure is created as an empty stub if missing and then ALTERed into
  its body, rather than through CREATE OR ALTER.  The reason - stray "sp_"
  named procedures in master shadowing the name - is explained at the head of
  14_PurchaseOrderProcedures.sql.

  Idempotent: safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  SP_GRNMstRecalc  -  rebuild a GRN's totals from its lines

  Same contract as SP_POMstRecalc, with three additions a receipt has and an
  order does not:

      TDSAmt       withheld on the taxable value        (deducted)
      TCSAmt       collected on the taxed value         (added)
      Debit/Credit the gap between what the supplier billed (BillValue) and
                   what this GRN works out to (NetValue).  Supplier over-billed
                   -> DebitValue; under-billed -> CreditValue.  Never both.
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_GRNMstRecalc', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNMstRecalc AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNMstRecalc
    @MTransNo INT,
    @SubscID  INT
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH L AS
    (
        SELECT D.MTransNo,
               SUM(D.BasicValue - ISNULL(D.DiscAmt, 0))                       AS BasicValue,
               SUM(CASE WHEN ISNULL(D.GSTP, 0) = 0 THEN ISNULL(D.TaxableValue, 0) ELSE 0 END)
                                                                              AS ExemptValue,
               SUM(ISNULL(D.TaxableValue, 0))                                 AS TaxableValue,
               SUM(ISNULL(D.PnFAmt, 0))                                       AS PnFAmt,
               SUM(ISNULL(D.FreightAmt, 0))                                   AS FreightAmt,
               SUM(ISNULL(D.IGSTAmt, 0))                                      AS IGSTAmt,
               SUM(ISNULL(D.CGSTAmt, 0))                                      AS CGSTAmt,
               SUM(ISNULL(D.SGSTAmt, 0))                                      AS SGSTAmt
          FROM dbo.GRNDtl D
         WHERE D.MTransNo = @MTransNo AND D.DeleteStatus = 'N'
         GROUP BY D.MTransNo
    )
    UPDATE M
       SET BasicValue   = ISNULL(L.BasicValue, 0),
           ExemptValue  = ISNULL(L.ExemptValue, 0),
           TaxableValue = ISNULL(L.TaxableValue, 0),
           PnFAmt       = ISNULL(L.PnFAmt, 0),
           FreightAmt   = ISNULL(L.FreightAmt, 0),
           IGSTAmt      = ISNULL(L.IGSTAmt, 0),
           CGSTAmt      = ISNULL(L.CGSTAmt, 0),
           SGSTAmt      = ISNULL(L.SGSTAmt, 0),
           TDSAmt       = ROUND(ISNULL(L.TaxableValue, 0) * ISNULL(M.TDSP, 0) / 100.0, 2),
           TCSAmt       = ROUND((ISNULL(L.TaxableValue, 0) + ISNULL(L.IGSTAmt, 0)
                               + ISNULL(L.CGSTAmt, 0) + ISNULL(L.SGSTAmt, 0))
                               * ISNULL(M.TCSP, 0) / 100.0, 2)
      FROM dbo.GRNMst M
      LEFT JOIN L ON L.MTransNo = M.MTransNo
     WHERE M.MTransNo = @MTransNo AND M.SubscID = @SubscID;

    /*  Net, then the bill comparison, in two further passes: each one reads
        the column the pass before it settled, which a single UPDATE cannot do
        (every SET in one statement sees the OLD value).  */
    UPDATE dbo.GRNMst
       SET NetValue = ISNULL(TaxableValue, 0) + ISNULL(IGSTAmt, 0)
                    + ISNULL(CGSTAmt, 0) + ISNULL(SGSTAmt, 0)
                    + ISNULL(OtherAmt, 0) + ISNULL(TCSAmt, 0)
                    - ISNULL(TDSAmt, 0) + ISNULL(RoundOff, 0)
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

    UPDATE dbo.GRNMst
       SET DebitValue  = CASE WHEN ISNULL(BillValue, 0) > ISNULL(NetValue, 0)
                              THEN ISNULL(BillValue, 0) - ISNULL(NetValue, 0) ELSE 0 END,
           CreditValue = CASE WHEN ISNULL(NetValue, 0) > ISNULL(BillValue, 0)
                               AND ISNULL(BillValue, 0) > 0
                              THEN ISNULL(NetValue, 0) - ISNULL(BillValue, 0) ELSE 0 END,
           CurrencyAmount = CASE WHEN ISNULL(CurrencyRate, 0) > 0
                                 THEN ROUND(ISNULL(NetValue, 0) / CurrencyRate, 2)
                                 ELSE ISNULL(NetValue, 0) END
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID;
END
GO


/*==============================================================================
  SP_GRNMst  -  goods receipt note header

  @Status
      Insert   allocates DocNo within DocSeries + DocYear + SubscID.
               TargetType = 'PO' requires a current, approved purchase order.
      Update   header fields; refused once the GRN has been inspected.
      Delete   soft delete of the header and every child.
      Lock     LockStatus = 'Y' - receipt confirmed, ready for inspection.
      Unlock   back to draft; refused once inspected.
      Recalc   totals only.

  The vendor block is snapshotted from SupplierMaster - or, when the GRN is
  raised against a purchase order, copied from the order, so that the receipt
  shows the same supplier details the order was placed on.
==============================================================================*/
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


/*==============================================================================
  SP_GRNDtl  -  goods receipt lines: what was billed and what arrived

  @Status:  Insert / Update / Delete / DeleteAll

  @POTransSlNo names the PODtl line being received against.  From it the
  procedure takes the line number (GRNDtl.SlNo = PODtl.SlNo, which is the join
  the PO balance is computed over), the rate, the discount and packing
  percentages, the grade and the order quantity - so a receipt cannot quietly
  be booked at a rate the order does not carry.

  On a direct GRN, @POTransSlNo is 0 and the caller supplies the rate.

  Inspection quantities are NOT touched here.  A new line starts with
  AccQty = RejQty = RewQty = AODQty = 0 and only SP_GRNInspDtl ever changes them.
==============================================================================*/
IF OBJECT_ID(N'dbo.SP_GRNDtl', N'P') IS NULL
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


/*==============================================================================
  STORE ALLOCATION
==============================================================================*/

/*------------------------------------------------------------------------------
  SP_GRNStoreAlloc  -  put a received line away into a bin

  @Status
      Allocate     set Store and Location on one receipt line
      AllocateAll  set the same Store and Location on every unallocated line
                   of the GRN - the common case, where a whole delivery goes
                   to one place
      Clear        take the line back off the shelf

  The store and location are checked against StoreLocation, so a typo cannot
  create a bin that does not exist.  A StoreLocation row with PartNo = 0 is a
  bin any part may go to; a row naming the part is that part's own bin.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNStoreAlloc', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNStoreAlloc AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNStoreAlloc
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @Store          VARCHAR(50),
    @Location       VARCHAR(50),
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

        DECLARE @LinePartNo INT, @Affected INT;

        SET @Store    = NULLIF(LTRIM(RTRIM(ISNULL(@Store, ''))), '');
        SET @Location = NULLIF(LTRIM(RTRIM(ISNULL(@Location, ''))), '');

        IF NOT EXISTS (SELECT 1 FROM dbo.GRNMst
                        WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Status IN ('Allocate', 'AllocateAll')
        BEGIN
            IF @Store IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Store is required.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status = 'Allocate'
                SELECT @LinePartNo = PartNo FROM dbo.GRNDtl
                 WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
                   AND SubscID = @SubscID AND DeleteStatus = 'N';

            /*  The bin must exist: either as a general bin (PartNo = 0) or as
                one registered for this particular part.  */
            IF NOT EXISTS (SELECT 1 FROM dbo.StoreLocation
                            WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                              AND Store = @Store
                              AND (@Location IS NULL OR Location = @Location)
                              AND (PartNo = 0 OR @Status = 'AllocateAll' OR PartNo = @LinePartNo))
            BEGIN
                SET @ID = 0;
                SET @Message = 'Store / location "' + @Store + ISNULL(' / ' + @Location, '') +
                               '" is not on the store location master.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Allocate')
        BEGIN
            UPDATE dbo.GRNDtl
               SET Store = @Store, Location = @Location,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line allocated to ' + @Store + ISNULL(' / ' + @Location, '') + '.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Receipt line not found.'; END
        END

        ELSE IF (@Status = 'AllocateAll')
        BEGIN
            UPDATE dbo.GRNDtl
               SET Store = @Store, Location = @Location,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND NULLIF(LTRIM(RTRIM(ISNULL(Store, ''))), '') IS NULL;

            SET @Affected = @@ROWCOUNT;

            IF @Affected > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = CONVERT(VARCHAR(10), @Affected) + ' line(s) allocated to ' +
                               @Store + ISNULL(' / ' + @Location, '') + '.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Every line on this GRN is already allocated.'; END
        END

        ELSE IF (@Status = 'Clear')
        BEGIN
            UPDATE dbo.GRNDtl
               SET Store = NULL, Location = NULL,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Allocation cleared.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Receipt line not found.'; END
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
  SP_StoreLocation  -  the bin master
  @Status:  Insert / Update / Delete
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_StoreLocation', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_StoreLocation AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_StoreLocation
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @PartNo         INT,
    @Store          VARCHAR(50),
    @Location       VARCHAR(50),
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

        DECLARE @PartCode VARCHAR(30), @PartDesc VARCHAR(100), @SLNo NUMERIC(12,2);

        SET @PartNo   = ISNULL(@PartNo, 0);
        SET @Store    = LTRIM(RTRIM(ISNULL(@Store, '')));
        SET @Location = LTRIM(RTRIM(ISNULL(@Location, '')));

        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF @Store = ''
            BEGIN
                SET @ID = 0; SET @Message = 'Store is required.';
                COMMIT TRANSACTION; RETURN;
            END

            -- PartNo 0 is the "any part" bin; anything else must be a real part.
            IF @PartNo > 0
            BEGIN
                SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 30),
                       @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 100)
                  FROM dbo.PartMaster
                 WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

                IF @PartCode IS NULL
                BEGIN
                    SET @ID = 0; SET @Message = 'Part not found.';
                    COMMIT TRANSACTION; RETURN;
                END
            END

            IF EXISTS (SELECT 1 FROM dbo.StoreLocation
                        WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                          AND PartNo = @PartNo AND Store = @Store AND ISNULL(Location, '') = @Location
                          AND (@Status = 'Insert' OR MTransNo <> @MTransNo))
            BEGIN
                SET @ID = 0; SET @Message = 'That store location already exists.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SLNo = ISNULL(MAX(SLNo), 0) + 1
              FROM dbo.StoreLocation WHERE SubscID = @SubscID AND DeleteStatus = 'N';

            INSERT INTO dbo.StoreLocation
            (SLNo, PartNo, PartCode, PartDescription, Store, Location,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SLNo, @PartNo, @PartCode, @PartDesc, @Store, NULLIF(@Location, ''),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'Y', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Store location saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.StoreLocation
               SET PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   Store = @Store, Location = NULLIF(@Location, ''),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Store location updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.StoreLocation
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Store location deleted successfully.'; END
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


/*==============================================================================
  INWARD INSPECTION (PO)
==============================================================================*/

/*------------------------------------------------------------------------------
  SP_GRNNCDtl  -  non-conformance lines raised during inspection

  @Status:  Insert / Update / Delete / DeleteAll

  One row per defect found on a receipt line.  NCType is 'Reject', 'Rework' or
  'AOD'; the part, grade and unit are copied from the receipt line rather than
  taken from the caller, so an NC cannot describe a part that was not received.

  Total NCQty of a type may not exceed what was received on the line - there is
  no way to reject 12 of a delivery of 10.
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
             LEFT(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), 10), LTRIM(RTRIM(@NCRemarks)),
             @NCQty, @Unit, @GRNTransSlNo, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Non-conformance line saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNNCDtl
               SET NCType = @NCType,
                   HeatNo = LEFT(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), 10),
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


/*------------------------------------------------------------------------------
  SP_GRNInspDtl  -  record the inspection verdict on one receipt line

  Writes only AccQty / RejQty / RewQty / AODQty, InspRemarks and RejReason.
  Nothing else on the line is touched: this procedure belongs to the inspector,
  and the receipt belongs to stores.

  Refuses unless the four quantities add up to exactly what was received.  The
  NC-backing check is deliberately NOT made here - a line is often keyed before
  its non-conformance detail is - it is made once, for the whole GRN, at
  SP_GRNInspPost.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNInspDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNInspDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNInspDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @AccQty         NUMERIC(12,4),
    @RejQty         NUMERIC(12,4),
    @RewQty         NUMERIC(12,4),
    @AODQty         NUMERIC(12,4),
    @InspRemarks    VARCHAR(250),
    @RejReason      VARCHAR(250),
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

        DECLARE @RecQty NUMERIC(12,4), @GRNType CHAR(1), @Total NUMERIC(14,4);

        SELECT @GRNType = GRNType FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @GRNType IN ('Q', 'C')
        BEGIN
            SET @ID = 0;
            SET @Message = 'The inspection has already been posted. Reopen it to make changes.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @RecQty = RecQty FROM dbo.GRNDtl
         WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
           AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @RecQty IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Receipt line not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Clear')
        BEGIN
            UPDATE dbo.GRNDtl
               SET AccQty = 0, RejQty = 0, RewQty = 0, AODQty = 0,
                   InspRemarks = NULL, RejReason = NULL,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo AND SubscID = @SubscID;

            SET @ID = @TransSlNo; SET @Message = 'Inspection cleared for this line.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status <> 'Save')
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
            COMMIT TRANSACTION; RETURN;
        END

        SET @AccQty = ISNULL(@AccQty, 0);
        SET @RejQty = ISNULL(@RejQty, 0);
        SET @RewQty = ISNULL(@RewQty, 0);
        SET @AODQty = ISNULL(@AODQty, 0);
        SET @Total  = @AccQty + @RejQty + @RewQty + @AODQty;

        IF @AccQty < 0 OR @RejQty < 0 OR @RewQty < 0 OR @AODQty < 0
        BEGIN
            SET @ID = 0; SET @Message = 'Inspection quantities cannot be negative.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Total <> @RecQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Accepted + rejected + rework + AOD is ' + CONVERT(VARCHAR(20), @Total) +
                           ', but ' + CONVERT(VARCHAR(20), @RecQty) + ' was received. Every piece must be accounted for.';
            COMMIT TRANSACTION; RETURN;
        END

        UPDATE dbo.GRNDtl
           SET AccQty = @AccQty, RejQty = @RejQty, RewQty = @RewQty, AODQty = @AODQty,
               InspRemarks = LTRIM(RTRIM(@InspRemarks)),
               RejReason   = LTRIM(RTRIM(@RejReason)),
               SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
               ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
         WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
           AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @@ROWCOUNT > 0
        BEGIN SET @ID = @TransSlNo; SET @Message = 'Inspection saved for this line.'; END
        ELSE
        BEGIN SET @ID = 0; SET @Message = 'Receipt line not found.'; END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO


/*------------------------------------------------------------------------------
  SP_GRNInspPost  -  post the inward inspection

  @Status
      Post     allocate InspNo, stamp QCDate and QCverifyBy, set GRNType = 'Q'
      Reopen   unwind that, back to GRNType = 'N', so the verdict can be redone

  Post is where the whole GRN is checked, because only here is it certain that
  the inspector has finished:

      * every line must be inspected  (Acc + Rej + Rew + AOD = RecQty)
      * every non-zero Rej / Rew / AOD quantity must be backed by NC lines of
        the matching type whose NCQty adds up to exactly that quantity

  InspNo is allocated as the next number within DocSeries + DocYear + SubscID,
  the same way a document number is.  It is stored as text because the column
  is VARCHAR - it is compared numerically here so '10' sorts after '9'.
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
                @NextInsp  INT,
                @BadLine   NUMERIC(12,2),
                @BadType   VARCHAR(10),
                @BadQty    NUMERIC(14,4),
                @BadNC     NUMERIC(14,4);

        SELECT @GRNType = GRNType, @DocSeries = DocSeries, @DocYear = DocYear
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

            -- Rule 3: everything received has to be accounted for.
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

            /*  Rule 4: every non-conforming quantity must be explained by NC
                lines of the matching type, and they must add up.  */
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

            /*  Next inspection number in the series.  TRY_CONVERT keeps a
                hand-typed, non-numeric legacy value from breaking MAX; the
                IS NOT NULL keeps the un-inspected rows out of the aggregate
                rather than letting it warn about eliminating them.  */
            SELECT @NextInsp = ISNULL(MAX(TRY_CONVERT(INT, InspNo)), 0) + 1
              FROM dbo.GRNMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear
               AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND InspNo IS NOT NULL;

            UPDATE dbo.GRNMst
               SET InspNo     = CONVERT(VARCHAR(50), @NextInsp),
                   QCDate     = ISNULL(@QCDate, CONVERT(DATE, GETDATE())),
                   QCverifyBy = LTRIM(RTRIM(@QCverifyBy)),
                   GRNType    = 'Q',
                   SystemIP   = @SystemIP,
                   ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = 'Inspection posted. Inspection No. ' + CONVERT(VARCHAR(10), @NextInsp) + '.';
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

            UPDATE dbo.GRNMst
               SET GRNType    = 'N',
                   InspNo     = NULL,
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


/*==============================================================================
  INWARD INSPECTION (CLOSURE)
==============================================================================*/

/*------------------------------------------------------------------------------
  SP_GRNNCClose  -  close the non-conformance on an inspected GRN

  @Status
      Close    stamp NCCloseDate, NCRemarks, NCCloseUserNo and the attachment
               path, and move the GRN to GRNType = 'C'
      Reopen   back to 'Q', clearing the closure block

  Only an inspected GRN ('Q') that actually carries non-conformance can be
  closed - closing a clean receipt would be noise in the trail.

  @NCCloseAttachment is the stored path of the closure evidence.  The API
  writes the file and passes the path; no bytes go through this procedure.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNNCClose', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNNCClose AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNNCClose
    @ID                INT             OUTPUT,
    @MTransNo          INT,
    @NCCloseDate       DATE,
    @NCRemarks         VARCHAR(250),
    @NCCloseAttachment VARCHAR(100),
    @SystemIP          VARCHAR(20),
    @ModifiedBy        INT,
    @ModifiedOn        DATETIME2,
    @SubscID           INT,
    @Status            VARCHAR(20),
    @Message           NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @GRNType CHAR(1), @NCCount INT;

        SELECT @GRNType = GRNType FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Close')
        BEGIN
            IF @GRNType = 'N'
            BEGIN
                SET @ID = 0; SET @Message = 'Post the inward inspection before closing it.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @GRNType = 'C'
            BEGIN
                SET @ID = 0; SET @Message = 'The non-conformance on this GRN is already closed.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @NCCount = COUNT(*) FROM dbo.GRNNCDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @NCCount = 0
            BEGIN
                SET @ID = 0;
                SET @Message = 'There is no non-conformance on this GRN to close.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NULLIF(LTRIM(RTRIM(ISNULL(@NCRemarks, ''))), '') IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Closure remarks are required.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNMst
               SET NCCloseDate       = ISNULL(@NCCloseDate, CONVERT(DATE, GETDATE())),
                   NCRemarks         = LTRIM(RTRIM(@NCRemarks)),
                   NCCloseUserNo     = @ModifiedBy,
                   NCCloseAttachment = LTRIM(RTRIM(@NCCloseAttachment)),
                   GRNType           = 'C',
                   SystemIP          = @SystemIP,
                   ModifiedBy        = @ModifiedBy,
                   ModifiedOn        = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = 'Non-conformance closed against ' + CONVERT(VARCHAR(10), @NCCount) + ' line(s).';
        END

        ELSE IF (@Status = 'Reopen')
        BEGIN
            IF @GRNType <> 'C'
            BEGIN
                SET @ID = 0; SET @Message = 'The non-conformance on this GRN is not closed.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNMst
               SET NCCloseDate       = NULL,
                   NCCloseUserNo     = NULL,
                   NCCloseAttachment = NULL,
                   GRNType           = 'Q',
                   SystemIP          = @SystemIP,
                   ModifiedBy        = @ModifiedBy,
                   ModifiedOn        = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo; SET @Message = 'Closure reopened.';
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
  GRN QR CODES AND ATTACHMENTS
==============================================================================*/

/*------------------------------------------------------------------------------
  SP_GRNQRCode  -  one scannable label per piece, or per pack, of a receipt line

  @Status
      Generate  create @LQty labels for the line, numbered from the last one
                already issued, with QRCode built as
                    <GRN year>-<GRN no>-<line>-<serial>
      Delete    one label
      DeleteAll every label on the GRN
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNQRCode', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNQRCode AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNQRCode
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @GRNSlNo        INT,
    @LQty           INT,
    @Qty            NUMERIC(12,2),
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

        DECLARE @DocYear INT, @DocNo INT, @PartNo INT, @SlNo NUMERIC(12,2),
                @POTransNo INT, @OAYear INT, @OANo INT, @OAID INT,
                @Last INT, @i INT;

        IF (@Status = 'Generate')
        BEGIN
            SELECT @DocYear = GM.DocYear, @DocNo = GM.DocNo, @POTransNo = GM.TargetTransNo,
                   @PartNo = GD.PartNo, @SlNo = GD.SlNo,
                   @OAYear = GD.OAYear, @OANo = GD.OANo, @OAID = GD.OAID
              FROM dbo.GRNDtl GD
              JOIN dbo.GRNMst GM ON GM.MTransNo = GD.MTransNo
             WHERE GD.TransSlNo = @GRNSlNo AND GD.MTransNo = @MTransNo
               AND GD.SubscID = @SubscID AND GD.DeleteStatus = 'N';

            IF @PartNo IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Receipt line not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF ISNULL(@LQty, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Label count must be greater than zero.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @Last = ISNULL(MAX(LinkNo), 0)
              FROM dbo.GRNQRCode
             WHERE GRNSlNo = @GRNSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @i = 1;
            WHILE @i <= @LQty
            BEGIN
                INSERT INTO dbo.GRNQRCode
                (MTransNo, GRNSlNo, POTransNo, LinkNo, PartNo, Qty, LQty,
                 OAYear, OANo, OAID, QRCode, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, LockStatus, LockCounter, SubscID)
                VALUES
                (@MTransNo, @GRNSlNo, @POTransNo, @Last + @i, @PartNo, @Qty, @LQty,
                 @OAYear, @OANo, @OAID,
                 -- SlNo is numeric(12,2); via INT so the label reads
                 -- "2627-1-2-1" and not "2627-1-2.00-1".
                 CONVERT(VARCHAR(10), @DocYear) + '-' + CONVERT(VARCHAR(10), @DocNo) + '-' +
                 CONVERT(VARCHAR(20), CONVERT(INT, @SlNo)) + '-' + CONVERT(VARCHAR(10), @Last + @i),
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                 'N', 'N', 1, @SubscID);

                SET @i = @i + 1;
            END

            UPDATE dbo.GRNDtl SET ScanStatus = 'Y'
             WHERE TransSlNo = @GRNSlNo AND SubscID = @SubscID;

            SET @ID = @GRNSlNo;
            SET @Message = CONVERT(VARCHAR(10), @LQty) + ' label(s) generated.';
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.GRNQRCode
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Label deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.GRNQRCode
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All labels deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active labels found.'; END
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
  SP_GRNAttachment  -  supplier invoice, test certificate, photographs

  @Status:  Insert / Update / Delete / DeleteAll
  @AttFile is a stored path, not the file itself.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNAttachment', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNAttachment AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNAttachment
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @AttNotes       VARCHAR(250),
    @AttFile        VARCHAR(250),
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

        DECLARE @SlNo NUMERIC(6,2);

        IF @Status IN ('Insert', 'Update')
           AND NOT EXISTS (SELECT 1 FROM dbo.GRNMst
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.GRNAttachment WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.GRNAttachment
            (MTransNo, SlNo, AttNotes, AttFile, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@MTransNo, @SlNo, LTRIM(RTRIM(@AttNotes)), LTRIM(RTRIM(@AttFile)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Attachment saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNAttachment
               SET AttNotes = LTRIM(RTRIM(@AttNotes)),
                   AttFile  = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@AttFile, ''))), ''), AttFile),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Attachment updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.GRNAttachment
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Attachment deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.GRNAttachment
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All attachments deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active attachments found.'; END
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
  HEAT No. ENTRY
==============================================================================*/

/*------------------------------------------------------------------------------
  SP_HeatNoMst  -  heat document header, raised against a GRN

  @Status:  Insert / Update / Delete / Lock / Unlock

  The GRN named by @TargetTransNo must exist and must actually carry the part
  being recorded - a heat certificate for a part that did not arrive is a
  data-entry mistake, not a record.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_HeatNoMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @Qty            NUMERIC(12,6),
    @DocNotes       VARCHAR(250),
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

        DECLARE @TargetDocYear INT, @TargetDocNo INT,
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250),
                @Unit VARCHAR(20), @GradeNo INT, @DtlQty NUMERIC(14,4);

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @TargetDocYear = DocYear, @TargetDocNo = DocNo
              FROM dbo.GRNMst
             WHERE MTransNo = @TargetTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @TargetDocYear IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'GRN not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @TargetTransNo AND PartNo = @PartNo
                              AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'That part was not received on this GRN.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
                   @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
                   @Unit     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos')
              FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;

            SELECT @MCount = COUNT(MTransNo) FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Heat document already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.HeatNoMst
                (DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo,
                 PartNo, PartCode, PartDescription, GradeNo, Grade, Qty, Unit,
                 DocNotes, DocStatus, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                 @TargetTransNo, @TargetDocYear, @TargetDocNo,
                 @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
                 ISNULL(@Qty, 0), @Unit, LTRIM(RTRIM(@DocNotes)), 'N',
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                 'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Heat document saved successfully.';
            END
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            /*  The header quantity is what the heat lines have to add up to,
                so it cannot be pulled below what has already been entered.  */
            SELECT @DtlQty = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF ISNULL(@Qty, 0) < @DtlQty
            BEGIN
                SET @ID = 0;
                SET @Message = 'Quantity cannot be less than the ' + CONVERT(VARCHAR(20), @DtlQty) +
                               ' already entered against heat numbers.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.HeatNoMst
               SET DocDate = @DocDate, TargetTransNo = @TargetTransNo,
                   TargetDocYear = @TargetDocYear, TargetDocNo = @TargetDocNo,
                   PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   Qty = ISNULL(@Qty, 0), Unit = @Unit,
                   DocNotes = LTRIM(RTRIM(@DocNotes)),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockStatus = ISNULL(@LockStatus, LockStatus),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Heat document updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatNoDtl SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.HeatRefNo SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'Heat document deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status IN ('Lock', 'Unlock'))
        BEGIN
            IF @Status = 'Lock' AND NOT EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                                                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                                                   AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Enter at least one heat number before approving.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.HeatNoMst
               SET LockStatus = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                   DocStatus  = CASE WHEN @Status = 'Lock' THEN 'A' ELSE 'N' END,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = 'Heat document ' + CASE WHEN @Status = 'Lock' THEN 'approved' ELSE 'unlocked' END + ' successfully.';
            END
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


/*------------------------------------------------------------------------------
  SP_HeatNoDtl  -  one heat number, its test certificate, and the pieces it covers

  @Status:  Insert / Update / Delete / DeleteAll

  @RefFrom / @RefTo expand into HeatRefNo: the individual piece serial numbers
  this heat covers.  They are rewritten from scratch on every save of the line,
  which is what makes a correction to the range behave the way the user
  expects.  Pass 0 and 0 to record no serials at all.

  Two things are checked that the legacy application left to the operator:
      * the heat numbers on a document may not add up to more than the
        document's own quantity;
      * the same heat number cannot be recorded twice for the same part.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_HeatNoDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @HeatNo         VARCHAR(50),
    @TCNo           VARCHAR(30),
    @TCDate         DATE,
    @TCFile         VARCHAR(100),
    @Qty            NUMERIC(12,4),
    @ItemRemarks    VARCHAR(500),
    @RefFrom        INT,
    @RefTo          INT,
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

        DECLARE @HdrQty NUMERIC(12,6), @HdrLocked CHAR(1), @Other NUMERIC(14,4),
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250), @Unit VARCHAR(10),
                @GradeNo INT, @SlNo NUMERIC(12,2), @NewID INT, @i INT;

        SELECT @HdrQty = Qty, @HdrLocked = LockStatus
          FROM dbo.HeatNoMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @HdrQty IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @HdrLocked = 'Y'
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document is approved. Unlock it before changing heat numbers.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'All heat numbers deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active heat numbers found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE HTransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @TransSlNo; SET @Message = 'Heat number deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update
        ------------------------------------------------------------------*/
        SET @HeatNo = NULLIF(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), '');

        IF @HeatNo IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat number is required.';
            COMMIT TRANSACTION; RETURN;
        END

        IF ISNULL(@Qty, 0) <= 0
        BEGIN
            SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
               @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
               @Unit     = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos'), 10)
          FROM dbo.PartMaster
         WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

        IF @PartCode IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Part not found.';
            COMMIT TRANSACTION; RETURN;
        END

        -- The same heat number cannot describe the same part twice.
        IF EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                      AND HeatNo = @HeatNo AND PartNo = @PartNo
                      AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo))
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat number ' + @HeatNo + ' has already been recorded for this part.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @Other = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
           AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo);

        IF @Other + @Qty > @HdrQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat quantities would total ' + CONVERT(VARCHAR(20), @Other + @Qty) +
                           ', more than the ' + CONVERT(VARCHAR(20), @HdrQty) + ' on the document.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
         WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.HeatNoDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.HeatNoDtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, GradeNo, Grade,
             ItemRemarks, Qty, Unit, HeatNo, TCNo, TCDate, TCFile,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
             LTRIM(RTRIM(@ItemRemarks)), @Qty, @Unit, @HeatNo,
             LTRIM(RTRIM(@TCNo)), @TCDate, LTRIM(RTRIM(@TCFile)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @NewID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Heat number saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   ItemRemarks = LTRIM(RTRIM(@ItemRemarks)), Qty = @Qty, Unit = @Unit,
                   HeatNo = @HeatNo, TCNo = LTRIM(RTRIM(@TCNo)), TCDate = @TCDate,
                   TCFile = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@TCFile, ''))), ''), TCFile),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @NewID = @TransSlNo;
            SET @Message = 'Heat number updated successfully.';
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Piece serial numbers: rewritten whole, so an edited range replaces
          the old one instead of merging with it.
        ------------------------------------------------------------------*/
        DELETE FROM dbo.HeatRefNo
         WHERE MTransNo = @MTransNo AND HTransSlNo = @NewID AND SubscID = @SubscID;

        IF ISNULL(@RefFrom, 0) > 0 AND ISNULL(@RefTo, 0) >= @RefFrom
        BEGIN
            IF (@RefTo - @RefFrom) > 10000
            BEGIN
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                SET @ID = 0;
                SET @Message = 'That reference range covers more than 10,000 pieces. Split it across heat lines.';
                RETURN;
            END

            SET @i = @RefFrom;
            WHILE @i <= @RefTo
            BEGIN
                INSERT INTO dbo.HeatRefNo
                (MTransNo, HTransSlNo, RefNo, PartNo, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, LockStatus, LockCounter, SubscID)
                VALUES
                (@MTransNo, @NewID, @i, @PartNo, @SystemIP, @CreatedBy, GETDATE(),
                 'N', 'N', 1, @SubscID);

                SET @i = @i + 1;
            END
        END

        SET @ID = @NewID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO


PRINT '15_GRNProcedures.sql completed.';
GO
