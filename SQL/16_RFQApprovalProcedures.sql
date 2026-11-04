/*==============================================================================
  16. RFQ APPROVAL  -  VIEW + STORED PROCEDURE

  Run 12_PurchaseValidTill.sql and 14_PurchaseOrderProcedures.sql first.

  ----------------------------------------------------------------------------
  WHERE THIS SITS IN THE PURCHASE CHAIN
  ----------------------------------------------------------------------------

      1. Purchase Requisition      PurReqMst     LockStatus = 'Y'  (approved)
               |
      2. Request For Quote         RFQMst        raised against the PR
               |
      3. RFQ approved              RFQMst        LockStatus = 'Y'
               |                                 -> only now may suppliers be
               |                                    asked to quote
      4. Supplier Price Update     RFQPriceMst   one row per invited supplier
               |
      5. Price Comparison          read-only     the quotes side by side
               |
      6. RFQ Approval  <- THIS     one supplier's quotation wins

  ----------------------------------------------------------------------------
  THE TWO FLAGS, AND WHY THEY ARE NOT THE SAME FLAG
  ----------------------------------------------------------------------------

  RFQMst carries both LockStatus and DocStatus, and this chain uses them for
  two genuinely different questions:

      LockStatus = 'Y'   the RFQ document itself is approved (step 3), so it
                         may be sent out and quoted against.
      DocStatus  = 'Y'   the RFQ is CLOSED - a winner has been picked (step 6)
                         and nothing further will be quoted against it.

  So step 6 sets DocStatus only.  It must NOT touch LockStatus: that was
  already set in step 3, and clearing it would retract the approval that let
  the suppliers quote in the first place.

  On the quotation side, RFQPriceMst uses the pair the same way round:

      LockStatus = 'Y'   this is the WINNING quotation.  VW_QuoteForPO offers
                         exactly these rows to the Purchase Order screen, so
                         this flag is what turns a quote into an orderable one.
      DocStatus  = 'N'   still open - the winner stays open because a PO has
                         yet to be raised from it.
      DocStatus  = 'Y'   closed - every quotation that did not win, so it drops
                         out of the buyer's way for good.

  One RFQ therefore ends up with exactly one RFQPriceMst row at
  (LockStatus 'Y', DocStatus 'N') and every other row at ('N', 'Y').
==============================================================================*/


