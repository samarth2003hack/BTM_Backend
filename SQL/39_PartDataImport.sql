/*==============================================================================
  39. PART DATA IMPORT

  Part Master import (Parts + Material Grades sheets) - PartDataImportController
  and EMSSoft/Modules/Masters/PartDataImport.html.

  No procedure changes: unlike Customer / Supplier (scripts 37 / 38) the Part
  Code is NOT generated - it is typed in the file and SP_PartMaster keeps
  requiring it.

  1. OtherMasterList rows for the five Other Master types PartMaster.html reads
     its dropdowns from - 'Part Category', 'Part Sub Category', 'Inventory
     Group', 'UOM', 'Store Location' - so the Other Master page can maintain
     them. (The import also registers a type it is the first to use.)

  2. Menu row 126 'Part Data Import' under Masters + AccessTemplate grants.

  ------------------------------------------------------------------------------
  CHECK BEFORE RUNNING
  ------------------------------------------------------------------------------
  * @Folder - on the dev box the page is in 'EMSSoft/Modules/Masters/'.
  * 11_MenulistFolders.sql deletes Category 'EMS' rows it does not list, so
    re-running it drops row 126 - re-run this script afterwards.

  Idempotent - safe to re-run.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

/*------------------------------------------------------------------------------
  1. OTHER MASTER TYPES used by the Part Master dropdowns
------------------------------------------------------------------------------*/
INSERT INTO dbo.OtherMasterList (MasterType, Department, DeleteStatus, SubscID)
SELECT  T.MasterType, 'Inventory', 'N', S.SubscID
  FROM  (VALUES ('Part Category'), ('Part Sub Category'), ('Inventory Group'),
                ('UOM'), ('Store Location')) T(MasterType)
 CROSS JOIN (SELECT DISTINCT SubscID FROM dbo.OtherMasterList WHERE SubscID IS NOT NULL) S
 WHERE NOT EXISTS (SELECT 1 FROM dbo.OtherMasterList L
                    WHERE L.MasterType = T.MasterType
                      AND L.SubscID    = S.SubscID
                      AND L.DeleteStatus = 'N');
GO

/*------------------------------------------------------------------------------
  2. MENU ROW + GRANTS  (MTransNo 126 is unused; after Supplier Data Import)
------------------------------------------------------------------------------*/
DECLARE @Folder VARCHAR(60) = 'Modules/Masters/';   -- <- check, see header

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
    (126, 'Masters', 1, 'Part Data Import', 26, 'bi-file-earmark-arrow-up', @Folder + 'PartDataImport.html');

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
