-- ============================================================
-- 12. Purchase — ValidTill
-- ------------------------------------------------------------
-- Request For Quote and RFQ Price Update both carry a "Valid Till"
-- date on screen: the last day the quotation the supplier is asked
-- for (RFQMst) — or the one they returned (RFQPriceMst) — stays good.
-- Neither table had a column for it, so the field could not round-trip.
--
-- This script adds the column to both headers and re-creates the two
-- header procedures with a matching @ValidTill parameter. It is
-- idempotent: re-running it is safe.
-- ============================================================

USE EMSSoft;
GO

-- ============================================================
-- 1. Columns
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.RFQMst') AND name = 'ValidTill')
BEGIN
    ALTER TABLE dbo.RFQMst ADD ValidTill DATE NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.RFQPriceMst') AND name = 'ValidTill')
BEGIN
    ALTER TABLE dbo.RFQPriceMst ADD ValidTill DATE NULL;
END
GO

-- ============================================================
-- 2. SP_RFQMst  (adds @ValidTill after @DocDate)
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.SP_RFQMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @ValidTill      DATE,
    @DocAmdNo       SMALLINT,
    @PRTransNo      INT,
    @DocNotes       VARCHAR(250),
    @DocAmdNotes    VARCHAR(250),
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

        IF (@Status = 'Insert')
        BEGIN
            IF (@DocAmdNo = 0)
            BEGIN
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.RFQMst
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;
            END
            ELSE IF (@DocAmdNo > 0)
            BEGIN
                UPDATE dbo.RFQMst SET DocAmdStatus = 'Y'
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
                  AND SubscID = @SubscID AND DocAmdNo < @DocAmdNo AND DeleteStatus = 'N';
            END

            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'RFQ already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.RFQMst
                (DocSeries, DocYear, DocNo, DocDate, ValidTill, DocAmdNo, DocAmdStatus,
                 PRTransNo, DocNotes, DocAmdNotes, DocStatus, SystemIP,
                 CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
                 LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @ValidTill, @DocAmdNo, 'N',
                 @PRTransNo, LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@DocAmdNotes)),
                 'N', @SystemIP, @CreatedBy, @CreatedOn,
                 'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'RFQ saved successfully.';
            END
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N'
              AND SubscID = @SubscID AND MTransNo <> @MTransNo;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'RFQ already exists.'; END
            ELSE
            BEGIN
                UPDATE dbo.RFQMst
                SET DocDate = @DocDate, ValidTill = @ValidTill, PRTransNo = @PRTransNo,
                    DocNotes = LTRIM(RTRIM(@DocNotes)), DocAmdNotes = LTRIM(RTRIM(@DocAmdNotes)),
                    SystemIP = @SystemIP, ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                    LockStatus = ISNULL(@LockStatus, LockStatus),
                    LockCounter = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RFQMst
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.RFQMst
            SET LockStatus = 'Y', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.RFQMst
            SET LockStatus = 'N', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RFQ unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

-- ============================================================
-- 3. SP_RFQPriceMst  (adds @ValidTill after @DocDate)
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.SP_RFQPriceMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @ValidTill      DATE,
    @DocAmdNo       SMALLINT,
    @DocAmdNotes    VARCHAR(250),
    @TargetTransNo  INT,
    @VendorNo       INT,
    @VendorName     VARCHAR(100),
    @ContactNo      VARCHAR(100),
    @VendorAddress  VARCHAR(250),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DocNotes       VARCHAR(1000),
    @PRYear         INT,
    @PRNo           INT,
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

        IF (@Status = 'Insert')
        BEGIN
            IF (@DocAmdNo = 0)
            BEGIN
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.RFQPriceMst
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;
            END
            ELSE IF (@DocAmdNo > 0)
            BEGIN
                UPDATE dbo.RFQPriceMst SET DocAmdStatus = 'Y'
                WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
                  AND SubscID = @SubscID AND DocAmdNo < @DocAmdNo AND DeleteStatus = 'N';
            END

            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQPriceMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Purchase Quote already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.RFQPriceMst
                (DocSeries, DocYear, DocNo, DocDate, ValidTill, DocAmdNo, DocAmdStatus,
                 DocAmdNotes, TargetTransNo, VendorNo, VendorName, ContactNo,
                 VendorAddress, RefNo, RefDate, DocNotes, DocStatus,
                 PRYear, PRNo, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @ValidTill, @DocAmdNo, 'N',
                 LTRIM(RTRIM(@DocAmdNotes)), @TargetTransNo, @VendorNo,
                 LTRIM(RTRIM(@VendorName)), LTRIM(RTRIM(@ContactNo)),
                 LTRIM(RTRIM(@VendorAddress)), LTRIM(RTRIM(@RefNo)), @RefDate,
                 LTRIM(RTRIM(@DocNotes)), 'N', @PRYear, @PRNo, @SystemIP,
                 @CreatedBy, @CreatedOn, 'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Purchase Quote saved successfully.';
            END
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @MCount = COUNT(MTransNo) FROM dbo.RFQPriceMst
            WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
              AND DocAmdNo = @DocAmdNo AND DeleteStatus = 'N'
              AND SubscID = @SubscID AND MTransNo <> @MTransNo;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Purchase Quote already exists.'; END
            ELSE
            BEGIN
                UPDATE dbo.RFQPriceMst
                SET DocDate = @DocDate, ValidTill = @ValidTill,
                    DocAmdNotes = LTRIM(RTRIM(@DocAmdNotes)),
                    TargetTransNo = @TargetTransNo, VendorNo = @VendorNo,
                    VendorName = LTRIM(RTRIM(@VendorName)),
                    ContactNo = LTRIM(RTRIM(@ContactNo)),
                    VendorAddress = LTRIM(RTRIM(@VendorAddress)),
                    RefNo = LTRIM(RTRIM(@RefNo)), RefDate = @RefDate,
                    DocNotes = LTRIM(RTRIM(@DocNotes)), PRYear = @PRYear, PRNo = @PRNo,
                    SystemIP = @SystemIP, ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                    LockStatus = ISNULL(@LockStatus, LockStatus),
                    LockCounter = ISNULL(LockCounter, 0) + 1
                WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

                IF @@ROWCOUNT > 0
                BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Quote updated successfully.'; END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RFQPriceMst
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Quote deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.RFQPriceMst
            SET LockStatus = 'Y', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Quote locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.RFQPriceMst
            SET LockStatus = 'N', ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn,
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Purchase Quote unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
