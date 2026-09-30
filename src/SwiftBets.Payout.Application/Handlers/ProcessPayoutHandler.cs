using Microsoft.Extensions.Logging;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application.Ports;
using SwiftBets.Payout.Domain;

namespace SwiftBets.Payout.Application.Handlers;

/// <summary>
/// Runs a payout from the named step it is at. ComputeDelta derives what this settlement version still owes;
/// CreditWallet moves it under a versioned idempotency key; RecordPayment stores it with its completed event. A
/// wallet outage schedules the attempt on the next ladder rung at the step that failed; a refusal (blacklisted
/// wallet) or an exhausted ladder parks it on the dead-letter for an operator.
/// </summary>
public sealed partial class ProcessPayoutHandler(
    IPayoutStore store,
    IWalletPayments wallet,
    IPayoutLadder ladder,
    RetryLadder rungs,
    IFaultPoint faults,
    TimeProvider time,
    ILogger<ProcessPayoutHandler> logger)
{
    public const string FaultAfterCredit = "payout.after-credit";
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    public static PayoutAttemptV1 FirstAttempt(CouponSettledV1 settled, DateTimeOffset now) =>
        new(settled.CouponId, settled.PunterId, settled.SettlementVersion, settled.Outcome, settled.TargetPayout, PayoutStep.ComputeDelta, 0, null, now);

    public async Task<PayoutOutcome> ExecuteAsync(PayoutAttemptV1 attempt)
    {
        var kind = attempt.Outcome switch
        {
            CouponOutcome.Won => SettlementKind.Won,
            CouponOutcome.Void => SettlementKind.Void,
            _ => SettlementKind.Lost,
        };
        var holder = Guid.NewGuid().ToString("N");
        var state = await store.TryLeaseAsync(attempt.CouponId, attempt.PunterId, holder, Lease);
        if (state is null)
        {
            return await RetryAsync(attempt, "coupon payout in progress elsewhere");
        }

        try
        {
            if (attempt.SettlementVersion <= state.LastVersion)
            {
                return PayoutOutcome.AlreadyApplied;
            }

            var plan = PayoutPlan.For(attempt.CouponId, attempt.SettlementVersion, kind, attempt.TargetPayout.MinorUnits, state.PaidToDate);
            if (plan.Type != PostingType.None)
            {
                var reference = $"coupon {attempt.CouponId} settlement v{attempt.SettlementVersion}";
                var (status, failure) = plan.Delta > 0
                    ? await wallet.CreditAsync(plan.IdempotencyKey, attempt.PunterId, plan.Delta, attempt.TargetPayout.Currency, reference)
                    : await wallet.DebitAsync(plan.IdempotencyKey, attempt.PunterId, -plan.Delta, attempt.TargetPayout.Currency, reference);
                if (status == WalletPaymentStatus.Unavailable)
                {
                    return await RetryAsync(attempt with { Step = PayoutStep.CreditWallet }, "wallet unavailable");
                }

                if (status == WalletPaymentStatus.Refused)
                {
                    await store.ParkAsync(attempt with { Step = PayoutStep.CreditWallet, LastError = failure }, failure ?? "wallet_refused");
                    LogDeadLettered(attempt.CouponId, attempt.SettlementVersion, failure ?? "wallet_refused");
                    return PayoutOutcome.DeadLettered;
                }
            }

            await faults.HitAsync(FaultAfterCredit);
            var currency = attempt.TargetPayout.Currency;
            var recorded = await store.RecordAsync(attempt.CouponId, attempt.SettlementVersion, attempt.TargetPayout.MinorUnits, plan.Delta, plan.IdempotencyKey,
                new PayoutCompletedV1(attempt.CouponId, attempt.PunterId, attempt.SettlementVersion, new Money(plan.Delta, currency), new Money(state.PaidToDate + plan.Delta, currency), time.GetUtcNow()));
            return !recorded ? PayoutOutcome.AlreadyApplied : plan.Type == PostingType.None ? PayoutOutcome.NothingToPay : PayoutOutcome.Paid;
        }
        finally
        {
            await store.ReleaseLeaseAsync(attempt.CouponId, holder);
        }
    }

    private async Task<PayoutOutcome> RetryAsync(PayoutAttemptV1 attempt, string reason)
    {
        var next = attempt with { Attempt = attempt.Attempt + 1, LastError = reason };
        if (rungs.Next(next.Attempt) is not { } rung)
        {
            await store.ParkAsync(next, $"retry ladder exhausted: {reason}");
            LogDeadLettered(attempt.CouponId, attempt.SettlementVersion, reason);
            return PayoutOutcome.DeadLettered;
        }

        await ladder.ScheduleAsync(next, rung.Rung, rung.Delay);
        LogScheduled(attempt.CouponId, attempt.SettlementVersion, next.Step, rung.Delay, reason);
        return PayoutOutcome.Scheduled;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payout {CouponId} v{Version} scheduled at step {Step} in {Delay}: {Reason}")]
    private partial void LogScheduled(Guid couponId, int version, PayoutStep step, TimeSpan delay, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Payout {CouponId} v{Version} dead-lettered: {Reason}")]
    private partial void LogDeadLettered(Guid couponId, int version, string reason);
}
