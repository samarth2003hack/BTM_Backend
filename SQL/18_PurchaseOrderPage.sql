/*==============================================================================
  18. PURCHASE ORDER PAGE  -  the data the live Purchase Order screen needs

  PurchaseOrder.html has moved off its in-page mock data and onto
  PurchaseOrderController (/api/PurchaseOrder/...). The tables, procedures and
  views it runs on already exist (13_ / 14_ scripts), so NO SCHEMA CHANGE is
  needed. What was missing is data:

    1. The Extras configuration. The Extras tab lists one fixed row per field
       with a dropdown of allowed values. It is kept in dbo.StandardTerms under
       TermType 'POEXTRAS' (read by GET /api/StandardTerms/GetDropdown/POEXTRAS):
           TermTitle        the field name        (max 30 chars - POExtras.Title)
           TermDescription  allowed values, '~'-delimited
       The four rows below are STARTING VALUES - edit them to suit.

    2. A sidebar entry for the page, plus the AccessTemplate grants without
       which a Menulist row is invisible (see 17_TransactionsMenu.sql).

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - the live menu rows for the other purchase screens point at
    'Modules/Transactions/', but on the dev box the pages are in
    'EMSSoft/Modules/Purchase/'. Set @Folder to wherever the deployed server
    actually serves them from.
  * @SubscID - the subscription the Extras rows belong to.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops row 706 again, the same as 701-705.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. EXTRAS CONFIGURATION  (StandardTerms, TermType 'POEXTRAS')

  LockStatus 'Y' is required: GetDropdown only returns approved rows. A field
  that already exists (same subscription + title) is left untouched, so values
  edited after the first run are never overwritten.
------------------------------------------------------------------------------*/
DECLARE @SubscID INT = 1;

DECLARE @Extras TABLE (TermSlNo NUMERIC(6,2), TermTitle VARCHAR(30), TermDescription VARCHAR(250));
INSERT INTO @Extras (TermSlNo, TermTitle, TermDescription)
VALUES
    (1, 'Packing',          'Standard~Export Worthy~Wooden Box'),
    (2, 'Mode of Dispatch', 'By Road~By Air~By Sea~Courier'),
    (3, 'Inspection',       'At Works~At Site~Third Party~Waived'),
    (4, 'Insurance',        'Buyer Scope~Supplier Scope~Not Applicable');

INSERT INTO dbo.StandardTerms
       (TermType, TermSlNo, TermTitle, TermDescription, SystemIP, CreatedBy,
        CreatedOn, DeleteStatus, LockStatus, LockCounter, SubscID)
SELECT 'POEXTRAS', E.TermSlNo, E.TermTitle, E.TermDescription, NULL, NULL,
       SYSDATETIME(), 'N', 'Y', 1, @SubscID
  FROM @Extras E
 WHERE NOT EXISTS (SELECT 1 FROM dbo.StandardTerms S
                    WHERE S.TermType = 'POEXTRAS'
                      AND S.SubscID  = @SubscID
                      AND S.TermTitle = E.TermTitle
                      AND S.DeleteStatus = 'N');
GO

/*------------------------------------------------------------------------------
  2. SIDEBAR ENTRY  +  GRANTS

  MTransNo 706 is unused (the table runs to 705). It sits after RFQ Approval,
  because that is where the process goes next: the approved quote becomes the
  order.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/Transactions/';   -- <- check, see header

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
VALUES (706, 'Transactions', 5, 'Purchase Order', 6, 'bi-bag-check', @Folder + 'PurchaseOrder.html');

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
-- Every template that already holds grants gets full access to the new module,
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

-- What was written.
SELECT TermSlNo, TermTitle, TermDescription, LockStatus, SubscID
  FROM dbo.StandardTerms
 WHERE TermType = 'POEXTRAS' AND DeleteStatus = 'N'
 ORDER BY SubscID, TermSlNo;

SELECT M.MTransNo, M.Section, M.ModuleName, M.MSLNo, M.ModulePage,
       (SELECT COUNT(*) FROM dbo.AccessTemplate A WHERE A.ModuleNo = M.MTransNo) AS Grants
  FROM dbo.Menulist M
 WHERE M.MTransNo = 706;
GO
