/*
================================================================================
  28_DeliveryChallanRGP.sql
  Delivery Challan, Returnable Gate Pass (outward) and RGP Inward.

    Tables : RGPMst, RGPDtl, RGPTerms, RGPInwMst, RGPInwDtl   (already exist)
    Procs  : SP_RGPMst, SP_RGPDtl, SP_RGPTerms, SP_RGPInwMst, SP_RGPInwDtl

  One set of tables serves two documents, told apart by RGPMst.RGPType:
    'DC'  — Delivery Challan. Non-returnable: never a source for an inward.
    'RGP' — Returnable Gate Pass. Received back through RGPInwMst / RGPInwDtl.
  (The same split the AquaFlow app used: DCController and RGPController both
  write SP_RGPMst, passing "DC" and "RGP".)

  Links:
    RGPInwMst.TargetTransNo / TargetDocYear / TargetDocNo = the RGP (RGPMst)
    RGPInwDtl.LinkNo                                      = RGPDtl.TransSlNo

  LockStatus on RGPMst:  N = draft, S = submitted, Y = approved.
    Save can only write N or S; Y is reached through Status 'Approve'
    (S -> Y) or the legacy 'Lock'. An approved document cannot be changed or
    deleted, and neither can its lines or terms.

  Numbering: each DocSeries + DocYear + RGPType keeps its own DocNo sequence
  (DC #1 and RGP #1 may share a series). The procedures allocate DocNo on
  Insert — whatever DocNo the client sent is only a preview.

  Server-side snapshots (the client's copies are ignored):
    * vendor code / name / address / GSTIN / state / phone / email from
      SupplierMaster;
    * part code / description / HSN (and a blank unit) from PartMaster;
    * line Amount = ROUND(Qty x Rate, 2), header ApproxValue = SUM(Amount);
    * an inward line's part, grade, unit, rate and RGPQty from its RGP line.

  RGP balance: RGPDtl.Qty less every RGPInwDtl.Qty on a live inward that links
  to it. SP_RGPInwDtl refuses an Inw Qty above the balance.

  DEVIATION from the original DDL: UQ_RGPMst now carries RGPType, otherwise
  DC #1 and RGP #1 in the same series would collide.

  Idempotent. SP_* procedures use create-stub-then-ALTER: master holds stray
  SP_* procedures that break CREATE OR ALTER (sp_ name resolution). Every
  ALTER carries the procedure's full body, so this file is the source of
  truth for these five procedures.
================================================================================
*/
USE EMSSoft;
GO

--------------------------------------------------------------------------------
-- 1. RGPInwDtl.LinkNo — the RGPDtl line an inward line receives against
--------------------------------------------------------------------------------
IF COL_LENGTH(N'dbo.RGPInwDtl', N'LinkNo') IS NULL
    ALTER TABLE dbo.RGPInwDtl ADD LinkNo INT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.RGPInwDtl') AND name = N'IX_RGPInwDtl_LinkNo')
    CREATE NONCLUSTERED INDEX IX_RGPInwDtl_LinkNo
        ON dbo.RGPInwDtl (LinkNo, DeleteStatus) INCLUDE (MTransNo, Qty);
GO

--------------------------------------------------------------------------------
-- 2. UQ_RGPMst — numbering is per RGPType, so the key must carry it
--------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.RGPMst') AND name = N'UQ_RGPMst')
   AND NOT EXISTS (SELECT 1
                     FROM sys.indexes i
                     JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                     JOIN sys.columns c        ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                    WHERE i.object_id = OBJECT_ID(N'dbo.RGPMst') AND i.name = N'UQ_RGPMst'
                      AND c.name = N'RGPType')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.RGPMst') AND name = N'UQ_RGPMst')
        ALTER TABLE dbo.RGPMst DROP CONSTRAINT UQ_RGPMst;
    ELSE
        DROP INDEX UQ_RGPMst ON dbo.RGPMst;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.RGPMst') AND name = N'UQ_RGPMst')
    ALTER TABLE dbo.RGPMst ADD CONSTRAINT UQ_RGPMst
        UNIQUE NONCLUSTERED (DocSeries, DocYear, RGPType, DocNo, DeleteStatus, SubscID);
GO

