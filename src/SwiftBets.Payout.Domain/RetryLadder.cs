namespace SwiftBets.Payout.Domain;

/// <summary>Retry rungs by attempt number; past the last rung a payout is dead-lettered for an operator.</summary>
public sealed class RetryLadder(IReadOnlyList<TimeSpan> rungs)
{
    public static RetryLadder Standard { get; } = new([TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15)]);

    public int Rungs => rungs.Count;

    /// <returns>The rung index and delay for the next attempt, or null when the ladder is exhausted.</returns>
    public (int Rung, TimeSpan Delay)? Next(int failedAttempts) =>
        failedAttempts < 1 || failedAttempts > rungs.Count ? null : (failedAttempts - 1, rungs[failedAttempts - 1]);
}
