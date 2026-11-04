/*==============================================================================
  PURCHASE / INWARD / REPORTS - COMBINED DEPLOYMENT SCRIPT  (generated 2026-09-30)

  Everything written after 00_SalesDeploy_All.sql. Runs, in order:
      27_SalesReportsMenu.sql       Sales Reports menu 811-820 + grants
      28_DeliveryChallanRGP.sql     DC / RGP / RGP Inward: LinkNo column,
                                    UQ_RGPMst incl. RGPType, 5 SP_RGP* procedures
      29_GRNPage.sql                SP_GRNDtl - GST% taken from the PO line
      30_GRNInspectionPages.sql     SP_GRNInspPost / SP_GRNNCDtl, 'QC Verify By' type
      31_GRNStorePage.sql           VW_GRNStoreAllocation
      32_HeatNoPage.sql             SP_HeatNoMst / SP_HeatNoDtl, IX_HeatRefNo_SubscID_RefNo
      34_SalesApproval.sql          QtnMst / OAMst RejectReason, RejectedBy, RejectedOn
                                    + SP_QtnApproval / SP_OAApproval
      35_PurchaseReportsMenu.sql    Purchase Reports menu 721-728 + grants
      36_OALinkPurchaseLines.sql    OA Year / No / ID on PR + PO lines: PurReqDtl columns,
                                    FN_OALinkCheck, SP_PurReqDtl, SP_PODtl

  NOT included, on purpose:
      18_PurchaseOrderPage.sql      its @Folder still says 'Modules/Transactions/'
      33_StoresInwardMenu.sql       those eight pages are not finished yet

  ORDER OF DEPLOYMENT
      1. 00_SalesDeploy_All.sql     (19 - 26), if the server does not have it yet
      2. this script
      3. then the new EMSAPI build - it maps the columns added here, so an API
         deployed before step 2 fails every Quotation / Order Acceptance read.

  HOW TO RUN
    sqlcmd:  sqlcmd -S <server> -E -C -b -i 00_PurchaseDeploy_All.sql
    SSMS:    Query > SQLCMD Mode ON, then execute (needed for :on error exit).

  Every part is idempotent - safe to re-run. BEFORE RUNNING: take a backup, and
  check @Folder in steps 27 and 35 matches where the server serves the pages.
==============================================================================*/
:on error exit

USE EMSSoft;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  PRE-FLIGHT - these scripts build ON TOP of objects from scripts 13-15 and of
  the RGP tables, which were created directly in the dev database (no repo
  script exists for them). Stop before changing anything if they are missing.
------------------------------------------------------------------------------*/
SET NOCOUNT ON;
DECLARE @Missing NVARCHAR(MAX) = N'';

SELECT @Missing += N', ' + R.Name
  FROM (VALUES (N'U', N'Menulist'), (N'U', N'AccessTemplate'), (N'U', N'OtherMasterList'),
               (N'U', N'UserMaster'), (N'U', N'SupplierMaster'), (N'U', N'PartMaster'),
               (N'U', N'MaterialStd'), (N'U', N'QtnMst'), (N'U', N'OAMst'),
               (N'U', N'RGPMst'), (N'U', N'RGPDtl'), (N'U', N'RGPTerms'),
               (N'U', N'RGPInwMst'), (N'U', N'RGPInwDtl'),
               (N'U', N'POMst'), (N'U', N'PODtl'),
               (N'U', N'GRNMst'), (N'U', N'GRNDtl'), (N'U', N'GRNNCDtl'),
               (N'U', N'HeatNoMst'), (N'U', N'HeatNoDtl'), (N'U', N'HeatRefNo'),
               (N'U', N'StoreLocation'),
               (N'P', N'SP_GRNMstRecalc'), (N'P', N'SP_PODtlPrice')) AS R(Type, Name)
 WHERE OBJECT_ID(N'dbo.' + R.Name, R.Type) IS NULL;

IF LEN(@Missing) > 0
BEGIN
    DECLARE @Msg NVARCHAR(MAX) = N'Pre-flight failed - missing in EMSSoft: ' + STUFF(@Missing, 1, 2, N'')
        + N'. Run 13_PurchaseOrderGRNTables / 14 / 15 and create the RGP tables first. Nothing was changed.';
    RAISERROR(@Msg, 16, 1);
END
ELSE
    PRINT 'Pre-flight OK - prerequisites present.';
GO


PRINT '>>> 27_SalesReportsMenu.sql';
GO

