/*==============================================================================
  13. PURCHASE ORDER -> GRN -> STORE / INSPECTION / HEAT No.  -  TABLES

  Covers sections 2.4 and 2.5 of "Purchas, Store, Sales And Production
  Tables.txt", plus the three tables the downstream screens need:

      2.4  Purchase order      POMst, PODtl, POTerms, POExtras
      2.5  Goods receipt       GRNMst, GRNDtl, GRNNCDtl, GRNQRCode,
                               GRNAttachment
      1.4  Heat traceability   HeatNoMst, HeatNoDtl, HeatRefNo
      2.9  Store               StoreLocation

  ----------------------------------------------------------------------------
  THE DOCUMENT CHAIN THIS SCRIPT PUTS IN PLACE
  ----------------------------------------------------------------------------

      PurReqMst ── RFQMst ── RFQPriceMst          (already in the database)
                                  |
                                  v
      (1) PURCHASE ORDER      POMst / PODtl / POTerms / POExtras
              LockStatus 'Y' + DocAmdStatus 'N'  =  the live, orderable revision
                                  |
                                  v
      (2) GOODS RECEIPT       GRNMst / GRNDtl          GRNType = 'N'
              BillQty may not exceed the PO line balance
                                  |
                 +----------------+----------------+
                 v                                 v
      (3) STORE ALLOCATION              (4) HEAT No. ENTRY
          GRNDtl.Store / .Location          HeatNoMst / HeatNoDtl / HeatRefNo
                                            (TargetTransNo -> GRNMst.MTransNo)
                                  |
                                  v
      (5) INWARD INSPECTION (PO)   GRNDtl.AccQty / RejQty / RewQty / AODQty
              non-conformance lines in GRNNCDtl
              posting stamps InspNo / QCDate / QCverifyBy, GRNType = 'Q'
                                  |
                                  v
      (6) INWARD INSPECTION (CLOSURE)   GRNMst.NCCloseDate / NCRemarks /
              NCCloseUserNo / NCCloseAttachment,        GRNType = 'C'

  ----------------------------------------------------------------------------
  CONVENTIONS  (identical to Table_Structures.txt and the rest of this schema)
  ----------------------------------------------------------------------------
      * dbo.TableName, no square brackets.
      * Surrogate key first: MTransNo (master) / TransSlNo (child), INT IDENTITY.
      * Fixed audit block last: SystemIP, CreatedBy, CreatedOn, ModifiedBy,
        ModifiedOn, DeleteStatus, DeleteReason, LockStatus, LockCounter, SubscID.
      * Named default constraints DF_<Table>_CreatedOn / _DeleteStatus /
        _LockStatus; CreatedOn defaults to GETDATE().
      * Filtered non-clustered indexes, WHERE DeleteStatus = 'N'.
      * No FOREIGN KEY constraints - the relationship is documented in the
        banner above each table, as everywhere else in this schema.

  ----------------------------------------------------------------------------
  TWO DEVIATIONS FROM THE SOURCE DOCUMENT, BOTH DELIBERATE
  ----------------------------------------------------------------------------
      1. GRNDtl:  the source declares UQ_GRNDtl UNIQUE (DocNo, DeleteStatus,
         SubscID).  That would permit exactly ONE line per GRN, which cannot be
         what was meant.  The rule created here instead is a filtered unique
         index, UQ_GRNDtl_Line on (SubscID, MTransNo, SlNo) WHERE
         DeleteStatus = 'N' - one LIVE line per line number per GRN.  The
         filter matters: a PO-backed line takes its number from the PO, so the
         number comes back if the line is deleted and re-added, and a
         DeleteStatus-in-the-key constraint would then collide on the second
         delete.

      2. GRNMst:  GRNType is declared NULL in the source and left NULL by the
         legacy application.  It is the stage marker for the whole inward flow
         ('N' received -> 'Q' inspected -> 'C' closed), and every worklist
         filters on it, so it defaults to 'N' here instead of NULL.  Nothing
         else about the column changes.

  Idempotent: every object is guarded, so re-running the script is safe.
==============================================================================*/

USE EMSSoft;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO


/*==============================================================================
  2.4  PURCHASE ORDER
==============================================================================*/

