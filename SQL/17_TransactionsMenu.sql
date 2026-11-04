/*==============================================================================
  17. TRANSACTIONS MENU  -  sidebar entries for the purchase chain

  The five purchase screens live in EMSSoft/Modules/Transactions/ but had no
  rows in dbo.Menulist, so the sidebar had no way to reach them. This adds a
  Section 'Transactions' (SSLNo 5), following the same folder -> Section rule
  11_MenulistFolders.sql uses:

      Modules/Masters/         -> 'Masters'         SSLNo 1
      Modules/Administration/  -> 'Administration'  SSLNo 2
      Modules/Accounts/        -> 'Accounts'        SSLNo 3
      Modules/Utilities/       -> 'Utilities'       SSLNo 4
      Modules/Transactions/    -> 'Transactions'    SSLNo 5   <- this script

  The modules are ordered the way the process runs, not alphabetically:

      1  Purchase Requisition   what is needed
      2  Request For Quote      who is being asked
      3  RFQ Price Update       what they quoted
      4  Price Comparison       the quotes side by side
      5  RFQ Approval           who won

  All five .html files exist in that folder, so no link lands on a 404.

  ------------------------------------------------------------------------------
  MTransNo
  ------------------------------------------------------------------------------
  Hand-assigned, and the value AccessTemplate.ModuleNo points at. 701-705 are
  unused (the table runs to 602), so no existing grant changes meaning.

  ------------------------------------------------------------------------------
  CAREFUL: 11_MenulistFolders.sql WILL REMOVE THESE
  ------------------------------------------------------------------------------
  That script rebuilds Category 'EMS' from its own @Menu table and deletes
  anything under that category it does not list. Re-running it after this one
  drops these five rows again. If the Transactions section is meant to be
  permanent, move these rows into 11_MenulistFolders.sql's @Menu table instead
  of keeping them here.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Category VARCHAR(50) = 'EMS';

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
    (701, 'Transactions', 5, 'Purchase Requisition', 1, 'bi-clipboard-check', 'Modules/Transactions/PurchaseRequisition.html'),
    (702, 'Transactions', 5, 'Request For Quote',    2, 'bi-envelope-paper',  'Modules/Transactions/RequestForQuote.html'),
    (703, 'Transactions', 5, 'RFQ Price Update',     3, 'bi-currency-rupee',  'Modules/Transactions/RFQPriceUpdate.html'),
    (704, 'Transactions', 5, 'Price Comparison',     4, 'bi-bar-chart-line',  'Modules/Transactions/PriceComparison.html'),
    (705, 'Transactions', 5, 'RFQ Approval',         5, 'bi-patch-check',     'Modules/Transactions/RFQApproval.html');

-- Upsert by MTransNo: re-running re-points the row rather than duplicating it.
-- Nothing outside these five ids is touched, so no other section is disturbed.
MERGE dbo.Menulist AS T
USING @Menu AS S
   ON T.MTransNo = S.MTransNo
WHEN MATCHED THEN
    UPDATE SET T.Category   = @Category,
               T.CSLNo      = 1,
               T.Section    = S.Section,
               T.SSLNo      = S.SSLNo,
               T.ModuleName = S.ModuleName,
               T.MSLNo      = S.MSLNo,
               T.ModuleIcon = S.ModuleIcon,
               T.ModulePage = S.ModulePage
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    VALUES (S.MTransNo, @Category, 1, S.Section, S.SSLNo, S.ModuleName, S.MSLNo, S.ModuleIcon, S.ModulePage);

/*------------------------------------------------------------------------------
  GRANTS  -  without these the five rows above are invisible

  NOTE: this stays in the SAME batch as the MERGE above - no GO between them -
  because @Menu is a table variable and would not survive a batch boundary.

  GET /api/Home/GetMenuList builds the sidebar from dbo.Menulist JOINed to
  dbo.AccessTemplate on the signed-in user's AccessTemplate. The whole menu is
  returned unjoined ONLY for a user whose AccessTemplate is blank and whose
  UserType is 'ADMIN' - and both current users sit on the 'Administrator'
  template, so that bypass does not apply to them. A menu row with no matching
  grant simply never reaches the browser.

  So every template that already holds a full set of grants gets the five new
  modules too, at AuthType 'A' (full). Templates are handled generically rather
  than by naming 'Administrator', so a second admin-like template does not get
  silently left behind.

  AuthType 'A' = full access, MyFavorite 'N' = not pinned. Department is copied
  from the template's existing rows so the new grants look like their siblings.
------------------------------------------------------------------------------*/
INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT  T.TemplateName,
        T.Department,
        M.MTransNo,
        'A',
        'N',
        T.SubscID
  FROM  (
            -- One row per (template, subscription), with the Department those
            -- grants already use.
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

-- What the sidebar now holds, section by section.
SELECT Section, SSLNo, COUNT(*) AS Modules
  FROM dbo.Menulist
 GROUP BY Section, SSLNo
 ORDER BY SSLNo;
GO

-- Grants per template, so it is clear the new modules are reachable.
SELECT TemplateName, SubscID, COUNT(*) AS Grants
  FROM dbo.AccessTemplate
 GROUP BY TemplateName, SubscID;
GO

-- The new rows, in the order they will appear.
SELECT MTransNo, Section, ModuleName, MSLNo, ModuleIcon, ModulePage
  FROM dbo.Menulist
 WHERE ModulePage LIKE 'Modules/Transactions/%'
 ORDER BY MSLNo;
GO
