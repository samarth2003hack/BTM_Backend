/*==============================================================================
  52. PRODUCTION + MAINTENANCE LOOKUPS

  The Other Master types the production chain (Machine Master, Die Master,
  Routing Chart, Job Card, Job Card Status) and the maintenance department
  (PM Check Sheet, Breakdown) pick from. Each is maintained on the existing
  Other Master page; nothing here needs a screen of its own.

      Operation               operations a machine performs / a route step runs.
                              A route is inspected after its last "Inspection" step.
      Die Type                the die family a route needs (RoutingChartMst.DieType,
                              DieMst.DieType hold the NAME).
      Shift                   job card shift.
      Operator Name           job card operator, PM check sheet day operator.
      Maintenance Supervisor  PM check sheet final approval.
      Production Supervisor   PM check sheet department approval.
      Fitter / Electrician    maintenance staff who do checks and attend breakdowns.

  Defaults: a subscription that has NO "Operation" rows gets the forging
  operations, and one with NO "Shift" rows gets General/First/Second/Third.
  Existing rows are never touched. Rename or deactivate them in Other Master.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. Master types, per subscription
------------------------------------------------------------------------------*/
DECLARE @Types TABLE (MasterType VARCHAR(50) NOT NULL, Department VARCHAR(50) NOT NULL);
INSERT INTO @Types (MasterType, Department) VALUES
    ('Operation',              'Production'),
    ('Die Type',               'Production'),
    ('Shift',                  'Production'),
    ('Operator Name',          'Production'),
    ('Maintenance Supervisor', 'Maintenance'),
    ('Production Supervisor',  'Maintenance'),
    ('Fitter',                 'Maintenance'),
    ('Electrician',            'Maintenance');

INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, T.Department, 'N', S.SubscID
  FROM  @Types T
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMasterList L
            WHERE L.MasterType   = T.MasterType
              AND L.SubscID      = S.SubscID
              AND L.DeleteStatus = 'N');
GO

/*------------------------------------------------------------------------------
  2. Default values - only for a subscription with none of that type
------------------------------------------------------------------------------*/
DECLARE @Defaults TABLE (MasterType VARCHAR(50) NOT NULL, MasterName VARCHAR(50) NOT NULL, Ord INT NOT NULL);
INSERT INTO @Defaults (MasterType, MasterName, Ord) VALUES
    ('Operation', 'Cutting',        1),
    ('Operation', 'Heating',        2),
    ('Operation', 'Forging',        3),
    ('Operation', 'Trimming',       4),
    ('Operation', 'Heat Treatment', 5),
    ('Operation', 'Grinding',       6),
    ('Operation', 'Shot Blasting',  7),
    ('Operation', 'Machining',      8),
    ('Operation', 'Inspection',     9),
    ('Shift',     'General',        1),
    ('Shift',     'First',          2),
    ('Shift',     'Second',         3),
    ('Shift',     'Third',          4);

INSERT INTO dbo.OtherMaster
       (MasterType, MasterName, Department, SystemIP, CreatedBy, CreatedOn,
        DeleteStatus, LockStatus, LockCounter, SubscID)
SELECT  D.MasterType, D.MasterName, 'Production', 'SQL52', 1, GETDATE(),
        'N', 'Y', 0, S.SubscID
  FROM  @Defaults D
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (
           SELECT 1 FROM dbo.OtherMaster O
            WHERE O.MasterType   = D.MasterType
              AND O.SubscID      = S.SubscID
              AND O.DeleteStatus = 'N')
 ORDER BY S.SubscID, D.MasterType, D.Ord;
GO

-- What exists now.
SELECT L.SubscID, L.MasterType, L.Department,
       (SELECT COUNT(*) FROM dbo.OtherMaster O
         WHERE O.MasterType = L.MasterType AND O.SubscID = L.SubscID AND O.DeleteStatus = 'N') AS Rows_
  FROM dbo.OtherMasterList L
 WHERE L.DeleteStatus = 'N'
   AND L.MasterType IN ('Operation', 'Die Type', 'Shift', 'Operator Name',
                        'Maintenance Supervisor', 'Production Supervisor', 'Fitter', 'Electrician')
 ORDER BY L.SubscID, L.MasterType;
GO
