namespace SwiftBets.Payout.Domain;

/// <summary>
/// What one settlement version owes: only the difference between its target and what was already paid moves, so a
/// correction from win to loss claws back exactly the earlier payout and a duplicate settlement moves nothing.
/// </summary>
public sealed record PayoutPlan(long Delta, PostingType Type, string IdempotencyKey)
{
    public static PayoutPlan For(Guid couponId, int settlementVersion, SettlementKind kind, long targetPayout, long paidToDate)
    {
        var delta = targetPayout - paidToDate;
        var type = delta switch
        {
            0 => PostingType.None,
            < 0 => PostingType.ResettleDebit,
            _ when kind == SettlementKind.CashedOut => PostingType.Cashout,
            _ when settlementVersion > 1 => PostingType.ResettleCredit,
            _ when kind == SettlementKind.Void => PostingType.VoidRefund,
            _ => PostingType.Win,
        };
        return new PayoutPlan(delta, type, Key(couponId, type, settlementVersion));
    }

    /// <summary>{coupon}_{bet}_{type}_{version}; a coupon carries one bet in this scope.</summary>
    public static string Key(Guid couponId, PostingType type, int version) =>
        $"{couponId:N}_bet1_{type switch
        {
            PostingType.Win => "WIN",
            PostingType.VoidRefund => "VOID_REFUND",
            PostingType.ResettleCredit => "RESETTLE_CREDIT",
            PostingType.ResettleDebit => "RESETTLE_DEBIT",
            PostingType.Cashout => "CASHOUT",
            _ => "NONE",
        }}_{version}";
}