/*------------------------------------------------------------------------------
  35. POMst  -  purchase order header

      TargetTransNo -> RFQPriceMst.MTransNo  when TargetType = 'PQ'
                    -> PurReqMst.MTransNo    when TargetType = 'PR'
                    -> NULL                  when TargetType = 'Direct'
      VendorNo      -> SupplierMaster.MTransNo
      VendorNoShip  -> SupplierMaster.MTransNo  (ship-to, when it differs)

      DocAmdNo / DocAmdStatus carry the revision chain: the row with
      DocAmdStatus = 'N' is the CURRENT revision, every superseded revision is
      flipped to 'Y'.  A GRN may only be raised against a revision that is both
      DocAmdStatus = 'N' and LockStatus = 'Y'.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.POMst', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.POMst
    (
        MTransNo        INT IDENTITY(1,1) NOT NULL,
        DocSeries       CHAR(10)      NOT NULL,
        DocYear         INT           NOT NULL,
        DocNo           INT           NOT NULL,
        DocDate         DATE          NOT NULL,
        DocAmdNo        SMALLINT      NOT NULL,
        DocAmdStatus    CHAR(1)       NOT NULL,
        TargetTransNo   INT           NULL,
        TargetDocYear   INT           NULL,
        TargetDocNo     INT           NULL,
        TargetAmdNo     INT           NULL,
        VendorNo        INT           NOT NULL,
        VendorCode      VARCHAR(20)   NOT NULL,
        VendorName      VARCHAR(100)  NOT NULL,
        Address         VARCHAR(500)  NOT NULL,
        GSTIN           VARCHAR(20)   NOT NULL,
        StateName       VARCHAR(50)   NULL,
        PhoneNo         VARCHAR(30)   NULL,
        EmailID         VARCHAR(80)   NULL,
        RefNo           VARCHAR(50)   NULL,
        RefDate         DATE          NULL,
        DueDate         DATE          NULL,
        DocNotes        VARCHAR(250)  NULL,
        DocAmdNotes     VARCHAR(250)  NULL,
        DocStatus       CHAR(10)      NOT NULL,
        TaxType         CHAR(20)      NOT NULL,
        BasicValue      NUMERIC(16,2) NULL,
        ExemptValue     NUMERIC(16,2) NULL,
        TaxableValue    NUMERIC(16,2) NULL,
        PnFAmt          NUMERIC(16,2) NULL,
        FreightAmt      NUMERIC(16,2) NULL,
        IGSTAmt         NUMERIC(16,2) NULL,
        CGSTAmt         NUMERIC(16,2) NULL,
        SGSTAmt         NUMERIC(16,2) NULL,
        OtherCharges    VARCHAR(50)   NULL,
        OtherAmt        NUMERIC(16,2) NULL,
        RoundOff        NUMERIC(6,4)  NULL,
        NetValue        NUMERIC(18,2) NULL,
        TxnCurrency     VARCHAR(10)   NULL,
        CurrencyRate    NUMERIC(6,2)  NULL,
        CurrencyAmount  NUMERIC(16,2) NULL,
        TargetType      VARCHAR(30)   NULL,
        PurSubject      VARCHAR(250)  NULL,
        VendorNoShip    INT           NULL,
        BusinessUnit    VARCHAR(50)   NULL,
        Plant           VARCHAR(50)   NULL,
        Source          VARCHAR(50)   NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_POMst_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_POMst_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_POMst_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_POMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_POMst UNIQUE (DocSeries, DocYear, DocNo, DocAmdNo, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.POMst';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POMst_DocNo' AND object_id = OBJECT_ID('dbo.POMst'))
    CREATE NONCLUSTERED INDEX IX_POMst_DocNo
        ON dbo.POMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POMst_DocDate' AND object_id = OBJECT_ID('dbo.POMst'))
    CREATE NONCLUSTERED INDEX IX_POMst_DocDate
        ON dbo.POMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POMst_VendorNo' AND object_id = OBJECT_ID('dbo.POMst'))
    CREATE NONCLUSTERED INDEX IX_POMst_VendorNo
        ON dbo.POMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POMst_TargetTransNo' AND object_id = OBJECT_ID('dbo.POMst'))
    CREATE NONCLUSTERED INDEX IX_POMst_TargetTransNo
        ON dbo.POMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

/*  The GRN screen asks "which POs still have something to receive?" on every
    open.  That question scans the live revision of every PO, so it gets its
    own index.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POMst_Open' AND object_id = OBJECT_ID('dbo.POMst'))
    CREATE NONCLUSTERED INDEX IX_POMst_Open
        ON dbo.POMst (SubscID, DocAmdStatus, LockStatus, DocYear, DocNo)
        INCLUDE (MTransNo, DocSeries, DocDate, VendorNo, VendorName, TargetType)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  36. PODtl  -  purchase order lines with tax break-up
      MTransNo -> POMst.MTransNo
      PartNo   -> PartMaster.MTransNo
      GradeNo  -> MaterialStd.MTransNo

      SlNo is the line number WITHIN the PO.  It is the value a GRN line copies
      into GRNDtl.SlNo to say which PO line it receives against, so it must stay
      stable for the life of the order.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.PODtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PODtl
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        SlNo            NUMERIC(12,2) NULL,
        MTransNo        INT           NOT NULL,
        PartNo          INT           NOT NULL,
        PartCode        VARCHAR(30)   NULL,
        PartDescription VARCHAR(100)  NULL,
        ItemRemarks     VARCHAR(500)  NULL,
        HSNCode         VARCHAR(10)   NULL,
        Qty             NUMERIC(12,4) NOT NULL,
        Unit            VARCHAR(20)   NULL,
        Rate            NUMERIC(14,2) NOT NULL,
        MRP             NUMERIC(14,2) NULL,
        BasicRate       NUMERIC(14,2) NULL,
        BasicValue      NUMERIC(16,2) NOT NULL,
        DiscP           NUMERIC(6,2)  NULL,
        DiscAmt         NUMERIC(12,2) NULL,
        PnFP            NUMERIC(6,2)  NULL,
        PnFAmt          NUMERIC(14,2) NULL,
        FreightAmt      NUMERIC(14,2) NULL,
        TaxableValue    NUMERIC(14,2) NULL,
        GSTP            NUMERIC(6,2)  NULL,
        IGSTAmt         NUMERIC(14,2) NULL,
        CGSTAmt         NUMERIC(14,2) NULL,
        SGSTAmt         NUMERIC(14,2) NULL,
        TotalValue      NUMERIC(16,2) NULL,
        Qty2            NUMERIC(12,2) NULL,
        ConvRatio       NUMERIC(12,2) NULL,
        TxnUnit         CHAR(1)       NULL,
        Unit2           VARCHAR(20)   NULL,
        OANo            INT           NULL,
        OAID            INT           NULL,
        OAYear          INT           NULL,
        DeleveryDate    DATE          NULL,
        GradeNo         INT           NULL,
        Grade           VARCHAR(50)   NULL,
        CoreGrade       VARCHAR(50)   NULL,
        Spare           VARCHAR(50)   NULL,
        SpareGrade      VARCHAR(50)   NULL,
        SpareQty        NUMERIC(12,2) NULL,
        MtrlStd         VARCHAR(50)   NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_PODtl_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_PODtl_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_PODtl_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_PODtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.PODtl';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PODtl_MTransNo' AND object_id = OBJECT_ID('dbo.PODtl'))
    CREATE NONCLUSTERED INDEX IX_PODtl_MTransNo
        ON dbo.PODtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, PartDescription, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PODtl_PartNo' AND object_id = OBJECT_ID('dbo.PODtl'))
    CREATE NONCLUSTERED INDEX IX_PODtl_PartNo
        ON dbo.PODtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit, Rate, Grade)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  37. POTerms  -  purchase order terms and conditions
      MTransNo -> POMst.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.POTerms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.POTerms
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        MTransNo        INT           NULL,
        SlNo            NUMERIC(6,2)  NULL,
        Title           VARCHAR(30)   NULL,
        Description     VARCHAR(250)  NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_POTerms_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_POTerms_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_POTerms_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_POTerms PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.POTerms';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POTerms_MTransNo' AND object_id = OBJECT_ID('dbo.POTerms'))
    CREATE NONCLUSTERED INDEX IX_POTerms_MTransNo
        ON dbo.POTerms (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  38. POExtras  -  purchase order user-defined fields
      MTransNo -> POMst.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.POExtras', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.POExtras
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        MTransNo        INT           NULL,
        SlNo            NUMERIC(6,2)  NULL,
        Title           VARCHAR(30)   NULL,
        Description     VARCHAR(100)  NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_POExtras_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_POExtras_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_POExtras_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_POExtras PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.POExtras';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_POExtras_MTransNo' AND object_id = OBJECT_ID('dbo.POExtras'))
    CREATE NONCLUSTERED INDEX IX_POExtras_MTransNo
        ON dbo.POExtras (MTransNo, SlNo)
        INCLUDE (Description, Title)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  2.5  GOODS RECEIPT (GRN)
==============================================================================*/

