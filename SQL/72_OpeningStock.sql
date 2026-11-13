/*==============================================================================
  72. OPENING STOCK

  The stock held on the go-live day. Until now the system had no record of it:
  stock is worked out from GRNs, issues, returns and invoices only, so every
  balance started at zero.

  This script:
    1. Adds OpeningStockMst / OpeningStockDtl (one document per count, a line
       per part + grade + heat with qty, rate and bin).
    2. Adds SP_OpeningStockSave   — header + lines in one call (lines as JSON),
                                     Draft (N) / Submitted (S) only.
            SP_OpeningStockStatus — Approve (S → Y, not an EXECUTIVE),
                                     Unapprove (Y → S, ADMIN, refused when the
                                     stock is already used), Delete (not approved).
    3. VW_StoreStock: approved OWN lines are an 'OPENING' row (In = qty). So
       Store Issue's stock check, RM Requirement / planning stock and the
       reports all see the opening stock with no change to their code.
    4. VW_CustomerMaterialStock: approved CUSTOMER lines count as received
       (per customer + OA, as the view keeps customer bar). Customer name falls
       back to Customer Master.
    5. SP_StoreIssueDtlPlanFields: a heat on an approved opening line is a
       valid heat for the Store Issue line (own: any opening of the part;
       customer: an opening on the same OA). Nothing else in it changes.
    6. Menu: Store → Opening Stock (last in Store), full access grants.

  ORDER: run BEFORE deploying the API build with OpeningStockController.
         Then run 73_FinishedGoodsStock.sql. Re-running this script on its own
         re-creates VW_StoreStock without 73's lines, so run 73 again after it.
  CHECK BEFORE RUNNING: steps 3-5 re-create the two views and the procedure in
  full from this database's copies (SQL 67 / 58 versions). If they were changed
  on the target server after those scripts, compare first.

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 72_OpeningStock.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  1. Tables
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.OpeningStockMst', N'U') IS NULL
CREATE TABLE dbo.OpeningStockMst
(
    MTransNo      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpeningStockMst PRIMARY KEY,
    DocSeries     CHAR(10)        NOT NULL,
    DocYear       INT             NOT NULL,
    DocNo         INT             NOT NULL,
    AsOnDate      DATE            NOT NULL,
    StockType     VARCHAR(10)     NOT NULL CONSTRAINT DF_OpeningStockMst_Type DEFAULT 'OWN',  -- OWN | CUSTOMER
    CustomerNo    INT             NULL,
    CustomerName  VARCHAR(100)    NULL,
    OAYear        INT             NULL,
    OANo          INT             NULL,
    Remarks       VARCHAR(250)    NULL,
    LockStatus    CHAR(1)         NOT NULL CONSTRAINT DF_OpeningStockMst_Lock DEFAULT 'N',    -- N | S | Y
    ApprovedBy    INT             NULL,
    ApprovedOn    DATETIME2       NULL,
    SystemIP      VARCHAR(20)     NULL,
    CreatedBy     INT             NULL,
    CreatedOn     DATETIME2       NULL,
    ModifiedBy    INT             NULL,
    ModifiedOn    DATETIME2       NULL,
    DeleteStatus  CHAR(1)         NOT NULL CONSTRAINT DF_OpeningStockMst_Del DEFAULT 'N',
    DeleteReason  VARCHAR(250)    NULL,
    LockCounter   INT             NOT NULL CONSTRAINT DF_OpeningStockMst_Cnt DEFAULT 0,
    SubscID       INT             NOT NULL
);
GO
IF OBJECT_ID(N'dbo.OpeningStockDtl', N'U') IS NULL
CREATE TABLE dbo.OpeningStockDtl
(
    TransSlNo     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OpeningStockDtl PRIMARY KEY,
    MTransNo      INT             NOT NULL,
    SlNo          INT             NOT NULL,
    PartNo        INT             NOT NULL,
    PartCode      VARCHAR(50)     NULL,
    PartName      VARCHAR(250)    NULL,
    GradeNo       INT             NULL,
    Grade         VARCHAR(50)     NOT NULL,
    Unit          VARCHAR(10)     NULL,
    Qty           NUMERIC(14,4)   NOT NULL,
    Rate          NUMERIC(14,2)   NOT NULL CONSTRAINT DF_OpeningStockDtl_Rate DEFAULT 0,
    Value         NUMERIC(16,2)   NOT NULL CONSTRAINT DF_OpeningStockDtl_Value DEFAULT 0,
    BinNo         INT             NULL,          -- StoreLocation.MTransNo
    Store         VARCHAR(50)     NULL,
    Location      VARCHAR(50)     NULL,
    HeatNo        VARCHAR(50)     NULL,
    TCNo          VARCHAR(30)     NULL,
    Remarks       VARCHAR(200)    NULL,
    DeleteStatus  CHAR(1)         NOT NULL CONSTRAINT DF_OpeningStockDtl_Del DEFAULT 'N',
    SubscID       INT             NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OpeningStockDtl_MTransNo')
    CREATE INDEX IX_OpeningStockDtl_MTransNo ON dbo.OpeningStockDtl (MTransNo) INCLUDE (PartNo, Grade, Qty, DeleteStatus);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OpeningStockDtl_Part')
    CREATE INDEX IX_OpeningStockDtl_Part ON dbo.OpeningStockDtl (PartNo, Grade) INCLUDE (HeatNo, Qty, MTransNo, DeleteStatus);
GO

/*------------------------------------------------------------------------------
  2a. SP_OpeningStockSave — header + lines in one transaction
      @LinesJson: [{ partNo, gradeNo, grade, qty, rate, binNo, heatNo, tcNo, remarks }]
      The lines replace the stored ones. Insert takes the next DocNo of the
      series + year. Refused: an approved document, an As On in the future,
      CUSTOMER without a live OA of that customer, no lines, qty <= 0, rate <= 0
      on OWN, a heat-traced part without a heat, a grade not on the part, the
      same part + grade + heat twice, a bin that does not exist.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_OpeningStockSave', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_OpeningStockSave AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_OpeningStockSave
    @ID          INT             OUTPUT,
    @MTransNo    INT,
    @DocSeries   VARCHAR(10),
    @DocYear     INT,
    @AsOnDate    DATE,
    @StockType   VARCHAR(10),
    @CustomerNo  INT,
    @OAYear      INT,
    @OANo        INT,
    @Remarks     VARCHAR(250),
    @LockStatus  CHAR(1),
    @LinesJson   NVARCHAR(MAX),
    @UserNo      INT,
    @SystemIP    VARCHAR(20),
    @SubscID     INT,
    @Message     NVARCHAR(200)   OUTPUT,
    @DocNo       INT             = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Type VARCHAR(10) = UPPER(LTRIM(RTRIM(ISNULL(@StockType, 'OWN')))),
            @Series VARCHAR(10) = LTRIM(RTRIM(ISNULL(@DocSeries, ''))),
            @CustName VARCHAR(100), @Lock CHAR(1), @Bad NVARCHAR(200);

    IF @Series = '' BEGIN SET @Message = N'Doc Series is required.'; RETURN; END
    IF ISNULL(@DocYear, 0) <= 0 BEGIN SET @Message = N'Doc Year is required.'; RETURN; END
    IF @AsOnDate IS NULL BEGIN SET @Message = N'As On Date is required.'; RETURN; END
    IF @AsOnDate > CAST(GETDATE() AS DATE) BEGIN SET @Message = N'As On Date cannot be in the future.'; RETURN; END
    IF @Type NOT IN ('OWN', 'CUSTOMER') BEGIN SET @Message = N'Stock Type must be OWN or CUSTOMER.'; RETURN; END
    IF ISNULL(@LockStatus, '') NOT IN ('N', 'S') BEGIN SET @Message = N'Status must be Draft or Submitted.'; RETURN; END

    IF @MTransNo > 0
    BEGIN
        SELECT @Lock = LockStatus FROM dbo.OpeningStockMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @Lock IS NULL BEGIN SET @Message = N'Opening stock document not found.'; RETURN; END
        IF @Lock = 'Y' BEGIN SET @Message = N'This opening stock is approved and cannot be changed.'; RETURN; END
    END

    IF @Type = 'CUSTOMER'
    BEGIN
        IF ISNULL(@CustomerNo, 0) <= 0 BEGIN SET @Message = N'Select the customer whose material this is.'; RETURN; END
        IF ISNULL(@OAYear, 0) <= 0 OR ISNULL(@OANo, 0) <= 0
        BEGIN SET @Message = N'Select the OA the customer''s material is for.'; RETURN; END
        IF NOT EXISTS (SELECT 1 FROM dbo.OAMst
                        WHERE SubscID = @SubscID AND DocYear = @OAYear AND DocNo = @OANo
                          AND DeleteStatus = 'N' AND DocAmdStatus = 'N' AND VendorNo = @CustomerNo)
        BEGIN SET @Message = N'OA ' + CONVERT(NVARCHAR(10), @OAYear) + N'-' + CONVERT(NVARCHAR(10), @OANo)
                           + N' is not a live order of this customer.'; RETURN; END
        SELECT @CustName = CustomerName FROM dbo.CustomerMaster WHERE MTransNo = @CustomerNo;
    END

    -- The lines, numbered in the order sent.
    CREATE TABLE #L (
        n INT, PartNo INT, GradeNo INT, Grade VARCHAR(50), Qty NUMERIC(14,4), Rate NUMERIC(14,2),
        BinNo INT, HeatNo VARCHAR(50), TCNo VARCHAR(30), Remarks VARCHAR(200),
        PartCode VARCHAR(50), PartName VARCHAR(250), Unit VARCHAR(10), Traced CHAR(1),
        Store VARCHAR(50), Location VARCHAR(50));
    BEGIN TRY
        INSERT INTO #L (n, PartNo, GradeNo, Grade, Qty, Rate, BinNo, HeatNo, TCNo, Remarks)
        SELECT CAST(j.[key] AS INT) + 1,
               x.partNo, NULLIF(x.gradeNo, 0), LTRIM(RTRIM(ISNULL(x.grade, ''))),
               ROUND(ISNULL(x.qty, 0), 4), ROUND(ISNULL(x.rate, 0), 2), NULLIF(x.binNo, 0),
               NULLIF(UPPER(LTRIM(RTRIM(ISNULL(x.heatNo, '')))), ''), NULLIF(LTRIM(RTRIM(ISNULL(x.tcNo, ''))), ''),
               NULLIF(LTRIM(RTRIM(ISNULL(x.remarks, ''))), '')
          FROM OPENJSON(ISNULL(@LinesJson, N'[]')) j
         CROSS APPLY OPENJSON(j.[value]) WITH (
               partNo INT, gradeNo INT, grade VARCHAR(50), qty NUMERIC(18,4), rate NUMERIC(18,2),
               binNo INT, heatNo VARCHAR(50), tcNo VARCHAR(30), remarks VARCHAR(200)) x;
    END TRY
    BEGIN CATCH
        SET @Message = N'The lines could not be read: ' + LEFT(ERROR_MESSAGE(), 150); RETURN;
    END CATCH

    IF NOT EXISTS (SELECT 1 FROM #L) BEGIN SET @Message = N'Add at least one line.'; RETURN; END

    UPDATE l SET PartCode = p.PartCode, PartName = p.PartName, Unit = LEFT(ISNULL(p.PrimaryUnit, ''), 10),
                 Traced = CASE WHEN UPPER(ISNULL(p.MfrLicNo, '')) = 'Y' THEN 'Y' ELSE 'N' END
      FROM #L l JOIN dbo.PartMaster p ON p.MTransNo = l.PartNo AND p.SubscID = @SubscID AND p.DeleteStatus = 'N';
    UPDATE l SET Store = s.Store, Location = s.Location
      FROM #L l JOIN dbo.StoreLocation s ON s.MTransNo = l.BinNo AND s.SubscID = @SubscID AND s.DeleteStatus = 'N';
    IF @Type = 'CUSTOMER' UPDATE #L SET Rate = 0;

    SELECT TOP (1) @Bad = N'Line ' + CONVERT(NVARCHAR(10), n) + N': ' + msg FROM (
        SELECT n, 1 AS o, N'part not found.' AS msg FROM #L WHERE PartCode IS NULL
        UNION ALL SELECT n, 2, N'grade ''' + Grade + N''' is not a grade of ' + PartCode + N'.' FROM #L l
          WHERE PartCode IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.MaterialGrade g
                 WHERE g.PartNo = l.PartNo AND g.SubscID = @SubscID AND g.DeleteStatus = 'N'
                   AND UPPER(LTRIM(RTRIM(g.Grade))) = UPPER(l.Grade))
        UNION ALL SELECT n, 3, PartCode + N': Qty must be greater than 0.' FROM #L WHERE Qty <= 0
        UNION ALL SELECT n, 4, PartCode + N': Rate is required for own stock.' FROM #L WHERE @Type = 'OWN' AND Rate <= 0
        UNION ALL SELECT n, 5, PartCode + N': Heat No is required - the part is heat traced.' FROM #L WHERE Traced = 'Y' AND HeatNo IS NULL
        UNION ALL SELECT n, 6, N'the store location is not in the Store Location master.' FROM #L WHERE BinNo IS NOT NULL AND Store IS NULL
        UNION ALL SELECT l.n, 7, l.PartCode + N' / ' + l.Grade + ISNULL(N' / heat ' + l.HeatNo, N'')
                         + N' is already on line ' + CONVERT(NVARCHAR(10), f.n) + N'.'
          FROM #L l CROSS APPLY (SELECT MIN(n) AS n FROM #L x WHERE x.PartNo = l.PartNo
                   AND UPPER(x.Grade) = UPPER(l.Grade) AND ISNULL(x.HeatNo, '') = ISNULL(l.HeatNo, '')) f
         WHERE f.n < l.n
    ) e ORDER BY n, o;
    IF @Bad IS NOT NULL BEGIN SET @Message = LEFT(@Bad, 200); RETURN; END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF @MTransNo > 0
        BEGIN
            UPDATE dbo.OpeningStockMst
               SET AsOnDate = @AsOnDate, StockType = @Type,
                   CustomerNo = CASE WHEN @Type = 'CUSTOMER' THEN @CustomerNo END,
                   CustomerName = CASE WHEN @Type = 'CUSTOMER' THEN @CustName END,
                   OAYear = CASE WHEN @Type = 'CUSTOMER' THEN @OAYear END,
                   OANo = CASE WHEN @Type = 'CUSTOMER' THEN @OANo END,
                   Remarks = NULLIF(LTRIM(RTRIM(ISNULL(@Remarks, ''))), ''),
                   LockStatus = @LockStatus, SystemIP = @SystemIP,
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(), LockCounter = LockCounter + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N' AND LockStatus <> 'Y';
            IF @@ROWCOUNT = 0
            BEGIN ROLLBACK TRANSACTION; SET @Message = N'This opening stock was approved meanwhile and cannot be changed.'; RETURN; END
            UPDATE dbo.OpeningStockDtl SET DeleteStatus = 'Y' WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            SET @ID = @MTransNo;
            SELECT @DocNo = DocNo FROM dbo.OpeningStockMst WHERE MTransNo = @MTransNo;
        END
        ELSE
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.OpeningStockMst WITH (UPDLOCK, HOLDLOCK)
             WHERE SubscID = @SubscID AND DocSeries = @Series AND DocYear = @DocYear;
            INSERT INTO dbo.OpeningStockMst
                (DocSeries, DocYear, DocNo, AsOnDate, StockType, CustomerNo, CustomerName, OAYear, OANo,
                 Remarks, LockStatus, SystemIP, CreatedBy, CreatedOn, DeleteStatus, LockCounter, SubscID)
            VALUES
                (@Series, @DocYear, @DocNo, @AsOnDate, @Type,
                 CASE WHEN @Type = 'CUSTOMER' THEN @CustomerNo END, CASE WHEN @Type = 'CUSTOMER' THEN @CustName END,
                 CASE WHEN @Type = 'CUSTOMER' THEN @OAYear END, CASE WHEN @Type = 'CUSTOMER' THEN @OANo END,
                 NULLIF(LTRIM(RTRIM(ISNULL(@Remarks, ''))), ''), @LockStatus, @SystemIP, @UserNo, SYSDATETIME(), 'N', 1, @SubscID);
            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
        END

        INSERT INTO dbo.OpeningStockDtl
            (MTransNo, SlNo, PartNo, PartCode, PartName, GradeNo, Grade, Unit, Qty, Rate, Value,
             BinNo, Store, Location, HeatNo, TCNo, Remarks, DeleteStatus, SubscID)
        SELECT @ID, l.n, l.PartNo, l.PartCode, l.PartName, l.GradeNo,
               ISNULL((SELECT TOP (1) LTRIM(RTRIM(g.Grade)) FROM dbo.MaterialGrade g
                        WHERE g.PartNo = l.PartNo AND g.SubscID = @SubscID AND g.DeleteStatus = 'N'
                          AND UPPER(LTRIM(RTRIM(g.Grade))) = UPPER(l.Grade)), l.Grade),
               l.Unit, l.Qty, l.Rate, ROUND(l.Qty * l.Rate, 2),
               l.BinNo, l.Store, l.Location, l.HeatNo, l.TCNo, l.Remarks, 'N', @SubscID
          FROM #L l ORDER BY l.n;

        COMMIT TRANSACTION;
        SET @Message = N'Opening stock saved.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = LEFT(ERROR_MESSAGE(), 200); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  2b. SP_OpeningStockStatus — @Action Approve | Unapprove | Delete
      Approve:   S → Y. Only ADMIN / MANAGER (an EXECUTIVE, or a user with no
                 type, cannot approve).
      Unapprove: Y → S, ADMIN only, with a reason. Refused when the stock it
                 added is already used (a balance above zero goes below zero).
      Delete:    not approved, with a reason; the lines go with it.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_OpeningStockStatus', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_OpeningStockStatus AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_OpeningStockStatus
    @ID        INT             OUTPUT,
    @MTransNo  INT,
    @Action    VARCHAR(20),
    @Reason    VARCHAR(250),
    @UserNo    INT,
    @SystemIP  VARCHAR(20),
    @SubscID   INT,
    @Message   NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Lock CHAR(1), @Type VARCHAR(10), @OAYear INT, @OANo INT,
            @UserType VARCHAR(50), @Why VARCHAR(250) = NULLIF(LTRIM(RTRIM(ISNULL(@Reason, ''))), ''),
            @Short NVARCHAR(150);

    SELECT @Lock = LockStatus, @Type = StockType, @OAYear = OAYear, @OANo = OANo
      FROM dbo.OpeningStockMst WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
    IF @Lock IS NULL BEGIN SET @Message = N'Opening stock document not found.'; RETURN; END
    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, '')))) FROM dbo.UserMaster WHERE MTransNo = @UserNo;

    IF @Action = 'Approve'
    BEGIN
        IF ISNULL(@UserType, '') NOT IN ('ADMIN', 'MANAGER')
        BEGIN SET @Message = N'Only an ADMIN or MANAGER can approve opening stock.'; RETURN; END
        IF @Lock = 'Y' BEGIN SET @Message = N'This opening stock is already approved.'; RETURN; END
        IF @Lock <> 'S' BEGIN SET @Message = N'Submit the draft before approving it.'; RETURN; END
        UPDATE dbo.OpeningStockMst
           SET LockStatus = 'Y', ApprovedBy = @UserNo, ApprovedOn = SYSDATETIME(), SystemIP = @SystemIP,
               ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(), LockCounter = LockCounter + 1
         WHERE MTransNo = @MTransNo AND LockStatus = 'S';
        IF @@ROWCOUNT > 0 BEGIN SET @ID = @MTransNo; SET @Message = N'Opening stock approved.'; END
        RETURN;
    END

    IF @Action = 'Unapprove'
    BEGIN
        IF ISNULL(@UserType, '') <> 'ADMIN' BEGIN SET @Message = N'Only an ADMIN can withdraw an approval.'; RETURN; END
        IF @Lock <> 'Y' BEGIN SET @Message = N'This opening stock is not approved.'; RETURN; END
        IF @Why IS NULL BEGIN SET @Message = N'A reason is required.'; RETURN; END
        BEGIN TRY
            BEGIN TRANSACTION;
            UPDATE dbo.OpeningStockMst
               SET LockStatus = 'S', ApprovedBy = NULL, ApprovedOn = NULL, SystemIP = @SystemIP,
                   Remarks = LEFT(ISNULL(Remarks + ' ', '') + '[Approval withdrawn: ' + @Why + ']', 250),
                   ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(), LockCounter = LockCounter + 1
             WHERE MTransNo = @MTransNo;

            -- The stock it added may already be issued: then the count stays.
            -- "Used" = the balance was above zero with this count and goes
            -- below zero without it (a part already negative for other
            -- reasons does not block the correction).
            IF @Type = 'OWN'
                SELECT TOP (1) @Short = l.PartCode + N' / ' + l.Grade
                  FROM (SELECT d.PartNo, MAX(d.PartCode) AS PartCode, LTRIM(RTRIM(d.Grade)) AS Grade, SUM(d.Qty) AS Qty
                          FROM dbo.OpeningStockDtl d
                         WHERE d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'
                         GROUP BY d.PartNo, LTRIM(RTRIM(d.Grade))) l
                 CROSS APPLY (SELECT SUM(s.InQty - s.OutQty) AS bal FROM dbo.VW_StoreStock s
                               WHERE s.SubscID = @SubscID AND s.PartNo = l.PartNo AND s.Grade = l.Grade) b
                 WHERE b.bal < 0 AND b.bal + l.Qty > 0;
            ELSE
                SELECT TOP (1) @Short = l.PartCode + N' / ' + l.Grade
                  FROM (SELECT d.PartNo, MAX(d.PartCode) AS PartCode, LTRIM(RTRIM(d.Grade)) AS Grade, SUM(d.Qty) AS Qty
                          FROM dbo.OpeningStockDtl d
                         WHERE d.MTransNo = @MTransNo AND d.DeleteStatus = 'N'
                         GROUP BY d.PartNo, LTRIM(RTRIM(d.Grade))) l
                  JOIN dbo.VW_CustomerMaterialStock v ON v.SubscID = @SubscID AND v.OAYear = @OAYear AND v.OANo = @OANo
                                                     AND v.PartNo = l.PartNo AND v.Grade = l.Grade
                 WHERE v.BalanceQty < 0 AND v.BalanceQty + l.Qty > 0;
            IF @Short IS NOT NULL
            BEGIN
                ROLLBACK TRANSACTION;
                SET @Message = N'The approval cannot be withdrawn: ' + @Short + N' from this opening stock has already been issued.';
                RETURN;
            END
            COMMIT TRANSACTION;
            SET @ID = @MTransNo; SET @Message = N'Approval withdrawn.';
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            SET @ID = 0; SET @Message = LEFT(ERROR_MESSAGE(), 200); THROW;
        END CATCH
        RETURN;
    END

    IF @Action = 'Delete'
    BEGIN
        IF @Lock = 'Y' BEGIN SET @Message = N'An approved opening stock cannot be deleted.'; RETURN; END
        IF @Why IS NULL BEGIN SET @Message = N'A reason is required.'; RETURN; END
        BEGIN TRY
            BEGIN TRANSACTION;
            UPDATE dbo.OpeningStockDtl SET DeleteStatus = 'Y' WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
            UPDATE dbo.OpeningStockMst
               SET DeleteStatus = 'Y', DeleteReason = @Why, ModifiedBy = @UserNo, ModifiedOn = SYSDATETIME(), SystemIP = @SystemIP
             WHERE MTransNo = @MTransNo AND LockStatus <> 'Y';
            COMMIT TRANSACTION;
            SET @ID = @MTransNo; SET @Message = N'Opening stock deleted.';
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            SET @ID = 0; SET @Message = LEFT(ERROR_MESSAGE(), 200); THROW;
        END CATCH
        RETURN;
    END

    SET @Message = N'Action must be Approve, Unapprove or Delete.';
END
GO

/*------------------------------------------------------------------------------
  3. VW_StoreStock — same as SQL 67, plus the approved OWN opening lines
------------------------------------------------------------------------------*/
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
        D.PartNo, NULL, '',
        NULL, NULL,
        CAST(0 AS NUMERIC(14,4)), CAST(ISNULL(D.Qty, 0) AS NUMERIC(14,4)),
        M.SubscID
  FROM  dbo.STxnMst M
  JOIN  dbo.STxnDtl D ON D.MTransNo = M.MTransNo
 WHERE  M.DeleteStatus = 'N' AND D.DeleteStatus = 'N';
