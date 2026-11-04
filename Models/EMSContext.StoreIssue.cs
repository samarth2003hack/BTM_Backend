using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Store Issue / Store Returns slice of the context: the four tables from
    /// SQL 44, the stock and balance views from SQL 46, and the save
    /// procedure calls. Entity types live in StoreIssueModels.cs.
    /// </summary>
    public partial class EMSContext
    {
        public DbSet<StoreIssueMst> StoreIssueMst { get; set; } = null!;
        public DbSet<StoreIssueDtl> StoreIssueDtl { get; set; } = null!;
        public DbSet<StoreReturnsMst> StoreReturnsMst { get; set; } = null!;
        public DbSet<StoreReturnsDtl> StoreReturnsDtl { get; set; } = null!;
        public DbSet<StoreStockRow> StoreStock { get; set; } = null!;
        public DbSet<StoreIssueMRNLine> StoreIssueMRNLines { get; set; } = null!;
        public DbSet<StoreIssueLineBal> StoreIssueLineBals { get; set; } = null!;

        private static SqlParameter SiP(string name, SqlDbType type, object? value, int size = 0)
        {
            var p = size > 0 ? new SqlParameter(name, type, size) : new SqlParameter(name, type);
            p.Value = value ?? DBNull.Value;
            return p;
        }

        private static SqlParameter SiDec(string name, decimal? value, byte precision, byte scale) =>
            new(name, SqlDbType.Decimal) { Precision = precision, Scale = scale, Value = (object?)value ?? DBNull.Value };

        private static SqlParameter SiOutId() =>
            new("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };

        private static int SiIdOf(SqlParameter id) =>
            id.Value == DBNull.Value || id.Value == null ? 0 : Convert.ToInt32(id.Value);

        // ── SP_StoreIssueMst (allocates DocNo itself on insert) ──────────
        public async Task<int> CallStoreIssueMstSP(StoreIssueMst h)
        {
            var id = SiOutId();
            var p = new[]
            {
                id,
                SiP("@MTransNo",      SqlDbType.Int,      h.MTransNo),
                SiP("@DocSeries",     SqlDbType.Char,     h.DocSeries.Trim(), 10),
                SiP("@DocYear",       SqlDbType.Int,      h.DocYear),
                SiP("@DocNo",         SqlDbType.Int,      h.DocNo),
                SiP("@DocDate",       SqlDbType.Date,     h.DocDate.Date),
                SiP("@TargetTransNo", SqlDbType.Int,      h.TargetTransNo),
                SiP("@TargetDocYear", SqlDbType.Int,      h.TargetDocYear),
                SiP("@TargetDocNo",   SqlDbType.Int,      h.TargetDocNo),
                SiP("@TargetAmdNo",   SqlDbType.Int,      h.TargetAmdNo ?? 0),
                SiP("@TargetType",    SqlDbType.VarChar,  h.TargetType, 50),
                SiP("@IndentorName",  SqlDbType.VarChar,  h.IndentorName, 100),
                SiP("@Department",    SqlDbType.VarChar,  h.Department, 50),
                SiP("@DocNotes",      SqlDbType.VarChar,  h.DocNotes, 250),
                SiP("@DocStatus",     SqlDbType.Char,     h.DocStatus, 1),
                SiP("@SystemIP",      SqlDbType.VarChar,  h.SystemIP, 20),
                SiP("@LockStatus",    SqlDbType.Char,     h.LockStatus, 1),
                SiP("@CreatedBy",     SqlDbType.Int,      h.CreatedBy),
                SiP("@CreatedOn",     SqlDbType.DateTime, h.CreatedOn),
                SiP("@SubscID",       SqlDbType.Int,      h.SubscID),
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_StoreIssueMst @ID = @ID OUTPUT, @MTransNo = @MTransNo, @DocSeries = @DocSeries,
                    @DocYear = @DocYear, @DocNo = @DocNo, @DocDate = @DocDate, @TargetTransNo = @TargetTransNo,
                    @TargetDocYear = @TargetDocYear, @TargetDocNo = @TargetDocNo, @TargetAmdNo = @TargetAmdNo,
                    @TargetType = @TargetType, @IndentorName = @IndentorName, @Department = @Department,
                    @DocNotes = @DocNotes, @DocStatus = @DocStatus, @SystemIP = @SystemIP,
                    @LockStatus = @LockStatus, @CreatedBy = @CreatedBy, @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);
            return SiIdOf(id);
        }

        // ── SP_StoreIssueDtl ─────────────────────────────────────────────
        public async Task<int> CallStoreIssueDtlSP(StoreIssueDtl d)
        {
            var id = SiOutId();
            var p = new[]
            {
                id,
                SiP("@MTransNo",        SqlDbType.Int,      d.MTransNo),
                SiP("@TransSlNo",       SqlDbType.Int,      d.TransSlNo),
                SiP("@SlNo",            SqlDbType.Int,      d.SlNo.HasValue ? (int)d.SlNo.Value : null),
                SiP("@LinkNo",          SqlDbType.Int,      d.LinkNo),
                SiP("@PartNo",          SqlDbType.Int,      d.PartNo),
                SiP("@OAYear",          SqlDbType.Int,      d.OAYear),
                SiP("@OANo",            SqlDbType.Int,      d.OANo),
                SiP("@OAID",            SqlDbType.Int,      d.OAID),
                SiP("@PartCode",        SqlDbType.VarChar,  d.PartCode, 30),
                SiP("@PartDescription", SqlDbType.VarChar,  d.PartDescription, 100),
                SiP("@GradeNo",         SqlDbType.Int,      d.GradeNo),
                SiP("@Grade",           SqlDbType.VarChar,  d.Grade, 50),
                SiP("@ItemRemarks",     SqlDbType.VarChar,  d.ItemRemarks, 500),
                SiP("@DCOpration",      SqlDbType.VarChar,  d.DCOpration, 150),
                SiDec("@MRNQty", d.MRNQty, 12, 4),
                SiDec("@Qty",    d.Qty,    12, 4),
                SiP("@Unit",            SqlDbType.VarChar,  d.Unit, 20),
                SiP("@CreatedBy",       SqlDbType.Int,      d.CreatedBy),
                SiP("@CreatedOn",       SqlDbType.DateTime, d.CreatedOn),
                SiP("@SubscID",         SqlDbType.Int,      d.SubscID),
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_StoreIssueDtl @ID = @ID OUTPUT, @MTransNo = @MTransNo, @TransSlNo = @TransSlNo,
                    @SlNo = @SlNo, @LinkNo = @LinkNo, @PartNo = @PartNo, @OAYear = @OAYear, @OANo = @OANo,
                    @OAID = @OAID, @PartCode = @PartCode, @PartDescription = @PartDescription,
                    @GradeNo = @GradeNo, @Grade = @Grade, @ItemRemarks = @ItemRemarks, @DCOpration = @DCOpration,
                    @MRNQty = @MRNQty, @Qty = @Qty, @Unit = @Unit, @CreatedBy = @CreatedBy,
                    @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);
            return SiIdOf(id);
        }

        // ── SP_StoreReturnsMst (allocates DocNo itself on insert) ────────
        public async Task<int> CallStoreReturnsMstSP(StoreReturnsMst h)
        {
            var id = SiOutId();
            var p = new[]
            {
                id,
                SiP("@MTransNo",      SqlDbType.Int,      h.MTransNo),
                SiP("@DocSeries",     SqlDbType.Char,     h.DocSeries.Trim(), 10),
                SiP("@DocYear",       SqlDbType.Int,      h.DocYear),
                SiP("@DocNo",         SqlDbType.Int,      h.DocNo),
                SiP("@DocDate",       SqlDbType.Date,     h.DocDate.Date),
                SiP("@TargetTransNo", SqlDbType.Int,      h.TargetTransNo),
                SiP("@TargetDocYear", SqlDbType.Int,      h.TargetDocYear),
                SiP("@TargetDocNo",   SqlDbType.Int,      h.TargetDocNo),
                SiP("@TargetAmdNo",   SqlDbType.Int,      h.TargetAmdNo ?? 0),
                SiP("@DocNotes",      SqlDbType.VarChar,  h.DocNotes, 250),
                SiP("@DocStatus",     SqlDbType.Char,     h.DocStatus, 1),
                SiP("@SystemIP",      SqlDbType.VarChar,  h.SystemIP, 20),
                SiP("@LockStatus",    SqlDbType.Char,     h.LockStatus, 1),
                SiP("@CreatedBy",     SqlDbType.Int,      h.CreatedBy),
                SiP("@CreatedOn",     SqlDbType.DateTime, h.CreatedOn),
                SiP("@SubscID",       SqlDbType.Int,      h.SubscID),
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_StoreReturnsMst @ID = @ID OUTPUT, @MTransNo = @MTransNo, @DocSeries = @DocSeries,
                    @DocYear = @DocYear, @DocNo = @DocNo, @DocDate = @DocDate, @TargetTransNo = @TargetTransNo,
                    @TargetDocYear = @TargetDocYear, @TargetDocNo = @TargetDocNo, @TargetAmdNo = @TargetAmdNo,
                    @DocNotes = @DocNotes, @DocStatus = @DocStatus, @SystemIP = @SystemIP,
                    @LockStatus = @LockStatus, @CreatedBy = @CreatedBy, @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);
            return SiIdOf(id);
        }

        // ── SP_StoreReturnsDtl ───────────────────────────────────────────
        public async Task<int> CallStoreReturnsDtlSP(StoreReturnsDtl d)
        {
            var id = SiOutId();
            var p = new[]
            {
                id,
                SiP("@MTransNo",        SqlDbType.Int,      d.MTransNo),
                SiP("@TransSlNo",       SqlDbType.Int,      d.TransSlNo),
                SiP("@SlNo",            SqlDbType.Int,      d.SlNo.HasValue ? (int)d.SlNo.Value : null),
                SiP("@LinkNo",          SqlDbType.Int,      d.LinkNo),
                SiP("@PartNo",          SqlDbType.Int,      d.PartNo),
                SiP("@OAYear",          SqlDbType.Int,      d.OAYear),
                SiP("@OANo",            SqlDbType.Int,      d.OANo),
                SiP("@OAID",            SqlDbType.Int,      d.OAID),
                SiP("@PartCode",        SqlDbType.VarChar,  d.PartCode, 30),
                SiP("@PartDescription", SqlDbType.VarChar,  d.PartDescription, 100),
                SiP("@GradeNo",         SqlDbType.Int,      d.GradeNo),
                SiP("@Grade",           SqlDbType.VarChar,  d.Grade, 50),
                SiP("@ItemRemarks",     SqlDbType.VarChar,  d.ItemRemarks, 500),
                SiDec("@IssueQty", d.IssueQty, 12, 4),
                SiDec("@RTNQty",   d.RTNQty,   12, 4),
                SiP("@Unit",            SqlDbType.VarChar,  d.Unit, 20),
                SiP("@CreatedBy",       SqlDbType.Int,      d.CreatedBy),
                SiP("@CreatedOn",       SqlDbType.DateTime, d.CreatedOn),
                SiP("@SubscID",         SqlDbType.Int,      d.SubscID),
            };
            await Database.ExecuteSqlRawAsync("""
                EXEC dbo.SP_StoreReturnsDtl @ID = @ID OUTPUT, @MTransNo = @MTransNo, @TransSlNo = @TransSlNo,
                    @SlNo = @SlNo, @LinkNo = @LinkNo, @PartNo = @PartNo, @OAYear = @OAYear, @OANo = @OANo,
                    @OAID = @OAID, @PartCode = @PartCode, @PartDescription = @PartDescription,
                    @GradeNo = @GradeNo, @Grade = @Grade, @ItemRemarks = @ItemRemarks,
                    @IssueQty = @IssueQty, @RTNQty = @RTNQty, @Unit = @Unit, @CreatedBy = @CreatedBy,
                    @CreatedOn = @CreatedOn, @SubscID = @SubscID
                """, p);
            return SiIdOf(id);
        }

        /// <summary>
        /// Serialises every write in the issue / return chain for the life of
        /// the current transaction, so two stores clerks cannot both pass the
        /// balance or stock check on the same quantity.
        /// </summary>
        public Task LockStoreChainAsync() =>
            Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = N'EMS_StoreIssueChain', @LockMode = N'Exclusive', " +
                "@LockOwner = N'Transaction', @LockTimeout = 15000");

        /// <summary>dbo.FN_OALinkCheck — null when the OA link is acceptable.</summary>
        public async Task<string?> CheckOALinkAsync(int? oaYear, int? oaNo, int? oaID, int subscID)
        {
            var conn = Database.GetDbConnection();
            var wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed) await conn.OpenAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
                cmd.CommandText = "SELECT dbo.FN_OALinkCheck(@Y, @N, @L, @S)";
                cmd.Parameters.Add(SiP("@Y", SqlDbType.Int, oaYear));
                cmd.Parameters.Add(SiP("@N", SqlDbType.Int, oaNo));
                cmd.Parameters.Add(SiP("@L", SqlDbType.Int, oaID));
                cmd.Parameters.Add(SiP("@S", SqlDbType.Int, subscID));
                var r = await cmd.ExecuteScalarAsync();
                return r == null || r == DBNull.Value ? null : r.ToString();
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
    }
}
