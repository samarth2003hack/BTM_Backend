/*==============================================================================
  71. SALES MENU - Order Acceptance Data Import

  Adds the OA bulk-import page to the Sales section, right after
  Order Acceptance Approval (MSLNo 30):
      25 Order Acceptance
      30 Order Acceptance Approval
      31 Order Acceptance Import      (new)
      35 Proforma Invoice

  API: OADataImportController (GetLookups / Validate / Import). No table or
  procedure changes - the import writes through SP_OAMst, SP_OADtl,
  SP_OADtlPlanFields, SP_OATerms and SP_OAApproval like the OA page.

  The row is matched by page path; a new row takes the next free MTransNo.
  Grants full access ('A') to every access template that holds grants.
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 71_OADataImportMenu.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Page VARCHAR(100) = 'Modules/Sales/OADataImport.html';

IF NOT EXISTS (SELECT 1 FROM dbo.Menulist WHERE ModulePage = @Page)
BEGIN
    DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);
    INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    SELECT TOP (1) @Max + 1, 'EMS', s.CSLNo, 'Sales', s.SSLNo,
           'Order Acceptance Import', 31, 'bi-file-earmark-arrow-up', @Page
      FROM dbo.Menulist s
     WHERE s.Category = 'EMS' AND s.Section = 'Sales'
     ORDER BY s.MSLNo;
    PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) added.';
END
ELSE
BEGIN
    UPDATE dbo.Menulist
       SET Section = 'Sales', MSLNo = 31, ModuleName = 'Order Acceptance Import', ModuleIcon = 'bi-file-earmark-arrow-up'
     WHERE ModulePage = @Page;
    PRINT 'Menu row already present - name, place and icon refreshed.';
END

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT MTransNo FROM dbo.Menulist WHERE ModulePage = @Page) M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added.';

SELECT m.MTransNo, m.MSLNo, m.ModuleName, m.ModuleIcon, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.Category = 'EMS' AND m.Section = 'Sales' AND m.MSLNo BETWEEN 25 AND 35
 ORDER BY m.MSLNo;
