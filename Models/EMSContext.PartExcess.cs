namespace EMSAPI.Models
{
    /// <summary>
    /// Extra allowed % on GRN (SQL/70): the wrapper of SP_PartMasterExcess,
    /// called by the Part Master save right after SP_PartMaster, inside the
    /// same transaction. Returns (ID, Message): ID 0 = refused.
    /// A NULL percentage keeps the stored value.
    /// </summary>
    public partial class EMSContext
    {
        public Task<(int ID, string Message)> CallPartMasterExcessSP(
            int mTransNo, decimal? excessPct, int userNo, int subscID) =>
            CallPlanFieldsSP("SP_PartMasterExcess",
                PfInt("@MTransNo", mTransNo),
                PfDec("@ExcessPct", excessPct, 5, 2),
                PfInt("@UserNo", userNo),
                PfInt("@SubscID", subscID));
    }
}
