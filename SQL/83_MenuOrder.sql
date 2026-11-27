/*==============================================================================
  83. MENU ORDER - sections and pages in working order

  Sidebar order (SSLNo):
     1 Dashboards      every module dashboard in one place
     2 Sales
     3 Purchase
     4 Planning        Work Calendar, Production Plan, Production Schedule,
                       RM Requirement, Plan vs Actual
     5 Production
     6 Store
     7 Quality         + Job Card Inspection (moved from Production)
     8 Maintenance
     9 Accounts
    10 Masters         + Conversion BOM (moved from Production, next to Routing Chart)
    11 Administration
    12 BIS Utility     was "BIS Utilities"

  Also:
    * Sales Dashboard pointed at Modules/SalesReports/Dashboard.html, which does
      not exist; it now opens Modules/Sales/SalesDashboard.html.
    * Purchase Dashboard (Modules/Purchase/PurchaseDashboard.html) had no menu
      row; it is added, with full access ('A') on every access template that
      holds grants, like SQL 75 did for the other dashboards.

  Access rights point at Menulist.MTransNo, so moving a row to another section
  or renumbering it keeps every grant. Rows are matched by page path (not by
  MTransNo), so this runs the same on any database. A row this script does not
  know keeps its section and goes to the end of it; the list of such rows is
  printed at the end.

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 83_MenuOrder.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRAN;

-- 1. Sales Dashboard: point the row at the real page.
IF NOT EXISTS (SELECT 1 FROM dbo.Menulist WHERE ModulePage = 'Modules/Sales/SalesDashboard.html')
    UPDATE dbo.Menulist SET ModulePage = 'Modules/Sales/SalesDashboard.html'
     WHERE ModulePage = 'Modules/SalesReports/Dashboard.html';

-- 2. Purchase Dashboard: add the row if missing.
IF NOT EXISTS (SELECT 1 FROM dbo.Menulist WHERE ModulePage = 'Modules/Purchase/PurchaseDashboard.html')
BEGIN
    INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
    SELECT ISNULL(MAX(MTransNo), 0) + 1, 'EMS', 1, 'Dashboards', 1, 'Purchase Dashboard', 20, 'bi-speedometer2',
           'Modules/Purchase/PurchaseDashboard.html'
      FROM dbo.Menulist;
    PRINT 'Purchase Dashboard menu row added.';
END

INSERT INTO dbo.AccessTemplate (TemplateName, Department, ModuleNo, AuthType, MyFavorite, SubscID)
SELECT T.TemplateName, T.Department, M.MTransNo, 'A', 'N', T.SubscID
  FROM (SELECT TemplateName, SubscID, MAX(Department) AS Department
          FROM dbo.AccessTemplate WHERE TemplateName IS NOT NULL
         GROUP BY TemplateName, SubscID) T
 CROSS JOIN (SELECT MTransNo FROM dbo.Menulist WHERE ModulePage = 'Modules/Purchase/PurchaseDashboard.html') M
 WHERE NOT EXISTS (SELECT 1 FROM dbo.AccessTemplate A
                    WHERE A.TemplateName = T.TemplateName AND A.SubscID = T.SubscID AND A.ModuleNo = M.MTransNo);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' access grant(s) added for Purchase Dashboard.';

-- 3. Section order.
DECLARE @Sec TABLE (Section VARCHAR(50), OldName VARCHAR(100), SSLNo INT);   -- OldName: earlier names, '|'-separated
INSERT INTO @Sec VALUES
    ('Dashboards', NULL, 1), ('Sales', NULL, 2), ('Purchase', NULL, 3), ('Planning', NULL, 4),
    ('Production', NULL, 5), ('Store', NULL, 6), ('Quality', NULL, 7), ('Maintenance', NULL, 8),
    ('Accounts', NULL, 9), ('Masters', NULL, 10), ('Administration', NULL, 11), ('BIS Utility', 'BIS Utilities|Utility', 12);

-- 4. Every known page: its section, place and (where it changes) its name.
DECLARE @Map TABLE (Page VARCHAR(150), Section VARCHAR(50), MSLNo INT, ModuleName VARCHAR(100) NULL);
INSERT INTO @Map VALUES
    -- Dashboards
    ('Modules/Sales/SalesDashboard.html',              'Dashboards',  10, 'Sales Dashboard'),
    ('Modules/Purchase/PurchaseDashboard.html',        'Dashboards',  20, 'Purchase Dashboard'),
    ('Modules/Production/Dashboard.html',              'Dashboards',  30, 'Production Dashboard'),
    ('Modules/Store/InventoryDashboard.html',          'Dashboards',  40, 'Inventory Dashboard'),
    ('Modules/Quality/QualityDashboard.html',          'Dashboards',  50, 'Quality Dashboard'),
    ('Modules/Maintenance/MaintenanceDashboard.html',  'Dashboards',  60, 'Maintenance Dashboard'),
    -- Sales
    ('Modules/Sales/LeadGeneration.html',              'Sales',  10, NULL),
    ('Modules/Sales/SalesEnquiry.html',                'Sales',  20, NULL),
    ('Modules/Sales/Quotation.html',                   'Sales',  30, NULL),
    ('Modules/Sales/QuotationApproval.html',           'Sales',  40, NULL),
    ('Modules/Sales/FollowUp.html',                    'Sales',  50, NULL),
    ('Modules/Sales/OrderAcceptance.html',             'Sales',  60, NULL),
    ('Modules/Sales/OAApproval.html',                  'Sales',  70, NULL),
    ('Modules/Sales/OADataImport.html',                'Sales',  80, NULL),
    ('Modules/Sales/Proforma.html',                    'Sales',  90, NULL),
    ('Modules/Sales/PackingSlip.html',                 'Sales', 100, NULL),
    ('Modules/Sales/SalesInvoice.html',                'Sales', 110, NULL),
    ('Modules/Sales/SalesReturns.html',                'Sales', 120, NULL),
    ('Modules/SalesReports/LeadReport.html',           'Sales', 200, NULL),
    ('Modules/SalesReports/EnquiryReport.html',        'Sales', 210, NULL),
    ('Modules/SalesReports/EnqAttachmentView.html',    'Sales', 220, NULL),
    ('Modules/SalesReports/QuotationReport.html',      'Sales', 230, NULL),
    ('Modules/SalesReports/QtnAttachmentView.html',    'Sales', 240, NULL),
    ('Modules/SalesReports/OrderAcceptanceReport.html','Sales', 250, NULL),
    ('Modules/SalesReports/OAStatusReport.html',       'Sales', 260, NULL),
    ('Modules/SalesReports/PendingOrderReport.html',   'Sales', 270, NULL),
    ('Modules/SalesReports/OAAttachmentView.html',     'Sales', 280, NULL),
    ('Modules/SalesReports/ProformaReport.html',       'Sales', 290, NULL),
    ('Modules/SalesReports/PackingSlipReport.html',    'Sales', 300, NULL),
    ('Modules/SalesReports/SalesReport.html',          'Sales', 310, NULL),
    ('Modules/SalesReports/SalesReturnReport.html',    'Sales', 320, NULL),
    -- Purchase
    ('Modules/Purchase/PurchaseRequisition.html',               'Purchase',  10, NULL),
    ('Modules/Purchase/PRApproval.html',                        'Purchase',  20, NULL),
    ('Modules/Purchase/RequestForQuote.html',                   'Purchase',  30, NULL),
    ('Modules/Purchase/RFQPriceUpdate.html',                    'Purchase',  40, NULL),
    ('Modules/Purchase/PriceComparison.html',                   'Purchase',  50, NULL),
    ('Modules/Purchase/RFQApproval.html',                       'Purchase',  60, NULL),
    ('Modules/Purchase/PurchaseOrder.html',                     'Purchase',  70, NULL),
    ('Modules/Purchase/PurchaseOrderApproval.html',             'Purchase',  80, NULL),
    ('Modules/PurchaseReports/PurchaseRequisitionReport.html',  'Purchase', 200, NULL),
    ('Modules/PurchaseReports/RequisitionStatusReport.html',    'Purchase', 210, NULL),
    ('Modules/PurchaseReports/RFQReport.html',                  'Purchase', 220, NULL),
    ('Modules/PurchaseReports/RFQApprovalReport.html',          'Purchase', 230, NULL),
    ('Modules/PurchaseReports/PurchaseOrderReport.html',        'Purchase', 240, NULL),
    ('Modules/PurchaseReports/PurchaseStatusReport.html',       'Purchase', 250, NULL),
    ('Modules/PurchaseReports/GRNReport.html',                  'Purchase', 260, NULL),
    ('Modules/PurchaseReports/IssueReport.html',                'Purchase', 270, NULL),
    ('Modules/PurchaseReports/DCReport.html',                   'Purchase', 280, NULL),
    ('Modules/PurchaseReports/RGPReport.html',                  'Purchase', 290, NULL),
    ('Modules/PurchaseReports/RGPStatusReport.html',            'Purchase', 300, NULL),
    ('Modules/PurchaseReports/RGPInwardReport.html',            'Purchase', 310, NULL),
    ('Modules/PurchaseReports/StockReports.html',               'Purchase', 320, NULL),
    ('Modules/PurchaseReports/RMStockStatement.html',           'Purchase', 330, NULL),
    -- Planning
    ('Modules/Masters/WorkCalendar.html',                       'Planning',  10, NULL),
    ('Modules/Production/ProductionPlan.html',                  'Planning',  20, NULL),
    ('Modules/Production/ProductionSchedule.html',              'Planning',  30, NULL),
    ('Modules/PurchaseReports/RMRequirementReport.html',        'Planning',  40, NULL),
    ('Modules/PurchaseReports/PlanVsActualReport.html',         'Planning',  50, NULL),
    -- Production
    ('Modules/Production/JobCard.html',                         'Production',  10, NULL),
    ('Modules/Production/JobCardStatus.html',                   'Production',  20, NULL),
    ('Modules/PurchaseReports/JobcardReport.html',              'Production',  30, NULL),
    ('Modules/Production/MRN.html',                             'Production',  40, NULL),
    ('Modules/Production/MRNApproval.html',                     'Production',  50, NULL),
    ('Modules/PurchaseReports/ProductionMRNReport.html',        'Production',  60, NULL),
    ('Modules/Production/MRNG.html',                            'Production',  70, NULL),
    ('Modules/Production/MRNGApproval.html',                    'Production',  80, NULL),
    ('Modules/PurchaseReports/GeneralMRNReport.html',           'Production',  90, NULL),
    ('Modules/Production/RGPOutward.html',                      'Production', 100, NULL),
    ('Modules/Production/RGPApproval.html',                     'Production', 110, NULL),
    ('Modules/Production/DC.html',                              'Production', 120, NULL),
    -- Store
    ('Modules/Store/GRN.html',                                  'Store',  10, NULL),
    ('Modules/Store/GRNStore.html',                             'Store',  20, NULL),
    ('Modules/Store/HeatNo.html',                               'Store',  30, NULL),
    ('Modules/Store/HeatNoApproval.html',                       'Store',  40, NULL),
    ('Modules/Store/StoreIssue.html',                           'Store',  50, NULL),
    ('Modules/Store/IssueReturn.html',                          'Store',  60, NULL),
    ('Modules/Store/RGPInward.html',                            'Store',  70, NULL),
    ('Modules/Store/CustomerMaterialInward.html',               'Store',  80, NULL),
    ('Modules/Store/OpeningStock.html',                         'Store',  90, NULL),
    -- Quality
    ('Modules/Quality/GRNQC.html',                              'Quality',  10, NULL),
    ('Modules/Quality/GRNNCClosure.html',                       'Quality',  20, NULL),
    ('Modules/Quality/JobCardInspection.html',                  'Quality',  30, NULL),
    -- Maintenance
    ('Modules/Maintenance/PMCheckSheet.html',                   'Maintenance', 10, NULL),
    ('Modules/Maintenance/BreakdownMaintenance.html',           'Maintenance', 20, NULL),
    ('Modules/MaintenanceReports/MachineMaintenanceReport.html','Maintenance', 30, NULL),
    ('Modules/MaintenanceReports/MachineHistoryCard.html',      'Maintenance', 40, NULL),
    -- Masters
    ('Modules/Masters/CustomerMaster.html',        'Masters',  10, NULL),
    ('Modules/Masters/SupplierMaster.html',        'Masters',  20, NULL),
    ('Modules/Masters/PartMaster.html',            'Masters',  30, NULL),
    ('Modules/Masters/PartCodeMaster.html',        'Masters',  40, NULL),
    ('Modules/Masters/PartParameterList.html',     'Masters',  50, NULL),
    ('Modules/Masters/MaterialStandard.html',      'Masters',  60, NULL),
    ('Modules/Masters/DrawingRegister.html',       'Masters',  70, NULL),
    ('Modules/Masters/HSNMaster.html',             'Masters',  80, NULL),
    ('Modules/Masters/MachineMaster.html',         'Masters',  90, NULL),
    ('Modules/Masters/DieMaster.html',             'Masters', 100, NULL),
    ('Modules/Masters/RoutingChart.html',          'Masters', 110, NULL),
    ('Modules/Production/ConvBOM.html',            'Masters', 120, NULL),
    ('Modules/Masters/PMChecklistMaster.html',     'Masters', 130, NULL),
    ('Modules/Masters/StoreLocation.html',         'Masters', 140, NULL),
    ('Modules/Masters/SupplierPriceList.html',     'Masters', 150, NULL),
    ('Modules/Masters/StandardTerms.html',         'Masters', 160, NULL),
    ('Modules/Masters/ExtrasMaster.html',          'Masters', 170, NULL),
    ('Modules/Masters/BankMaster.html',            'Masters', 180, NULL),
    ('Modules/Masters/CurrencyMaster.html',        'Masters', 190, NULL),
    ('Modules/Masters/StateMaster.html',           'Masters', 200, NULL),
    ('Modules/Masters/OtherMaster.html',           'Masters', 210, NULL),
    ('Modules/Masters/CustomerDataImport.html',    'Masters', 220, NULL),
    ('Modules/Masters/SupplierDataImport.html',    'Masters', 230, NULL),
    ('Modules/Masters/PartDataImport.html',        'Masters', 240, NULL),
    ('Modules/Masters/MasterReportList.html',      'Masters', 250, NULL),
    -- Accounts
    ('Modules/Accounts/Dashboard.html',            'Accounts', 10, NULL),
    ('Modules/Accounts/GroupHeads.html',           'Accounts', 20, NULL),
    ('Modules/Accounts/AccountMaster.html',        'Accounts', 30, NULL),
    ('Modules/Accounts/TaxMaster.html',            'Accounts', 40, NULL),
    ('Modules/Accounts/AccountConfig.html',        'Accounts', 50, NULL),
    -- Administration
    ('Modules/Administration/UserMaster.html',     'Administration', 10, NULL),
    ('Modules/Administration/AccessTemplate.html', 'Administration', 20, NULL),
    ('Modules/Administration/AccessLog.html',      'Administration', 30, NULL),
    ('Modules/Administration/ErrorLog.html',       'Administration', 40, NULL),
    ('Modules/Administration/Company.html',        'Administration', 50, NULL),
    ('Modules/Administration/BranchMaster.html',   'Administration', 60, NULL),
    -- Utility
    ('Modules/BISUtilities/GeneralConfig.html',    'BIS Utility', 10, NULL),
    ('Modules/BISUtilities/MenuList.html',         'BIS Utility', 20, NULL),
    ('Modules/BISUtilities/OtherMasterList.html',  'BIS Utility', 30, NULL),
    ('Modules/BISUtilities/DataBackup.html',       'BIS Utility', 40, NULL),
    ('Modules/BISUtilities/TableUpdate.html',      'BIS Utility', 50, NULL);

-- Known pages: section, order, name.
UPDATE m
   SET m.Section    = x.Section,
       m.SSLNo      = s.SSLNo,
       m.CSLNo      = 1,
       m.MSLNo      = x.MSLNo,
       m.ModuleName = ISNULL(x.ModuleName, m.ModuleName),
       m.ModuleIcon = CASE WHEN x.Section = 'Dashboards' THEN 'bi-speedometer2' ELSE m.ModuleIcon END
  FROM dbo.Menulist m
  JOIN @Map x ON LOWER(LTRIM(RTRIM(m.ModulePage))) = LOWER(x.Page)
  JOIN @Sec s ON s.Section = x.Section;
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) placed.';

-- Rows this script does not know: keep their section (renamed if it was
-- renamed), ordered with it, after the known pages.
UPDATE m
   SET m.Section = s.Section,
       m.SSLNo   = s.SSLNo,
       m.MSLNo   = 900 + ISNULL(m.MSLNo, 0)
  FROM dbo.Menulist m
  JOIN @Sec s ON s.Section = m.Section OR CHARINDEX('|' + m.Section + '|', '|' + ISNULL(s.OldName, '') + '|') > 0
 WHERE NOT EXISTS (SELECT 1 FROM @Map x WHERE LOWER(x.Page) = LOWER(LTRIM(RTRIM(m.ModulePage))))
   AND m.MSLNo < 900;

COMMIT;

-- What is left over (should be empty on a standard database).
SELECT MTransNo, Section, SSLNo, MSLNo, ModuleName, ModulePage AS [Row not in this script]
  FROM dbo.Menulist m
 WHERE NOT EXISTS (SELECT 1 FROM @Map x WHERE LOWER(x.Page) = LOWER(LTRIM(RTRIM(m.ModulePage))));

-- The new menu.
SELECT Section, SSLNo, MSLNo, ModuleName, ModulePage
  FROM dbo.Menulist
 ORDER BY CSLNo, SSLNo, MSLNo;
