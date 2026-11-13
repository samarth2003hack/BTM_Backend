using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EMSAPI.Models
{
    // Opening Stock (SQL 72): the stock held on the go-live day.
    // OpeningStockController reads the tables with raw SQL and writes them
    // through SP_OpeningStockSave / SP_OpeningStockStatus.

    /// <summary>POST /api/OpeningStock/Mst/SaveFull — header + lines in one call.</summary>
    public class OpeningStockSaveRequest
    {
        public OpeningStockHeader? Header { get; set; }
        public List<OpeningStockLine> Lines { get; set; } = new();
    }

    public class OpeningStockHeader
    {
        public int MTransNo { get; set; }
        public string? DocSeries { get; set; }
        public int DocYear { get; set; }
        public int DocNo { get; set; }
        public DateTime? AsOnDate { get; set; }
        /// <summary>OWN | CUSTOMER</summary>
        public string? StockType { get; set; }
        public int CustomerNo { get; set; }
        public int OAYear { get; set; }
        public int OANo { get; set; }
        public string? Remarks { get; set; }
        /// <summary>N draft | S submitted (approval is its own call)</summary>
        public string? LockStatus { get; set; }
        public int CreatedBy { get; set; }
        public int ModifiedBy { get; set; }
        public int SubscID { get; set; }
        public string? Status { get; set; }
    }

    public class OpeningStockLine
    {
        public int TransSlNo { get; set; }
        public int SlNo { get; set; }
        public int PartNo { get; set; }
        public int GradeNo { get; set; }
        public string? Grade { get; set; }
        public decimal Qty { get; set; }
        public decimal Rate { get; set; }
        public int BinNo { get; set; }
        public string? HeatNo { get; set; }
        public string? TcNo { get; set; }
        public string? Remarks { get; set; }
    }

    /// <summary>SP wrappers (SQL 72). ID 0 = refused, Message says why.</summary>
    public partial class EMSContext
    {
        private static readonly JsonSerializerOptions OsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public async Task<(int ID, int DocNo, string Message)> CallOpeningStockSaveSP(
            OpeningStockHeader h, IEnumerable<OpeningStockLine> lines, int userNo, string? systemIP)
        {
            var id = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var no = new SqlParameter("@DocNo", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var json = JsonSerializer.Serialize(lines.Select(l => new
            {
                l.PartNo, l.GradeNo, l.Grade, l.Qty, l.Rate, l.BinNo, l.HeatNo, l.TcNo, l.Remarks
            }), OsJson);
            var args = new[]
            {
                id, msg, no,
                PfInt("@MTransNo", h.MTransNo),
                PfStr("@DocSeries", h.DocSeries?.Trim(), 10),
                PfInt("@DocYear", h.DocYear),
                PfDate("@AsOnDate", h.AsOnDate),
                PfStr("@StockType", h.StockType?.Trim().ToUpperInvariant(), 10),
                PfInt("@CustomerNo", h.CustomerNo),
                PfInt("@OAYear", h.OAYear),
                PfInt("@OANo", h.OANo),
                PfStr("@Remarks", h.Remarks?.Trim(), 250),
                new SqlParameter("@LockStatus", SqlDbType.Char, 1) { Value = PfValue(h.LockStatus?.Trim().ToUpperInvariant()) },
                new SqlParameter("@LinesJson", SqlDbType.NVarChar, -1) { Value = json },
                PfInt("@UserNo", userNo),
                PfStr("@SystemIP", systemIP, 20),
                PfInt("@SubscID", h.SubscID),
            };
            await Database.ExecuteSqlRawAsync(
                "EXEC dbo.SP_OpeningStockSave @ID = @ID OUTPUT, @MTransNo = @MTransNo, @DocSeries = @DocSeries, " +
                "@DocYear = @DocYear, @AsOnDate = @AsOnDate, @StockType = @StockType, @CustomerNo = @CustomerNo, " +
                "@OAYear = @OAYear, @OANo = @OANo, @Remarks = @Remarks, @LockStatus = @LockStatus, " +
                "@LinesJson = @LinesJson, @UserNo = @UserNo, @SystemIP = @SystemIP, @SubscID = @SubscID, " +
                "@Message = @Message OUTPUT, @DocNo = @DocNo OUTPUT", args);
            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value),
                    no.Value == DBNull.Value ? 0 : Convert.ToInt32(no.Value),
                    msg.Value?.ToString() ?? string.Empty);
        }

        /// <summary>action: Approve | Unapprove | Delete</summary>
        public Task<(int ID, string Message)> CallOpeningStockStatusSP(
            int mTransNo, string action, string? reason, int userNo, string? systemIP, int subscID) =>
            CallPlanFieldsSP("SP_OpeningStockStatus",
                PfInt("@MTransNo", mTransNo),
                PfStr("@Action", action, 20),
                PfStr("@Reason", reason?.Trim(), 250),
                PfInt("@UserNo", userNo),
                PfStr("@SystemIP", systemIP, 20),
                PfInt("@SubscID", subscID));
    }
}