/*------------------------------------------------------------------------------
  39. GRNMst  -  goods receipt note header (supplier bill / inward)

      TargetTransNo -> POMst.MTransNo   when TargetType = 'PO'
                    -> NULL             when TargetType = 'Direct'
      VendorNo      -> SupplierMaster.MTransNo

      GRNType is the stage marker for the whole inward flow:
          'N'  received, awaiting inward inspection
          'Q'  inspected - InspNo, QCDate, QCverifyBy stamped, line quantities
               split into AccQty / RejQty / RewQty / AODQty
          'C'  non-conformance closed - NCCloseDate, NCRemarks, NCCloseUserNo

      The three quantity-value pairs at the foot:
          BillValue    what the supplier's invoice says
          NetValue     what this GRN actually works out to
          DebitValue   BillValue - NetValue, when the supplier over-billed
          CreditValue  NetValue - BillValue, when the supplier under-billed
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.GRNMst', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNMst
    (
        MTransNo          INT IDENTITY(1,1) NOT NULL,
        DocSeries         CHAR(10)      NOT NULL,
        DocYear           INT           NOT NULL,
        DocNo             INT           NOT NULL,
        DocDate           DATE          NOT NULL,
        TargetTransNo     INT           NULL,
        TargetDocYear     INT           NULL,
        TargetDocNo       INT           NULL,
        TargetAmdNo       INT           NULL,
        VendorNo          INT           NOT NULL,
        VendorCode        VARCHAR(20)   NOT NULL,
        VendorName        VARCHAR(100)  NOT NULL,
        Address           VARCHAR(500)  NOT NULL,
        GSTIN             VARCHAR(20)   NOT NULL,
        StateName         VARCHAR(50)   NULL,
        PhoneNo           VARCHAR(30)   NULL,
        EmailID           VARCHAR(80)   NULL,
        RefNo             VARCHAR(50)   NULL,
        RefDate           DATE          NULL,
        InwardNo          VARCHAR(50)   NULL,
        InwardDate        DATE          NULL,
        LRNo              VARCHAR(50)   NULL,
        LRDate            DATE          NULL,
        DCNo              VARCHAR(50)   NULL,
        DCDate            DATE          NULL,
        DocNotes          VARCHAR(250)  NULL,
        DocStatus         CHAR(10)      NOT NULL,
        TaxType           CHAR(20)      NOT NULL,
        BasicValue        NUMERIC(16,2) NULL,
        ExemptValue       NUMERIC(16,2) NULL,
        TaxableValue      NUMERIC(16,2) NULL,
        PnFAmt            NUMERIC(16,2) NULL,
        FreightAmt        NUMERIC(16,2) NULL,
        IGSTAmt           NUMERIC(16,2) NULL,
        CGSTAmt           NUMERIC(16,2) NULL,
        SGSTAmt           NUMERIC(16,2) NULL,
        TDSP              NUMERIC(6,2)  NULL,
        TDSAmt            NUMERIC(16,2) NULL,
        TCSP              NUMERIC(6,2)  NULL,
        TCSAmt            NUMERIC(16,2) NULL,
        OtherCharges      VARCHAR(50)   NULL,
        OtherAmt          NUMERIC(16,2) NULL,
        RoundOff          NUMERIC(6,2)  NULL,
        NetValue          NUMERIC(18,2) NULL,
        DebitValue        NUMERIC(18,2) NULL,
        CreditValue       NUMERIC(18,2) NULL,
        TxnCurrency       VARCHAR(10)   NULL,
        CurrencyRate      NUMERIC(6,2)  NULL,
        CurrencyAmount    NUMERIC(16,2) NULL,
        VehicleNo         VARCHAR(20)   NULL,
        BillValue         NUMERIC(14,2) NULL,
        TargetType        VARCHAR(10)   NULL,
        GRNType           CHAR(1)       NULL CONSTRAINT DF_GRNMst_GRNType DEFAULT('N'),
        QCDate            DATE          NULL,
        QCverifyBy        VARCHAR(50)   NULL,
        InspNo            VARCHAR(50)   NULL,
        NCCloseDate       DATE          NULL,
        NCRemarks         VARCHAR(250)  NULL,
        NCCloseUserNo     INT           NULL,
        NCCloseAttachment VARCHAR(100)  NULL,
        SystemIP          VARCHAR(20)   NULL,
        CreatedBy         INT           NULL,
        CreatedOn         DATETIME2     NOT NULL CONSTRAINT DF_GRNMst_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy        INT           NULL,
        ModifiedOn        DATETIME2     NULL,
        DeleteStatus      CHAR(1)       NOT NULL CONSTRAINT DF_GRNMst_DeleteStatus DEFAULT('N'),
        DeleteReason      VARCHAR(250)  NULL,
        LockStatus        CHAR(1)       NOT NULL CONSTRAINT DF_GRNMst_LockStatus   DEFAULT('N'),
        LockCounter       INT           NULL,
        SubscID           INT           NULL,
        CONSTRAINT PK_GRNMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_GRNMst UNIQUE (DocSeries, DocYear, DocNo, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.GRNMst';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_DocNo' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_DocNo
        ON dbo.GRNMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, VendorNo, VendorName, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_DocDate' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_DocDate
        ON dbo.GRNMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus, VendorNo, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_VendorNo' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_VendorNo
        ON dbo.GRNMst (VendorNo, DocDate)
        INCLUDE (DocNo, DocStatus, NetValue)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_TargetTransNo' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_TargetTransNo
        ON dbo.GRNMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO

/*  The inspection and closure worklists select on GRNType, and the GRN's own
    list sorts on LockStatus.  One index serves all three screens.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_Stage' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_Stage
        ON dbo.GRNMst (SubscID, GRNType, LockStatus)
        INCLUDE (MTransNo, DocSeries, DocYear, DocNo, DocDate, VendorNo, VendorName,
                 InspNo, QCDate, NCCloseDate, CreatedBy)
        WHERE DeleteStatus = 'N';
GO

/*  Receipts against a given PO, for the balance-quantity subquery.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNMst_TargetDoc' AND object_id = OBJECT_ID('dbo.GRNMst'))
    CREATE NONCLUSTERED INDEX IX_GRNMst_TargetDoc
        ON dbo.GRNMst (SubscID, TargetType, TargetDocYear, TargetDocNo)
        INCLUDE (MTransNo, DocNo, DocDate)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  40. GRNDtl  -  GRN lines: billed / received / accepted / rejected quantity
      MTransNo -> GRNMst.MTransNo
      PartNo   -> PartMaster.MTransNo
      GradeNo  -> MaterialStd.MTransNo

      SlNo carries the PODtl.SlNo of the line being received when
      GRNMst.TargetType = 'PO'.  That is the join the PO balance is computed
      over, so it is never renumbered on a PO-backed GRN.  On a direct GRN it
      is simply the next line number.

      The quantity ladder, in the order the flow fills it in:
          BillQty   supplier invoiced         (GRN entry)
          RecQty    physically received       (GRN entry)
          AccQty    passed inspection         (inward inspection)
          RejQty    failed, to be returned    (inward inspection)
          RewQty    failed, to be reworked    (inward inspection)
          AODQty    accepted on deviation     (inward inspection)
      with AccQty + RejQty + RewQty + AODQty = RecQty once inspected.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.GRNDtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNDtl
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        DocNo           INT           NOT NULL,
        VendorNo        INT           NOT NULL,
        DocDate         DATE          NOT NULL,
        SlNo            NUMERIC(12,2) NULL,
        MTransNo        INT           NOT NULL,
        PartNo          INT           NOT NULL,
        PartCode        VARCHAR(30)   NULL,
        PartDescription VARCHAR(100)  NULL,
        ItemRemarks     VARCHAR(500)  NULL,
        HSNCode         VARCHAR(10)   NULL,
        BillQty         NUMERIC(12,4) NOT NULL,
        RecQty          NUMERIC(12,4) NOT NULL,
        AccQty          NUMERIC(12,4) NOT NULL,
        RejQty          NUMERIC(12,4) NOT NULL,
        RewQty          NUMERIC(12,4) NOT NULL,
        AODQty          NUMERIC(12,4) NOT NULL,
        Weight          NUMERIC(12,4) NOT NULL,
        Unit            VARCHAR(20)   NULL,
        RateType        VARCHAR(20)   NULL,
        Rate            NUMERIC(14,2) NOT NULL,
        BasicValue      NUMERIC(16,2) NOT NULL,
        DiscP           NUMERIC(6,2)  NULL,
        DiscAmt         NUMERIC(12,2) NULL,
        PnFP            NUMERIC(6,2)  NULL,
        PnFAmt          NUMERIC(14,2) NULL,
        FreightAmt      NUMERIC(14,2) NULL,
        TaxableValue    NUMERIC(14,2) NULL,
        GSTP            NUMERIC(6,2)  NULL,
        IGSTAmt         NUMERIC(14,2) NULL,
        CGSTAmt         NUMERIC(14,2) NULL,
        SGSTAmt         NUMERIC(14,2) NULL,
        TotalValue      NUMERIC(16,2) NULL,
        BarCode         VARCHAR(30)   NULL,
        ScanStatus      CHAR(1)       NULL,
        POQty           NUMERIC(12,2) NULL,
        LandingCost     NUMERIC(14,2) NULL,
        LandingValue    NUMERIC(16,2) NULL,
        ChassisNo       VARCHAR(30)   NULL,
        EngineNo        VARCHAR(30)   NULL,
        RegdNo          VARCHAR(30)   NULL,
        RejReason       VARCHAR(250)  NULL,
        Store           VARCHAR(50)   NULL,
        Location        VARCHAR(50)   NULL,
        OAYear          INT           NULL,
        OANo            INT           NULL,
        OAID            INT           NULL,
        Unit2           VARCHAR(30)   NULL,
        ConvQty         NUMERIC(14,4) NULL,
        GradeNo         INT           NULL,
        Grade           VARCHAR(50)   NULL,
        InspRemarks     VARCHAR(250)  NULL,
        PatternNo       INT           NULL,
        CoreGrade       VARCHAR(50)   NULL,
        Spare           VARCHAR(50)   NULL,
        SpareGrade      VARCHAR(50)   NULL,
        SpareQty        NUMERIC(12,2) NULL,
        PLoosQty        NUMERIC(12,2) NULL,
        CoreQty         NUMERIC(12,2) NULL,
        CLooseQty       NUMERIC(12,2) NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_GRNDtl_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_GRNDtl_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_GRNDtl_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_GRNDtl PRIMARY KEY CLUSTERED (TransSlNo)
        -- The line-number rule is a filtered unique index, UQ_GRNDtl_Line
        -- below, not a table constraint.  See "TWO DEVIATIONS" in the banner.
    );
    PRINT 'Created dbo.GRNDtl';
END
GO

/*  One live line per line number per GRN.

    This cannot be a UNIQUE CONSTRAINT with DeleteStatus in the key, the way
    the masters do it.  On a PO-backed GRN the line number is the PO line's
    number, so deleting a line and re-adding it reuses the number - and a
    second delete would then collide with the first deleted row.  Filtering on
    DeleteStatus = 'N' states the rule that is actually meant: two deleted
    copies of the same line are history, not a duplicate.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_GRNDtl_Line' AND object_id = OBJECT_ID('dbo.GRNDtl'))
    CREATE UNIQUE NONCLUSTERED INDEX UQ_GRNDtl_Line
        ON dbo.GRNDtl (SubscID, MTransNo, SlNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNDtl_MTransNo' AND object_id = OBJECT_ID('dbo.GRNDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNDtl_MTransNo
        ON dbo.GRNDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, PartDescription, Unit, Rate)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNDtl_PartNo' AND object_id = OBJECT_ID('dbo.GRNDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNDtl_PartNo
        ON dbo.GRNDtl (PartNo)
        INCLUDE (MTransNo, Unit, Rate, Grade)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNDtl_VendorNo' AND object_id = OBJECT_ID('dbo.GRNDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNDtl_VendorNo
        ON dbo.GRNDtl (VendorNo)
        INCLUDE (MTransNo, PartNo, Rate)
        WHERE DeleteStatus = 'N';
GO

/*  Store allocation lists "received but not yet binned" across all open GRNs.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNDtl_Store' AND object_id = OBJECT_ID('dbo.GRNDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNDtl_Store
        ON dbo.GRNDtl (SubscID, Store, Location)
        INCLUDE (MTransNo, PartNo, PartCode, AccQty, AODQty)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  41. GRNNCDtl  -  GRN non-conformance lines
      MTransNo     -> GRNMst.MTransNo
      GRNTransSlNo -> GRNDtl.TransSlNo   (the receipt line the NC is raised on)
      PartNo       -> PartMaster.MTransNo

      NCType is 'Reject', 'Rework' or 'AOD'.  Inward inspection will not post a
      GRN line carrying RejQty / RewQty / AODQty unless the matching NC lines
      exist here and their NCQty adds up to that quantity.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.GRNNCDtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNNCDtl
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        SlNo            NUMERIC(12,2) NULL,
        MTransNo        INT           NOT NULL,
        PartNo          INT           NOT NULL,
        GradeNo         INT           NULL,
        Grade           VARCHAR(50)   NULL,
        NCType          VARCHAR(10)   NULL,
        HeatNo          VARCHAR(10)   NULL,
        NCRemarks       VARCHAR(500)  NULL,
        NCQty           NUMERIC(12,4) NOT NULL,
        Unit            VARCHAR(20)   NULL,
        GRNTransSlNo    INT           NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_GRNNCDtl_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_GRNNCDtl_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_GRNNCDtl_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_GRNNCDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.GRNNCDtl';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNNCDtl_MTransNo' AND object_id = OBJECT_ID('dbo.GRNNCDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNNCDtl_MTransNo
        ON dbo.GRNNCDtl (MTransNo, SlNo)
        INCLUDE (PartNo, Unit, Grade)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNNCDtl_PartNo' AND object_id = OBJECT_ID('dbo.GRNNCDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNNCDtl_PartNo
        ON dbo.GRNNCDtl (PartNo)
        INCLUDE (MTransNo, Unit, Grade)
        WHERE DeleteStatus = 'N';
GO

/*  Inspection posting asks, per receipt line and NC type, "does the NC quantity
    add up?".  This index answers it without touching the table.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNNCDtl_GRNLine' AND object_id = OBJECT_ID('dbo.GRNNCDtl'))
    CREATE NONCLUSTERED INDEX IX_GRNNCDtl_GRNLine
        ON dbo.GRNNCDtl (GRNTransSlNo, NCType)
        INCLUDE (NCQty, HeatNo, NCRemarks)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  42. GRNQRCode  -  GRN QR / barcode tracking per received line
      MTransNo  -> GRNMst.MTransNo
      GRNSlNo   -> GRNDtl.TransSlNo
      POTransNo -> POMst.MTransNo
      PartNo    -> PartMaster.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.GRNQRCode', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNQRCode
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        MTransNo        INT           NOT NULL,
        GRNSlNo         INT           NULL,
        POTransNo       INT           NULL,
        LinkNo          INT           NULL,
        PartNo          INT           NULL,
        Qty             NUMERIC(12,2) NULL,
        LQty            INT           NULL,
        OAYear          INT           NULL,
        OANo            INT           NULL,
        OAID            INT           NULL,
        QRCode          VARCHAR(500)  NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_GRNQRCode_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_GRNQRCode_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_GRNQRCode_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_GRNQRCode PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.GRNQRCode';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNQRCode_MTransNo' AND object_id = OBJECT_ID('dbo.GRNQRCode'))
    CREATE NONCLUSTERED INDEX IX_GRNQRCode_MTransNo
        ON dbo.GRNQRCode (MTransNo)
        INCLUDE (PartNo, Qty)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNQRCode_PartNo' AND object_id = OBJECT_ID('dbo.GRNQRCode'))
    CREATE NONCLUSTERED INDEX IX_GRNQRCode_PartNo
        ON dbo.GRNQRCode (PartNo)
        INCLUDE (MTransNo, Qty)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  43. GRNAttachment  -  GRN attachments
      MTransNo -> GRNMst.MTransNo

      AttFile holds the path the file was stored under, relative to the API's
      web root - the bytes live on disk, not in the row.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.GRNAttachment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GRNAttachment
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        MTransNo        INT           NULL,
        SlNo            NUMERIC(6,2)  NULL,
        AttNotes        VARCHAR(250)  NULL,
        AttFile         VARCHAR(250)  NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_GRNAttachment_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_GRNAttachment_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_GRNAttachment_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_GRNAttachment PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.GRNAttachment';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GRNAttachment_MTransNo' AND object_id = OBJECT_ID('dbo.GRNAttachment'))
    CREATE NONCLUSTERED INDEX IX_GRNAttachment_MTransNo
        ON dbo.GRNAttachment (MTransNo, SlNo)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  1.4  HEAT / LOT TRACEABILITY  -  the Heat No. Entry screen

  A heat document is raised AGAINST a GRN: TargetTransNo -> GRNMst.MTransNo.
  The header names the part and grade and the total quantity covered; each
  detail line is one heat number with its test certificate; HeatRefNo expands a
  line into the individual piece serial numbers it covers.
==============================================================================*/