/*==============================================================================
  27. SALES REPORTS MENU  -  sidebar section for EMSSoft/Modules/SalesReports/

  The seven report pages now read live data from SalesReportsController
  (GET api/{Report}/AllReports), and the three attachment viewers from
  GET api/SalesReports/Attachments/{Enq|Qtn|OA}/{subscID}. No stored
  procedures or tables are needed,
  only the menu rows and their AccessTemplate grants (a Menulist row alone is
  invisible, see 25_SalesMenuAndLookups.sql).

  The Series filter reads OtherMaster type 'DocSeries' (created by script 25);
  add the series values from the Other Master screen.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/SalesReports/'.
    Set it to wherever the deployed server serves them from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 811-820 (as it does 701-706 and 801-807).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  SIDEBAR SECTION  +  GRANTS

  MTransNo 811-820 are unused. SSLNo 7 puts Sales Reports after Sales, rows in
  the same process order as the Sales section, attachment viewers last.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/SalesReports/';   -- <- check, see header

DECLARE @Menu TABLE
(
    MTransNo    INT,
    Section     VARCHAR(50),
    SSLNo       INT,
    ModuleName  VARCHAR(50),
    MSLNo       INT,
    ModuleIcon  VARCHAR(50),
    ModulePage  VARCHAR(100)
);

INSERT INTO @Menu (MTransNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
VALUES
    (811, 'Sales Reports', 7, 'Lead Report',             1, 'bi-person-lines-fill',  @Folder + 'LeadReport.html'),
    (812, 'Sales Reports', 7, 'Enquiry Report',          2, 'bi-journal-text',       @Folder + 'EnquiryReport.html'),
    (813, 'Sales Reports', 7, 'Quotation Report',        3, 'bi-file-earmark-bar-graph', @Folder + 'QuotationReport.html'),
    (814, 'Sales Reports', 7, 'Order Acceptance Report', 4, 'bi-clipboard-data',     @Folder + 'OrderAcceptancePReport.html'),
    (815, 'Sales Reports', 7, 'Proforma Report',         5, 'bi-receipt',            @Folder + 'ProformaReport.html'),
    (816, 'Sales Reports', 7, 'Packing Slip Report',     6, 'bi-box-seam',           @Folder + 'PackingSlipReport.html'),
    (817, 'Sales Reports', 7, 'Sales Report',            7, 'bi-graph-up',           @Folder + 'SalesReport.html'),
    (818, 'Sales Reports', 7, 'Enquiry Attachments',     8, 'bi-paperclip',          @Folder + 'EnqAttachmentView.html'),
    (819, 'Sales Reports', 7, 'Quotation Attachments',   9, 'bi-paperclip',          @Folder + 'QtnAttachmentView.html'),
    (820, 'Sales Reports', 7, 'OA Attachments',         10, 'bi-paperclip',          @Folder + 'OAAttachmentView.html');

MERGE dbo.Menulist AS T
USING @Menu AS S
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

-- Same batch as the MERGE (no GO): @Menu would not survive a batch boundary.
-- Every template that already holds grants gets full access to the reports,
-- with Department copied from its existing rows.
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT  T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM  (
            SELECT TemplateName, SubscID, MAX(Department) AS Department
              FROM dbo.AccessTemplate
             WHERE TemplateName IS NOT NULL
             GROUP BY TemplateName, SubscID
        ) T
 CROSS JOIN (SELECT MTransNo FROM @Menu) M
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.AccessTemplate A
            WHERE A.TemplateName = T.TemplateName
              AND A.SubscID      = T.SubscID
              AND A.ModuleNo     = M.MTransNo);
GO

GO
PRINT '27_SalesReportsMenu.sql applied.';
GO

PRINT '>>> 28_DeliveryChallanRGP.sql';
GO

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

GO
PRINT '28_DeliveryChallanRGP.sql applied.';
GO

PRINT '>>> 29_GRNPage.sql';
GO

/*==============================================================================
  29. GRN PAGE  -  what the live Goods Receipt Note screen needs

  GRN.html has moved off its in-page mock data and onto GRNController
  (/api/GRN/...). Tables, views and procedures come from 13_ / 15_. The screen
  adds three endpoints in GRNController (no schema change):
      GET  Mst/GetNextDocNo/{subscID}   DocNo preview (SP_GRNMst allocates)
      GET  Mst/GetRegister/{subscID}    list incl. TxnCurrency / CurrencyRate
      POST Mst/SaveFull                 header + lines + drops + Lock, in ONE
                                        transaction (a refused line rolls the
                                        whole receipt back)

  The only database change:

    SP_GRNDtl - on a PO-backed line the GST% now comes from the PO line
                (PODtl.GSTP), like the rate, discount and packing already do,
                instead of the part master's current TaxP. A receipt is valued
                on the terms the order was placed on; the screen shows the PO
                line's GST% and checks Bill Value against that total. Direct
                lines (POTransSlNo 0) still use PartMaster.TaxP. Parameters,
                statuses, messages and the GRNType stage rule are unchanged.

  SP_* names are shadowed by master, so CREATE OR ALTER fails: create a stub
  if missing, then ALTER.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.SP_GRNDtl', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNDtl AS BEGIN SET NOCOUNT ON; END');
GO
ALTER PROCEDURE dbo.SP_GRNDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @POTransSlNo    INT,
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @BillQty        NUMERIC(12,4),
    @RecQty         NUMERIC(12,4),
    @Weight         NUMERIC(12,4),
    @Rate           NUMERIC(14,2),
    @DiscP          NUMERIC(6,2),
    @PnFP           NUMERIC(6,2),
    @FreightAmt     NUMERIC(14,2),
    @Grade          VARCHAR(50),
    @Store          VARCHAR(50),
    @Location       VARCHAR(50),
    @BarCode        VARCHAR(30),
    @ChassisNo      VARCHAR(30),
    @EngineNo       VARCHAR(30),
    @RegdNo         VARCHAR(30),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @TaxType      VARCHAR(20),
                @GRNType      CHAR(1),
                @GDocNo       INT,
                @GDocDate     DATE,
                @GVendorNo    INT,
                @GTargetType  VARCHAR(10),
                @GTargetNo    INT,
                @SlNo         NUMERIC(12,2),
                @POQty        NUMERIC(12,2),
                @Balance      NUMERIC(14,4),
                @AlreadyOnMe  NUMERIC(14,4),
                @PartCode     VARCHAR(30),
                @PartDesc     VARCHAR(100),
                @HSNCode      VARCHAR(10),
                @Unit         VARCHAR(20),
                @Unit2        VARCHAR(30),
                @ConvRatio    NUMERIC(14,4),
                @GSTP         NUMERIC(6,2),
                @GradeNo      INT,
                @OAYear       INT,
                @OANo         INT,
                @OAID         INT,
                @BasicValue   NUMERIC(16,2),
                @DiscAmt      NUMERIC(12,2),
                @BasicRate    NUMERIC(14,2),
                @PnFAmt       NUMERIC(14,2),
                @TaxableValue NUMERIC(14,2),
                @IGSTAmt      NUMERIC(14,2),
                @CGSTAmt      NUMERIC(14,2),
                @SGSTAmt      NUMERIC(14,2),
                @TotalValue   NUMERIC(16,2);

        SELECT @TaxType     = LTRIM(RTRIM(TaxType)),
               @GRNType     = GRNType,
               @GDocNo      = DocNo,
               @GDocDate    = DocDate,
               @GVendorNo   = VendorNo,
               @GTargetType = TargetType,
               @GTargetNo   = TargetTransNo
          FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @TaxType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @GRNType IN ('Q', 'C')
        BEGIN
            SET @ID = 0;
            SET @Message = 'This GRN has been inspected. Reopen the inspection before changing lines.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update: resolve the part, the PO line and the price, then
          check the quantity against what the order still has outstanding.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update')
        BEGIN
            IF ISNULL(@BillQty, 0) <= 0 AND ISNULL(@RecQty, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Enter a billed or a received quantity.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @BillQty = ISNULL(@BillQty, 0);
            SET @RecQty  = ISNULL(NULLIF(@RecQty, 0), @BillQty);

            SELECT @PartCode  = LEFT(LTRIM(RTRIM(ISNULL(P.PartCode, ''))), 30),
                   @PartDesc  = LEFT(LTRIM(RTRIM(ISNULL(P.PartName, ''))), 100),
                   @HSNCode   = P.HSNCode,
                   @Unit      = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos'),
                   @Unit2     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.SecondaryUnit, ''))), ''),
                                ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos')),
                   @ConvRatio = ISNULL(NULLIF(P.ConversionRatio, 0), 1),
                   @GSTP      = ISNULL(P.TaxP, 0)
              FROM dbo.PartMaster P
             WHERE P.MTransNo = @PartNo AND P.DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @GTargetType = 'PO' AND ISNULL(@POTransSlNo, 0) > 0
            BEGIN
                SELECT @SlNo      = PD.SlNo,
                       @POQty     = PD.Qty,
                       @Rate      = PD.Rate,
                       @DiscP     = PD.DiscP,
                       @PnFP      = PD.PnFP,
                       @Grade     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@Grade, ''))), ''), PD.Grade),
                       -- GST as ordered (29_GRNPage.sql), falling back to the part master.
                       @GSTP      = ISNULL(PD.GSTP, @GSTP),
                       @OAYear    = PD.OAYear,
                       @OANo      = PD.OANo,
                       @OAID      = PD.OAID
                  FROM dbo.PODtl PD
                 WHERE PD.TransSlNo = @POTransSlNo AND PD.MTransNo = @GTargetNo
                   AND PD.SubscID = @SubscID AND PD.DeleteStatus = 'N'
                   AND PD.PartNo = @PartNo;

                IF @SlNo IS NULL
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'That purchase order line was not found, or it is for a different part.';
                    COMMIT TRANSACTION; RETURN;
                END

                /*  Outstanding on the PO line = ordered, minus everything
                    already billed on it, EXCLUDING this line when it is being
                    edited rather than added.  */
                SELECT @AlreadyOnMe = ISNULL(SUM(GD.BillQty), 0)
                  FROM dbo.GRNDtl GD
                  JOIN dbo.GRNMst GM ON GM.MTransNo = GD.MTransNo
                 WHERE GD.DeleteStatus = 'N' AND GM.DeleteStatus = 'N'
                   AND GM.TargetType = 'PO' AND GM.TargetTransNo = @GTargetNo
                   AND GD.SlNo = @SlNo
                   AND (@Status = 'Insert' OR GD.TransSlNo <> @TransSlNo);

                SET @Balance = @POQty - @AlreadyOnMe;

                IF @BillQty > @Balance
                BEGIN
                    SET @ID = 0;
                    SET @Message = 'Billed quantity ' + CONVERT(VARCHAR(20), @BillQty) +
                                   ' exceeds the balance of ' + CONVERT(VARCHAR(20), @Balance) +
                                   ' on purchase order line ' + CONVERT(VARCHAR(20), @SlNo) + '.';
                    COMMIT TRANSACTION; RETURN;
                END
            END
            ELSE IF @Status = 'Insert'
            BEGIN
                SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
                  FROM dbo.GRNDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';
                SET @POQty = 0;
            END
            ELSE
            BEGIN
                SELECT @SlNo = SlNo, @POQty = POQty
                  FROM dbo.GRNDtl WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo;
            END

            SELECT @GradeNo = MTransNo
              FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

            /*  Value follows the BILLED quantity - that is what the supplier
                is owed for.  What was physically received, and what survives
                inspection, are tracked separately and settle later.  */
            EXEC dbo.SP_PODtlPrice
                 @BillQty, @Rate, @DiscP, @PnFP, @FreightAmt, @GSTP, @TaxType,
                 @BasicValue   OUTPUT, @DiscAmt      OUTPUT, @BasicRate OUTPUT,
                 @PnFAmt       OUTPUT, @TaxableValue OUTPUT, @IGSTAmt   OUTPUT,
                 @CGSTAmt      OUTPUT, @SGSTAmt      OUTPUT, @TotalValue OUTPUT;
        END

        IF (@Status = 'Insert')
        BEGIN
            INSERT INTO dbo.GRNDtl
            (DocNo, VendorNo, DocDate, SlNo, MTransNo, PartNo, PartCode, PartDescription,
             ItemRemarks, HSNCode, BillQty, RecQty, AccQty, RejQty, RewQty, AODQty,
             Weight, Unit, Unit2, ConvQty, RateType, Rate, BasicValue, DiscP, DiscAmt,
             PnFP, PnFAmt, FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt,
             TotalValue, BarCode, ScanStatus, POQty, ChassisNo, EngineNo, RegdNo,
             Store, Location, OAYear, OANo, OAID, GradeNo, Grade,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@GDocNo, @GVendorNo, @GDocDate, @SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc,
             LTRIM(RTRIM(@ItemRemarks)), @HSNCode, @BillQty, @RecQty, 0, 0, 0, 0,
             ISNULL(@Weight, 0), @Unit, @Unit2, ROUND(@RecQty * @ConvRatio, 4), 'Basic',
             @Rate, @BasicValue, ISNULL(@DiscP, 0), @DiscAmt,
             ISNULL(@PnFP, 0), @PnFAmt, ISNULL(@FreightAmt, 0), @TaxableValue, @GSTP,
             @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
             LTRIM(RTRIM(@BarCode)), 'N', ISNULL(@POQty, 0),
             LTRIM(RTRIM(@ChassisNo)), LTRIM(RTRIM(@EngineNo)), LTRIM(RTRIM(@RegdNo)),
             NULLIF(LTRIM(RTRIM(ISNULL(@Store, ''))), ''),
             NULLIF(LTRIM(RTRIM(ISNULL(@Location, ''))), ''),
             @OAYear, @OANo, @OAID, @GradeNo, LTRIM(RTRIM(@Grade)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Receipt line saved successfully.';
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNDtl
               SET PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDesc,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   BillQty         = @BillQty,
                   RecQty          = @RecQty,
                   Weight          = ISNULL(@Weight, 0),
                   Unit            = @Unit,
                   Unit2           = @Unit2,
                   ConvQty         = ROUND(@RecQty * @ConvRatio, 4),
                   Rate            = @Rate,
                   BasicValue      = @BasicValue,
                   DiscP           = ISNULL(@DiscP, 0),
                   DiscAmt         = @DiscAmt,
                   PnFP            = ISNULL(@PnFP, 0),
                   PnFAmt          = @PnFAmt,
                   FreightAmt      = ISNULL(@FreightAmt, 0),
                   TaxableValue    = @TaxableValue,
                   GSTP            = @GSTP,
                   IGSTAmt         = @IGSTAmt,
                   CGSTAmt         = @CGSTAmt,
                   SGSTAmt         = @SGSTAmt,
                   TotalValue      = @TotalValue,
                   BarCode         = LTRIM(RTRIM(@BarCode)),
                   POQty           = ISNULL(@POQty, 0),
                   ChassisNo       = LTRIM(RTRIM(@ChassisNo)),
                   EngineNo        = LTRIM(RTRIM(@EngineNo)),
                   RegdNo          = LTRIM(RTRIM(@RegdNo)),
                   GradeNo         = @GradeNo,
                   Grade           = LTRIM(RTRIM(@Grade)),
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Receipt line updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.GRNDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                -- The NC lines hanging off this receipt line go with it.
                UPDATE dbo.GRNNCDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE GRNTransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @TransSlNo; SET @Message = 'Receipt line deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.GRNDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.GRNNCDtl
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'All receipt lines deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active receipt lines found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        IF @ID > 0 EXEC dbo.SP_GRNMstRecalc @MTransNo, @SubscID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

GO
PRINT '29_GRNPage.sql applied.';
GO

PRINT '>>> 30_GRNInspectionPages.sql';
GO

/*==============================================================================
  30. GRN INSPECTION PAGES  -  what GRNQC.html and GRNNCClosure.html need
      beyond 15_GRNProcedures.sql

  Run after 13_PurchaseOrderGRNTables.sql and 15_GRNProcedures.sql.
  Idempotent - safe to re-run.

  ----------------------------------------------------------------------------
  WHAT CHANGES
  ----------------------------------------------------------------------------
  1. OtherMasterList gets the 'QC Verify By' master type, per subscription,
     so the inspection form's QC Verify By dropdown can be maintained from
     Other Master (the legacy screen read the same master type).

  2. SP_GRNInspPost
       * Reopen no longer clears InspNo.  Editing a posted inspection is
         Reopen -> rewrite -> Post; clearing the number meant the edit came
         back with a NEW number (MAX + 1) whenever another GRN had been
         inspected in the meantime.
       * Post re-uses the GRN's InspNo when it already holds one, and only
         allocates MAX + 1 for a GRN that has never been inspected.
     A withdrawn inspection therefore keeps its number too, as the legacy
     application did (RecDeleteQC left InspNo alone).

  3. SP_GRNNCDtl
       * A heat-traced part (PartMaster.MfrLicNo = 'Y', the flag the legacy
         application used for "heat no required") may only be rejected /
         reworked / accepted-on-deviation against a heat number that was
         recorded for that part and grade on THIS GRN (HeatNoMst.TargetTransNo).
         This is the legacy CheckHeatNo rule, moved into the procedure so it
         holds however the endpoint is called.

  SP_GRNInspDtl and SP_GRNNCClose are unchanged.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. 'QC Verify By' master type
------------------------------------------------------------------------------*/
INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  'QC Verify By', 'Admin', 'N', S.SubscID
  FROM  (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE  NOT EXISTS (
            SELECT 1 FROM dbo.OtherMasterList L
             WHERE L.MasterType   = 'QC Verify By'
               AND L.SubscID      = S.SubscID
               AND L.DeleteStatus = 'N');
GO


/*------------------------------------------------------------------------------
  2. SP_GRNInspPost  -  post the inward inspection

  @Status
      Post     InspNo (kept, or next in the series), QCDate, QCverifyBy,
               GRNType = 'Q'
      Reopen   back to GRNType = 'N'; QCDate and QCverifyBy cleared,
               InspNo KEPT so a re-post carries the same number

  Post checks the whole GRN:
      * every line inspected  (Acc + Rej + Rew + AOD = RecQty)
      * every non-zero Rej / Rew / AOD quantity backed by NC lines of the
        matching type whose NCQty adds up to exactly that quantity
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNInspPost', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNInspPost AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNInspPost
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @QCDate         DATE,
    @QCverifyBy     VARCHAR(50),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @GRNType   CHAR(1),
                @DocSeries CHAR(10),
                @DocYear   INT,
                @CurInsp   VARCHAR(50),
                @NextInsp  INT,
                @BadLine   NUMERIC(12,2),
                @BadType   VARCHAR(10),
                @BadQty    NUMERIC(14,4),
                @BadNC     NUMERIC(14,4);

        SELECT @GRNType = GRNType, @DocSeries = DocSeries, @DocYear = DocYear,
               @CurInsp = NULLIF(LTRIM(RTRIM(ISNULL(InspNo, ''))), '')
          FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'GRN not found.';
            COMMIT TRANSACTION; RETURN;
        END

        /*==================================================================
          POST
        ==================================================================*/
        IF (@Status = 'Post')
        BEGIN
            IF @GRNType IN ('Q', 'C')
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has already been inspected.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has no lines to inspect.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Everything received has to be accounted for.
            SELECT TOP 1 @BadLine = SlNo
              FROM dbo.GRNDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND AccQty + RejQty + RewQty + AODQty <> RecQty
             ORDER BY SlNo;

            IF @BadLine IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Line ' + CONVERT(VARCHAR(20), @BadLine) +
                               ' has not been fully inspected. Accepted + rejected + rework + AOD must equal the received quantity.';
                COMMIT TRANSACTION; RETURN;
            END

            /*  Every non-conforming quantity must be explained by NC lines of
                the matching type, and they must add up.  */
            SELECT TOP 1 @BadLine = X.SlNo, @BadType = X.NCType,
                         @BadQty = X.Qty, @BadNC = X.NCQty
              FROM (
                    SELECT D.SlNo, 'Reject' AS NCType, D.RejQty AS Qty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'Reject'
                                      AND N.DeleteStatus = 'N'), 0) AS NCQty
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.RejQty > 0
                    UNION ALL
                    SELECT D.SlNo, 'Rework', D.RewQty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'Rework'
                                      AND N.DeleteStatus = 'N'), 0)
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.RewQty > 0
                    UNION ALL
                    SELECT D.SlNo, 'AOD', D.AODQty,
                           ISNULL((SELECT SUM(N.NCQty) FROM dbo.GRNNCDtl N
                                    WHERE N.GRNTransSlNo = D.TransSlNo AND N.NCType = 'AOD'
                                      AND N.DeleteStatus = 'N'), 0)
                      FROM dbo.GRNDtl D
                     WHERE D.MTransNo = @MTransNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N'
                       AND D.AODQty > 0
                   ) X
             WHERE X.Qty <> X.NCQty
             ORDER BY X.SlNo, X.NCType;

            IF @BadLine IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Line ' + CONVERT(VARCHAR(20), @BadLine) + ': ' + @BadType +
                               ' quantity is ' + CONVERT(VARCHAR(20), @BadQty) +
                               ' but the non-conformance detail adds up to ' + CONVERT(VARCHAR(20), @BadNC) + '.';
                COMMIT TRANSACTION; RETURN;
            END

            /*  A GRN that was inspected before (then reopened) keeps its
                number.  Otherwise the next number in the series; TRY_CONVERT
                keeps a hand-typed, non-numeric legacy value from breaking MAX.  */
            IF @CurInsp IS NULL
            BEGIN
                SELECT @NextInsp = ISNULL(MAX(TRY_CONVERT(INT, InspNo)), 0) + 1
                  FROM dbo.GRNMst
                 WHERE DocSeries = @DocSeries AND DocYear = @DocYear
                   AND SubscID = @SubscID AND DeleteStatus = 'N'
                   AND InspNo IS NOT NULL;

                SET @CurInsp = CONVERT(VARCHAR(50), @NextInsp);
            END

            UPDATE dbo.GRNMst
               SET InspNo     = @CurInsp,
                   QCDate     = ISNULL(@QCDate, CONVERT(DATE, GETDATE())),
                   QCverifyBy = LTRIM(RTRIM(@QCverifyBy)),
                   GRNType    = 'Q',
                   SystemIP   = @SystemIP,
                   ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo;
            SET @Message = 'Inspection posted. Inspection No. ' + @CurInsp + '.';
        END

        /*==================================================================
          REOPEN
        ==================================================================*/
        ELSE IF (@Status = 'Reopen')
        BEGIN
            IF @GRNType = 'N'
            BEGIN
                SET @ID = 0; SET @Message = 'This GRN has not been inspected yet.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @GRNType = 'C'
            BEGIN
                SET @ID = 0;
                SET @Message = 'The non-conformance has been closed. Reopen the closure first.';
                COMMIT TRANSACTION; RETURN;
            END

            -- InspNo is deliberately kept: see the header of this script.
            UPDATE dbo.GRNMst
               SET GRNType    = 'N',
                   QCDate     = NULL,
                   QCverifyBy = NULL,
                   SystemIP   = @SystemIP,
                   ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            SET @ID = @MTransNo; SET @Message = 'Inspection reopened.';
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
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
  3. SP_GRNNCDtl  -  non-conformance lines raised during inspection

  @Status:  Insert / Update / Delete / DeleteAll

  One row per defect found on a receipt line.  NCType is 'Reject', 'Rework' or
  'AOD'; the part, grade and unit are copied from the receipt line rather than
  taken from the caller, so an NC cannot describe a part that was not received.

  Total NCQty on a line may not exceed what was received on it.

  A heat-traced part (PartMaster.MfrLicNo = 'Y') needs a heat number that was
  recorded against this GRN, for that part and grade, on the Heat No. screen.
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_GRNNCDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_GRNNCDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_GRNNCDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @GRNTransSlNo   INT,
    @NCType         VARCHAR(10),
    @HeatNo         VARCHAR(10),
    @NCRemarks      VARCHAR(500),
    @NCQty          NUMERIC(12,4),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @MTransNo INT, @PartNo INT, @GradeNo INT, @Grade VARCHAR(50),
                @Unit VARCHAR(20), @RecQty NUMERIC(12,4), @GRNType CHAR(1),
                @Existing NUMERIC(14,4), @SlNo NUMERIC(12,2);

        SET @NCType = LTRIM(RTRIM(ISNULL(@NCType, '')));
        SET @HeatNo = LEFT(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), 10);

        IF (@Status = 'DeleteAll')
        BEGIN
            /*  DeleteAll works on the whole GRN, addressed by @GRNTransSlNo
                carrying the GRN's MTransNo - so guard it separately.  */
            UPDATE dbo.GRNNCDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @GRNTransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @GRNTransSlNo; SET @Message = 'All non-conformance lines deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active non-conformance lines found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Delete')
        BEGIN
            SELECT @MTransNo = MTransNo FROM dbo.GRNNCDtl
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @MTransNo IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GRNType = GRNType FROM dbo.GRNMst WHERE MTransNo = @MTransNo;
            IF @GRNType IN ('Q', 'C')
            BEGIN
                SET @ID = 0;
                SET @Message = 'The inspection has been posted. Reopen it before changing non-conformance lines.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.GRNNCDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            SET @ID = @TransSlNo; SET @Message = 'Non-conformance line deleted successfully.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update
        ------------------------------------------------------------------*/
        IF @NCType NOT IN ('Reject', 'Rework', 'AOD')
        BEGIN
            SET @ID = 0; SET @Message = 'NC Type must be Reject, Rework or AOD.';
            COMMIT TRANSACTION; RETURN;
        END

        IF ISNULL(@NCQty, 0) <= 0
        BEGIN
            SET @ID = 0; SET @Message = 'NC quantity must be greater than zero.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @MTransNo = D.MTransNo, @PartNo = D.PartNo, @GradeNo = D.GradeNo,
               @Grade = D.Grade, @Unit = D.Unit, @RecQty = D.RecQty
          FROM dbo.GRNDtl D
         WHERE D.TransSlNo = @GRNTransSlNo AND D.SubscID = @SubscID AND D.DeleteStatus = 'N';

        IF @MTransNo IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Receipt line not found.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @GRNType = GRNType FROM dbo.GRNMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @GRNType IN ('Q', 'C')
        BEGIN
            SET @ID = 0;
            SET @Message = 'The inspection has been posted. Reopen it before adding non-conformance lines.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @Existing = ISNULL(SUM(NCQty), 0)
          FROM dbo.GRNNCDtl
         WHERE GRNTransSlNo = @GRNTransSlNo AND SubscID = @SubscID AND DeleteStatus = 'N'
           AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo);

        IF @Existing + @NCQty > @RecQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Non-conformance of ' + CONVERT(VARCHAR(20), @Existing + @NCQty) +
                           ' exceeds the received quantity of ' + CONVERT(VARCHAR(20), @RecQty) + '.';
            COMMIT TRANSACTION; RETURN;
        END

        /*  Heat-traced part: the heat number must have come in on this GRN.  */
        IF EXISTS (SELECT 1 FROM dbo.PartMaster P
                    WHERE P.MTransNo = @PartNo
                      AND UPPER(LTRIM(RTRIM(ISNULL(P.MfrLicNo, '')))) = 'Y')
        BEGIN
            IF @HeatNo = ''
            BEGIN
                SET @ID = 0; SET @Message = 'This part is heat traced. Enter the heat number.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1
                             FROM dbo.HeatNoDtl HD
                             JOIN dbo.HeatNoMst HM ON HM.MTransNo = HD.MTransNo
                            WHERE HM.TargetTransNo = @MTransNo
                              AND HM.SubscID       = @SubscID
                              AND HM.DeleteStatus  = 'N'
                              AND HD.DeleteStatus  = 'N'
                              AND HD.PartNo        = @PartNo
                              AND LTRIM(RTRIM(ISNULL(HD.Grade, ''))) = LTRIM(RTRIM(ISNULL(@Grade, '')))
                              AND HD.HeatNo        = @HeatNo)
            BEGIN
                SET @ID = 0;
                SET @Message = 'Heat number ' + @HeatNo + ' was not received on this GRN for this part.';
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.GRNNCDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.GRNNCDtl
            (SlNo, MTransNo, PartNo, GradeNo, Grade, NCType, HeatNo, NCRemarks,
             NCQty, Unit, GRNTransSlNo, SystemIP, CreatedBy, CreatedOn,
             DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @GradeNo, @Grade, @NCType,
             @HeatNo, LTRIM(RTRIM(@NCRemarks)),
             @NCQty, @Unit, @GRNTransSlNo, @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
             'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Non-conformance line saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.GRNNCDtl
               SET NCType = @NCType,
                   HeatNo = @HeatNo,
                   NCRemarks = LTRIM(RTRIM(@NCRemarks)),
                   NCQty = @NCQty,
                   PartNo = @PartNo, GradeNo = @GradeNo, Grade = @Grade, Unit = @Unit,
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND GRNTransSlNo = @GRNTransSlNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Non-conformance line updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

-- What was written.
SELECT MTransNo, MasterType, Department, SubscID
  FROM dbo.OtherMasterList
 WHERE MasterType = 'QC Verify By' AND DeleteStatus = 'N';

SELECT name, modify_date
  FROM sys.procedures
 WHERE name IN ('SP_GRNInspPost', 'SP_GRNNCDtl');
GO

GO
PRINT '30_GRNInspectionPages.sql applied.';
GO

PRINT '>>> 31_GRNStorePage.sql';
GO

/*==========================================================================
  31_GRNStorePage.sql — backing for EMSSoft/Modules/Purchase/GRNStore.html
  --------------------------------------------------------------------------
  Idempotent: safe to run again.

  VW_GRNStoreAllocation — one row per GRN, counted over its ACCEPTED lines
  only (AccQty > 0). A line inspection rejected in full has nothing to put
  away, so it never keeps a GRN on the "awaiting allocation" list — which is
  why VW_GRNInwardStatus.UnallocatedLines (every live line) is not used here.
  That view is read by other pages and is left untouched.

      LineCount       accepted lines
      AllocatedCount  accepted lines with a Store
      PendingCount    accepted lines still without one
      AllocatedBy/On  who touched the most recently allocated accepted line
                      last — the page's "only the last allocator may edit"
                      rule reads it (GRNDtl carries no dedicated column)

  The page shows only inspected receipts (GRNType 'Q' / 'C'): AccQty stays 0
  until SP_GRNInspDtl records the verdict, so before inspection there is no
  accepted quantity to allocate. SP_GRNStoreAlloc itself stays ungated.

  No change to SP_GRNStoreAlloc / SP_StoreLocation is required.
==========================================================================*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER VIEW dbo.VW_GRNStoreAllocation
AS
SELECT  GM.MTransNo,
        GM.DocSeries,
        GM.DocYear,
        GM.DocNo,
        GM.DocDate,
        GM.VendorNo,
        GM.VendorName,
        GM.GRNType,
        GM.InspNo,
        GM.QCDate,
        GM.QCverifyBy,
        GM.LockStatus,
        GM.SubscID,
        ISNULL(A.LineCount, 0)                                AS LineCount,
        ISNULL(A.AllocatedCount, 0)                           AS AllocatedCount,
        ISNULL(A.LineCount, 0) - ISNULL(A.AllocatedCount, 0)  AS PendingCount,
        LA.ModifiedBy                                         AS AllocatedBy,
        LA.ModifiedOn                                         AS AllocatedOn
  FROM  dbo.GRNMst GM
 OUTER APPLY
       (
           SELECT COUNT(*) AS LineCount,
                  SUM(CASE WHEN NULLIF(LTRIM(RTRIM(GD.Store)), '') IS NULL THEN 0 ELSE 1 END)
                           AS AllocatedCount
             FROM dbo.GRNDtl GD
            WHERE GD.MTransNo = GM.MTransNo AND GD.DeleteStatus = 'N'
              AND GD.AccQty > 0
       ) A
 OUTER APPLY
       (
           SELECT TOP (1) GD.ModifiedBy, GD.ModifiedOn
             FROM dbo.GRNDtl GD
            WHERE GD.MTransNo = GM.MTransNo AND GD.DeleteStatus = 'N'
              AND GD.AccQty > 0
              AND NULLIF(LTRIM(RTRIM(GD.Store)), '') IS NOT NULL
            ORDER BY GD.ModifiedOn DESC, GD.TransSlNo DESC
       ) LA
 WHERE  GM.DeleteStatus = 'N';
GO

GO
PRINT '31_GRNStorePage.sql applied.';
GO

PRINT '>>> 32_HeatNoPage.sql';
GO

/*==============================================================================
  32. HEAT NO PAGE  -  what the live Heat No. Entry screen needs

  HeatNo.html has moved off its in-page mock data and onto HeatNoController
  (/api/HeatNo/...). Tables HeatNoMst / HeatNoDtl / HeatRefNo already exist
  (13_), and HeatNoDtl.TCFile already holds the test-certificate path, so there
  is NO NEW COLUMN. What changes:

    1. SP_HeatNoMst  - the page's three-state workflow
         LockStatus  N = draft (an EXECUTIVE's own)
                     S = submitted for approval
                     Y = approved              (DocStatus 'A')
       * Insert / Update take N or S. Y is only ever set by 'Approve' / 'Lock'.
         Updating an approved document therefore re-submits it (S) - the page
         re-approves it after the heat lines have been rewritten, because
         SP_HeatNoDtl refuses to touch the lines of an approved document.
       * New Status 'Approve': S -> Y only, needs at least one heat line, and
         an EXECUTIVE user may not approve.
       * 'Unlock' now returns an approved document to S (submitted), not N.
       * An EXECUTIVE may not change or delete an approved document.
       * One heat document per GRN + part (the page never offers a pair that
         already has one; the procedure now refuses it too).
       * The document quantity may not exceed what was received of that part
         on the GRN.
    2. SP_HeatNoDtl
       * A piece reference range may not overlap a range already issued on
         another live heat document (same subscription).
       * New Status 'TCFile': records the uploaded test certificate's path on
         one heat line (the file itself is written by the API under
         wwwroot/Uploads/HeatTC/{MTransNo}/).
       Everything else is unchanged: heat qty <= document qty, one heat number
       per part, 10,000-piece range limit, ranges rewritten whole.
    3. Index on HeatRefNo (SubscID, DeleteStatus, RefNo) for the "last
       reference number issued" preview and the overlap check.

  The procedures are shadowed by master's SP_ prefix, so CREATE OR ALTER
  fails: each is created as a stub if missing, then ALTERed with its full body.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SP_HeatNoMst
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoMst', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoMst AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_HeatNoMst
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @MCount         INT,
    @DocSeries      CHAR(10),
    @DocYear        INT,
    @DocNo          INT,
    @DocDate        DATE,
    @TargetTransNo  INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @Qty            NUMERIC(12,6),
    @DocNotes       VARCHAR(250),
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

        DECLARE @TargetDocYear INT, @TargetDocNo INT,
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250),
                @Unit VARCHAR(20), @GradeNo INT, @DtlQty NUMERIC(14,4),
                @RecQty NUMERIC(14,4), @CurLock CHAR(1), @UserType VARCHAR(50),
                @OtherDocNo INT;

        -- Who is acting: an EXECUTIVE saves drafts, never approves, and never
        -- touches an approved document.
        SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
          FROM dbo.UserMaster
         WHERE MTransNo = ISNULL(@ModifiedBy, @CreatedBy);

        -- Draft or submitted only; approval has its own status.
        SET @LockStatus = CASE WHEN @LockStatus = 'N' THEN 'N' ELSE 'S' END;

        IF @Status IN ('Update', 'Delete', 'Lock', 'Unlock', 'Approve')
            SELECT @CurLock = LockStatus FROM dbo.HeatNoMst
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @Status IN ('Update', 'Delete') AND @CurLock = 'Y' AND @UserType = 'EXECUTIVE'
        BEGIN
            SET @ID = 0;
            SET @Message = 'This heat no entry is approved. Only a non-executive user can change or delete it.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @TargetDocYear = DocYear, @TargetDocNo = DocNo
              FROM dbo.GRNMst
             WHERE MTransNo = @TargetTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @TargetDocYear IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'GRN not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.GRNDtl
                            WHERE MTransNo = @TargetTransNo AND PartNo = @PartNo
                              AND SubscID = @SubscID AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0;
                SET @Message = 'That part was not received on this GRN.';
                COMMIT TRANSACTION; RETURN;
            END

            -- The heats describe what arrived, so the document cannot cover
            -- more than the GRN received of that part.
            SELECT @RecQty = ISNULL(SUM(RecQty), 0) FROM dbo.GRNDtl
             WHERE MTransNo = @TargetTransNo AND PartNo = @PartNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF ISNULL(@Qty, 0) > @RecQty
            BEGIN
                SET @ID = 0;
                SET @Message = 'Quantity ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, ISNULL(@Qty, 0))) +
                               ' is more than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @RecQty)) +
                               ' received of this part on the GRN.';
                COMMIT TRANSACTION; RETURN;
            END

            -- One heat document per GRN + part.
            SELECT TOP 1 @OtherDocNo = DocNo FROM dbo.HeatNoMst
             WHERE TargetTransNo = @TargetTransNo AND PartNo = @PartNo
               AND SubscID = @SubscID AND DeleteStatus = 'N'
               AND (@Status = 'Insert' OR MTransNo <> @MTransNo);

            IF @OtherDocNo IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'This GRN and part already have a Heat No entry (Sl No ' +
                               CONVERT(VARCHAR(20), @OtherDocNo) + ').';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
                   @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
                   @Unit     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos')
              FROM dbo.PartMaster
             WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @DocNo = ISNULL(MAX(DocNo), 0) + 1
              FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND SubscID = @SubscID;

            SELECT @MCount = COUNT(MTransNo) FROM dbo.HeatNoMst
             WHERE DocSeries = @DocSeries AND DocYear = @DocYear AND DocNo = @DocNo
               AND DeleteStatus = 'N' AND SubscID = @SubscID;

            IF (@MCount > 0)
            BEGIN SET @ID = 0; SET @Message = 'Heat document already exists.'; END
            ELSE
            BEGIN
                INSERT INTO dbo.HeatNoMst
                (DocSeries, DocYear, DocNo, DocDate, TargetTransNo, TargetDocYear, TargetDocNo,
                 PartNo, PartCode, PartDescription, GradeNo, Grade, Qty, Unit,
                 DocNotes, DocStatus, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID)
                VALUES
                (LTRIM(RTRIM(@DocSeries)), @DocYear, @DocNo, @DocDate,
                 @TargetTransNo, @TargetDocYear, @TargetDocNo,
                 @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
                 ISNULL(@Qty, 0), @Unit, LTRIM(RTRIM(@DocNotes)), 'N',
                 @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()),
                 'N', NULL, @LockStatus, 1, @SubscID);

                SET @ID = CONVERT(INT, SCOPE_IDENTITY());
                SET @Message = 'Heat document saved successfully.';
            END
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            /*  The header quantity is what the heat lines have to add up to,
                so it cannot be pulled below what has already been entered.  */
            SELECT @DtlQty = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF ISNULL(@Qty, 0) < @DtlQty
            BEGIN
                SET @ID = 0;
                SET @Message = 'Quantity cannot be less than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @DtlQty)) +
                               ' already entered against heat numbers.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Saving an edit (re)submits it: an approved document goes back
            -- to S so its heat lines can be rewritten, and is re-approved.
            UPDATE dbo.HeatNoMst
               SET DocDate = @DocDate, TargetTransNo = @TargetTransNo,
                   TargetDocYear = @TargetDocYear, TargetDocNo = @TargetDocNo,
                   PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   Qty = ISNULL(@Qty, 0), Unit = @Unit,
                   DocNotes = LTRIM(RTRIM(@DocNotes)),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockStatus = @LockStatus,
                   DocStatus = 'N',
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'Heat document updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoMst
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatNoDtl SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';
                UPDATE dbo.HeatRefNo SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                                         ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'Heat document deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status IN ('Lock', 'Unlock', 'Approve'))
        BEGIN
            IF @CurLock IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status IN ('Lock', 'Approve') AND @UserType = 'EXECUTIVE'
            BEGIN
                SET @ID = 0; SET @Message = 'An executive user cannot approve a heat no entry.';
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status = 'Approve' AND @CurLock <> 'S'
            BEGIN
                SET @ID = 0;
                SET @Message = CASE WHEN @CurLock = 'Y'
                                    THEN 'This heat no entry is already approved.'
                                    ELSE 'Only a submitted heat no entry can be approved. Submit the draft first.' END;
                COMMIT TRANSACTION; RETURN;
            END

            IF @Status IN ('Lock', 'Approve') AND NOT EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                                                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID
                                                   AND DeleteStatus = 'N')
            BEGIN
                SET @ID = 0; SET @Message = 'Enter at least one heat number before approving.';
                COMMIT TRANSACTION; RETURN;
            END

            -- Unlocking an approved document puts it back up for approval.
            UPDATE dbo.HeatNoMst
               SET LockStatus = CASE WHEN @Status = 'Unlock' THEN 'S' ELSE 'Y' END,
                   DocStatus  = CASE WHEN @Status = 'Unlock' THEN 'N' ELSE 'A' END,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE()),
                   LockCounter = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN
                SET @ID = @MTransNo;
                SET @Message = 'Heat document ' + CASE WHEN @Status = 'Unlock' THEN 'unlocked' ELSE 'approved' END + ' successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
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
  2. SP_HeatNoDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_HeatNoDtl', 'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_HeatNoDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_HeatNoDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @PartNo         INT,
    @Grade          VARCHAR(50),
    @HeatNo         VARCHAR(50),
    @TCNo           VARCHAR(30),
    @TCDate         DATE,
    @TCFile         VARCHAR(100),
    @Qty            NUMERIC(12,4),
    @ItemRemarks    VARCHAR(500),
    @RefFrom        INT,
    @RefTo          INT,
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @HdrQty NUMERIC(12,6), @HdrLocked CHAR(1), @Other NUMERIC(14,4),
                @PartCode VARCHAR(50), @PartDesc VARCHAR(250), @Unit VARCHAR(10),
                @GradeNo INT, @SlNo NUMERIC(12,2), @NewID INT, @i INT,
                @ClashFrom INT, @ClashTo INT, @ClashHeat VARCHAR(50);

        SELECT @HdrQty = Qty, @HdrLocked = LockStatus
          FROM dbo.HeatNoMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @HdrQty IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @HdrLocked = 'Y'
        BEGIN
            SET @ID = 0; SET @Message = 'Heat document is approved. Unlock it before changing heat numbers.';
            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @MTransNo; SET @Message = 'All heat numbers deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active heat numbers found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN
                UPDATE dbo.HeatRefNo
                   SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                       ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
                 WHERE HTransSlNo = @TransSlNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

                SET @ID = @TransSlNo; SET @Message = 'Heat number deleted successfully.';
            END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          TCFile: the API has stored the uploaded certificate; record where.
        ------------------------------------------------------------------*/
        IF (@Status = 'TCFile')
        BEGIN
            SET @TCFile = NULLIF(LTRIM(RTRIM(ISNULL(@TCFile, ''))), '');

            IF @TCFile IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'No test certificate file given.';
                COMMIT TRANSACTION; RETURN;
            END

            UPDATE dbo.HeatNoDtl
               SET TCFile = @TCFile, SystemIP = @SystemIP,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Test certificate uploaded successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Heat line not found.'; END

            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Insert / Update
        ------------------------------------------------------------------*/
        SET @HeatNo = NULLIF(LTRIM(RTRIM(ISNULL(@HeatNo, ''))), '');

        IF @HeatNo IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Heat number is required.';
            COMMIT TRANSACTION; RETURN;
        END

        IF ISNULL(@Qty, 0) <= 0
        BEGIN
            SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @PartCode = LEFT(LTRIM(RTRIM(ISNULL(PartCode, ''))), 50),
               @PartDesc = LEFT(LTRIM(RTRIM(ISNULL(PartName, ''))), 250),
               @Unit     = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(PrimaryUnit, ''))), ''), 'Nos'), 10)
          FROM dbo.PartMaster
         WHERE MTransNo = @PartNo AND DeleteStatus = 'N';

        IF @PartCode IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Part not found.';
            COMMIT TRANSACTION; RETURN;
        END

        -- The same heat number cannot describe the same part twice.
        IF EXISTS (SELECT 1 FROM dbo.HeatNoDtl
                    WHERE SubscID = @SubscID AND DeleteStatus = 'N'
                      AND HeatNo = @HeatNo AND PartNo = @PartNo
                      AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo))
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat number ' + @HeatNo + ' has already been recorded for this part.';
            COMMIT TRANSACTION; RETURN;
        END

        SELECT @Other = ISNULL(SUM(Qty), 0) FROM dbo.HeatNoDtl
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N'
           AND (@Status = 'Insert' OR TransSlNo <> @TransSlNo);

        IF @Other + @Qty > @HdrQty
        BEGIN
            SET @ID = 0;
            SET @Message = 'Heat quantities would total ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @Other + @Qty)) +
                           ', more than the ' + CONVERT(VARCHAR(30), CONVERT(FLOAT, @HdrQty)) + ' on the document.';
            COMMIT TRANSACTION; RETURN;
        END

        /*  A piece reference number is issued once. Ranges within this same
            document are laid out end to end by the screen and re-saved in
            turn, so only OTHER live documents are checked.  */
        IF ISNULL(@RefFrom, 0) > 0 AND ISNULL(@RefTo, 0) >= @RefFrom
        BEGIN
            DECLARE @ClashLine INT;

            SELECT TOP 1 @ClashLine = r.HTransSlNo
              FROM dbo.HeatRefNo r
             WHERE r.SubscID = @SubscID AND r.DeleteStatus = 'N'
               AND r.MTransNo <> @MTransNo
               AND r.RefNo BETWEEN @RefFrom AND @RefTo
             ORDER BY r.RefNo;

            -- Report the whole range the clashing heat line holds.
            IF @ClashLine IS NOT NULL
                SELECT @ClashFrom = MIN(r.RefNo), @ClashTo = MAX(r.RefNo),
                       @ClashHeat = (SELECT HeatNo FROM dbo.HeatNoDtl WHERE TransSlNo = @ClashLine)
                  FROM dbo.HeatRefNo r
                 WHERE r.HTransSlNo = @ClashLine AND r.DeleteStatus = 'N';

            IF @ClashFrom IS NOT NULL
            BEGIN
                SET @ID = 0;
                SET @Message = 'Ref No ' + CONVERT(VARCHAR(20), @RefFrom) + '-' + CONVERT(VARCHAR(20), @RefTo) +
                               ' overlaps numbers already issued (' + CONVERT(VARCHAR(20), @ClashFrom) + '-' +
                               CONVERT(VARCHAR(20), @ClashTo) + ', heat ' + ISNULL(@ClashHeat, '?') + ').';
                COMMIT TRANSACTION; RETURN;
            END
        END

        SELECT @GradeNo = MTransNo FROM dbo.MaterialStd
         WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.HeatNoDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.HeatNoDtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, GradeNo, Grade,
             ItemRemarks, Qty, Unit, HeatNo, TCNo, TCDate, TCFile,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc, @GradeNo, LTRIM(RTRIM(@Grade)),
             LTRIM(RTRIM(@ItemRemarks)), @Qty, @Unit, @HeatNo,
             LTRIM(RTRIM(@TCNo)), @TCDate, LTRIM(RTRIM(@TCFile)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @NewID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Heat number saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.HeatNoDtl
               SET PartNo = @PartNo, PartCode = @PartCode, PartDescription = @PartDesc,
                   GradeNo = @GradeNo, Grade = LTRIM(RTRIM(@Grade)),
                   ItemRemarks = LTRIM(RTRIM(@ItemRemarks)), Qty = @Qty, Unit = @Unit,
                   HeatNo = @HeatNo, TCNo = LTRIM(RTRIM(@TCNo)), TCDate = @TCDate,
                   TCFile = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(@TCFile, ''))), ''), TCFile),
                   SystemIP = @SystemIP, ModifiedBy = @ModifiedBy,
                   ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT = 0
            BEGIN
                SET @ID = 0; SET @Message = 'Record not found.';
                COMMIT TRANSACTION; RETURN;
            END

            SET @NewID = @TransSlNo;
            SET @Message = 'Heat number updated successfully.';
        END
        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Piece serial numbers: rewritten whole, so an edited range replaces
          the old one instead of merging with it.
        ------------------------------------------------------------------*/
        DELETE FROM dbo.HeatRefNo
         WHERE MTransNo = @MTransNo AND HTransSlNo = @NewID AND SubscID = @SubscID;

        IF ISNULL(@RefFrom, 0) > 0 AND ISNULL(@RefTo, 0) >= @RefFrom
        BEGIN
            IF (@RefTo - @RefFrom) > 10000
            BEGIN
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                SET @ID = 0;
                SET @Message = 'That reference range covers more than 10,000 pieces. Split it across heat lines.';
                RETURN;
            END

            SET @i = @RefFrom;
            WHILE @i <= @RefTo
            BEGIN
                INSERT INTO dbo.HeatRefNo
                (MTransNo, HTransSlNo, RefNo, PartNo, SystemIP, CreatedBy, CreatedOn,
                 DeleteStatus, LockStatus, LockCounter, SubscID)
                VALUES
                (@MTransNo, @NewID, @i, @PartNo, @SystemIP, @CreatedBy, GETDATE(),
                 'N', 'N', 1, @SubscID);

                SET @i = @i + 1;
            END
        END

        SET @ID = @NewID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END