--------------------------------------------------------------------------------
-- 3. SP_RGPMst — DC / RGP header
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_RGPMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RGPMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RGPMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      VARCHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @RGPType        VARCHAR(10),
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
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @DueDate        DATE,
    @DocNotes       VARCHAR(250),
    @DocStatus      CHAR(10),
    @ApproxValue    NUMERIC(18, 2),
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

    DECLARE @CurLock CHAR(1),
            @CurType VARCHAR(10),
            @DocName VARCHAR(40);

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @DocSeries = LTRIM(RTRIM(ISNULL(@DocSeries, '')));
        SET @RGPType   = UPPER(LTRIM(RTRIM(ISNULL(@RGPType, ''))));
        SET @CreatedOn  = ISNULL(@CreatedOn, SYSDATETIME());
        SET @ModifiedOn = ISNULL(@ModifiedOn, SYSDATETIME());

        -- Every status but Insert works on an existing, live document.
        IF (@Status <> 'Insert')
        BEGIN
            SELECT @CurLock = LockStatus, @CurType = RGPType
              FROM dbo.RGPMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@CurType IS NULL)
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            -- The type a document was raised as never changes.
            SET @RGPType = @CurType;
        END

        SET @DocName = CASE @RGPType WHEN 'DC' THEN 'delivery challan'
                                     ELSE 'returnable gate pass' END;

        --=========================================
        -- INSERT / UPDATE — shared checks and snapshots
        --=========================================
        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            IF (@RGPType NOT IN ('DC', 'RGP'))
            BEGIN
                SET @ID = 0; SET @Message = 'RGP Type must be DC or RGP.';
                COMMIT TRANSACTION; RETURN;
            END

            IF (@Status = 'Update' AND @CurLock = 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'This ' + @DocName + ' is approved and cannot be changed.';
                COMMIT TRANSACTION; RETURN;
            END

            -- A save writes a draft or a submission; approval has its own status.
            SET @LockStatus = CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'S' END;

            -- Vendor snapshot from Supplier Master — the client's copy is ignored.
            SET @VendorName = NULL;
            SELECT @VendorCode = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierCode, ''))), 20),
                   @VendorName = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierName, ''))), 100),
                   @Address    = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierAddress, ''))), 500),
                   @GSTIN      = LEFT(LTRIM(RTRIM(ISNULL(S.GSTIN, ''))), 20),
                   @StateName  = LEFT(S.StateName, 50),
                   @PhoneNo    = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(S.PhoneNo, ''))), ''), S.MobileNo), 30),
                   @EmailID    = LEFT(S.EmailID, 80)
              FROM dbo.SupplierMaster S
             WHERE S.MTransNo = @VendorNo AND S.SubscID = @SubscID AND S.DeleteStatus = 'N';

            IF (@VendorName IS NULL)
            BEGIN
                SET @ID = 0; SET @Message = 'Supplier not found.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            -- Allocate DocNo: DC and RGP number independently.
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.RGPMst WITH (UPDLOCK, HOLDLOCK)
             WHERE DocSeries = @DocSeries
               AND DocYear   = @DocYear
               AND RGPType   = @RGPType
               AND SubscID   = @SubscID;

            SELECT @MCount = COUNT(MTransNo)
              FROM dbo.RGPMst
             WHERE DocSeries    = @DocSeries
               AND DocYear      = @DocYear
               AND RGPType      = @RGPType
               AND DocNo        = @DocNo
               AND DeleteStatus = 'N'
               AND SubscID      = @SubscID;

            IF (@MCount > 0)
            BEGIN
                SET @ID = 0;
                SET @Message = 'This ' + @DocName + ' number already exists.';
            END
            ELSE
            BEGIN
                INSERT INTO dbo.RGPMst
                (
                    DocSeries, DocYear, DocNo, DocDate, RGPType,
                    TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo,
                    VendorNo, VendorCode, VendorName, Address, GSTIN,
                    StateName, PhoneNo, EmailID, RefNo, RefDate,
                    DueDate, DocNotes, DocStatus, ApproxValue,
                    SystemIP, CreatedBy, CreatedOn,
                    DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                )
                VALUES
                (
                    @DocSeries, @DocYear, @DocNo, @DocDate, @RGPType,
                    @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
                    @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
                    @StateName, @PhoneNo, @EmailID,
                    LTRIM(RTRIM(@RefNo)), @RefDate,
                    @DueDate, LTRIM(RTRIM(@DocNotes)), 'N',
                    0,   -- rolled up by SP_RGPDtl as the lines are written
                    @SystemIP, @CreatedBy, @CreatedOn,
                    'N', NULL, @LockStatus, 1, @SubscID
                );

                SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40)
                             + ' saved successfully.';
            END
        END

        --=========================================
        -- UPDATE — number, series and type never change
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RGPMst
               SET DocDate       = @DocDate,
                   TargetTransNo = @TargetTransNo,
                   TargetDocYear = @TargetDocYear,
                   TargetDocNo   = @TargetDocNo,
                   TargetAmdNo   = @TargetAmdNo,
                   VendorNo      = @VendorNo,
                   VendorCode    = @VendorCode,
                   VendorName    = @VendorName,
                   Address       = @Address,
                   GSTIN         = @GSTIN,
                   StateName     = @StateName,
                   PhoneNo       = @PhoneNo,
                   EmailID       = @EmailID,
                   RefNo         = LTRIM(RTRIM(@RefNo)),
                   RefDate       = @RefDate,
                   DueDate       = @DueDate,
                   DocNotes      = LTRIM(RTRIM(@DocNotes)),
                   ApproxValue   = (SELECT ISNULL(SUM(ISNULL(D.Amount, 0)), 0)
                                      FROM dbo.RGPDtl D
                                     WHERE D.MTransNo = @MTransNo AND D.DeleteStatus = 'N'),
                   SystemIP      = @SystemIP,
                   ModifiedBy    = @ModifiedBy,
                   ModifiedOn    = @ModifiedOn,
                   LockStatus    = @LockStatus,
                   LockCounter   = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40) + ' updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE — header, lines and terms together
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            IF (@CurLock = 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'An approved ' + @DocName + ' cannot be deleted.';
                COMMIT TRANSACTION; RETURN;
            END

            IF EXISTS (SELECT 1 FROM dbo.RGPInwMst
                        WHERE TargetTransNo = @MTransNo AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Material has already been received back against this gate pass. Delete the RGP inward first.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.RGPMst
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.RGPDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
                 WHERE MTransNo = @MTransNo AND DeleteStatus <> 'Y';

                UPDATE dbo.RGPTerms
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
                 WHERE MTransNo = @MTransNo AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo;
                SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40) + ' deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- APPROVE — submitted (S) -> approved (Y)
        --=========================================
        ELSE IF (@Status = 'Approve')
        BEGIN
            IF (ISNULL(@CurLock, '') <> 'S')
            BEGIN
                SET @ID = 0;
                SET @Message = CASE WHEN @CurLock = 'Y'
                                    THEN 'This ' + @DocName + ' is already approved.'
                                    ELSE 'Only a submitted ' + @DocName + ' can be approved.' END;
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.RGPDtl
                            WHERE MTransNo = @MTransNo AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'A ' + @DocName + ' without line items cannot be approved.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.RGPMst
               SET LockStatus  = 'Y',
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N'
               AND LockStatus   = 'S';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40) + ' approved successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- LOCK (legacy) — straight to approved
        --=========================================
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.RGPMst
               SET LockStatus  = 'Y',
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40) + ' locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- UNLOCK — back to draft, unless material has come back against it
        --=========================================
        ELSE IF (@Status = 'Unlock')
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.RGPInwMst
                        WHERE TargetTransNo = @MTransNo AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'Material has already been received back against this gate pass; it cannot be unlocked.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.RGPMst
               SET LockStatus  = 'N',
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = UPPER(LEFT(@DocName, 1)) + SUBSTRING(@DocName, 2, 40) + ' unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status.';
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
-- 4. SP_RGPDtl — DC / RGP line items
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_RGPDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RGPDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RGPDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12, 2),
    @PartNo         INT,
    @PartCode       VARCHAR(30),
    @PartDescription VARCHAR(100),
    @ItemRemarks    VARCHAR(500),
    @HSNCode        VARCHAR(10),
    @Qty            NUMERIC(12, 4),
    @Unit           VARCHAR(20),
    @Rate           NUMERIC(14, 2),
    @Amount         NUMERIC(16, 2),
    @GradeNo        INT,
    @Grade          VARCHAR(50),
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

    DECLARE @ParentLock CHAR(1),
            @PrimaryUnit VARCHAR(20);

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @CreatedOn  = ISNULL(@CreatedOn, SYSDATETIME());
        SET @ModifiedOn = ISNULL(@ModifiedOn, SYSDATETIME());

        SELECT @ParentLock = LockStatus
          FROM dbo.RGPMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF (@ParentLock IS NULL)
        BEGIN
            SET @ID = 0; SET @Message = 'Document not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@ParentLock = 'Y')
        BEGIN
            SET @ID = 0; SET @Message = 'This document is approved; its line items cannot be changed.';
            COMMIT TRANSACTION; RETURN;
        END

        --=========================================
        -- INSERT / UPDATE — part snapshot and amount
        --=========================================
        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            IF (ISNULL(@Qty, 0) <= 0)
            BEGIN
                SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
                COMMIT TRANSACTION; RETURN;
            END
            IF (ISNULL(@Rate, 0) < 0)
            BEGIN
                SET @ID = 0; SET @Message = 'Rate cannot be negative.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @PartDescription = NULL;
            SELECT @PartCode        = LEFT(LTRIM(RTRIM(ISNULL(P.PartCode, ''))), 30),
                   @PartDescription = LEFT(LTRIM(RTRIM(ISNULL(P.PartName, ''))), 100),
                   @HSNCode         = LEFT(P.HSNCode, 10),
                   @PrimaryUnit     = LEFT(P.PrimaryUnit, 20)
              FROM dbo.PartMaster P
             WHERE P.MTransNo = @PartNo AND P.SubscID = @SubscID AND P.DeleteStatus = 'N';

            IF (@PartDescription IS NULL)
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @Unit   = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@Unit, ''))), ''), @PrimaryUnit);
            SET @Amount = ROUND(@Qty * ISNULL(@Rate, 0), 2);
        END

        --=========================================
        -- INSERT — SlNo is the grid position when given, else the next one
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            IF (ISNULL(@SlNo, 0) <= 0)
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                  FROM dbo.RGPDtl
                 WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.RGPDtl
            (
                SlNo, MTransNo, PartNo, PartCode, PartDescription,
                ItemRemarks, HSNCode, Qty, Unit, Rate, Amount,
                GradeNo, Grade, SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @SlNo, @MTransNo, @PartNo,
                @PartCode, @PartDescription,
                LTRIM(RTRIM(@ItemRemarks)), @HSNCode,
                @Qty, LTRIM(RTRIM(@Unit)), ISNULL(@Rate, 0), @Amount,
                @GradeNo, LTRIM(RTRIM(@Grade)),
                @SystemIP, @CreatedBy, @CreatedOn,
                'N', NULL, 'N', 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RGPDtl
               SET SlNo            = ISNULL(NULLIF(@SlNo, 0), SlNo),
                   PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDescription,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   Qty             = @Qty,
                   Unit            = LTRIM(RTRIM(@Unit)),
                   Rate            = ISNULL(@Rate, 0),
                   Amount          = @Amount,
                   GradeNo         = @GradeNo,
                   Grade           = LTRIM(RTRIM(@Grade)),
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = @ModifiedOn,
                   LockCounter     = ISNULL(LockCounter, 0) + 1
             WHERE TransSlNo     = @TransSlNo
               AND MTransNo      = @MTransNo
               AND SubscID       = @SubscID
               AND DeleteStatus  = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RGPDtl
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE ALL
        --=========================================
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.RGPDtl
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status.';
        END

        -- Keep the header's Approx Value equal to its live lines.
        IF (@ID > 0)
            UPDATE dbo.RGPMst
               SET ApproxValue = (SELECT ISNULL(SUM(ISNULL(D.Amount, 0)), 0)
                                    FROM dbo.RGPDtl D
                                   WHERE D.MTransNo = @MTransNo AND D.DeleteStatus = 'N')
             WHERE MTransNo = @MTransNo;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

--------------------------------------------------------------------------------
-- 5. SP_RGPTerms — DC / RGP terms
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_RGPTerms', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RGPTerms AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RGPTerms
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(6, 2),
    @Title          VARCHAR(30),
    @Description    VARCHAR(250),
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

    DECLARE @ParentLock CHAR(1);

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @CreatedOn  = ISNULL(@CreatedOn, SYSDATETIME());
        SET @ModifiedOn = ISNULL(@ModifiedOn, SYSDATETIME());

        SELECT @ParentLock = LockStatus
          FROM dbo.RGPMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF (@ParentLock IS NULL)
        BEGIN
            SET @ID = 0; SET @Message = 'Document not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@ParentLock = 'Y')
        BEGIN
            SET @ID = 0; SET @Message = 'This document is approved; its terms cannot be changed.';
            COMMIT TRANSACTION; RETURN;
        END

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            IF (ISNULL(@SlNo, 0) <= 0)
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                  FROM dbo.RGPTerms
                 WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.RGPTerms
            (
                MTransNo, SlNo, Title, Description,
                SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
            )
            VALUES
            (
                @MTransNo, @SlNo,
                LTRIM(RTRIM(@Title)), LTRIM(RTRIM(@Description)),
                @SystemIP, @CreatedBy, @CreatedOn,
                'N', NULL, 'N', 1, @SubscID
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Term saved successfully.';
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RGPTerms
               SET SlNo        = ISNULL(NULLIF(@SlNo, 0), SlNo),
                   Title       = LTRIM(RTRIM(@Title)),
                   Description = LTRIM(RTRIM(@Description)),
                   SystemIP    = @SystemIP,
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RGPTerms
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Term deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE ALL
        --=========================================
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.RGPTerms
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All terms deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active terms found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status.';
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
-- 6. SP_RGPInwMst — RGP inward header
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_RGPInwMst', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RGPInwMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RGPInwMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @TargetDocYear  INT,
    @TargetDocNo    INT,
    @TargetAmdNo    INT,
    @TargetType     VARCHAR(10),
    @VendorNo       INT,
    @VendorCode     VARCHAR(20),
    @VendorName     VARCHAR(100),
    @Address        VARCHAR(500),
    @GSTIN          VARCHAR(20),
    @StateName      VARCHAR(50),
    @PhoneNo        VARCHAR(30),
    @EmailID        VARCHAR(80),
    @RefNo          VARCHAR(50),
    @RefDate        DATE,
    @InwardNo       VARCHAR(50),
    @InwardDate     DATE,
    @LRNo           VARCHAR(50),
    @LRDate         DATE,
    @DCNo           VARCHAR(50),
    @DCDate         DATE,
    @DocNotes       VARCHAR(250),
    @DocStatus      CHAR(10),
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

    DECLARE @CurTarget INT,
            @RgpType   VARCHAR(10),
            @RgpLock   CHAR(1),
            @Found     BIT = 0;

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @DocSeries  = LTRIM(RTRIM(ISNULL(@DocSeries, '')));
        SET @CreatedOn  = ISNULL(@CreatedOn, SYSDATETIME());
        SET @ModifiedOn = ISNULL(@ModifiedOn, SYSDATETIME());

        IF (@Status <> 'Insert')
        BEGIN
            SELECT @Found = 1, @CurTarget = TargetTransNo
              FROM dbo.RGPInwMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF (@Found = 0)
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        --=========================================
        -- INSERT / UPDATE — the gate pass decides target and supplier
        --=========================================
        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            -- An inward stays with the gate pass it was received against.
            IF (@Status = 'Update') SET @TargetTransNo = @CurTarget;

            SELECT @RgpType       = M.RGPType,
                   @RgpLock       = M.LockStatus,
                   @TargetDocYear = M.DocYear,
                   @TargetDocNo   = M.DocNo,
                   @VendorNo      = M.VendorNo,
                   @VendorCode    = M.VendorCode,
                   @VendorName    = M.VendorName,
                   @Address       = M.Address,
                   @GSTIN         = M.GSTIN,
                   @StateName     = M.StateName,
                   @PhoneNo       = M.PhoneNo,
                   @EmailID       = M.EmailID
              FROM dbo.RGPMst M
             WHERE M.MTransNo = @TargetTransNo AND M.SubscID = @SubscID AND M.DeleteStatus = 'N';

            IF (@RgpType IS NULL)
            BEGIN
                SET @ID = 0; SET @Message = 'Returnable gate pass not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF (@RgpType <> 'RGP')
            BEGIN
                SET @ID = 0; SET @Message = 'A delivery challan is not returnable; only a returnable gate pass can be received back.';
                COMMIT TRANSACTION; RETURN;
            END

            IF (@Status = 'Insert' AND @RgpLock <> 'Y')
            BEGIN
                SET @ID = 0; SET @Message = 'This returnable gate pass has not been approved yet.';
                COMMIT TRANSACTION; RETURN;
            END

            IF (@Status = 'Insert'
                AND NOT EXISTS (SELECT 1
                                  FROM dbo.RGPDtl D
                                 WHERE D.MTransNo = @TargetTransNo AND D.DeleteStatus = 'N'
                                   AND D.Qty > ISNULL((SELECT SUM(I.Qty)
                                                         FROM dbo.RGPInwDtl I
                                                         JOIN dbo.RGPInwMst IM ON IM.MTransNo = I.MTransNo
                                                        WHERE I.LinkNo = D.TransSlNo
                                                          AND I.DeleteStatus = 'N' AND IM.DeleteStatus = 'N'), 0)))
            BEGIN
                SET @ID = 0; SET @Message = 'Everything on this returnable gate pass has already come back.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Fresh supplier snapshot; the gate pass's own copy is the fallback.
            SELECT @VendorCode = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierCode, ''))), 20),
                   @VendorName = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierName, ''))), 100),
                   @Address    = LEFT(LTRIM(RTRIM(ISNULL(S.SupplierAddress, ''))), 500),
                   @GSTIN      = LEFT(LTRIM(RTRIM(ISNULL(S.GSTIN, ''))), 20),
                   @StateName  = LEFT(S.StateName, 50),
                   @PhoneNo    = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(S.PhoneNo, ''))), ''), S.MobileNo), 30),
                   @EmailID    = LEFT(S.EmailID, 80)
              FROM dbo.SupplierMaster S
             WHERE S.MTransNo = @VendorNo AND S.SubscID = @SubscID AND S.DeleteStatus = 'N';

            SET @TargetAmdNo = 0;
            SET @TargetType  = 'RGP';
        END

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.RGPInwMst WITH (UPDLOCK, HOLDLOCK)
             WHERE DocSeries = @DocSeries
               AND DocYear   = @DocYear
               AND SubscID   = @SubscID;

            SELECT @MCount = COUNT(MTransNo)
              FROM dbo.RGPInwMst
             WHERE DocSeries    = @DocSeries
               AND DocYear      = @DocYear
               AND DocNo        = @DocNo
               AND DeleteStatus = 'N'
               AND SubscID      = @SubscID;

            IF (@MCount > 0)
            BEGIN
                SET @ID = 0;
                SET @Message = 'RGP Inward already exists.';
            END
            ELSE
            BEGIN
                INSERT INTO dbo.RGPInwMst
                (
                    DocSeries, DocYear, DocNo, DocDate,
                    TargetTransNo, TargetDocYear, TargetDocNo, TargetAmdNo, TargetType,
                    VendorNo, VendorCode, VendorName, Address, GSTIN,
                    StateName, PhoneNo, EmailID, RefNo, RefDate,
                    InwardNo, InwardDate, LRNo, LRDate, DCNo, DCDate,
                    DocNotes, DocStatus, SystemIP, CreatedBy, CreatedOn,
                    DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID
                )
                VALUES
                (
                    @DocSeries, @DocYear, @DocNo, @DocDate,
                    @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo, @TargetType,
                    @VendorNo, ISNULL(@VendorCode, ''), ISNULL(@VendorName, ''),
                    ISNULL(@Address, ''), ISNULL(@GSTIN, ''),
                    @StateName, @PhoneNo, @EmailID,
                    LTRIM(RTRIM(@RefNo)), @RefDate,
                    LTRIM(RTRIM(@InwardNo)), @InwardDate,
                    LTRIM(RTRIM(@LRNo)), @LRDate,
                    LTRIM(RTRIM(@DCNo)), @DCDate,
                    LTRIM(RTRIM(@DocNotes)), 'N',
                    @SystemIP, @CreatedBy, @CreatedOn,
                    'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID
                );

                SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'RGP Inward saved successfully.';
            END
        END

        --=========================================
        -- UPDATE — number, series and gate pass never change
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RGPInwMst
               SET DocDate       = @DocDate,
                   TargetDocYear = @TargetDocYear,
                   TargetDocNo   = @TargetDocNo,
                   TargetAmdNo   = @TargetAmdNo,
                   TargetType    = @TargetType,
                   VendorNo      = @VendorNo,
                   VendorCode    = ISNULL(@VendorCode, ''),
                   VendorName    = ISNULL(@VendorName, ''),
                   Address       = ISNULL(@Address, ''),
                   GSTIN         = ISNULL(@GSTIN, ''),
                   StateName     = @StateName,
                   PhoneNo       = @PhoneNo,
                   EmailID       = @EmailID,
                   RefNo         = LTRIM(RTRIM(@RefNo)),
                   RefDate       = @RefDate,
                   InwardNo      = LTRIM(RTRIM(@InwardNo)),
                   InwardDate    = @InwardDate,
                   LRNo          = LTRIM(RTRIM(@LRNo)),
                   LRDate        = @LRDate,
                   DCNo          = LTRIM(RTRIM(@DCNo)),
                   DCDate        = @DCDate,
                   DocNotes      = LTRIM(RTRIM(@DocNotes)),
                   SystemIP      = @SystemIP,
                   ModifiedBy    = @ModifiedBy,
                   ModifiedOn    = @ModifiedOn,
                   LockStatus    = ISNULL(@LockStatus, LockStatus),
                   LockCounter   = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RGP Inward updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE — the quantities go back onto the gate pass balance
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RGPInwMst
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.RGPInwDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
                 WHERE MTransNo = @MTransNo AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'RGP Inward deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- LOCK
        --=========================================
        ELSE IF (@Status = 'Lock')
        BEGIN
            UPDATE dbo.RGPInwMst
               SET LockStatus  = 'Y',
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RGP Inward locked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- UNLOCK
        --=========================================
        ELSE IF (@Status = 'Unlock')
        BEGIN
            UPDATE dbo.RGPInwMst
               SET LockStatus  = 'N',
                   ModifiedBy  = @ModifiedBy,
                   ModifiedOn  = @ModifiedOn,
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'RGP Inward unlocked successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status.';
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
-- 7. SP_RGPInwDtl — RGP inward lines, with the balance check
--    @LinkNo (new, last, defaulted) = the RGPDtl.TransSlNo being received.
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.SP_RGPInwDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_RGPInwDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_RGPInwDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12, 2),
    @PartNo         INT,
    @PartCode       VARCHAR(30),
    @PartDescription VARCHAR(100),
    @ItemRemarks    VARCHAR(500),
    @HSNCode        VARCHAR(10),
    @RGPQty         NUMERIC(12, 2),
    @Qty            NUMERIC(12, 4),
    @Unit           VARCHAR(20),
    @Rate           NUMERIC(14, 2),
    @GradeNo        INT,
    @Grade          VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @LockStatus     CHAR(1),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @LinkNo         INT             = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Target   INT,
            @Found    BIT = 0,
            @LineOk   BIT = 0,
            @Received NUMERIC(18, 4),
            @Balance  NUMERIC(18, 4);

    BEGIN TRY
        BEGIN TRANSACTION;

        SET @CreatedOn  = ISNULL(@CreatedOn, SYSDATETIME());
        SET @ModifiedOn = ISNULL(@ModifiedOn, SYSDATETIME());

        SELECT @Found = 1, @Target = TargetTransNo
          FROM dbo.RGPInwMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF (@Found = 0)
        BEGIN
            SET @ID = 0; SET @Message = 'RGP Inward not found.';
            COMMIT TRANSACTION; RETURN;
        END

        --=========================================
        -- INSERT / UPDATE — the RGP line decides part, rate and balance
        --=========================================
        IF (@Status IN ('Insert', 'Update'))
        BEGIN
            IF (@Status = 'Update' AND @LinkNo IS NULL)
                SELECT @LinkNo = LinkNo FROM dbo.RGPInwDtl
                 WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo;

            SELECT @LineOk          = 1,
                   @PartNo          = D.PartNo,
                   @PartCode        = D.PartCode,
                   @PartDescription = D.PartDescription,
                   @HSNCode         = D.HSNCode,
                   @RGPQty          = D.Qty,
                   @Unit            = D.Unit,
                   @Rate            = D.Rate,
                   @GradeNo         = D.GradeNo,
                   @Grade           = D.Grade
              FROM dbo.RGPDtl D
             WHERE D.TransSlNo = @LinkNo AND D.MTransNo = @Target AND D.DeleteStatus = 'N';

            IF (@LineOk = 0)
            BEGIN
                SET @ID = 0; SET @Message = 'The gate pass line was not found on this returnable gate pass.';
                COMMIT TRANSACTION; RETURN;
            END

            IF (ISNULL(@Qty, 0) <= 0)
            BEGIN
                SET @ID = 0; SET @Message = 'Inw Qty must be greater than zero.';
                COMMIT TRANSACTION; RETURN;
            END

            IF EXISTS (SELECT 1 FROM dbo.RGPInwDtl
                        WHERE MTransNo = @MTransNo AND LinkNo = @LinkNo AND DeleteStatus = 'N'
                          AND TransSlNo <> ISNULL(@TransSlNo, 0))
            BEGIN
                SET @ID = 0; SET @Message = 'This gate pass line is already on this inward.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Received on every other live inward line against this RGP line.
            SELECT @Received = ISNULL(SUM(I.Qty), 0)
              FROM dbo.RGPInwDtl I WITH (UPDLOCK, HOLDLOCK)
              JOIN dbo.RGPInwMst IM ON IM.MTransNo = I.MTransNo
             WHERE I.LinkNo = @LinkNo
               AND I.DeleteStatus = 'N' AND IM.DeleteStatus = 'N'
               AND I.TransSlNo <> ISNULL(@TransSlNo, 0);

            SET @Balance = @RGPQty - @Received;

            IF (@Qty > @Balance)
            BEGIN
                SET @ID = 0;
                SET @Message = 'Inw Qty ' + CONVERT(VARCHAR(30), CAST(@Qty AS DECIMAL(18, 2)))
                             + ' is more than the RGP balance of '
                             + CONVERT(VARCHAR(30), CAST(@Balance AS DECIMAL(18, 2)))
                             + ' ' + ISNULL(@Unit, '') + ' for ' + ISNULL(@PartDescription, 'this line') + '.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        --=========================================
        -- INSERT
        --=========================================
        IF (@Status = 'Insert')
        BEGIN
            IF (ISNULL(@SlNo, 0) <= 0)
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                  FROM dbo.RGPInwDtl
                 WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.RGPInwDtl
            (
                SlNo, MTransNo, PartNo, PartCode, PartDescription,
                ItemRemarks, HSNCode, RGPQty, Qty, Unit, Rate,
                GradeNo, Grade, SystemIP, CreatedBy, CreatedOn,
                DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID,
                LinkNo
            )
            VALUES
            (
                @SlNo, @MTransNo, @PartNo,
                @PartCode, @PartDescription,
                LTRIM(RTRIM(@ItemRemarks)), @HSNCode,
                @RGPQty, @Qty, @Unit, ISNULL(@Rate, 0),
                @GradeNo, @Grade,
                @SystemIP, @CreatedBy, @CreatedOn,
                'N', NULL, ISNULL(@LockStatus, 'N'), 1, @SubscID,
                @LinkNo
            );

            SET @ID      = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END

        --=========================================
        -- UPDATE
        --=========================================
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.RGPInwDtl
               SET PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDescription,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   RGPQty          = @RGPQty,
                   Qty             = @Qty,
                   Unit            = @Unit,
                   Rate            = ISNULL(@Rate, 0),
                   GradeNo         = @GradeNo,
                   Grade           = @Grade,
                   LinkNo          = @LinkNo,
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = @ModifiedOn,
                   LockStatus      = ISNULL(@LockStatus, LockStatus),
                   LockCounter     = ISNULL(LockCounter, 0) + 1
             WHERE TransSlNo     = @TransSlNo
               AND MTransNo      = @MTransNo
               AND SubscID       = @SubscID
               AND DeleteStatus  = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE
        --=========================================
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.RGPInwDtl
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE TransSlNo    = @TransSlNo
               AND MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        --=========================================
        -- DELETE ALL
        --=========================================
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.RGPInwDtl
               SET DeleteStatus = 'Y',
                   DeleteReason = @DeleteReason,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = @ModifiedOn
             WHERE MTransNo     = @MTransNo
               AND SubscID      = @SubscID
               AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status.';
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO
