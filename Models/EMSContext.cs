using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// EF Core context for the EMSSoft database.
    ///
    /// Reads go through the DbSets below. Writes go through the Call*SP helpers,
    /// which all share one contract: an @ID output (affected MTransNo, or 0 on
    /// failure) and an @Message output carrying the result text. Controllers
    /// surface @Message to the caller verbatim.
    /// </summary>
    public partial class EMSContext : DbContext
    {
        public EMSContext(DbContextOptions<EMSContext> options) : base(options)
        {
        }

        public DbSet<Company> Company { get; set; } = null!;
        public DbSet<BranchMaster> BranchMaster { get; set; } = null!;
        public DbSet<BankMaster> BankMaster { get; set; } = null!;
        public DbSet<CurrencyMaster> CurrencyMaster { get; set; } = null!;
        public DbSet<StateMaster> StateMaster { get; set; } = null!;
        public DbSet<UserMaster> UserMaster { get; set; } = null!;
        public DbSet<AccessTemplate> AccessTemplate { get; set; } = null!;
        public DbSet<OtherMaster> OtherMaster { get; set; } = null!;
        public DbSet<OtherMasterList> OtherMasterList { get; set; } = null!;
        public DbSet<Menulist> Menulist { get; set; } = null!;
        public DbSet<GeneralConfig> GeneralConfig { get; set; } = null!;
        public DbSet<EinvSetup> EinvSetup { get; set; } = null!;
        public DbSet<EinvDtl> EinvDtl { get; set; } = null!;
        public DbSet<AccessLog> AccessLog { get; set; } = null!;
        public DbSet<ErrorLog> ErrorLog { get; set; } = null!;

        // Data-change trail. Written by the TR_Audit_* triggers, never by the
        // API — see SQL/12_AuditLog.sql. Read-only from here.
        public DbSet<AuditLog> AuditLog { get; set; } = null!;
        public DbSet<AuditLogDtl> AuditLogDtl { get; set; } = null!;
        public DbSet<AuditConfig> AuditConfig { get; set; } = null!;
        public DbSet<AuditLogView> AuditLogView { get; set; } = null!;

        // Accounting module
        public DbSet<GroupHeads> GroupHeads { get; set; } = null!;
        public DbSet<AccountsMaster> AccountsMaster { get; set; } = null!;
        public DbSet<AccountConfig> AccountConfig { get; set; } = null!;
        public DbSet<TaxMaster> TaxMaster { get; set; } = null!;
        public DbSet<GroupView> GroupView { get; set; } = null!;

        // Trading / inventory masters
        public DbSet<CustomerMaster> CustomerMaster { get; set; } = null!;
        public DbSet<CustomerContacts> CustomerContacts { get; set; } = null!;
        public DbSet<CustomerTerm> CustomerTerm { get; set; } = null!;
        public DbSet<ShippingAddress> ShippingAddress { get; set; } = null!;
        public DbSet<StandardTerms> StandardTerms { get; set; } = null!;
        public DbSet<SupplierMaster> SupplierMaster { get; set; } = null!;
        public DbSet<SupplierContacts> SupplierContacts { get; set; } = null!;
        public DbSet<SupplierTerm> SupplierTerm { get; set; } = null!;
        public DbSet<PartMaster> PartMaster { get; set; } = null!;
        public DbSet<PartCodeMaster> PartCodeMaster { get; set; } = null!;
        public DbSet<PartParameterList> PartParameterList { get; set; } = null!;
        public DbSet<DrawingNos> DrawingNos { get; set; } = null!;
        public DbSet<MaterialGrade> MaterialGrade { get; set; } = null!;
        public DbSet<MaterialStd> MaterialStd { get; set; } = null!;
        public DbSet<HSNMaster> HSNMaster { get; set; } = null!;
        public DbSet<PriceList> PriceList { get; set; } = null!;
        public DbSet<MOCMaster> MOCMaster { get; set; } = null!;

        public DbSet<MOCTemplate> MOCTemplates { get; set; } = null!;
        public DbSet<ChemicalMaster> ChemicalMaster { get; set; } = null!;

        public DbSet<MechanicalMaster> MechanicalMaster { get; set; } = null!;

        public DbSet<MaterialComposition> MaterialComposition { get; set; } = null!;

        public DbSet<SalesTeam> SalesTeam { get; set; } = null!;
        public DbSet<FeedbackMasterSales> FeedbackMasterSales { get; set; } = null!;

        public DbSet<PurReqMst> PurReqMst { get; set; } = null!;
        public DbSet<PurReqDtl> PurReqDtl { get; set; } = null!;
        public DbSet<RFQMst> RFQMst { get; set; } = null!;
        public DbSet<RFQDtl> RFQDtl { get; set; } = null!;
        public DbSet<RFQSupplier> RFQSupplier { get; set; } = null!;
        public DbSet<RFQTerms> RFQTerms { get; set; } = null!;
        public DbSet<RFQPriceMst> RFQPriceMst { get; set; } = null!;
        public DbSet<RFQPriceDtl> RFQPriceDtl { get; set; } = null!;
        public DbSet<RFQPriceTerms> RFQPriceTerms { get; set; } = null!;

        // Purchase order
        public DbSet<POMst> POMst { get; set; } = null!;
        public DbSet<PODtl> PODtl { get; set; } = null!;
        public DbSet<POTerms> POTerms { get; set; } = null!;
        public DbSet<POExtras> POExtras { get; set; } = null!;

        // Goods receipt and the inward flow
        public DbSet<GRNMst> GRNMst { get; set; } = null!;
        public DbSet<GRNDtl> GRNDtl { get; set; } = null!;
        public DbSet<GRNNCDtl> GRNNCDtl { get; set; } = null!;
        public DbSet<GRNQRCode> GRNQRCode { get; set; } = null!;
        public DbSet<GRNAttachment> GRNAttachment { get; set; } = null!;

        // Store and heat traceability
        public DbSet<StoreLocation> StoreLocation { get; set; } = null!;
        public DbSet<HeatNoMst> HeatNoMst { get; set; } = null!;
        public DbSet<HeatNoDtl> HeatNoDtl { get; set; } = null!;
        public DbSet<HeatRefNo> HeatRefNo { get; set; } = null!;

        // Read-only views behind the purchase-order source picker, the GRN
        // screen's PO balance, and the three inward worklists.
        public DbSet<PurReqForPO> PurReqForPO { get; set; } = null!;
        public DbSet<QuoteForPO> QuoteForPO { get; set; } = null!;
        public DbSet<PurReqItemsForPO> PurReqItemsForPO { get; set; } = null!;
        public DbSet<POPendingGRN> POPendingGRN { get; set; } = null!;
        public DbSet<GRNInwardStatus> GRNInwardStatus { get; set; } = null!;
        public DbSet<RFQApprovalList> RFQApprovalList { get; set; } = null!;

        public DbSet<LeadGeneration> LeadGeneration { get; set; } = null!;
        public DbSet<EnqMst> EnqMst { get; set; } = null!;
        public DbSet<EnqDtl> EnqDtl { get; set; } = null!;
        public DbSet<EnqFollowUp> EnqFollowUp { get; set; } = null!;
        public DbSet<ENQFeasibilityMst> ENQFeasibilityMst { get; set; } = null!;
        public DbSet<OAMst> OAMst { get; set; } = null!;
        public DbSet<OADtl> OADtl { get; set; } = null!;
        public DbSet<OATerms> OATerms { get; set; } = null!;
        public DbSet<OAExtras> OAExtras { get; set; } = null!;
        public DbSet<OANotes> OANotes { get; set; } = null!;
        public DbSet<OAAttachment> OAAttachment { get; set; } = null!;

        public DbSet<QtnMst> QtnMst { get; set; } = null!;
        public DbSet<QtnDtl> QtnDtl { get; set; } = null!;
        public DbSet<QtnTerms> QtnTerms { get; set; } = null!;
        public DbSet<QtnExtras> QtnExtras { get; set; } = null!;
        public DbSet<QTNNotes> QTNNotes { get; set; } = null!;
        public DbSet<QtnAttachment> QtnAttachment { get; set; } = null!;
        public DbSet<RGPMst> RGPMst { get; set; } = null!;
        public DbSet<RGPDtl> RGPDtl { get; set; } = null!;
        public DbSet<RGPTerms> RGPTerms { get; set; } = null!;
        public DbSet<RGPInwMst> RGPInwMst { get; set; } = null!;
        public DbSet<RGPInwDtl> RGPInwDtl { get; set; } = null!;
        public DbSet<ExtraTermsMaster> ExtraTermsMaster { get; set; } = null!;

        public DbSet<SalesReturnMst> SalesReturnMst { get; set; } = null!;
        public DbSet<SalesReturnDtl> SalesReturnDtl { get; set; } = null!;
        public DbSet<MachineMst> MachineMst { get; set; } = null!;
        public DbSet<DieMst> DieMst { get; set; } = null!;
        public DbSet<RoutingChartMst> RoutingChartMst { get; set; } = null!;
        public DbSet<RoutingChartDtl> RoutingChartDtl { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Menulist.MTransNo is the hand-assigned module number, not an identity.
            modelBuilder.Entity<Menulist>()
                .Property(x => x.MTransNo)
                .ValueGeneratedNever();

            // GroupView is a read-only view over the GroupHeads tree.
            modelBuilder.Entity<GroupView>()
                .HasNoKey()
                .ToView("GroupView");

            // VW_AuditLog is the trail with the changing user's name resolved.
            modelBuilder.Entity<AuditLogView>()
                .HasNoKey()
                .ToView("VW_AuditLog");

            // The purchase and inward views. Keyless and mapped with ToView,
            // so the HasTrigger loop at the foot of this method skips them —
            // nothing is ever written through these.
            modelBuilder.Entity<PurReqForPO>().HasNoKey().ToView("VW_PurReqForPO");
            modelBuilder.Entity<QuoteForPO>().HasNoKey().ToView("VW_QuoteForPO");
            modelBuilder.Entity<PurReqItemsForPO>().HasNoKey().ToView("VW_PurReqItemsForPO");
            modelBuilder.Entity<POPendingGRN>().HasNoKey().ToView("VW_POPendingGRN");
            modelBuilder.Entity<GRNInwardStatus>().HasNoKey().ToView("VW_GRNInwardStatus");
            modelBuilder.Entity<RFQApprovalList>().HasNoKey().ToView("VW_RFQApprovalList");

            // The trading masters carry numeric() columns of several precisions.
            // Spelling each out keeps EF from assuming decimal(18,2) and silently
            // rounding rates and weights on the way in and out.
            modelBuilder.Entity<CustomerMaster>()
                .Property(x => x.CreditLimit).HasColumnType("numeric(18,2)");

            modelBuilder.Entity<CustomerTerm>()
                .Property(x => x.TermSlNo).HasColumnType("numeric(10,2)");

            modelBuilder.Entity<SupplierTerm>()
                .Property(x => x.TermSlNo).HasColumnType("numeric(10,2)");

            modelBuilder.Entity<StandardTerms>()
                .Property(x => x.TermSlNo).HasColumnType("numeric(10,2)");

            modelBuilder.Entity<PartMaster>(part =>
            {
                part.Property(x => x.TaxP).HasColumnType("numeric(6,2)");
                part.Property(x => x.ConversionRatio).HasColumnType("numeric(14,4)");
                part.Property(x => x.PackQty).HasColumnType("numeric(14,2)");
                part.Property(x => x.PackSize).HasColumnType("numeric(14,2)");
                part.Property(x => x.MinStock).HasColumnType("numeric(14,2)");
                part.Property(x => x.MaxStock).HasColumnType("numeric(14,2)");
                part.Property(x => x.MOQ).HasColumnType("numeric(14,2)");
                part.Property(x => x.ShelfLife).HasColumnType("numeric(14,2)");
                part.Property(x => x.CostPrice).HasColumnType("numeric(18,2)");
                part.Property(x => x.SalesPrice).HasColumnType("numeric(18,2)");
                part.Property(x => x.MRP).HasColumnType("numeric(18,2)");
                part.Property(x => x.PartLength).HasColumnType("numeric(14,2)");
                part.Property(x => x.PartWidth).HasColumnType("numeric(14,2)");
                part.Property(x => x.PartHeight).HasColumnType("numeric(14,2)");
                part.Property(x => x.PartWeight).HasColumnType("numeric(14,3)");
            });

            modelBuilder.Entity<MaterialGrade>(grade =>
            {
                grade.Property(x => x.MinStock).HasColumnType("numeric(14,2)");
                grade.Property(x => x.MaxStock).HasColumnType("numeric(14,2)");
                grade.Property(x => x.MOQ).HasColumnType("numeric(14,2)");
            });

            modelBuilder.Entity<PriceList>(price =>
            {
                price.Property(x => x.Rate).HasColumnType("numeric(18,4)");
                price.Property(x => x.EffectiveDt).HasColumnType("date");
            });

            modelBuilder.Entity<DrawingNos>()
                .Property(x => x.RevDate).HasColumnType("date");

            // SaveChanges AGAINST TABLES THAT HAVE TRIGGERS
            //
            // To read an identity value back, EF Core 7 writes
            //     INSERT ... OUTPUT INSERTED.MTransNo VALUES ...
            // and SQL Server rejects a bare OUTPUT clause on any table
            // carrying a trigger. SQL/12_AuditLog.sql puts an audit trigger on
            // nearly every table here, so without this every SaveChanges would
            // fail — as login did, being one of the few writes that does not
            // go through a stored procedure.
            //
            // Telling EF a trigger exists switches it to the OUTPUT ... INTO
            // form, which SQL Server does allow. EF never looks the name up;
            // it only needs to know there is one. Applied to every mapped
            // table rather than a list, so a table added later cannot be
            // missed — and so nobody has to remember this when adding one.
            //
            // Keyless entities mapped with ToView return no table name and are
            // skipped: they are never written to.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var table = entityType.GetTableName();

                if (string.IsNullOrEmpty(table))
                    continue;

                modelBuilder.Entity(entityType.ClrType)
                    .ToTable(tb => tb.HasTrigger($"TR_Audit_{table}"));
            }
        }

        //======================================================================
        // Helpers
        //======================================================================

        private static SqlParameter Out(string name) =>
            new SqlParameter(name, SqlDbType.Int) { Direction = ParameterDirection.Output };

        private static SqlParameter OutMsg() =>
            new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

        private static SqlParameter P(string name, object? value) =>
            new SqlParameter(name, value ?? DBNull.Value);

        // Trims and normalises an incoming string: empty becomes NULL so the
        // procedures' "IS NULL OR = ''" checks behave consistently.
        private static object Txt(string? value) =>
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

        private static (int ID, string Message) Result(SqlParameter id, SqlParameter msg) =>
            (id.Value is int i ? i : 0, msg.Value?.ToString() ?? string.Empty);

        // A decimal passed through P() arrives as decimal(18,0) and loses its
        // fractional part on the way into SQL Server — a 12.5 kg receipt books
        // as 13. Money and quantity parameters therefore go through Dec(),
        // which states the precision the procedure declares.
        private static SqlParameter Dec(string name, decimal? value, byte precision, byte scale) =>
            new SqlParameter(name, SqlDbType.Decimal)
            {
                Precision = precision,
                Scale = scale,
                Value = (object?)value ?? DBNull.Value
            };

        // DATE, not DATETIME2: the procedures' date parameters are DATE, and
        // letting ADO.NET infer DateTime would send a time component with it.
        private static SqlParameter Dt(string name, DateTime? value) =>
            new SqlParameter(name, SqlDbType.Date) { Value = (object?)value ?? DBNull.Value };

        private static SqlParameter Dt2(string name, DateTime? value) =>
            new SqlParameter(name, SqlDbType.DateTime2) { Value = (object?)value ?? DBNull.Value };


        //======================================================================
        // SP_Company
        //======================================================================
        public async Task<(int ID, string Message)> CallCompanySP(SP_Company dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_Company @ID OUTPUT, @MTransNo, @MCount, @CompCode, @CompName, @TradingName,
                  @CompAddress, @State, @StateCode, @PhoneNo, @PinCode, @SalesPhoneNo, @PurchasePhoneNo,
                  @GeneralEmail, @SalesEmail, @PurchaseEmail, @Website, @GSTIN, @PAN, @CIN, @IECNO,
                  @FromYear, @ToYear, @FYear, @YearCode, @TxnCurrency, @CompDataSource,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@CompCode", Txt(dto.CompCode)),
                P("@CompName", Txt(dto.CompName)),
                P("@TradingName", Txt(dto.TradingName)),
                P("@CompAddress", Txt(dto.CompAddress)),
                P("@State", Txt(dto.State)),
                P("@StateCode", dto.StateCode),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@PinCode", dto.PinCode),
                P("@SalesPhoneNo", Txt(dto.SalesPhoneNo)),
                P("@PurchasePhoneNo", Txt(dto.PurchasePhoneNo)),
                P("@GeneralEmail", Txt(dto.GeneralEmail)),
                P("@SalesEmail", Txt(dto.SalesEmail)),
                P("@PurchaseEmail", Txt(dto.PurchaseEmail)),
                P("@Website", Txt(dto.Website)),
                P("@GSTIN", Txt(dto.GSTIN)),
                P("@PAN", Txt(dto.PAN)),
                P("@CIN", Txt(dto.CIN)),
                P("@IECNO", Txt(dto.IECNO)),
                P("@FromYear", dto.FromYear),
                P("@ToYear", dto.ToYear),
                P("@FYear", dto.FYear),
                P("@YearCode", Txt(dto.YearCode)),
                P("@TxnCurrency", Txt(dto.TxnCurrency)),
                P("@CompDataSource", Txt(dto.CompDataSource)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_BranchMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallBranchMasterSP(SP_BranchMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_BranchMaster @ID OUTPUT, @MTransNo, @MCount, @BranchCode, @BranchType, @BranchName,
                  @BranchAddress, @PhoneNo, @EmailID, @PinCode, @GSTIN, @CIN, @Remarks, @BranchUser,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@BranchCode", Txt(dto.BranchCode)),
                P("@BranchType", Txt(dto.BranchType)),
                P("@BranchName", Txt(dto.BranchName)),
                P("@BranchAddress", Txt(dto.BranchAddress)),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@EmailID", Txt(dto.EmailID)),
                P("@PinCode", dto.PinCode),
                P("@GSTIN", Txt(dto.GSTIN)),
                P("@CIN", Txt(dto.CIN)),
                P("@Remarks", Txt(dto.Remarks)),
                P("@BranchUser", dto.BranchUser),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_BankMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallBankMasterSP(SP_BankMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_BankMaster @ID OUTPUT, @MTransNo, @MCount, @BankCode, @BankType, @BankName,
                  @BankID, @IFSCCode, @BranchName, @BranchCode, @Location, @MICRCode,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@BankCode", Txt(dto.BankCode)),
                P("@BankType", Txt(dto.BankType)),
                P("@BankName", Txt(dto.BankName)),
                P("@BankID", Txt(dto.BankID)),
                P("@IFSCCode", Txt(dto.IFSCCode)),
                P("@BranchName", Txt(dto.BranchName)),
                P("@BranchCode", Txt(dto.BranchCode)),
                P("@Location", Txt(dto.Location)),
                P("@MICRCode", Txt(dto.MICRCode)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_CurrencyMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallCurrencyMasterSP(SP_CurrencyMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_CurrencyMaster @ID OUTPUT, @MTransNo, @MCount, @Country, @Currency, @CurrencyCode,
          @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
          @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@Country", Txt(dto.Country)),
                P("@Currency", Txt(dto.Currency)),
                P("@CurrencyCode", Txt(dto.CurrencyCode)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }
        //======================================================================
        // SP_StateMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallStateMasterSP(SP_StateMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_StateMaster @ID OUTPUT, @MTransNo, @MCount, @State, @StateCode,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@State", Txt(dto.State)),
                P("@StateCode", dto.StateCode),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_UserMaster   (dto.PW must already be encrypted by the caller)
        //======================================================================
        public async Task<(int ID, string Message)> CallUserMasterSP(SP_UserMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_UserMaster @ID OUTPUT, @MTransNo, @MCount, @UserName, @UserId, @PW, @UserType,
                  @Designation, @Department, @ContactNo, @EmailId, @ImageFile, @AccessTemplate, @Branch,
                  @PWExpiry, @BadLogins, @HomePage, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@UserName", Txt(dto.UserName)),
                P("@UserId", Txt(dto.UserId)),
                P("@PW", Txt(dto.PW)),
                P("@UserType", Txt(dto.UserType)),
                P("@Designation", Txt(dto.Designation)),
                P("@Department", Txt(dto.Department)),
                P("@ContactNo", Txt(dto.ContactNo)),
                P("@EmailId", Txt(dto.EmailId)),
                P("@ImageFile", Txt(dto.ImageFile)),
                P("@AccessTemplate", Txt(dto.AccessTemplate)),
                P("@Branch", Txt(dto.Branch)),
                P("@PWExpiry", dto.PWExpiry),
                P("@BadLogins", dto.BadLogins),
                P("@HomePage", Txt(dto.HomePage)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_OtherMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallOtherMasterSP(SP_OtherMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_OtherMaster @ID OUTPUT, @MTransNo, @MCount, @MasterType, @MasterName, @Department,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@MasterType", Txt(dto.MasterType)),
                P("@MasterName", Txt(dto.MasterName)),
                P("@Department", Txt(dto.Department)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_OtherMasterList
        //======================================================================
        public async Task<(int ID, string Message)> CallOtherMasterListSP(SP_OtherMasterList dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_OtherMasterList @ID OUTPUT, @MTransNo, @MasterType, @Department, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MasterType", Txt(dto.MasterType)),
                P("@Department", Txt(dto.Department)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_Menulist
        //======================================================================
        public async Task<(int ID, string Message)> CallMenulistSP(SP_Menulist dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_Menulist @ID OUTPUT, @MTransNo, @Category, @CSLNo, @Section, @SSLNo,
                  @ModuleName, @MSLNo, @ModuleIcon, @ModulePage, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@Category", Txt(dto.Category)),
                P("@CSLNo", dto.CSLNo),
                P("@Section", Txt(dto.Section)),
                P("@SSLNo", dto.SSLNo),
                P("@ModuleName", Txt(dto.ModuleName)),
                P("@MSLNo", dto.MSLNo),
                P("@ModuleIcon", Txt(dto.ModuleIcon)),
                P("@ModulePage", Txt(dto.ModulePage)),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_AccessTemplate
        //======================================================================
        public async Task<(int ID, string Message)> CallAccessTemplateSP(SP_AccessTemplate dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AccessTemplate @ID OUTPUT, @MTransNo, @TemplateName, @Department, @ModuleNo,
                  @AuthType, @MyFavorite, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@TemplateName", Txt(dto.TemplateName)),
                P("@Department", Txt(dto.Department)),
                P("@ModuleNo", dto.ModuleNo),
                P("@AuthType", Txt(dto.AuthType)),
                P("@MyFavorite", Txt(dto.MyFavorite)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_GeneralConfig
        //======================================================================
        public async Task<(int ID, string Message)> CallGeneralConfigSP(SP_GeneralConfig dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_GeneralConfig @ID OUTPUT, @MTransNo, @MCount, @ConfigKey, @ConfigValue,
                  @ConfigDepartment, @CreatedBy, @CreatedOn, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@ConfigKey", Txt(dto.ConfigKey)),
                P("@ConfigValue", (object?)dto.ConfigValue ?? DBNull.Value),
                P("@ConfigDepartment", Txt(dto.ConfigDepartment)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_EinvSetup
        //======================================================================
        public async Task<(int ID, string Message)> CallEinvSetupSP(SP_EinvSetup dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_EinvSetup @ID OUTPUT, @MTransNo, @EUserID, @EPassword, @AppKey, @AuthToken,
                  @Sek, @TokenExp, @EwbAppKey, @EwbAuthToken, @EwbSEK, @EwbTokenExp,
                  @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@EUserID", Txt(dto.EUserID)),
                P("@EPassword", Txt(dto.EPassword)),
                P("@AppKey", Txt(dto.AppKey)),
                P("@AuthToken", Txt(dto.AuthToken)),
                P("@Sek", Txt(dto.Sek)),
                P("@TokenExp", Txt(dto.TokenExp)),
                P("@EwbAppKey", Txt(dto.EwbAppKey)),
                P("@EwbAuthToken", Txt(dto.EwbAuthToken)),
                P("@EwbSEK", Txt(dto.EwbSEK)),
                P("@EwbTokenExp", Txt(dto.EwbTokenExp)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_EinvDtl
        //======================================================================
        public async Task<(int ID, string Message)> CallEinvDtlSP(SP_EinvDtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_EinvDtl @ID OUTPUT, @TransSlNo, @MTransNo, @IRNNo, @ScanQRNo, @ACKNo,
                  @EWBillNo, @EWBillDt, @AckDt, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@IRNNo", Txt(dto.IRNNo)),
                P("@ScanQRNo", Txt(dto.ScanQRNo)),
                P("@ACKNo", Txt(dto.ACKNo)),
                P("@EWBillNo", Txt(dto.EWBillNo)),
                P("@EWBillDt", Txt(dto.EWBillDt)),
                P("@AckDt", Txt(dto.AckDt)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_AccessLog
        //======================================================================
        public async Task<(int ID, string Message)> CallAccessLogSP(SP_AccessLog dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AccessLog @ID OUTPUT, @MTransNo, @IPAddress, @LatLong, @Location,
                  @LoginType, @LoginId, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@IPAddress", Txt(dto.IPAddress)),
                P("@LatLong", Txt(dto.LatLong)),
                P("@Location", Txt(dto.Location)),
                P("@LoginType", Txt(dto.LoginType)),
                P("@LoginId", Txt(dto.LoginId)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_ErrorLog
        //======================================================================
        public async Task<(int ID, string Message)> CallErrorLogSP(SP_ErrorLog dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_ErrorLog @ID OUTPUT, @ErrorMsg, @StackTrace, @LoginType, @LoginId, @SubscID, @Message OUTPUT",
                id,
                P("@ErrorMsg", Txt(dto.ErrorMsg)),
                P("@StackTrace", Txt(dto.StackTrace)),
                P("@LoginType", Txt(dto.LoginType)),
                P("@LoginId", Txt(dto.LoginId)),
                P("@SubscID", dto.SubscID),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // Audit trail maintenance
        //
        // These two are the exceptions to the @ID / @Message contract above:
        // neither touches a record, so there is no ID to return — only the
        // @Message the procedure builds.
        //======================================================================

        /// <summary>
        /// Registers any table that has appeared since the last run and builds
        /// its audit trigger. Pass rebuild = true to recreate every trigger,
        /// which is how a change to the trigger body is rolled out.
        /// </summary>
        public async Task<string> CallAuditLogSyncSP(string? table = null, bool rebuild = false)
        {
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AuditLog_Sync @Table, @Rebuild, @Message OUTPUT",
                P("@Table", Txt(table)),
                P("@Rebuild", rebuild ? "Y" : "N"),
                msg);

            return msg.Value?.ToString() ?? string.Empty;
        }

        /// <summary>Drops trail entries older than keepDays, in batches.</summary>
        public async Task<string> CallAuditLogPurgeSP(int keepDays, int? subscID)
        {
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AuditLog_Purge @KeepDays, @SubscID, @BatchSize, @Message OUTPUT",
                P("@KeepDays", keepDays),
                P("@SubscID", subscID),
                P("@BatchSize", 5000),
                msg);

            return msg.Value?.ToString() ?? string.Empty;
        }

        //======================================================================
        // SP_GroupHeads
        //======================================================================
        public async Task<(int ID, string Message)> CallGroupHeadsSP(SP_GroupHeads dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_GroupHeads @ID OUTPUT, @MCount, @GrpNo, @GrpName, @GrpMastNo, @GrpStatus,
                  @BSStatus, @FAStatus, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MCount", dto.MCount),
                P("@GrpNo", dto.GrpNo),
                P("@GrpName", Txt(dto.GrpName)),
                P("@GrpMastNo", dto.GrpMastNo),
                P("@GrpStatus", Txt(dto.GrpStatus)),
                P("@BSStatus", Txt(dto.BSStatus)),
                P("@FAStatus", Txt(dto.FAStatus)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_AccountMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallAccountMasterSP(SP_AccountMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AccountMaster @ID OUTPUT, @AcNo, @MCount, @AcName, @AcAlias, @AcType,
                  @VendNo, @GroupHeadNo, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@AcNo", dto.AcNo),
                P("@MCount", dto.MCount),
                P("@AcName", Txt(dto.AcName)),
                P("@AcAlias", Txt(dto.AcAlias)),
                P("@AcType", Txt(dto.AcType)),
                P("@VendNo", dto.VendNo),
                P("@GroupHeadNo", dto.GroupHeadNo),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_TaxMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallTaxMasterSP(SP_TaxMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_TaxMaster @ID OUTPUT, @MTransNo, @MCount, @TaxCode, @TaxP, @CessP,
                  @CGSTAcNo, @SGSTAcNo, @IGSTAcNo, @CessAcNo, @PSACNo, @UsedIn,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID,
                  @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@TaxCode", Txt(dto.TaxCode)),
                P("@TaxP", dto.TaxP),
                P("@CessP", dto.CessP),
                P("@CGSTAcNo", dto.CGSTAcNo),
                P("@SGSTAcNo", dto.SGSTAcNo),
                P("@IGSTAcNo", dto.IGSTAcNo),
                P("@CessAcNo", dto.CessAcNo),
                P("@PSACNo", dto.PSACNo),
                P("@UsedIn", Txt(dto.UsedIn)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_AccountConfig
        //======================================================================
        public async Task<(int ID, string Message)> CallAccountConfigSP(SP_AccountConfig dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_AccountConfig @ID OUTPUT, @MTransNo, @ModuleName, @ACCType, @AcNo,
                  @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@ModuleName", Txt(dto.ModuleName)),
                P("@ACCType", Txt(dto.ACCType)),
                P("@AcNo", dto.AcNo),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_CustomerMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallCustomerMasterSP(SP_CustomerMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_CustomerMaster @ID OUTPUT, @MTransNo, @MCount, @CustomerCode, @CustomerType,
                  @CustomerName, @AliasName, @Category, @CustomerAddress, @PinCode, @PinCodeLocation,
                  @StateName, @StateCode, @PhoneNo, @MobileNo, @EmailID, @Website, @TaxType, @GSTIN, @PAN,
                  @TxnCurrency, @SalesExec, @CreditPeriod, @CreditLimit, @CustomerPriceList, @VendorCode,
                  @Remarks, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID,
                  @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@CustomerCode", Txt(dto.CustomerCode)),
                P("@CustomerType", Txt(dto.CustomerType)),
                P("@CustomerName", Txt(dto.CustomerName)),
                P("@AliasName", Txt(dto.AliasName)),
                P("@Category", Txt(dto.Category)),
                P("@CustomerAddress", Txt(dto.CustomerAddress)),
                P("@PinCode", Txt(dto.PinCode)),
                P("@PinCodeLocation", Txt(dto.PinCodeLocation)),
                P("@StateName", Txt(dto.StateName)),
                P("@StateCode", dto.StateCode),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@MobileNo", Txt(dto.MobileNo)),
                P("@EmailID", Txt(dto.EmailID)),
                P("@Website", Txt(dto.Website)),
                P("@TaxType", Txt(dto.TaxType)),
                P("@GSTIN", Txt(dto.GSTIN)),
                P("@PAN", Txt(dto.PAN)),
                P("@TxnCurrency", Txt(dto.TxnCurrency)),
                P("@SalesExec", Txt(dto.SalesExec)),
                P("@CreditPeriod", dto.CreditPeriod),
                P("@CreditLimit", dto.CreditLimit),
                P("@CustomerPriceList", Txt(dto.CustomerPriceList)),
                P("@VendorCode", Txt(dto.VendorCode)),
                P("@Remarks", Txt(dto.Remarks)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_CustomerContacts   (@MTransNo is the customer, @TransSlNo the row)
        //======================================================================
        public async Task<(int ID, string Message)> CallCustomerContactsSP(SP_CustomerContacts dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_CustomerContacts @ID OUTPUT, @TransSlNo, @MTransNo, @MCount, @Title, @CPName,
                  @Designation, @PhoneNo, @MobileNo, @Email, @SystemIP, @LockStatus, @CreatedBy,
                  @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@Title", Txt(dto.Title)),
                P("@CPName", Txt(dto.CPName)),
                P("@Designation", Txt(dto.Designation)),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@MobileNo", Txt(dto.MobileNo)),
                P("@Email", Txt(dto.Email)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_CustomerTerm
        //======================================================================
        public async Task<(int ID, string Message)> CallCustomerTermSP(SP_CustomerTerm dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_CustomerTerm @ID OUTPUT, @TransSlNo, @MTransNo, @MCount, @TermSlNo, @TermTitle,
                  @TermDescription, @TermType, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@TermSlNo", dto.TermSlNo),
                P("@TermTitle", Txt(dto.TermTitle)),
                P("@TermDescription", Txt(dto.TermDescription)),
                P("@TermType", Txt(dto.TermType)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_ShippingAddress
        //======================================================================
        public async Task<(int ID, string Message)> CallShippingAddressSP(SP_ShippingAddress dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_ShippingAddress @ID OUTPUT, @TransSlNo, @MTransNo, @MCount, @ShipName, @ShipAlias,
                  @ShipAddress, @ShipState, @ShipStateCode, @PinCodeLocation, @PinCode, @PhoneNo, @MobileNo,
                  @EmailID, @GSTIN, @PAN, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason,
                  @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@ShipName", Txt(dto.ShipName)),
                P("@ShipAlias", Txt(dto.ShipAlias)),
                P("@ShipAddress", Txt(dto.ShipAddress)),
                P("@ShipState", Txt(dto.ShipState)),
                P("@ShipStateCode", dto.ShipStateCode),
                P("@PinCodeLocation", Txt(dto.PinCodeLocation)),
                P("@PinCode", Txt(dto.PinCode)),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@MobileNo", Txt(dto.MobileNo)),
                P("@EmailID", Txt(dto.EmailID)),
                P("@GSTIN", Txt(dto.GSTIN)),
                P("@PAN", Txt(dto.PAN)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_StandardTerms
        //======================================================================
        public async Task<(int ID, string Message)> CallStandardTermsSP(SP_StandardTerms dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_StandardTerms @ID OUTPUT, @MTransNo, @MCount, @TermType, @TermSlNo, @TermTitle,
                  @TermDescription, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason,
                  @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@TermType", Txt(dto.TermType)),
                P("@TermSlNo", dto.TermSlNo),
                P("@TermTitle", Txt(dto.TermTitle)),
                P("@TermDescription", Txt(dto.TermDescription)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_SupplierMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallSupplierMasterSP(SP_SupplierMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_SupplierMaster @ID OUTPUT, @MTransNo, @MCount, @SupplierCode, @SupplierType,
                  @SupplierName, @AliasName, @Category, @SupplierAddress, @StateName, @StateCode, @PinCode,
                  @PinCodeLocation, @PhoneNo, @MobileNo, @EmailID, @Website, @TaxType, @GSTIN, @PAN,
                  @TxnCurrency, @CreditPeriod, @BankName, @BankAccountNo, @IFSC, @Remarks,
                  @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID,
                  @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@SupplierCode", Txt(dto.SupplierCode)),
                P("@SupplierType", Txt(dto.SupplierType)),
                P("@SupplierName", Txt(dto.SupplierName)),
                P("@AliasName", Txt(dto.AliasName)),
                P("@Category", Txt(dto.Category)),
                P("@SupplierAddress", Txt(dto.SupplierAddress)),
                P("@StateName", Txt(dto.StateName)),
                P("@StateCode", dto.StateCode),
                P("@PinCode", Txt(dto.PinCode)),
                P("@PinCodeLocation", Txt(dto.PinCodeLocation)),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@MobileNo", Txt(dto.MobileNo)),
                P("@EmailID", Txt(dto.EmailID)),
                P("@Website", Txt(dto.Website)),
                P("@TaxType", Txt(dto.TaxType)),
                P("@GSTIN", Txt(dto.GSTIN)),
                P("@PAN", Txt(dto.PAN)),
                P("@TxnCurrency", Txt(dto.TxnCurrency)),
                P("@CreditPeriod", dto.CreditPeriod),
                P("@BankName", Txt(dto.BankName)),
                P("@BankAccountNo", Txt(dto.BankAccountNo)),
                P("@IFSC", Txt(dto.IFSC)),
                P("@Remarks", Txt(dto.Remarks)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_SupplierContacts
        //======================================================================
        public async Task<(int ID, string Message)> CallSupplierContactsSP(SP_SupplierContacts dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_SupplierContacts @ID OUTPUT, @TransSlNo, @MTransNo, @MCount, @Title, @CPName,
                  @Designation, @PhoneNo, @MobileNo, @Email, @SystemIP, @LockStatus, @CreatedBy,
                  @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@Title", Txt(dto.Title)),
                P("@CPName", Txt(dto.CPName)),
                P("@Designation", Txt(dto.Designation)),
                P("@PhoneNo", Txt(dto.PhoneNo)),
                P("@MobileNo", Txt(dto.MobileNo)),
                P("@Email", Txt(dto.Email)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_SupplierTerm
        //======================================================================
        public async Task<(int ID, string Message)> CallSupplierTermSP(SP_SupplierTerm dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_SupplierTerm @ID OUTPUT, @TransSlNo, @MTransNo, @MCount, @TermSlNo, @TermTitle,
                  @TermDescription, @TermType, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@TermSlNo", dto.TermSlNo),
                P("@TermTitle", Txt(dto.TermTitle)),
                P("@TermDescription", Txt(dto.TermDescription)),
                P("@TermType", Txt(dto.TermType)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_PartMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallPartMasterSP(SP_PartMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_PartMaster @ID OUTPUT, @MTransNo, @MCount, @PartCode, @PartName, @CodeDetails,
                  @PartDetails, @ShortDescription, @FullDescription, @DrawingNo, @DrgRevNo, @ColorCode,
                  @CatNo, @SubCatNo, @GroupNo, @Category, @SubCategory, @InventoryGroup, @Department,
                  @Brand, @PartType, @PartClass, @HSNCode, @TaxP, @PrimaryUnit, @SecondaryUnit,
                  @ConversionRatio, @TxnUnit, @Packing, @PackQty, @PackSize, @SPart, @PPart, @MPart,
                  @MinStock, @MaxStock, @MOQ, @ShelfLife, @CostPrice, @SalesPrice, @MRP, @MfrLicNo,
                  @MfgStd, @StoreLocation, @Rack, @RackRow, @RackCase, @PartLength, @PartWidth,
                  @PartHeight, @PartWeight, @AtchFileName, @SystemIP, @LockStatus, @CreatedBy,
                  @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@PartCode", Txt(dto.PartCode)),
                P("@PartName", Txt(dto.PartName)),
                P("@CodeDetails", Txt(dto.CodeDetails)),
                P("@PartDetails", Txt(dto.PartDetails)),
                P("@ShortDescription", Txt(dto.ShortDescription)),
                P("@FullDescription", Txt(dto.FullDescription)),
                P("@DrawingNo", Txt(dto.DrawingNo)),
                P("@DrgRevNo", dto.DrgRevNo),
                P("@ColorCode", Txt(dto.ColorCode)),
                P("@CatNo", dto.CatNo),
                P("@SubCatNo", dto.SubCatNo),
                P("@GroupNo", dto.GroupNo),
                P("@Category", Txt(dto.Category)),
                P("@SubCategory", Txt(dto.SubCategory)),
                P("@InventoryGroup", Txt(dto.InventoryGroup)),
                P("@Department", Txt(dto.Department)),
                P("@Brand", Txt(dto.Brand)),
                P("@PartType", Txt(dto.PartType)),
                P("@PartClass", Txt(dto.PartClass)),
                P("@HSNCode", Txt(dto.HSNCode)),
                P("@TaxP", dto.TaxP),
                P("@PrimaryUnit", Txt(dto.PrimaryUnit)),
                P("@SecondaryUnit", Txt(dto.SecondaryUnit)),
                P("@ConversionRatio", dto.ConversionRatio),
                P("@TxnUnit", Txt(dto.TxnUnit)),
                P("@Packing", Txt(dto.Packing)),
                P("@PackQty", dto.PackQty),
                P("@PackSize", dto.PackSize),
                P("@SPart", Txt(dto.SPart)),
                P("@PPart", Txt(dto.PPart)),
                P("@MPart", Txt(dto.MPart)),
                P("@MinStock", dto.MinStock),
                P("@MaxStock", dto.MaxStock),
                P("@MOQ", dto.MOQ),
                P("@ShelfLife", dto.ShelfLife),
                P("@CostPrice", dto.CostPrice),
                P("@SalesPrice", dto.SalesPrice),
                P("@MRP", dto.MRP),
                P("@MfrLicNo", Txt(dto.MfrLicNo)),
                P("@MfgStd", Txt(dto.MfgStd)),
                P("@StoreLocation", Txt(dto.StoreLocation)),
                P("@Rack", Txt(dto.Rack)),
                P("@RackRow", Txt(dto.RackRow)),
                P("@RackCase", Txt(dto.RackCase)),
                P("@PartLength", dto.PartLength),
                P("@PartWidth", dto.PartWidth),
                P("@PartHeight", dto.PartHeight),
                P("@PartWeight", dto.PartWeight),
                P("@AtchFileName", Txt(dto.AtchFileName)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_PartCodeMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallPartCodeMasterSP(SP_PartCodeMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_PartCodeMaster @ID OUTPUT, @MTransNo, @MCount, @Parameter, @Code, @Description,
                  @ValveType, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID,
                  @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@Parameter", Txt(dto.Parameter)),
                P("@Code", Txt(dto.Code)),
                P("@Description", Txt(dto.Description)),
                P("@ValveType", Txt(dto.ValveType)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_PartParameterList
        //======================================================================
        public async Task<(int ID, string Message)> CallPartParameterListSP(SP_PartParameterList dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_PartParameterList @ID OUTPUT, @MTransNo, @MCount, @PartType, @Parameter, @SlNo,
                  @MaxLength, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID,
                  @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@PartType", Txt(dto.PartType)),
                P("@Parameter", Txt(dto.Parameter)),
                P("@SlNo", dto.SlNo),
                P("@MaxLength", dto.MaxLength),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_DrawingNos
        //======================================================================
        public async Task<(int ID, string Message)> CallDrawingNosSP(SP_DrawingNos dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_DrawingNos @ID OUTPUT, @MTransNo, @MCount, @DrawingNo, @PartNo, @DrawingType,
                  @DrawingGroup, @PartGroup, @PSlNo, @DrawingDescription, @DrawingRemarks, @RevNo,
                  @RevDate, @RevStatus, @FileAttach, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@DrawingNo", Txt(dto.DrawingNo)),
                P("@PartNo", dto.PartNo),
                P("@DrawingType", Txt(dto.DrawingType)),
                P("@DrawingGroup", Txt(dto.DrawingGroup)),
                P("@PartGroup", Txt(dto.PartGroup)),
                P("@PSlNo", dto.PSlNo),
                P("@DrawingDescription", Txt(dto.DrawingDescription)),
                P("@DrawingRemarks", Txt(dto.DrawingRemarks)),
                P("@RevNo", dto.RevNo),
                P("@RevDate", dto.RevDate),
                P("@RevStatus", Txt(dto.RevStatus)),
                P("@FileAttach", Txt(dto.FileAttach)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_MaterialGrade
        //======================================================================
        public async Task<(int ID, string Message)> CallMaterialGradeSP(SP_MaterialGrade dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_MaterialGrade @ID OUTPUT, @MTransNo, @MCount, @PartNo, @GradeNo, @Grade,
                  @MinStock, @MaxStock, @MOQ, @SystemIP, @LockStatus, @CreatedBy, @CreatedOn,
                  @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@PartNo", dto.PartNo),
                P("@GradeNo", dto.GradeNo),
                P("@Grade", Txt(dto.Grade)),
                P("@MinStock", dto.MinStock),
                P("@MaxStock", dto.MaxStock),
                P("@MOQ", dto.MOQ),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_MaterialStd
        //======================================================================
        public async Task<(int ID, string Message)> CallMaterialStdSP(SP_MaterialStd dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_MaterialStd @ID OUTPUT, @MTransNo, @MCount, @Grade, @MatrStd, @SystemIP,
                  @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@Grade", Txt(dto.Grade)),
                P("@MatrStd", Txt(dto.MatrStd)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }

        //======================================================================
        // SP_HSNMaster
        //======================================================================
        public async Task<(int ID, string Message)> CallHSNMasterSP(SP_HSNMaster dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_HSNMaster @ID OUTPUT, @MTransNo, @MCount, @HSNCode, @HSNDescription, @SystemIP,
                  @LockStatus, @CreatedBy, @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@HSNCode", Txt(dto.HSNCode)),
                P("@HSNDescription", Txt(dto.HSNDescription)),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }


        public async Task<(int ID, string Message)> CallMaterialCompositionSP(
            SP_MaterialComposition dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            };

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter("@MaterialNo", dto.MaterialNo),

        new SqlParameter(
            "@Material",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.Material?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@HeatTreatment",
            SqlDbType.VarChar,
            200)
        {
            Value = (object?)dto.HeatTreatment?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@Description",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.Description?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@ShortName",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.ShortName?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@MinR",
            SqlDbType.Decimal)
        {
            Precision = 12,
            Scale = 4,
            Value = (object?)dto.MinR ?? DBNull.Value
        },

        new SqlParameter(
            "@MaxR",
            SqlDbType.Decimal)
        {
            Precision = 12,
            Scale = 2,
            Value = (object?)dto.MaxR ?? DBNull.Value
        },

        new SqlParameter(
            "@MType",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.MType ?? DBNull.Value
        },

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.CreatedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.CreatedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.ModifiedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.ModifiedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@DeleteReason",
            SqlDbType.VarChar,
            250)
        {
            Value = (object?)dto.DeleteReason ?? DBNull.Value
        },

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.LockStatus ?? "N"
        },

        new SqlParameter(
            "@SubscID",
            SqlDbType.Int)
        {
            Value = (object?)dto.SubscID ?? DBNull.Value
        },

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_MaterialComposition
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @MaterialNo,
            @Material,
            @HeatTreatment,
            @Description,
            @ShortName,
            @MinR,
            @MaxR,
            @MType,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @ModifiedBy,
            @ModifiedOn,
            @DeleteReason,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }


        public async Task<(int ID, string Message)> CallFeedbackMasterSalesSP(
            SP_FeedbackMasterSales dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            };

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter(
            "@FeedbackParameter",
            SqlDbType.VarChar,
            200)
        {
            Value = (object?)dto.FeedbackParameter?.Trim() ?? DBNull.Value
        },

        new SqlParameter(
            "@FeedbackType",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.FeedbackType ?? DBNull.Value
        },

        new SqlParameter(
            "@AmdNo",
            SqlDbType.Int)
        {
            Value = (object?)dto.AmdNo ?? DBNull.Value
        },

        new SqlParameter(
            "@AmdNotes",
            SqlDbType.VarChar,
            100)
        {
            Value = (object?)dto.AmdNotes?.Trim() ?? DBNull.Value
        },

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.CreatedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.CreatedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.ModifiedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.ModifiedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@DeleteReason",
            SqlDbType.VarChar,
            250)
        {
            Value = (object?)dto.DeleteReason ?? DBNull.Value
        },

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.LockStatus ?? "N"
        },

        new SqlParameter(
            "@SubscID",
            SqlDbType.Int)
        {
            Value = (object?)dto.SubscID ?? DBNull.Value
        },

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_FeedbackMasterSales
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @FeedbackParameter,
            @FeedbackType,
            @AmdNo,
            @AmdNotes,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @ModifiedBy,
            @ModifiedOn,
            @DeleteReason,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }

        public async Task<(int ID, string Message)> CallSalesTeamSP(
            SP_SalesTeam dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            };

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter(
            "@SalesManager",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.SalesManager?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@SalesMan",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.SalesMan?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.CreatedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.CreatedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.ModifiedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.ModifiedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@DeleteReason",
            SqlDbType.VarChar,
            250)
        {
            Value = (object?)dto.DeleteReason ?? DBNull.Value
        },

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.LockStatus ?? "N"
        },

        new SqlParameter(
            "@SubscID",
            SqlDbType.Int)
        {
            Value = (object?)dto.SubscID ?? DBNull.Value
        },

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_SalesTeam
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @SalesManager,
            @SalesMan,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @ModifiedBy,
            @ModifiedOn,
            @DeleteReason,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }

        public async Task<(int ID, string Message)> CallChemicalMasterSP(
            SP_ChemicalMaster dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            };

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter(
            "@ChemicalCode",
            SqlDbType.VarChar,
            30)
        {
            Value = (object?)dto.ChemicalCode?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@ChemicalName",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.ChemicalName?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@ShortName",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.ShortName?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@Unit",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.Unit?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.CreatedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.CreatedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.ModifiedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.ModifiedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@DeleteReason",
            SqlDbType.VarChar,
            250)
        {
            Value = (object?)dto.DeleteReason ?? DBNull.Value
        },

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.LockStatus ?? "N"
        },

        new SqlParameter(
            "@SubscID",
            SqlDbType.Int)
        {
            Value = (object?)dto.SubscID ?? DBNull.Value
        },

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_ChemicalMaster
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @ChemicalCode,
            @ChemicalName,
            @ShortName,
            @Unit,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @ModifiedBy,
            @ModifiedOn,
            @DeleteReason,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }

        public async Task<(int ID, string Message)> CallMOCTemplateSP(
            SP_MOCTemplate dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            }; // [P3]

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            }; // [P3]

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter(
            "@ValveType",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.ValveType?.Trim()
                    ?? DBNull.Value
        }, // [P7]

        new SqlParameter(
            "@MOCParameter",
            SqlDbType.VarChar,
            500)
        {
            Value = (object?)dto.MOCParameter?.Trim()
                    ?? DBNull.Value
        }, // [P7]

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter("@CreatedBy", dto.CreatedBy),

        new SqlParameter("@CreatedOn", dto.CreatedOn),

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = dto.LockStatus
        },

        new SqlParameter("@SubscID", dto.SubscID),

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_MOCTemplate
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @ValveType,
            @MOCParameter,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }



        public async Task<(int ID, string Message)> CallMechanicalMasterSP(
            SP_MechanicalMaster dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter(
                "@Message",
                SqlDbType.NVarChar,
                200)
            {
                Direction = ParameterDirection.Output
            };

            var parameters = new[]
            {
        outputId,

        new SqlParameter("@MTransNo", dto.MTransNo),

        new SqlParameter("@MCount", dto.MCount),

        new SqlParameter(
            "@MechanicalCode",
            SqlDbType.VarChar,
            30)
        {
            Value = (object?)dto.MechanicalCode?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@MechanicalName",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.MechanicalName?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@ShortName",
            SqlDbType.VarChar,
            50)
        {
            Value = (object?)dto.ShortName?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@Unit",
            SqlDbType.VarChar,
            30)
        {
            Value = (object?)dto.Unit?.Trim()
                    ?? DBNull.Value
        },

        new SqlParameter(
            "@SystemIP",
            SqlDbType.VarChar,
            20)
        {
            Value = (object?)dto.SystemIP ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.CreatedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@CreatedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.CreatedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedBy",
            SqlDbType.Int)
        {
            Value = (object?)dto.ModifiedBy ?? DBNull.Value
        },

        new SqlParameter(
            "@ModifiedOn",
            SqlDbType.DateTime2)
        {
            Value = (object?)dto.ModifiedOn ?? DBNull.Value
        },

        new SqlParameter(
            "@DeleteReason",
            SqlDbType.VarChar,
            250)
        {
            Value = (object?)dto.DeleteReason ?? DBNull.Value
        },

        new SqlParameter(
            "@LockStatus",
            SqlDbType.Char,
            1)
        {
            Value = (object?)dto.LockStatus ?? "N"
        },

        new SqlParameter(
            "@SubscID",
            SqlDbType.Int)
        {
            Value = (object?)dto.SubscID ?? DBNull.Value
        },

        new SqlParameter("@Status", dto.Status),

        outputMessage
    };

            await Database.ExecuteSqlRawAsync(
                """
        EXEC dbo.SP_MechanicalMaster
            @ID OUTPUT,
            @MTransNo,
            @MCount,
            @MechanicalCode,
            @MechanicalName,
            @ShortName,
            @Unit,
            @SystemIP,
            @CreatedBy,
            @CreatedOn,
            @ModifiedBy,
            @ModifiedOn,
            @DeleteReason,
            @LockStatus,
            @SubscID,
            @Status,
            @Message OUTPUT
        """,
                parameters);

            return
            (
                outputId.Value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(outputId.Value),

                outputMessage.Value?.ToString() ?? string.Empty
            );
        }

        public async Task<(int ID, string Message)> CallMOCMasterSP(SP_MOCMaster dto)
        {
            var outputId = new SqlParameter("@ID", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            var outputMessage = new SqlParameter("@Message", SqlDbType.NVarChar, 200)
            {
                Direction = ParameterDirection.Output
            };

            await Database.ExecuteSqlRawAsync(

                @"EXEC SP_MOCMaster

        @ID OUTPUT,
        @MTransNo,
        @MCount,

        @ValveType,
        @CodeDetails,
        @MOCCode,
        @MOCDetails,
        @MOCDescription,
        @MOCShort,

        @SystemIP,
        @CreatedBy,
        @CreatedOn,
        @ModifiedBy,
        @ModifiedOn,
        @DeleteStatus,
        @DeleteReason,
        @LockStatus,
        @LockCounter,
        @SubscID,

        @Status,
        @Message OUTPUT",

                outputId,

                new SqlParameter("@MTransNo", dto.MTransNo),
                new SqlParameter("@MCount", dto.MCount),

                new SqlParameter("@ValveType",
                    (object?)dto.ValveType ?? DBNull.Value),

                new SqlParameter("@CodeDetails",
                    (object?)dto.CodeDetails ?? DBNull.Value),

                new SqlParameter("@MOCCode", dto.MOCCode),

                new SqlParameter("@MOCDetails",
                    (object?)dto.MOCDetails ?? DBNull.Value),

                new SqlParameter("@MOCDescription", dto.MOCDescription),

                new SqlParameter("@MOCShort", dto.MOCShort),

                new SqlParameter("@SystemIP",
                    (object?)dto.SystemIP ?? DBNull.Value),

                new SqlParameter("@CreatedBy",
                    (object?)dto.CreatedBy ?? DBNull.Value),

                new SqlParameter("@CreatedOn", dto.CreatedOn),

                new SqlParameter("@ModifiedBy",
                    (object?)dto.ModifiedBy ?? DBNull.Value),

                new SqlParameter("@ModifiedOn",
                    (object?)dto.ModifiedOn ?? DBNull.Value),

                new SqlParameter("@DeleteStatus", dto.DeleteStatus),

                new SqlParameter("@DeleteReason",
                    (object?)dto.DeleteReason ?? DBNull.Value),

                new SqlParameter("@LockStatus", dto.LockStatus),

                new SqlParameter("@LockCounter",
                    (object?)dto.LockCounter ?? DBNull.Value),

                new SqlParameter("@SubscID",
                    (object?)dto.SubscID ?? DBNull.Value),

                new SqlParameter("@Status", dto.Status),

                outputMessage
            );

            return
            (
                Convert.ToInt32(outputId.Value),
                outputMessage.Value?.ToString() ?? ""
            );
        }

        //======================================================================
        // SP_PriceList
        //======================================================================
        public async Task<(int ID, string Message)> CallPriceListSP(SP_PriceList dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync(
                @"EXEC SP_PriceList @ID OUTPUT, @MTransNo, @MCount, @VendorNo, @PartNo, @GradeNo, @Grade,
                  @Unit, @Rate, @EffectiveDt, @ItemRemarks, @LeadTime, @SystemIP, @LockStatus, @CreatedBy,
                  @CreatedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT",
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                P("@VendorNo", dto.VendorNo),
                P("@PartNo", dto.PartNo),
                P("@GradeNo", dto.GradeNo),
                P("@Grade", Txt(dto.Grade)),
                P("@Unit", Txt(dto.Unit)),
                P("@Rate", dto.Rate),
                P("@EffectiveDt", dto.EffectiveDt),
                P("@ItemRemarks", Txt(dto.ItemRemarks)),
                P("@LeadTime", dto.LeadTime),
                P("@SystemIP", Txt(dto.SystemIP)),
                P("@LockStatus", Txt(dto.LockStatus)),
                P("@CreatedBy", dto.CreatedBy),
                P("@CreatedOn", dto.CreatedOn),
                P("@DeleteReason", Txt(dto.DeleteReason)),
                P("@SubscID", dto.SubscID),
                P("@Status", Txt(dto.Status)),
                msg);

            return Result(id, msg);
        }



        // ── SP_PurReqMst ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPurReqMstSP(SP_PurReqMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",   dto.MTransNo),
        new SqlParameter("@MCount",     dto.MCount),
        new SqlParameter("@DocSeries",  SqlDbType.Char,    10)  { Value = (object?)dto.DocSeries?.Trim()   ?? DBNull.Value },
        new SqlParameter("@DocYear",    SqlDbType.Int)          { Value = (object?)dto.DocYear              ?? DBNull.Value },
        new SqlParameter("@DocNo",      SqlDbType.Int)          { Value = (object?)dto.DocNo                ?? DBNull.Value },
        new SqlParameter("@DocDate",    SqlDbType.Date)         { Value = (object?)dto.DocDate              ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",   SqlDbType.SmallInt)     { Value = (object?)dto.DocAmdNo             ?? DBNull.Value },
        new SqlParameter("@ReqBy",      SqlDbType.VarChar,  50) { Value = (object?)dto.ReqBy?.Trim()        ?? DBNull.Value },
        new SqlParameter("@ReqDept",    SqlDbType.VarChar,  50) { Value = (object?)dto.ReqDept?.Trim()      ?? DBNull.Value },
        new SqlParameter("@Project",    SqlDbType.VarChar, 250) { Value = (object?)dto.Project?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DueDate",    SqlDbType.Date)         { Value = (object?)dto.DueDate              ?? DBNull.Value },
        new SqlParameter("@DocNotes",   SqlDbType.VarChar, 250) { Value = (object?)dto.DocNotes?.Trim()     ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes",SqlDbType.VarChar, 250) { Value = (object?)dto.DocAmdNotes?.Trim()  ?? DBNull.Value },
        new SqlParameter("@RFQReq",     SqlDbType.Char,     1)  { Value = (object?)dto.RFQReq               ?? DBNull.Value },
        new SqlParameter("@SystemIP",   SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP             ?? DBNull.Value },
        new SqlParameter("@CreatedBy",  SqlDbType.Int)          { Value = (object?)dto.CreatedBy            ?? DBNull.Value },
        new SqlParameter("@CreatedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn            ?? DBNull.Value },
        new SqlParameter("@ModifiedBy", SqlDbType.Int)          { Value = (object?)dto.ModifiedBy           ?? DBNull.Value },
        new SqlParameter("@ModifiedOn", SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn           ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar,250) { Value = (object?)dto.DeleteReason         ?? DBNull.Value },
        new SqlParameter("@LockStatus", SqlDbType.Char,     1)  { Value = (object?)dto.LockStatus           ?? "N"         },
        new SqlParameter("@SubscID",    SqlDbType.Int)          { Value = (object?)dto.SubscID              ?? DBNull.Value },
        new SqlParameter("@Status",     dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_PurReqMst
            @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo,
            @DocDate, @DocAmdNo, @ReqBy, @ReqDept, @Project, @DueDate,
            @DocNotes, @DocAmdNotes, @RFQReq, @SystemIP, @CreatedBy, @CreatedOn,
            @ModifiedBy, @ModifiedOn, @DeleteReason, @LockStatus, @SubscID,
            @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_PurReqDtl ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPurReqDtlSP(SP_PurReqDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)     { Precision=12, Scale=2, Value=(object?)dto.SlNo       ?? DBNull.Value },
        new SqlParameter("@PartNo",      SqlDbType.Int)         { Value=(object?)dto.PartNo                            ?? DBNull.Value },
        new SqlParameter("@ItemRemarks", SqlDbType.VarChar,500) { Value=(object?)dto.ItemRemarks?.Trim()               ?? DBNull.Value },
        new SqlParameter("@Qty",         SqlDbType.Decimal)     { Precision=14, Scale=4, Value=(object?)dto.Qty        ?? DBNull.Value },
        new SqlParameter("@Unit",        SqlDbType.VarChar, 10) { Value=(object?)dto.Unit?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar, 20) { Value=(object?)dto.SystemIP                          ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)         { Value=(object?)dto.CreatedBy                         ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)   { Value=(object?)dto.CreatedOn                         ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)         { Value=(object?)dto.ModifiedBy                        ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn                        ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar,250) { Value=(object?)dto.DeleteReason                      ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)         { Value=(object?)dto.SubscID                           ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg,
        new SqlParameter("@OAYear",      SqlDbType.Int)         { Value=(object?)dto.OAYear                            ?? DBNull.Value },
        new SqlParameter("@OANo",        SqlDbType.Int)         { Value=(object?)dto.OANo                              ?? DBNull.Value },
        new SqlParameter("@OAID",        SqlDbType.Int)         { Value=(object?)dto.OAID                              ?? DBNull.Value },
        new SqlParameter("@GradeNo",     SqlDbType.Int)         { Value=(object?)dto.GradeNo                           ?? DBNull.Value },
        new SqlParameter("@Grade",       SqlDbType.VarChar, 50) { Value=(object?)dto.Grade?.Trim()                     ?? DBNull.Value }
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_PurReqDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @PartNo, @ItemRemarks,
            @Qty, @Unit, @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy,
            @ModifiedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT,
            @OAYear = @OAYear, @OANo = @OANo, @OAID = @OAID,
            @GradeNo = @GradeNo, @Grade = @Grade
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQMst ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQMstSP(SP_RFQMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@MCount",      dto.MCount),
        new SqlParameter("@DocSeries",   SqlDbType.Char,    10)  { Value=(object?)dto.DocSeries?.Trim()  ?? DBNull.Value },
        new SqlParameter("@DocYear",     SqlDbType.Int)          { Value=(object?)dto.DocYear             ?? DBNull.Value },
        new SqlParameter("@DocNo",       SqlDbType.Int)          { Value=(object?)dto.DocNo               ?? DBNull.Value },
        new SqlParameter("@DocDate",     SqlDbType.Date)         { Value=(object?)dto.DocDate             ?? DBNull.Value },
        new SqlParameter("@ValidTill",   SqlDbType.Date)         { Value=(object?)dto.ValidTill           ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",    SqlDbType.SmallInt)     { Value=(object?)dto.DocAmdNo            ?? DBNull.Value },
        new SqlParameter("@PRTransNo",   SqlDbType.Int)          { Value=(object?)dto.PRTransNo           ?? DBNull.Value },
        new SqlParameter("@DocNotes",    SqlDbType.VarChar, 250) { Value=(object?)dto.DocNotes?.Trim()    ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes", SqlDbType.VarChar, 250) { Value=(object?)dto.DocAmdNotes?.Trim() ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value=(object?)dto.SystemIP            ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value=(object?)dto.CreatedBy           ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value=(object?)dto.CreatedOn           ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value=(object?)dto.ModifiedBy          ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value=(object?)dto.ModifiedOn          ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value=(object?)dto.DeleteReason        ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value=(object?)dto.LockStatus          ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value=(object?)dto.SubscID             ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQMst
            @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo,
            @DocDate, @ValidTill, @DocAmdNo, @PRTransNo, @DocNotes, @DocAmdNotes,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQDtl ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQDtlSP(SP_RFQDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SLNo",        SqlDbType.Decimal)     { Precision=12, Scale=2, Value=(object?)dto.SLNo       ?? DBNull.Value },
        new SqlParameter("@PartNo",      SqlDbType.Int)         { Value=(object?)dto.PartNo                            ?? DBNull.Value },
        new SqlParameter("@ItemRemarks", SqlDbType.VarChar,500) { Value=(object?)dto.ItemRemarks?.Trim()               ?? DBNull.Value },
        new SqlParameter("@Qty",         SqlDbType.Decimal)     { Precision=12, Scale=4, Value=(object?)dto.Qty        ?? DBNull.Value },
        new SqlParameter("@Unit",        SqlDbType.VarChar, 10) { Value=(object?)dto.Unit?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar, 20) { Value=(object?)dto.SystemIP                          ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)         { Value=(object?)dto.CreatedBy                         ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)   { Value=(object?)dto.CreatedOn                         ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)         { Value=(object?)dto.ModifiedBy                        ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn                        ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar,250) { Value=(object?)dto.DeleteReason                      ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)         { Value=(object?)dto.SubscID                           ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg,
        new SqlParameter("@GradeNo",     SqlDbType.Int)         { Value=(object?)dto.GradeNo                           ?? DBNull.Value },
        new SqlParameter("@Grade",       SqlDbType.VarChar, 50) { Value=(object?)dto.Grade?.Trim()                     ?? DBNull.Value }
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SLNo, @PartNo, @ItemRemarks,
            @Qty, @Unit, @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy,
            @ModifiedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT,
            @GradeNo = @GradeNo, @Grade = @Grade
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQSupplier ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQSupplierSP(SP_RFQSupplier dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",    dto.TransSlNo),
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@SlNo",         SqlDbType.Decimal)      { Precision=6, Scale=2, Value=(object?)dto.SlNo         ?? DBNull.Value },
        new SqlParameter("@VendorNo",     SqlDbType.Int)          { Value=(object?)dto.VendorNo                            ?? DBNull.Value },
        new SqlParameter("@VendorName",   SqlDbType.VarChar, 100) { Value=(object?)dto.VendorName?.Trim()                  ?? DBNull.Value },
        new SqlParameter("@ContactNo",    SqlDbType.VarChar, 100) { Value=(object?)dto.ContactNo?.Trim()                   ?? DBNull.Value },
        new SqlParameter("@VendorAddress",SqlDbType.VarChar, 250) { Value=(object?)dto.VendorAddress?.Trim()               ?? DBNull.Value },
        new SqlParameter("@SystemIP",     SqlDbType.VarChar,  20) { Value=(object?)dto.SystemIP                            ?? DBNull.Value },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)          { Value=(object?)dto.CreatedBy                           ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2)    { Value=(object?)dto.CreatedOn                           ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)          { Value=(object?)dto.ModifiedBy                          ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2)    { Value=(object?)dto.ModifiedOn                          ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value=(object?)dto.DeleteReason                        ?? DBNull.Value },
        new SqlParameter("@SubscID",      SqlDbType.Int)          { Value=(object?)dto.SubscID                             ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQSupplier
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @VendorNo, @VendorName,
            @ContactNo, @VendorAddress, @SystemIP, @CreatedBy, @CreatedOn,
            @ModifiedBy, @ModifiedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQTerms ─────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQTermsSP(SP_RFQTerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)     { Precision=6, Scale=2, Value=(object?)dto.SlNo       ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30){ Value=(object?)dto.Title?.Trim()                    ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250){ Value=(object?)dto.Description?.Trim()              ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20){ Value=(object?)dto.SystemIP                         ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)         { Value=(object?)dto.CreatedBy                        ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)   { Value=(object?)dto.CreatedOn                        ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)         { Value=(object?)dto.ModifiedBy                       ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn                       ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250){ Value=(object?)dto.DeleteReason                     ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)         { Value=(object?)dto.SubscID                          ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQTerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @Title, @Description,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQPriceMst ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQPriceMstSP(SP_RFQPriceMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@MCount",       dto.MCount),
        new SqlParameter("@DocSeries",    SqlDbType.Char,     10)  { Value=(object?)dto.DocSeries?.Trim()    ?? DBNull.Value },
        new SqlParameter("@DocYear",      SqlDbType.Int)           { Value=(object?)dto.DocYear              ?? DBNull.Value },
        new SqlParameter("@DocNo",        SqlDbType.Int)           { Value=(object?)dto.DocNo                ?? DBNull.Value },
        new SqlParameter("@DocDate",      SqlDbType.Date)          { Value=(object?)dto.DocDate              ?? DBNull.Value },
        new SqlParameter("@ValidTill",    SqlDbType.Date)          { Value=(object?)dto.ValidTill            ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",     SqlDbType.SmallInt)      { Value=(object?)dto.DocAmdNo             ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes",  SqlDbType.VarChar,  250) { Value=(object?)dto.DocAmdNotes?.Trim()  ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",SqlDbType.Int)           { Value=(object?)dto.TargetTransNo        ?? DBNull.Value },
        new SqlParameter("@VendorNo",     SqlDbType.Int)           { Value=(object?)dto.VendorNo             ?? DBNull.Value },
        new SqlParameter("@VendorName",   SqlDbType.VarChar,  100) { Value=(object?)dto.VendorName?.Trim()   ?? DBNull.Value },
        new SqlParameter("@ContactNo",    SqlDbType.VarChar,  100) { Value=(object?)dto.ContactNo?.Trim()    ?? DBNull.Value },
        new SqlParameter("@VendorAddress",SqlDbType.VarChar,  250) { Value=(object?)dto.VendorAddress?.Trim()?? DBNull.Value },
        new SqlParameter("@RefNo",        SqlDbType.VarChar,   50) { Value=(object?)dto.RefNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefDate",      SqlDbType.Date)          { Value=(object?)dto.RefDate              ?? DBNull.Value },
        new SqlParameter("@DocNotes",     SqlDbType.VarChar, 1000) { Value=(object?)dto.DocNotes?.Trim()     ?? DBNull.Value },
        new SqlParameter("@PRYear",       SqlDbType.Int)           { Value=(object?)dto.PRYear               ?? DBNull.Value },
        new SqlParameter("@PRNo",         SqlDbType.Int)           { Value=(object?)dto.PRNo                 ?? DBNull.Value },
        new SqlParameter("@SystemIP",     SqlDbType.VarChar,   20) { Value=(object?)dto.SystemIP             ?? DBNull.Value },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)           { Value=(object?)dto.CreatedBy            ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2)     { Value=(object?)dto.CreatedOn            ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)           { Value=(object?)dto.ModifiedBy           ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2)     { Value=(object?)dto.ModifiedOn           ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar,  250) { Value=(object?)dto.DeleteReason         ?? DBNull.Value },
        new SqlParameter("@LockStatus",   SqlDbType.Char,       1) { Value=(object?)dto.LockStatus           ?? "N"         },
        new SqlParameter("@SubscID",      SqlDbType.Int)           { Value=(object?)dto.SubscID              ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQPriceMst
            @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo,
            @DocDate, @ValidTill, @DocAmdNo, @DocAmdNotes, @TargetTransNo, @VendorNo,
            @VendorName, @ContactNo, @VendorAddress, @RefNo, @RefDate,
            @DocNotes, @PRYear, @PRNo, @SystemIP, @CreatedBy, @CreatedOn,
            @ModifiedBy, @ModifiedOn, @DeleteReason, @LockStatus, @SubscID,
            @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQApproval ──────────────────────────────────────────
        // Picks the winning quotation for one RFQ. The procedure does all three
        // writes (winner, losers, RFQ) inside one transaction, so there is
        // nothing to coordinate on this side.
        public async Task<(int ID, string Message)> CallRFQApprovalSP(SP_RFQApproval dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",        dto.MTransNo),
        new SqlParameter("@ApprovedQuoteNo", dto.ApprovedQuoteNo),
        new SqlParameter("@SystemIP",   SqlDbType.VarChar, 20) { Value=(object?)dto.SystemIP   ?? DBNull.Value },
        new SqlParameter("@ModifiedBy", SqlDbType.Int)         { Value=(object?)dto.ModifiedBy ?? DBNull.Value },
        new SqlParameter("@ModifiedOn", SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn ?? DBNull.Value },
        new SqlParameter("@SubscID",    SqlDbType.Int)         { Value=(object?)dto.SubscID    ?? DBNull.Value },
        new SqlParameter("@Status",     dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQApproval
            @ID OUTPUT, @MTransNo, @ApprovedQuoteNo, @SystemIP,
            @ModifiedBy, @ModifiedOn, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQPriceDtl ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQPriceDtlSP(SP_RFQPriceDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SLNo",        SqlDbType.Decimal)     { Precision=12, Scale=2, Value=(object?)dto.SLNo       ?? DBNull.Value },
        new SqlParameter("@PartNo",      SqlDbType.Int)         { Value=(object?)dto.PartNo                            ?? DBNull.Value },
        new SqlParameter("@ItemRemarks", SqlDbType.VarChar,500) { Value=(object?)dto.ItemRemarks?.Trim()               ?? DBNull.Value },
        new SqlParameter("@Qty",         SqlDbType.Decimal)     { Precision=12, Scale=4, Value=(object?)dto.Qty        ?? DBNull.Value },
        new SqlParameter("@Unit",        SqlDbType.VarChar, 10) { Value=(object?)dto.Unit?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@Rate",        SqlDbType.Decimal)     { Precision=12, Scale=4, Value=(object?)dto.Rate       ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar, 20) { Value=(object?)dto.SystemIP                          ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)         { Value=(object?)dto.CreatedBy                         ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)   { Value=(object?)dto.CreatedOn                         ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)         { Value=(object?)dto.ModifiedBy                        ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn                        ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar,250) { Value=(object?)dto.DeleteReason                      ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)         { Value=(object?)dto.SubscID                           ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg,
        new SqlParameter("@GradeNo",     SqlDbType.Int)         { Value=(object?)dto.GradeNo                           ?? DBNull.Value },
        new SqlParameter("@Grade",       SqlDbType.VarChar, 50) { Value=(object?)dto.Grade?.Trim()                     ?? DBNull.Value }
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQPriceDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SLNo, @PartNo, @ItemRemarks,
            @Qty, @Unit, @Rate, @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy,
            @ModifiedOn, @DeleteReason, @SubscID, @Status, @Message OUTPUT,
            @GradeNo = @GradeNo, @Grade = @Grade
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RFQPriceTerms ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRFQPriceTermsSP(SP_RFQPriceTerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)     { Precision=6, Scale=2, Value=(object?)dto.SlNo       ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30){ Value=(object?)dto.Title?.Trim()                    ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250){ Value=(object?)dto.Description?.Trim()              ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20){ Value=(object?)dto.SystemIP                         ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)         { Value=(object?)dto.CreatedBy                        ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)   { Value=(object?)dto.CreatedOn                        ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)         { Value=(object?)dto.ModifiedBy                       ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)   { Value=(object?)dto.ModifiedOn                       ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250){ Value=(object?)dto.DeleteReason                     ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)         { Value=(object?)dto.SubscID                          ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RFQPriceTerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @Title, @Description,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }


        //======================================================================
        // PURCHASE ORDER -> GRN -> STORE / INSPECTION / HEAT No.
        //
        // See SQL/14_PurchaseOrderProcedures.sql and SQL/15_GRNProcedures.sql.
        // Every one of these returns the same (ID, Message) pair: ID = 0 means
        // the procedure refused and Message says why, which the controllers
        // turn into a 409.
        //
        // Note the names are schema-qualified in every EXEC below. They have
        // to be: SP_POMst and friends also exist in master, left there by an
        // older script, and an unqualified "sp_"-prefixed name is looked up in
        // master first. dbo. pins them to this database.
        //======================================================================

        // ── SP_POMst ────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPOMstSP(SP_POMst dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_POMst
                    @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo, @DocDate,
                    @DocAmdNo, @TargetTransNo, @TargetType, @VendorNo, @VendorNoShip,
                    @RefNo, @RefDate, @DueDate, @DocNotes, @DocAmdNotes, @PurSubject,
                    @BusinessUnit, @Plant, @Source, @OtherCharges, @OtherAmt, @RoundOff,
                    @TxnCurrency, @CurrencyRate, @SystemIP, @CreatedBy, @CreatedOn,
                    @ModifiedBy, @ModifiedOn, @DeleteReason, @LockStatus, @SubscID,
                    @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                new SqlParameter("@DocSeries", SqlDbType.Char, 10) { Value = Txt(dto.DocSeries) },
                P("@DocYear", dto.DocYear),
                P("@DocNo", dto.DocNo ?? 0),
                Dt("@DocDate", dto.DocDate),
                new SqlParameter("@DocAmdNo", SqlDbType.SmallInt) { Value = (object?)dto.DocAmdNo ?? (short)0 },
                P("@TargetTransNo", dto.TargetTransNo ?? 0),
                new SqlParameter("@TargetType", SqlDbType.VarChar, 30) { Value = Txt(dto.TargetType) },
                P("@VendorNo", dto.VendorNo ?? 0),
                P("@VendorNoShip", dto.VendorNoShip ?? 0),
                new SqlParameter("@RefNo", SqlDbType.VarChar, 50) { Value = Txt(dto.RefNo) },
                Dt("@RefDate", dto.RefDate),
                Dt("@DueDate", dto.DueDate),
                new SqlParameter("@DocNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.DocNotes) },
                new SqlParameter("@DocAmdNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.DocAmdNotes) },
                new SqlParameter("@PurSubject", SqlDbType.VarChar, 250) { Value = Txt(dto.PurSubject) },
                new SqlParameter("@BusinessUnit", SqlDbType.VarChar, 50) { Value = Txt(dto.BusinessUnit) },
                new SqlParameter("@Plant", SqlDbType.VarChar, 50) { Value = Txt(dto.Plant) },
                new SqlParameter("@Source", SqlDbType.VarChar, 50) { Value = Txt(dto.Source) },
                new SqlParameter("@OtherCharges", SqlDbType.VarChar, 50) { Value = Txt(dto.OtherCharges) },
                Dec("@OtherAmt", dto.OtherAmt ?? 0, 16, 2),
                Dec("@RoundOff", dto.RoundOff ?? 0, 6, 4),
                new SqlParameter("@TxnCurrency", SqlDbType.VarChar, 10) { Value = Txt(dto.TxnCurrency) },
                Dec("@CurrencyRate", dto.CurrencyRate, 6, 2),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = (object?)dto.LockStatus ?? "N" },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_PODtl ────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallPODtlSP(SP_PODtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_PODtl
                    @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @PartNo, @ItemRemarks,
                    @Qty, @Qty2, @Rate, @MRP, @DiscP, @PnFP, @FreightAmt, @DeleveryDate,
                    @Grade, @MtrlStd, @CoreGrade, @Spare, @SpareGrade, @SpareQty,
                    @OAYear, @OANo, @OAID, @SystemIP, @CreatedBy, @CreatedOn,
                    @ModifiedBy, @ModifiedOn, @DeleteReason, @SubscID, @Status,
                    @Message OUTPUT, @GradeNo = @GradeNo
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                Dec("@SlNo", dto.SlNo ?? 0, 12, 2),
                P("@PartNo", dto.PartNo ?? 0),
                P("@GradeNo", dto.GradeNo ?? 0),
                new SqlParameter("@ItemRemarks", SqlDbType.VarChar, 500) { Value = Txt(dto.ItemRemarks) },
                Dec("@Qty", dto.Qty, 12, 4),
                Dec("@Qty2", dto.Qty2 ?? 0, 12, 2),
                Dec("@Rate", dto.Rate, 14, 2),
                Dec("@MRP", dto.MRP, 14, 2),
                Dec("@DiscP", dto.DiscP ?? 0, 6, 2),
                Dec("@PnFP", dto.PnFP ?? 0, 6, 2),
                Dec("@FreightAmt", dto.FreightAmt ?? 0, 14, 2),
                Dt("@DeleveryDate", dto.DeleveryDate),
                new SqlParameter("@Grade", SqlDbType.VarChar, 50) { Value = Txt(dto.Grade) },
                new SqlParameter("@MtrlStd", SqlDbType.VarChar, 50) { Value = Txt(dto.MtrlStd) },
                new SqlParameter("@CoreGrade", SqlDbType.VarChar, 50) { Value = Txt(dto.CoreGrade) },
                new SqlParameter("@Spare", SqlDbType.VarChar, 50) { Value = Txt(dto.Spare) },
                new SqlParameter("@SpareGrade", SqlDbType.VarChar, 50) { Value = Txt(dto.SpareGrade) },
                Dec("@SpareQty", dto.SpareQty ?? 0, 12, 2),
                P("@OAYear", dto.OAYear ?? 0),
                P("@OANo", dto.OANo ?? 0),
                P("@OAID", dto.OAID ?? 0),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_POTerms / SP_POExtras ────────────────────────────────
        // Identical signatures, so one helper serves both; only the
        // description length differs, and the procedure truncates.
        private async Task<(int ID, string Message)> CallPOLineSP(string procedure, SP_POLine dto, int descLen)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync($"""
                EXEC dbo.{procedure}
                    @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo, @Title, @Description,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                Dec("@SlNo", dto.SlNo ?? 0, 6, 2),
                new SqlParameter("@Title", SqlDbType.VarChar, 30) { Value = Txt(dto.Title) },
                new SqlParameter("@Description", SqlDbType.VarChar, descLen) { Value = Txt(dto.Description) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        public Task<(int ID, string Message)> CallPOTermsSP(SP_POLine dto) =>
            CallPOLineSP("SP_POTerms", dto, 250);

        public Task<(int ID, string Message)> CallPOExtrasSP(SP_POLine dto) =>
            CallPOLineSP("SP_POExtras", dto, 100);

        // ── SP_POGenerateFromQuote ──────────────────────────────────
        public async Task<(int ID, string Message)> CallPOGenerateFromQuoteSP(SP_POGenerateFromQuote dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_POGenerateFromQuote
                    @ID OUTPUT, @QuoteTransNo, @DocSeries, @DocYear, @DocDate, @DueDate,
                    @PurSubject, @BusinessUnit, @Plant, @RefNo, @RefDate, @DocNotes,
                    @TxnCurrency, @CurrencyRate, @SystemIP, @CreatedBy, @SubscID,
                    @Message OUTPUT
                """,
                id,
                P("@QuoteTransNo", dto.QuoteTransNo),
                new SqlParameter("@DocSeries", SqlDbType.Char, 10) { Value = Txt(dto.DocSeries) },
                P("@DocYear", dto.DocYear),
                Dt("@DocDate", dto.DocDate),
                Dt("@DueDate", dto.DueDate),
                new SqlParameter("@PurSubject", SqlDbType.VarChar, 250) { Value = Txt(dto.PurSubject) },
                new SqlParameter("@BusinessUnit", SqlDbType.VarChar, 50) { Value = Txt(dto.BusinessUnit) },
                new SqlParameter("@Plant", SqlDbType.VarChar, 50) { Value = Txt(dto.Plant) },
                new SqlParameter("@RefNo", SqlDbType.VarChar, 50) { Value = Txt(dto.RefNo) },
                Dt("@RefDate", dto.RefDate),
                new SqlParameter("@DocNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.DocNotes) },
                new SqlParameter("@TxnCurrency", SqlDbType.VarChar, 10) { Value = Txt(dto.TxnCurrency) },
                Dec("@CurrencyRate", dto.CurrencyRate, 6, 2),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                P("@SubscID", dto.SubscID),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNMst ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNMstSP(SP_GRNMst dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNMst
                    @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo, @DocDate,
                    @TargetTransNo, @TargetType, @VendorNo, @RefNo, @RefDate,
                    @InwardNo, @InwardDate, @LRNo, @LRDate, @DCNo, @DCDate,
                    @VehicleNo, @BillValue, @DocNotes, @TDSP, @TCSP,
                    @OtherCharges, @OtherAmt, @RoundOff, @TxnCurrency, @CurrencyRate,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                new SqlParameter("@DocSeries", SqlDbType.Char, 10) { Value = Txt(dto.DocSeries) },
                P("@DocYear", dto.DocYear),
                P("@DocNo", dto.DocNo ?? 0),
                Dt("@DocDate", dto.DocDate),
                P("@TargetTransNo", dto.TargetTransNo ?? 0),
                new SqlParameter("@TargetType", SqlDbType.VarChar, 10) { Value = Txt(dto.TargetType) },
                P("@VendorNo", dto.VendorNo ?? 0),
                new SqlParameter("@RefNo", SqlDbType.VarChar, 50) { Value = Txt(dto.RefNo) },
                Dt("@RefDate", dto.RefDate),
                new SqlParameter("@InwardNo", SqlDbType.VarChar, 50) { Value = Txt(dto.InwardNo) },
                Dt("@InwardDate", dto.InwardDate),
                new SqlParameter("@LRNo", SqlDbType.VarChar, 50) { Value = Txt(dto.LRNo) },
                Dt("@LRDate", dto.LRDate),
                new SqlParameter("@DCNo", SqlDbType.VarChar, 50) { Value = Txt(dto.DCNo) },
                Dt("@DCDate", dto.DCDate),
                new SqlParameter("@VehicleNo", SqlDbType.VarChar, 20) { Value = Txt(dto.VehicleNo) },
                Dec("@BillValue", dto.BillValue ?? 0, 14, 2),
                new SqlParameter("@DocNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.DocNotes) },
                Dec("@TDSP", dto.TDSP ?? 0, 6, 2),
                Dec("@TCSP", dto.TCSP ?? 0, 6, 2),
                new SqlParameter("@OtherCharges", SqlDbType.VarChar, 50) { Value = Txt(dto.OtherCharges) },
                Dec("@OtherAmt", dto.OtherAmt ?? 0, 16, 2),
                Dec("@RoundOff", dto.RoundOff ?? 0, 6, 2),
                new SqlParameter("@TxnCurrency", SqlDbType.VarChar, 10) { Value = Txt(dto.TxnCurrency) },
                Dec("@CurrencyRate", dto.CurrencyRate, 6, 2),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = (object?)dto.LockStatus ?? "N" },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNDtl ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNDtlSP(SP_GRNDtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNDtl
                    @ID OUTPUT, @TransSlNo, @MTransNo, @POTransSlNo, @PartNo, @ItemRemarks,
                    @BillQty, @RecQty, @Weight, @Rate, @DiscP, @PnFP, @FreightAmt,
                    @Grade, @Store, @Location, @BarCode, @ChassisNo, @EngineNo, @RegdNo,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@POTransSlNo", dto.POTransSlNo),
                P("@PartNo", dto.PartNo ?? 0),
                new SqlParameter("@ItemRemarks", SqlDbType.VarChar, 500) { Value = Txt(dto.ItemRemarks) },
                Dec("@BillQty", dto.BillQty ?? 0, 12, 4),
                Dec("@RecQty", dto.RecQty ?? 0, 12, 4),
                Dec("@Weight", dto.Weight ?? 0, 12, 4),
                Dec("@Rate", dto.Rate ?? 0, 14, 2),
                Dec("@DiscP", dto.DiscP ?? 0, 6, 2),
                Dec("@PnFP", dto.PnFP ?? 0, 6, 2),
                Dec("@FreightAmt", dto.FreightAmt ?? 0, 14, 2),
                new SqlParameter("@Grade", SqlDbType.VarChar, 50) { Value = Txt(dto.Grade) },
                new SqlParameter("@Store", SqlDbType.VarChar, 50) { Value = Txt(dto.Store) },
                new SqlParameter("@Location", SqlDbType.VarChar, 50) { Value = Txt(dto.Location) },
                new SqlParameter("@BarCode", SqlDbType.VarChar, 30) { Value = Txt(dto.BarCode) },
                new SqlParameter("@ChassisNo", SqlDbType.VarChar, 30) { Value = Txt(dto.ChassisNo) },
                new SqlParameter("@EngineNo", SqlDbType.VarChar, 30) { Value = Txt(dto.EngineNo) },
                new SqlParameter("@RegdNo", SqlDbType.VarChar, 30) { Value = Txt(dto.RegdNo) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNStoreAlloc ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNStoreAllocSP(SP_GRNStoreAlloc dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNStoreAlloc
                    @ID OUTPUT, @TransSlNo, @MTransNo, @Store, @Location,
                    @SystemIP, @ModifiedBy, @ModifiedOn, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                new SqlParameter("@Store", SqlDbType.VarChar, 50) { Value = Txt(dto.Store) },
                new SqlParameter("@Location", SqlDbType.VarChar, 50) { Value = Txt(dto.Location) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_StoreLocation ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallStoreLocationSP(SP_StoreLocation dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_StoreLocation
                    @ID OUTPUT, @MTransNo, @PartNo, @Store, @Location,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                P("@PartNo", dto.PartNo),
                new SqlParameter("@Store", SqlDbType.VarChar, 50) { Value = Txt(dto.Store) },
                new SqlParameter("@Location", SqlDbType.VarChar, 50) { Value = Txt(dto.Location) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNNCDtl ─────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNNCDtlSP(SP_GRNNCDtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNNCDtl
                    @ID OUTPUT, @TransSlNo, @GRNTransSlNo, @NCType, @HeatNo, @NCRemarks,
                    @NCQty, @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@GRNTransSlNo", dto.GRNTransSlNo),
                new SqlParameter("@NCType", SqlDbType.VarChar, 10) { Value = Txt(dto.NCType) },
                new SqlParameter("@HeatNo", SqlDbType.VarChar, 10) { Value = Txt(dto.HeatNo) },
                new SqlParameter("@NCRemarks", SqlDbType.VarChar, 500) { Value = Txt(dto.NCRemarks) },
                Dec("@NCQty", dto.NCQty ?? 0, 12, 4),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNInspDtl ───────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNInspDtlSP(SP_GRNInspDtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNInspDtl
                    @ID OUTPUT, @TransSlNo, @MTransNo, @AccQty, @RejQty, @RewQty, @AODQty,
                    @InspRemarks, @RejReason, @SystemIP, @ModifiedBy, @ModifiedOn,
                    @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                Dec("@AccQty", dto.AccQty ?? 0, 12, 4),
                Dec("@RejQty", dto.RejQty ?? 0, 12, 4),
                Dec("@RewQty", dto.RewQty ?? 0, 12, 4),
                Dec("@AODQty", dto.AODQty ?? 0, 12, 4),
                new SqlParameter("@InspRemarks", SqlDbType.VarChar, 250) { Value = Txt(dto.InspRemarks) },
                new SqlParameter("@RejReason", SqlDbType.VarChar, 250) { Value = Txt(dto.RejReason) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNInspPost ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNInspPostSP(SP_GRNInspPost dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNInspPost
                    @ID OUTPUT, @MTransNo, @QCDate, @QCverifyBy,
                    @SystemIP, @ModifiedBy, @ModifiedOn, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                Dt("@QCDate", dto.QCDate),
                new SqlParameter("@QCverifyBy", SqlDbType.VarChar, 50) { Value = Txt(dto.QCverifyBy) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNNCClose ───────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNNCCloseSP(SP_GRNNCClose dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNNCClose
                    @ID OUTPUT, @MTransNo, @NCCloseDate, @NCRemarks, @NCCloseAttachment,
                    @SystemIP, @ModifiedBy, @ModifiedOn, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                Dt("@NCCloseDate", dto.NCCloseDate),
                new SqlParameter("@NCRemarks", SqlDbType.VarChar, 250) { Value = Txt(dto.NCRemarks) },
                new SqlParameter("@NCCloseAttachment", SqlDbType.VarChar, 100) { Value = Txt(dto.NCCloseAttachment) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNQRCode ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNQRCodeSP(SP_GRNQRCode dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNQRCode
                    @ID OUTPUT, @TransSlNo, @MTransNo, @GRNSlNo, @LQty, @Qty,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@GRNSlNo", dto.GRNSlNo),
                P("@LQty", dto.LQty),
                Dec("@Qty", dto.Qty ?? 0, 12, 2),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_GRNAttachment ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallGRNAttachmentSP(SP_GRNAttachment dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_GRNAttachment
                    @ID OUTPUT, @TransSlNo, @MTransNo, @AttNotes, @AttFile,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                new SqlParameter("@AttNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.AttNotes) },
                new SqlParameter("@AttFile", SqlDbType.VarChar, 250) { Value = Txt(dto.AttFile) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_HeatNoMst ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallHeatNoMstSP(SP_HeatNoMst dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_HeatNoMst
                    @ID OUTPUT, @MTransNo, @MCount, @DocSeries, @DocYear, @DocNo, @DocDate,
                    @TargetTransNo, @PartNo, @Grade, @Qty, @DocNotes,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@MTransNo", dto.MTransNo),
                P("@MCount", dto.MCount),
                new SqlParameter("@DocSeries", SqlDbType.Char, 10) { Value = Txt(dto.DocSeries) },
                P("@DocYear", dto.DocYear),
                P("@DocNo", dto.DocNo ?? 0),
                Dt("@DocDate", dto.DocDate),
                P("@TargetTransNo", dto.TargetTransNo),
                P("@PartNo", dto.PartNo),
                new SqlParameter("@Grade", SqlDbType.VarChar, 50) { Value = Txt(dto.Grade) },
                Dec("@Qty", dto.Qty ?? 0, 12, 6),
                new SqlParameter("@DocNotes", SqlDbType.VarChar, 250) { Value = Txt(dto.DocNotes) },
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = (object?)dto.LockStatus ?? "N" },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }

        // ── SP_HeatNoDtl ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallHeatNoDtlSP(SP_HeatNoDtl dto)
        {
            var id = Out("@ID");
            var msg = OutMsg();

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_HeatNoDtl
                    @ID OUTPUT, @TransSlNo, @MTransNo, @PartNo, @Grade, @HeatNo,
                    @TCNo, @TCDate, @TCFile, @Qty, @ItemRemarks, @RefFrom, @RefTo,
                    @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
                    @DeleteReason, @SubscID, @Status, @Message OUTPUT
                """,
                id,
                P("@TransSlNo", dto.TransSlNo),
                P("@MTransNo", dto.MTransNo),
                P("@PartNo", dto.PartNo),
                new SqlParameter("@Grade", SqlDbType.VarChar, 50) { Value = Txt(dto.Grade) },
                new SqlParameter("@HeatNo", SqlDbType.VarChar, 50) { Value = Txt(dto.HeatNo) },
                new SqlParameter("@TCNo", SqlDbType.VarChar, 30) { Value = Txt(dto.TCNo) },
                Dt("@TCDate", dto.TCDate),
                new SqlParameter("@TCFile", SqlDbType.VarChar, 100) { Value = Txt(dto.TCFile) },
                Dec("@Qty", dto.Qty ?? 0, 12, 4),
                new SqlParameter("@ItemRemarks", SqlDbType.VarChar, 500) { Value = Txt(dto.ItemRemarks) },
                P("@RefFrom", dto.RefFrom),
                P("@RefTo", dto.RefTo),
                new SqlParameter("@SystemIP", SqlDbType.VarChar, 20) { Value = Txt(dto.SystemIP) },
                P("@CreatedBy", dto.CreatedBy),
                Dt2("@CreatedOn", dto.CreatedOn),
                P("@ModifiedBy", dto.ModifiedBy),
                Dt2("@ModifiedOn", dto.ModifiedOn),
                new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = Txt(dto.DeleteReason) },
                P("@SubscID", dto.SubscID),
                P("@Status", dto.Status),
                msg);

            return Result(id, msg);
        }
        public async Task<(int ID, string Message)> CallLeadGenerationSP(SP_LeadGeneration dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@MCount",         dto.MCount),
        new SqlParameter("@DocSeries",      SqlDbType.Char,      10)   { Value = (object?)dto.DocSeries?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)             { Value = (object?)dto.DocYear                ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)             { Value = (object?)dto.DocNo                  ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)            { Value = (object?)dto.DocDate                ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)             { Value = (object?)dto.VendorNo               ?? DBNull.Value },
        new SqlParameter("@VendorCode",     SqlDbType.VarChar,   30)   { Value = (object?)dto.VendorCode?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar,  100)   { Value = (object?)dto.VendorName?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorAddress",  SqlDbType.VarChar,  250)   { Value = (object?)dto.VendorAddress?.Trim()  ?? DBNull.Value },
        new SqlParameter("@City",           SqlDbType.VarChar,   50)   { Value = (object?)dto.City?.Trim()           ?? DBNull.Value },
        new SqlParameter("@ContactPerson",  SqlDbType.VarChar,   50)   { Value = (object?)dto.ContactPerson?.Trim()  ?? DBNull.Value },
        new SqlParameter("@PhoneNo",        SqlDbType.VarChar,   50)   { Value = (object?)dto.PhoneNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@Product",        SqlDbType.VarChar, 1000)   { Value = (object?)dto.Product?.Trim()        ?? DBNull.Value },
        new SqlParameter("@Qty",            SqlDbType.Int)             { Value = (object?)dto.Qty                    ?? DBNull.Value },
        new SqlParameter("@Followupdate",   SqlDbType.Date)            { Value = (object?)dto.Followupdate           ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar,  250)   { Value = (object?)dto.DocNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@SalesExecutive", SqlDbType.VarChar,   50)   { Value = (object?)dto.SalesExecutive?.Trim() ?? DBNull.Value },
        new SqlParameter("@FileAttach",     SqlDbType.VarChar,  100)   { Value = (object?)dto.FileAttach?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Emailid",        SqlDbType.VarChar,  100)   { Value = (object?)dto.Emailid?.Trim()        ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,   20)   { Value = (object?)dto.SystemIP               ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)             { Value = (object?)dto.CreatedBy              ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)       { Value = (object?)dto.CreatedOn              ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)             { Value = (object?)dto.ModifiedBy             ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)       { Value = (object?)dto.ModifiedOn             ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar,  250)   { Value = (object?)dto.DeleteReason           ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,       1)   { Value = (object?)dto.LockStatus             ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)             { Value = (object?)dto.SubscID                ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_LeadGeneration
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate,
            @VendorNo, @VendorCode, @VendorName, @VendorAddress,
            @City, @ContactPerson, @PhoneNo, @Product, @Qty,
            @Followupdate, @DocNotes, @SalesExecutive,
            @FileAttach, @Emailid, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_EnqMst ─────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallEnqMstSP(SP_EnqMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",          dto.MTransNo),
        new SqlParameter("@MCount",            dto.MCount),
        new SqlParameter("@DocSeries",         SqlDbType.Char,    10)  { Value = (object?)dto.DocSeries?.Trim()         ?? DBNull.Value },
        new SqlParameter("@DocYear",           SqlDbType.Int)          { Value = (object?)dto.DocYear                   ?? DBNull.Value },
        new SqlParameter("@DocNo",             SqlDbType.Int)          { Value = (object?)dto.DocNo                     ?? DBNull.Value },
        new SqlParameter("@DocDate",           SqlDbType.Date)         { Value = (object?)dto.DocDate                   ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",          SqlDbType.SmallInt)     { Value = (object?)dto.DocAmdNo                  ?? DBNull.Value },
        new SqlParameter("@VendorNo",          SqlDbType.Int)          { Value = (object?)dto.VendorNo                  ?? DBNull.Value },
        new SqlParameter("@VendorCode",        SqlDbType.VarChar,  30) { Value = (object?)dto.VendorCode?.Trim()        ?? DBNull.Value },
        new SqlParameter("@VendorName",        SqlDbType.VarChar,  80) { Value = (object?)dto.VendorName?.Trim()        ?? DBNull.Value },
        new SqlParameter("@VendorAddress",     SqlDbType.VarChar, 250) { Value = (object?)dto.VendorAddress?.Trim()     ?? DBNull.Value },
        new SqlParameter("@StateName",         SqlDbType.VarChar,  50) { Value = (object?)dto.StateName?.Trim()         ?? DBNull.Value },
        new SqlParameter("@Zone",              SqlDbType.VarChar,  50) { Value = (object?)dto.Zone?.Trim()              ?? DBNull.Value },
        new SqlParameter("@CPName",            SqlDbType.VarChar,  80) { Value = (object?)dto.CPName?.Trim()            ?? DBNull.Value },
        new SqlParameter("@CPDesignation",     SqlDbType.VarChar,  50) { Value = (object?)dto.CPDesignation?.Trim()     ?? DBNull.Value },
        new SqlParameter("@CPPhone",           SqlDbType.VarChar,  30) { Value = (object?)dto.CPPhone?.Trim()           ?? DBNull.Value },
        new SqlParameter("@RefNo",             SqlDbType.VarChar,  50) { Value = (object?)dto.RefNo?.Trim()             ?? DBNull.Value },
        new SqlParameter("@RefDate",           SqlDbType.Date)         { Value = (object?)dto.RefDate                   ?? DBNull.Value },
        new SqlParameter("@DueDate",           SqlDbType.Date)         { Value = (object?)dto.DueDate                   ?? DBNull.Value },
        new SqlParameter("@SalesExec",         SqlDbType.VarChar,  50) { Value = (object?)dto.SalesExec?.Trim()         ?? DBNull.Value },
        new SqlParameter("@EnqSource",         SqlDbType.VarChar,  50) { Value = (object?)dto.EnqSource?.Trim()         ?? DBNull.Value },
        new SqlParameter("@EnqPriority",       SqlDbType.VarChar,  50) { Value = (object?)dto.EnqPriority?.Trim()       ?? DBNull.Value },
        new SqlParameter("@EnqCategory",       SqlDbType.VarChar,  50) { Value = (object?)dto.EnqCategory?.Trim()       ?? DBNull.Value },
        new SqlParameter("@RefProject",        SqlDbType.VarChar,  50) { Value = (object?)dto.RefProject?.Trim()        ?? DBNull.Value },
        new SqlParameter("@DocNotes",          SqlDbType.VarChar, 250) { Value = (object?)dto.DocNotes?.Trim()          ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes",       SqlDbType.VarChar, 250) { Value = (object?)dto.DocAmdNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@FileAttach",        SqlDbType.VarChar, 100) { Value = (object?)dto.FileAttach?.Trim()        ?? DBNull.Value },
        new SqlParameter("@CPEmail",           SqlDbType.VarChar, 100) { Value = (object?)dto.CPEmail?.Trim()           ?? DBNull.Value },
        new SqlParameter("@LeadStatus",        SqlDbType.VarChar,  20) { Value = (object?)dto.LeadStatus?.Trim()        ?? DBNull.Value },
        new SqlParameter("@EnqRemarks",        SqlDbType.VarChar, 250) { Value = (object?)dto.EnqRemarks?.Trim()        ?? DBNull.Value },
        new SqlParameter("@CPAphone",          SqlDbType.VarChar,  30) { Value = (object?)dto.CPAphone?.Trim()          ?? DBNull.Value },
        new SqlParameter("@CPCC",              SqlDbType.VarChar,  20) { Value = (object?)dto.CPCC?.Trim()              ?? DBNull.Value },
        new SqlParameter("@CPACC",             SqlDbType.VarChar,  20) { Value = (object?)dto.CPACC?.Trim()             ?? DBNull.Value },
        new SqlParameter("@Nextfollowupdate",  SqlDbType.Date)         { Value = (object?)dto.Nextfollowupdate          ?? DBNull.Value },
        new SqlParameter("@DrvNo",             SqlDbType.Int)          { Value = (object?)dto.DrvNo                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",          SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                  ?? DBNull.Value },
        new SqlParameter("@CreatedBy",         SqlDbType.Int)          { Value = (object?)dto.CreatedBy                 ?? DBNull.Value },
        new SqlParameter("@CreatedOn",         SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                 ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",        SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",        SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                ?? DBNull.Value },
        new SqlParameter("@DeleteReason",      SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason              ?? DBNull.Value },
        new SqlParameter("@LockStatus",        SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                ?? "N"         },
        new SqlParameter("@SubscID",           SqlDbType.Int)          { Value = (object?)dto.SubscID                   ?? DBNull.Value },
        new SqlParameter("@Status",            dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_EnqMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate, @DocAmdNo,
            @VendorNo, @VendorCode, @VendorName, @VendorAddress,
            @StateName, @Zone, @CPName, @CPDesignation, @CPPhone,
            @RefNo, @RefDate, @DueDate, @SalesExec,
            @EnqSource, @EnqPriority, @EnqCategory, @RefProject,
            @DocNotes, @DocAmdNotes, @FileAttach, @CPEmail,
            @LeadStatus, @EnqRemarks, @CPAphone, @CPCC, @CPACC,
            @Nextfollowupdate, @DrvNo, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_EnqDtl ─────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallEnqDtlSP(SP_EnqDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",    dto.TransSlNo),
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@SLNo",         SqlDbType.Decimal)       { Precision = 12, Scale = 2,  Value = (object?)dto.SLNo            ?? DBNull.Value },
        new SqlParameter("@PartNo",       SqlDbType.Int)           { Value = (object?)dto.PartNo                                       ?? DBNull.Value },
        new SqlParameter("@Description",  SqlDbType.VarChar,  250) { Value = (object?)dto.Description?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",  SqlDbType.VarChar, 2000) { Value = (object?)dto.ItemRemarks?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@Qty",          SqlDbType.Decimal)       { Precision = 12, Scale = 2,  Value = (object?)dto.Qty              ?? DBNull.Value },
        new SqlParameter("@Unit",         SqlDbType.VarChar,   20) { Value = (object?)dto.Unit?.Trim()                                 ?? DBNull.Value },
        new SqlParameter("@CustPartNo",   SqlDbType.VarChar,   50) { Value = (object?)dto.CustPartNo?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@PartCode",     SqlDbType.VarChar,   30) { Value = (object?)dto.PartCode?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@PartSysCode",  SqlDbType.VarChar,  100) { Value = (object?)dto.PartSysCode?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@PartSysDesc",  SqlDbType.VarChar,  500) { Value = (object?)dto.PartSysDesc?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@AmdNo",        SqlDbType.Int)           { Value = (object?)dto.AmdNo                                        ?? DBNull.Value },
        new SqlParameter("@AmdMode",      SqlDbType.VarChar,   10) { Value = (object?)dto.AmdMode?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@SystemIP",     SqlDbType.VarChar,   20) { Value = (object?)dto.SystemIP                                     ?? DBNull.Value },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)           { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)           { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar,  250) { Value = (object?)dto.DeleteReason                                 ?? DBNull.Value },
        new SqlParameter("@LockStatus",   SqlDbType.Char,       1) { Value = (object?)dto.LockStatus                                   ?? "N"         },
        new SqlParameter("@SubscID",      SqlDbType.Int)           { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_EnqDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SLNo,
            @PartNo, @Description, @ItemRemarks, @Qty, @Unit,
            @CustPartNo, @PartCode, @PartSysCode, @PartSysDesc,
            @AmdNo, @AmdMode, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_EnqFollowUp ────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallEnqFollowUpSP(SP_EnqFollowUp dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",     dto.TransSlNo),
        new SqlParameter("@MTransNo",      dto.MTransNo),
        new SqlParameter("@SLNo",          SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.SLNo             ?? DBNull.Value },
        new SqlParameter("@FollowUpType",  SqlDbType.VarChar,   30) { Value = (object?)dto.FollowUpType?.Trim()                         ?? DBNull.Value },
        new SqlParameter("@FollowupDate",  SqlDbType.Date)          { Value = (object?)dto.FollowupDate                                 ?? DBNull.Value },
        new SqlParameter("@FollowupNotes", SqlDbType.VarChar, 2000) { Value = (object?)dto.FollowupNotes?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@NextVisitDate", SqlDbType.Date)          { Value = (object?)dto.NextVisitDate                                ?? DBNull.Value },
        new SqlParameter("@EnqNo",         SqlDbType.Int)           { Value = (object?)dto.EnqNo                                        ?? DBNull.Value },
        new SqlParameter("@QtnNo",         SqlDbType.Int)           { Value = (object?)dto.QtnNo                                        ?? DBNull.Value },
        new SqlParameter("@OANo",          SqlDbType.Int)           { Value = (object?)dto.OANo                                         ?? DBNull.Value },
        new SqlParameter("@PINo",          SqlDbType.Int)           { Value = (object?)dto.PINo                                         ?? DBNull.Value },
        new SqlParameter("@InvNo",         SqlDbType.Int)           { Value = (object?)dto.InvNo                                        ?? DBNull.Value },
        new SqlParameter("@SystemIP",      SqlDbType.VarChar,   20) { Value = (object?)dto.SystemIP                                     ?? DBNull.Value },
        new SqlParameter("@CreatedBy",     SqlDbType.Int)           { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",    SqlDbType.Int)           { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",    SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason",  SqlDbType.VarChar,  250) { Value = (object?)dto.DeleteReason                                 ?? DBNull.Value },
        new SqlParameter("@LockStatus",    SqlDbType.Char,       1) { Value = (object?)dto.LockStatus                                   ?? "N"         },
        new SqlParameter("@SubscID",       SqlDbType.Int)           { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",        dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_EnqFollowUp
            @ID OUTPUT, @TransSlNo, @MTransNo, @SLNo,
            @FollowUpType, @FollowupDate, @FollowupNotes, @NextVisitDate,
            @EnqNo, @QtnNo, @OANo, @PINo, @InvNo, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_ENQFeasibilityMst ──────────────────────────────────────
        public async Task<(int ID, string Message)> CallENQFeasibilityMstSP(SP_ENQFeasibilityMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",        dto.MTransNo),
        new SqlParameter("@MCount",          dto.MCount),
        new SqlParameter("@SlNo",            SqlDbType.Decimal)       { Precision = 6, Scale = 2, Value = (object?)dto.SlNo              ?? DBNull.Value },
        new SqlParameter("@FeasibilityPoint",SqlDbType.VarChar,  250) { Value = (object?)dto.FeasibilityPoint?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@Remarks",         SqlDbType.VarChar,   50) { Value = (object?)dto.Remarks?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@AmdNo",           SqlDbType.Int)           { Value = (object?)dto.AmdNo                                        ?? DBNull.Value },
        new SqlParameter("@AmdNotes",        SqlDbType.VarChar,  100) { Value = (object?)dto.AmdNotes?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@DocStatus",       SqlDbType.Char,       1) { Value = (object?)dto.DocStatus                                    ?? "N"         },
        new SqlParameter("@DocStatusNote",   SqlDbType.VarChar,  100) { Value = (object?)dto.DocStatusNote?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@SystemIP",        SqlDbType.VarChar,   20) { Value = (object?)dto.SystemIP                                     ?? DBNull.Value },
        new SqlParameter("@CreatedBy",       SqlDbType.Int)           { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",       SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",      SqlDbType.Int)           { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",      SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason",    SqlDbType.VarChar,  250) { Value = (object?)dto.DeleteReason                                 ?? DBNull.Value },
        new SqlParameter("@LockStatus",      SqlDbType.Char,       1) { Value = (object?)dto.LockStatus                                   ?? "N"         },
        new SqlParameter("@SubscID",         SqlDbType.Int)           { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",          dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_ENQFeasibilityMst
            @ID OUTPUT, @MTransNo, @MCount,
            @SlNo, @FeasibilityPoint, @Remarks, @AmdNo, @AmdNotes,
            @DocStatus, @DocStatusNote, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
        public async Task<(int ID, string Message)> CallOAMstSP(SP_OAMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@MCount",         dto.MCount),
        new SqlParameter("@DocSeries",      SqlDbType.Char,     10)   { Value = (object?)dto.DocSeries?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)            { Value = (object?)dto.DocYear                ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)            { Value = (object?)dto.DocNo                  ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)           { Value = (object?)dto.DocDate                ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",       SqlDbType.SmallInt)       { Value = (object?)dto.DocAmdNo               ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",  SqlDbType.Int)            { Value = (object?)dto.TargetTransNo          ?? DBNull.Value },
        new SqlParameter("@TargetDocYear",  SqlDbType.Int)            { Value = (object?)dto.TargetDocYear          ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",    SqlDbType.Int)            { Value = (object?)dto.TargetDocNo            ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",    SqlDbType.Int)            { Value = (object?)dto.TargetAmdNo            ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)            { Value = (object?)dto.VendorNo               ?? DBNull.Value },
        new SqlParameter("@VendorCode",     SqlDbType.VarChar,  20)   { Value = (object?)dto.VendorCode?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar, 100)   { Value = (object?)dto.VendorName?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Address",        SqlDbType.VarChar, 500)   { Value = (object?)dto.Address?.Trim()        ?? DBNull.Value },
        new SqlParameter("@GSTIN",          SqlDbType.VarChar,  20)   { Value = (object?)dto.GSTIN?.Trim()          ?? DBNull.Value },
        new SqlParameter("@StateName",      SqlDbType.VarChar,  50)   { Value = (object?)dto.StateName?.Trim()      ?? DBNull.Value },
        new SqlParameter("@PhoneNo",        SqlDbType.VarChar,  30)   { Value = (object?)dto.PhoneNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@EmailID",        SqlDbType.VarChar,  80)   { Value = (object?)dto.EmailID?.Trim()        ?? DBNull.Value },
        new SqlParameter("@CPName",         SqlDbType.VarChar,  80)   { Value = (object?)dto.CPName?.Trim()         ?? DBNull.Value },
        new SqlParameter("@CPDesignation",  SqlDbType.VarChar,  50)   { Value = (object?)dto.CPDesignation?.Trim()  ?? DBNull.Value },
        new SqlParameter("@CPPhone",        SqlDbType.VarChar,  30)   { Value = (object?)dto.CPPhone?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefNo",          SqlDbType.VarChar,  50)   { Value = (object?)dto.RefNo?.Trim()          ?? DBNull.Value },
        new SqlParameter("@RefDate",        SqlDbType.Date)           { Value = (object?)dto.RefDate                ?? DBNull.Value },
        new SqlParameter("@OtherRefNo",     SqlDbType.VarChar,  30)   { Value = (object?)dto.OtherRefNo?.Trim()     ?? DBNull.Value },
        new SqlParameter("@SalesExec",      SqlDbType.VarChar,  50)   { Value = (object?)dto.SalesExec?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DueDate",        SqlDbType.Date)           { Value = (object?)dto.DueDate                ?? DBNull.Value },
        new SqlParameter("@TargetDate",     SqlDbType.Date)           { Value = (object?)dto.TargetDate             ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar, 250)   { Value = (object?)dto.DocNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes",    SqlDbType.VarChar, 250)   { Value = (object?)dto.DocAmdNotes?.Trim()    ?? DBNull.Value },
        new SqlParameter("@DocRegion",      SqlDbType.VarChar,  50)   { Value = (object?)dto.DocRegion?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocCategory",    SqlDbType.VarChar,  50)   { Value = (object?)dto.DocCategory?.Trim()    ?? DBNull.Value },
        new SqlParameter("@TaxType",        SqlDbType.VarChar,  20)   { Value = (object?)dto.TaxType?.Trim()        ?? DBNull.Value },
        new SqlParameter("@BasicValue",     SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue      ?? DBNull.Value },
        new SqlParameter("@ExemptValue",    SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.ExemptValue     ?? DBNull.Value },
        new SqlParameter("@TaxableValue",   SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.TaxableValue    ?? DBNull.Value },
        new SqlParameter("@PnFAmt",         SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.PnFAmt          ?? DBNull.Value },
        new SqlParameter("@FreightAmt",     SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.FreightAmt      ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",        SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.IGSTAmt         ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",        SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.CGSTAmt         ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",        SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.SGSTAmt         ?? DBNull.Value },
        new SqlParameter("@OtherCharges",   SqlDbType.VarChar,  50)   { Value = (object?)dto.OtherCharges?.Trim()   ?? DBNull.Value },
        new SqlParameter("@OtherAmt",       SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.OtherAmt        ?? DBNull.Value },
        new SqlParameter("@RoundOff",       SqlDbType.Decimal)        { Precision =  6, Scale = 2, Value = (object?)dto.RoundOff        ?? DBNull.Value },
        new SqlParameter("@NetValue",       SqlDbType.Decimal)        { Precision = 18, Scale = 2, Value = (object?)dto.NetValue         ?? DBNull.Value },
        new SqlParameter("@TxnCurrency",    SqlDbType.VarChar,  10)   { Value = (object?)dto.TxnCurrency?.Trim()    ?? DBNull.Value },
        new SqlParameter("@CurrencyRate",   SqlDbType.Decimal)        { Precision =  6, Scale = 2, Value = (object?)dto.CurrencyRate    ?? DBNull.Value },
        new SqlParameter("@CurrencyAmount", SqlDbType.Decimal)        { Precision = 16, Scale = 2, Value = (object?)dto.CurrencyAmount  ?? DBNull.Value },
        new SqlParameter("@FileAttach",     SqlDbType.VarChar,  30)   { Value = (object?)dto.FileAttach?.Trim()     ?? DBNull.Value },
        new SqlParameter("@AmdNo",          SqlDbType.Int)            { Value = (object?)dto.AmdNo                  ?? DBNull.Value },
        new SqlParameter("@AmdNotes",       SqlDbType.VarChar, 100)   { Value = (object?)dto.AmdNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@VendorNoShip",   SqlDbType.Int)            { Value = (object?)dto.VendorNoShip           ?? DBNull.Value },
        new SqlParameter("@ChassisNo",      SqlDbType.VarChar,  50)   { Value = (object?)dto.ChassisNo?.Trim()      ?? DBNull.Value },
        new SqlParameter("@EngineNo",       SqlDbType.VarChar,  50)   { Value = (object?)dto.EngineNo?.Trim()       ?? DBNull.Value },
        new SqlParameter("@VehicleModel",   SqlDbType.VarChar,  50)   { Value = (object?)dto.VehicleModel?.Trim()   ?? DBNull.Value },
        new SqlParameter("@IntNotes",       SqlDbType.VarChar,2000)   { Value = (object?)dto.IntNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@OAType",         SqlDbType.Char,      1)   { Value = (object?)dto.OAType                 ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20)   { Value = (object?)dto.SystemIP               ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)            { Value = (object?)dto.CreatedBy              ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)      { Value = (object?)dto.CreatedOn              ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)            { Value = (object?)dto.ModifiedBy             ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)      { Value = (object?)dto.ModifiedOn             ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250)   { Value = (object?)dto.DeleteReason           ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1)   { Value = (object?)dto.LockStatus             ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)            { Value = (object?)dto.SubscID                ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OAMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate, @DocAmdNo,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
            @StateName, @PhoneNo, @EmailID, @CPName, @CPDesignation, @CPPhone,
            @RefNo, @RefDate, @OtherRefNo, @SalesExec, @DueDate, @TargetDate,
            @DocNotes, @DocAmdNotes, @DocRegion, @DocCategory, @TaxType,
            @BasicValue, @ExemptValue, @TaxableValue,
            @PnFAmt, @FreightAmt, @IGSTAmt, @CGSTAmt, @SGSTAmt,
            @OtherCharges, @OtherAmt, @RoundOff, @NetValue,
            @TxnCurrency, @CurrencyRate, @CurrencyAmount,
            @FileAttach, @AmdNo, @AmdNotes, @VendorNoShip,
            @ChassisNo, @EngineNo, @VehicleModel, @IntNotes, @OAType,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_OADtl ──────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallOADtlSP(SP_OADtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",        dto.TransSlNo),
        new SqlParameter("@MTransNo",         dto.MTransNo),
        new SqlParameter("@SlNo",             SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.SlNo             ?? DBNull.Value },
        new SqlParameter("@LinkRefYear",      SqlDbType.Int)          { Value = (object?)dto.LinkRefYear                                   ?? DBNull.Value },
        new SqlParameter("@LinkRefNo",        SqlDbType.Int)          { Value = (object?)dto.LinkRefNo                                     ?? DBNull.Value },
        new SqlParameter("@LinkRefId",        SqlDbType.Int)          { Value = (object?)dto.LinkRefId                                     ?? DBNull.Value },
        new SqlParameter("@PartNo",           SqlDbType.Int)          { Value = (object?)dto.PartNo                                        ?? DBNull.Value },
        new SqlParameter("@PartDescription",  SqlDbType.VarChar, 200) { Value = (object?)dto.PartDescription?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",      SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@CustPartNo",       SqlDbType.VarChar,  20) { Value = (object?)dto.CustPartNo?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@DeliveryDate",     SqlDbType.Date)         { Value = (object?)dto.DeliveryDate                                  ?? DBNull.Value },
        new SqlParameter("@Qty",              SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.Qty                ?? DBNull.Value },
        new SqlParameter("@ConvQty",          SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.ConvQty            ?? DBNull.Value },
        new SqlParameter("@TxnUnit",          SqlDbType.Int)          { Value = (object?)dto.TxnUnit                                       ?? DBNull.Value },
        new SqlParameter("@Unit",             SqlDbType.VarChar,  10) { Value = (object?)dto.Unit?.Trim()                                  ?? DBNull.Value },
        new SqlParameter("@Rate",             SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.Rate               ?? DBNull.Value },
        new SqlParameter("@BasicValue",       SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue         ?? DBNull.Value },
        new SqlParameter("@DiscP",            SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.DiscP              ?? DBNull.Value },
        new SqlParameter("@DiscAmt",          SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.DiscAmt            ?? DBNull.Value },
        new SqlParameter("@PnFP",             SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.PnFP               ?? DBNull.Value },
        new SqlParameter("@PnFAmt",           SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.PnFAmt             ?? DBNull.Value },
        new SqlParameter("@FreightRate",      SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.FreightRate        ?? DBNull.Value },
        new SqlParameter("@FreightAmt",       SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.FreightAmt         ?? DBNull.Value },
        new SqlParameter("@TaxableValue",     SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.TaxableValue       ?? DBNull.Value },
        new SqlParameter("@GSTP",             SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.GSTP               ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",          SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.IGSTAmt            ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",          SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.CGSTAmt            ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",          SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.SGSTAmt            ?? DBNull.Value },
        new SqlParameter("@TotalValue",       SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.TotalValue         ?? DBNull.Value },
        new SqlParameter("@LinkNo",           SqlDbType.Int)          { Value = (object?)dto.LinkNo                                        ?? DBNull.Value },
        new SqlParameter("@PartCode",         SqlDbType.VarChar,  50) { Value = (object?)dto.PartCode?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@MocSysCode",       SqlDbType.VarChar,  50) { Value = (object?)dto.MocSysCode?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@MocSysDesc",       SqlDbType.VarChar, 250) { Value = (object?)dto.MocSysDesc?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@MocCode",          SqlDbType.VarChar,  50) { Value = (object?)dto.MocCode?.Trim()                               ?? DBNull.Value },
        new SqlParameter("@MocDescription",   SqlDbType.VarChar, 250) { Value = (object?)dto.MocDescription?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@ValveCode",        SqlDbType.VarChar, 100) { Value = (object?)dto.ValveCode?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@ValveDescription", SqlDbType.VarChar, 500) { Value = (object?)dto.ValveDescription?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@FSlNoValve",       SqlDbType.Int)          { Value = (object?)dto.FSlNoValve                                    ?? DBNull.Value },
        new SqlParameter("@TSlNoValve",       SqlDbType.Int)          { Value = (object?)dto.TSlNoValve                                    ?? DBNull.Value },
        new SqlParameter("@ListRate",         SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.ListRate           ?? DBNull.Value },
        new SqlParameter("@MocShort",         SqlDbType.VarChar, 250) { Value = (object?)dto.MocShort?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@Deviation",        SqlDbType.VarChar, 250) { Value = (object?)dto.Deviation?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@SpecialReq",       SqlDbType.VarChar, 250) { Value = (object?)dto.SpecialReq?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@AmdNo",            SqlDbType.Int)          { Value = (object?)dto.AmdNo                                         ?? DBNull.Value },
        new SqlParameter("@AmdMode",          SqlDbType.VarChar,  10) { Value = (object?)dto.AmdMode?.Trim()                               ?? DBNull.Value },
        new SqlParameter("@POID",             SqlDbType.Int)          { Value = (object?)dto.POID                                          ?? DBNull.Value },
        new SqlParameter("@SystemIP",         SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                      ?? DBNull.Value },
        new SqlParameter("@CreatedBy",        SqlDbType.Int)          { Value = (object?)dto.CreatedBy                                     ?? DBNull.Value },
        new SqlParameter("@CreatedOn",        SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                                     ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",       SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",       SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                                    ?? DBNull.Value },
        new SqlParameter("@DeleteReason",     SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                                  ?? DBNull.Value },
        new SqlParameter("@LockStatus",       SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                                    ?? "N"         },
        new SqlParameter("@SubscID",          SqlDbType.Int)          { Value = (object?)dto.SubscID                                       ?? DBNull.Value },
        new SqlParameter("@Status",           dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OADtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @LinkRefYear, @LinkRefNo, @LinkRefId,
            @PartNo, @PartDescription, @ItemRemarks, @CustPartNo, @DeliveryDate,
            @Qty, @ConvQty, @TxnUnit, @Unit, @Rate, @BasicValue,
            @DiscP, @DiscAmt, @PnFP, @PnFAmt, @FreightRate, @FreightAmt,
            @TaxableValue, @GSTP, @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
            @LinkNo, @PartCode, @MocSysCode, @MocSysDesc, @MocCode, @MocDescription,
            @ValveCode, @ValveDescription, @FSlNoValve, @TSlNoValve,
            @ListRate, @MocShort, @Deviation, @SpecialReq,
            @AmdNo, @AmdMode, @POID,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_OATerms ────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallOATermsSP(SP_OATerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OATerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_OAExtras ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallOAExtrasSP(SP_OAExtras dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 100) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OAExtras
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_OANotes ────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallOANotesSP(SP_OANotes dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo  ?? DBNull.Value },
        new SqlParameter("@Notes",       SqlDbType.VarChar, 250) { Value = (object?)dto.Notes?.Trim()                    ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                         ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                        ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                        ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                       ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                       ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                     ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                       ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                          ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OANotes
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Notes, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_OAAttachment ───────────────────────────────────────────
        public async Task<(int ID, string Message)> CallOAAttachmentSP(SP_OAAttachment dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@AttNotes",    SqlDbType.VarChar, 250) { Value = (object?)dto.AttNotes?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@AttFile",     SqlDbType.VarChar, 250) { Value = (object?)dto.AttFile?.Trim()                         ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_OAAttachment
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @AttNotes, @AttFile, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
        public async Task<(int ID, string Message)> CallQtnMstSP(SP_QtnMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@MCount",         dto.MCount),
        new SqlParameter("@DocSeries",      SqlDbType.Char,     10)  { Value = (object?)dto.DocSeries?.Trim()      ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)           { Value = (object?)dto.DocYear                ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)           { Value = (object?)dto.DocNo                  ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)          { Value = (object?)dto.DocDate                ?? DBNull.Value },
        new SqlParameter("@DocAmdNo",       SqlDbType.SmallInt)      { Value = (object?)dto.DocAmdNo               ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",  SqlDbType.Int)           { Value = (object?)dto.TargetTransNo          ?? DBNull.Value },
        new SqlParameter("@TargetDocYear",  SqlDbType.Int)           { Value = (object?)dto.TargetDocYear          ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",    SqlDbType.Int)           { Value = (object?)dto.TargetDocNo            ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",    SqlDbType.Int)           { Value = (object?)dto.TargetAmdNo            ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)           { Value = (object?)dto.VendorNo               ?? DBNull.Value },
        new SqlParameter("@VendorCode",     SqlDbType.VarChar,  30)  { Value = (object?)dto.VendorCode?.Trim()     ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Address",        SqlDbType.VarChar, 500)  { Value = (object?)dto.Address?.Trim()        ?? DBNull.Value },
        new SqlParameter("@GSTIN",          SqlDbType.VarChar,  20)  { Value = (object?)dto.GSTIN?.Trim()          ?? DBNull.Value },
        new SqlParameter("@PhoneNo",        SqlDbType.VarChar,  30)  { Value = (object?)dto.PhoneNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@EmailID",        SqlDbType.VarChar,  80)  { Value = (object?)dto.EmailID?.Trim()        ?? DBNull.Value },
        new SqlParameter("@CPName",         SqlDbType.VarChar,  80)  { Value = (object?)dto.CPName?.Trim()         ?? DBNull.Value },
        new SqlParameter("@CPDesignation",  SqlDbType.VarChar,  50)  { Value = (object?)dto.CPDesignation?.Trim()  ?? DBNull.Value },
        new SqlParameter("@CPPhone",        SqlDbType.VarChar,  30)  { Value = (object?)dto.CPPhone?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefNo",          SqlDbType.VarChar,  50)  { Value = (object?)dto.RefNo?.Trim()          ?? DBNull.Value },
        new SqlParameter("@RefDate",        SqlDbType.Date)          { Value = (object?)dto.RefDate                ?? DBNull.Value },
        new SqlParameter("@SalesExec",      SqlDbType.VarChar,  50)  { Value = (object?)dto.SalesExec?.Trim()      ?? DBNull.Value },
        new SqlParameter("@TargetDate",     SqlDbType.Date)          { Value = (object?)dto.TargetDate             ?? DBNull.Value },
        new SqlParameter("@RefProject",     SqlDbType.VarChar, 100)  { Value = (object?)dto.RefProject?.Trim()     ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()       ?? DBNull.Value },
        new SqlParameter("@DocAmdNotes",    SqlDbType.VarChar, 250)  { Value = (object?)dto.DocAmdNotes?.Trim()    ?? DBNull.Value },
        new SqlParameter("@TaxType",        SqlDbType.VarChar,  20)  { Value = (object?)dto.TaxType?.Trim()        ?? DBNull.Value },
        new SqlParameter("@BasicValue",     SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue      ?? DBNull.Value },
        new SqlParameter("@ExemptValue",    SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.ExemptValue     ?? DBNull.Value },
        new SqlParameter("@TaxableValue",   SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.TaxableValue    ?? DBNull.Value },
        new SqlParameter("@PnFAmt",         SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.PnFAmt          ?? DBNull.Value },
        new SqlParameter("@FreightAmt",     SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.FreightAmt      ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.IGSTAmt         ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.CGSTAmt         ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.SGSTAmt         ?? DBNull.Value },
        new SqlParameter("@OtherCharges",   SqlDbType.VarChar,  50)  { Value = (object?)dto.OtherCharges?.Trim()   ?? DBNull.Value },
        new SqlParameter("@OtherAmt",       SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.OtherAmt        ?? DBNull.Value },
        new SqlParameter("@RoundOff",       SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.RoundOff        ?? DBNull.Value },
        new SqlParameter("@NetValue",       SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.NetValue         ?? DBNull.Value },
        new SqlParameter("@TxnCurrency",    SqlDbType.VarChar,  30)  { Value = (object?)dto.TxnCurrency?.Trim()    ?? DBNull.Value },
        new SqlParameter("@CurrencyRate",   SqlDbType.Decimal)       { Precision = 12, Scale = 4, Value = (object?)dto.CurrencyRate    ?? DBNull.Value },
        new SqlParameter("@CurrencyAmount", SqlDbType.Decimal)       { Precision = 14, Scale = 2, Value = (object?)dto.CurrencyAmount  ?? DBNull.Value },
        new SqlParameter("@StateName",      SqlDbType.VarChar,  50)  { Value = (object?)dto.StateName?.Trim()      ?? DBNull.Value },
        new SqlParameter("@FileAttach",     SqlDbType.VarChar, 100)  { Value = (object?)dto.FileAttach?.Trim()     ?? DBNull.Value },
        new SqlParameter("@Zone",           SqlDbType.VarChar,  50)  { Value = (object?)dto.Zone?.Trim()           ?? DBNull.Value },
        new SqlParameter("@QtnCategory",    SqlDbType.VarChar,  50)  { Value = (object?)dto.QtnCategory?.Trim()    ?? DBNull.Value },
        new SqlParameter("@ChassisNo",      SqlDbType.VarChar,  50)  { Value = (object?)dto.ChassisNo?.Trim()      ?? DBNull.Value },
        new SqlParameter("@EngineNo",       SqlDbType.VarChar,  50)  { Value = (object?)dto.EngineNo?.Trim()       ?? DBNull.Value },
        new SqlParameter("@VehicleModel",   SqlDbType.VarChar,  50)  { Value = (object?)dto.VehicleModel?.Trim()   ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP               ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)           { Value = (object?)dto.CreatedBy              ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn              ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)           { Value = (object?)dto.ModifiedBy             ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn             ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason           ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1)  { Value = (object?)dto.LockStatus             ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)           { Value = (object?)dto.SubscID                ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QtnMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate, @DocAmdNo,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
            @PhoneNo, @EmailID, @CPName, @CPDesignation, @CPPhone,
            @RefNo, @RefDate, @SalesExec, @TargetDate, @RefProject,
            @DocNotes, @DocAmdNotes, @TaxType,
            @BasicValue, @ExemptValue, @TaxableValue,
            @PnFAmt, @FreightAmt, @IGSTAmt, @CGSTAmt, @SGSTAmt,
            @OtherCharges, @OtherAmt, @RoundOff, @NetValue,
            @TxnCurrency, @CurrencyRate, @CurrencyAmount,
            @StateName, @FileAttach, @Zone, @QtnCategory,
            @ChassisNo, @EngineNo, @VehicleModel,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_QtnDtl ─────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallQtnDtlSP(SP_QtnDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",       dto.TransSlNo),
        new SqlParameter("@MTransNo",        dto.MTransNo),
        new SqlParameter("@SLNo",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.SLNo            ?? DBNull.Value },
        new SqlParameter("@LinkRefYear",     SqlDbType.Int)          { Value = (object?)dto.LinkRefYear                                  ?? DBNull.Value },
        new SqlParameter("@LinkRefNo",       SqlDbType.Int)          { Value = (object?)dto.LinkRefNo                                    ?? DBNull.Value },
        new SqlParameter("@LinkRefId",       SqlDbType.Int)          { Value = (object?)dto.LinkRefId                                    ?? DBNull.Value },
        new SqlParameter("@PartNo",          SqlDbType.Int)          { Value = (object?)dto.PartNo                                       ?? DBNull.Value },
        new SqlParameter("@PartDescription", SqlDbType.VarChar, 250) { Value = (object?)dto.PartDescription?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",     SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@CustPartNo",      SqlDbType.VarChar,  50) { Value = (object?)dto.CustPartNo?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@DeliveryDate",    SqlDbType.Date)         { Value = (object?)dto.DeliveryDate                                 ?? DBNull.Value },
        new SqlParameter("@Qty",             SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.Qty               ?? DBNull.Value },
        new SqlParameter("@ConvQty",         SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.ConvQty           ?? DBNull.Value },
        new SqlParameter("@TxnUnit",         SqlDbType.Int)          { Value = (object?)dto.TxnUnit                                      ?? DBNull.Value },
        new SqlParameter("@Unit",            SqlDbType.VarChar,  10) { Value = (object?)dto.Unit?.Trim()                                 ?? DBNull.Value },
        new SqlParameter("@Rate",            SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.Rate              ?? DBNull.Value },
        new SqlParameter("@BasicValue",      SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.BasicValue        ?? DBNull.Value },
        new SqlParameter("@DiscP",           SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.DiscP             ?? DBNull.Value },
        new SqlParameter("@DiscAmt",         SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.DiscAmt           ?? DBNull.Value },
        new SqlParameter("@PnFP",            SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.PnFP              ?? DBNull.Value },
        new SqlParameter("@PnFAmt",          SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.PnFAmt            ?? DBNull.Value },
        new SqlParameter("@FreightRate",     SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.FreightRate       ?? DBNull.Value },
        new SqlParameter("@FreightAmt",      SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.FreightAmt        ?? DBNull.Value },
        new SqlParameter("@TaxableValue",    SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.TaxableValue      ?? DBNull.Value },
        new SqlParameter("@GSTP",            SqlDbType.Decimal)      { Precision =  6, Scale = 2, Value = (object?)dto.GSTP              ?? DBNull.Value },
        new SqlParameter("@IGSTAmt",         SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.IGSTAmt           ?? DBNull.Value },
        new SqlParameter("@CGSTAmt",         SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.CGSTAmt           ?? DBNull.Value },
        new SqlParameter("@SGSTAmt",         SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.SGSTAmt           ?? DBNull.Value },
        new SqlParameter("@TotalValue",      SqlDbType.Decimal)      { Precision = 16, Scale = 2, Value = (object?)dto.TotalValue        ?? DBNull.Value },
        new SqlParameter("@LinkNo",          SqlDbType.Int)          { Value = (object?)dto.LinkNo                                       ?? DBNull.Value },
        new SqlParameter("@PartCode",        SqlDbType.VarChar,  50) { Value = (object?)dto.PartCode?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@MocSysCode",      SqlDbType.VarChar, 100) { Value = (object?)dto.MocSysCode?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@MocSysDesc",      SqlDbType.VarChar, 500) { Value = (object?)dto.MocSysDesc?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@MocCode",         SqlDbType.VarChar, 100) { Value = (object?)dto.MocCode?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@MocDescription",  SqlDbType.VarChar, 500) { Value = (object?)dto.MocDescription?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@ValveCode",       SqlDbType.VarChar, 100) { Value = (object?)dto.ValveCode?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@ValveDescription",SqlDbType.VarChar, 500) { Value = (object?)dto.ValveDescription?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@MOCShort",        SqlDbType.VarChar, 250) { Value = (object?)dto.MOCShort?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@Deviation",       SqlDbType.VarChar, 250) { Value = (object?)dto.Deviation?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@SpecialReq",      SqlDbType.VarChar, 250) { Value = (object?)dto.SpecialReq?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@PartSysCode",     SqlDbType.VarChar, 100) { Value = (object?)dto.PartSysCode?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@PartSysDesc",     SqlDbType.VarChar, 500) { Value = (object?)dto.PartSysDesc?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@AmdNo",           SqlDbType.Int)          { Value = (object?)dto.AmdNo                                        ?? DBNull.Value },
        new SqlParameter("@AmdMode",         SqlDbType.VarChar,  10) { Value = (object?)dto.AmdMode?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@SystemIP",        SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                     ?? DBNull.Value },
        new SqlParameter("@CreatedBy",       SqlDbType.Int)          { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",       SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",      SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason",    SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                                 ?? DBNull.Value },
        new SqlParameter("@LockStatus",      SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                                   ?? "N"         },
        new SqlParameter("@SubscID",         SqlDbType.Int)          { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",          dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QtnDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SLNo,
            @LinkRefYear, @LinkRefNo, @LinkRefId,
            @PartNo, @PartDescription, @ItemRemarks, @CustPartNo, @DeliveryDate,
            @Qty, @ConvQty, @TxnUnit, @Unit, @Rate, @BasicValue,
            @DiscP, @DiscAmt, @PnFP, @PnFAmt, @FreightRate, @FreightAmt,
            @TaxableValue, @GSTP, @IGSTAmt, @CGSTAmt, @SGSTAmt, @TotalValue,
            @LinkNo, @PartCode, @MocSysCode, @MocSysDesc, @MocCode, @MocDescription,
            @ValveCode, @ValveDescription, @MOCShort, @Deviation, @SpecialReq,
            @PartSysCode, @PartSysDesc, @AmdNo, @AmdMode,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_QtnTerms ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallQtnTermsSP(SP_QtnTerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QtnTerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_QtnExtras ──────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallQtnExtrasSP(SP_QtnExtras dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 100) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QtnExtras
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_QTNNotes ───────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallQTNNotesSP(SP_QTNNotes dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo  ?? DBNull.Value },
        new SqlParameter("@Notes",       SqlDbType.VarChar, 250) { Value = (object?)dto.Notes?.Trim()                    ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                         ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                        ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                        ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                       ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                       ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                     ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                       ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                          ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QTNNotes
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Notes, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_QtnAttachment ──────────────────────────────────────────
        public async Task<(int ID, string Message)> CallQtnAttachmentSP(SP_QtnAttachment dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@AttNotes",    SqlDbType.VarChar, 250) { Value = (object?)dto.AttNotes?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@AttFile",     SqlDbType.VarChar, 250) { Value = (object?)dto.AttFile?.Trim()                         ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_QtnAttachment
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @AttNotes, @AttFile, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
        public async Task<(int ID, string Message)> CallRGPMstSP(SP_RGPMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",      dto.MTransNo),
        new SqlParameter("@MCount",        dto.MCount),
        new SqlParameter("@DocSeries",     SqlDbType.VarChar,  10)  { Value = (object?)dto.DocSeries?.Trim()    ?? DBNull.Value },
        new SqlParameter("@DocYear",       SqlDbType.Int)           { Value = (object?)dto.DocYear              ?? DBNull.Value },
        new SqlParameter("@DocNo",         SqlDbType.Int)           { Value = (object?)dto.DocNo                ?? DBNull.Value },
        new SqlParameter("@DocDate",       SqlDbType.Date)          { Value = (object?)dto.DocDate              ?? DBNull.Value },
        new SqlParameter("@RGPType",       SqlDbType.VarChar,  10)  { Value = (object?)dto.RGPType?.Trim()      ?? DBNull.Value },
        new SqlParameter("@TargetTransNo", SqlDbType.Int)           { Value = (object?)dto.TargetTransNo        ?? DBNull.Value },
        new SqlParameter("@TargetDocYear", SqlDbType.Int)           { Value = (object?)dto.TargetDocYear        ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",   SqlDbType.Int)           { Value = (object?)dto.TargetDocNo          ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",   SqlDbType.Int)           { Value = (object?)dto.TargetAmdNo          ?? DBNull.Value },
        new SqlParameter("@VendorNo",      SqlDbType.Int)           { Value = (object?)dto.VendorNo             ?? DBNull.Value },
        new SqlParameter("@VendorCode",    SqlDbType.VarChar,  20)  { Value = (object?)dto.VendorCode?.Trim()   ?? DBNull.Value },
        new SqlParameter("@VendorName",    SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()   ?? DBNull.Value },
        new SqlParameter("@Address",       SqlDbType.VarChar, 500)  { Value = (object?)dto.Address?.Trim()      ?? DBNull.Value },
        new SqlParameter("@GSTIN",         SqlDbType.VarChar,  20)  { Value = (object?)dto.GSTIN?.Trim()        ?? DBNull.Value },
        new SqlParameter("@StateName",     SqlDbType.VarChar,  50)  { Value = (object?)dto.StateName?.Trim()    ?? DBNull.Value },
        new SqlParameter("@PhoneNo",       SqlDbType.VarChar,  30)  { Value = (object?)dto.PhoneNo?.Trim()      ?? DBNull.Value },
        new SqlParameter("@EmailID",       SqlDbType.VarChar,  80)  { Value = (object?)dto.EmailID?.Trim()      ?? DBNull.Value },
        new SqlParameter("@RefNo",         SqlDbType.VarChar,  50)  { Value = (object?)dto.RefNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefDate",       SqlDbType.Date)          { Value = (object?)dto.RefDate              ?? DBNull.Value },
        new SqlParameter("@DueDate",       SqlDbType.Date)          { Value = (object?)dto.DueDate              ?? DBNull.Value },
        new SqlParameter("@DocNotes",      SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()     ?? DBNull.Value },
        new SqlParameter("@DocStatus",     SqlDbType.Char,      1)  { Value = (object?)"N"                                      },
        new SqlParameter("@ApproxValue",   SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.ApproxValue ?? DBNull.Value },
        new SqlParameter("@SystemIP",      SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP             ?? DBNull.Value },
        new SqlParameter("@CreatedBy",     SqlDbType.Int)           { Value = (object?)dto.CreatedBy            ?? DBNull.Value },
        new SqlParameter("@CreatedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn            ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",    SqlDbType.Int)           { Value = (object?)dto.ModifiedBy           ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",    SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn           ?? DBNull.Value },
        new SqlParameter("@DeleteReason",  SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason         ?? DBNull.Value },
        new SqlParameter("@LockStatus",    SqlDbType.Char,      1)  { Value = (object?)dto.LockStatus           ?? "N"         },
        new SqlParameter("@SubscID",       SqlDbType.Int)           { Value = (object?)dto.SubscID              ?? DBNull.Value },
        new SqlParameter("@Status",        dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RGPMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate, @RGPType,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo,
            @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
            @StateName, @PhoneNo, @EmailID, @RefNo, @RefDate, @DueDate,
            @DocNotes, @DocStatus, @ApproxValue, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RGPDtl ────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRGPDtlSP(SP_RGPDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",      dto.TransSlNo),
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@SlNo",           SqlDbType.Decimal)      { Precision = 12, Scale = 2,  Value = (object?)dto.SlNo            ?? DBNull.Value },
        new SqlParameter("@PartNo",         SqlDbType.Int)          { Value = (object?)dto.PartNo                                       ?? DBNull.Value },
        new SqlParameter("@PartCode",       SqlDbType.VarChar,  30) { Value = (object?)dto.PartCode?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@PartDescription",SqlDbType.VarChar, 100) { Value = (object?)dto.PartDescription?.Trim()                      ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",    SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@HSNCode",        SqlDbType.VarChar,  10) { Value = (object?)dto.HSNCode?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@Qty",            SqlDbType.Decimal)      { Precision = 12, Scale = 4,  Value = (object?)dto.Qty              ?? DBNull.Value },
        new SqlParameter("@Unit",           SqlDbType.VarChar,  20) { Value = (object?)dto.Unit?.Trim()                                 ?? DBNull.Value },
        new SqlParameter("@Rate",           SqlDbType.Decimal)      { Precision = 14, Scale = 2,  Value = (object?)dto.Rate             ?? DBNull.Value },
        new SqlParameter("@Amount",         SqlDbType.Decimal)      { Precision = 16, Scale = 2,  Value = (object?)dto.Amount           ?? DBNull.Value },
        new SqlParameter("@GradeNo",        SqlDbType.Int)          { Value = (object?)dto.GradeNo                                      ?? DBNull.Value },
        new SqlParameter("@Grade",          SqlDbType.VarChar,  50) { Value = (object?)dto.Grade?.Trim()                                ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                     ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)          { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                                 ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                                   ?? "N"         },
        new SqlParameter("@SubscID",        SqlDbType.Int)          { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RGPDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @PartNo, @PartCode, @PartDescription, @ItemRemarks, @HSNCode,
            @Qty, @Unit, @Rate, @Amount, @GradeNo, @Grade,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RGPTerms ──────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRGPTermsSP(SP_RGPTerms dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",   dto.TransSlNo),
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 6, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)          { Value = (object?)dto.CreatedBy                               ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                               ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                              ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                              ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                            ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                              ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.Int)          { Value = (object?)dto.SubscID                                 ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RGPTerms
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @Title, @Description, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RGPInwMst ─────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRGPInwMstSP(SP_RGPInwMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",      dto.MTransNo),
        new SqlParameter("@MCount",        dto.MCount),
        new SqlParameter("@DocSeries",     SqlDbType.Char,     10)  { Value = (object?)dto.DocSeries?.Trim()    ?? DBNull.Value },
        new SqlParameter("@DocYear",       SqlDbType.Int)           { Value = (object?)dto.DocYear              ?? DBNull.Value },
        new SqlParameter("@DocNo",         SqlDbType.Int)           { Value = (object?)dto.DocNo                ?? DBNull.Value },
        new SqlParameter("@DocDate",       SqlDbType.Date)          { Value = (object?)dto.DocDate              ?? DBNull.Value },
        new SqlParameter("@TargetTransNo", SqlDbType.Int)           { Value = (object?)dto.TargetTransNo        ?? DBNull.Value },
        new SqlParameter("@TargetDocYear", SqlDbType.Int)           { Value = (object?)dto.TargetDocYear        ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",   SqlDbType.Int)           { Value = (object?)dto.TargetDocNo          ?? DBNull.Value },
        new SqlParameter("@TargetAmdNo",   SqlDbType.Int)           { Value = (object?)dto.TargetAmdNo          ?? DBNull.Value },
        new SqlParameter("@TargetType",    SqlDbType.VarChar,  10)  { Value = (object?)dto.TargetType?.Trim()   ?? DBNull.Value },
        new SqlParameter("@VendorNo",      SqlDbType.Int)           { Value = (object?)dto.VendorNo             ?? DBNull.Value },
        new SqlParameter("@VendorCode",    SqlDbType.VarChar,  20)  { Value = (object?)dto.VendorCode?.Trim()   ?? DBNull.Value },
        new SqlParameter("@VendorName",    SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()   ?? DBNull.Value },
        new SqlParameter("@Address",       SqlDbType.VarChar, 500)  { Value = (object?)dto.Address?.Trim()      ?? DBNull.Value },
        new SqlParameter("@GSTIN",         SqlDbType.VarChar,  20)  { Value = (object?)dto.GSTIN?.Trim()        ?? DBNull.Value },
        new SqlParameter("@StateName",     SqlDbType.VarChar,  50)  { Value = (object?)dto.StateName?.Trim()    ?? DBNull.Value },
        new SqlParameter("@PhoneNo",       SqlDbType.VarChar,  30)  { Value = (object?)dto.PhoneNo?.Trim()      ?? DBNull.Value },
        new SqlParameter("@EmailID",       SqlDbType.VarChar,  80)  { Value = (object?)dto.EmailID?.Trim()      ?? DBNull.Value },
        new SqlParameter("@RefNo",         SqlDbType.VarChar,  50)  { Value = (object?)dto.RefNo?.Trim()        ?? DBNull.Value },
        new SqlParameter("@RefDate",       SqlDbType.Date)          { Value = (object?)dto.RefDate              ?? DBNull.Value },
        new SqlParameter("@InwardNo",      SqlDbType.VarChar,  50)  { Value = (object?)dto.InwardNo?.Trim()     ?? DBNull.Value },
        new SqlParameter("@InwardDate",    SqlDbType.Date)          { Value = (object?)dto.InwardDate           ?? DBNull.Value },
        new SqlParameter("@LRNo",          SqlDbType.VarChar,  50)  { Value = (object?)dto.LRNo?.Trim()         ?? DBNull.Value },
        new SqlParameter("@LRDate",        SqlDbType.Date)          { Value = (object?)dto.LRDate               ?? DBNull.Value },
        new SqlParameter("@DCNo",          SqlDbType.VarChar,  50)  { Value = (object?)dto.DCNo?.Trim()         ?? DBNull.Value },
        new SqlParameter("@DCDate",        SqlDbType.Date)          { Value = (object?)dto.DCDate               ?? DBNull.Value },
        new SqlParameter("@DocNotes",      SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()     ?? DBNull.Value },
        new SqlParameter("@DocStatus",     SqlDbType.Char,      1)  { Value = (object?)"N"                                      },
        new SqlParameter("@SystemIP",      SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP             ?? DBNull.Value },
        new SqlParameter("@CreatedBy",     SqlDbType.Int)           { Value = (object?)dto.CreatedBy            ?? DBNull.Value },
        new SqlParameter("@CreatedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn            ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",    SqlDbType.Int)           { Value = (object?)dto.ModifiedBy           ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",    SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn           ?? DBNull.Value },
        new SqlParameter("@DeleteReason",  SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason         ?? DBNull.Value },
        new SqlParameter("@LockStatus",    SqlDbType.Char,      1)  { Value = (object?)dto.LockStatus           ?? "N"         },
        new SqlParameter("@SubscID",       SqlDbType.Int)           { Value = (object?)dto.SubscID              ?? DBNull.Value },
        new SqlParameter("@Status",        dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RGPInwMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate,
            @TargetTransNo, @TargetDocYear, @TargetDocNo, @TargetAmdNo, @TargetType,
            @VendorNo, @VendorCode, @VendorName, @Address, @GSTIN,
            @StateName, @PhoneNo, @EmailID, @RefNo, @RefDate,
            @InwardNo, @InwardDate, @LRNo, @LRDate, @DCNo, @DCDate,
            @DocNotes, @DocStatus, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RGPInwDtl ─────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRGPInwDtlSP(SP_RGPInwDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",       dto.TransSlNo),
        new SqlParameter("@MTransNo",        dto.MTransNo),
        new SqlParameter("@SlNo",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.SlNo            ?? DBNull.Value },
        new SqlParameter("@PartNo",          SqlDbType.Int)          { Value = (object?)dto.PartNo                                      ?? DBNull.Value },
        new SqlParameter("@PartCode",        SqlDbType.VarChar,  30) { Value = (object?)dto.PartCode?.Trim()                            ?? DBNull.Value },
        new SqlParameter("@PartDescription", SqlDbType.VarChar, 100) { Value = (object?)dto.PartDescription?.Trim()                     ?? DBNull.Value },
        new SqlParameter("@ItemRemarks",     SqlDbType.VarChar, 500) { Value = (object?)dto.ItemRemarks?.Trim()                         ?? DBNull.Value },
        new SqlParameter("@HSNCode",         SqlDbType.VarChar,  10) { Value = (object?)dto.HSNCode?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@RGPQty",          SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = (object?)dto.RGPQty           ?? DBNull.Value },
        new SqlParameter("@Qty",             SqlDbType.Decimal)      { Precision = 12, Scale = 4, Value = (object?)dto.Qty              ?? DBNull.Value },
        new SqlParameter("@Unit",            SqlDbType.VarChar,  20) { Value = (object?)dto.Unit?.Trim()                                ?? DBNull.Value },
        new SqlParameter("@Rate",            SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = (object?)dto.Rate             ?? DBNull.Value },
        new SqlParameter("@GradeNo",         SqlDbType.Int)          { Value = (object?)dto.GradeNo                                     ?? DBNull.Value },
        new SqlParameter("@Grade",           SqlDbType.VarChar,  50) { Value = (object?)dto.Grade?.Trim()                               ?? DBNull.Value },
        new SqlParameter("@SystemIP",        SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                    ?? DBNull.Value },
        new SqlParameter("@CreatedBy",       SqlDbType.Int)          { Value = (object?)dto.CreatedBy                                   ?? DBNull.Value },
        new SqlParameter("@CreatedOn",       SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",      SqlDbType.Int)          { Value = (object?)dto.ModifiedBy                                  ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",      SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn                                  ?? DBNull.Value },
        new SqlParameter("@DeleteReason",    SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                                ?? DBNull.Value },
        new SqlParameter("@LockStatus",      SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                                  ?? "N"         },
        new SqlParameter("@SubscID",         SqlDbType.Int)          { Value = (object?)dto.SubscID                                     ?? DBNull.Value },
        new SqlParameter("@Status",          dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RGPInwDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @SlNo,
            @PartNo, @PartCode, @PartDescription, @ItemRemarks, @HSNCode,
            @RGPQty, @Qty, @Unit, @Rate, @GradeNo, @Grade,
            @SystemIP, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }
        public async Task<(int ID, string Message)> CallExtraTermsMasterSP(SP_ExtraTermsMaster dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@MCount",      dto.MCount),
        new SqlParameter("@SlNo",        SqlDbType.Decimal)      { Precision = 10, Scale = 2, Value = (object?)dto.SlNo          ?? DBNull.Value },
        new SqlParameter("@ModuleName",  SqlDbType.VarChar,  50) { Value = (object?)dto.ModuleName?.Trim()                        ?? DBNull.Value },
        new SqlParameter("@Title",       SqlDbType.VarChar,  30) { Value = (object?)dto.Title?.Trim()                             ?? DBNull.Value },
        new SqlParameter("@Description", SqlDbType.VarChar, 250) { Value = (object?)dto.Description?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@ConfYN",      SqlDbType.Char,      1) { Value = (object?)dto.ConfYN                                    ?? "N"         },
        new SqlParameter("@TempNo",      SqlDbType.Int)          { Value = (object?)dto.TempNo                                    ?? DBNull.Value },
        new SqlParameter("@SystemIP",    SqlDbType.VarChar,  20) { Value = (object?)dto.SystemIP                                  ?? DBNull.Value },
        new SqlParameter("@CreatedBy",   SqlDbType.SmallInt)     { Value = (object?)dto.CreatedBy                                 ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime)     { Value = (object?)dto.CreatedOn                                 ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.SmallInt)     { Value = (object?)dto.ModifiedBy                                ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime)     { Value = (object?)dto.ModifiedOn                                ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                              ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                                ?? "N"         },
        new SqlParameter("@SubscID",     SqlDbType.SmallInt)     { Value = (object?)dto.SubscID                                   ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_ExtraTermsMaster
            @ID OUTPUT, @MTransNo, @MCount,
            @SlNo, @ModuleName, @Title, @Description, @ConfYN,
            @TempNo, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        public async Task<(int ID, string Message)> CallSalesReturnMstSP(SP_SalesReturnMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@MCount",         dto.MCount),
        new SqlParameter("@DocSeries",      SqlDbType.Char,     10)  { Value = (object?)dto.DocSeries?.Trim()     ?? DBNull.Value },
        new SqlParameter("@DocYear",        SqlDbType.Int)           { Value = (object?)dto.DocYear               ?? DBNull.Value },
        new SqlParameter("@DocNo",          SqlDbType.Int)           { Value = (object?)dto.DocNo                 ?? DBNull.Value },
        new SqlParameter("@DocDate",        SqlDbType.Date)          { Value = (object?)dto.DocDate               ?? DBNull.Value },
        new SqlParameter("@TargetTransNo",  SqlDbType.Int)           { Value = (object?)dto.TargetTransNo         ?? DBNull.Value },
        new SqlParameter("@TargetDocYear",  SqlDbType.Int)           { Value = (object?)dto.TargetDocYear         ?? DBNull.Value },
        new SqlParameter("@TargetDocNo",    SqlDbType.Int)           { Value = (object?)dto.TargetDocNo           ?? DBNull.Value },
        new SqlParameter("@VendorNo",       SqlDbType.Int)           { Value = (object?)dto.VendorNo              ?? DBNull.Value },
        new SqlParameter("@VendorNoShip",   SqlDbType.Int)           { Value = (object?)dto.VendorNoShip          ?? DBNull.Value },
        new SqlParameter("@VendorName",     SqlDbType.VarChar, 100)  { Value = (object?)dto.VendorName?.Trim()    ?? DBNull.Value },
        new SqlParameter("@TxnGSTIN",       SqlDbType.VarChar,  20)  { Value = (object?)dto.TxnGSTIN?.Trim()      ?? DBNull.Value },
        new SqlParameter("@TaxType",        SqlDbType.VarChar,  20)  { Value = (object?)dto.TaxType?.Trim()       ?? DBNull.Value },
        new SqlParameter("@RefNo",          SqlDbType.VarChar,  30)  { Value = (object?)dto.RefNo?.Trim()         ?? DBNull.Value },
        new SqlParameter("@RefDate",        SqlDbType.Date)          { Value = (object?)dto.RefDate               ?? DBNull.Value },
        new SqlParameter("@InvValue",       SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.InvValue       ?? (object)0m },
        new SqlParameter("@TaxableValue",   SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.TaxableValue   ?? (object)0m },
        new SqlParameter("@TaxP",           SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.TaxP           ?? (object)0m },
        new SqlParameter("@IGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.IGSTAmt        ?? (object)0m },
        new SqlParameter("@CGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.CGSTAmt        ?? (object)0m },
        new SqlParameter("@SGSTAmt",        SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.SGSTAmt        ?? (object)0m },
        new SqlParameter("@RoundOff",       SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.RoundOff       ?? (object)0m },
        new SqlParameter("@NetValue",       SqlDbType.Decimal)       { Precision = 18, Scale = 2, Value = (object?)dto.NetValue        ?? (object)0m },
        new SqlParameter("@TxnCurrency",    SqlDbType.VarChar,  30)  { Value = (object?)dto.TxnCurrency?.Trim()   ?? "INR"           },
        new SqlParameter("@CurrencyRate",   SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.CurrencyRate   ?? DBNull.Value },
        new SqlParameter("@CurrencyAmount", SqlDbType.Decimal)       { Precision = 14, Scale = 2, Value = (object?)dto.CurrencyAmount ?? DBNull.Value },
        new SqlParameter("@DocNotes",       SqlDbType.VarChar, 250)  { Value = (object?)dto.DocNotes?.Trim()      ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP              ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)           { Value = (object?)dto.CreatedBy             ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn             ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)           { Value = (object?)dto.ModifiedBy            ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn            ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason          ?? DBNull.Value },
        new SqlParameter("@LockStatus",     SqlDbType.Char,      1)  { Value = (object?)dto.LockStatus            ?? "N"          },
        new SqlParameter("@SubscID",        SqlDbType.Int)           { Value = (object?)dto.SubscID               ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_SalesReturnMst
            @ID OUTPUT, @MTransNo, @MCount,
            @DocSeries, @DocYear, @DocNo, @DocDate,
            @TargetTransNo, @TargetDocYear, @TargetDocNo,
            @VendorNo, @VendorNoShip, @VendorName, @TxnGSTIN,
            @TaxType, @RefNo, @RefDate,
            @InvValue, @TaxableValue, @TaxP,
            @IGSTAmt, @CGSTAmt, @SGSTAmt,
            @RoundOff, @NetValue,
            @TxnCurrency, @CurrencyRate, @CurrencyAmount,
            @DocNotes, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @LockStatus, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }



        // ── SP_MachineMst ─────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallMachineMstSP(SP_MachineMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",    dto.MTransNo),
        new SqlParameter("@MCount",      dto.MCount),
        new SqlParameter("@MachineCode", SqlDbType.VarChar,   20) { Value = (object?)dto.MachineCode?.Trim() ?? DBNull.Value },
        new SqlParameter("@MachineName", SqlDbType.VarChar,  100) { Value = (object?)dto.MachineName?.Trim() ?? DBNull.Value },
        new SqlParameter("@DateofInst",  SqlDbType.DateTime2)     { Value = (object?)dto.DateofInst          ?? DBNull.Value },
        new SqlParameter("@Capacity",    SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.Capacity ?? DBNull.Value },
        new SqlParameter("@Operation",   SqlDbType.VarChar, 5000) { Value = (object?)dto.Operation           ?? DBNull.Value },
        new SqlParameter("@Remarks",     SqlDbType.VarChar,  200) { Value = (object?)dto.Remarks?.Trim()     ?? DBNull.Value },
        new SqlParameter("@LockStatus",  SqlDbType.Char,       1) { Value = (object?)dto.LockStatus          ?? "N"         },
        new SqlParameter("@CreatedBy",   SqlDbType.Int)           { Value = (object?)dto.CreatedBy            ?? DBNull.Value },
        new SqlParameter("@CreatedOn",   SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn            ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",  SqlDbType.Int)           { Value = (object?)dto.ModifiedBy           ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",  SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn           ?? DBNull.Value },
        new SqlParameter("@DeleteReason",SqlDbType.VarChar,  250) { Value = (object?)dto.DeleteReason         ?? DBNull.Value },
        new SqlParameter("@SubscID",     SqlDbType.Int)           { Value = (object?)dto.SubscID              ?? DBNull.Value },
        new SqlParameter("@Status",      dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_MachineMst
            @ID OUTPUT, @MTransNo, @MCount,
            @MachineCode, @MachineName, @DateofInst, @Capacity,
            @Operation, @Remarks, @LockStatus,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_DieMst ─────────────────────────────────────────────────
        public async Task<(int ID, string Message)> CallDieMstSP(SP_DieMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@MCount",       dto.MCount),
        new SqlParameter("@PartNo",       SqlDbType.Int)      { Value = (object?)dto.PartNo                                       ?? DBNull.Value },
        new SqlParameter("@GradeNo",      SqlDbType.Int)      { Value = (object?)dto.GradeNo                                      ?? DBNull.Value },
        new SqlParameter("@DieType",      SqlDbType.VarChar, 50) { Value = (object?)dto.DieType?.Trim()                           ?? DBNull.Value },
        new SqlParameter("@MaxQty",       SqlDbType.Decimal)  { Precision = 12, Scale = 2, Value = (object?)dto.MaxQty            ?? (object)0m   },
        new SqlParameter("@OpeningQty",   SqlDbType.Decimal)  { Precision = 12, Scale = 2, Value = (object?)dto.OpeningQty        ?? (object)0m   },
        new SqlParameter("@Remarks",      SqlDbType.VarChar, 200) { Value = (object?)dto.Remarks?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@LockStatus",   SqlDbType.Char,      1) { Value = (object?)dto.LockStatus                               ?? "N"         },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)      { Value = (object?)dto.CreatedBy                                    ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2){ Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)      { Value = (object?)dto.ModifiedBy                                   ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2){ Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                             ?? DBNull.Value },
        new SqlParameter("@SubscID",      SqlDbType.Int)      { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_DieMst
            @ID OUTPUT, @MTransNo, @MCount,
            @PartNo, @GradeNo, @DieType,
            @MaxQty, @OpeningQty, @Remarks, @LockStatus,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RoutingChartMst ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRoutingChartMstSP(SP_RoutingChartMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@MCount",       dto.MCount),
        new SqlParameter("@PartNo",       SqlDbType.Int)          { Value = (object?)dto.PartNo                 ?? DBNull.Value },
        new SqlParameter("@GradeNo",      SqlDbType.Int)          { Value = (object?)dto.GradeNo                ?? DBNull.Value },
        new SqlParameter("@RouteName",    SqlDbType.VarChar, 100) { Value = (object?)dto.RouteName?.Trim()      ?? DBNull.Value },
        new SqlParameter("@BatchQty",     SqlDbType.Int)          { Value = (object?)dto.BatchQty               ?? (object)1    },
        new SqlParameter("@DieType",      SqlDbType.VarChar,  50) { Value = (object?)dto.DieType?.Trim()        ?? DBNull.Value },
        new SqlParameter("@LockStatus",   SqlDbType.Char,       1){ Value = (object?)dto.LockStatus             ?? "N"         },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)          { Value = (object?)dto.CreatedBy              ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2)    { Value = (object?)dto.CreatedOn              ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)          { Value = (object?)dto.ModifiedBy             ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2)    { Value = (object?)dto.ModifiedOn             ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason           ?? DBNull.Value },
        new SqlParameter("@SubscID",      SqlDbType.Int)          { Value = (object?)dto.SubscID                ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RoutingChartMst
            @ID OUTPUT, @MTransNo, @MCount,
            @PartNo, @GradeNo, @RouteName, @BatchQty, @DieType,
            @LockStatus, @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_RoutingChartDtl ────────────────────────────────────────
        public async Task<(int ID, string Message)> CallRoutingChartDtlSP(SP_RoutingChartDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",    dto.TransSlNo),
        new SqlParameter("@MTransNo",     dto.MTransNo),
        new SqlParameter("@SlNo",         SqlDbType.Int)     { Value = (object?)dto.SlNo                                          ?? DBNull.Value },
        new SqlParameter("@Operation",    SqlDbType.Int)     { Value = (object?)dto.Operation                                     ?? DBNull.Value },
        new SqlParameter("@MachineNo",    SqlDbType.Int)     { Value = (object?)dto.MachineNo                                     ?? DBNull.Value },
        new SqlParameter("@TimeMin",      SqlDbType.Decimal) { Precision = 8,  Scale = 2, Value = (object?)dto.TimeMin            ?? (object)0m   },
        new SqlParameter("@OperCost",     SqlDbType.Decimal) { Precision = 12, Scale = 2, Value = (object?)dto.OperCost           ?? (object)0m   },
        new SqlParameter("@CreatedBy",    SqlDbType.Int)     { Value = (object?)dto.CreatedBy                                     ?? DBNull.Value },
        new SqlParameter("@CreatedOn",    SqlDbType.DateTime2){ Value = (object?)dto.CreatedOn                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",   SqlDbType.Int)     { Value = (object?)dto.ModifiedBy                                    ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",   SqlDbType.DateTime2){ Value = (object?)dto.ModifiedOn                                   ?? DBNull.Value },
        new SqlParameter("@DeleteReason", SqlDbType.VarChar, 250) { Value = (object?)dto.DeleteReason                             ?? DBNull.Value },
        new SqlParameter("@SubscID",      SqlDbType.Int)     { Value = (object?)dto.SubscID                                      ?? DBNull.Value },
        new SqlParameter("@Status",       dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_RoutingChartDtl
            @ID OUTPUT, @TransSlNo, @MTransNo,
            @SlNo, @Operation, @MachineNo, @TimeMin, @OperCost,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }


        // ── SP_SalesReturnDtl ─────────────────────────────────────────
        public async Task<(int ID, string Message)> CallSalesReturnDtlSP(SP_SalesReturnDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };

            var p = new[]
            {
        id,
        new SqlParameter("@TransSlNo",      dto.TransSlNo),
        new SqlParameter("@MTransNo",       dto.MTransNo),
        new SqlParameter("@InvTransNo",     SqlDbType.Int)           { Value = (object?)dto.InvTransNo                                ?? DBNull.Value },
        new SqlParameter("@SlNo",           SqlDbType.Int)           { Value = (object?)dto.SlNo                                      ?? DBNull.Value },
        new SqlParameter("@LinkNo",         SqlDbType.Int)           { Value = (object?)dto.LinkNo                                    ?? DBNull.Value },
        new SqlParameter("@PartNo",         SqlDbType.Int)           { Value = (object?)dto.PartNo                                    ?? DBNull.Value },
        new SqlParameter("@PartCode",       SqlDbType.VarChar,  50)  { Value = (object?)dto.PartCode?.Trim()                          ?? DBNull.Value },
        new SqlParameter("@Description",    SqlDbType.VarChar, 250)  { Value = (object?)dto.Description?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@MocDescription", SqlDbType.VarChar, 250)  { Value = (object?)dto.MocDescription?.Trim()                    ?? DBNull.Value },
        new SqlParameter("@Qty",            SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.Qty            ?? (object)0m   },
        new SqlParameter("@ReturnQty",      SqlDbType.Decimal)       { Precision = 12, Scale = 2, Value = (object?)dto.ReturnQty      ?? (object)0m   },
        new SqlParameter("@Unit",           SqlDbType.VarChar,  20)  { Value = (object?)dto.Unit?.Trim()                              ?? DBNull.Value },
        new SqlParameter("@Rate",           SqlDbType.Decimal)       { Precision = 14, Scale = 2, Value = (object?)dto.Rate           ?? (object)0m   },
        new SqlParameter("@GSTP",           SqlDbType.Decimal)       { Precision =  6, Scale = 2, Value = (object?)dto.GSTP           ?? (object)0m   },
        new SqlParameter("@TotalValue",     SqlDbType.Decimal)       { Precision = 16, Scale = 2, Value = (object?)dto.TotalValue      ?? (object)0m   },
        new SqlParameter("@ItemRemarks",    SqlDbType.VarChar, 250)  { Value = (object?)dto.ItemRemarks?.Trim()                       ?? DBNull.Value },
        new SqlParameter("@SystemIP",       SqlDbType.VarChar,  20)  { Value = (object?)dto.SystemIP                                  ?? DBNull.Value },
        new SqlParameter("@CreatedBy",      SqlDbType.Int)           { Value = (object?)dto.CreatedBy                                 ?? DBNull.Value },
        new SqlParameter("@CreatedOn",      SqlDbType.DateTime2)     { Value = (object?)dto.CreatedOn                                 ?? DBNull.Value },
        new SqlParameter("@ModifiedBy",     SqlDbType.Int)           { Value = (object?)dto.ModifiedBy                                ?? DBNull.Value },
        new SqlParameter("@ModifiedOn",     SqlDbType.DateTime2)     { Value = (object?)dto.ModifiedOn                                ?? DBNull.Value },
        new SqlParameter("@DeleteReason",   SqlDbType.VarChar, 250)  { Value = (object?)dto.DeleteReason                              ?? DBNull.Value },
        new SqlParameter("@SubscID",        SqlDbType.Int)           { Value = (object?)dto.SubscID                                   ?? DBNull.Value },
        new SqlParameter("@Status",         dto.Status),
        msg
    };

            await Database.ExecuteSqlRawAsync("""
        EXEC dbo.SP_SalesReturnDtl
            @ID OUTPUT, @TransSlNo, @MTransNo, @InvTransNo,
            @SlNo, @LinkNo, @PartNo, @PartCode,
            @Description, @MocDescription,
            @Qty, @ReturnQty, @Unit, @Rate, @GSTP, @TotalValue,
            @ItemRemarks, @SystemIP,
            @CreatedBy, @CreatedOn, @ModifiedBy, @ModifiedOn,
            @DeleteReason, @SubscID, @Status, @Message OUTPUT
        """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }


    }
}