GO

/*------------------------------------------------------------------------------
  3. INDEX - last reference number issued / overlap check
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = N'IX_HeatRefNo_SubscID_RefNo' AND object_id = OBJECT_ID(N'dbo.HeatRefNo'))
    CREATE NONCLUSTERED INDEX IX_HeatRefNo_SubscID_RefNo
        ON dbo.HeatRefNo (SubscID, DeleteStatus, RefNo)
        INCLUDE (MTransNo, HTransSlNo);
GO

PRINT '32_HeatNoPage.sql applied.';
GO

GO
PRINT '32_HeatNoPage.sql applied.';
GO

PRINT '>>> 34_SalesApproval.sql';
GO

/*==============================================================================
  34. SALES APPROVAL  -  approve / reject for Quotation and Order Acceptance

  LockStatus on QtnMst and OAMst:
      N  draft (an EXECUTIVE's save)
      S  submitted, waiting for approval
      Y  approved
      R  rejected   <- new; the reason is kept in RejectReason

  Adds RejectReason / RejectedBy / RejectedOn to both headers, and one
  procedure per document:
      SP_QtnApproval   @Status 'Approve' | 'Reject'
      SP_OAApproval    @Status 'Approve' | 'Reject'

  Rules (both documents):
    * only the live, current revision (DeleteStatus 'N', DocAmdStatus 'N')
    * an EXECUTIVE can neither approve nor reject
    * Approve  - refused when already approved; clears any earlier rejection
    * Reject   - reason required; refused when approved (unlock it first)
                 or already rejected
  A rejected document stays editable, so the author can correct it and
  submit again; the reason stays on the row until it is approved.

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures
  and CREATE OR ALTER then fails with Msg 208.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  COLUMNS
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.QtnMst', 'RejectReason') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectReason VARCHAR(250) NULL;
IF COL_LENGTH('dbo.QtnMst', 'RejectedBy') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectedBy INT NULL;
IF COL_LENGTH('dbo.QtnMst', 'RejectedOn') IS NULL
    ALTER TABLE dbo.QtnMst ADD RejectedOn DATETIME2 NULL;

IF COL_LENGTH('dbo.OAMst', 'RejectReason') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectReason VARCHAR(250) NULL;
IF COL_LENGTH('dbo.OAMst', 'RejectedBy') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectedBy INT NULL;
IF COL_LENGTH('dbo.OAMst', 'RejectedOn') IS NULL
    ALTER TABLE dbo.OAMst ADD RejectedOn DATETIME2 NULL;
GO

/*------------------------------------------------------------------------------
  SP_QtnApproval
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_QtnApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_QtnApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_QtnApproval
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @RejectReason   VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Approve' | 'Reject'
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @CurLock CHAR(1), @CurAmd CHAR(1), @UserType VARCHAR(50);

    SELECT @CurLock = ISNULL(LockStatus, 'N'), @CurAmd = ISNULL(DocAmdStatus, 'N')
      FROM dbo.QtnMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @MTransNo * 0 + @ModifiedBy;

    SET @RejectReason = NULLIF(LTRIM(RTRIM(ISNULL(@RejectReason, ''))), '');

    IF @CurLock IS NULL
        SET @Message = N'Quotation not found.';
    ELSE IF @CurAmd = 'Y'
        SET @Message = N'This is a superseded revision. Only the current revision can be approved or rejected.';
    ELSE IF @UserType = 'EXECUTIVE'
        SET @Message = N'An executive cannot approve or reject a quotation.';
    ELSE IF @Status = 'Approve'
    BEGIN
        IF @CurLock = 'Y'
            SET @Message = N'Quotation is already approved.';
        ELSE
        BEGIN
            UPDATE dbo.QtnMst
               SET LockStatus   = 'Y',
                   RejectReason = NULL,
                   RejectedBy   = NULL,
                   RejectedOn   = NULL,
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Quotation approved successfully.'; END
        END
    END
    ELSE IF @Status = 'Reject'
    BEGIN
        IF @RejectReason IS NULL
            SET @Message = N'Rejection reason is required.';
        ELSE IF @CurLock = 'Y'
            SET @Message = N'Quotation is approved. Unlock it before rejecting.';
        ELSE IF @CurLock = 'R'
            SET @Message = N'Quotation is already rejected.';
        ELSE
        BEGIN
            UPDATE dbo.QtnMst
               SET LockStatus   = 'R',
                   RejectReason = @RejectReason,
                   RejectedBy   = @ModifiedBy,
                   RejectedOn   = GETDATE(),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Quotation rejected.'; END
        END
    END
    ELSE
        SET @Message = N'Invalid status. Use Approve or Reject.';
END
GO

/*------------------------------------------------------------------------------
  SP_OAApproval
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_OAApproval', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_OAApproval AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_OAApproval
    @ID             INT             OUTPUT,
    @MTransNo       INT,
    @RejectReason   VARCHAR(250),
    @SystemIP       VARCHAR(20),
    @ModifiedBy     INT,
    @SubscID        INT,
    @Status         VARCHAR(20),    -- 'Approve' | 'Reject'
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ID = 0;
    SET @Message = N'';

    DECLARE @CurLock CHAR(1), @CurAmd CHAR(1), @UserType VARCHAR(50);

    SELECT @CurLock = ISNULL(LockStatus, 'N'), @CurAmd = ISNULL(DocAmdStatus, 'N')
      FROM dbo.OAMst
     WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

    SELECT @UserType = UPPER(LTRIM(RTRIM(ISNULL(UserType, ''))))
      FROM dbo.UserMaster
     WHERE MTransNo = @ModifiedBy;

    SET @RejectReason = NULLIF(LTRIM(RTRIM(ISNULL(@RejectReason, ''))), '');

    IF @CurLock IS NULL
        SET @Message = N'Order Acceptance not found.';
    ELSE IF @CurAmd = 'Y'
        SET @Message = N'This is a superseded revision. Only the current revision can be approved or rejected.';
    ELSE IF @UserType = 'EXECUTIVE'
        SET @Message = N'An executive cannot approve or reject an order acceptance.';
    ELSE IF @Status = 'Approve'
    BEGIN
        IF @CurLock = 'Y'
            SET @Message = N'Order Acceptance is already approved.';
        ELSE
        BEGIN
            UPDATE dbo.OAMst
               SET LockStatus   = 'Y',
                   RejectReason = NULL,
                   RejectedBy   = NULL,
                   RejectedOn   = NULL,
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Order Acceptance approved successfully.'; END
        END
    END
    ELSE IF @Status = 'Reject'
    BEGIN
        IF @RejectReason IS NULL
            SET @Message = N'Rejection reason is required.';
        ELSE IF @CurLock = 'Y'
            SET @Message = N'Order Acceptance is approved. Unlock it before rejecting.';
        ELSE IF @CurLock = 'R'
            SET @Message = N'Order Acceptance is already rejected.';
        ELSE
        BEGIN
            UPDATE dbo.OAMst
               SET LockStatus   = 'R',
                   RejectReason = @RejectReason,
                   RejectedBy   = @ModifiedBy,
                   RejectedOn   = GETDATE(),
                   SystemIP     = @SystemIP,
                   ModifiedBy   = @ModifiedBy,
                   ModifiedOn   = GETDATE(),
                   LockCounter  = ISNULL(LockCounter, 0) + 1
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = N'Order Acceptance rejected.'; END
        END
    END
    ELSE
        SET @Message = N'Invalid status. Use Approve or Reject.';
END
GO

GO
PRINT '34_SalesApproval.sql applied.';
GO

PRINT '>>> 35_PurchaseReportsMenu.sql';
GO

/*==============================================================================
  35. PURCHASE REPORTS MENU  -  sidebar section for EMSSoft/Modules/PurchaseReports/

  The report pages read live data from PurchaseReportsController
  (GET api/PurchaseOrderReport|GRNReport/AllReports, plus the RFQ list
  endpoints) and InwardReportsController (GET api/DCReport|RGPReport|
  RGPInwardReport/AllReports). No tables or procedures are needed, only the
  menu rows and their AccessTemplate grants (a Menulist row alone is
  invisible, see 25_SalesMenuAndLookups.sql).

  Issue Report is listed although the Store Issue module (and its tables) does
  not exist yet, the same way the menu already lists other planned pages.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/PurchaseReports/'.
    Set it to wherever the deployed server serves them from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 721-728 (as it does 701-714 and 801-820).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  SIDEBAR SECTION  +  GRANTS

  MTransNo 721-728 are unused. SSLNo 8 puts Purchase Reports after Sales
  Reports, rows in the order material moves through purchasing.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/PurchaseReports/';   -- <- check, see header

DECLARE @Menu TABLE
(
    MTransNo    INT,
    Section     VARCHAR(50),
    SSLNo       INT,
    ModuleName  VARCHAR(50),
    MSLNo       INT,
    ModuleIcon  VARCHAR(50),
    ModulePage  VARCHAR(100)
);

INSERT INTO @Menu (MTransNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
VALUES
    (721, 'Purchase Reports', 8, 'RFQ Report',            1, 'bi-envelope-paper',        @Folder + 'RFQReport.html'),
    (722, 'Purchase Reports', 8, 'RFQ Approval Report',   2, 'bi-patch-check',           @Folder + 'RFQApprovalReport.html'),
    (723, 'Purchase Reports', 8, 'Purchase Order Report', 3, 'bi-bag-check',             @Folder + 'PurchaseOrderReport.html'),
    (724, 'Purchase Reports', 8, 'GRN Report',            4, 'bi-box-seam',              @Folder + 'GRNReport.html'),
    (725, 'Purchase Reports', 8, 'DC Report',             5, 'bi-truck',                 @Folder + 'DCReport.html'),
    (726, 'Purchase Reports', 8, 'RGP Report',            6, 'bi-box-arrow-right',       @Folder + 'RGPReport.html'),
    (727, 'Purchase Reports', 8, 'RGP Inward Report',     7, 'bi-box-arrow-in-left',     @Folder + 'RGPInwardReport.html'),
    (728, 'Purchase Reports', 8, 'Issue Report',          8, 'bi-box-arrow-up-right',    @Folder + 'IssueReport.html');

MERGE dbo.Menulist AS T
USING @Menu AS S
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

-- Same batch as the MERGE (no GO): @Menu would not survive a batch boundary.
-- Every template that already holds grants gets full access to the reports,
-- with Department copied from its existing rows.
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT  T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM  (
            SELECT TemplateName, SubscID, MAX(Department) AS Department
              FROM dbo.AccessTemplate
             WHERE TemplateName IS NOT NULL
             GROUP BY TemplateName, SubscID
        ) T
 CROSS JOIN (SELECT MTransNo FROM @Menu) M
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.AccessTemplate A
            WHERE A.TemplateName = T.TemplateName
              AND A.SubscID      = T.SubscID
              AND A.ModuleNo     = M.MTransNo);
GO

GO
PRINT '35_PurchaseReportsMenu.sql applied.';
GO

PRINT '>>> 36_OALinkPurchaseLines.sql';
GO

/*==============================================================================
  36. OA LINK ON PURCHASE LINES  -  OA Year / OA No / OA ID

  A purchase requisition or purchase order line may name the Order Acceptance
  line it is bought for:
      OAYear = OAMst.DocYear,  OANo = OAMst.DocNo,  OAID = OADtl.SlNo
  taken only from the live, current revision of the OA
  (OAMst.DeleteStatus = 'N' AND OAMst.DocAmdStatus = 'N', OADtl.DeleteStatus = 'N').

  The link is OPTIONAL, but it is all three or none: leave all three blank, or
  fill all three. dbo.FN_OALinkCheck holds that rule once, and both line
  procedures call it on Insert / Update:
      SP_PurReqDtl   - PurReqDtl gains OAYear / OANo / OAID (it had none, so the
                       OA picked on the requisition screen was being dropped).
                       The three parameters are new, optional and LAST, so an
                       older caller that does not send them still works.
      SP_PODtl       - already stored them; now it also checks them. Body is
                       the live definition with only the check added.

  0 and NULL both mean "blank".

  SP_ names: create-stub-then-ALTER, because master shadows SP_ procedures.
  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

/*------------------------------------------------------------------------------
  COLUMNS
------------------------------------------------------------------------------*/
IF COL_LENGTH('dbo.PurReqDtl', 'OAYear') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OAYear INT NULL;
IF COL_LENGTH('dbo.PurReqDtl', 'OANo') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OANo INT NULL;
IF COL_LENGTH('dbo.PurReqDtl', 'OAID') IS NULL
    ALTER TABLE dbo.PurReqDtl ADD OAID INT NULL;
