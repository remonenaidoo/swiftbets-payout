using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application.Handlers;

namespace SwiftBets.Payout.Application.Tests;

public sealed class RedriveDeadLettersTests
{
    private static PayoutAttemptV1 Parked() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, CouponOutcome.Won, new Money(2_500, "ZAR"), default, 7, "wallet unavailable", DateTimeOffset.UtcNow);

    [Fact]
    public async Task Every_parked_payout_goes_back_to_the_first_rung_with_a_clean_slate()
    {
        var store = new FakeStore();
        store.Parked.Add((Parked(), "wallet unavailable"));
        store.Parked.Add((Parked(), "wallet unavailable"));
        var ladder = new CapturingLadder();

        var redriven = await new RedriveDeadLettersHandler(store, ladder).HandleAsync(100, CancellationToken.None);

        redriven.ShouldBe(2);
        ladder.Scheduled.ShouldAllBe(s => s.Rung == 0 && s.Attempt.Attempt == 0 && s.Attempt.LastError == null);
        store.Parked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_redrive_sends_nothing_again()
    {
        var store = new FakeStore();
        store.Parked.Add((Parked(), "wallet unavailable"));
        var ladder = new CapturingLadder();
        var handler = new RedriveDeadLettersHandler(store, ladder);

        await handler.HandleAsync(100, CancellationToken.None);
        (await handler.HandleAsync(100, CancellationToken.None)).ShouldBe(0);

        ladder.Scheduled.Count.ShouldBe(1);
    }
}