/*------------------------------------------------------------------------------
  15. HeatNoMst  -  heat number header
      TargetTransNo -> GRNMst.MTransNo
      PartNo        -> PartMaster.MTransNo
      GradeNo       -> MaterialStd.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.HeatNoMst', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HeatNoMst
    (
        MTransNo        INT IDENTITY(1,1) NOT NULL,
        DocSeries       CHAR(10)      NOT NULL,
        DocYear         INT           NOT NULL,
        DocNo           INT           NOT NULL,
        DocDate         DATE          NOT NULL,
        TargetTransNo   INT           NULL,
        TargetDocYear   INT           NULL,
        TargetDocNo     INT           NULL,
        PartNo          INT           NOT NULL,
        PartCode        VARCHAR(50)   NULL,
        PartDescription VARCHAR(250)  NULL,
        GradeNo         INT           NULL,
        Grade           VARCHAR(50)   NULL,
        Qty             NUMERIC(12,6) NULL,
        Unit            VARCHAR(20)   NULL,
        DocNotes        VARCHAR(250)  NULL,
        DocStatus       CHAR(10)      NOT NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_HeatNoMst_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_HeatNoMst_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_HeatNoMst_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_HeatNoMst PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_HeatNoMst UNIQUE (DocSeries, DocYear, DocNo, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.HeatNoMst';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoMst_DocNo' AND object_id = OBJECT_ID('dbo.HeatNoMst'))
    CREATE NONCLUSTERED INDEX IX_HeatNoMst_DocNo
        ON dbo.HeatNoMst (SubscID, DocSeries, DocYear, DocNo)
        INCLUDE (DocDate, DocStatus, DocNotes, PartNo, Qty)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoMst_DocDate' AND object_id = OBJECT_ID('dbo.HeatNoMst'))
    CREATE NONCLUSTERED INDEX IX_HeatNoMst_DocDate
        ON dbo.HeatNoMst (SubscID, DocDate)
        INCLUDE (DocNo, DocSeries, DocStatus)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoMst_PartNo' AND object_id = OBJECT_ID('dbo.HeatNoMst'))
    CREATE NONCLUSTERED INDEX IX_HeatNoMst_PartNo
        ON dbo.HeatNoMst (PartNo)
        INCLUDE (DocNo, DocDate, Qty, Unit, Grade)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoMst_TargetTransNo' AND object_id = OBJECT_ID('dbo.HeatNoMst'))
    CREATE NONCLUSTERED INDEX IX_HeatNoMst_TargetTransNo
        ON dbo.HeatNoMst (TargetTransNo)
        INCLUDE (DocNo, DocDate, DocStatus)
        WHERE DeleteStatus = 'N' AND TargetTransNo IS NOT NULL;
GO


/*------------------------------------------------------------------------------
  16. HeatNoDtl  -  heat number lines, one per heat with its test certificate
      MTransNo -> HeatNoMst.MTransNo
      PartNo   -> PartMaster.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.HeatNoDtl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HeatNoDtl
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        SlNo            NUMERIC(12,2) NULL,
        MTransNo        INT           NOT NULL,
        PartNo          INT           NOT NULL,
        PartCode        VARCHAR(50)   NULL,
        PartDescription VARCHAR(250)  NULL,
        GradeNo         INT           NULL,
        Grade           VARCHAR(50)   NULL,
        ItemRemarks     VARCHAR(500)  NULL,
        Qty             NUMERIC(12,4) NOT NULL,
        Unit            VARCHAR(10)   NULL,
        HeatNo          VARCHAR(50)   NULL,
        TCNo            VARCHAR(30)   NULL,
        TCDate          DATE          NULL,
        TCFile          VARCHAR(100)  NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_HeatNoDtl_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_HeatNoDtl_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_HeatNoDtl_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_HeatNoDtl PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.HeatNoDtl';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoDtl_MTransNo' AND object_id = OBJECT_ID('dbo.HeatNoDtl'))
    CREATE NONCLUSTERED INDEX IX_HeatNoDtl_MTransNo
        ON dbo.HeatNoDtl (MTransNo, SlNo)
        INCLUDE (PartNo, PartCode, PartDescription, Qty, Unit)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoDtl_PartNo' AND object_id = OBJECT_ID('dbo.HeatNoDtl'))
    CREATE NONCLUSTERED INDEX IX_HeatNoDtl_PartNo
        ON dbo.HeatNoDtl (PartNo)
        INCLUDE (MTransNo, Qty, Unit, Grade)
        WHERE DeleteStatus = 'N';
GO

/*  "Has this heat number already been entered for this part and grade?" is
    asked on every keystroke of the heat-number field.  */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatNoDtl_HeatNo' AND object_id = OBJECT_ID('dbo.HeatNoDtl'))
    CREATE NONCLUSTERED INDEX IX_HeatNoDtl_HeatNo
        ON dbo.HeatNoDtl (SubscID, HeatNo, PartNo)
        INCLUDE (MTransNo, TransSlNo, Grade, Qty, TCNo)
        WHERE DeleteStatus = 'N';
