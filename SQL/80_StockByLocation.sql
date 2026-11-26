/* ============================================================================
   80_StockByLocation.sql  (idempotent)

   Stock per bin (store + location). GRN Store Allocation and Opening Stock
   already record the bin goods are put in (Store, Location on the StoreLocation
   master), but Store Issue and Issue Return did not, so the stock view had no
   location on the way out and no bin balance could be worked out.

   Now:
     * StoreIssueDtl.Store / Location   - the bin the bar is taken from.
     * StoreReturnsDtl.Store / Location - the bin the unused bar is put back in.
     * VW_StoreStock ISSUE and RETURN rows carry them.
     * VW_StockByLocation - balance per part + grade + bin. Movements with no
       bin (older lines, production, sales) fall in the bin '' (shown as
       "No bin").
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.StoreIssueDtl', 'Store') IS NULL ALTER TABLE dbo.StoreIssueDtl ADD Store VARCHAR(50) NULL;
IF COL_LENGTH('dbo.StoreIssueDtl', 'Location') IS NULL ALTER TABLE dbo.StoreIssueDtl ADD Location VARCHAR(50) NULL;
IF COL_LENGTH('dbo.StoreReturnsDtl', 'Store') IS NULL ALTER TABLE dbo.StoreReturnsDtl ADD Store VARCHAR(50) NULL;
IF COL_LENGTH('dbo.StoreReturnsDtl', 'Location') IS NULL ALTER TABLE dbo.StoreReturnsDtl ADD Location VARCHAR(50) NULL;
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
        D.Store, D.Location,                       -- SQL 80: the bin it was put back in
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
        D.Store, D.Location,                       -- SQL 80: the bin it was taken from
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

/* Balance per part + grade + bin (own stock). */
CREATE OR ALTER VIEW dbo.VW_StockByLocation
AS
SELECT  s.SubscID, s.PartNo,
        LTRIM(RTRIM(ISNULL(s.Grade, '')))      AS Grade,
        LTRIM(RTRIM(ISNULL(s.Store, '')))      AS Store,
        LTRIM(RTRIM(ISNULL(s.Location, '')))   AS Location,
        CAST(SUM(ISNULL(s.InQty, 0) - ISNULL(s.OutQty, 0)) AS NUMERIC(14,4)) AS BalanceQty,
        MAX(s.DocDate)                         AS LastMoveDate
  FROM  dbo.VW_StoreStock s
 GROUP BY s.SubscID, s.PartNo, LTRIM(RTRIM(ISNULL(s.Grade, ''))),
          LTRIM(RTRIM(ISNULL(s.Store, ''))), LTRIM(RTRIM(ISNULL(s.Location, '')));
GO

/* One-time backfill of older lines (only lines with no bin yet):
   an issue line takes the bin when its part + grade was received into
   exactly one bin (GRN / Opening Stock); a return line takes its issue
   line's bin. Lines of parts kept in several bins stay "No bin". */
;WITH bins AS (
    SELECT s.SubscID, s.PartNo, LTRIM(RTRIM(ISNULL(s.Grade, ''))) AS Grade,
           MIN(LTRIM(RTRIM(s.Store))) AS Store, MIN(LTRIM(RTRIM(ISNULL(s.Location, '')))) AS Location,
           COUNT(DISTINCT CONCAT(LTRIM(RTRIM(s.Store)), '|', LTRIM(RTRIM(ISNULL(s.Location, ''))))) AS BinCount
      FROM dbo.VW_StoreStock s
     WHERE s.TxnType IN ('GRN', 'OPENING') AND NULLIF(LTRIM(RTRIM(s.Store)), '') IS NOT NULL
     GROUP BY s.SubscID, s.PartNo, LTRIM(RTRIM(ISNULL(s.Grade, ''))))
UPDATE d
   SET d.Store = b.Store, d.Location = NULLIF(b.Location, '')
  FROM dbo.StoreIssueDtl d
  JOIN bins b ON b.SubscID = d.SubscID AND b.PartNo = d.PartNo AND b.Grade = LTRIM(RTRIM(ISNULL(d.Grade, ''))) AND b.BinCount = 1
 WHERE NULLIF(LTRIM(RTRIM(d.Store)), '') IS NULL AND ISNULL(d.MaterialSource, 'O') <> 'C';
GO
UPDATE r
   SET r.Store = i.Store, r.Location = i.Location
  FROM dbo.StoreReturnsDtl r
  JOIN dbo.StoreIssueDtl i ON i.TransSlNo = r.LinkNo
 WHERE NULLIF(LTRIM(RTRIM(r.Store)), '') IS NULL AND NULLIF(LTRIM(RTRIM(i.Store)), '') IS NOT NULL;
GO