GO

/*------------------------------------------------------------------------------
  FN_OALinkCheck - NULL when the link is acceptable, otherwise the message.
------------------------------------------------------------------------------*/
CREATE OR ALTER FUNCTION dbo.FN_OALinkCheck
(
    @OAYear     INT,
    @OANo       INT,
    @OAID       INT,
    @SubscID    INT
)
RETURNS NVARCHAR(200)
AS
BEGIN
    DECLARE @Y INT = NULLIF(@OAYear, 0),
            @N INT = NULLIF(@OANo, 0),
            @L INT = NULLIF(@OAID, 0);

    IF @Y IS NULL AND @N IS NULL AND @L IS NULL
        RETURN NULL;

    IF @Y IS NULL OR @N IS NULL OR @L IS NULL
        RETURN N'OA Year, OA No and OA ID go together - select all three, or leave all three blank.';

    IF NOT EXISTS (SELECT 1
                     FROM dbo.OAMst M
                    WHERE M.DocYear      = @Y
                      AND M.DocNo        = @N
                      AND M.SubscID      = @SubscID
                      AND M.DeleteStatus = 'N'
                      AND M.DocAmdStatus = 'N')
        RETURN N'OA ' + CAST(@Y AS NVARCHAR(10)) + N' / ' + CAST(@N AS NVARCHAR(10))
             + N' was not found, or is not the current revision.';

    IF NOT EXISTS (SELECT 1
                     FROM dbo.OAMst M
                     JOIN dbo.OADtl D ON D.MTransNo = M.MTransNo
                    WHERE M.DocYear      = @Y
                      AND M.DocNo        = @N
                      AND M.SubscID      = @SubscID
                      AND M.DeleteStatus = 'N'
                      AND M.DocAmdStatus = 'N'
                      AND D.DeleteStatus = 'N'
                      AND D.SlNo         = @L)
        RETURN N'OA ID ' + CAST(@L AS NVARCHAR(10)) + N' is not a line of OA '
             + CAST(@Y AS NVARCHAR(10)) + N' / ' + CAST(@N AS NVARCHAR(10)) + N'.';

    RETURN NULL;
