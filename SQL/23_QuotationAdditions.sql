/*==============================================================================
  23. QUOTATION ADDITIONS  -  the rules the live Quotation screen relies on

  Quotation.html has moved off its in-page mock data and onto QtnController
  (/api/Qtn/...). The tables and the six procedures already exist, so NO
  SCHEMA CHANGE is needed. This script only re-issues dbo.SP_QtnMst with the
  revision rules the old procedure left to the caller:

    INSERT (amendment, @DocAmdNo > 0)
      * The document being amended must still have a current revision
        (DocAmdStatus 'N', not deleted) - otherwise it is refused.
      * The revision number is allocated here, like DocNo on a first issue:
        MAX(DocAmdNo) + 1 over EVERY row of the document, deleted ones
        included. UQ_QtnMst includes DeleteStatus, so re-using the number of
        a deleted revision would collide the next time that one is deleted.
      * Every older revision is marked superseded (DocAmdStatus 'Y').

    UPDATE
      * Refused on a superseded revision (it is history) and on an approved
        one (LockStatus 'Y') - both change only by amendment.

    DELETE
      * Refused on a superseded revision - only the current one can go.
      * Refused once an Order Acceptance (OAMst.TargetTransNo) is raised
        against it.
      * Deleting the current amendment re-instates the revision before it,
        so the document does not vanish from the list.

  Everything else (column list, trimming, DocNo allocation, Lock / Unlock)
  is unchanged from the procedure that was in the database.

  Idempotent - safe to re-run.
==============================================================================*/
USE EMSSoft;
GO

