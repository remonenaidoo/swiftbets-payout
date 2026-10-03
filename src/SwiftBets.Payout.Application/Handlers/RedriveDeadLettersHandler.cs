using SwiftBets.Payout.Application.Ports;

namespace SwiftBets.Payout.Application.Handlers;

/// <summary>
/// Puts every parked payout back on the first rung of the retry ladder, after the cause (a wallet outage) is fixed.
/// Each one is taken before it is scheduled, so two re-drives never send the same payout twice; payouts are idempotent anyway.
/// </summary>
public sealed class RedriveDeadLettersHandler(IPayoutStore store, IPayoutLadder ladder)
{
    public async Task<int> HandleAsync(int limit, CancellationToken cancellationToken)
    {
        var redriven = 0;
        foreach (var (parked, _, _) in await store.ListDeadLettersAsync(Math.Clamp(limit, 1, 500), cancellationToken))
        {
            if (await store.TakeDeadLetterAsync(parked.CouponId, parked.SettlementVersion) is { } attempt)
            {
                await ladder.ScheduleAsync(attempt with { Attempt = 0, LastError = null }, rung: 0, TimeSpan.Zero);
                redriven++;
            }
        }

        return redriven;
    }
}
