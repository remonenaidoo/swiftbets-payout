using Microsoft.Extensions.Logging.Abstractions;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application.Handlers;
using SwiftBets.Payout.Domain;

namespace SwiftBets.Payout.Application.Tests;

public sealed class ProcessPayoutHandlerTests
{
    [Fact]
    public async Task Wallet_outage_resumes_at_credit_wallet_and_pays_exactly_once()
    {
        var (handler, wallet, store, ladder) = Build();
        var attempt = Attempt(3_000);
        wallet.IsDown = true;

        (await handler.ExecuteAsync(attempt)).ShouldBe(PayoutOutcome.Scheduled);
        var retry = ladder.Scheduled.Single().Attempt;
        wallet.IsDown = false;
        (await handler.ExecuteAsync(retry)).ShouldBe(PayoutOutcome.Paid);
        (await handler.ExecuteAsync(retry)).ShouldBe(PayoutOutcome.AlreadyApplied);

        retry.Step.ShouldBe(PayoutStep.CreditWallet);
        wallet.Applied.Values.ShouldBe([3_000L]);
        store.StateOf(attempt.CouponId).PaidToDate.ShouldBe(3_000);
    }

    [Fact]
    public async Task Crash_after_the_credit_is_recorded_on_reprocessing_without_paying_twice()
    {
        var (handler, wallet, store, _) = Build(new CrashOnce(ProcessPayoutHandler.FaultAfterCredit));
        var attempt = Attempt(3_000);

        await Should.ThrowAsync<FaultInjectedException>(() => handler.ExecuteAsync(attempt));
        (await handler.ExecuteAsync(attempt)).ShouldBe(PayoutOutcome.Paid);

        wallet.Calls.ShouldBe(2);
        wallet.Applied.Count.ShouldBe(1);
        store.StateOf(attempt.CouponId).PaidToDate.ShouldBe(3_000);
    }

    [Fact]
    public async Task Blacklisted_wallet_is_dead_lettered_not_retried()
    {
        var (handler, wallet, store, ladder) = Build();
        wallet.RefuseWith = "AccountBlacklisted";

        (await handler.ExecuteAsync(Attempt(3_000))).ShouldBe(PayoutOutcome.DeadLettered);

        store.Parked.Single().Reason.ShouldBe("AccountBlacklisted");
        ladder.Scheduled.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_v2_settlement_pays_its_target()
    {
        var (handler, wallet, _, _) = Build();
        var settled = new CouponSettledV2(Guid.NewGuid(), Guid.NewGuid(), 1, CouponOutcome.Won, new Money(1_000, "ZAR"), new Money(2_500, "ZAR"), [], DateTimeOffset.UtcNow);

        (await handler.ExecuteAsync(ProcessPayoutHandler.FirstAttempt(settled, DateTimeOffset.UtcNow))).ShouldBe(PayoutOutcome.Paid);

        wallet.Applied.Values.ShouldBe([2_500L]);
    }

    [Fact]
    public async Task A_replayed_v2_settlement_of_a_paid_version_pays_nothing()
    {
        var (handler, wallet, _, _) = Build();
        var settled = new CouponSettledV2(Guid.NewGuid(), Guid.NewGuid(), 1, CouponOutcome.Won, new Money(1_000, "ZAR"), new Money(2_500, "ZAR"), [], DateTimeOffset.UtcNow);
        await handler.ExecuteAsync(ProcessPayoutHandler.FirstAttempt(settled, DateTimeOffset.UtcNow));

        (await handler.ExecuteAsync(ProcessPayoutHandler.FirstAttempt(settled, DateTimeOffset.UtcNow))).ShouldBe(PayoutOutcome.AlreadyApplied);

        wallet.Applied.Values.ShouldBe([2_500L]);
    }

    private static PayoutAttemptV1 Attempt(long target) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, CouponOutcome.Won, new Money(target, "ZAR"), PayoutStep.ComputeDelta, 0, null, DateTimeOffset.UtcNow);

    private static (ProcessPayoutHandler, FakeWallet, FakeStore, CapturingLadder) Build(IFaultPoint? faults = null)
    {
        var wallet = new FakeWallet();
        var store = new FakeStore();
        var ladder = new CapturingLadder();
        var handler = new ProcessPayoutHandler(store, wallet, ladder, RetryLadder.Standard, faults ?? new CrashOnce("never"), TimeProvider.System, NullLogger<ProcessPayoutHandler>.Instance);
        return (handler, wallet, store, ladder);
    }
}