-- Stub-then-ALTER: an unqualified SP_ name resolves against master first.
IF OBJECT_ID(N'dbo.SP_QtnMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @DocAmdNo       SMALLINT,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @VendorNo       INT,
    @VendorCode     VARCHAR(30),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @CPName         VARCHAR(80),
    @CPDesignation  VARCHAR(50),
    @CPPhone        VARCHAR(30),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @SalesExec      VARCHAR(50),
    @TargetDate     DATE,
    @RefProject     VARCHAR(100),
    @DocNotes       VARCHAR(250),
    @DocAmdNotes    VARCHAR(250),
    @TaxType        VARCHAR(20),
    @BasicValue     NUMERIC(16, 2),
    @ExemptValue    NUMERIC(16, 2),
    @TaxableValue   NUMERIC(16, 2),
    @PnFAmt         NUMERIC(16, 2),
    @FreightAmt     NUMERIC(16, 2),
    @IGSTAmt        NUMERIC(16, 2),
    @CGSTAmt        NUMERIC(16, 2),
    @SGSTAmt        NUMERIC(16, 2),
    @OtherCharges   VARCHAR(50),
    @OtherAmt       NUMERIC(16, 2),
    @RoundOff       NUMERIC(6, 2),
    @NetValue       NUMERIC(18, 2),
    @TxnCurrency    VARCHAR(30),
    @CurrencyRate   NUMERIC(12, 4),
    @CurrencyAmount NUMERIC(14, 2),
    @StateName      VARCHAR(50),
    @FileAttach     VARCHAR(100),
    @Zone           VARCHAR(50),
    @QtnCategory    VARCHAR(50),
    @ChassisNo      VARCHAR(50),
    @EngineNo       VARCHAR(50),
    @VehicleModel   VARCHAR(50),
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

    DECLARE @CurAmdStatus CHAR(1),
            @CurLock      CHAR(1),
            @CurSeries    CHAR(10),
            @CurYear      INT,
            @CurDocNo     INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SET @ID = 0;
            SET @Message = NULL;

            IF (ISNULL(@DocAmdNo, 0) = 0)
            BEGIN
                SET @DocAmdNo = 0;
                SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
                FROM dbo.QtnMst
                WHERE DocSeries = @DocSeries
                  AND DocYear   = @DocYear
                  AND SubscID   = @SubscID;
            END
            ELSE
            BEGIN
                -- An amendment revises a document that still has a live,
                -- current revision.
                IF NOT EXISTS (SELECT 1 FROM dbo.QtnMst
                               WHERE DocSeries    = @DocSeries
                                 AND DocYear      = @DocYear
                                 AND DocNo        = @DocNo
                                 AND SubscID      = @SubscID
                                 AND DeleteStatus = 'N'
                                 AND DocAmdStatus = 'N')
                BEGIN
                    SET @Message = 'The quotation being amended was not found, or is not its current revision.';
                END
                ELSE
                BEGIN
                    -- Allocated here, over deleted rows too (UQ_QtnMst includes DeleteStatus).
                    SELECT @DocAmdNo = ISNULL(MAX(DocAmdNo), 0) + 1
                    FROM dbo.QtnMst
                    WHERE DocSeries = @DocSeries
                      AND DocYear   = @DocYear
                      AND DocNo     = @DocNo
                      AND SubscID   = @SubscID;

                    UPDATE dbo.QtnMst
                       SET DocAmdStatus = 'Y'
                     WHERE DocSeries    = @DocSeries
                       AND DocYear      = @DocYear
                       AND DocNo        = @DocNo
                       AND SubscID      = @SubscID
                       AND DocAmdNo     < @DocAmdNo
                       AND DeleteStatus = 'N';
                END
            END

            IF (@Message IS NULL)
            BEGIN
                SELECT @MCount = COUNT(MTransNo)
                FROM dbo.QtnMst
                WHERE DocSeries    = @DocSeries
                  AND DocYear      = @DocYear
                  AND DocNo        = @DocNo
                  AND DocAmdNo     = @DocAmdNo
                  AND DeleteStatus = 'N'
                  AND SubscID      = @SubscID;

                IF (@MCount > 0)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Quotation already exists.';
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.QtnMst
                    (
                        DocSeries, DocYear, DocNo, DocDate, DocAmdNo, DocAmdStatus,
                        TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                        VendorNo, VendorCode, VendorName, Address, GSTIN,
                        PhoneNo, EmailID, CPName, CPDesignation, CPPhone,
                        RefNo, RefDate, SalesExec, TargetDate, RefProject,
                        DocNotes, DocAmdNotes, DocStatus, TaxType,
                        BasicValue, ExemptValue, TaxableValue, PnFAmt, FreightAmt,
                        IGSTAmt, CGSTAmt, SGSTAmt, OtherCharges, OtherAmt,
                        RoundOff, NetValue, TxnCurrency, CurrencyRate, CurrencyAmount,
                        StateName, FileAttach, Zone, QtnCategory,
                        ChassisNo, EngineNo, VehicleModel,
                        SystemIP, CreatedBy, CreatedOn,
                        DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                    )
                    VALUES
                    (
                        LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate, @DocAmdNo, 'N',
                        @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                        @VendorNo, LTRIM(RTRIM(@VendorCode)), LTRIM(RTRIM(@VendorName)),
                        LTRIM(RTRIM(@Address)), LTRIM(RTRIM(ISNULL(@GSTIN, ''))),
                        LTRIM(RTRIM(@PhoneNo)), LTRIM(RTRIM(@EmailID)),
                        LTRIM(RTRIM(@CPName)), LTRIM(RTRIM(@CPDesignation)),
                        LTRIM(RTRIM(@CPPhone)), LTRIM(RTRIM(@RefNo)), @RefDate,
                        LTRIM(RTRIM(@SalesExec)), @TargetDate, LTRIM(RTRIM(@RefProject)),
                        LTRIM(RTRIM(@DocNotes)), LTRIM(RTRIM(@DocAmdNotes)),
                        'N',  -- DocStatus hardcoded
                        LTRIM(RTRIM(@TaxType)),
                        @BasicValue, @ExemptValue, @TaxableValue, @PnFAmt, @FreightAmt,
                        @IGSTAmt, @CGSTAmt, @SGSTAmt,
                        LTRIM(RTRIM(@OtherCharges)), @OtherAmt, @RoundOff, @NetValue,
                        LTRIM(RTRIM(@TxnCurrency)), @CurrencyRate, @CurrencyAmount,
                        LTRIM(RTRIM(@StateName)), LTRIM(RTRIM(@FileAttach)),
                        LTRIM(RTRIM(@Zone)), LTRIM(RTRIM(@QtnCategory)),
                        LTRIM(RTRIM(@ChassisNo)), LTRIM(RTRIM(@EngineNo)),
                        LTRIM(RTRIM(@VehicleModel)),
                        @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                        'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                    );

                    SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                    SET @Message = CASE WHEN @DocAmdNo > 0
                                        THEN 'Quotation amended successfully.'
                                        ELSE 'Quotation saved successfully.' END;
                END
            END
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            SELECT @CurAmdStatus = DocAmdStatus, @CurLock = LockStatus
            FROM dbo.QtnMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@CurAmdStatus IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            ELSE IF (@CurAmdStatus = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'This revision has been superseded by an amendment and cannot be edited.'; END
            ELSE IF (@CurLock = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'This quotation is approved and cannot be edited. Amend it to raise a revision.'; END
            ELSE
            BEGIN
                SELECT @MCount = COUNT(MTransNo)
                FROM dbo.QtnMst
                WHERE DocSeries    = @DocSeries
                  AND DocYear      = @DocYear
                  AND DocNo        = @DocNo
                  AND DocAmdNo     = @DocAmdNo
                  AND DeleteStatus = 'N'
                  AND SubscID      = @SubscID
                  AND MTransNo     <> @MTransNo;

                IF (@MCount > 0)
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Quotation already exists.';
                END
                ELSE
                BEGIN
                    UPDATE dbo.QtnMst
                    SET DocDate        = @DocDate,
                        TargetTransNo  = @TargetTransNo,
                        TargetDocYear  = @TargetDocYear,
                        TargetDocNo    = @TargetDocNo,
                        TargetAmdNo    = @TargetAmdNo,
                        VendorNo       = @VendorNo,
                        VendorCode     = LTRIM(RTRIM(@VendorCode)),
                        VendorName     = LTRIM(RTRIM(@VendorName)),
                        Address        = LTRIM(RTRIM(@Address)),
                        GSTIN          = LTRIM(RTRIM(ISNULL(@GSTIN, ''))),
                        PhoneNo        = LTRIM(RTRIM(@PhoneNo)),
                        EmailID        = LTRIM(RTRIM(@EmailID)),
                        CPName         = LTRIM(RTRIM(@CPName)),
                        CPDesignation  = LTRIM(RTRIM(@CPDesignation)),
                        CPPhone        = LTRIM(RTRIM(@CPPhone)),
                        RefNo          = LTRIM(RTRIM(@RefNo)),
                        RefDate        = @RefDate,
                        SalesExec      = LTRIM(RTRIM(@SalesExec)),
                        TargetDate     = @TargetDate,
                        RefProject     = LTRIM(RTRIM(@RefProject)),
                        DocNotes       = LTRIM(RTRIM(@DocNotes)),
                        DocAmdNotes    = LTRIM(RTRIM(@DocAmdNotes)),
                        TaxType        = LTRIM(RTRIM(@TaxType)),
                        BasicValue     = @BasicValue,
                        ExemptValue    = @ExemptValue,
                        TaxableValue   = @TaxableValue,
                        PnFAmt         = @PnFAmt,
                        FreightAmt     = @FreightAmt,
                        IGSTAmt        = @IGSTAmt,
                        CGSTAmt        = @CGSTAmt,
                        SGSTAmt        = @SGSTAmt,
                        OtherCharges   = LTRIM(RTRIM(@OtherCharges)),
                        OtherAmt       = @OtherAmt,
                        RoundOff       = @RoundOff,
                        NetValue       = @NetValue,
                        TxnCurrency    = LTRIM(RTRIM(@TxnCurrency)),
                        CurrencyRate   = @CurrencyRate,
                        CurrencyAmount = @CurrencyAmount,
                        StateName      = LTRIM(RTRIM(@StateName)),
                        FileAttach     = LTRIM(RTRIM(@FileAttach)),
                        Zone           = LTRIM(RTRIM(@Zone)),
                        QtnCategory    = LTRIM(RTRIM(@QtnCategory)),
                        ChassisNo      = LTRIM(RTRIM(@ChassisNo)),
                        EngineNo       = LTRIM(RTRIM(@EngineNo)),
                        VehicleModel   = LTRIM(RTRIM(@VehicleModel)),
                        SystemIP       = @SystemIP,
                        ModifiedBy     = @ModifiedBy,
                        ModifiedOn     = ISNULL(@ModifiedOn, GETDATE()),
                        LockStatus     = ISNULL(@LockStatus, LockStatus),
                        LockCounter    = ISNULL(LockCounter, 0) + 1
                    WHERE MTransNo     = @MTransNo
                      AND SubscID      = @SubscID
                      AND DeleteStatus = 'N';

                    IF @@ROWCOUNT > 0
                    BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation updated successfully.'; END
                    ELSE
                    BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
                END
            END
        END

        --=========================================
        -- DELETE
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            SELECT @CurAmdStatus = DocAmdStatus,
                   @CurSeries    = DocSeries,
                   @CurYear      = DocYear,
                   @CurDocNo     = DocNo
            FROM dbo.QtnMst
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@CurAmdStatus IS NULL)
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            ELSE IF (@CurAmdStatus = 'Y')
            BEGIN SET @ID = 0; SET @Message = 'Only the current revision of a quotation can be deleted.'; END
            ELSE IF EXISTS (SELECT 1 FROM dbo.OAMst
                            WHERE TargetTransNo = @MTransNo
                              AND SubscID       = @SubscID
                              AND DeleteStatus  = 'N')
            BEGIN SET @ID = 0; SET @Message = 'An Order Acceptance has been raised against this quotation, so it cannot be deleted.'; END
            ELSE
            BEGIN
                UPDATE dbo.QtnMst
                SET DeleteStatus = 'Y',
                    DeleteReason = @DeleteReason,
                    ModifiedBy   = @ModifiedBy,
                    ModifiedOn   = ISNULL(@ModifiedOn, GETDATE())
                WHERE MTransNo     = @MTransNo
                  AND SubscID      = @SubscID
                  AND DeleteStatus <> 'Y';

                IF @@ROWCOUNT > 0
                BEGIN
                    -- The revision before it becomes current again.
                    UPDATE dbo.QtnMst
                       SET DocAmdStatus = 'N'
                     WHERE MTransNo = (SELECT TOP 1 MTransNo FROM dbo.QtnMst
                                       WHERE DocSeries    = @CurSeries
                                         AND DocYear      = @CurYear
                                         AND DocNo        = @CurDocNo
                                         AND SubscID      = @SubscID
                                         AND DeleteStatus = 'N'
                                       ORDER BY DocAmdNo DESC);

                    SET @ID = @MTransNo; SET @Message = 'Quotation deleted successfully.';
                END
                ELSE
                BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
            END
        END

        --=========================================
        -- LOCK
        --=========================================
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.QtnMst
            SET LockStatus  = 'Y',
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- UNLOCK
        --=========================================
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.QtnMst
            SET LockStatus  = 'N',
                ModifiedBy  = @ModifiedBy,
                ModifiedOn  = ISNULL(@ModifiedOn, GETDATE()),
                LockCounter = ISNULL(LockCounter, 0) + 1
            WHERE MTransNo     = @MTransNo
              AND SubscID      = @SubscID
              AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Quotation unlocked successfully.'; END
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