GO


/*------------------------------------------------------------------------------
  17. HeatRefNo  -  the individual piece serial numbers a heat line covers
      MTransNo   -> HeatNoMst.MTransNo
      HTransSlNo -> HeatNoDtl.TransSlNo
      PartNo     -> PartMaster.MTransNo
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.HeatRefNo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HeatRefNo
    (
        TransSlNo       INT IDENTITY(1,1) NOT NULL,
        MTransNo        INT           NOT NULL,
        HTransSlNo      INT           NOT NULL,
        RefNo           INT           NOT NULL,
        PartNo          INT           NOT NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_HeatRefNo_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_HeatRefNo_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_HeatRefNo_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_HeatRefNo PRIMARY KEY CLUSTERED (TransSlNo)
    );
    PRINT 'Created dbo.HeatRefNo';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatRefNo_MTransNo' AND object_id = OBJECT_ID('dbo.HeatRefNo'))
    CREATE NONCLUSTERED INDEX IX_HeatRefNo_MTransNo
        ON dbo.HeatRefNo (MTransNo)
        INCLUDE (PartNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatRefNo_PartNo' AND object_id = OBJECT_ID('dbo.HeatRefNo'))
    CREATE NONCLUSTERED INDEX IX_HeatRefNo_PartNo
        ON dbo.HeatRefNo (PartNo)
        INCLUDE (MTransNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HeatRefNo_HTransSlNo' AND object_id = OBJECT_ID('dbo.HeatRefNo'))
    CREATE NONCLUSTERED INDEX IX_HeatRefNo_HTransSlNo
        ON dbo.HeatRefNo (HTransSlNo, RefNo)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  2.9  STORE  -  the bin list the Store Allocation screen picks from
==============================================================================*/

