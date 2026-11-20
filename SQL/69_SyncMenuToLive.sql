/*==============================================================================
  69. MENU SYNC - updates the Menulist table only

  Brings a database's menu in line with the reference menu, matching rows by
  PAGE PATH (ModulePage). MTransNo is never used for matching, because it
  differs between databases.
    * page already there  -> section, order, name and icon are set to the reference
    * page missing         -> added, with the next free MTransNo
    * nothing is deleted   -> menu rows that exist only on this database stay
  Access grants (AccessTemplate) are NOT changed. A newly added page has no
  grants until they are set in Access Template.

  Runs in one transaction. Read the counts, then COMMIT; or ROLLBACK;
  Run with: sqlcmd -I -S <server> -d <database> -i 69_SyncMenuToLive.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

CREATE TABLE #Src (
    ModulePage VARCHAR(100) NOT NULL PRIMARY KEY,
    Category   VARCHAR(20)  NOT NULL,
    CSLNo      INT          NULL,
    Section    VARCHAR(50)  NULL,
    SSLNo      INT          NULL,
    ModuleName VARCHAR(50)  NULL,
    MSLNo      INT          NULL,
    ModuleIcon VARCHAR(50)  NULL
);

INSERT INTO #Src (ModulePage, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon) VALUES
    (N'Modules/Accounts/Dashboard.html', N'EMS', 1, N'Accounts', 8, N'Dashboard', 1, N'bi-speedometer2'),
    (N'Modules/Accounts/GroupHeads.html', N'EMS', 1, N'Accounts', 8, N'Group Heads', 2, N'bi-diagram-2'),
    (N'Modules/Accounts/AccountMaster.html', N'EMS', 1, N'Accounts', 8, N'Account Master', 3, N'bi-journal-bookmark'),
    (N'Modules/Accounts/TaxMaster.html', N'EMS', 1, N'Accounts', 8, N'Tax Master', 4, N'bi-percent'),
    (N'Modules/Accounts/AccountConfig.html', N'EMS', 1, N'Accounts', 8, N'Account Configuration', 5, N'bi-sliders'),
    (N'Modules/Administration/UserMaster.html', N'EMS', 1, N'Administration', 9, N'User Management', 1, N'bi-people'),
    (N'Modules/Administration/AccessTemplate.html', N'EMS', 1, N'Administration', 9, N'Access Template', 2, N'bi-shield-lock'),
    (N'Modules/Administration/AccessLog.html', N'EMS', 1, N'Administration', 9, N'Access Log', 5, N'bi-clock-history'),
    (N'Modules/Administration/ErrorLog.html', N'EMS', 1, N'Administration', 9, N'Error Log', 6, N'bi-bug'),
    (N'Modules/Administration/Company.html', N'EMS', 1, N'Administration', 9, N'Company Master', 7, N'bi-building'),
    (N'Modules/Administration/BranchMaster.html', N'EMS', 1, N'Administration', 9, N'Branch Master', 8, N'bi-diagram-3'),
    (N'Modules/BISUtilities/GeneralConfig.html', N'EMS', 1, N'BIS Utilities', 15, N'General Configuration', 1, N'bi-gear'),
    (N'Modules/BISUtilities/MenuList.html', N'EMS', 1, N'BIS Utilities', 15, N'Menu List', 2, N'bi-menu-button-wide'),
    (N'Modules/BISUtilities/OtherMasterList.html', N'EMS', 1, N'BIS Utilities', 15, N'Other Master List', 3, N'bi-card-list'),
    (N'Modules/BISUtilities/DataBackup.html', N'EMS', 1, N'BIS Utilities', 15, N'Data Backup', 4, N'bi-hdd'),
    (N'Modules/BISUtilities/TableUpdate.html', N'EMS', 1, N'BIS Utilities', 15, N'Table Update', 5, N'bi-table'),
    (N'Modules/Maintenance/PMCheckSheet.html', N'EMS', 1, N'Maintenance', 10, N'PM Check Sheet', 1, N'bi-clipboard2-check'),
    (N'Modules/Maintenance/BreakdownMaintenance.html', N'EMS', 1, N'Maintenance', 10, N'Breakdown Maintenance', 5, N'bi-tools'),
    (N'Modules/MaintenanceReports/MachineMaintenanceReport.html', N'EMS', 1, N'Maintenance', 10, N'Machine Maintenance Report', 10, N'bi-file-earmark-bar-graph'),
    (N'Modules/MaintenanceReports/MachineHistoryCard.html', N'EMS', 1, N'Maintenance', 10, N'Machine History Card', 20, N'bi-clock-history'),
    (N'Modules/Masters/CustomerMaster.html', N'EMS', 1, N'Masters', 1, N'Customer Master', 10, N'bi-person-vcard'),
    (N'Modules/Masters/SupplierMaster.html', N'EMS', 1, N'Masters', 1, N'Supplier Master', 20, N'bi-truck'),
    (N'Modules/Masters/PartMaster.html', N'EMS', 1, N'Masters', 1, N'Part Master', 30, N'bi-box-seam'),
    (N'Modules/Masters/PartCodeMaster.html', N'EMS', 1, N'Masters', 1, N'Part Code Master', 40, N'bi-upc-scan'),
    (N'Modules/Masters/PartParameterList.html', N'EMS', 1, N'Masters', 1, N'Part Parameter List', 50, N'bi-sliders2'),
    (N'Modules/Masters/MaterialStandard.html', N'EMS', 1, N'Masters', 1, N'Material Standard', 60, N'bi-rulers'),
    (N'Modules/Masters/DrawingRegister.html', N'EMS', 1, N'Masters', 1, N'Drawing Register', 70, N'bi-file-earmark-image'),
    (N'Modules/Masters/HSNMaster.html', N'EMS', 1, N'Masters', 1, N'HSN Master', 80, N'bi-upc'),
    (N'Modules/Masters/MachineMaster.html', N'EMS', 1, N'Masters', 1, N'Machine Master', 90, N'bi-gear-wide-connected'),
    (N'Modules/Masters/DieMaster.html', N'EMS', 1, N'Masters', 1, N'Die Master', 100, N'bi-tools'),
    (N'Modules/Masters/RoutingChart.html', N'EMS', 1, N'Masters', 1, N'Routing Chart', 110, N'bi-diagram-3'),
    (N'Modules/Masters/WorkCalendar.html', N'EMS', 1, N'Masters', 1, N'Work Calendar', 120, N'bi-calendar3'),
    (N'Modules/Masters/PMChecklistMaster.html', N'EMS', 1, N'Masters', 1, N'PM Checklist Master', 130, N'bi-clipboard2-check'),
    (N'Modules/Masters/StoreLocation.html', N'EMS', 1, N'Masters', 1, N'Store Location', 140, N'bi-geo-alt'),
    (N'Modules/Masters/SupplierPriceList.html', N'EMS', 1, N'Masters', 1, N'Price List', 150, N'bi-tags'),
    (N'Modules/Masters/StandardTerms.html', N'EMS', 1, N'Masters', 1, N'Standard Terms', 160, N'bi-file-text'),
    (N'Modules/Masters/ExtrasMaster.html', N'EMS', 1, N'Masters', 1, N'Extras Master', 170, N'bi-layers'),
    (N'Modules/Masters/BankMaster.html', N'EMS', 1, N'Masters', 1, N'Bank Master', 180, N'bi-bank'),
    (N'Modules/Masters/CurrencyMaster.html', N'EMS', 1, N'Masters', 1, N'Currency Master', 190, N'bi-currency-exchange'),
    (N'Modules/Masters/StateMaster.html', N'EMS', 1, N'Masters', 1, N'State Master', 200, N'bi-geo-alt'),
    (N'Modules/Masters/OtherMaster.html', N'EMS', 1, N'Masters', 1, N'Other Master', 210, N'bi-list-ul'),
    (N'Modules/Masters/CustomerDataImport.html', N'EMS', 1, N'Masters', 1, N'Customer Data Import', 220, N'bi-file-earmark-arrow-up'),
    (N'Modules/Masters/SupplierDataImport.html', N'EMS', 1, N'Masters', 1, N'Supplier Data Import', 230, N'bi-file-earmark-arrow-up'),
    (N'Modules/Masters/PartDataImport.html', N'EMS', 1, N'Masters', 1, N'Part Data Import', 240, N'bi-file-earmark-arrow-up'),
    (N'Modules/Masters/MasterReportList.html', N'EMS', 1, N'Masters', 1, N'Masters Report List', 250, N'bi-card-list'),
    (N'Modules/Production/ConvBOM.html', N'EMS', 1, N'Production', 9, N'Conversion BOM', 10, N'bi-diagram-3'),
    (N'Modules/Production/ProductionPlan.html', N'EMS', 1, N'Production', 9, N'Production Plan', 20, N'bi-kanban'),
    (N'Modules/Production/ProductionSchedule.html', N'EMS', 1, N'Production', 9, N'Production Schedule', 30, N'bi-calendar-week'),
    (N'Modules/Production/JobCard.html', N'EMS', 1, N'Production', 9, N'Job Card', 40, N'bi-clipboard-check'),
    (N'Modules/Production/JobCardStatus.html', N'EMS', 1, N'Production', 9, N'Job Card Status', 50, N'bi-clipboard-data'),
    (N'Modules/Quality/JobCardInspection.html', N'EMS', 1, N'Production', 9, N'Job Card Inspection', 60, N'bi-clipboard2-pulse'),
    (N'Modules/PurchaseReports/JobcardReport.html', N'EMS', 1, N'Production', 9, N'Job Card Report', 70, N'bi-file-earmark-richtext'),
    (N'Modules/PurchaseReports/PlanVsActualReport.html', N'EMS', 1, N'Production', 9, N'Plan vs Actual', 75, N'bi-bar-chart-line'),
    (N'Modules/Production/MRN.html', N'EMS', 1, N'Production', 9, N'Production MRN', 80, N'bi-gear-wide-connected'),
    (N'Modules/Production/MRNApproval.html', N'EMS', 1, N'Production', 9, N'Production MRN Approval', 90, N'bi-tools'),
    (N'Modules/PurchaseReports/ProductionMRNReport.html', N'EMS', 1, N'Production', 9, N'Production MRN Report', 100, N'bi-file-earmark-richtext'),
    (N'Modules/Production/MRNG.html', N'EMS', 1, N'Production', 9, N'General MRN', 110, N'bi-card-checklist'),
    (N'Modules/Production/MRNGApproval.html', N'EMS', 1, N'Production', 9, N'General MRN Approval', 120, N'bi-tools'),
    (N'Modules/PurchaseReports/GeneralMRNReport.html', N'EMS', 1, N'Production', 9, N'General MRN Report', 130, N'bi-file-earmark-richtext'),
    (N'Modules/Production/RGPOutward.html', N'EMS', 1, N'Production', 9, N'RGP Outward', 140, N'bi-box-arrow-right'),
    (N'Modules/Production/RGPApproval.html', N'EMS', 1, N'Production', 9, N'RGP Approval', 150, N'bi-check2-circle'),
    (N'Modules/Production/DC.html', N'EMS', 1, N'Production', 9, N'Delivery Challan', 160, N'bi-truck'),
    (N'Modules/Purchase/PurchaseRequisition.html', N'EMS', 1, N'Purchase', 3, N'Purchase Requisition', 5, N'bi-clipboard-check'),
    (N'Modules/Purchase/PRApproval.html', N'EMS', 1, N'Purchase', 3, N'PR Approval', 6, N'bi-clipboard-check'),
    (N'Modules/Purchase/RequestForQuote.html', N'EMS', 1, N'Purchase', 3, N'Request For Quote', 10, N'bi-envelope-paper'),
    (N'Modules/Purchase/RFQPriceUpdate.html', N'EMS', 1, N'Purchase', 3, N'RFQ Price Update', 15, N'bi-currency-rupee'),
    (N'Modules/Purchase/PriceComparison.html', N'EMS', 1, N'Purchase', 3, N'Price Comparison', 20, N'bi-bar-chart-line'),
    (N'Modules/Purchase/RFQApproval.html', N'EMS', 1, N'Purchase', 3, N'RFQ Approval', 25, N'bi-patch-check'),
    (N'Modules/Purchase/PurchaseOrder.html', N'EMS', 1, N'Purchase', 3, N'Purchase Order', 30, N'bi-cart-check'),
    (N'Modules/Purchase/PurchaseOrderApproval.html', N'EMS', 1, N'Purchase', 3, N'PO Approval', 31, N'bi-file-earmark-check'),
    (N'Modules/PurchaseReports/PurchaseRequisitionReport.html', N'EMS', 1, N'Purchase', 3, N'Purchase Requisition Report', 100, N'bi-file-earmark-bar-graph'),
    (N'Modules/PurchaseReports/RequisitionStatusReport.html', N'EMS', 1, N'Purchase', 3, N'Purchase Requisition Status Report', 105, N'bi-clipboard-data'),
    (N'Modules/PurchaseReports/RFQReport.html', N'EMS', 1, N'Purchase', 3, N'RFQ Report', 110, NULL),
    (N'Modules/PurchaseReports/RFQApprovalReport.html', N'EMS', 1, N'Purchase', 3, N'RFQ Approval Report', 115, NULL),
    (N'Modules/PurchaseReports/PurchaseOrderReport.html', N'EMS', 1, N'Purchase', 3, N'Purchase Order Reports', 120, N'bi-file-earmark-text'),
    (N'Modules/PurchaseReports/PurchaseStatusReport.html', N'EMS', 1, N'Purchase', 3, N'Purchase Status Report', 125, N'bi-bar-chart-line'),
    (N'Modules/PurchaseReports/GRNReport.html', N'EMS', 1, N'Purchase', 3, N'GRN Report', 130, N'bi-receipt'),
    (N'Modules/PurchaseReports/IssueReport.html', N'EMS', 1, N'Purchase', 3, N'Issue Report', 135, N'bi-file-earmark-spreadsheet'),
    (N'Modules/PurchaseReports/DCReport.html', N'EMS', 1, N'Purchase', 3, N'DC Report', 140, N'bi-file-richtext'),
    (N'Modules/PurchaseReports/RGPReport.html', N'EMS', 1, N'Purchase', 3, N'RGP Report', 145, N'bi-file-richtext'),
    (N'Modules/PurchaseReports/RGPStatusReport.html', N'EMS', 1, N'Purchase', 3, N'RGP Status Report', 150, N'bi-graph-up-arrow'),
    (N'Modules/PurchaseReports/RGPInwardReport.html', N'EMS', 1, N'Purchase', 3, N'RGP Inward Report', 155, N'bi-file-earmark-check'),
    (N'Modules/PurchaseReports/StockReports.html', N'EMS', 1, N'Purchase', 3, N'Stock Reports', 160, N'bi-boxes'),
    (N'Modules/PurchaseReports/RMRequirementReport.html', N'EMS', 1, N'Purchase', 3, N'RM Requirement', 165, N'bi-calculator'),
    (N'Modules/PurchaseReports/RMStockStatement.html', N'EMS', 1, N'Purchase', 3, N'RM Stock Statement', 170, N'bi-archive'),
    (N'Modules/Quality/GRNQC.html', N'EMS', 1, N'Quality', 12, N'Quality Inspection', 1, N'bi-clipboard-check'),
    (N'Modules/Quality/GRNNCClosure.html', N'EMS', 1, N'Quality', 12, N'Quality Inspection Closure', 2, N'bi-lock'),
    (N'Modules/Sales/LeadGeneration.html', N'EMS', 1, N'Sales', 2, N'Lead Generation', 5, N'bi-person-plus'),
    (N'Modules/Sales/SalesEnquiry.html', N'EMS', 1, N'Sales', 2, N'Sales Enquiry', 10, N'bi-chat-left-dots'),
    (N'Modules/Sales/Quotation.html', N'EMS', 1, N'Sales', 2, N'Quotation', 15, N'bi-file-earmark-text'),
    (N'Modules/Sales/QuotationApproval.html', N'EMS', 1, N'Sales', 2, N'Quotation Approval', 20, N'bi-file-earmark-text'),
    (N'Modules/Sales/FollowUp.html', N'EMS', 1, N'Sales', 2, N'Quotation Follow-Up', 21, N'bi-telephone-outbound'),
    (N'Modules/Sales/OrderAcceptance.html', N'EMS', 1, N'Sales', 2, N'Order Acceptance', 25, N'bi-check2-square'),
    (N'Modules/Sales/OAApproval.html', N'EMS', 1, N'Sales', 2, N'Order Acceptance Approval', 30, N'bi-tools'),
    (N'Modules/Sales/Proforma.html', N'EMS', 1, N'Sales', 2, N'Proforma Invoice', 35, N'bi-receipt-cutoff'),
    (N'Modules/Sales/PackingSlip.html', N'EMS', 1, N'Sales', 2, N'Packing', 40, N'bi-box-seam'),
    (N'Modules/Sales/SalesInvoice.html', N'EMS', 1, N'Sales', 2, N'Sales Invoice', 45, N'bi-receipt'),
    (N'Modules/Sales/SalesReturns.html', N'EMS', 1, N'Sales', 2, N'Sales returns', 46, N'bi-receipt'),
    (N'Modules/SalesReports/LeadReport.html', N'EMS', 1, N'Sales', 2, N'Lead Report', 50, N'bi-person-lines-fill'),
    (N'Modules/SalesReports/EnquiryReport.html', N'EMS', 1, N'Sales', 2, N'Enquiry Report', 55, N'bi-chat-square-text'),
    (N'Modules/SalesReports/EnqAttachmentView.html', N'EMS', 1, N'Sales', 2, N'Enquiry Attachment View', 60, N'bi-paperclip'),
    (N'Modules/SalesReports/QuotationReport.html', N'EMS', 1, N'Sales', 2, N'Quotation Report', 65, N'bi-file-earmark-richtext'),
    (N'Modules/SalesReports/QtnAttachmentView.html', N'EMS', 1, N'Sales', 2, N'Quotation Attachment View', 70, N'bi-paperclip'),
    (N'Modules/SalesReports/OrderAcceptanceReport.html', N'EMS', 1, N'Sales', 2, N'Order Acceptance Report', 75, N'bi-file-earmark-richtext'),
    (N'Modules/SalesReports/OAStatusReport.html', N'EMS', 1, N'Sales', 2, N'OA Status Report', 76, N'bi-file-earmark-richtext'),
    (N'Modules/SalesReports/PendingOrderReport.html', N'EMS', 1, N'Sales', 2, N'Pending Order Report', 77, N'bi-hourglass-split'),
    (N'Modules/SalesReports/OAAttachmentView.html', N'EMS', 1, N'Sales', 2, N'Order Acceptance Attachment View', 80, N'bi-paperclip'),
    (N'Modules/SalesReports/ProformaReport.html', N'EMS', 1, N'Sales', 2, N'Proforma Report', 81, N'bi-file-earmark-richtext'),
    (N'Modules/SalesReports/PackingSlipReport.html', N'EMS', 1, N'Sales', 2, N'Packing slip Report', 82, N'bi-file-earmark-richtext'),
    (N'Modules/SalesReports/SalesReport.html', N'EMS', 1, N'Sales', 2, N'Sales Report', 85, N'bi-graph-up'),
    (N'Modules/SalesReports/SalesReturnReport.html', N'EMS', 1, N'Sales', 2, N'Sales returns Report', 86, N'bi-graph-up'),
    (N'Modules/SalesReports/Dashboard.html', N'EMS', 1, N'Sales', 2, N'Dashboard', 90, N'bi-speedometer2'),
    (N'Modules/Store/GRN.html', N'EMS', 1, N'Store', 11, N'Goods Receipt Note (PO)', 1, N'bi-eye'),
    (N'Modules/Store/GRNStore.html', N'EMS', 1, N'Store', 11, N'Store Allocation', 2, N'bi-diagram-3'),
    (N'Modules/Store/HeatNo.html', N'EMS', 1, N'Store', 11, N'Heat No Entry', 3, N'bi-upc-scan'),
    (N'Modules/Store/HeatNoApproval.html', N'EMS', 1, N'Store', 11, N'Heat No Approval', 3, N'bi-tools'),
    (N'Modules/Store/StoreIssue.html', N'EMS', 1, N'Store', 11, N'Store Issue', 4, N'bi-box-arrow-up-right'),
    (N'Modules/Store/IssueReturn.html', N'EMS', 1, N'Store', 11, N'Issue Return', 5, N'bi-arrow-return-left'),
    (N'Modules/Store/RGPInward.html', N'EMS', 1, N'Store', 11, N'RGP Inward', 6, N'bi-box-arrow-left'),
    (N'Modules/Store/CustomerMaterialInward.html', N'EMS', 1, N'Store', 11, N'Customer Material Inward', 7, N'bi-box-arrow-in-down');

-- 1. Pages that already have a row: set section, order, name and icon.
UPDATE m
   SET m.CSLNo      = s.CSLNo,
       m.Section    = s.Section,
       m.SSLNo      = s.SSLNo,
       m.ModuleName = s.ModuleName,
       m.MSLNo      = s.MSLNo,
       m.ModuleIcon = s.ModuleIcon
  FROM dbo.Menulist m
  JOIN #Src s ON s.ModulePage = m.ModulePage
 WHERE m.Category = s.Category
   AND (   ISNULL(m.CSLNo, -1)      <> ISNULL(s.CSLNo, -1)
        OR ISNULL(m.Section, '')    <> ISNULL(s.Section, '')
        OR ISNULL(m.SSLNo, -1)      <> ISNULL(s.SSLNo, -1)
        OR ISNULL(m.ModuleName, '') <> ISNULL(s.ModuleName, '')
        OR ISNULL(m.MSLNo, -1)      <> ISNULL(s.MSLNo, -1)
        OR ISNULL(m.ModuleIcon, '') <> ISNULL(s.ModuleIcon, ''));
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) updated.';

-- 2. Pages with no row yet: add them with the next free number.
DECLARE @Max INT = (SELECT ISNULL(MAX(MTransNo), 0) FROM dbo.Menulist);

INSERT INTO dbo.Menulist (MTransNo, Category, CSLNo, Section, SSLNo, ModuleName, MSLNo, ModuleIcon, ModulePage)
SELECT @Max + ROW_NUMBER() OVER (ORDER BY s.Section, s.MSLNo, s.ModulePage),
       s.Category, s.CSLNo, s.Section, s.SSLNo, s.ModuleName, s.MSLNo, s.ModuleIcon, s.ModulePage
  FROM #Src s
 WHERE NOT EXISTS (SELECT 1 FROM dbo.Menulist m WHERE m.ModulePage = s.ModulePage);
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) added.';

-- 3. The menu now: items per section.
SELECT Section, COUNT(*) AS Items
  FROM dbo.Menulist WHERE Category = 'EMS'
 GROUP BY Section ORDER BY Section;

-- Check the counts, then run COMMIT; (or ROLLBACK; to abandon).
COMMIT TRANSACTION;
