/*==============================================================================
  73. FINISHED GOODS IN STOCK (forgings)

  Separate from 72 so it can be left out: it only changes finished goods.

  Before this script finished forgings never came INTO stock — only the sales
  invoice took them out — and the invoice line carried no grade, so a grade's
  stock never went down. The Stock Reports therefore showed forgings negative.

  VW_StoreStock (same as SQL 72 otherwise):
    A. 'PRODUCTION' rows: the OK qty of every job card inspection is a receipt
       of the job card's part + grade, on the inspection date.
    B. 'SALES' rows take the grade of the OA line the invoice line bills
       (OADtl.MocCode, as stored).

  Who is affected: anything that reads this view for a FINISHED part — Stock
  Reports and RM Stock Statement. Store Issue never issues forgings, and
  RM Requirement / the plan read bar, so their figures do not change. A raw
  material sold on an invoice now reduces the stock of its grade (before, it
  reduced a blank grade nothing matched).

  ORDER: run AFTER 72 (this re-creates the view from 72's version).
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 73_FinishedGoodsStock.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.OpeningStockMst', N'U') IS NULL
    RAISERROR('Run 72_OpeningStock.sql first.', 16, 1);
GO

ALTER VIEW dbo.VW_StoreStock
AS
SELECT  CAST('GRN' AS VARCHAR(10))                 AS TxnType,
        M.MTransNo                                 AS RefTransNo,
        D.TransSlNo                                AS RefTransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo,
        D.GradeNo,
        ISNULL(LTRIM(RTRIM(D.Grade)), '')          AS Grade,
        D.Store, D.Location,
        CAST(ISNULL(D.AccQty, 0) + ISNULL(D.AODQty, 0) AS NUMERIC(14,4)) AS InQty,
        CAST(0 AS NUMERIC(14,4))                   AS OutQty,
        M.SubscID
  FROM  dbo.GRNMst M
  JOIN  dbo.GRNDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  M.LockStatus = 'Y'
   AND  M.GRNType IN ('Q', 'C')
UNION ALL
-- SQL 72: approved opening stock (own) — the balance at the start of its As On date
SELECT  'OPENING', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, CAST(M.AsOnDate AS DATETIME),
        D.PartNo, D.GradeNo, ISNULL(LTRIM(RTRIM(D.Grade)), ''),
        D.Store, D.Location,
        CAST(D.Qty AS NUMERIC(14,4)), CAST(0 AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.OpeningStockMst M
  JOIN  dbo.OpeningStockDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  M.LockStatus = 'Y' AND M.StockType = 'OWN'
UNION ALL
SELECT  'RETURN', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, D.GradeNo, ISNULL(LTRIM(RTRIM(D.Grade)), ''),
        NULL, NULL,
        CAST(D.RTNQty AS NUMERIC(14,4)), CAST(0 AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.StoreReturnsMst M
  JOIN  dbo.StoreReturnsDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   -- customer bar coming back from the shop goes to the customer, not to us
   AND  NOT EXISTS (SELECT 1 FROM dbo.StoreIssueDtl si
                     WHERE si.TransSlNo = D.LinkNo AND si.MaterialSource = 'C')
UNION ALL
SELECT  'ISSUE', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, D.GradeNo, ISNULL(LTRIM(RTRIM(D.Grade)), ''),
        NULL, NULL,
        CAST(0 AS NUMERIC(14,4)), CAST(D.Qty AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.StoreIssueMst M
  JOIN  dbo.StoreIssueDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  D.MaterialSource <> 'C'                 -- customer bar is not our stock
UNION ALL
SELECT  'SALES', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, NULL,
        -- SQL 73: the grade of the OA line the invoice line bills, spelt as the
        -- OA (and so the GRN) holds it
        ISNULL(LTRIM(RTRIM(OD.MocCode)), ''),
        NULL, NULL,
        CAST(0 AS NUMERIC(14,4)), CAST(ISNULL(D.Qty, 0) AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.STxnMst M
  JOIN  dbo.STxnDtl D ON D.MTransNo = M.MTransNo
  LEFT  JOIN dbo.OADtl OD ON OD.TransSlNo = D.LinkNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
UNION ALL
-- SQL 73: forgings into stock — the OK qty of each job card inspection
SELECT  'PRODUCTION', I.MTransNo, I.MTransNo,
        CAST(NULL AS CHAR(10)), I.DocYear, I.DocNo, CAST(I.InspDate AS DATETIME),
        J.PartNo, J.GradeNo, ISNULL(LTRIM(RTRIM(J.Grade)), ''),
        NULL, NULL,
        CAST(ISNULL(I.OKQty, 0) AS NUMERIC(14,4)), CAST(0 AS NUMERIC(14,4)),
        I.SubscID
  FROM  dbo.JobCardInspection I
  JOIN  dbo.JobCardMst J ON J.MTransNo = I.JobCardTransNo AND J.DeleteStatus = 'N'
 WHERE  I.DeleteStatus = 'N' AND ISNULL(I.OKQty, 0) > 0;
GO

PRINT '73_FinishedGoodsStock applied.';
GO
