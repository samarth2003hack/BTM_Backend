-- ============================================================
-- Util: Delete ALL transaction data (Sales, Purchase, Stores)
-- Masters, users, menus, config and company setup are KEPT.
-- Identity counters are reset so new documents start from 1.
-- WARNING: irreversible. Take a backup first:
--   BACKUP DATABASE EMSSoft TO DISK = 'D:\Backup\EMSSoft_BeforeClear.bak' WITH INIT;
-- ============================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Tables TABLE (Seq INT IDENTITY(1,1), Name SYSNAME);

-- Ordered child-first so foreign keys are never violated
INSERT INTO @Tables (Name) VALUES
-- Sales: Enquiry -> Quotation -> OA -> Proforma / Packing / Invoice
 ('OAAttachment'),('OADtl'),('OAExtras'),('OANotes'),('OATerms'),('OAMst'),
 ('QtnAttachment'),('QtnDtl'),('QtnExtras'),('QtnFollowUp'),('QTNNotes'),('QtnTerms'),('QtnMst'),
 ('EnqDtl'),('EnqFollowUp'),('ENQFeasibilityMst'),('EnqMst'),
 ('LeadGeneration'),
 ('ProformaDtl'),('ProformaExtra'),('ProformaTerms'),('ProformaMst'),
 ('PackingSlipDtl'),('PackingSlipMst'),
 ('EinvDtl'),
 ('STxnDtl'),('STxnExtra'),('StxnTerms'),('STxnMst'),
-- Purchase: PR -> RFQ -> Quote -> PO
 ('RFQPriceDtl'),('RFQPriceTerms'),('RFQPriceMst'),
 ('RFQDtl'),('RFQSupplier'),('RFQTerms'),('RFQMst'),
 ('PurReqDtl'),('PurReqMst'),
 ('PODtl'),('POExtras'),('POTerms'),('POMst'),
-- Stores / Inward
 ('GRNAttachment'),('GRNNCDtl'),('GRNQRCode'),('GRNDtl'),('GRNMst'),
 ('HeatNoDtl'),('HeatRefNo'),('HeatNoMst'),
 ('RGPInwDtl'),('RGPInwMst'),('RGPDtl'),('RGPTerms'),('RGPMst'),
 ('MRNDtl'),('MRNMst'),
 ('StoreIssueDtl'),('StoreIssueMst'),
 ('StoreReturnsDtl'),('StoreReturnsMst');

-- Optional: also clear logs (uncomment to include)
-- INSERT INTO @Tables (Name) VALUES ('AuditLogDtl'),('AuditLog'),('AccessLog'),('ErrorLog');

BEGIN TRAN;

DECLARE @Seq INT = 1, @Max INT, @Name SYSNAME, @Sql NVARCHAR(MAX);
SELECT @Max = MAX(Seq) FROM @Tables;

WHILE @Seq <= @Max
BEGIN
    SELECT @Name = Name FROM @Tables WHERE Seq = @Seq;

    IF OBJECT_ID(N'dbo.' + QUOTENAME(@Name), N'U') IS NOT NULL
    BEGIN
        SET @Sql = N'DELETE FROM dbo.' + QUOTENAME(@Name) + N';';
        EXEC sp_executesql @Sql;
        PRINT CONCAT(@Name, ': ', @@ROWCOUNT, ' row(s) deleted');

        -- Reset identity only if it has been used (avoids starting a fresh table at 0)
        IF EXISTS (SELECT 1 FROM sys.identity_columns
                   WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@Name))
                     AND last_value IS NOT NULL)
            DBCC CHECKIDENT (@Name, RESEED, 0) WITH NO_INFOMSGS;
    END

    SET @Seq += 1;
END

COMMIT TRAN;
PRINT 'All transaction data deleted.';
