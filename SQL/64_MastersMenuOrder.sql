/*==============================================================================
  64. MASTERS MENU ORDER

  Re-orders the Masters section of the menu in groups. Only MSLNo is changed;
  MTransNo, names, icons, pages and access grants are untouched. Rows are
  matched by page path; pages missing from the menu are skipped.

      Parties        Customer, Supplier
      Part data      Part, Part Code, Part Parameter List, Material Standard,
                     Drawing Register, HSN
      Production     Machine, Die, Routing Chart, Work Calendar, PM Checklist,
                     Store Location
      Commercial     Price List, Standard Terms, Extras
      General        Bank, Currency, State, Other Master
      Reports        Masters Report List

  Numbers go up in steps of 10 so a new master can slot in without renumbering.
  Back up first:  SELECT * INTO dbo.Menulist_backup_masters FROM dbo.Menulist;
  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 64_MastersMenuOrder.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @Order TABLE (ModulePage VARCHAR(100), MSLNo INT);
INSERT INTO @Order (ModulePage, MSLNo) VALUES
    ('Modules/Masters/CustomerMaster.html',       10),
    ('Modules/Masters/SupplierMaster.html',       20),
    ('Modules/Masters/PartMaster.html',           30),
    ('Modules/Masters/PartCodeMaster.html',       40),
    ('Modules/Masters/PartParameterList.html',    50),
    ('Modules/Masters/MaterialStandard.html',     60),
    ('Modules/Masters/DrawingRegister.html',      70),
    ('Modules/Masters/HSNMaster.html',            80),
    ('Modules/Masters/MachineMaster.html',        90),
    ('Modules/Masters/DieMaster.html',           100),
    ('Modules/Masters/RoutingChart.html',        110),
    ('Modules/Masters/WorkCalendar.html',        120),
    ('Modules/Masters/PMChecklistMaster.html',   130),
    ('Modules/Masters/StoreLocation.html',       140),
    ('Modules/Masters/SupplierPriceList.html',   150),
    ('Modules/Masters/StandardTerms.html',       160),
    ('Modules/Masters/ExtrasMaster.html',        170),
    ('Modules/Masters/BankMaster.html',          180),
    ('Modules/Masters/CurrencyMaster.html',      190),
    ('Modules/Masters/StateMaster.html',         200),
    ('Modules/Masters/OtherMaster.html',         210),
    ('Modules/Masters/MasterReportList.html',    220);

UPDATE m
   SET m.MSLNo = o.MSLNo
  FROM dbo.Menulist m
  JOIN @Order o ON o.ModulePage = m.ModulePage
 WHERE m.Category = 'EMS' AND m.Section = 'Masters'
   AND ISNULL(m.MSLNo, -1) <> o.MSLNo;
PRINT CONVERT(VARCHAR(10), @@ROWCOUNT) + ' menu row(s) re-ordered.';

SELECT m.MTransNo, m.MSLNo, m.ModuleName, m.ModulePage
  FROM dbo.Menulist m
 WHERE m.Category = 'EMS' AND m.Section = 'Masters'
 ORDER BY m.MSLNo;
