/* ============================================================================
   78_ReturnsIntoBarStock.sql  (idempotent)

   Issue Return has three return types: Unused, End Piece and Scrap.
   Before this script all three went back into bar stock (VW_StoreStock), so
   scrap and short end pieces showed up as "available bar" for Store Issue,
   RM Requirement, the Production Plan and the stock reports.

   Now (normal forging practice):
     * Unused     -> back into bar stock (VW_StoreStock RETURN rows), as before.
     * End Piece  -> kept apart as end-piece stock (VW_OffcutStock), not bar.
     * Scrap      -> kept apart as scrap stock (VW_OffcutStock), not bar.
   A blank return type counts as Unused (older rows).
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
CREATE OR ALTER VIEW dbo.VW_StoreStock
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
-- SQL 72: approved opening stock (own) - the balance at the start of its As On date
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
   -- SQL 78: only Unused bar goes back to bar stock. End Piece and Scrap
   -- are kept apart (dbo.VW_OffcutStock) and never count as bar.
   AND  ISNULL(NULLIF(LTRIM(RTRIM(D.ReturnType)), ''), 'Unused') = 'Unused'
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
-- SQL 73: forgings into stock - the OK qty of each job card inspection
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

/* End pieces and scrap returned from the shop floor, by part + grade + heat.
   Kind = 'End Piece' or 'Scrap'. Qty is in the bar's stock unit (kg). */
CREATE OR ALTER VIEW dbo.VW_OffcutStock
AS
SELECT  CASE WHEN LTRIM(RTRIM(D.ReturnType)) = 'Scrap' THEN 'Scrap' ELSE 'End Piece' END AS Kind,
        M.MTransNo                                 AS RefTransNo,
        D.TransSlNo                                AS RefTransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, D.GradeNo,
        ISNULL(LTRIM(RTRIM(D.Grade)), '')          AS Grade,
        ISNULL(LTRIM(RTRIM(SI.HeatNo)), '')        AS HeatNo,
        CAST(D.RTNQty AS NUMERIC(14,4))            AS Qty,
        M.SubscID
  FROM  dbo.StoreReturnsMst M
  JOIN  dbo.StoreReturnsDtl D ON D.MTransNo = M.MTransNo
  LEFT  JOIN dbo.StoreIssueDtl SI ON SI.TransSlNo = D.LinkNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  LTRIM(RTRIM(D.ReturnType)) IN ('End Piece', 'Scrap')
   AND  ISNULL(SI.MaterialSource, 'O') <> 'C';
GO
