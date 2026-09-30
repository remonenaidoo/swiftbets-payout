using SwiftBets.Contracts.Payout;

namespace SwiftBets.Payout.Application.Ports;

public interface IPayoutStore
{
    /// <summary>
    /// Takes a short lease on the coupon's payout row so only one worker moves its money at a time; null while
    /// another holder's lease is live.
    /// </summary>
    Task<PayoutState?> TryLeaseAsync(Guid couponId, Guid punterId, string holder, TimeSpan lease);

    Task ReleaseLeaseAsync(Guid couponId, string holder);

    /// <summary>
    /// Records the payment and the completed event (outbox) in one transaction, only if this version is newer than
    /// the last recorded one. Returns false for a stale or repeated version.
    /// </summary>
    Task<bool> RecordAsync(Guid couponId, int version, long targetPayout, long delta, string idempotencyKey, PayoutCompletedV1 completed);

    Task ParkAsync(PayoutAttemptV1 attempt, string reason);

    Task<IReadOnlyList<(PayoutAttemptV1 Attempt, string Reason, DateTimeOffset ParkedAt)>> ListDeadLettersAsync(int limit, CancellationToken cancellationToken);

    Task<PayoutAttemptV1?> TakeDeadLetterAsync(Guid couponId, int version);

    Task<CouponPayoutView?> GetCouponPayoutAsync(Guid couponId, CancellationToken cancellationToken);
}
