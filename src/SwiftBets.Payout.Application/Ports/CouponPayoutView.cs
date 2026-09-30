namespace SwiftBets.Payout.Application.Ports;

public sealed record CouponPayoutView(Guid CouponId, long PaidToDate, int LastVersion, bool IsLeased, IReadOnlyList<PaymentView> Payments, IReadOnlyList<string> DeadLetterReasons);
