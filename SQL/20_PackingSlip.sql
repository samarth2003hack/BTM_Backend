/*
================================================================================
 20_PackingSlip.sql
 Packing Slip (Sales) — tables + write procedures.

   PackingSlipMst   header: one slip packs lines of ONE Order Acceptance
                    (TargetTransNo / TargetDocYear / TargetDocNo / TargetAmdNo
                    and OAYear / OANo / OADate all describe that OA).
   PackingSlipDtl   one row per OA line per case. PackingSlipDtl.OAID is
                    OADtl.TransSlNo; the same OAID may appear on several rows
                    of one slip (one OA line split across cases), but only
                    once per CaseNo.

 No amendments on this document. SP_PackingSlipMst allocates DocNo itself
 (MAX(DocNo)+1 per DocSeries + DocYear + SubscID); SP_PackingSlipDtl
 allocates SlNo itself (MAX(SlNo)+1 per MTransNo) — post child rows one at a
 time, never in parallel.

 Idempotent: safe to run repeatedly.
================================================================================
*/
USE EMSSoft;
GO

--------------------------------------------------------------------------------
-- 1. PackingSlipMst
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PackingSlipMst', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingSlipMst
    (
        MTransNo INT IDENTITY(1,1) NOT NULL,
        DocSeries varchar(10) NOT NULL,
        DocYear INT NOT NULL,
        DocNo INT NOT NULL,
        DocDate date NOT NULL,
        TargetTransNo INT NULL,
        TargetDocYear INT NULL,
        TargetDocNo INT NULL,
        TargetAmdNo INT NULL,
        VendorNo INT NOT NULL,
        VendorCode varchar(20) NOT NULL,
        VendorName varchar(100) NOT NULL,
        Address varchar(500) NOT NULL,
        GSTIN varchar(20) NOT NULL,
        StateName varchar(50) NULL,
        PhoneNo varchar(30) NULL,
        EmailID varchar(80) NULL,
        OAYear INT NULL,
        OANo INT NULL,
        OADate date NULL,
        RefNo varchar(50) NULL,
        RefDate date NULL,
        DocNotes varchar(250) NULL,
        DocStatus char(10) NOT NULL,
        TotalCases INT NULL,
        TotalWeight numeric(18, 2) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_PackingSlipMst_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipMst_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipMst_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_PackingSlipMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_PackingSlipMst UNIQUE (DocSeries, DocYear, DocNo, DeleteStatus, SubscID)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_DocNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_DocNo
        ON dbo.PackingSlipMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, DocNotes)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_DocDate' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_DocDate
        ON dbo.PackingSlipMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_VendorNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_VendorNo
        ON dbo.PackingSlipMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipMst_TargetTransNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipMst'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipMst_TargetTransNo
        ON dbo.PackingSlipMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

