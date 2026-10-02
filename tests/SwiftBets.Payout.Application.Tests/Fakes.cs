using System.Collections.Concurrent;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.Contracts.Payout;
using SwiftBets.Payout.Application.Ports;

namespace SwiftBets.Payout.Application.Tests;

internal sealed class FakeWallet : IWalletPayments
{
    public ConcurrentDictionary<string, long> Applied { get; } = new(StringComparer.Ordinal);

    public int Calls;

    public bool IsDown { get; set; }

    public string? RefuseWith { get; set; }

    public Task<(WalletPaymentStatus Status, string? FailureCode)> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) => Apply(idempotencyKey, amount);

    public Task<(WalletPaymentStatus Status, string? FailureCode)> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) => Apply(idempotencyKey, -amount);

    private Task<(WalletPaymentStatus, string?)> Apply(string key, long amount)
    {
        Interlocked.Increment(ref Calls);
        if (IsDown)
        {
            return Task.FromResult<(WalletPaymentStatus, string?)>((WalletPaymentStatus.Unavailable, null));
        }

        if (RefuseWith is { } code)
        {
            return Task.FromResult<(WalletPaymentStatus, string?)>((WalletPaymentStatus.Refused, code));
        }

        Applied.TryAdd(key, amount);
        return Task.FromResult<(WalletPaymentStatus, string?)>((WalletPaymentStatus.Succeeded, null));
    }
}

internal sealed class FakeStore : IPayoutStore
{
    private readonly Dictionary<Guid, (PayoutState State, string? Holder)> _rows = [];

    public List<(PayoutAttemptV1 Attempt, string Reason)> Parked { get; } = [];

    public PayoutState StateOf(Guid couponId) => _rows[couponId].State;

    public Task<PayoutState?> TryLeaseAsync(Guid couponId, Guid punterId, string holder, TimeSpan lease)
    {
        var row = _rows.GetValueOrDefault(couponId, (new PayoutState(0, 0), null));
        if (row.Holder is not null)
        {
            return Task.FromResult<PayoutState?>(null);
        }

        _rows[couponId] = (row.State, holder);
        return Task.FromResult<PayoutState?>(row.State);
    }

    public Task ReleaseLeaseAsync(Guid couponId, string holder)
    {
        if (_rows[couponId].Holder == holder)
        {
            _rows[couponId] = (_rows[couponId].State, null);
        }

        return Task.CompletedTask;
    }

    public Task<bool> RecordAsync(Guid couponId, int version, long targetPayout, long delta, string idempotencyKey, PayoutCompletedV1 completed)
    {
        var (state, holder) = _rows[couponId];
        if (version <= state.LastVersion)
        {
            return Task.FromResult(false);
        }

        _rows[couponId] = (new PayoutState(state.PaidToDate + delta, version), holder);
        return Task.FromResult(true);
    }

    public Task ParkAsync(PayoutAttemptV1 attempt, string reason)
    {
        Parked.Add((attempt, reason));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(PayoutAttemptV1 Attempt, string Reason, DateTimeOffset ParkedAt)>> ListDeadLettersAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(PayoutAttemptV1, string, DateTimeOffset)>>([]);

    public Task<PayoutAttemptV1?> TakeDeadLetterAsync(Guid couponId, int version) => Task.FromResult<PayoutAttemptV1?>(null);

    public Task<CouponPayoutView?> GetCouponPayoutAsync(Guid couponId, CancellationToken cancellationToken) => Task.FromResult<CouponPayoutView?>(null);

    public Task<IReadOnlyList<PaidTotal>> GetPaidTotalsAsync(IReadOnlyCollection<Guid> couponIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PaidTotal>>([]);
}

internal sealed class CapturingLadder : IPayoutLadder
{
    public List<(PayoutAttemptV1 Attempt, int Rung)> Scheduled { get; } = [];

    public Task ScheduleAsync(PayoutAttemptV1 attempt, int rung, TimeSpan delay)
    {
        Scheduled.Add((attempt, rung));
        return Task.CompletedTask;
    }
}

internal sealed class CrashOnce(string name) : IFaultPoint
{
    private bool _crashed;

    public ValueTask HitAsync(string point, CancellationToken cancellationToken = default)
    {
        if (point == name && !_crashed)
        {
            _crashed = true;
            throw new FaultInjectedException(point);
        }

        return ValueTask.CompletedTask;
    }
}
