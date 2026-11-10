using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EMSAPI.Models
{
    /// <summary>
    /// Production Planning fields on the existing pages (SQL/58): the wrappers
    /// of the small "companion" procedures each controller calls right after
    /// the page's existing save procedure, inside the same transaction.
    /// Every wrapper returns (ID, Message): ID 0 = refused, Message says why.
    /// A NULL argument means "keep the stored value" unless noted.
    /// </summary>
    public partial class EMSContext
    {
        private static object PfValue(object? value) => value ?? DBNull.Value;

        private static SqlParameter PfDec(string name, decimal? value, byte precision, byte scale) =>
            new(name, SqlDbType.Decimal) { Precision = precision, Scale = scale, Value = PfValue(value) };

        private static SqlParameter PfInt(string name, int? value) =>
            new(name, SqlDbType.Int) { Value = PfValue(value) };

        private static SqlParameter PfStr(string name, string? value, int size) =>
            new(name, SqlDbType.VarChar, size) { Value = PfValue(value) };

        private static SqlParameter PfDate(string name, DateTime? value) =>
            new(name, SqlDbType.Date) { Value = value.HasValue ? value.Value.Date : DBNull.Value };

        /// <summary>EXEC dbo.&lt;sp&gt; @ID OUTPUT, &lt;named args&gt;, @Message OUTPUT.</summary>
        private async Task<(int ID, string Message)> CallPlanFieldsSP(string sp, params SqlParameter[] args)
        {
            var id  = new SqlParameter("@ID", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var msg = new SqlParameter("@Message", SqlDbType.NVarChar, 200) { Direction = ParameterDirection.Output };
            var all = new List<SqlParameter> { id, msg };
            all.AddRange(args);

            var named = string.Join(", ", args.Select(a => $"{a.ParameterName} = {a.ParameterName}"));
            await Database.ExecuteSqlRawAsync(
                $"EXEC dbo.{sp} @ID = @ID OUTPUT, {named}, @Message = @Message OUTPUT", all);

            return (id.Value == DBNull.Value ? 0 : Convert.ToInt32(id.Value), msg.Value?.ToString() ?? string.Empty);
        }

        // ── SP_MachinePlanFields ─────────────────────────────────────────
        public Task<(int ID, string Message)> CallMachinePlanFieldsSP(
            int mTransNo, string? capacityUnit, int? shiftsPerDay, decimal? efficiencyPct,
            decimal? minPieceWt, decimal? maxPieceWt, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_MachinePlanFields",
                PfInt("@MTransNo", mTransNo),
                PfStr("@CapacityUnit", capacityUnit, 20),
                PfInt("@ShiftsPerDay", shiftsPerDay),
                PfDec("@EfficiencyPct", efficiencyPct, 5, 2),
                PfDec("@MinPieceWt", minPieceWt, 10, 3),
                PfDec("@MaxPieceWt", maxPieceWt, 10, 3),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_RoutingChartPlanFields — stepsJson [{ setupMin }] in step order ──
        public Task<(int ID, string Message)> CallRoutingChartPlanFieldsSP(
            int mTransNo, int? postForgeDays, string? stepsJson, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_RoutingChartPlanFields",
                PfInt("@MTransNo", mTransNo),
                PfInt("@PostForgeDays", postForgeDays),
                new SqlParameter("@Steps", SqlDbType.NVarChar, -1) { Value = PfValue(stepsJson) },
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_ConvBOMPlanFields ─────────────────────────────────────────
        public Task<(int ID, string Message)> CallConvBOMPlanFieldsSP(
            int mTransNo, decimal? barDia, decimal? cutLength, decimal? yieldPct, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_ConvBOMPlanFields",
                PfInt("@MTransNo", mTransNo),
                PfDec("@BarDia", barDia, 8, 2),
                PfDec("@CutLength", cutLength, 8, 2),
                PfDec("@YieldPct", yieldPct, 5, 2),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_DiePlanFields ─────────────────────────────────────────────
        public Task<(int ID, string Message)> CallDiePlanFieldsSP(
            int mTransNo, string? dieStatus, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_DiePlanFields",
                PfInt("@MTransNo", mTransNo),
                PfStr("@DieStatus", dieStatus, 20),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_OADtlPlanFields — committedDate NULL = use the delivery date ──
        public Task<(int ID, string Message)> CallOADtlPlanFieldsSP(
            int transSlNo, DateTime? committedDate, string? priority, string? customerMaterial,
            int userNo, int subscID) =>
            CallPlanFieldsSP("SP_OADtlPlanFields",
                PfInt("@TransSlNo", transSlNo),
                PfDate("@CommittedDate", committedDate),
                PfStr("@Priority", priority, 10),
                new SqlParameter("@CustomerMaterial", SqlDbType.Char, 1) { Value = PfValue(customerMaterial) },
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_JobCardPlanFields — plan link, hammer, die, planned dates ──
        public Task<(int ID, string Message)> CallJobCardPlanFieldsSP(
            int mTransNo, int planID, int planJobSeq, int planLineSlNo,
            DateTime? plannedStart, DateTime? plannedEnd, int machineNo, int dieNo, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_JobCardPlanFields",
                PfInt("@MTransNo", mTransNo),
                PfInt("@PlanID", planID),
                PfInt("@PlanJobSeq", planJobSeq),
                PfInt("@PlanLineSlNo", planLineSlNo),
                PfDate("@PlannedStart", plannedStart),
                PfDate("@PlannedEnd", plannedEnd),
                PfInt("@MachineNo", machineNo),
                PfInt("@DieNo", dieNo),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_JobCardEntryPlanFields — same JSON order as SP_JobCardEntrySave ──
        public Task<(int ID, string Message)> CallJobCardEntryPlanFieldsSP(
            int mTransNo, string entriesJson, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_JobCardEntryPlanFields",
                PfInt("@MTransNo", mTransNo),
                new SqlParameter("@Entries", SqlDbType.NVarChar, -1) { Value = entriesJson },
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_JobCardInspectionPlanFields — rejection reason ────────────
        public Task<(int ID, string Message)> CallJobCardInspectionPlanFieldsSP(
            int mTransNo, string? rejectionReason, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_JobCardInspectionPlanFields",
                PfInt("@MTransNo", mTransNo),
                PfStr("@RejectionReason", rejectionReason, 100),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_MaterialGradePlanFields ───────────────────────────────────
        public Task<(int ID, string Message)> CallMaterialGradePlanFieldsSP(
            int mTransNo, decimal? safetyStock, int? leadTimeDays, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_MaterialGradePlanFields",
                PfInt("@MTransNo", mTransNo),
                PfDec("@SafetyStock", safetyStock, 14, 2),
                PfInt("@LeadTimeDays", leadTimeDays),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_PurReqDtlPlanFields — requiredBy NULL = header due date ───
        public Task<(int ID, string Message)> CallPurReqDtlPlanFieldsSP(
            int transSlNo, DateTime? requiredBy, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_PurReqDtlPlanFields",
                PfInt("@TransSlNo", transSlNo),
                PfDate("@RequiredBy", requiredBy),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_StoreReturnsDtlPlanFields ─────────────────────────────────
        public Task<(int ID, string Message)> CallStoreReturnsDtlPlanFieldsSP(
            int transSlNo, string? returnType, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_StoreReturnsDtlPlanFields",
                PfInt("@TransSlNo", transSlNo),
                PfStr("@ReturnType", returnType, 20),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));

        // ── SP_StoreIssueDtlPlanFields — heat no ('' = none; else approved) ──
        /// <param name="materialSource">'O' our bar, 'C' customer bar (SQL 67); null keeps the line's.</param>
        public Task<(int ID, string Message)> CallStoreIssueDtlPlanFieldsSP(
            int transSlNo, string? heatNo, int userNo, int subscID, string? materialSource = null) =>
            CallPlanFieldsSP("SP_StoreIssueDtlPlanFields",
                PfInt("@TransSlNo", transSlNo),
                PfStr("@HeatNo", heatNo, 50),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID),
                PfStr("@MaterialSource", materialSource, 1));
    }
}
