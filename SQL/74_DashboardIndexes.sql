/*==============================================================================
  74. DASHBOARD INDEXES

  Read-only indexes for the dashboard endpoints (Sales, Purchase, Production,
  Maintenance, Inventory, Quality). Every dashboard query is limited by a date
  and summed by the server; these let SQL Server jump straight to the dates
  it needs instead of reading the whole table as the data grows.

      table               index                          serves
      JobCardEntry        IX_JobCardEntry_EntryDate      forged qty, idle time, scrap (by entry date)
      JobCardInspection   IX_JobCardInspection_InspDate  rejection / rework, quality trend
      ProductionPlanDay   IX_ProductionPlanDay_PlanDate  planned qty, today's / tomorrow's plan
      StoreReturnsMst     IX_StoreReturnsMst_DocDate     bar-end scrap and end pieces
      SalesReturnMst      IX_SalesReturnMst_DocDate      customer returns
      STxnDtl             IX_STxnDtl_LinkNo              qty invoiced against an OA line
                                                         (order book, pending orders, planning)

  What this changes: NOTHING in the data or the columns. Nonclustered indexes
  only — a little more disk, and each insert / update on these tables also
  updates the index (a few extra milliseconds per saved document). Pages and
  procedures keep working exactly as before.

  Already present (left alone): GRNMst / POMst / OAMst / STxnMst DocDate,
  BreakdownMst machine, JobCardEntry job / die / machine.

  Idempotent - safe to re-run.   Run with:  sqlcmd -I -S <server> -d <db> -i 74_DashboardIndexes.sql
==============================================================================*/
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobCardEntry_EntryDate' AND object_id = OBJECT_ID('dbo.JobCardEntry'))
    CREATE NONCLUSTERED INDEX IX_JobCardEntry_EntryDate ON dbo.JobCardEntry (EntryDate)
        INCLUDE (MTransNo, StepSlNo, MachineNo, Qty, IdleMin, IdleReason, DeleteStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobCardInspection_InspDate' AND object_id = OBJECT_ID('dbo.JobCardInspection'))
    CREATE NONCLUSTERED INDEX IX_JobCardInspection_InspDate ON dbo.JobCardInspection (SubscID, InspDate)
        INCLUDE (JobCardTransNo, InspectedQty, OKQty, ReworkQty, RejectedQty, RejectionReason, DeleteStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductionPlanDay_PlanDate' AND object_id = OBJECT_ID('dbo.ProductionPlanDay'))
    CREATE NONCLUSTERED INDEX IX_ProductionPlanDay_PlanDate ON dbo.ProductionPlanDay (SubscID, PlanDate)
        INCLUDE (MTransNo, JobSeq, LineSlNo, PlanQty);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StoreReturnsMst_DocDate' AND object_id = OBJECT_ID('dbo.StoreReturnsMst'))
    CREATE NONCLUSTERED INDEX IX_StoreReturnsMst_DocDate ON dbo.StoreReturnsMst (SubscID, DocDate)
        INCLUDE (DeleteStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesReturnMst_DocDate' AND object_id = OBJECT_ID('dbo.SalesReturnMst'))
    CREATE NONCLUSTERED INDEX IX_SalesReturnMst_DocDate ON dbo.SalesReturnMst (SubscID, DocDate)
        INCLUDE (DeleteStatus, VendorNo, VendorName, CurrencyRate, LockStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_STxnDtl_LinkNo' AND object_id = OBJECT_ID('dbo.STxnDtl'))
    CREATE NONCLUSTERED INDEX IX_STxnDtl_LinkNo ON dbo.STxnDtl (LinkNo)
        INCLUDE (MTransNo, Qty, DeleteStatus);

SELECT OBJECT_NAME(i.object_id) AS [table], i.name AS [index]
  FROM sys.indexes i
 WHERE i.name IN ('IX_JobCardEntry_EntryDate', 'IX_JobCardInspection_InspDate', 'IX_ProductionPlanDay_PlanDate',
                  'IX_StoreReturnsMst_DocDate', 'IX_SalesReturnMst_DocDate', 'IX_STxnDtl_LinkNo')
 ORDER BY 1;