/*------------------------------------------------------------------------------
  63. StoreLocation  -  store / rack location mapping per part
      PartNo -> PartMaster.MTransNo

      A row with PartNo = 0 is a bin that any part may be allocated to; a row
      with a real PartNo is that part's preferred bin, offered first on the
      allocation screen.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.StoreLocation', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StoreLocation
    (
        MTransNo        INT IDENTITY(1,1) NOT NULL,
        SLNo            NUMERIC(12,2) NULL,
        PartNo          INT           NOT NULL,
        PartCode        VARCHAR(30)   NULL,
        PartDescription VARCHAR(100)  NULL,
        Store           VARCHAR(50)   NULL,
        Location        VARCHAR(50)   NULL,
        SystemIP        VARCHAR(20)   NULL,
        CreatedBy       INT           NULL,
        CreatedOn       DATETIME2     NOT NULL CONSTRAINT DF_StoreLocation_CreatedOn    DEFAULT(GETDATE()),
        ModifiedBy      INT           NULL,
        ModifiedOn      DATETIME2     NULL,
        DeleteStatus    CHAR(1)       NOT NULL CONSTRAINT DF_StoreLocation_DeleteStatus DEFAULT('N'),
        DeleteReason    VARCHAR(250)  NULL,
        LockStatus      CHAR(1)       NOT NULL CONSTRAINT DF_StoreLocation_LockStatus   DEFAULT('N'),
        LockCounter     INT           NULL,
        SubscID         INT           NULL,
        CONSTRAINT PK_StoreLocation PRIMARY KEY CLUSTERED (MTransNo),
        CONSTRAINT UQ_StoreLocation UNIQUE (PartNo, Store, Location, DeleteStatus, SubscID)
    );
    PRINT 'Created dbo.StoreLocation';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StoreLocation_PartNo' AND object_id = OBJECT_ID('dbo.StoreLocation'))
    CREATE NONCLUSTERED INDEX IX_StoreLocation_PartNo
        ON dbo.StoreLocation (PartNo)
        WHERE DeleteStatus = 'N';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StoreLocation_Key' AND object_id = OBJECT_ID('dbo.StoreLocation'))
    CREATE NONCLUSTERED INDEX IX_StoreLocation_Key
        ON dbo.StoreLocation (SubscID, PartNo, Store, Location)
        INCLUDE (SLNo, PartCode, PartDescription)
        WHERE DeleteStatus = 'N';
GO


/*==============================================================================
  VIEWS

  Both are read paths the screens hit constantly, and both encode a join that is
  easy to get subtly wrong by hand - so they live here once rather than in every
  caller.
==============================================================================*/

