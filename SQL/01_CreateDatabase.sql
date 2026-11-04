/*==============================================================================
  EMSSoft — 01_CreateDatabase.sql
  ------------------------------------------------------------------------------
  Creates the EMSSoft database if it does not already exist.

  Run order:
      01_CreateDatabase.sql
      02_Tables.sql
      03_StoredProcedures.sql
      04_SeedData.sql

  Matches the connection string in appsettings.json:
      Server=DESKTOP-R3L704H\SQLEXPRESS;Database=EMSSoft;Trusted_Connection=True
==============================================================================*/

IF DB_ID('EMSSoft') IS NULL
BEGIN
    PRINT 'Creating database EMSSoft ...';
    CREATE DATABASE EMSSoft;
END
ELSE
BEGIN
    PRINT 'Database EMSSoft already exists — skipping create.';
END
GO

ALTER DATABASE EMSSoft SET RECOVERY SIMPLE;
GO

USE EMSSoft;
GO

PRINT 'Database EMSSoft ready.';
GO
