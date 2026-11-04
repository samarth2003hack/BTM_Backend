/*==============================================================================
  60. CUSTOMER MATERIAL INWARD  -  Modules/Store/CustomerMaterialInward.html

  Job work: some customers send their own steel bar to be forged. The bar
  arrives on the CUSTOMER's Delivery Challan and is recorded against the
  customer's Order Acceptance. It is NEVER our stock - it is kept in the
  same stock picture under a second owner ("one ledger, two owners"):

      our stock        GRN / Store Issue / Store Returns   (scripts 29-46)
      customer stock   Customer Material Inward  (+)        (this script)
                       Store Issue of customer bar (-)      (later)
                       Outward DC returning bar / scrap (-) (later)

  Not RGP (our material going out and back) and not GRN (purchased
  material): neither table is touched here.

      Customer DC ─► CustMatInwardMst (one per DC, against one OA)
                        └─► CustMatInwardDtl (one per bar lot: RM part,
                            grade, dia, kg, heat, TC, optional OA line)
                     VW_CustomerMaterialStock  received - issued - returned
                     per customer x OA x RM part x grade x dia x unit

  Numbering: per subscription and financial year (April start; 2026-11-04
  is year 2627).  CMI-2627-00001.  SP_CustMatInwardMst allocates the
  number at insert, under UPDLOCK, exactly like SP_JobCardMst (SQL 53).

  Objects:
      CustMatInwardMst, CustMatInwardDtl
      VW_CustomerMaterialStock     the customer-owned balance
      SP_CustMatInwardMst          'Insert' | 'Update' | 'Delete'
      SP_CustMatInwardDtlSave      replace-all line save from JSON

  No menu rows here - the menu entry is added by hand. Page path:
      Modules/Store/CustomerMaterialInward.html

  OADtl.CustomerMaterial (CHAR(1) 'Y'/'N', added by SQL 58) marks the OA
  lines the customer supplies bar for. Nothing here depends on it: the
  API reads it only when COL_LENGTH says it exists.

  Depends on dbo.FN_FYCode (SQL 53 / 55).
  SP_ names: create-stub-then-ALTER (master shadows SP_ procedures).
  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. TABLES
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.CustMatInwardMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustMatInwardMst
    (
        MTransNo        INT IDENTITY(1, 1) NOT NULL,
        DocYear         INT             NOT NULL,       -- financial-year code, 2627
        DocNo           INT             NOT NULL,       -- 1, 2, 3 ... per SubscID + DocYear
        InwardNo        VARCHAR(30)     NOT NULL,       -- CMI-2627-00001
        InwardDate      DATE            NOT NULL,

        CustomerNo      INT             NOT NULL,       -- OAMst.VendorNo (CustomerMaster)
        CustomerName    VARCHAR(100)    NULL,           -- snapshot off the OA at save
        OAYear          INT             NOT NULL,       -- OAMst.DocYear
        OANo            INT             NOT NULL,       -- OAMst.DocNo

        DCNo            VARCHAR(30)     NOT NULL,       -- the customer's delivery challan
        DCDate          DATE            NOT NULL,
        ReceivedBy      VARCHAR(100)    NULL,
        Remarks         VARCHAR(250)    NULL,
        DocStatus       VARCHAR(20)     NOT NULL CONSTRAINT DF_CustMatInwardMst_DocStatus   DEFAULT ('Received'),
                                                        -- Received | Verified (weight / heat / TC checked)

        SystemIP        VARCHAR(20)     NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_CustMatInwardMst_LockStatus  DEFAULT ('N'),
        LockCounter     INT             NOT NULL CONSTRAINT DF_CustMatInwardMst_LockCounter DEFAULT (0),
        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_CustMatInwardMst_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_CustMatInwardMst_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_CustMatInwardMst PRIMARY KEY CLUSTERED (MTransNo)
    );

    CREATE UNIQUE INDEX UQ_CustMatInwardMst_No ON dbo.CustMatInwardMst (SubscID, DocYear, DocNo);
    CREATE INDEX IX_CustMatInwardMst_OA ON dbo.CustMatInwardMst (SubscID, OAYear, OANo) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_CustMatInwardMst_DC ON dbo.CustMatInwardMst (SubscID, CustomerNo, DCNo) WHERE DeleteStatus = 'N';
END;
GO

IF OBJECT_ID(N'dbo.CustMatInwardDtl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustMatInwardDtl
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        MTransNo        INT             NOT NULL,       -- CustMatInwardMst.MTransNo
        SlNo            INT             NOT NULL,       -- order within the saved set

        PartNo          INT             NOT NULL,       -- RM part (PartMaster.MTransNo)
        GradeNo         INT             NULL,           -- MaterialGrade.GradeNo of PartNo
        Grade           VARCHAR(50)     NOT NULL,
        BarDia          NUMERIC(9, 2)   NULL,           -- mm, optional
        Qty             NUMERIC(18, 3)  NOT NULL,       -- kg
        Unit            VARCHAR(20)     NULL,
        HeatNo          VARCHAR(30)     NULL,
        TCNo            VARCHAR(30)     NULL,
        OAID            INT             NULL,           -- OADtl.SlNo the bar is for (optional)
        OALinkNo        INT             NULL,           -- OADtl.TransSlNo at save
        Remarks         VARCHAR(250)    NULL,

        CreatedBy       INT             NOT NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_CustMatInwardDtl_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_CustMatInwardDtl_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_CustMatInwardDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );

    CREATE INDEX IX_CustMatInwardDtl_Mst  ON dbo.CustMatInwardDtl (MTransNo) WHERE DeleteStatus = 'N';
    CREATE INDEX IX_CustMatInwardDtl_Part ON dbo.CustMatInwardDtl (SubscID, PartNo) INCLUDE (Qty) WHERE DeleteStatus = 'N';
END;
GO

/*------------------------------------------------------------------------------
  2. VW_CustomerMaterialStock - customer-owned bar, one row per
     SubscID x customer x OA x RM part x grade x bar dia x unit

      ReceivedQty   Σ live Customer Material Inward lines
      IssuedQty     PLACEHOLDER (always 0): Store Issue of customer bar to a
                    job card will feed this - issues are NOT read yet
      ReturnedQty   PLACEHOLDER (always 0): an outward DC returning unused bar
                    / scrap to the customer will feed this - not read yet
      BalanceQty    ReceivedQty - IssuedQty - ReturnedQty

  When the issue / return documents exist, add them to the "mv" union as
  negative movements and turn the two placeholders into SUMs - the column
  names and the BalanceQty formula stay as they are, so every reader
  (CustomerMaterialInwardController Balance / BalanceByOA, the RM
  Requirement report) keeps working unchanged.
------------------------------------------------------------------------------*/
CREATE OR ALTER VIEW dbo.VW_CustomerMaterialStock
AS
WITH mv AS (
    -- Receipts: the customer's bar coming in on its DC.
    SELECT  m.SubscID, m.CustomerNo, m.OAYear, m.OANo,
            d.PartNo, d.GradeNo, d.Grade, d.BarDia, ISNULL(d.Unit, '') AS Unit,
            d.Qty               AS ReceivedQty,
            m.MTransNo          AS InwardTransNo,
            m.InwardDate
      FROM  dbo.CustMatInwardMst m
      JOIN  dbo.CustMatInwardDtl d ON d.MTransNo = m.MTransNo AND d.DeleteStatus = 'N'
     WHERE  m.DeleteStatus = 'N'
    -- UNION ALL  Store Issue of customer bar  (later: IssuedQty)
    -- UNION ALL  outward DC back to customer  (later: ReturnedQty)
),
agg AS (
    SELECT  SubscID, CustomerNo, OAYear, OANo, PartNo, GradeNo, Grade, BarDia, Unit,
            SUM(ReceivedQty)                AS ReceivedQty,
            COUNT(DISTINCT InwardTransNo)   AS InwardCount,
            MAX(InwardDate)                 AS LastInwardDate
      FROM  mv
     GROUP  BY SubscID, CustomerNo, OAYear, OANo, PartNo, GradeNo, Grade, BarDia, Unit
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
        a.Unit,
        CAST(a.ReceivedQty AS NUMERIC(18, 3))                   AS ReceivedQty,
        CAST(0 AS NUMERIC(18, 3))                               AS IssuedQty,     -- PLACEHOLDER, see header
        CAST(0 AS NUMERIC(18, 3))                               AS ReturnedQty,   -- PLACEHOLDER, see header
        CAST(a.ReceivedQty - 0 - 0 AS NUMERIC(18, 3))           AS BalanceQty,
        a.InwardCount,
        a.LastInwardDate
  FROM  agg a
  LEFT JOIN dbo.PartMaster p ON p.MTransNo = a.PartNo
 OUTER APPLY (SELECT TOP (1) x.CustomerName FROM dbo.CustMatInwardMst x
               WHERE x.SubscID = a.SubscID AND x.CustomerNo = a.CustomerNo AND x.DeleteStatus = 'N'
               ORDER BY x.MTransNo DESC) cn;
GO

/*------------------------------------------------------------------------------
  3. SP_CustMatInwardMst - @Status 'Insert' | 'Update' | 'Delete'
     Returns @ID = MTransNo (0 + @Message when refused).

     The customer is never taken from the caller: it is read off the live,
     current revision of the OA (OAMst.VendorNo / VendorName). @CustomerNo,
     when given, is only cross-checked. A saved inward stays on its OA.
     One customer DC is recorded once per financial year of the DC.
     Delete is soft, needs a reason, and is refused if it would leave a
     customer balance below zero (bar already issued - see the view).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_CustMatInwardMst', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_CustMatInwardMst AS BEGIN SET NOCOUNT ON; END');
GO

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
    @SubscID       INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @No VARCHAR(30), @CurOAYear INT, @CurOANo INT, @CurCust INT;
    DECLARE @Cust INT, @CustName VARCHAR(100);

    BEGIN TRY
        IF @Status NOT IN ('Insert', 'Update', 'Delete')
        BEGIN SET @Message = N'Unknown status.'; RETURN; END

        IF @Status IN ('Update', 'Delete')
        BEGIN
            SELECT @No = InwardNo, @CurOAYear = OAYear, @CurOANo = OANo, @CurCust = CustomerNo
              FROM dbo.CustMatInwardMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
            IF @No IS NULL
            BEGIN SET @Message = N'Customer material inward not found.'; RETURN; END
        END

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

            -- Bar already issued out of this receipt cannot be un-received.
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
        SET @DocStatus = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@DocStatus, ''))), ''), 'Received');

        IF @InwardDate IS NULL
        BEGIN SET @Message = N'Inward Date is required.'; RETURN; END
        IF @DCNo = ''
        BEGIN SET @Message = N'Customer DC No is required.'; RETURN; END
        IF @DCDate IS NULL
        BEGIN SET @Message = N'DC Date is required.'; RETURN; END
        IF @DCDate > @InwardDate
        BEGIN SET @Message = N'The DC Date cannot be after the Inward Date.'; RETURN; END
        IF @DocStatus NOT IN ('Received', 'Verified')
        BEGIN SET @Message = N'Status must be Received or Verified.'; RETURN; END

        IF @Status = 'Update' AND (@OAYear <> @CurOAYear OR @OANo <> @CurOANo)
        BEGIN SET @Message = N'A saved inward stays on its OA - delete it and enter it again against the other OA.'; RETURN; END

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
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N' AND CustomerNo = @Cust
                      AND DCNo = @DCNo AND dbo.FN_FYCode(DCDate) = dbo.FN_FYCode(@DCDate)
                      AND MTransNo <> CASE WHEN @Status = 'Update' THEN @MTransNo ELSE 0 END)
        BEGIN
            SET @Message = N'Customer DC ' + CONVERT(NVARCHAR(30), @DCNo) + N' is already recorded for this customer.';
            RETURN;
        END

        /*---------------- Insert ----------------*/
        IF @Status = 'Insert'
        BEGIN
            BEGIN TRANSACTION;
                DECLARE @Year INT = dbo.FN_FYCode(@InwardDate);
                DECLARE @DocNo INT =
                    ISNULL((SELECT MAX(DocNo) FROM dbo.CustMatInwardMst WITH (UPDLOCK, HOLDLOCK)
                             WHERE SubscID = @SubscID AND DocYear = @Year), 0) + 1;

                INSERT INTO dbo.CustMatInwardMst
                       (DocYear, DocNo, InwardNo, InwardDate, CustomerNo, CustomerName, OAYear, OANo,
                        DCNo, DCDate, ReceivedBy, Remarks, DocStatus, SystemIP, CreatedBy, CreatedOn, SubscID)
                VALUES (@Year, @DocNo, CONCAT('CMI-', @Year, '-', RIGHT('00000' + CAST(@DocNo AS VARCHAR(10)), 5)),
                        @InwardDate, @Cust, @CustName, @OAYear, @OANo,
                        @DCNo, @DCDate, NULLIF(LTRIM(RTRIM(@ReceivedBy)), ''), NULLIF(LTRIM(RTRIM(@Remarks)), ''),
                        @DocStatus, @SystemIP, @UserNo, SYSDATETIME(), @SubscID);
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
  4. SP_CustMatInwardDtlSave - replace an inward's whole line set
     @Lines = JSON array, in serial order (SlNo = array position + 1):
       [{ "partNo": 12, "gradeNo": 1, "grade": "EN8", "barDia": 40,
          "qty": 1250.5, "unit": "KG", "heatNo": "H123", "tcNo": "TC9",
          "oaID": 1, "remarks": "" }, ...]
     The old set is soft-deleted ('Superseded'), so history stays.
     Each line is checked: a live part, a live grade of that part (by
     gradeNo, else by grade text), qty above zero, dia not negative, and an
     OA line - when given - of the inward's own OA. Unit defaults to KG
     (customer bar is booked by weight).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_CustMatInwardDtlSave', N'P') IS NULL
    EXEC (N'CREATE PROCEDURE dbo.SP_CustMatInwardDtlSave AS BEGIN SET NOCOUNT ON; END');
GO

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
    DECLARE @No VARCHAR(30), @OAYear INT, @OANo INT, @Cust INT, @Txt NVARCHAR(200);

    BEGIN TRY
        SELECT @No = InwardNo, @OAYear = OAYear, @OANo = OANo, @Cust = CustomerNo
          FROM dbo.CustMatInwardMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';
        IF @No IS NULL
        BEGIN SET @Message = N'Customer material inward not found.'; RETURN; END

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

        -- OA line -> its TransSlNo, on the inward's own OA (live, current revision).
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

        -- A smaller receipt may not undercut bar already issued / returned.
        IF EXISTS (SELECT 1 FROM dbo.VW_CustomerMaterialStock
                    WHERE SubscID = @SubscID AND CustomerNo = @Cust
                      AND OAYear = @OAYear AND OANo = @OANo AND BalanceQty < 0)
        BEGIN
            ROLLBACK TRANSACTION;
            SET @Message = CONVERT(NVARCHAR(30), @No)
                         + N': the new quantities are below what has already been issued or returned.';
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

-- What was written.
SELECT name, type_desc FROM sys.objects
 WHERE name IN ('CustMatInwardMst', 'CustMatInwardDtl', 'VW_CustomerMaterialStock',
                'SP_CustMatInwardMst', 'SP_CustMatInwardDtlSave')
 ORDER BY type_desc, name;

GO
