/*==============================================================================
  25. SALES MENU + LOOKUP TYPES  -  what the live Sales screens need to be
      reachable and to fill their dropdowns

  The seven pages in EMSSoft/Modules/Sales/ now run on the real API
  (LeadGeneration, Enq, Qtn, OA, Proforma, PackingSlip, SalesInvoice
  controllers). Two pieces of data were missing:

    1. A 'Sales' sidebar section. The menu had no Sales rows at all, and a
       Menulist row alone is invisible without an AccessTemplate grant, so both
       are written here (same MERGE + grant pattern as 18_PurchaseOrderPage.sql).

    2. OtherMaster lookup TYPES the Sales pages read through
       GET /api/OtherMaster/GetDropdown/{masterType}/{subscID}:
           DocSeries   document series on every Sales form
           Unit        units of measure on quotation / OA lines
           Region      quotation / OA region
           BoxType     packing slip case type
           Location    lead city
       Only the types are created. The values (e.g. a series such as
       'AF 2526') are business data - add them from the Other Master screen.
       Until a DocSeries value exists, the Sales forms cannot be saved.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the pages are in 'EMSSoft/Modules/Sales/'. Set it
    to wherever the deployed server serves them from.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops rows 801-807 again (the same as 701-706).

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. SIDEBAR SECTION  +  GRANTS

  MTransNo 801-807 are unused. SSLNo 6 puts Sales after Transactions. Rows are
  in process order: Lead -> Enquiry -> Quotation -> OA -> Proforma ->
  Packing Slip -> Invoice.
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/Sales/';   -- <- check, see header

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
    (801, 'Sales', 6, 'Lead Generation',   1, 'bi-person-plus',      @Folder + 'LeadGeneration.html'),
    (802, 'Sales', 6, 'Sales Enquiry',     2, 'bi-question-circle',  @Folder + 'SalesEnquiry.html'),
    (803, 'Sales', 6, 'Quotation',         3, 'bi-file-earmark-text',@Folder + 'Quotation.html'),
    (804, 'Sales', 6, 'Order Acceptance',  4, 'bi-clipboard-check',  @Folder + 'OrderAcceptance.html'),
    (805, 'Sales', 6, 'Proforma Invoice',  5, 'bi-receipt',          @Folder + 'Proforma.html'),
    (806, 'Sales', 6, 'Packing Slip',      6, 'bi-box-seam',         @Folder + 'PackingSlip.html'),
    (807, 'Sales', 6, 'Sales Invoice',     7, 'bi-receipt-cutoff',   @Folder + 'SalesInvoice.html');

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
-- Every template that already holds grants gets full access to the new
-- modules, with Department copied from its existing rows.
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

/*------------------------------------------------------------------------------
  2. OTHER MASTER LOOKUP TYPES

  One row per subscription that already has lookup types, so each tenant can
  maintain its own values. Department follows the existing rows ('Admin').
------------------------------------------------------------------------------*/
DECLARE @Types TABLE (MasterType VARCHAR(50));
INSERT INTO @Types (MasterType)
VALUES ('DocSeries'), ('Unit'), ('Region'), ('BoxType'), ('Location');

INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, 'Admin', 'N', S.SubscID
  FROM  @Types T
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMasterList L
            WHERE L.MasterType   = T.MasterType
              AND L.SubscID      = S.SubscID
              AND L.DeleteStatus = 'N');
GO

-- What was written.
SELECT M.MTransNo, M.Section, M.ModuleName, M.MSLNo, M.ModulePage,
       (SELECT COUNT(*) FROM dbo.AccessTemplate A WHERE A.ModuleNo = M.MTransNo) AS Grants
  FROM dbo.Menulist M
 WHERE M.MTransNo BETWEEN 801 AND 807
 ORDER BY M.MSLNo;

SELECT MTransNo, MasterType, Department, SubscID
  FROM dbo.OtherMasterList
 WHERE DeleteStatus = 'N'
 ORDER BY SubscID, MTransNo;
GO
