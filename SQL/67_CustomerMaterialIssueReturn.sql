/*==============================================================================
  67. CUSTOMER MATERIAL - issue to job cards, return to store, return to customer

  Until now the customer-owned bar (SQL 60) was only RECEIVED: its balance
  never went down, and a Store Issue of that bar was taken from OUR stock.
  After this script the customer's bar has a full ledger of its own:

      Balance = Received - Issued (net of Issue Returns) - Returned to customer

  1. StoreIssueDtl.MaterialSource   'O' our bar (default) | 'C' customer bar.
     The API sets 'C' when the issue line's OA line is marked
     OADtl.CustomerMaterial = 'Y'. Saved on the line, so later changes to the
     OA flag do not rewrite history.
  2. CustMatInwardMst.DocType       'I' inward (default) | 'R' return to
     customer (unused bar / end pieces sent back on our outward DC).
     Returns get their own number series: CMR-<FY>-00001.
  3. VW_StoreStock (our stock)      leaves out customer bar: issue lines
     marked 'C', and Issue Returns of those lines.
  4. VW_CustomerMaterialStock       IssuedQty and ReturnedQty are now real:
       IssuedQty   = Store Issue 'C' lines - Issue Returns of them
       ReturnedQty = return-to-customer documents (DocType 'R')
     Keyed by customer x OA x RM part x grade (an issue line carries no bar
     dia; BarDia / Unit shown are those of the receipts).
  5. SP_StoreIssueDtlPlanFields     new @MaterialSource. A customer line's Heat
     No must be a heat from that customer's inward on the same OA, and the
     customer balance may not go below zero.
  6. SP_CustMatInwardMst / SP_CustMatInwardDtlSave  understand DocType 'R'.

  Run after 60 (and 58). Run with: sqlcmd -I -S <server> -d <db> -i 67_CustomerMaterialIssueReturn.sql
  Idempotent - safe to re-run.
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  1-2. Columns + number key
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.StoreIssueDtl', 'MaterialSource') IS NULL
    ALTER TABLE dbo.StoreIssueDtl ADD MaterialSource CHAR(1) NOT NULL
        CONSTRAINT DF_StoreIssueDtl_MaterialSource DEFAULT 'O' WITH VALUES;
GO
IF COL_LENGTH('dbo.CustMatInwardMst', 'DocType') IS NULL
    ALTER TABLE dbo.CustMatInwardMst ADD DocType CHAR(1) NOT NULL
        CONSTRAINT DF_CustMatInwardMst_DocType DEFAULT 'I' WITH VALUES;
GO
-- Inward and return keep separate number series: the key carries DocType.
IF NOT EXISTS (SELECT 1 FROM sys.index_columns ic
                 JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                 JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID('dbo.CustMatInwardMst') AND i.name = 'UQ_CustMatInwardMst_No'
                  AND c.name = 'DocType')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_CustMatInwardMst_No' AND parent_object_id = OBJECT_ID('dbo.CustMatInwardMst'))
        ALTER TABLE dbo.CustMatInwardMst DROP CONSTRAINT UQ_CustMatInwardMst_No;
    ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_CustMatInwardMst_No' AND object_id = OBJECT_ID('dbo.CustMatInwardMst'))
        DROP INDEX UQ_CustMatInwardMst_No ON dbo.CustMatInwardMst;
    ALTER TABLE dbo.CustMatInwardMst ADD CONSTRAINT UQ_CustMatInwardMst_No UNIQUE (SubscID, DocYear, DocType, DocNo);
END
GO

/*------------------------------------------------------------------------------
  3. VW_StoreStock - OUR stock. Same as SQL 46, except customer bar is left out.
------------------------------------------------------------------------------*/
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
        D.PartNo, NULL, '',
        NULL, NULL,
        CAST(0 AS NUMERIC(14,4)), CAST(ISNULL(D.Qty, 0) AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.STxnMst M
  JOIN  dbo.STxnDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N';
GO

/*------------------------------------------------------------------------------
  4. VW_CustomerMaterialStock - the customer's own bar
------------------------------------------------------------------------------*/
CREATE OR ALTER VIEW dbo.VW_CustomerMaterialStock
AS
WITH oa AS (
    -- the customer of each OA (live, current revision)
    SELECT  m.SubscID, m.DocYear AS OAYear, m.DocNo AS OANo, MAX(m.VendorNo) AS CustomerNo
      FROM  dbo.OAMst m
     WHERE  m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
     GROUP  BY m.SubscID, m.DocYear, m.DocNo
),
mv AS (
    -- in: the customer's bar arriving on the customer's DC
    SELECT  m.SubscID, m.CustomerNo, m.OAYear, m.OANo, d.PartNo, d.GradeNo,
            LTRIM(RTRIM(ISNULL(d.Grade, ''))) AS Grade, d.BarDia, ISNULL(d.Unit, '') AS Unit,
            d.Qty AS ReceivedQty, CAST(0 AS NUMERIC(18, 3)) AS IssuedQty, CAST(0 AS NUMERIC(18, 3)) AS ReturnedQty,
            m.MTransNo AS InwardTransNo, m.InwardDate
      FROM  dbo.CustMatInwardMst m
      JOIN  dbo.CustMatInwardDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
     WHERE  m.DeleteStatus = 'N' AND m.DocType = 'I'
    UNION ALL
    -- out: sent back to the customer on our DC (unused bar, end pieces)
    SELECT  m.SubscID, m.CustomerNo, m.OAYear, m.OANo, d.PartNo, d.GradeNo,
            LTRIM(RTRIM(ISNULL(d.Grade, ''))), NULL, '',
            0, 0, d.Qty, NULL, NULL
      FROM  dbo.CustMatInwardMst m
      JOIN  dbo.CustMatInwardDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
     WHERE  m.DeleteStatus = 'N' AND m.DocType = 'R'
    UNION ALL
    -- out: issued to the shop (job card / MRN) from the customer's bar
    SELECT  im.SubscID, oa.CustomerNo, i.OAYear, i.OANo, i.PartNo, i.GradeNo,
            LTRIM(RTRIM(ISNULL(i.Grade, ''))), NULL, '',
            0, i.Qty, 0, NULL, NULL
      FROM  dbo.StoreIssueDtl i
      JOIN  dbo.StoreIssueMst im ON im.MTransNo = i.MTransNo AND im.DeleteStatus = 'N'
      JOIN  oa ON oa.SubscID = im.SubscID AND oa.OAYear = i.OAYear AND oa.OANo = i.OANo
     WHERE  i.DeleteStatus = 'N' AND i.MaterialSource = 'C'
    UNION ALL
    -- back: customer bar returned from the shop to the store (Issue Return)
    SELECT  rm.SubscID, oa.CustomerNo, i.OAYear, i.OANo, i.PartNo, i.GradeNo,
            LTRIM(RTRIM(ISNULL(i.Grade, ''))), NULL, '',
            0, -r.RTNQty, 0, NULL, NULL
      FROM  dbo.StoreReturnsDtl r
      JOIN  dbo.StoreReturnsMst rm ON rm.MTransNo = r.MTransNo AND rm.DeleteStatus = 'N'
      JOIN  dbo.StoreIssueDtl i ON i.TransSlNo = r.LinkNo AND i.MaterialSource = 'C'
      JOIN  oa ON oa.SubscID = rm.SubscID AND oa.OAYear = i.OAYear AND oa.OANo = i.OANo
     WHERE  r.DeleteStatus = 'N'
),
agg AS (
    SELECT  SubscID, CustomerNo, OAYear, OANo, PartNo, Grade,
            MAX(GradeNo)                     AS GradeNo,
            MAX(BarDia)                      AS BarDia,
            MAX(Unit)                        AS Unit,
            SUM(ReceivedQty)                 AS ReceivedQty,
            SUM(IssuedQty)                   AS IssuedQty,
            SUM(ReturnedQty)                 AS ReturnedQty,
            COUNT(DISTINCT InwardTransNo)    AS InwardCount,
            MAX(InwardDate)                  AS LastInwardDate
      FROM  mv
     GROUP  BY SubscID, CustomerNo, OAYear, OANo, PartNo, Grade
)
SELECT  a.SubscID,
        a.CustomerNo,
        cn.CustomerName,
        a.OAYear,
        a.OANo,
        a.PartNo,
        p.PartCode,
        p.PartName,
        a.GradeNo,
        a.Grade,
        a.BarDia,
        CASE WHEN a.Unit = '' THEN 'KG' ELSE a.Unit END                          AS Unit,
        CAST(a.ReceivedQty AS NUMERIC(18, 3))                                    AS ReceivedQty,
        CAST(a.IssuedQty AS NUMERIC(18, 3))                                      AS IssuedQty,
        CAST(a.ReturnedQty AS NUMERIC(18, 3))                                    AS ReturnedQty,
        CAST(a.ReceivedQty - a.IssuedQty - a.ReturnedQty AS NUMERIC(18, 3))      AS BalanceQty,
        a.InwardCount,
        a.LastInwardDate
  FROM  agg a
  LEFT JOIN dbo.PartMaster p ON p.MTransNo = a.PartNo
 OUTER APPLY (SELECT TOP (1) x.CustomerName FROM dbo.CustMatInwardMst x
               WHERE x.SubscID = a.SubscID AND x.CustomerNo = a.CustomerNo AND x.DeleteStatus = 'N'
               ORDER BY x.MTransNo DESC) cn;
GO

/*------------------------------------------------------------------------------
  5. SP_StoreIssueDtlPlanFields - heat number + material source of an issue line
     @MaterialSource NULL keeps what the line has ('O' / 'C').
     'O' (our bar): Heat No must be on an APPROVED heat document of the part.
     'C' (customer bar): Heat No must be on a live inward of the customer's
     bar for the same OA and part; and the customer balance may not go below
     zero (checked after the line is written, inside the same transaction).
------------------------------------------------------------------------------*/
ALTER PROCEDURE dbo.SP_StoreIssueDtlPlanFields
    @ID              INT             OUTPUT,
    @TransSlNo       INT,
    @HeatNo          VARCHAR(50),
    @UserNo          INT,
    @SubscID         INT,
    @Message         NVARCHAR(200)   OUTPUT,
    @MaterialSource  CHAR(1)         = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @H VARCHAR(50) = NULLIF(UPPER(LTRIM(RTRIM(@HeatNo))), ''),
            @PartNo INT, @Grade VARCHAR(50), @Code VARCHAR(50),
            @Src CHAR(1), @OAYear INT, @OANo INT;

    BEGIN TRY
        SELECT @PartNo = PartNo, @Grade = LTRIM(RTRIM(ISNULL(Grade, ''))),
               @Code = ISNULL(NULLIF(LTRIM(RTRIM(PartCode)), ''), PartDescription),
               @Src = ISNULL(NULLIF(UPPER(@MaterialSource), ''), MaterialSource),
               @OAYear = OAYear, @OANo = OANo
          FROM dbo.StoreIssueDtl
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @PartNo IS NULL
        BEGIN SET @Message = N'Issue line not found.'; RETURN; END
        IF @Src NOT IN ('O', 'C')
        BEGIN SET @Message = N'Material source must be O (ours) or C (customer).'; RETURN; END

        IF @Src = 'O' AND @H IS NOT NULL AND NOT EXISTS (
               SELECT 1
                 FROM dbo.HeatNoDtl d
                 JOIN dbo.HeatNoMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.LockStatus = 'Y'
                WHERE d.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.PartNo = @PartNo
                  AND UPPER(LTRIM(RTRIM(d.HeatNo))) = @H
                  AND (@Grade = ''
                       OR LTRIM(RTRIM(ISNULL(NULLIF(LTRIM(RTRIM(d.Grade)), ''), ISNULL(m.Grade, '')))) IN ('', @Grade)))
        BEGIN
            SET @Message = N'Heat No ' + @H + N' is not an approved heat of ' + ISNULL(@Code, N'this part')
                         + CASE WHEN @Grade <> '' THEN N' (grade ' + @Grade + N')' ELSE N'' END + N'.';
            RETURN;
        END

        IF @Src = 'C' AND @H IS NOT NULL AND NOT EXISTS (
               SELECT 1
                 FROM dbo.CustMatInwardDtl d
                 JOIN dbo.CustMatInwardMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N' AND m.DocType = 'I'
                WHERE m.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.PartNo = @PartNo
                  AND m.OAYear = @OAYear AND m.OANo = @OANo
                  AND UPPER(LTRIM(RTRIM(d.HeatNo))) = @H
                  AND (@Grade = '' OR LTRIM(RTRIM(ISNULL(d.Grade, ''))) IN ('', @Grade)))
        BEGIN
            SET @Message = N'Heat No ' + @H + N' was not received from the customer for OA '
                         + CONVERT(NVARCHAR(10), @OAYear) + N'-' + CONVERT(NVARCHAR(10), @OANo)
                         + N' (' + ISNULL(@Code, N'this part') + N').';
            RETURN;
        END

        BEGIN TRANSACTION;

        UPDATE dbo.StoreIssueDtl
           SET HeatNo = @H, MaterialSource = @Src
         WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @Src = 'C' AND EXISTS (
               SELECT 1 FROM dbo.VW_CustomerMaterialStock v
                WHERE v.SubscID = @SubscID AND v.OAYear = @OAYear AND v.OANo = @OANo
                  AND v.PartNo = @PartNo AND v.Grade = @Grade AND v.BalanceQty < 0)
        BEGIN
            ROLLBACK TRANSACTION;
            SET @Message = N'Not enough customer material of ' + ISNULL(@Code, N'this part')
                         + CASE WHEN @Grade <> '' THEN N' (grade ' + @Grade + N')' ELSE N'' END
                         + N' for OA ' + CONVERT(NVARCHAR(10), @OAYear) + N'-' + CONVERT(NVARCHAR(10), @OANo) + N'.';
            RETURN;
        END

        COMMIT TRANSACTION;
        SET @ID = @TransSlNo;
        SET @Message = N'Heat number saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6a. SP_CustMatInwardMst - now also DocType 'R' (return to customer)
      @DocType is used on Insert only; a saved document keeps its type.
      'R': number CMR-<FY>-00001 (own series), DC No = OUR outward DC,
      status 'Returned'.
------------------------------------------------------------------------------*/
ALTER PROCEDURE dbo.SP_CustMatInwardMst
    @ID            INT            OUTPUT,
    @Message       NVARCHAR(200)  OUTPUT,
    @Status        VARCHAR(10),
    @MTransNo      INT,
    @InwardDate    DATE,
    @CustomerNo    INT,
    @OAYear        INT,
    @OANo          INT,
    @DCNo          VARCHAR(30),
    @DCDate        DATE,
    @ReceivedBy    VARCHAR(100),
    @Remarks       VARCHAR(250),
    @DocStatus     VARCHAR(20),
    @SystemIP      VARCHAR(20),
    @UserNo        INT,
    @DeleteReason  VARCHAR(250),
    @SubscID       INT,
    @DocType       CHAR(1)        = 'I'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @No VARCHAR(30), @CurOAYear INT, @CurOANo INT, @CurCust INT, @CurType CHAR(1);
    DECLARE @Cust INT, @CustName VARCHAR(100);

    BEGIN TRY
        IF @Status NOT IN ('Insert', 'Update', 'Delete')
        BEGIN SET @Message = N'Unknown status.'; RETURN; END

        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @No = InwardNo, @CurOAYear = OAYear, @CurOANo = OANo, @CurCust = CustomerNo, @CurType = DocType
              FROM dbo.CustMatInwardMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @No IS NULL
            BEGIN SET @Message = N'Customer material document not found.'; RETURN; END
            SET @DocType = @CurType;
        END
        SET @DocType = UPPER(ISNULL(NULLIF(LTRIM(RTRIM(@DocType)), ''), 'I'));
        IF @DocType NOT IN ('I', 'R')
        BEGIN SET @Message = N'Type must be I (inward) or R (return to customer).'; RETURN; END

        /*---------------- Delete ----------------*/
        IF @Status = 'Delete'
        BEGIN
            IF LTRIM(RTRIM(ISNULL(@DeleteReason, ''))) = ''
            BEGIN SET @Message = N'Enter the reason for deleting.'; RETURN; END

            BEGIN TRANSACTION;

            UPDATE dbo.CustMatInwardDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            UPDATE dbo.CustMatInwardMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason, SystemIP = @SystemIP,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            -- Bar already issued or returned out of this receipt cannot be un-received.
            IF EXISTS (SELECT 1 FROM dbo.VW_CustomerMaterialStock
                        WHERE SubscID = @SubscID AND CustomerNo = @CurCust
                          AND OAYear = @CurOAYear AND OANo = @CurOANo AND BalanceQty < 0)
            BEGIN
                ROLLBACK TRANSACTION;
                SET @Message = CONVERT(NVARCHAR(30), @No)
                             + N': part of this material has already been issued or returned - it cannot be deleted.';
                RETURN;
            END

            COMMIT TRANSACTION;
            SET @ID = @MTransNo;
            SET @Message = N'Record Deleted Successfully';
            RETURN;
        END

        /*---------------- Insert / Update: validation ----------------*/
        SET @DCNo = LTRIM(RTRIM(ISNULL(@DCNo, '')));
        SET @DocStatus = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@DocStatus, ''))), ''),
                                CASE WHEN @DocType = 'R' THEN 'Returned' ELSE 'Received' END);

        IF @InwardDate IS NULL
        BEGIN SET @Message = CASE WHEN @DocType = 'R' THEN N'Return Date is required.' ELSE N'Inward Date is required.' END; RETURN; END
        IF @DCNo = ''
        BEGIN SET @Message = CASE WHEN @DocType = 'R' THEN N'Our DC No is required.' ELSE N'Customer DC No is required.' END; RETURN; END
        IF @DCDate IS NULL
        BEGIN SET @Message = N'DC Date is required.'; RETURN; END
        IF @DocType = 'I' AND @DCDate > @InwardDate
        BEGIN SET @Message = N'The DC Date cannot be after the Inward Date.'; RETURN; END
        IF @DocType = 'I' AND @DocStatus NOT IN ('Received', 'Verified')
        BEGIN SET @Message = N'Status must be Received or Verified.'; RETURN; END
        IF @DocType = 'R' AND @DocStatus NOT IN ('Returned', 'Verified')
        BEGIN SET @Message = N'Status must be Returned or Verified.'; RETURN; END

        IF @Status = 'Update' AND (@OAYear <> @CurOAYear OR @OANo <> @CurOANo)
        BEGIN SET @Message = N'A saved document stays on its OA - delete it and enter it again against the other OA.'; RETURN; END

        -- The live, current revision of the OA names the customer.
        SELECT TOP (1) @Cust = m.VendorNo, @CustName = m.VendorName
          FROM dbo.OAMst m
         WHERE m.SubscID = @SubscID AND m.DocYear = @OAYear AND m.DocNo = @OANo
           AND m.DeleteStatus = 'N' AND m.DocAmdStatus = 'N'
         ORDER BY m.MTransNo DESC;

        IF @Cust IS NULL AND @Status = 'Update'
            SELECT @Cust = @CurCust, @CustName = CustomerName      -- OA since withdrawn: keep the snapshot
              FROM dbo.CustMatInwardMst WHERE MTransNo = @MTransNo;

        IF @Cust IS NULL
        BEGIN SET @Message = N'Select a valid Order Acceptance.'; RETURN; END
        IF ISNULL(@CustomerNo, 0) > 0 AND @CustomerNo <> @Cust
        BEGIN SET @Message = N'The selected OA belongs to another customer.'; RETURN; END

        IF EXISTS (SELECT 1 FROM dbo.CustMatInwardMst
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND CustomerNo = @Cust AND DocType = @DocType
                      AND DCNo = @DCNo AND dbo.FN_FYCode(DCDate) = dbo.FN_FYCode(@DCDate)
                      AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
        BEGIN
            SET @Message = CASE WHEN @DocType = 'R' THEN N'Our DC ' ELSE N'Customer DC ' END
                         + CONVERT(NVARCHAR(30), @DCNo) + N' is already recorded for this customer.';
            RETURN;
        END

        /*---------------- Insert ----------------*/
        IF @Status = 'Insert'
        BEGIN
            BEGIN TRANSACTION;
                DECLARE @Year INT = dbo.FN_FYCode(@InwardDate);
                DECLARE @DocNo INT =
                    ISNULL((SELECT MAX(DocNo) FROM dbo.CustMatInwardMst WITH (UPDLOCK, HOLDLOCK)
                             WHERE SubscID = @SubscID AND DocYear = @Year AND DocType = @DocType), 0) + 1;

                INSERT INTO dbo.CustMatInwardMst
                       (DocYear, DocNo, InwardNo, InwardDate, CustomerNo, CustomerName, OAYear, OANo,
                        DCNo, DCDate, ReceivedBy, Remarks, DocStatus, SystemIP, CreatedBy, CreatedOn, SubscID, DocType)
                VALUES (@Year, @DocNo,
                        CONCAT(CASE WHEN @DocType = 'R' THEN 'CMR-' ELSE 'CMI-' END, @Year, '-', RIGHT('00000' + CAST(@DocNo AS VARCHAR(10)), 5)),
                        @InwardDate, @Cust, @CustName, @OAYear, @OANo,
                        @DCNo, @DCDate, NULLIF(LTRIM(RTRIM(@ReceivedBy)), ''), NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                        @DocStatus, @SystemIP, @UserNo, SYSDATETIME(), @SubscID, @DocType);
                SET @ID = SCOPE_IDENTITY();
            COMMIT TRANSACTION;
            SET @Message = N'Record Saved Successfully';
            RETURN;
        END

        /*---------------- Update ----------------*/
        -- The number keeps its year even if the date moves across 1 April.
        BEGIN TRANSACTION;
            UPDATE dbo.CustMatInwardMst
               SET InwardDate = @InwardDate, CustomerName = @CustName,
                   DCNo = @DCNo, DCDate = @DCDate,
                   ReceivedBy = NULLIF(LTRIM(RTRIM(@ReceivedBy)), ''),
                   Remarks = NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                   DocStatus = @DocStatus, SystemIP = @SystemIP,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(),
                   LockCounter = LockCounter + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        COMMIT TRANSACTION;
        SET @ID = @MTransNo;
        SET @Message = N'Record Edited Successfully';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  6b. SP_CustMatInwardDtlSave - same as SQL 60; on a RETURN document the lines
      may not send back more than the customer's balance.
------------------------------------------------------------------------------*/
ALTER PROCEDURE dbo.SP_CustMatInwardDtlSave
    @ID        INT            OUTPUT,
    @MTransNo  INT,
    @Lines     NVARCHAR(MAX),
    @UserNo    INT,
    @SubscID   INT,
    @Message   NVARCHAR(200)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @L TABLE
    (
        Pos       INT PRIMARY KEY,
        PartNo    INT,
        GradeNo   INT,
        Grade     VARCHAR(50),
        BarDia    NUMERIC(9, 2),
        Qty       NUMERIC(18, 3),
        Unit      VARCHAR(20),
        HeatNo    VARCHAR(30),
        TCNo      VARCHAR(30),
        OAID      INT,
        OALinkNo  INT,
        Remarks   VARCHAR(250)
    );
    DECLARE @No VARCHAR(30), @OAYear INT, @OANo INT, @Cust INT, @Txt NVARCHAR(200), @Type CHAR(1);

    BEGIN TRY
        SELECT @No = InwardNo, @OAYear = OAYear, @OANo = OANo, @Cust = CustomerNo, @Type = DocType
          FROM dbo.CustMatInwardMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @No IS NULL
        BEGIN SET @Message = N'Customer material document not found.'; RETURN; END

        IF ISJSON(@Lines) = 1
            INSERT INTO @L (Pos, PartNo, GradeNo, Grade, BarDia, Qty, Unit, HeatNo, TCNo, OAID, Remarks)
            SELECT CONVERT(INT, [key]) + 1,
                   TRY_CONVERT(INT,            JSON_VALUE(value, '$.partNo')),
                   TRY_CONVERT(INT,            JSON_VALUE(value, '$.gradeNo')),
                   LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(value, '$.grade'), ''))), 50),
                   TRY_CONVERT(NUMERIC(9, 2),  JSON_VALUE(value, '$.barDia')),
                   TRY_CONVERT(NUMERIC(18, 3), JSON_VALUE(value, '$.qty')),
                   NULLIF(LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(value, '$.unit'), ''))), 20), ''),
                   NULLIF(LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(value, '$.heatNo'), ''))), 30), ''),
                   NULLIF(LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(value, '$.tcNo'), ''))), 30), ''),
                   NULLIF(TRY_CONVERT(INT,     JSON_VALUE(value, '$.oaID')), 0),
                   NULLIF(LEFT(LTRIM(RTRIM(ISNULL(JSON_VALUE(value, '$.remarks'), ''))), 250), '')
              FROM OPENJSON(@Lines);

        IF NOT EXISTS (SELECT 1 FROM @L)
        BEGIN SET @Message = N'Add at least one material line.'; RETURN; END

        -- Grade by number first, then by its text, against the part's live grades.
        UPDATE l
           SET GradeNo = g.GradeNo, Grade = g.Grade
          FROM @L l
         CROSS APPLY (SELECT TOP (1) mg.GradeNo, LTRIM(RTRIM(mg.Grade)) AS Grade
                        FROM dbo.MaterialGrade mg
                       WHERE mg.PartNo = l.PartNo AND mg.SubscID = @SubscID AND mg.DeleteStatus = 'N'
                         AND ((ISNULL(l.GradeNo, 0) > 0 AND mg.GradeNo = l.GradeNo)
                           OR (ISNULL(l.GradeNo, 0) = 0 AND LTRIM(RTRIM(mg.Grade)) = l.Grade))
                       ORDER BY CASE WHEN mg.LockStatus = 'Y' THEN 0 ELSE 1 END, mg.MTransNo) g;

        -- OA line -> its TransSlNo, on the document's own OA (live, current revision).
        UPDATE l
           SET OALinkNo = d.TransSlNo
          FROM @L l
         CROSS APPLY (SELECT TOP (1) od.TransSlNo
                        FROM dbo.OAMst om
                        JOIN dbo.OADtl od ON od.MTransNo = om.MTransNo AND od.DeleteStatus = 'N'
                       WHERE om.SubscID = @SubscID AND om.DocYear = @OAYear AND om.DocNo = @OANo
                         AND om.DeleteStatus = 'N' AND om.DocAmdStatus = 'N'
                         AND od.SlNo = l.OAID
                       ORDER BY om.MTransNo DESC) d
         WHERE l.OAID IS NOT NULL;

        SELECT TOP (1) @Txt =
               N'Line ' + CONVERT(NVARCHAR(12), l.Pos) +
               CASE WHEN p.MTransNo IS NULL                 THEN N': select a valid RM part.'
                    WHEN ISNULL(l.Qty, 0) <= 0              THEN N': quantity must be greater than zero.'
                    WHEN ISNULL(l.BarDia, 0) < 0            THEN N': bar dia cannot be negative.'
                    WHEN ISNULL(l.GradeNo, 0) = 0 OR l.Grade = ''
                                                            THEN N': select a grade of ' + ISNULL(p.PartCode, N'the part') + N'.'
                    ELSE                                         N': OA line ' + CONVERT(NVARCHAR(12), l.OAID) + N' is not on this OA.'
               END
          FROM @L l
          LEFT JOIN dbo.PartMaster p ON p.MTransNo = l.PartNo AND p.SubscID = @SubscID AND p.DeleteStatus = 'N'
         WHERE p.MTransNo IS NULL
            OR ISNULL(l.Qty, 0) <= 0
            OR ISNULL(l.BarDia, 0) < 0
            OR ISNULL(l.GradeNo, 0) = 0 OR l.Grade = ''
            OR (l.OAID IS NOT NULL AND l.OALinkNo IS NULL)
         ORDER BY l.Pos;

        IF @Txt IS NOT NULL
        BEGIN SET @Message = @Txt; RETURN; END

        BEGIN TRANSACTION;

        UPDATE dbo.CustMatInwardDtl
           SET DeleteStatus = 'Y', DeleteReason = 'Superseded',
               ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

        INSERT INTO dbo.CustMatInwardDtl
               (MTransNo, SlNo, PartNo, GradeNo, Grade, BarDia, Qty, Unit, HeatNo, TCNo, OAID, OALinkNo,
                Remarks, CreatedBy, CreatedOn, SubscID)
        SELECT @MTransNo, l.Pos, l.PartNo, l.GradeNo, l.Grade, NULLIF(l.BarDia, 0), l.Qty,
               ISNULL(l.Unit, 'KG'),
               l.HeatNo, l.TCNo, l.OAID, l.OALinkNo, l.Remarks, @UserNo, SYSDATETIME(), @SubscID
          FROM @L l
          JOIN dbo.PartMaster p ON p.MTransNo = l.PartNo
         ORDER BY l.Pos;

        -- Receipts may not undercut what has already gone out; returns may
        -- not send back more than the customer's balance.
        IF EXISTS (SELECT 1 FROM dbo.VW_CustomerMaterialStock
                    WHERE SubscID = @SubscID AND CustomerNo = @Cust
                      AND OAYear = @OAYear AND OANo = @OANo AND BalanceQty < 0)
        BEGIN
            ROLLBACK TRANSACTION;
            SET @Message = CONVERT(NVARCHAR(30), @No)
                         + CASE WHEN @Type = 'R'
                                THEN N': more is being returned than the customer''s balance of that bar on this OA.'
                                ELSE N': the new quantities are below what has already been issued or returned.' END;
            RETURN;
        END

        COMMIT TRANSACTION;

        SET @ID = @MTransNo;
        SET @Message = N'Lines saved successfully.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

PRINT '67_CustomerMaterialIssueReturn applied.';
GO
