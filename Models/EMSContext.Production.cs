using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Production slice of the context: the MRN tables (SQL 44) and the
    /// procedures the MRN / MRNG controllers call (SQL 44 + 45).
    /// </summary>
    public partial class EMSContext
    {
        public DbSet<MRNMst> MRNMst { get; set; } = null!;
        public DbSet<MRNDtl> MRNDtl { get; set; } = null!;

        private static object MrnDbValue(object? value) => value ?? DBNull.Value;

        // ── SP_MRNMst — inserts allocate the DocNo; returns MTransNo ──────
        public async Task<int> CallMRNMstSP(SP_MRNMst dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id,
                new SqlParameter("@MTransNo",      SqlDbType.Int)          { Value = dto.MTransNo },
                new SqlParameter("@DocSeries",     SqlDbType.Char,      10) { Value = dto.DocSeries },
                new SqlParameter("@DocYear",       SqlDbType.Int)          { Value = dto.DocYear },
                new SqlParameter("@DocNo",         SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@DocDate",       SqlDbType.Date)         { Value = dto.DocDate.Date },
                new SqlParameter("@TargetTransNo", SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@TargetDocYear", SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@TargetDocNo",   SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@TargetAmdNo",   SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@MRNType",       SqlDbType.VarChar,   30) { Value = dto.MRNType },
                new SqlParameter("@Requiredby",    SqlDbType.VarChar,   50) { Value = MrnDbValue(dto.Requiredby) },
                new SqlParameter("@Department",    SqlDbType.VarChar,   50) { Value = MrnDbValue(dto.Department) },
                new SqlParameter("@RefNo",         SqlDbType.VarChar,   50) { Value = "" },
                new SqlParameter("@RefDate",       SqlDbType.Date)         { Value = dto.DocDate.Date },
                new SqlParameter("@DocNotes",      SqlDbType.VarChar,  250) { Value = MrnDbValue(dto.DocNotes) },
                new SqlParameter("@DocStatus",     SqlDbType.Char,       1) { Value = "N" },
                new SqlParameter("@SystemIP",      SqlDbType.VarChar,   20) { Value = MrnDbValue(dto.SystemIP) },
                new SqlParameter("@LockStatus",    SqlDbType.Char,       1) { Value = dto.LockStatus },
                new SqlParameter("@CreatedBy",     SqlDbType.Int)          { Value = dto.CreatedBy },
                new SqlParameter("@CreatedOn",     SqlDbType.DateTime)     { Value = dto.CreatedOn },
                new SqlParameter("@SubscID",       SqlDbType.Int)          { Value = dto.SubscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_MRNMst
                    @ID = @ID OUTPUT, @MTransNo = @MTransNo, @DocSeries = @DocSeries, @DocYear = @DocYear,
                    @DocNo = @DocNo, @DocDate = @DocDate, @TargetTransNo = @TargetTransNo,
                    @TargetDocYear = @TargetDocYear, @TargetDocNo = @TargetDocNo, @TargetAmdNo = @TargetAmdNo,
                    @MRNType = @MRNType, @Requiredby = @Requiredby, @Department = @Department,
                    @RefNo = @RefNo, @RefDate = @RefDate, @DocNotes = @DocNotes, @DocStatus = @DocStatus,
                    @SystemIP = @SystemIP, @LockStatus = @LockStatus, @CreatedBy = @CreatedBy,
                    @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);

            return id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value);
        }

        // ── SP_MRNDtl — insert / update one line; returns TransSlNo ──────
        public async Task<int> CallMRNDtlSP(SP_MRNDtl dto)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id,
                new SqlParameter("@MTransNo",        SqlDbType.Int)          { Value = dto.MTransNo },
                new SqlParameter("@TransSlNo",       SqlDbType.Int)          { Value = dto.TransSlNo },
                new SqlParameter("@SlNo",            SqlDbType.Int)          { Value = dto.SlNo },
                new SqlParameter("@LinkNo",          SqlDbType.Int)          { Value = 0 },
                new SqlParameter("@PartNo",          SqlDbType.Int)          { Value = dto.PartNo },
                new SqlParameter("@OAYear",          SqlDbType.Int)          { Value = MrnDbValue(dto.OAYear) },
                new SqlParameter("@OANo",            SqlDbType.Int)          { Value = MrnDbValue(dto.OANo) },
                new SqlParameter("@OAID",            SqlDbType.Int)          { Value = MrnDbValue(dto.OAID) },
                new SqlParameter("@PartDescription", SqlDbType.VarChar,  250) { Value = MrnDbValue(dto.PartDescription) },
                new SqlParameter("@GradeNo",         SqlDbType.Int)          { Value = MrnDbValue(dto.GradeNo) },
                new SqlParameter("@Grade",           SqlDbType.VarChar,   50) { Value = MrnDbValue(dto.Grade) },
                new SqlParameter("@ItemRemarks",     SqlDbType.VarChar,  250) { Value = MrnDbValue(dto.ItemRemarks) },
                new SqlParameter("@Store",           SqlDbType.VarChar,   50) { Value = DBNull.Value },
                new SqlParameter("@Qty",             SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = dto.Qty },
                new SqlParameter("@BalQty",          SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = dto.BalQty },
                new SqlParameter("@Unit",            SqlDbType.VarChar,   20) { Value = MrnDbValue(dto.Unit) },
                new SqlParameter("@Rate",            SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = dto.Rate },
                new SqlParameter("@Amount",          SqlDbType.Decimal)      { Precision = 14, Scale = 2, Value = dto.Amount },
                new SqlParameter("@ConvPartNo",      SqlDbType.Int)          { Value = MrnDbValue(dto.ConvPartNo) },
                new SqlParameter("@ConvDescription", SqlDbType.VarChar,  250) { Value = MrnDbValue(dto.ConvDescription) },
                new SqlParameter("@ConvGradeNo",     SqlDbType.Int)          { Value = MrnDbValue(dto.ConvGradeNo) },
                new SqlParameter("@ConvGrade",       SqlDbType.VarChar,   50) { Value = MrnDbValue(dto.ConvGrade) },
                new SqlParameter("@ConvQty",         SqlDbType.Decimal)      { Precision = 12, Scale = 2, Value = MrnDbValue(dto.ConvQty) },
                new SqlParameter("@CreatedBy",       SqlDbType.Int)          { Value = dto.CreatedBy },
                new SqlParameter("@CreatedOn",       SqlDbType.DateTime)     { Value = dto.CreatedOn },
                new SqlParameter("@SubscID",         SqlDbType.Int)          { Value = dto.SubscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_MRNDtl
                    @ID = @ID OUTPUT, @MTransNo = @MTransNo, @TransSlNo = @TransSlNo, @SlNo = @SlNo,
                    @LinkNo = @LinkNo, @PartNo = @PartNo, @OAYear = @OAYear, @OANo = @OANo, @OAID = @OAID,
                    @PartDescription = @PartDescription, @GradeNo = @GradeNo, @Grade = @Grade,
                    @ItemRemarks = @ItemRemarks, @Store = @Store, @Qty = @Qty, @BalQty = @BalQty,
                    @Unit = @Unit, @Rate = @Rate, @Amount = @Amount, @ConvPartNo = @ConvPartNo,
                    @ConvDescription = @ConvDescription, @ConvGradeNo = @ConvGradeNo,
                    @ConvGrade = @ConvGrade, @ConvQty = @ConvQty, @CreatedBy = @CreatedBy,
                    @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);

            return id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value);
        }

        // ── SP_MRNStatus (SQL 45) — Approve / Unlock / Delete / DtlDelete ──
        public async Task<(int ID, string Message)> CallMRNStatusSP(
            string action, string mrnType, int mTransNo, int transSlNo,
            int userNo, string? reason, string? systemIP, int subscID)
        {
            var id  = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var p = new[]
            {
                id, msg,
                new SqlParameter("@Action",    SqlDbType.VarChar,  20) { Value = action },
                new SqlParameter("@MRNType",   SqlDbType.VarChar,  30) { Value = mrnType },
                new SqlParameter("@MTransNo",  SqlDbType.Int)          { Value = mTransNo },
                new SqlParameter("@TransSlNo", SqlDbType.Int)          { Value = transSlNo },
                new SqlParameter("@UserNo",    SqlDbType.Int)          { Value = userNo },
                new SqlParameter("@Reason",    SqlDbType.VarChar, 250) { Value = MrnDbValue(reason) },
                new SqlParameter("@SystemIP",  SqlDbType.VarChar,  20) { Value = MrnDbValue(systemIP) },
                new SqlParameter("@SubscID",   SqlDbType.Int)          { Value = subscID },
            };

            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_MRNStatus
                    @ID = @ID OUTPUT, @Action = @Action, @MRNType = @MRNType, @MTransNo = @MTransNo,
                    @TransSlNo = @TransSlNo, @UserNo = @UserNo, @Reason = @Reason,
                    @SystemIP = @SystemIP, @SubscID = @SubscID, @Message = @Message OUTPUT
                """, p);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        // ── dbo.FN_OALinkCheck (SQL 36) — null when the OA link is fine ──
        public async Task<string?> CheckOALink(int oaYear, int oaNo, int oaID, int subscID)
        {
            return await Database
                .SqlQuery<string?>($"SELECT dbo.FN_OALinkCheck({oaYear}, {oaNo}, {oaID}, {subscID}) AS [Value]")
                .FirstOrDefaultAsync();
        }

        // ── dbo.FN_MRNIssueCount (SQL 45) — live Store Issues against an MRN ──
        public async Task<int> MRNIssueCount(int mTransNo)
        {
            return await Database
                .SqlQuery<int>($"SELECT dbo.FN_MRNIssueCount({mTransNo}) AS [Value]")
                .FirstOrDefaultAsync();
        }
    }
}