/*------------------------------------------------------------------------------
  VW_POPendingGRN  -  PO lines with something still to receive

  "Ordered minus everything already billed against this PO line."  The GRN
  screen uses it twice: to list the POs worth opening, and to seed the lines of
  a new GRN.

  A PO line is in scope only when its header is the live revision
  (DocAmdStatus = 'N') and approved (LockStatus = 'Y') - an unapproved or
  superseded revision must not be receivable.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.VW_POPendingGRN', 'V') IS NOT NULL
    DROP VIEW dbo.VW_POPendingGRN;
GO

CREATE VIEW dbo.VW_POPendingGRN
AS
SELECT  PM.MTransNo        AS POTransNo,
        PM.DocSeries       AS PODocSeries,
        PM.DocYear         AS PODocYear,
        PM.DocNo           AS PODocNo,
        PM.DocDate         AS PODocDate,
        PM.DocAmdNo        AS PODocAmdNo,
        PM.TargetType,
        PM.VendorNo,
        PM.VendorCode,
        PM.VendorName,
        PM.TaxType,
        PM.TxnCurrency,
        PM.SubscID,
        PD.TransSlNo       AS POTransSlNo,
        PD.SlNo            AS LinkNo,
        PD.PartNo,
        PD.PartCode,
        PD.PartDescription,
        PD.ItemRemarks,
        PD.HSNCode,
        PD.GradeNo,
        PD.Grade,
        PD.MtrlStd,
        PD.CoreGrade,
        PD.Spare,
        PD.SpareGrade,
        PD.SpareQty,
        PD.DeleveryDate,
        PD.OAYear,
        PD.OANo,
        PD.OAID,
        PD.Unit,
        PD.Unit2,
        PD.ConvRatio,
        PD.Rate,
        PD.DiscP,
        PD.PnFP,
        PD.GSTP,
        PD.Qty             AS POQty,
        ISNULL(R.BilledQty, 0) AS BilledQty,
        PD.Qty - ISNULL(R.BilledQty, 0) AS BalanceQty,
        CASE WHEN PD.Qty > 0 AND PD.FreightAmt > 0
             THEN PD.FreightAmt / PD.Qty ELSE 0 END AS FreightRate
  FROM  dbo.PODtl PD
  JOIN  dbo.POMst PM
    ON  PM.MTransNo = PD.MTransNo
 OUTER APPLY
       (
           SELECT SUM(GD.BillQty) AS BilledQty
             FROM dbo.GRNDtl GD
             JOIN dbo.GRNMst GM ON GM.MTransNo = GD.MTransNo
            WHERE GD.DeleteStatus = 'N'
              AND GM.DeleteStatus = 'N'
              AND GM.TargetType   = 'PO'
              AND GM.TargetTransNo = PM.MTransNo
              AND GD.SlNo          = PD.SlNo
       ) R
 WHERE  PD.DeleteStatus = 'N'
   AND  PM.DeleteStatus = 'N'
   AND  PM.DocAmdStatus = 'N'
   AND  PM.LockStatus   = 'Y';
GO

/*------------------------------------------------------------------------------
  VW_GRNInwardStatus  -  one row per GRN with its stage and its NC totals

  Drives the three inward worklists (receipt, inspection, closure) from a single
  read, so they cannot disagree about which stage a GRN is at.
------------------------------------------------------------------------------*/
IF OBJECT_ID('dbo.VW_GRNInwardStatus', 'V') IS NOT NULL
    DROP VIEW dbo.VW_GRNInwardStatus;
