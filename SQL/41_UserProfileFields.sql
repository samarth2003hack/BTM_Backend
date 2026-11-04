/*==============================================================================
  41. USER PROFILE - PERSONAL DETAILS ON dbo.UserMaster

  The My Profile page (CommonPages/Profile.html) lets a signed-in user edit
  their own record. Its personal fields had nowhere to live, so they are added
  to UserMaster itself - no new table:

      AltContactNo     VARCHAR(20)    alternative contact number
      DOB              DATE           date of birth (age is derived, not stored)
      Gender           VARCHAR(10)    'Male' | 'Female' | 'Other'
      MaritalStatus    VARCHAR(15)    'Married' | 'Unmarried'
      AnniversaryDate  DATE           only when MaritalStatus = 'Married'
      Religion         VARCHAR(50)
      Nationality      VARCHAR(50)
      Address          VARCHAR(250)
      AboutMe          VARCHAR(500)

  All nullable, so existing rows, SP_UserMaster (explicit column lists) and the
  generic TR_Audit_UserMaster are unaffected. The profile endpoints in
  UserMasterController write these columns directly; SP_UserMaster is not
  changed and still never touches them.

  ORDER MATTERS: the EF entity (Models/UserMaster.cs) maps these columns, so
  this script must run on a database BEFORE an API build that includes them
  is deployed against it - otherwise every full UserMaster read (sign-in
  included) fails with "Invalid column name".

  Idempotent - safe to run more than once.
==============================================================================*/
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.UserMaster', 'AltContactNo') IS NULL
    ALTER TABLE dbo.UserMaster ADD AltContactNo VARCHAR(20) NULL;
IF COL_LENGTH('dbo.UserMaster', 'DOB') IS NULL
    ALTER TABLE dbo.UserMaster ADD DOB DATE NULL;
IF COL_LENGTH('dbo.UserMaster', 'Gender') IS NULL
    ALTER TABLE dbo.UserMaster ADD Gender VARCHAR(10) NULL;
IF COL_LENGTH('dbo.UserMaster', 'MaritalStatus') IS NULL
    ALTER TABLE dbo.UserMaster ADD MaritalStatus VARCHAR(15) NULL;
IF COL_LENGTH('dbo.UserMaster', 'AnniversaryDate') IS NULL
    ALTER TABLE dbo.UserMaster ADD AnniversaryDate DATE NULL;
IF COL_LENGTH('dbo.UserMaster', 'Religion') IS NULL
    ALTER TABLE dbo.UserMaster ADD Religion VARCHAR(50) NULL;
IF COL_LENGTH('dbo.UserMaster', 'Nationality') IS NULL
    ALTER TABLE dbo.UserMaster ADD Nationality VARCHAR(50) NULL;
IF COL_LENGTH('dbo.UserMaster', 'Address') IS NULL
    ALTER TABLE dbo.UserMaster ADD Address VARCHAR(250) NULL;
IF COL_LENGTH('dbo.UserMaster', 'AboutMe') IS NULL
    ALTER TABLE dbo.UserMaster ADD AboutMe VARCHAR(500) NULL;
GO

PRINT '41_UserProfileFields applied.';
GO
