/*==============================================================================
  EMSSoft — 08_TestData.sql
  ------------------------------------------------------------------------------
  One company and one user copied across from the LMS database (LoanDRC), so the
  new front-end has realistic data to sign in against.

  OPTIONAL — the app is fully usable without this script. Skip it on any
  environment that should not carry LMS data.

  ------------------------------------------------------------------------------
  WHAT IS COPIED
  ------------------------------------------------------------------------------
    Company     LoanDRC "SUPREME FINANCE"  →  EMSSoft dbo.Company
    UserMaster  LoanDRC "biserp"           →  EMSSoft dbo.UserMaster

  The password column carries over byte for byte: both databases encrypt with
  the same TripleDES key, so the existing password keeps working.

      User Id  : biserp
      Password : bishubli@9343

  ------------------------------------------------------------------------------
  COLUMN MAPPING
  ------------------------------------------------------------------------------
  The two schemas differ, so this is a mapped copy rather than a straight one:

    LoanDRC.Company            EMSSoft.Company
    ---------------            ---------------
    CompanyCode             →  CompCode      ('-' in the source is not a usable
                                              code, so it becomes 'SF01')
    CompanyName             →  CompName
    TradingName             →  TradingName
    CompAddress             →  CompAddress
    PhoneNo                 →  PhoneNo
    EmailId                 →  GeneralEmail
    Website                 →  Website
    TINNo                   →  GSTIN         (nearest equivalent field)
    RCCMNo                  →  CIN           (nearest equivalent field)
    TxnCurrency             →  TxnCurrency   ('-' in the source is not a
                                              currency, so it becomes 'INR')
    ProvinceNo/TerritoryNo  →  (dropped — EMSSoft uses State/StateCode)
    LogoPath, POBox, Bank*  →  (dropped — no equivalent column)

    LoanDRC.UserMaster         EMSSoft.UserMaster
    ------------------         ------------------
    TemplateName 'Finance'  →  AccessTemplate 'Administrator'

  That last mapping is deliberate: EMSSoft has no 'Finance' template, and a user
  pointed at a template that does not exist gets an EMPTY menu. Mapping to
  'Administrator' means the test user can actually see the application. Create a
  'Finance' template on the Access Template screen and switch the user over when
  you want to test restricted access.

  LockStatus is forced to 'Y' — it is the ACTIVE flag, and anything else blocks
  sign-in.
==============================================================================*/

USE EMSSoft;
GO

SET NOCOUNT ON;
GO

DECLARE @Subsc INT = 1;
DECLARE @User  INT = 1;


/*------------------------------------------------------------------------------
  1. Company — SUPREME FINANCE
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM dbo.Company
                WHERE CompCode = 'SF01' AND DeleteStatus = 'N' AND SubscID = @Subsc)
BEGIN
    INSERT INTO dbo.Company
    (
        CompCode, CompName, TradingName, CompAddress,
        State, StateCode, PhoneNo, PinCode,
        GeneralEmail, SalesEmail, PurchaseEmail, Website,
        GSTIN, PAN, CIN,
        FromYear, ToYear, FYear, YearCode,
        TxnCurrency, CompDataSource,
        CreatedBy, DeleteStatus, LockStatus, SubscID
    )
    VALUES
    (
        'SF01',
        'SUPREME FINANCE',
        'SUPREME FINANCE',
        '12 eme rue, Limite, Kinshasa, DRC',
        'Karnataka', 29,
        '83473', 560001,
        'supremefinance@gmail.com', 'supremefinance@gmail.com', 'supremefinance@gmail.com',
        'www.supremefinance.com',
        '09876VDF',                              -- from TINNo
        NULL,
        'CD/KIN/RCCM/02-3-2025',                 -- from RCCMNo
        '2026-04-01', '2027-03-31', 2026, '2026-2027',
        'INR',                                   -- source held '-'
        'Copied from LoanDRC for testing',
        @User, 'N', 'Y', @Subsc
    );

    PRINT 'Company: SUPREME FINANCE inserted.';
END
ELSE
    PRINT 'Company: SUPREME FINANCE already present — skipped.';


/*------------------------------------------------------------------------------
  2. Branch for that company
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM dbo.BranchMaster
                WHERE BranchCode = 'SFHO' AND DeleteStatus = 'N' AND SubscID = @Subsc)
BEGIN
    INSERT INTO dbo.BranchMaster
    (
        BranchCode, BranchType, BranchName, BranchAddress,
        PhoneNo, EmailID, PinCode,
        CreatedBy, DeleteStatus, LockStatus, SubscID
    )
    VALUES
    (
        'SFHO', 'HEAD OFFICE', 'Supreme Finance - Head Office',
        '12 eme rue, Limite, Kinshasa, DRC',
        '83473', 'supremefinance@gmail.com', 560001,
        @User, 'N', 'Y', @Subsc
    );

    PRINT 'BranchMaster: Supreme Finance Head Office inserted.';
END
ELSE
    PRINT 'BranchMaster: Supreme Finance Head Office already present — skipped.';


/*------------------------------------------------------------------------------
  3. User — biserp / bishubli@9343
     PW is the ciphertext straight from LoanDRC; both databases use the same key.
------------------------------------------------------------------------------*/
IF NOT EXISTS (SELECT 1 FROM dbo.UserMaster
                WHERE UserId = 'biserp' AND DeleteStatus = 'N' AND SubscID = @Subsc)
BEGIN
    INSERT INTO dbo.UserMaster
    (
        UserName, UserId, PW, UserType,
        Designation, Department, ContactNo, EmailId,
        AccessTemplate, Branch,
        PWExpiry, BadLogins, LockCounter, HomePage,
        CreatedBy, DeleteStatus, LockStatus, SubscID
    )
    VALUES
    (
        'Gangadhar', 'biserp',
        'PImCuxZI5XJGgmNaHx71jw==',              -- TripleDES('bishubli@9343')
        'ADMIN',
        'Finance', 'Accounts', '9343045121', 'gangadhary@gmail.com',
        'Administrator',                         -- see the note in the header
        'Supreme Finance - Head Office',
        0, 5, 0, 'Modules/Home.html',
        @User, 'N', 'Y', @Subsc
    );

    PRINT 'UserMaster: biserp inserted — sign in with biserp / bishubli@9343';
END
ELSE
    PRINT 'UserMaster: biserp already present — skipped.';
GO

PRINT '08_TestData.sql complete.';
GO