GO

CREATE VIEW dbo.VW_GRNInwardStatus
AS
SELECT  GM.MTransNo,
        GM.DocSeries,
        GM.DocYear,
        GM.DocNo,
        GM.DocDate,
        GM.TargetType,
        GM.TargetTransNo,
        GM.TargetDocYear,
        GM.TargetDocNo,
        GM.VendorNo,
        GM.VendorCode,
        GM.VendorName,
        GM.RefNo,
        GM.RefDate,
        GM.InwardNo,
        GM.InwardDate,
        GM.DCNo,
        GM.DCDate,
        GM.LRNo,
        GM.LRDate,
        GM.BillValue,
        GM.NetValue,
        GM.DebitValue,
        GM.CreditValue,
        GM.GRNType,
        GM.InspNo,
        GM.QCDate,
        GM.QCverifyBy,
        GM.NCCloseDate,
        GM.NCCloseUserNo,
        GM.NCRemarks,
        GM.NCCloseAttachment,
        GM.LockStatus,
        GM.CreatedBy,
        GM.CreatedOn,
        GM.ModifiedBy,
        GM.ModifiedOn,
        GM.SubscID,
        CASE GM.GRNType
             WHEN 'N' THEN 'Received'
             WHEN 'Q' THEN 'Inspected'
             WHEN 'C' THEN 'NC Closed'
             ELSE 'Received'
        END AS StageName,
        ISNULL(L.LineCount, 0) AS LineCount,
        ISNULL(L.BillQty,   0) AS TotalBillQty,
        ISNULL(L.RecQty,    0) AS TotalRecQty,
        ISNULL(L.AccQty,    0) AS TotalAccQty,
        ISNULL(L.RejQty,    0) AS TotalRejQty,
        ISNULL(L.RewQty,    0) AS TotalRewQty,
        ISNULL(L.AODQty,    0) AS TotalAODQty,
        ISNULL(L.UnallocatedLines, 0) AS UnallocatedLines,
        ISNULL(N.NCLines,   0) AS NCLines,
        ISNULL(N.NCQty,     0) AS TotalNCQty
  FROM  dbo.GRNMst GM
 OUTER APPLY
       (
           SELECT COUNT(*)        AS LineCount,
                  SUM(GD.BillQty) AS BillQty,
                  SUM(GD.RecQty)  AS RecQty,
                  SUM(GD.AccQty)  AS AccQty,
                  SUM(GD.RejQty)  AS RejQty,
                  SUM(GD.RewQty)  AS RewQty,
                  SUM(GD.AODQty)  AS AODQty,
                  SUM(CASE WHEN NULLIF(LTRIM(RTRIM(GD.Store)), '') IS NULL THEN 1 ELSE 0 END)
                                  AS UnallocatedLines
             FROM dbo.GRNDtl GD
            WHERE GD.MTransNo = GM.MTransNo AND GD.DeleteStatus = 'N'
       ) L
 OUTER APPLY
       (
           SELECT COUNT(*)       AS NCLines,
                  SUM(NC.NCQty)  AS NCQty
             FROM dbo.GRNNCDtl NC
            WHERE NC.MTransNo = GM.MTransNo AND NC.DeleteStatus = 'N'
       ) N
 WHERE  GM.DeleteStatus = 'N';
GO


/*==============================================================================
  AUDIT TRAIL

  SP_AuditLog_Sync registers every eligible new table in AuditConfig and builds
  its trigger.  It never overwrites a row already there, so the ModuleName
  corrections below are applied after the sync and then survive later runs.
==============================================================================*/
DECLARE @AuditMsg NVARCHAR(200);
EXEC dbo.SP_AuditLog_Sync @Message = @AuditMsg OUTPUT;
PRINT 'Audit sync: ' + ISNULL(@AuditMsg, '(no message)');
GO

UPDATE dbo.AuditConfig SET ModuleName = 'Purchase Order'      WHERE TableName IN ('POMst', 'PODtl', 'POTerms', 'POExtras');
UPDATE dbo.AuditConfig SET ModuleName = 'Goods Receipt Note'  WHERE TableName IN ('GRNMst', 'GRNDtl', 'GRNNCDtl', 'GRNQRCode', 'GRNAttachment');
UPDATE dbo.AuditConfig SET ModuleName = 'Heat No Entry'       WHERE TableName IN ('HeatNoMst', 'HeatNoDtl', 'HeatRefNo');
UPDATE dbo.AuditConfig SET ModuleName = 'Store Location'      WHERE TableName  = 'StoreLocation';
GO

/*  The Dtl/Mst naming rule in SP_AuditLog_Sync catches PODtl, GRNDtl, GRNNCDtl
    and HeatNoDtl, but not the children whose names do not end in "Dtl".  Point
    those at their parents by hand so the trail nests correctly.  */
UPDATE dbo.AuditConfig SET ParentTable = 'POMst',     ParentKeyColumn = 'MTransNo' WHERE TableName IN ('POTerms', 'POExtras');
UPDATE dbo.AuditConfig SET ParentTable = 'GRNMst',    ParentKeyColumn = 'MTransNo' WHERE TableName IN ('GRNQRCode', 'GRNAttachment');
UPDATE dbo.AuditConfig SET ParentTable = 'HeatNoMst', ParentKeyColumn = 'MTransNo' WHERE TableName  = 'HeatRefNo';
GO

/*  GRNNCDtl's parent is the GRN header, not a "GRNNCMst" that does not exist.  */
UPDATE dbo.AuditConfig SET ParentTable = 'GRNMst', ParentKeyColumn = 'MTransNo' WHERE TableName = 'GRNNCDtl';
GO

PRINT '13_PurchaseOrderGRNTables.sql completed.';
GO
