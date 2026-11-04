/*==============================================================================
  50. QUOTATION FOLLOW-UP  -  Modules/Sales/FollowUp.html

  The chase list for quotations no order has come back on.

  An OPEN quotation is the live, current revision (DeleteStatus 'N',
  DocAmdStatus 'N') with DocStatus 'N' and no live Order Acceptance raised
  against any of its revisions. Nothing sets QtnMst.DocStatus 'Y' when an OA
  is made, so the OA check is what takes a converted quotation off the list.

  A quotation's follow-ups belong to the DOCUMENT, not to one revision: the
  log and the roll-up read every revision with the same SubscID / DocSeries /
  DocYear / DocNo, so amending a quotation does not wipe its history. New
  entries are written against the current revision.

  Objects:
      QtnFollowUp                          one row per contact
      QtnMst.TruncateReason / TruncatedBy / TruncatedOn
      SP_QtnFollowUp   @Status 'Insert' | 'Update' | 'Delete'
      SP_QtnTruncate   DocStatus 'N' -> 'T' (+ reason); nothing is deleted
      Menulist 808     Sales -> Quotation Follow-Up (+ grants)

  The new QtnMst columns are NOT mapped on the EF QtnMst entity, so the API
  can be deployed before or after this script; only the follow-up endpoints
  need it.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. TABLE
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.QtnFollowUp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QtnFollowUp
    (
        TransSlNo       INT IDENTITY(1, 1) NOT NULL,
        SLNo            NUMERIC(12, 2)  NULL,           -- 1, 2, 3 ... per quotation document
        MTransNo        INT             NOT NULL,       -- QtnMst revision current when recorded
        FollowupDate    DATE            NOT NULL,
        FollowUpType    VARCHAR(30)     NOT NULL,       -- medium: Call / Email / WhatsApp / Visit / Meeting / Video
        ContactPerson   VARCHAR(100)    NULL,
        Response        VARCHAR(30)     NOT NULL,       -- Awaiting / Interested / ... / NotInterested
        NextVisitDate   DATE            NULL,           -- NULL = chase closed (On Hold / Not Interested)
        FollowupNotes   VARCHAR(2000)   NOT NULL,

        SystemIP        VARCHAR(20)     NULL,
        CreatedBy       INT             NULL,
        CreatedOn       DATETIME2       NOT NULL CONSTRAINT DF_QtnFollowUp_CreatedOn    DEFAULT (SYSDATETIME()),
        ModifiedBy      INT             NULL,
        ModifiedOn      DATETIME2       NULL,
        DeleteStatus    CHAR(1)         NOT NULL CONSTRAINT DF_QtnFollowUp_DeleteStatus DEFAULT ('N'),
        DeleteReason    VARCHAR(250)    NULL,
        LockStatus      CHAR(1)         NOT NULL CONSTRAINT DF_QtnFollowUp_LockStatus   DEFAULT ('N'),
        LockCounter     INT             NULL,
        SubscID         INT             NOT NULL,

        CONSTRAINT PK_QtnFollowUp PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_QtnFollowUp_QtnMst FOREIGN KEY (MTransNo) REFERENCES dbo.QtnMst (MTransNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_QtnFollowUp_MTransNo' AND object_id = OBJECT_ID(N'dbo.QtnFollowUp'))
    CREATE NONCLUSTERED INDEX IX_QtnFollowUp_MTransNo
        ON dbo.QtnFollowUp (MTransNo, DeleteStatus)
        INCLUDE (FollowupDate, NextVisitDate);
GO

/*------------------------------------------------------------------------------
  2. TRUNCATE COLUMNS ON QtnMst
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.QtnMst', 'TruncateReason') IS NULL
    ALTER TABLE dbo.QtnMst ADD TruncateReason VARCHAR(500) NULL;
IF COL_LENGTH('dbo.QtnMst', 'TruncatedBy') IS NULL
    ALTER TABLE dbo.QtnMst ADD TruncatedBy INT NULL;
IF COL_LENGTH('dbo.QtnMst', 'TruncatedOn') IS NULL
    ALTER TABLE dbo.QtnMst ADD TruncatedOn DATETIME2 NULL;
GO

/*------------------------------------------------------------------------------
  3. SP_QtnFollowUp

  Every status first resolves the quotation document from @MTransNo and finds
  its current revision; writes are refused once the quotation is no longer
  open (truncated, converted into an OA, or deleted).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_QtnFollowUp', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnFollowUp AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnFollowUp
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @FollowupDate   DATE,
    @FollowUpType   VARCHAR(30),
    @ContactPerson  VARCHAR(100),
    @Response       VARCHAR(30),
    @NextVisitDate  DATE,
    @FollowupNotes  VARCHAR(2000),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Insert' | 'Update' | 'Delete'
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Series CHAR(10), @Year INT, @DocNo INT,
            @CurTransNo INT, @CurDocStatus CHAR(1);

    -- The document @MTransNo belongs to (any revision, even a deleted one).
    SELECT @Series = DocSeries, @Year = DocYear, @DocNo = DocNo
      FROM dbo.QtnMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID;

    -- Its live, current revision.
    SELECT TOP (1) @CurTransNo = MTransNo, @CurDocStatus = DocStatus
      FROM dbo.QtnMst
     WHERE SubscID = @SubscID AND DocSeries = @Series AND DocYear = @Year AND DocNo = @DocNo
       AND DeleteStatus = 'N' AND DocAmdStatus = 'N'
     ORDER BY DocAmdNo DESC;

    IF (@CurTransNo IS NULL)
    BEGIN SET @Message = N'Quotation not found.'; RETURN; END

    IF (@CurDocStatus = 'T')
    BEGIN SET @Message = N'This quotation has been truncated; its follow-ups can no longer be changed.'; RETURN; END

    IF (@CurDocStatus <> 'N')
    BEGIN SET @Message = N'This quotation is closed; its follow-ups can no longer be changed.'; RETURN; END

    IF EXISTS (SELECT 1
                 FROM dbo.OAMst o
                 JOIN dbo.QtnMst r ON r.MTransNo = o.TargetTransNo
                WHERE o.SubscID = @SubscID AND o.DeleteStatus = 'N'
                  AND r.SubscID = @SubscID AND r.DocSeries = @Series
                  AND r.DocYear = @Year    AND r.DocNo     = @DocNo)
    BEGIN SET @Message = N'An Order Acceptance has been raised against this quotation, so it is no longer followed up.'; RETURN; END

    IF (@Status IN ('Insert', 'Update'))
    BEGIN
        SET @FollowUpType  = NULLIF(LTRIM(RTRIM(@FollowUpType)), '');
        SET @Response      = NULLIF(LTRIM(RTRIM(@Response)), '');
        SET @FollowupNotes = NULLIF(LTRIM(RTRIM(@FollowupNotes)), '');
        SET @ContactPerson = NULLIF(LTRIM(RTRIM(@ContactPerson)), '');

        IF (@FollowupDate IS NULL)
        BEGIN SET @Message = N'Follow-up Date is required.'; RETURN; END
        -- One day of slack: the server clock may run behind the user's day.
        IF (@FollowupDate > DATEADD(DAY, 1, CAST(GETDATE() AS DATE)))
        BEGIN SET @Message = N'Follow-up Date cannot be in the future.'; RETURN; END
        IF (@FollowUpType IS NULL)
        BEGIN SET @Message = N'Medium is required.'; RETURN; END
        IF (@Response IS NULL)
        BEGIN SET @Message = N'Response is required.'; RETURN; END
        IF (@FollowupNotes IS NULL)
        BEGIN SET @Message = N'Notes / Remarks are required.'; RETURN; END
        IF (@NextVisitDate IS NOT NULL AND @NextVisitDate < @FollowupDate)
        BEGIN SET @Message = N'The next follow-up cannot be before the follow-up itself.'; RETURN; END
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert')
        BEGIN
            DECLARE @SLNo NUMERIC(12, 2);
            SELECT @SLNo = ISNULL(MAX(f.SLNo), 0) + 1
              FROM dbo.QtnFollowUp f
              JOIN dbo.QtnMst r ON r.MTransNo = f.MTransNo
             WHERE r.SubscID = @SubscID AND r.DocSeries = @Series
               AND r.DocYear = @Year    AND r.DocNo     = @DocNo
               AND f.DeleteStatus = 'N';

            INSERT INTO dbo.QtnFollowUp
            (
                SLNo, MTransNo, FollowupDate, FollowUpType, ContactPerson, Response,
                NextVisitDate, FollowupNotes, SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @SLNo, @CurTransNo, @FollowupDate, @FollowUpType, @ContactPerson, @Response,
                @NextVisitDate, @FollowupNotes, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, SYSDATETIME()),
                'N', 'N', 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = N'Follow-up saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.QtnFollowUp
               SET FollowupDate  = @FollowupDate,
                   FollowUpType  = @FollowUpType,
                   ContactPerson = @ContactPerson,
                   Response      = @Response,
                   NextVisitDate = @NextVisitDate,
                   FollowupNotes = @FollowupNotes,
                   SystemIP      = @SystemIP,
                   ModifiedBy    = @ModifiedBy,
                   ModifiedOn    = ISNULL(@ModifiedOn, SYSDATETIME()),
                   LockCounter   = ISNULL(LockCounter, 0) + 1
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = N'Follow-up updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = N'Follow-up not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.QtnFollowUp
               SET DeleteStatus = 'Y',
                   DeleteReason = ISNULL(@DeleteReason, 'No reason provided'),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = ISNULL(@ModifiedOn, SYSDATETIME())
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = N'Follow-up deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = N'Follow-up not found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = N'Invalid status.';
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
  4. SP_QtnTruncate

  Moves the current revision's DocStatus from 'N' to 'T' and keeps the reason.
  The quotation and its follow-ups stay on record. Refused once an OA exists
  against any revision - that quotation was won, not lost.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_QtnTruncate', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnTruncate AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnTruncate
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @TruncateReason VARCHAR(500),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @SubscID        INT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @Series CHAR(10), @Year INT, @DocNo INT,
            @AmdStatus CHAR(1), @DocStatus CHAR(1);

    SELECT @Series = DocSeries, @Year = DocYear, @DocNo = DocNo,
           @AmdStatus = DocAmdStatus, @DocStatus = DocStatus
      FROM dbo.QtnMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    IF (@AmdStatus IS NULL)
    BEGIN SET @Message = N'Quotation not found.'; RETURN; END
    IF (@AmdStatus = 'Y')
    BEGIN SET @Message = N'Only the current revision of a quotation can be truncated.'; RETURN; END
    IF (@DocStatus = 'T')
    BEGIN SET @Message = N'This quotation has already been truncated.'; RETURN; END
    IF (@DocStatus <> 'N')
    BEGIN SET @Message = N'This quotation is already closed.'; RETURN; END
    IF EXISTS (SELECT 1
                 FROM dbo.OAMst o
                 JOIN dbo.QtnMst r ON r.MTransNo = o.TargetTransNo
                WHERE o.SubscID = @SubscID AND o.DeleteStatus = 'N'
                  AND r.SubscID = @SubscID AND r.DocSeries = @Series
                  AND r.DocYear = @Year    AND r.DocNo     = @DocNo)
    BEGIN SET @Message = N'An Order Acceptance has been raised against this quotation, so it cannot be truncated.'; RETURN; END

    BEGIN TRY
        UPDATE dbo.QtnMst
           SET DocStatus      = 'T',
               TruncateReason = NULLIF(LTRIM(RTRIM(@TruncateReason)), ''),
               TruncatedBy    = @ModifiedBy,
               TruncatedOn    = SYSDATETIME(),
               SystemIP       = @SystemIP,
               ModifiedBy     = @ModifiedBy,
               ModifiedOn     = SYSDATETIME()
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID
           AND DeleteStatus = 'N' AND DocStatus = 'N';

        IF @@ROWCOUNT > 0
        BEGIN SET @ID = @MTransNo; SET @Message = N'Quotation truncated successfully.'; END
        ELSE
        BEGIN SET @Message = N'Quotation not found.'; END
    END TRY
    BEGIN CATCH
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  5. SIDEBAR ROW 808 + GRANTS

  Sales section (SSLNo 6), after Sales Invoice. Every template that already
  holds grants gets full access, Department copied from its existing rows.
------------------------------------------------------------------------------*/
MERGE dbo.Menulist AS T
USING (SELECT 808 AS MTransNo, 'Sales' AS Section, 6 AS SSLNo,
              'Quotation Follow-Up' AS ModuleName, 8 AS MSLNo,
              'bi-telephone-outbound' AS ModuleIcon,
              'Modules/Sales/FollowUp.html' AS ModulePage) AS S
   ON T.MTransNo = S.MTransNo