END
GO

/*------------------------------------------------------------------------------
  SP_PurReqDtl
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PurReqDtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PurReqDtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PurReqDtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12, 2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(14, 4),
    @Unit           VARCHAR(10),
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT,
    @OAYear         INT = NULL,
    @OANo           INT = NULL,
    @OAID           INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF @Status IN ('Insert', 'Update')
        BEGIN
            SET @Message = dbo.FN_OALinkCheck(@OAYear, @OANo, @OAID, @SubscID);
            IF @Message IS NOT NULL
            BEGIN
                SET @ID = 0;
                COMMIT TRANSACTION; RETURN;
            END
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
            FROM dbo.PurReqDtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.PurReqDtl
            (SlNo, MTransNo, PartNo, ItemRemarks, Qty, Unit, OAYear, OANo, OAID, SystemIP,
             CreatedBy, CreatedOn, DeleteStatus, DeleteReason, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, LTRIM(RTRIM(@ItemRemarks)), @Qty,
             LTRIM(RTRIM(@Unit)), NULLIF(@OAYear, 0), NULLIF(@OANo, 0), NULLIF(@OAID, 0),
             @SystemIP, @CreatedBy, @CreatedOn, 'N', NULL, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END
        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET PartNo = @PartNo, ItemRemarks = LTRIM(RTRIM(@ItemRemarks)),
                Qty = @Qty, Unit = LTRIM(RTRIM(@Unit)),
                OAYear = NULLIF(@OAYear, 0), OANo = NULLIF(@OANo, 0), OAID = NULLIF(@OAID, 0),
                SystemIP = @SystemIP,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
              AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END
        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PurReqDtl
            SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                ModifiedBy = @ModifiedBy, ModifiedOn = @ModifiedOn
            WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
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
  SP_PODtl - live definition, plus the FN_OALinkCheck call after the quantity
  check (Insert / Update only).
------------------------------------------------------------------------------*/
IF OBJECT_ID(N'dbo.SP_PODtl', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE dbo.SP_PODtl AS BEGIN SET NOCOUNT ON; END');
GO

ALTER PROCEDURE dbo.SP_PODtl
    @ID             INT             OUTPUT,
    @TransSlNo      INT,
    @MTransNo       INT,
    @SlNo           NUMERIC(12,2),
    @PartNo         INT,
    @ItemRemarks    VARCHAR(500),
    @Qty            NUMERIC(12,4),
    @Qty2           NUMERIC(12,2),
    @Rate           NUMERIC(14,2),
    @MRP            NUMERIC(14,2),
    @DiscP          NUMERIC(6,2),
    @PnFP           NUMERIC(6,2),
    @FreightAmt     NUMERIC(14,2),
    @DeleveryDate   DATE,
    @Grade          VARCHAR(50),
    @MtrlStd        VARCHAR(50),
    @CoreGrade      VARCHAR(50),
    @Spare          VARCHAR(50),
    @SpareGrade     VARCHAR(50),
    @SpareQty       NUMERIC(12,2),
    @OAYear         INT,
    @OANo           INT,
    @OAID           INT,
    @SystemIP       VARCHAR(20),
    @CreatedBy      INT,
    @CreatedOn      DATETIME2,
    @ModifiedBy     INT,
    @ModifiedOn     DATETIME2,
    @DeleteReason   VARCHAR(250),
    @SubscID        INT,
    @Status         VARCHAR(20),
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @TaxType      VARCHAR(20),
                @Locked       CHAR(1),
                @PartCode     VARCHAR(30),
                @PartDesc     VARCHAR(100),
                @HSNCode      VARCHAR(10),
                @Unit         VARCHAR(20),
                @Unit2        VARCHAR(20),
                @ConvRatio    NUMERIC(12,2),
                @TxnUnit      CHAR(1),
                @GSTP         NUMERIC(6,2),
                @GradeNo      INT,
                @BasicValue   NUMERIC(16,2),
                @DiscAmt      NUMERIC(12,2),
                @BasicRate    NUMERIC(14,2),
                @PnFAmt       NUMERIC(14,2),
                @TaxableValue NUMERIC(14,2),
                @IGSTAmt      NUMERIC(14,2),
                @CGSTAmt      NUMERIC(14,2),
                @SGSTAmt      NUMERIC(14,2),
                @TotalValue   NUMERIC(16,2);

        SELECT @TaxType = LTRIM(RTRIM(TaxType)), @Locked = LockStatus
          FROM dbo.POMst
         WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus = 'N';

        IF @TaxType IS NULL
        BEGIN
            SET @ID = 0; SET @Message = 'Purchase Order not found.';
            COMMIT TRANSACTION; RETURN;
        END

        IF @Locked = 'Y'
        BEGIN
            SET @ID = 0;
            SET @Message = 'Purchase Order is approved. Unlock it, or raise an amendment, before changing lines.';
            COMMIT TRANSACTION; RETURN;
        END

        /*------------------------------------------------------------------
          Part snapshot, for Insert and Update.
        ------------------------------------------------------------------*/
        IF @Status IN ('Insert', 'Update')
        BEGIN
            SELECT @PartCode  = LEFT(LTRIM(RTRIM(ISNULL(P.PartCode, ''))), 30),
                   @PartDesc  = LEFT(LTRIM(RTRIM(ISNULL(P.PartName, ''))), 100),
                   @HSNCode   = P.HSNCode,
                   @Unit      = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos'),
                   @Unit2     = ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.SecondaryUnit, ''))), ''),
                                ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(P.PrimaryUnit, ''))), ''), 'Nos')),
                   @ConvRatio = ISNULL(NULLIF(P.ConversionRatio, 0), 1),
                   @TxnUnit   = LEFT(ISNULL(P.TxnUnit, 'P'), 1),
                   @GSTP      = ISNULL(P.TaxP, 0)
              FROM dbo.PartMaster P
             WHERE P.MTransNo = @PartNo AND P.DeleteStatus = 'N';

            IF @PartCode IS NULL
            BEGIN
                SET @ID = 0; SET @Message = 'Part not found.';
                COMMIT TRANSACTION; RETURN;
            END

            IF ISNULL(@Qty, 0) <= 0
            BEGIN
                SET @ID = 0; SET @Message = 'Quantity must be greater than zero.';
                COMMIT TRANSACTION; RETURN;
            END

            -- OA link: optional, but all three or none (SQL/36).
            SET @Message = dbo.FN_OALinkCheck(@OAYear, @OANo, @OAID, @SubscID);
            IF @Message IS NOT NULL
            BEGIN
                SET @ID = 0;
                COMMIT TRANSACTION; RETURN;
            END

            SELECT @GradeNo = MTransNo
              FROM dbo.MaterialStd
             WHERE Grade = @Grade AND DeleteStatus = 'N' AND SubscID = @SubscID;

            EXEC dbo.SP_PODtlPrice
                 @Qty, @Rate, @DiscP, @PnFP, @FreightAmt, @GSTP, @TaxType,
                 @BasicValue   OUTPUT, @DiscAmt      OUTPUT, @BasicRate OUTPUT,
                 @PnFAmt       OUTPUT, @TaxableValue OUTPUT, @IGSTAmt   OUTPUT,
                 @CGSTAmt      OUTPUT, @SGSTAmt      OUTPUT, @TotalValue OUTPUT;
        END

        IF (@Status = 'Insert')
        BEGIN
            SELECT @SlNo = ISNULL(MAX(SlNo), 0) + 1
              FROM dbo.PODtl WHERE MTransNo = @MTransNo AND DeleteStatus = 'N';

            INSERT INTO dbo.PODtl
            (SlNo, MTransNo, PartNo, PartCode, PartDescription, ItemRemarks, HSNCode,
             Qty, Unit, Rate, MRP, BasicRate, BasicValue, DiscP, DiscAmt, PnFP, PnFAmt,
             FreightAmt, TaxableValue, GSTP, IGSTAmt, CGSTAmt, SGSTAmt, TotalValue,
             Qty2, ConvRatio, TxnUnit, Unit2, OANo, OAID, OAYear, DeleveryDate,
             GradeNo, Grade, CoreGrade, Spare, SpareGrade, SpareQty, MtrlStd,
             SystemIP, CreatedBy, CreatedOn, DeleteStatus, DeleteReason,
             LockStatus, LockCounter, SubscID)
            VALUES
            (@SlNo, @MTransNo, @PartNo, @PartCode, @PartDesc, LTRIM(RTRIM(@ItemRemarks)), @HSNCode,
             @Qty, @Unit, @Rate, @MRP, @BasicRate, @BasicValue, ISNULL(@DiscP, 0), @DiscAmt,
             ISNULL(@PnFP, 0), @PnFAmt, ISNULL(@FreightAmt, 0), @TaxableValue, @GSTP,
             @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
             ISNULL(@Qty2, 0), @ConvRatio, @TxnUnit, @Unit2,
             NULLIF(@OANo, 0), NULLIF(@OAID, 0), NULLIF(@OAYear, 0), @DeleveryDate,
             @GradeNo, LTRIM(RTRIM(@Grade)), LTRIM(RTRIM(@CoreGrade)), LTRIM(RTRIM(@Spare)),
             LTRIM(RTRIM(@SpareGrade)), ISNULL(@SpareQty, 0), LTRIM(RTRIM(@MtrlStd)),
             @SystemIP, @CreatedBy, ISNULL(@CreatedOn, GETDATE()), 'N', NULL, 'N', 1, @SubscID);

            SET @ID = CONVERT(INT, SCOPE_IDENTITY());
            SET @Message = 'Line item saved successfully.';
        END

        ELSE IF (@Status = 'Update')
        BEGIN
            UPDATE dbo.PODtl
               SET PartNo          = @PartNo,
                   PartCode        = @PartCode,
                   PartDescription = @PartDesc,
                   ItemRemarks     = LTRIM(RTRIM(@ItemRemarks)),
                   HSNCode         = @HSNCode,
                   Qty             = @Qty,
                   Unit            = @Unit,
                   Rate            = @Rate,
                   MRP             = @MRP,
                   BasicRate       = @BasicRate,
                   BasicValue      = @BasicValue,
                   DiscP           = ISNULL(@DiscP, 0),
                   DiscAmt         = @DiscAmt,
                   PnFP            = ISNULL(@PnFP, 0),
                   PnFAmt          = @PnFAmt,
                   FreightAmt      = ISNULL(@FreightAmt, 0),
                   TaxableValue    = @TaxableValue,
                   GSTP            = @GSTP,
                   IGSTAmt         = @IGSTAmt,
                   CGSTAmt         = @CGSTAmt,
                   SGSTAmt         = @SGSTAmt,
                   TotalValue      = @TotalValue,
                   Qty2            = ISNULL(@Qty2, 0),
                   ConvRatio       = @ConvRatio,
                   TxnUnit         = @TxnUnit,
                   Unit2           = @Unit2,
                   OANo            = NULLIF(@OANo, 0),
                   OAID            = NULLIF(@OAID, 0),
                   OAYear          = NULLIF(@OAYear, 0),
                   DeleveryDate    = @DeleveryDate,
                   GradeNo         = @GradeNo,
                   Grade           = LTRIM(RTRIM(@Grade)),
                   CoreGrade       = LTRIM(RTRIM(@CoreGrade)),
                   Spare           = LTRIM(RTRIM(@Spare)),
                   SpareGrade      = LTRIM(RTRIM(@SpareGrade)),
                   SpareQty        = ISNULL(@SpareQty, 0),
                   MtrlStd         = LTRIM(RTRIM(@MtrlStd)),
                   SystemIP        = @SystemIP,
                   ModifiedBy      = @ModifiedBy,
                   ModifiedOn      = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus = 'N';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item updated successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'Delete')
        BEGIN
            UPDATE dbo.PODtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE TransSlNo = @TransSlNo AND MTransNo = @MTransNo
               AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @TransSlNo; SET @Message = 'Line item deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'Record not found.'; END
        END

        ELSE IF (@Status = 'DeleteAll')
        BEGIN
            UPDATE dbo.PODtl
               SET DeleteStatus = 'Y', DeleteReason = @DeleteReason,
                   ModifiedBy = @ModifiedBy, ModifiedOn = ISNULL(@ModifiedOn, GETDATE())
             WHERE MTransNo = @MTransNo AND SubscID = @SubscID AND DeleteStatus <> 'Y';

            IF @@ROWCOUNT > 0
            BEGIN SET @ID = @MTransNo; SET @Message = 'All line items deleted successfully.'; END
            ELSE
            BEGIN SET @ID = 0; SET @Message = 'No active line items found.'; END
        END

        ELSE
        BEGIN
            SET @ID = 0; SET @Message = 'Unknown status: ' + ISNULL(@Status, '(null)');
        END

        IF @ID > 0 EXEC dbo.SP_POMstRecalc @MTransNo, @SubscID;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        SET @ID = 0; SET @Message = ERROR_MESSAGE(); THROW;
    END CATCH
END

GO

GO
PRINT '36_OALinkPurchaseLines.sql applied.';
GO

PRINT 'Purchase / inward / reports deployment complete.';
GO
