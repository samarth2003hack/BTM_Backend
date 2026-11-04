/*==============================================================================
  EMSSoft — 12_AuditLog.sql
  ------------------------------------------------------------------------------
  A single audit trail covering every module: who changed which record, when,
  from where, and — column by column — what the value was before and after.

  Idempotent. Safe to re-run. Run AFTER 11_MenulistFolders.sql.

  ------------------------------------------------------------------------------
  WHY TRIGGERS, AND NOT THE APPLICATION
  ------------------------------------------------------------------------------
  Every write in this system goes through a stored procedure (SP_BankMaster,
  SP_CustomerMaster, ...), so there is no single place in the C# layer that sees
  the before-image of a row. Auditing in the controllers would also mean 50
  controllers each remembering to do it, and would miss anything written by
  SP_TableUpdation, a migration script, or someone in SSMS.

  Auditing in the database catches all of it. The alternatives were weighed:

    Change Data Capture   Not available on SQL Server Express (this instance is
                          2022 Express). Also records no user.
    Temporal tables       Available on Express, and would give a clean row
                          history — but they record no user, no reason and no
                          action type, and every audited table would need its
                          schema altered plus a history table.
    AFTER triggers        Chosen. Catch every write path, see both images, and
                          can read CreatedBy / ModifiedBy straight off the row,
                          which the stored procedures already stamp on every
                          insert and update.

  ------------------------------------------------------------------------------
  HOW IT FITS TOGETHER
  ------------------------------------------------------------------------------
      dbo.AuditConfig          one row per audited table — what to call it,
                               which column is its key, which master it hangs
                               off. Edit this to turn a table on or off.
      dbo.AuditIgnoreColumn    columns never written to the trail. Housekeeping
                               (ModifiedOn, SystemIP, ...) and secrets
                               (UserMaster.PW, UserMaster.TokenNo).
      dbo.AuditLog             one row per record changed.
      dbo.AuditLogDtl          one row per column changed.

      dbo.TR_Audit_<Table>     six lines. Copies inserted/deleted into two temp
                               tables and hands them to the engine below.
      dbo.SP_AuditLog_Capture  the engine. Table-agnostic: it reads the row
                               images as JSON and diffs them, so it never names
                               a business column and never needs changing when
                               a table gains one.

      dbo.SP_AuditLog_Sync     registers eligible tables and builds their
                               triggers. Re-run it after adding a table.
      dbo.SP_AuditLog_Status   what is covered and what is not.
      dbo.SP_AuditLog_Purge    retention.
      dbo.VW_AuditLog          the trail with the user's name resolved.

  ------------------------------------------------------------------------------
  WHAT IS RECORDED, PER ACTION
  ------------------------------------------------------------------------------
      Insert       every non-null column, as NewValue
      Update       only the columns whose value actually differs
      SoftDelete   DeleteStatus 'N' -> 'Y', with the DeleteReason on the header
      Restore      DeleteStatus 'Y' -> 'N'
      Delete       a real DELETE — every non-null column, as OldValue

  An update that changes nothing auditable writes no row at all. This matters:
  the stored procedures re-assign every column on Update whether or not the
  user touched it, so without this the trail would fill with empty saves.

  ------------------------------------------------------------------------------
  KNOWN LIMITS
  ------------------------------------------------------------------------------
  * ChangedBy is taken from the row's ModifiedBy / CreatedBy, which is what the
    stored procedures stamp. A row edited directly in SSMS without touching
    ModifiedBy is attributed to whoever last edited it through the app; the
    DBUser / HostName / AppName columns on the header are what tell you that
    really happened. Ways to tighten this are noted at SP_AuditLog_Capture.
  * A statement that changes a row's primary key is logged as a delete plus an
    insert. No table here has an updatable key, so this does not arise today.
  * Values are compared as text under a binary collation, so a change of case
    alone is recorded as a change.
  * The trigger never fails a business write: a fault inside it is swallowed and
    written to dbo.ErrorLog under LoginType 'AuditLog'.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  1. dbo.AuditConfig — which tables are audited
==============================================================================*/
IF OBJECT_ID('dbo.AuditConfig', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditConfig
    (
        MTransNo        INT IDENTITY(1,1) NOT NULL,
        TableName       VARCHAR(128)  NOT NULL,
        ModuleName      VARCHAR(100)  NULL,
        KeyColumn       VARCHAR(128)  NOT NULL,
        ParentTable     VARCHAR(128)  NULL,
        ParentKeyColumn VARCHAR(128)  NULL,
        CaptureSnapshot CHAR(1)       NOT NULL CONSTRAINT DF_AuditConfig_Snapshot  DEFAULT('N'),
        IsActive        CHAR(1)       NOT NULL CONSTRAINT DF_AuditConfig_IsActive  DEFAULT('Y'),
        CreatedOn       DATETIME2(0)  NOT NULL CONSTRAINT DF_AuditConfig_CreatedOn DEFAULT(SYSDATETIME()),
        CONSTRAINT PK_AuditConfig PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_AuditConfig UNIQUE (TableName)
    );
    PRINT 'Created dbo.AuditConfig';
END
GO


/*==============================================================================
  2. dbo.AuditIgnoreColumn — columns that never reach the trail

  TableName '*' applies to every audited table.
==============================================================================*/
IF OBJECT_ID('dbo.AuditIgnoreColumn', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditIgnoreColumn
    (
        MTransNo   INT IDENTITY(1,1) NOT NULL,
        TableName  VARCHAR(128) NOT NULL,
        ColumnName VARCHAR(128) NOT NULL,
        Note       VARCHAR(200) NULL,
        CONSTRAINT PK_AuditIgnoreColumn PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_AuditIgnoreColumn UNIQUE (TableName, ColumnName)
    );
    PRINT 'Created dbo.AuditIgnoreColumn';
END
GO

MERGE dbo.AuditIgnoreColumn AS tgt
USING (VALUES
    -- Housekeeping. The header already carries who / when / from where, so
    -- repeating it per column would double the size of the trail for nothing.
    ('*',          'CreatedBy',  'On the AuditLog header as ChangedBy'),
    ('*',          'CreatedOn',  'On the AuditLog header as ChangedOn'),
    ('*',          'ModifiedBy', 'On the AuditLog header as ChangedBy'),
    ('*',          'ModifiedOn', 'On the AuditLog header as ChangedOn'),
    ('*',          'SystemIP',   'On the AuditLog header'),
    ('*',          'LockCounter','Concurrency stamp, not user data'),
    -- Secrets and session noise. These must never be written to a log table.
    ('UserMaster', 'PW',         'Credential'),
    ('UserMaster', 'TokenNo',    'Live session credential'),
    ('UserMaster', 'BadLogins',  'Login bookkeeping, already in AccessLog'),
    ('UserMaster', 'LastLogin',  'Login bookkeeping, already in AccessLog'),
    ('UserMaster', 'logoutDate', 'Login bookkeeping, already in AccessLog')
) AS src (TableName, ColumnName, Note)
    ON  tgt.TableName  = src.TableName
    AND tgt.ColumnName = src.ColumnName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (TableName, ColumnName, Note) VALUES (src.TableName, src.ColumnName, src.Note);
GO


/*==============================================================================
  3. dbo.AuditLog — one row per record changed
==============================================================================*/
IF OBJECT_ID('dbo.AuditLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        AuditNo        BIGINT IDENTITY(1,1) NOT NULL,

        TableName      VARCHAR(128)  NOT NULL,
        ModuleName     VARCHAR(100)  NULL,
        RecordID       VARCHAR(100)  NOT NULL,
        ParentTable    VARCHAR(128)  NULL,
        ParentRecordID VARCHAR(100)  NULL,

        ActionType     VARCHAR(10)   NOT NULL,
        ChangeCount    INT           NOT NULL CONSTRAINT DF_AuditLog_ChangeCount DEFAULT(0),
        Reason         VARCHAR(250)  NULL,
        RecordSnapshot NVARCHAR(MAX) NULL,

        ChangedBy      INT           NULL,
        ChangedOn      DATETIME2(0)  NOT NULL CONSTRAINT DF_AuditLog_ChangedOn DEFAULT(SYSDATETIME()),
        SystemIP       VARCHAR(45)   NULL,
        HostName       VARCHAR(128)  NULL,
        AppName        VARCHAR(128)  NULL,
        DBUser         VARCHAR(128)  NULL,

        SubscID        INT           NULL,

        CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (AuditNo),
        CONSTRAINT CK_AuditLog_ActionType CHECK
            (ActionType IN ('Insert', 'Update', 'Delete', 'SoftDelete', 'Restore'))
    );
    PRINT 'Created dbo.AuditLog';
END
GO

-- "What happened to this record?" — the history screen on any master.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_Record' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_Record
        ON dbo.AuditLog (SubscID, TableName, RecordID, AuditNo DESC)
        INCLUDE (ActionType, ChangedBy, ChangedOn, ChangeCount);
GO

-- "What changed this week?" — the default landing query of the log viewer.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_ChangedOn' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_ChangedOn
        ON dbo.AuditLog (SubscID, ChangedOn DESC)
        INCLUDE (TableName, ModuleName, RecordID, ActionType, ChangedBy, ChangeCount);
GO

-- "What has this user been doing?"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_ChangedBy' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_ChangedBy
        ON dbo.AuditLog (SubscID, ChangedBy, ChangedOn DESC)
        INCLUDE (TableName, ModuleName, RecordID, ActionType);
GO

-- "Everything that happened to this purchase requisition, lines included."
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLog_Parent' AND object_id = OBJECT_ID('dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_Parent
        ON dbo.AuditLog (SubscID, ParentTable, ParentRecordID, AuditNo DESC)
        WHERE ParentTable IS NOT NULL;
GO


/*==============================================================================
  4. dbo.AuditLogDtl — one row per column changed
==============================================================================*/
IF OBJECT_ID('dbo.AuditLogDtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogDtl
    (
        TransSlNo  BIGINT IDENTITY(1,1) NOT NULL,
        AuditNo    BIGINT        NOT NULL,
        ColumnName VARCHAR(128)  NOT NULL,
        OldValue   NVARCHAR(MAX) NULL,
        NewValue   NVARCHAR(MAX) NULL,
        CONSTRAINT PK_AuditLogDtl PRIMARY KEY CLUSTERED (TransSlNo),
        CONSTRAINT FK_AuditLogDtl_AuditLog FOREIGN KEY (AuditNo)
            REFERENCES dbo.AuditLog (AuditNo) ON DELETE CASCADE
    );
    PRINT 'Created dbo.AuditLogDtl';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogDtl_AuditNo' AND object_id = OBJECT_ID('dbo.AuditLogDtl'))
    CREATE NONCLUSTERED INDEX IX_AuditLogDtl_AuditNo
        ON dbo.AuditLogDtl (AuditNo)
        INCLUDE (ColumnName);
GO

-- "Who ever touched the credit limit?"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogDtl_ColumnName' AND object_id = OBJECT_ID('dbo.AuditLogDtl'))
    CREATE NONCLUSTERED INDEX IX_AuditLogDtl_ColumnName
        ON dbo.AuditLogDtl (ColumnName, AuditNo);
GO


/*==============================================================================
  5. dbo.SP_AuditLog_Capture — the engine

  Called by every TR_Audit_* trigger, which has already copied the row images
  into #AuditIns and #AuditDel. Those two temp tables are the procedure's real
  input: a temp table created by the caller is visible to everything it calls,
  including the dynamic SQL below, and copying them is the only way to get
  inserted / deleted out of a trigger and into shared code.

  Nothing here names a business column. Each row image is turned into a JSON
  object with FOR JSON, then OPENJSON unpivots it into column/value pairs, and
  the two sides are joined on the column name. That is what lets one procedure
  serve all 50-odd tables and keep serving them when a table gains a column.

  The header facts — who, which subscription, from which IP, whether this was a
  soft delete — are read back out of those same pairs, so a table that happens
  not to have DeleteStatus or ModifiedBy simply yields NULL there instead of
  breaking.

  ON ATTRIBUTION
  ChangedBy comes off the row. If you later want it to be the verified caller
  rather than whatever the row says, have the API push the user onto the
  connection with sp_set_session_context N'UserNo' and read
  SESSION_CONTEXT(N'UserNo') here in preference to the row columns; the rest of
  this procedure is unaffected.
==============================================================================*/
CREATE OR ALTER PROCEDURE dbo.SP_AuditLog_Capture
    @TableName VARCHAR(128),
    @KeyColumn VARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ModuleName  VARCHAR(100),
            @ParentTable VARCHAR(128),
            @ParentKey   VARCHAR(128),
            @Snapshot    CHAR(1),
            @IsActive    CHAR(1);

    SELECT @ModuleName  = ModuleName,
           @ParentTable = ParentTable,
           @ParentKey   = ParentKeyColumn,
           @Snapshot    = CaptureSnapshot,
           @IsActive    = IsActive
      FROM dbo.AuditConfig
     WHERE TableName = @TableName;

    -- Not registered, or switched off in AuditConfig. The trigger stays in
    -- place; this is the cheap way to silence one table.
    IF ISNULL(@IsActive, 'N') <> 'Y'
        RETURN;

    /*--------------------------------------------------------------------------
      1. Row images as JSON, one object per row, every column present.
         INCLUDE_NULL_VALUES matters: without it a column going from a value to
         NULL would vanish from the new image and the change would be missed.
    --------------------------------------------------------------------------*/
    CREATE TABLE #AuditRowJson
    (
        Side     CHAR(1)       NOT NULL,
        RecordID VARCHAR(100)  NOT NULL,
        RowJson  NVARCHAR(MAX) NULL
    );

    DECLARE @k   NVARCHAR(300) = QUOTENAME(@KeyColumn);
    DECLARE @sql NVARCHAR(MAX) =
    N'INSERT INTO #AuditRowJson (Side, RecordID, RowJson)
      SELECT ''I'', CONVERT(VARCHAR(100), t.' + @k + N'),
             (SELECT s.* FROM #AuditIns s WHERE s.' + @k + N' = t.' + @k + N'
               FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
        FROM #AuditIns t;

      INSERT INTO #AuditRowJson (Side, RecordID, RowJson)
      SELECT ''D'', CONVERT(VARCHAR(100), t.' + @k + N'),
             (SELECT s.* FROM #AuditDel s WHERE s.' + @k + N' = t.' + @k + N'
               FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)
        FROM #AuditDel t;';

    EXEC sys.sp_executesql @sql;

    IF NOT EXISTS (SELECT 1 FROM #AuditRowJson)
        RETURN;

    /*--------------------------------------------------------------------------
      2. One row per column value, per side. 'I' is the new image, 'D' the old.

         OPENJSON hands back its key and value columns in Latin1_General_BIN2,
         which will not compare against AuditIgnoreColumn's columns further
         down. Forced to the database collation here, once, so everything
         downstream is comparable; the diff in step 4 asks for a binary
         comparison explicitly where it wants one.
    --------------------------------------------------------------------------*/
    SELECT r.Side,
           r.RecordID,
           CONVERT(VARCHAR(128), j.[key]) COLLATE DATABASE_DEFAULT AS ColumnName,
           j.[value]                      COLLATE DATABASE_DEFAULT AS Val
      INTO #AuditCell
      FROM #AuditRowJson r
     CROSS APPLY OPENJSON(r.RowJson) j;

    /*--------------------------------------------------------------------------
      3. The two row images side by side, then the header facts read off them
         with JSON_VALUE.

         RecordID + Side is unique, so this is a join rather than a pivot — and
         deliberately so: aggregating the columns out would raise "Null value is
         eliminated by an aggregate" on every single save the application makes.

         Nothing here requires a table to have any of these columns. A table
         without DeleteStatus or ModifiedBy simply yields NULL and is treated as
         a plain update by whoever the row says last touched it.
    --------------------------------------------------------------------------*/
    DECLARE @ParentPath NVARCHAR(300) =
        CASE WHEN @ParentKey IS NULL THEN N'$."~none~"' ELSE N'$."' + @ParentKey + N'"' END;

    SELECT k.RecordID,
           HasNew = CASE WHEN i.RecordID IS NULL THEN 0 ELSE 1 END,
           HasOld = CASE WHEN d.RecordID IS NULL THEN 0 ELSE 1 END,

           NewDeleteStatus = JSON_VALUE(i.RowJson, '$.DeleteStatus'),
           OldDeleteStatus = JSON_VALUE(d.RowJson, '$.DeleteStatus'),
           NewDeleteReason = JSON_VALUE(i.RowJson, '$.DeleteReason'),
           OldDeleteReason = JSON_VALUE(d.RowJson, '$.DeleteReason'),

           NewModifiedBy   = JSON_VALUE(i.RowJson, '$.ModifiedBy'),
           OldModifiedBy   = JSON_VALUE(d.RowJson, '$.ModifiedBy'),
           NewCreatedBy    = JSON_VALUE(i.RowJson, '$.CreatedBy'),
           OldCreatedBy    = JSON_VALUE(d.RowJson, '$.CreatedBy'),

           NewSystemIP     = JSON_VALUE(i.RowJson, '$.SystemIP'),
           OldSystemIP     = JSON_VALUE(d.RowJson, '$.SystemIP'),
           NewSubscID      = JSON_VALUE(i.RowJson, '$.SubscID'),
           OldSubscID      = JSON_VALUE(d.RowJson, '$.SubscID'),

           NewParentID     = JSON_VALUE(i.RowJson, @ParentPath),
           OldParentID     = JSON_VALUE(d.RowJson, @ParentPath),

           NewJson         = i.RowJson,
           OldJson         = d.RowJson
      INTO #AuditRec
      FROM (SELECT DISTINCT RecordID FROM #AuditRowJson) k
      LEFT JOIN #AuditRowJson i ON i.RecordID = k.RecordID AND i.Side = 'I'
      LEFT JOIN #AuditRowJson d ON d.RecordID = k.RecordID AND d.Side = 'D';

    /*--------------------------------------------------------------------------
      4. The differences. A FULL JOIN so a column present on only one side —
         an insert, a hard delete — still produces a row.

         The binary collation is deliberate: under the database's default
         collation 'abc' and 'ABC' compare equal, and a rename that only
         changes case would go unrecorded.
    --------------------------------------------------------------------------*/
    ;WITH NewCell AS (SELECT RecordID, ColumnName, Val FROM #AuditCell WHERE Side = 'I'),
          OldCell AS (SELECT RecordID, ColumnName, Val FROM #AuditCell WHERE Side = 'D'),
          Pair AS
          (
              SELECT ISNULL(n.RecordID,   o.RecordID)   AS RecordID,
                     ISNULL(n.ColumnName, o.ColumnName) AS ColumnName,
                     o.Val AS OldValue,
                     n.Val AS NewValue
                FROM NewCell n
                FULL OUTER JOIN OldCell o
                  ON  o.RecordID   = n.RecordID
                  AND o.ColumnName = n.ColumnName
          )
    SELECT p.RecordID, p.ColumnName, p.OldValue, p.NewValue
      INTO #AuditDiff
      FROM Pair p
     WHERE NOT EXISTS (SELECT 1
                         FROM dbo.AuditIgnoreColumn g
                        WHERE g.ColumnName = p.ColumnName
                          AND g.TableName IN ('*', @TableName))
       AND (
                (p.OldValue IS     NULL AND p.NewValue IS NOT NULL)
             OR (p.OldValue IS NOT NULL AND p.NewValue IS     NULL)
             OR (p.OldValue COLLATE Latin1_General_BIN2 <> p.NewValue COLLATE Latin1_General_BIN2)
           );

    /*--------------------------------------------------------------------------
      5. Headers. Records whose only changes were ignored columns are dropped
         here — that is what keeps a re-save with no edits out of the trail.
    --------------------------------------------------------------------------*/
    DECLARE @Header TABLE (AuditNo BIGINT NOT NULL, RecordID VARCHAR(100) NOT NULL);

    INSERT INTO dbo.AuditLog
    (
        TableName, ModuleName, RecordID, ParentTable, ParentRecordID,
        ActionType, ChangeCount, Reason, RecordSnapshot,
        ChangedBy, ChangedOn, SystemIP, HostName, AppName, DBUser, SubscID
    )
    OUTPUT inserted.AuditNo, inserted.RecordID INTO @Header (AuditNo, RecordID)
    SELECT @TableName,
           @ModuleName,
           r.RecordID,
           CASE WHEN @ParentKey IS NULL THEN NULL ELSE @ParentTable END,
           CASE WHEN @ParentKey IS NULL THEN NULL ELSE COALESCE(r.NewParentID, r.OldParentID) END,

           a.ActionType,
           n.ChangeCount,
           CASE WHEN a.ActionType IN ('SoftDelete', 'Delete')
                THEN LEFT(COALESCE(r.NewDeleteReason, r.OldDeleteReason), 250) END,
           -- The surviving image: the new row, or the old one on a hard delete.
           CASE WHEN @Snapshot = 'Y' THEN COALESCE(r.NewJson, r.OldJson) END,

           -- An insert is attributed to CreatedBy, a hard delete to whoever
           -- last touched the row, anything else to ModifiedBy.
           TRY_CONVERT(INT, CASE WHEN r.HasOld = 0 THEN COALESCE(r.NewCreatedBy,  r.NewModifiedBy)
                                 WHEN r.HasNew = 0 THEN COALESCE(r.OldModifiedBy, r.OldCreatedBy)
                                 ELSE                   COALESCE(r.NewModifiedBy, r.NewCreatedBy) END),
           SYSDATETIME(),
           LEFT(COALESCE(r.NewSystemIP, r.OldSystemIP), 45),
           LEFT(HOST_NAME(),   128),
           LEFT(APP_NAME(),    128),
           LEFT(SUSER_SNAME(), 128),
           TRY_CONVERT(INT, COALESCE(r.NewSubscID, r.OldSubscID))
      FROM #AuditRec r
     CROSS APPLY (SELECT ActionType =
                      CASE WHEN r.HasNew = 1 AND r.HasOld = 0 THEN 'Insert'
                           WHEN r.HasNew = 0 AND r.HasOld = 1 THEN 'Delete'
                           WHEN r.NewDeleteStatus = 'Y' AND ISNULL(r.OldDeleteStatus, 'N') = 'N' THEN 'SoftDelete'
                           WHEN r.NewDeleteStatus = 'N' AND ISNULL(r.OldDeleteStatus, 'Y') = 'Y' THEN 'Restore'
                           ELSE 'Update' END) a
     CROSS APPLY (SELECT ChangeCount = (SELECT COUNT(*) FROM #AuditDiff d WHERE d.RecordID = r.RecordID)) n
     WHERE n.ChangeCount > 0;

    IF NOT EXISTS (SELECT 1 FROM @Header)
        RETURN;

    /*--------------------------------------------------------------------------
      6. Details.
    --------------------------------------------------------------------------*/
    INSERT INTO dbo.AuditLogDtl (AuditNo, ColumnName, OldValue, NewValue)
    SELECT h.AuditNo, d.ColumnName, d.OldValue, d.NewValue
      FROM @Header h
      JOIN #AuditDiff d ON d.RecordID = h.RecordID;
END
GO


/*==============================================================================
  6. dbo.SP_AuditLog_Sync — register tables and build their triggers

      EXEC dbo.SP_AuditLog_Sync;                          -- add what is missing
      EXEC dbo.SP_AuditLog_Sync @Rebuild = 'Y';           -- recreate every trigger
      EXEC dbo.SP_AuditLog_Sync @Table = 'BankMaster';    -- just the one

  Re-run it after creating a table. It never overwrites an AuditConfig row you
  have edited by hand, so a ModuleName or a parent link set below survives.

  A table qualifies when it is a base table with a single-column primary key
  and is not one of the log tables themselves. Anything skipped is listed by
  SP_AuditLog_Status.
==============================================================================*/
CREATE OR ALTER PROCEDURE dbo.SP_AuditLog_Sync
    @Table   VARCHAR(128)  = NULL,
    @Rebuild CHAR(1)       = 'N',
    @Message NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @registered INT = 0, @built INT = 0;

    /*--------------------------------------------------------------------------
      Register anything eligible that is not in AuditConfig yet.

      A table keyed on TransSlNo that also carries MTransNo is a detail table in
      this schema — every one of them, without exception — so its parent key is
      MTransNo. The parent table itself is guessed only for the Dtl/Mst pairs,
      which is a rule; the rest are named in the seed below, which is not.
    --------------------------------------------------------------------------*/
    ;WITH Eligible AS
    (
        SELECT t.name AS TableName,
               k.KeyColumn
          FROM sys.tables t
         CROSS APPLY
              (
                  SELECT TOP 2 c.name AS KeyColumn, COUNT(*) OVER () AS KeyCols
                    FROM sys.indexes i
                    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    JOIN sys.columns c        ON c.object_id  = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.object_id = t.object_id AND i.is_primary_key = 1
                   ORDER BY ic.key_ordinal
              ) k
         WHERE t.is_ms_shipped = 0
           AND t.temporal_type = 0
           AND k.KeyCols = 1
           AND t.name NOT IN ('AuditLog', 'AuditLogDtl', 'AuditConfig', 'AuditIgnoreColumn',
                              'AccessLog', 'ErrorLog')
           AND t.name NOT LIKE '%[_]Backup[_]%'
           AND (@Table IS NULL OR t.name = @Table)
    )
    INSERT INTO dbo.AuditConfig (TableName, ModuleName, KeyColumn, ParentTable, ParentKeyColumn)
    SELECT e.TableName,
           e.TableName,
           e.KeyColumn,
           CASE WHEN e.TableName LIKE '%Dtl'
                 AND OBJECT_ID('dbo.' + LEFT(e.TableName, LEN(e.TableName) - 3) + 'Mst', 'U') IS NOT NULL
                THEN LEFT(e.TableName, LEN(e.TableName) - 3) + 'Mst' END,
           CASE WHEN e.KeyColumn = 'TransSlNo'
                 AND EXISTS (SELECT 1 FROM sys.columns c
                              WHERE c.object_id = OBJECT_ID('dbo.' + e.TableName)
                                AND c.name = 'MTransNo')
                THEN 'MTransNo' END
      FROM Eligible e
     WHERE NOT EXISTS (SELECT 1 FROM dbo.AuditConfig a WHERE a.TableName = e.TableName);

    SET @registered = @@ROWCOUNT;

    /*--------------------------------------------------------------------------
      Build the triggers.

      The body is the same for every table bar the two names, so it lives here
      once. A fix to it reaches every table on the next @Rebuild = 'Y'.
    --------------------------------------------------------------------------*/
    DECLARE @Template NVARCHAR(MAX) = N'
CREATE OR ALTER TRIGGER dbo.[TR_Audit_{T}]
ON dbo.[{T}]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    -- The write has already happened and is not ours to undo. XACT_ABORT OFF
    -- keeps a fault in here from dooming the caller''s transaction; the CATCH
    -- below then swallows it. Scoped to this trigger, restored on exit.
    SET XACT_ABORT OFF;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
        RETURN;

    -- Escape hatch for bulk loads and migrations:
    --     EXEC sys.sp_set_session_context N''AuditSuppress'', N''1'';
    IF CONVERT(VARCHAR(10), SESSION_CONTEXT(N''AuditSuppress'')) = ''1''
        RETURN;

    BEGIN TRY
        SELECT * INTO #AuditIns FROM inserted;
        SELECT * INTO #AuditDel FROM deleted;

        EXEC dbo.SP_AuditLog_Capture @TableName = ''{T}'', @KeyColumn = ''{K}'';
    END TRY
    BEGIN CATCH
        -- Auditing must never cost the user their save. Record the fault where
        -- it will be seen and let the write stand.
        IF XACT_STATE() <> -1
            INSERT INTO dbo.ErrorLog
                (ErrorMsg, StackTrace, ErrorDate, ErrorTime, LoginType, LoginId, SubscID)
            VALUES
                (LEFT(''TR_Audit_{T}: '' + ERROR_MESSAGE(), 2000),
                 LEFT(''Line '' + CONVERT(VARCHAR(10), ERROR_LINE()) +
                      '' in '' + ISNULL(ERROR_PROCEDURE(), ''TR_Audit_{T}''), 2000),
                 CONVERT(DATE, GETDATE()),
                 CONVERT(VARCHAR(10), GETDATE(), 108),
                 ''AuditLog'',
                 LEFT(SUSER_SNAME(), 30),
                 NULL);
    END CATCH
END';

    DECLARE @tbl VARCHAR(128), @key VARCHAR(128), @sql NVARCHAR(MAX);

    DECLARE cfg CURSOR LOCAL FAST_FORWARD FOR
        SELECT c.TableName, c.KeyColumn
          FROM dbo.AuditConfig c
         WHERE c.IsActive = 'Y'
           AND (@Table IS NULL OR c.TableName = @Table)
           AND OBJECT_ID('dbo.' + c.TableName, 'U') IS NOT NULL;

    OPEN cfg;
    FETCH NEXT FROM cfg INTO @tbl, @key;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @Rebuild = 'Y' OR OBJECT_ID('dbo.TR_Audit_' + @tbl, 'TR') IS NULL
        BEGIN
            SET @sql = REPLACE(REPLACE(@Template, '{T}', @tbl), '{K}', @key);
            EXEC sys.sp_executesql @sql;
            SET @built += 1;
        END

        FETCH NEXT FROM cfg INTO @tbl, @key;
    END

    CLOSE cfg;
    DEALLOCATE cfg;

    SET @Message = CONVERT(VARCHAR(10), @registered) + ' table(s) registered, ' +
                   CONVERT(VARCHAR(10), @built)      + ' trigger(s) built.';
END
GO


/*==============================================================================
  7. dbo.SP_AuditLog_DropTriggers — take the trail offline

  For a bulk load or a restore where the trail would be noise. AuditConfig is
  left alone, so SP_AuditLog_Sync puts everything back.
==============================================================================*/
CREATE OR ALTER PROCEDURE dbo.SP_AuditLog_DropTriggers
    @Table   VARCHAR(128)  = NULL,
    @Message NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @name SYSNAME, @sql NVARCHAR(MAX), @n INT = 0;

    DECLARE trg CURSOR LOCAL FAST_FORWARD FOR
        SELECT tr.name
          FROM sys.triggers tr
         WHERE tr.parent_id > 0
           AND tr.name LIKE 'TR[_]Audit[_]%'
           AND (@Table IS NULL OR OBJECT_NAME(tr.parent_id) = @Table);

    OPEN trg;
    FETCH NEXT FROM trg INTO @name;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'DROP TRIGGER dbo.' + QUOTENAME(@name) + N';';
        EXEC sys.sp_executesql @sql;
        SET @n += 1;
        FETCH NEXT FROM trg INTO @name;
    END

    CLOSE trg;
    DEALLOCATE trg;

    SET @Message = CONVERT(VARCHAR(10), @n) + ' audit trigger(s) dropped.';
END
GO


/*==============================================================================
  8. dbo.SP_AuditLog_Status — coverage report

  Two result sets: what is audited, and every base table that is not, with the
  reason. Run it after adding a table to see whether it was picked up.
==============================================================================*/
CREATE OR ALTER PROCEDURE dbo.SP_AuditLog_Status
AS
BEGIN
    SET NOCOUNT ON;

    SELECT c.TableName,
           c.ModuleName,
           c.KeyColumn,
           c.ParentTable,
           c.ParentKeyColumn,
           c.CaptureSnapshot,
           c.IsActive,
           TriggerBuilt = CASE WHEN OBJECT_ID('dbo.TR_Audit_' + c.TableName, 'TR') IS NULL
                               THEN 'N' ELSE 'Y' END,
           TriggerEnabled = CASE WHEN tr.is_disabled = 1 THEN 'N'
                                 WHEN tr.object_id IS NULL THEN '-' ELSE 'Y' END,
           LoggedRows   = (SELECT COUNT(*) FROM dbo.AuditLog a WHERE a.TableName = c.TableName),
           LastChangeOn = (SELECT MAX(a.ChangedOn) FROM dbo.AuditLog a WHERE a.TableName = c.TableName)
      FROM dbo.AuditConfig c
      LEFT JOIN sys.triggers tr ON tr.name = 'TR_Audit_' + c.TableName AND tr.parent_id > 0
     ORDER BY c.TableName;

    SELECT t.name AS TableName,
           Reason = CASE
               WHEN t.name IN ('AuditLog', 'AuditLogDtl', 'AuditConfig', 'AuditIgnoreColumn')
                    THEN 'Audit infrastructure — auditing it would recurse'
               WHEN t.name IN ('AccessLog', 'ErrorLog')
                    THEN 'Already an append-only log'
               WHEN t.name LIKE '%[_]Backup[_]%'
                    THEN 'Backup copy'
               WHEN NOT EXISTS (SELECT 1 FROM sys.indexes i
                                 WHERE i.object_id = t.object_id AND i.is_primary_key = 1)
                    THEN 'No primary key — nothing to identify the row by'
               WHEN (SELECT COUNT(*) FROM sys.index_columns ic
                      JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                     WHERE ic.object_id = t.object_id AND i.is_primary_key = 1) > 1
                    THEN 'Composite primary key — not supported'
               ELSE 'Eligible but unregistered — run SP_AuditLog_Sync' END
      FROM sys.tables t
     WHERE t.is_ms_shipped = 0
       AND NOT EXISTS (SELECT 1 FROM dbo.AuditConfig c WHERE c.TableName = t.name)
     ORDER BY t.name;
END
GO


/*==============================================================================
  9. dbo.SP_AuditLog_Purge — retention

  Deletes headers older than @KeepDays in batches so the log stays online while
  it runs. AuditLogDtl follows through the cascade.

      EXEC dbo.SP_AuditLog_Purge @KeepDays = 730, @Message = @m OUTPUT;
==============================================================================*/
CREATE OR ALTER PROCEDURE dbo.SP_AuditLog_Purge
    @KeepDays  INT           = 730,
    @SubscID   INT           = NULL,
    @BatchSize INT           = 5000,
    @Message   NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @KeepDays < 30
    BEGIN
        SET @Message = 'Refusing to purge: keep at least 30 days.';
        RETURN;
    END

    DECLARE @cutoff DATETIME2(0) = DATEADD(DAY, -@KeepDays, SYSDATETIME());
    DECLARE @total  INT = 0, @batch INT = 1;

    WHILE @batch > 0
    BEGIN
        DELETE TOP (@BatchSize)
          FROM dbo.AuditLog
         WHERE ChangedOn < @cutoff
           AND (@SubscID IS NULL OR SubscID = @SubscID);

        SET @batch = @@ROWCOUNT;
        SET @total += @batch;
    END

    SET @Message = CONVERT(VARCHAR(10), @total) + ' audit header(s) purged before ' +
                   CONVERT(VARCHAR(20), @cutoff, 120) + '.';
END
GO


/*==============================================================================
  10. dbo.VW_AuditLog — the trail with the user resolved

  What the log viewer reads. Date and time are split out to match the shape the
  front-end grids already use for AccessLog and ErrorLog.
==============================================================================*/
CREATE OR ALTER VIEW dbo.VW_AuditLog
AS
SELECT a.AuditNo,
       a.TableName,
       a.ModuleName,
       a.RecordID,
       a.ParentTable,
       a.ParentRecordID,
       a.ActionType,
       a.ChangeCount,
       a.Reason,
       a.ChangedBy,
       u.UserName                            AS ChangedByName,
       u.UserId                              AS ChangedByLogin,
       a.ChangedOn,
       CONVERT(DATE, a.ChangedOn)            AS ChangedDate,
       CONVERT(VARCHAR(8), a.ChangedOn, 108) AS ChangedTime,
       a.SystemIP,
       a.HostName,
       a.AppName,
       a.DBUser,
       a.SubscID
  FROM dbo.AuditLog a
  LEFT JOIN dbo.UserMaster u ON u.MTransNo = a.ChangedBy;
GO


/*==============================================================================
  11. Register every table and build the triggers
==============================================================================*/
DECLARE @msg NVARCHAR(200);
EXEC dbo.SP_AuditLog_Sync @Rebuild = 'Y', @Message = @msg OUTPUT;
PRINT @msg;
GO


/*==============================================================================
  12. Friendly module names and the master/detail links the rules cannot infer

  Only the rows the sync could not work out for itself. Applied with UPDATE, so
  re-running the script re-asserts them but leaves anything else you have
  edited in AuditConfig alone.
==============================================================================*/
UPDATE c
   SET c.ModuleName      = v.ModuleName,
       c.ParentTable     = COALESCE(v.ParentTable, c.ParentTable),
       c.ParentKeyColumn = CASE WHEN v.ParentTable IS NULL THEN c.ParentKeyColumn ELSE 'MTransNo' END
  FROM dbo.AuditConfig c
  JOIN (VALUES
    -- Company and setup
    ('Company',             'Company',                NULL),
    ('BranchMaster',        'Branch Master',          NULL),
    ('GeneralConfig',       'General Config',         NULL),
    ('EinvSetup',           'E-Invoice Setup',        NULL),
    ('EinvDtl',             'E-Invoice Detail',       'EinvSetup'),
    ('Menulist',            'Menu List',              NULL),

    -- Administration
    ('UserMaster',          'User Master',            NULL),
    ('AccessTemplate',      'Access Template',        NULL),

    -- Accounting
    ('GroupHeads',          'Group Heads',            NULL),
    ('AccountsMaster',      'Account Master',         NULL),
    ('AccountConfig',       'Account Config',         NULL),
    ('TaxMaster',           'Tax Master',             NULL),
    ('BankMaster',          'Bank Master',            NULL),
    ('CurrencyMaster',      'Currency Master',        NULL),
    ('StateMaster',         'State Master',           NULL),
    ('HSNMaster',           'HSN Master',             NULL),

    -- Customer
    ('CustomerMaster',      'Customer Master',        NULL),
    ('CustomerContacts',    'Customer Contacts',      'CustomerMaster'),
    ('CustomerTerm',        'Customer Terms',         'CustomerMaster'),
    ('ShippingAddress',     'Shipping Address',       'CustomerMaster'),

    -- Supplier
    ('SupplierMaster',      'Supplier Master',        NULL),
    ('SupplierContacts',    'Supplier Contacts',      'SupplierMaster'),
    ('SupplierTerm',        'Supplier Terms',         'SupplierMaster'),

    -- Item / part
    ('PartMaster',          'Part Master',            NULL),
    ('PartCodeMaster',      'Part Code Master',       NULL),
    ('PartParameterList',   'Part Parameter List',    NULL),
    ('DrawingNos',          'Drawing Numbers',        NULL),
    ('MaterialGrade',       'Material Grade',         NULL),
    ('MaterialStd',         'Material Standard',      NULL),
    ('MOCMaster',           'MOC Master',             NULL),
    ('MOCTemplate',         'MOC Template',           NULL),
    ('ChemicalMaster',      'Chemical Master',        NULL),
    ('MechanicalMaster',    'Mechanical Master',      NULL),
    ('PriceList',           'Price List',             NULL),
    ('StandardTerms',       'Standard Terms',         NULL),
    ('OtherMaster',         'Other Master',           NULL),
    ('OtherMasterList',     'Other Master List',      NULL),

    -- Sales
    ('SalesTeam',           'Sales Team',             NULL),
    ('FeedbackMasterSales', 'Sales Feedback Master',  NULL),

    -- Purchase
    ('PurReqMst',           'Purchase Requisition',   NULL),
    ('PurReqDtl',           'Purchase Requisition',   'PurReqMst'),
    ('RFQMst',              'Request For Quote',      NULL),
    ('RFQDtl',              'Request For Quote',      'RFQMst'),
    ('RFQSupplier',         'Request For Quote',      'RFQMst'),
    ('RFQTerms',            'Request For Quote',      'RFQMst'),
    ('RFQPriceMst',         'RFQ Price Update',       NULL),
    ('RFQPriceDtl',         'RFQ Price Update',       'RFQPriceMst'),
    ('RFQPriceTerms',       'RFQ Price Update',       'RFQPriceMst')
) AS v (TableName, ModuleName, ParentTable)
    ON v.TableName = c.TableName;
GO


/*==============================================================================
  13. Report
==============================================================================*/
PRINT '';
PRINT '12_AuditLog.sql complete.';
GO

SELECT Registered   = COUNT(*),
       Active       = SUM(CASE WHEN IsActive = 'Y' THEN 1 ELSE 0 END),
       WithTrigger  = SUM(CASE WHEN OBJECT_ID('dbo.TR_Audit_' + TableName, 'TR') IS NOT NULL THEN 1 ELSE 0 END),
       DetailTables = SUM(CASE WHEN ParentTable IS NOT NULL THEN 1 ELSE 0 END)
  FROM dbo.AuditConfig;
GO

EXEC dbo.SP_AuditLog_Status;
GO