--------------------------------------------------------------------------------
-- 2. PackingSlipDtl  (child of PackingSlipMst : MTransNo -> PackingSlipMst.MTransNo)
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.PackingSlipDtl', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PackingSlipDtl
    (
        TransSlNo INT IDENTITY(1,1) NOT NULL,
        SlNo numeric(12, 2) NULL,
        MTransNo INT NOT NULL,
        OAID INT NOT NULL,
        CaseNo INT NOT NULL,
        PartNo INT NOT NULL,
        PartCode varchar(30) NULL,
        PartDescription varchar(250) NULL,
        MOC varchar(250) NULL,
        OARemarks varchar(250) NULL,
        ItemRemarks varchar(500) NULL,
        HSNCode varchar(10) NULL,
        Qty numeric(12, 4) NOT NULL,
        Unit varchar(20) NULL,
        CaseSize varchar(50) NULL,
        NetWeight numeric(12, 2) NULL,
        GrossWeight numeric(12, 2) NULL,
        ValveSlNo varchar(200) NULL,
        BoxType varchar(50) NULL,
        SystemIP varchar(20) NULL,
        CreatedBy INT NULL,
        CreatedOn DATETIME2 NOT NULL CONSTRAINT DF_PackingSlipDtl_CreatedOn DEFAULT(GETDATE()),
        ModifiedBy INT NULL,
        ModifiedOn DATETIME2 NULL,
        DeleteStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipDtl_DeleteStatus DEFAULT('N'),
        DeleteReason varchar(250) NULL,
        LockStatus CHAR(1) NOT NULL CONSTRAINT DF_PackingSlipDtl_LockStatus DEFAULT('N'),
        LockCounter INT NULL,
        SubscID INT NULL,
        CONSTRAINT PK_PackingSlipDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipDtl_MTransNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipDtl'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipDtl_MTransNo
        ON dbo.PackingSlipDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, PartDescription, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PackingSlipDtl_PartNo' AND object_id = OBJECT_ID(N'dbo.PackingSlipDtl'))
    CREATE NONCLUSTERED INDEX IX_PackingSlipDtl_PartNo
        ON dbo.PackingSlipDtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

--------------------------------------------------------------------------------
-- 3. SP_PackingSlipMst
--    Insert / Update / Delete / Lock / Unlock.
--    DocStatus: 'N' open, 'Y' closed (invoiced) — a closed slip cannot be
--    edited or deleted. LockStatus is the approval flag the page drives
--    ('S' submitted, 'N' draft, 'Y' approved).
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_PackingSlipMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PackingSlipMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PackingSlipMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @DocSeries      VARCHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @VendorCode     VARCHAR(20),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @StateName      VARCHAR(50),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @OAYear         INT,
    @OANo           INT,
    @OADate         DATE,
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(250),
    @TotalCases     INT,
    @TotalWeight    NUMERIC(18, 2),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Closed CHAR(1) = 'N';

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
            FROM dbo.PackingSlipMst WITH (UPDLOCK, HOLDLOCK)
            WHERE DocSeries = LTRIM(RTRIM(@DocSeries))
              AND DocYear   = @DocYear
              AND SubscID   = @SubscID;

            INSERT INTO dbo.PackingSlipMst
            (
                DocSeries, DocYear, DocNo, DocDate,
                TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                VendorNo, VendorCode, VendorName, Address, GSTIN,
                StateName, PhoneNo, EmailID,
                OAYear, OANo, OADate, RefNo, RefDate,
                DocNotes, DocStatus, TotalCases, TotalWeight,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                @VendorNo, LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)),
                LTRIM(RTRIM(@Address)), LTRIM(RTRIM(@GSTIN)),
                LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@PhoneNo)), LTRIM(RTRIM(@EmailID)),
                @OAYear, @OANo, @OADate, LTRIM(RTRIM(@RefNo)), @RefDate,
                LTRIM(RTRIM(@DocNotes)), 'N', @TotalCases, @TotalWeight,
                @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Packing Slip saved successfully.';
        END

        --=========================================
        -- UPDATE  (series / year / number never change)
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @Closed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@Closed = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Packing Slip is already invoiced and cannot be changed.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipMst
                SET DocDate       = @DocDate,
                    TargetTransNo = @TargetTransNo,
                    TargetDocYear = @TargetDocYear,
                    TargetDocNo   = @TargetDocNo,
                    TargetAmdNo   = @TargetAmdNo,
                    VendorNo      = @VendorNo,
                    VendorCode    = LTRIM(RTRIM(@VendorCode)),
                    VendorName    = LTRIM(RTRIM(@VendorName)),
                    Address       = LTRIM(RTRIM(@Address)),
                    GSTIN         = LTRIM(RTRIM(@GSTIN)),
                    StateName     = LTRIM(RTRIM(@StateName)),
                    PhoneNo       = LTRIM(RTRIM(@PhoneNo)),
                    EmailID       = LTRIM(RTRIM(@EmailID)),
                    OAYear        = @OAYear,
                    OANo          = @OANo,
                    OADate        = @OADate,
                    RefNo         = LTRIM(RTRIM(@RefNo)),
                    RefDate       = @RefDate,
                    DocNotes      = LTRIM(RTRIM(@DocNotes)),
                    TotalCases    = @TotalCases,
                    TotalWeight   = @TotalWeight,
                    SystemIP      = @SystemIP,
                    ModifiedBy    = @ModifiedBy,
                    ModifiedOn    = ISNULL(@ModifiedOn, GETDATE()),
                    LockStatus    = ISNULL(@LockStatus, LockStatus),
                    LockCounter   = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Packing Slip updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- DELETE  (soft; its lines go with it)
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            SELECT @Closed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@Closed = 'Y')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Packing Slip is already invoiced and cannot be deleted.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    UPDATE dbo.PackingSlipDtl
                    SET DeleteStatus = 'Y',
                        DeleteReason = @DeleteReason,
                        ModifiedBy   = @ModifiedBy,
                        ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                    WHERE MTransNo     = @MTransNo
                      AND SubscID      = @SubscID
                      AND DeleteStatus <> 'Y';

                    SET @ID = @MTransNo; SET @Message = 'Packing Slip deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK / UNLOCK
        --=========================================
        ELSE IF (@Status = 'Lock' OR @Status = 'Unlock')
        BEGIN
            UPDATE dbo.PackingSlipMst
            SET LockStatus  = CASE WHEN @Status = 'Lock' THEN 'Y' ELSE 'N' END,
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = CASE WHEN @Status = 'Lock'
                                    THEN 'Packing Slip locked successfully.'
                                    ELSE 'Packing Slip unlocked successfully.' END;
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 4. SP_PackingSlipDtl
--    Insert / Update / Delete / DeleteAll.
--    Insert + Update refuse (ID 0) when:
--      - the header is missing, deleted or invoiced;
--      - the OA line (OAID = OADtl.TransSlNo) is not on the slip's OA;
--      - the same OAID is already packed into the same CaseNo on this slip;
--      - the qty would take the OA line past its ordered qty, counting every
--        other non-deleted row on every non-deleted slip.
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_PackingSlipDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PackingSlipDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PackingSlipDtl
    @ID              INT             OUTPUT,
    @TransSlNo       INT,
    @MTransNo        INT,
    @SlNo            NUMERIC(12, 2),
    @OAID            INT,
    @CaseNo          INT,
    @PartNo          INT,
    @PartCode        VARCHAR(30),
    @PartDescription VARCHAR(250),
    @MOC             VARCHAR(250),
    @OARemarks       VARCHAR(250),
    @ItemRemarks     VARCHAR(500),
    @HSNCode         VARCHAR(10),
    @Qty             NUMERIC(12, 4),
    @Unit            VARCHAR(20),
    @CaseSize        VARCHAR(50),
    @NetWeight       NUMERIC(12, 2),
    @GrossWeight     NUMERIC(12, 2),
    @ValveSlNo       VARCHAR(200),
    @BoxType         VARCHAR(50),
    @SystemIP        VARCHAR(20),
    @CreatedBy       INT,
    @CreatedOn       DATETIME2,
    @ModifiedBy      INT,
    @ModifiedOn      DATETIME2,
    @DeleteReason    VARCHAR(250),
    @LockStatus      CHAR(1),
    @SubscID         INT,
    @Status          VARCHAR(20),
    @Message         NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF (@Status = 'Insert' OR @Status = 'Update')
        BEGIN
            DECLARE @OATransNo INT, @HdrClosed CHAR(1),
                    @OAQty NUMERIC(12, 4), @Packed NUMERIC(14, 4),
                    @Self INT = CASE WHEN @Status = 'Update' THEN @TransSlNo ELSE 0 END;

            SELECT @OATransNo = TargetTransNo,
                   @HdrClosed = CASE WHEN LTRIM(RTRIM(DocStatus)) = 'Y' THEN 'Y' ELSE 'N' END
            FROM dbo.PackingSlipMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SELECT @OAQty = Qty
            FROM dbo.OADtl
            WHERE TransSlNo = @OAID AND MTransNo = @OATransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            SELECT @Packed = ISNULL(SUM(d.Qty), 0)
            FROM dbo.PackingSlipDtl d WITH (UPDLOCK, HOLDLOCK)
            JOIN dbo.PackingSlipMst m ON m.MTransNo = d.MTransNo
            WHERE d.OAID = @OAID
              AND d.SubscID = @SubscID
              AND d.DeleteStatus = 'N'
              AND m.DeleteStatus = 'N'
              AND d.TransSlNo <> @Self;

            IF (@HdrClosed IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Packing Slip not found.'; END
            ELSE IF (@HdrClosed = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'Packing Slip is already invoiced and cannot be changed.'; END
            ELSE IF (@OAQty IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' is not on this order acceptance.'; END
            ELSE IF EXISTS (SELECT 1 FROM dbo.PackingSlipDtl
                            WHERE MTransNo = @MTransNo AND OAID = @OAID AND CaseNo = @CaseNo
                              AND DeleteStatus = 'N' AND TransSlNo <> @Self)
            BEGIN SET @ID = 0; SET @Message = 'OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' is already packed in case ' + CONVERT(VARCHAR(12), @CaseNo) + '.'; END
            ELSE IF (@Packed + @Qty > @OAQty)
            BEGIN
                SET @ID = 0;
                SET @Message = 'Qty for OA ID ' + CONVERT(VARCHAR(12), @OAID) + ' exceeds the balance ('
                             + CONVERT(VARCHAR(20), CONVERT(NUMERIC(14, 2), @OAQty - @Packed)) + ').';
            END
            ELSE IF (@Status = 'Insert')
            BEGIN
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                FROM dbo.PackingSlipDtl
                WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

                INSERT INTO dbo.PackingSlipDtl
                (
                    SlNo, MTransNo, OAID, CaseNo, PartNo, PartCode, PartDescription,
                    MOC, OARemarks, ItemRemarks, HSNCode, Qty, Unit, CaseSize,
                    NetWeight, GrossWeight, ValveSlNo, BoxType,
                    SystemIP, CreatedBy, CreatedOn,
                    DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                )
                VALUES
                (
                    @SlNo, @MTransNo, @OAID, @CaseNo, @PartNo,
                    LTRIM(RTRIM(@PartCode)), LTRIM(RTRIM(@PartDescription)),
                    LTRIM(RTRIM(@MOC)), LTRIM(RTRIM(@OARemarks)), LTRIM(RTRIM(@ItemRemarks)),
                    LTRIM(RTRIM(@HSNCode)), @Qty, LTRIM(RTRIM(@Unit)), LTRIM(RTRIM(@CaseSize)),
                    @NetWeight, @GrossWeight, LTRIM(RTRIM(@ValveSlNo)), LTRIM(RTRIM(@BoxType)),
                    @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                    'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                );

                SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Case line saved successfully.';
            END
            ELSE
            BEGIN
                UPDATE dbo.PackingSlipDtl
                SET OAID            = @OAID,
                    CaseNo          = @CaseNo,
                    PartNo          = @PartNo,
                    PartCode        = LTRIM(RTRIM(@PartCode)),
                    PartDescription = LTRIM(RTRIM(@PartDescription)),
                    MOC             = LTRIM(RTRIM(@MOC)),
                    OARemarks       = LTRIM(RTRIM(@OARemarks)),
                    ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                    HSNCode         = LTRIM(RTRIM(@HSNCode)),
                    Qty             = @Qty,
                    Unit            = LTRIM(RTRIM(@Unit)),
                    CaseSize        = LTRIM(RTRIM(@CaseSize)),
                    NetWeight       = @NetWeight,
                    GrossWeight     = @GrossWeight,
                    ValveSlNo       = LTRIM(RTRIM(@ValveSlNo)),
                    BoxType         = LTRIM(RTRIM(@BoxType)),
                    SystemIP        = @SystemIP,
                    ModifiedBy      = @ModifiedBy,
                    ModifiedOn      = ISNULL(@ModifiedOn, GETDATE()),
                    LockStatus      = ISNULL(@LockStatus, LockStatus),
                    LockCounter     = ISNULL(LockCounter, 0) + 1
                WHERE TransSlNo    = @TransSlNo
                  AND MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @TransSlNo; SET @Message = 'Case line updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PackingSlipDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Case line deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PackingSlipDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All case lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active case lines found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Invalid status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
