namespace SwiftBets.Payout.Application.Ports;

public sealed record PaidTotal(Guid CouponId, long PaidToDate, int LastVersion);