WHEN MATCHED THEN
    UPDATE SET T.Category   = 'EMS',
               T.CSLNo      = 1,
               T.Section    = S.Section,
               T.SSLNo      = S.SSLNo,
               T.ModuleName = S.ModuleName,
               T.MSLNo      = S.MSLNo,
               T.ModuleIcon = S.ModuleIcon,
               T.ModulePage = S.ModulePage
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    VALUES (S.MTransNo, 'EMS', 1, S.Section, S.SSLNo, S.ModuleName, S.MSLNo, S.ModuleIcon, S.ModulePage);

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT  T.TemplateName, T.Department, 808, 'A', 'N', T.SubscID
  FROM  (
            SELECT TemplateName, SubscID, MAX(Department) AS Department
              FROM dbo.AccessTemplate
             WHERE TemplateName IS NOT NULL
             GROUP BY TemplateName, SubscID
        ) T
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.AccessTemplate A
            WHERE A.TemplateName = T.TemplateName
              AND A.SubscID      = T.SubscID
              AND A.ModuleNo     = 808);
GO

-- What was written.
SELECT OBJECT_ID(N'dbo.QtnFollowUp')    AS QtnFollowUp,
       OBJECT_ID(N'dbo.SP_QtnFollowUp') AS SP_QtnFollowUp,
       OBJECT_ID(N'dbo.SP_QtnTruncate') AS SP_QtnTruncate,
       COL_LENGTH('dbo.QtnMst', 'TruncateReason') AS TruncateReasonLen,
       (SELECT COUNT(*) FROM dbo.AccessTemplate WHERE ModuleNo = 808) AS MenuGrants;
GO
