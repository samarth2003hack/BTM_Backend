/*==============================================================================
  46. STORE ISSUE / STORE RETURNS  -  stock view and balance views

  Backing for EMSSoft/Modules/Purchase/StoreIssue.html  (api/StoreIssue)
          and EMSSoft/Modules/Purchase/IssueReturn.html (api/StoreReturns).
  Tables and save procedures come from 44_StoresIssueTables.sql.

  VW_StoreStock          one movement row per stock-affecting line
      IN   'GRN'         approved (LockStatus 'Y'), INSPECTED (GRNType 'Q'/'C')
                         GRN lines: AccQty + AODQty (accepted + accepted on
                         deviation). Before inspection AccQty is 0, so an
                         uninspected receipt is not issuable stock yet.
      IN   'RETURN'      live store returns (RTNQty)
      OUT  'ISSUE'       live store issues (Qty)
      OUT  'SALES'       live sales invoice lines (STxnDtl.Qty). Invoice lines
                         carry no grade, so they reduce the ungraded ('') stock
                         of a part only - as in the reference Stock view.
      Grade is the trimmed grade text, '' when blank - stock is per
      PartNo + Grade. RefTransNo / RefTransSlNo identify the source row so a
      caller can leave out the document being edited.

  VW_StoreIssueMRNLine   approved, live MRN lines (PROD / GEN) with the
                         quantity already issued against each on live issues
                         and the balance (Qty - IssuedQty). Read only - MRN
                         tables belong to the MRN pages.
  VW_StoreIssueLineBal   live store issue lines with the quantity returned
                         against each on live returns, and the balance.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO
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
SELECT  'RETURN', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, D.GradeNo, ISNULL(LTRIM(RTRIM(D.Grade)), ''),
        NULL, NULL,
        CAST(D.RTNQty AS NUMERIC(14,4)), CAST(0 AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.StoreReturnsMst M
  JOIN  dbo.StoreReturnsDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
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
UNION ALL
SELECT  'SALES', M.MTransNo, D.TransSlNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        D.PartNo, NULL, '',
        NULL, NULL,
        CAST(0 AS NUMERIC(14,4)), CAST(ISNULL(D.Qty, 0) AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.STxnMst M
  JOIN  dbo.STxnDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N';
GO

CREATE OR ALTER VIEW dbo.VW_StoreIssueMRNLine
AS
SELECT  M.MTransNo,
        M.MRNType,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        M.Requiredby, M.Department,
        CAST(M.SubscID AS INT)                     AS SubscID,
        D.TransSlNo                                AS LinkNo,
        D.SlNo,
        D.PartNo,
        P.PartCode,
        COALESCE(NULLIF(LTRIM(RTRIM(D.Description)), ''), P.PartName) AS PartName,
        D.GradeNo,
        ISNULL(LTRIM(RTRIM(D.Grade)), '')          AS Grade,
        D.OAYear, D.OANo, D.OAID,
        COALESCE(NULLIF(LTRIM(RTRIM(D.Unit)), ''), P.PrimaryUnit) AS Unit,
        D.ItemRemarks,
        CAST(ISNULL(D.Qty, 0) AS NUMERIC(14,4))    AS Qty,
        CAST(ISNULL(I.IssuedQty, 0) AS NUMERIC(14,4)) AS IssuedQty,
        CAST(ISNULL(D.Qty, 0) - ISNULL(I.IssuedQty, 0) AS NUMERIC(14,4)) AS BalQty
  FROM  dbo.MRNMst M
  JOIN  dbo.MRNDtl D ON D.MTransNo = M.MTransNo
  LEFT JOIN dbo.PartMaster P ON P.MTransNo = D.PartNo
  OUTER APPLY (SELECT SUM(SD.Qty) AS IssuedQty
                 FROM dbo.StoreIssueDtl SD
                 JOIN dbo.StoreIssueMst SM ON SM.MTransNo = SD.MTransNo
                WHERE SD.LinkNo = D.TransSlNo
                  AND SM.TargetTransNo = M.MTransNo
                  AND SD.DeleteStatus = 'N' AND SM.DeleteStatus = 'N') I
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  M.LockStatus = 'Y'
   AND  M.MRNType IN ('PROD', 'GEN');
GO

CREATE OR ALTER VIEW dbo.VW_StoreIssueLineBal
AS
SELECT  M.MTransNo,
        M.DocSeries, M.DocYear, M.DocNo, M.DocDate,
        M.TargetType, M.IndentorName, M.Department, M.LockStatus,
        M.SubscID,
        D.TransSlNo                                AS LinkNo,
        D.SlNo,
        D.PartNo, D.PartCode, D.PartDescription    AS PartName,
        D.GradeNo,
        ISNULL(LTRIM(RTRIM(D.Grade)), '')          AS Grade,
        D.OAYear, D.OANo, D.OAID,
        D.Unit,
        CAST(D.Qty AS NUMERIC(14,4))               AS Qty,
        CAST(ISNULL(R.ReturnedQty, 0) AS NUMERIC(14,4)) AS ReturnedQty,
        CAST(D.Qty - ISNULL(R.ReturnedQty, 0) AS NUMERIC(14,4)) AS BalQty
  FROM  dbo.StoreIssueMst M
  JOIN  dbo.StoreIssueDtl D ON D.MTransNo = M.MTransNo
  OUTER APPLY (SELECT SUM(RD.RTNQty) AS ReturnedQty
                 FROM dbo.StoreReturnsDtl RD
                 JOIN dbo.StoreReturnsMst RM ON RM.MTransNo = RD.MTransNo
                WHERE RD.LinkNo = D.TransSlNo
                  AND RM.TargetTransNo = M.MTransNo
                  AND RD.DeleteStatus = 'N' AND RM.DeleteStatus = 'N') R
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N';
GO

PRINT '46_StoreIssueReturns applied.';
GO
