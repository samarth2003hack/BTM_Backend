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
    (818, 'Sales Reports', 7, 'Enquiry Attachments',     9, 'bi-paperclip',          @Folder + 'EnqAttachmentView.html'),
    (819, 'Sales Reports', 7, 'Quotation Attachments',  10, 'bi-paperclip',          @Folder + 'QtnAttachmentView.html'),
    (820, 'Sales Reports', 7, 'OA Attachments',         11, 'bi-paperclip',          @Folder + 'OAAttachmentView.html');

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
