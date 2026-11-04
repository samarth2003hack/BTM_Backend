/*==============================================================================
  26. FIX SALES TARGET FOREIGN KEYS

  Two constraints were created pointing at their OWN table instead of the
  upstream document their names describe:

      FK_QtnMst_EnqMst   QtnMst.TargetTransNo -> QtnMst.MTransNo   (should be EnqMst)
      FK_OAMst_QtnMst    OAMst.TargetTransNo  -> OAMst.MTransNo    (should be QtnMst)

  With them in place a quotation cannot be raised against an enquiry, nor an
  order acceptance against a quotation - the insert fails unless the enquiry's
  MTransNo happens to equal an existing quotation's. Each is dropped and
  recreated against the intended table. TargetTransNo stays nullable, so a
  document raised without an upstream reference is unaffected.

  Recreating the constraint validates existing rows: if any TargetTransNo does
  not match a row in the upstream table, the ADD fails and nothing is changed
  for that constraint - clean the row up first.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys
            WHERE name = 'FK_QtnMst_EnqMst'
              AND referenced_object_id <> OBJECT_ID(N'dbo.EnqMst'))
    ALTER TABLE dbo.QtnMst DROP CONSTRAINT FK_QtnMst_EnqMst;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_QtnMst_EnqMst')
    ALTER TABLE dbo.QtnMst WITH CHECK
      ADD CONSTRAINT FK_QtnMst_EnqMst FOREIGN KEY (TargetTransNo) REFERENCES dbo.EnqMst (MTransNo);
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys
            WHERE name = 'FK_OAMst_QtnMst'
              AND referenced_object_id <> OBJECT_ID(N'dbo.QtnMst'))
    ALTER TABLE dbo.OAMst DROP CONSTRAINT FK_OAMst_QtnMst;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_OAMst_QtnMst')
    ALTER TABLE dbo.OAMst WITH CHECK
      ADD CONSTRAINT FK_OAMst_QtnMst FOREIGN KEY (TargetTransNo) REFERENCES dbo.QtnMst (MTransNo);
GO

-- What is in place now.
SELECT fk.name, OBJECT_NAME(fk.parent_object_id) AS ParentTable,
       OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable
  FROM sys.foreign_keys fk
 WHERE fk.name IN ('FK_QtnMst_EnqMst', 'FK_OAMst_QtnMst');
GO
