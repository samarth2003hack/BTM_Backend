/*==============================================================================
  56. STORE ISSUE AGAINST A JOB CARD  -  Modules/Store/StoreIssue.html

  Stores can now issue a job card's raw material, as the legacy screen did
  ("Job Card" issue type), alongside Production MRN and General MRN.

      StoreIssueMst.TargetType    = 'Job Card'
      StoreIssueMst.TargetTransNo = JobCardMst.MTransNo
      StoreIssueMst.TargetDocYear / TargetDocNo = JobCardMst.DocYear / DocNo
      StoreIssueDtl.LinkNo        = JobCardMst.MTransNo  (one RM line per job card)

  VW_StoreIssueMRNLine (SQL 46) gains the job card RM line as source type
  'JC': part / grade / unit of the job card's raw material, Qty = RMQty,
  balance = RMQty less live issues against the job card. Every live job card
  is offered while it has a balance (no approval step on job cards).
  Stock in hand is checked exactly as for MRN lines.

  Job card ids and MRN ids come from different tables and can be equal, so
  the MRN side now ignores 'Job Card' issues everywhere it matches issues
  back to MRNs (the view's issued qty, FN_MRNLineIssued, FN_MRNIssueCount).
  VW_JobCardSummary (SQL 53) reads the job card side (RMIssuedQty).

  Requires SQL 46 and SQL 53. Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. VW_StoreIssueMRNLine - approved MRN lines + live job card RM lines
------------------------------------------------------------------------------*/
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
                  AND ISNULL(SM.TargetType, '') <> 'Job Card'
                  AND SD.DeleteStatus = 'N' AND SM.DeleteStatus = 'N') I
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N'
   AND  M.LockStatus = 'Y'
   AND  M.MRNType IN ('PROD', 'GEN')

UNION ALL

SELECT  J.MTransNo,
        CAST('JC' AS VARCHAR(30))                  AS MRNType,
        CAST('JC' AS CHAR(10))                     AS DocSeries,
        J.DocYear, J.DocNo, J.JobDate              AS DocDate,
        CAST(J.Operator AS VARCHAR(50))            AS Requiredby,
        CAST('Production' AS VARCHAR(50))          AS Department,
        J.SubscID,
        J.MTransNo                                 AS LinkNo,
        1                                          AS SlNo,
        J.RMPartNo                                 AS PartNo,
        P.PartCode,
        P.PartName,
        J.RMGradeNo                                AS GradeNo,
        ISNULL(LTRIM(RTRIM(J.RMGrade)), '')        AS Grade,
        J.OAYear, J.OANo, J.OAID,
        COALESCE(NULLIF(LTRIM(RTRIM(J.RMUnit)), ''), P.PrimaryUnit) AS Unit,
        CAST(CONCAT('Job Card ', J.JobCardNo) AS VARCHAR(250)) AS ItemRemarks,
        CAST(J.RMQty AS NUMERIC(14,4))             AS Qty,
        CAST(ISNULL(I.IssuedQty, 0) AS NUMERIC(14,4)) AS IssuedQty,
        CAST(J.RMQty - ISNULL(I.IssuedQty, 0) AS NUMERIC(14,4)) AS BalQty
  FROM  dbo.JobCardMst J
  LEFT JOIN dbo.PartMaster P ON P.MTransNo = J.RMPartNo
  OUTER APPLY (SELECT SUM(SD.Qty) AS IssuedQty
                 FROM dbo.StoreIssueDtl SD
                 JOIN dbo.StoreIssueMst SM ON SM.MTransNo = SD.MTransNo
                WHERE SM.TargetType = 'Job Card'
                  AND SM.TargetTransNo = J.MTransNo
                  AND SD.DeleteStatus = 'N' AND SM.DeleteStatus = 'N') I
 WHERE  J.DeleteStatus = 'N';
GO

/*------------------------------------------------------------------------------
  2. MRN-side functions ignore job card issues
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_MRNLineIssued (@TransSlNo INT)
RETURNS BIT
AS
BEGIN
    IF EXISTS (SELECT 1
                 FROM dbo.StoreIssueDtl D
                 JOIN dbo.StoreIssueMst M ON M.MTransNo = D.MTransNo
                WHERE D.LinkNo       = @TransSlNo
                  AND ISNULL(M.TargetType, '') <> 'Job Card'
                  AND D.DeleteStatus = 'N'
                  AND M.DeleteStatus = 'N')
        RETURN 1;
    RETURN 0;
END;
GO

CREATE OR ALTER FUNCTION dbo.FN_MRNIssueCount (@MTransNo INT)
RETURNS INT
AS
BEGIN
    RETURN (SELECT COUNT(*)
              FROM dbo.StoreIssueMst
             WHERE TargetTransNo = @MTransNo
               AND ISNULL(TargetType, '') <> 'Job Card'
               AND DeleteStatus  = 'N');
END;
GO

-- What the source view offers now, by type.
SELECT MRNType, COUNT(*) AS Lines, SUM(BalQty) AS Balance
  FROM dbo.VW_StoreIssueMRNLine
 GROUP BY MRNType;
GO