GO

/*------------------------------------------------------------------------------
  4. VW_CustomerMaterialStock — same as SQL 67, plus the approved CUSTOMER
     opening lines (received on their OA), and the name from Customer Master
     when no inward carries it
------------------------------------------------------------------------------*/
ALTER VIEW dbo.VW_CustomerMaterialStock
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
    -- SQL 72: approved customer-material opening stock (on its OA)
    SELECT  m.SubscID, m.CustomerNo, m.OAYear, m.OANo, d.PartNo, d.GradeNo,
            LTRIM(RTRIM(ISNULL(d.Grade, ''))), NULL, ISNULL(d.Unit, ''),
            d.Qty, 0, 0, NULL, CAST(m.AsOnDate AS DATETIME)
      FROM  dbo.OpeningStockMst m
      JOIN  dbo.OpeningStockDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
     WHERE  m.DeleteStatus = 'N' AND m.LockStatus = 'Y' AND m.StockType = 'CUSTOMER'
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
        ISNULL(cn.CustomerName, cm.CustomerName)                                 AS CustomerName,
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
               ORDER BY x.MTransNo DESC) cn
  LEFT JOIN dbo.CustomerMaster cm ON cm.MTransNo = a.CustomerNo;
GO

/*------------------------------------------------------------------------------
  5. SP_StoreIssueDtlPlanFields — same as SQL 67, plus: a heat on an approved
     opening line is valid (own: the part's; customer: the same OA's)
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
           -- SQL 72: or a heat on an approved own opening stock of the part
           AND NOT EXISTS (
               SELECT 1
                 FROM dbo.OpeningStockDtl d
                 JOIN dbo.OpeningStockMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N'
                                           AND m.LockStatus = 'Y' AND m.StockType = 'OWN'
                WHERE m.SubscID = @SubscID AND d.DeleteStatus = 'N' AND d.PartNo = @PartNo
                  AND UPPER(LTRIM(RTRIM(d.HeatNo))) = @H
                  AND (@Grade = '' OR LTRIM(RTRIM(ISNULL(d.Grade, ''))) IN ('', @Grade)))
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
           -- SQL 72: or a heat on the customer's approved opening stock for this OA
           AND NOT EXISTS (
               SELECT 1
                 FROM dbo.OpeningStockDtl d
                 JOIN dbo.OpeningStockMst m ON m.MTransNo = d.MTransNo AND m.DeleteStatus = 'N'
                                           AND m.LockStatus = 'Y' AND m.StockType = 'CUSTOMER'
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
  6. Menu: Store → Opening Stock (last in the Store section)
------------------------------------------------------------------------------*/
DECLARE @Page VARCHAR(100) = 'Modules/Store/OpeningStock.html';
IF NOT EXISTS (SELECT 1 FROM dbo.Menulist WHERE ModulePage = @Page)
BEGIN
    DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);
    INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    SELECT TOP (1) @Max + 1, 'EMS', s.CSLNo, 'Store', s.SSLNo, 'Opening Stock',
           (SELECT ISNULL(MAX(MSLNo), 0) + 1 FROM dbo.Menulist WHERE Category = 'EMS' AND Section = 'Store'),
           'bi-box-seam', @Page
      FROM dbo.Menulist s
     WHERE s.Category = 'EMS' AND s.Section = 'Store'
     ORDER BY s.MSLNo;
    PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) added.';
END
ELSE PRINT 'Menu row already present.';

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT MTransNo FROM dbo.Menulist WHERE ModulePage = @Page) M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added.';
GO

PRINT '72_OpeningStock applied.';
GO
