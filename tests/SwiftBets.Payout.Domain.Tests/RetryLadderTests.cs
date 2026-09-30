namespace SwiftBets.Payout.Domain.Tests;

public sealed class RetryLadderTests
{
    [Fact]
    public void First_failure_goes_to_the_five_second_rung() => RetryLadder.Standard.Next(1).ShouldBe((0, TimeSpan.FromSeconds(5)));

    [Fact]
    public void Failure_past_the_fifteen_minute_rung_leaves_the_ladder() => RetryLadder.Standard.Next(4).ShouldBeNull();
}
