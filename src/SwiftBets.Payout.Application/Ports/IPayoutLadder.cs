using SwiftBets.Contracts.Payout;

namespace SwiftBets.Payout.Application.Ports;

public interface IPayoutLadder
{
    /// <summary>Publishes the attempt to the rung's topic, due after the rung's delay; no worker sleeps on it.</summary>
    Task ScheduleAsync(PayoutAttemptV1 attempt, int rung, TimeSpan delay);
}