/*------------------------------------------------------------------------------
  VW_RFQApprovalList  -  the rows on the RFQ Approval and Price Comparison grids

  One row per live RFQ that has been approved and has at least one quotation
  against it.  Approved RFQs stay in the list rather than disappearing: the
  screen reopens them read-only as a record of the decision, which is why the
  winning supplier travels with the row.

  ApprovedOn / ApprovedBy come off the winning quotation rather than off a
  column of their own - SP_RFQApproval stamps ModifiedBy/ModifiedOn onto that
  row at the moment it locks it, so it already records who decided and when.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.VW_RFQApprovalList', N'V') IS NOT NULL
    DROP VIEW dbo.VW_RFQApprovalList;
GO

CREATE VIEW dbo.VW_RFQApprovalList
AS
SELECT  RQ.MTransNo,
        RQ.DocSeries,
        RQ.DocYear,
        RQ.DocNo,
        RQ.DocDate,
        RQ.ValidTill,
        RQ.DocAmdNo,
        RQ.DocNotes,
        RQ.PRTransNo,
        PR.DocYear              AS PRYear,
        PR.DocNo                AS PRNo,
        Q.QuoteCount,
        -- 'Y' once a winner has been picked; the grid's Status column.
        RQ.DocStatus,
        -- The step-3 approval that let the suppliers quote at all.
        RQ.LockStatus,
        ISNULL(W.MTransNo, 0)   AS ApprovedQuoteNo,
        W.VendorNo              AS ApprovedVendorNo,
        W.VendorName            AS ApprovedVendorName,
        W.ModifiedOn            AS ApprovedOn,
        W.ModifiedBy            AS ApprovedBy,
        RQ.SubscID
  FROM  dbo.RFQMst RQ
  LEFT  JOIN dbo.PurReqMst PR
         ON  PR.MTransNo = RQ.PRTransNo
 CROSS APPLY
       (
           SELECT COUNT(*) AS QuoteCount
             FROM dbo.RFQPriceMst QP
            WHERE QP.TargetTransNo = RQ.MTransNo
              AND QP.DeleteStatus  = 'N'
              AND QP.DocAmdStatus  = 'N'
       ) Q
 OUTER APPLY
       (
           SELECT TOP (1)
                  QP.MTransNo, QP.VendorNo, QP.VendorName,
                  QP.ModifiedOn, QP.ModifiedBy
             FROM dbo.RFQPriceMst QP
            WHERE QP.TargetTransNo = RQ.MTransNo
              AND QP.DeleteStatus  = 'N'
              AND QP.DocAmdStatus  = 'N'
              AND QP.LockStatus    = 'Y'
            ORDER BY QP.MTransNo DESC
       ) W
 WHERE  RQ.DeleteStatus = 'N'
   AND  RQ.DocAmdStatus = 'N'
   AND  RQ.LockStatus   = 'Y'   -- step 3 done: the RFQ was approved
   AND  Q.QuoteCount    > 0;    -- and somebody actually quoted
GO


/*------------------------------------------------------------------------------
  SP_RFQApproval  -  pick the winning quotation for one RFQ

      @Status = 'Approve'   lock the chosen quotation, close the rest, close
                            the RFQ.  All three in one transaction: a half-
                            applied approval would leave either two winners or
                            none, and VW_QuoteForPO would offer the wrong rows
                            to the Purchase Order screen.

  Created as a stub first and then ALTERed, rather than CREATE OR ALTER.  An
  older script seeded SP_ procedures into master without a USE statement, and
  CREATE OR ALTER resolves to those instead of to the copy in this database.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_RFQApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RFQApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RFQApproval
    @ID                 INT             OUTPUT,
    @MTransNo           INT,            -- RFQMst.MTransNo being approved
    @ApprovedQuoteNo    INT,            -- RFQPriceMst.MTransNo of the winner
    @SystemIP           VARCHAR(20),
    @ModifiedBy         INT,
    @ModifiedOn         DATETIME2,
    @SubscID            INT,
    @Status             VARCHAR(20),
    @Message            NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    SET @ID      = 0;
    SET @Message = N'';

    BEGIN TRY
        IF (@Status <> 'Approve')
        BEGIN
            SET @Message = N'Unsupported status.';
            RETURN;
        END

        BEGIN TRANSACTION;

        DECLARE @RFQDocStatus  CHAR(1),
                @RFQLockStatus CHAR(1);

        -- The RFQ must be the live revision, approved in step 3, and still open.
        SELECT  @RFQDocStatus  = DocStatus,
                @RFQLockStatus = LockStatus
          FROM  dbo.RFQMst WITH (UPDLOCK, HOLDLOCK)
         WHERE  MTransNo     = @MTransNo
           AND  SubscID      = @SubscID
           AND  DeleteStatus = 'N'
           AND  DocAmdStatus = 'N';

        IF (@RFQDocStatus IS NULL)
        BEGIN
            SET @Message = N'RFQ not found.';
            ROLLBACK TRANSACTION;
            RETURN;
        END

        IF (@RFQLockStatus <> 'Y')
        BEGIN
            SET @Message = N'This RFQ has not been approved yet, so its quotations cannot be approved.';
            ROLLBACK TRANSACTION;
            RETURN;
        END

        IF (@RFQDocStatus = 'Y')
        BEGIN
            SET @Message = N'This RFQ is already closed against an approved supplier.';
            ROLLBACK TRANSACTION;
            RETURN;
        END

        -- The winning quotation must belong to THIS RFQ and be the live revision.
        IF NOT EXISTS (
            SELECT 1
              FROM dbo.RFQPriceMst
             WHERE MTransNo      = @ApprovedQuoteNo
               AND TargetTransNo = @MTransNo
               AND SubscID       = @SubscID
               AND DeleteStatus  = 'N'
               AND DocAmdStatus  = 'N')
        BEGIN
            SET @Message = N'The selected quotation does not belong to this RFQ.';
            ROLLBACK TRANSACTION;
            RETURN;
        END

        -- 1. Every quotation that did NOT win: unlocked and closed.
        UPDATE  dbo.RFQPriceMst
           SET  LockStatus  = 'N',
                DocStatus   = 'Y',
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
         WHERE  TargetTransNo = @MTransNo
           AND  SubscID       = @SubscID
           AND  DeleteStatus  = 'N'
           AND  MTransNo     <> @ApprovedQuoteNo;

        -- 2. The winner: locked (this is what VW_QuoteForPO offers to the PO
        --    screen) and left OPEN, because the PO still has to be raised.
        UPDATE  dbo.RFQPriceMst
           SET  LockStatus  = 'Y',
                DocStatus   = 'N',
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
         WHERE  MTransNo = @ApprovedQuoteNo
           AND  SubscID  = @SubscID;

        -- 3. The RFQ itself: closed.  LockStatus is deliberately untouched -
        --    it still records the step-3 approval.
        UPDATE  dbo.RFQMst
           SET  DocStatus   = 'Y',
                SystemIP    = @SystemIP,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
         WHERE  MTransNo = @MTransNo
           AND  SubscID  = @SubscID;

        COMMIT TRANSACTION;

        SET @ID      = @MTransNo;
        SET @Message = N'RFQ approved successfully.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
