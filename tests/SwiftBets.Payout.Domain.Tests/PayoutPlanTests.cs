namespace SwiftBets.Payout.Domain.Tests;

public sealed class PayoutPlanTests
{
    private static readonly Guid Coupon = Guid.Parse("0199aaaa-0000-7000-8000-000000000001");

    [Fact]
    public void First_winning_settlement_credits_the_full_target_under_a_win_key()
    {
        var plan = PayoutPlan.For(Coupon, 1, SettlementKind.Won, targetPayout: 3_000, paidToDate: 0);

        plan.Delta.ShouldBe(3_000);
        plan.IdempotencyKey.ShouldBe("0199aaaa000070008000000000000001_bet1_WIN_1");
    }

    [Fact]
    public void Correction_to_a_loss_claws_back_exactly_what_was_paid()
    {
        var plan = PayoutPlan.For(Coupon, 2, SettlementKind.Lost, targetPayout: 0, paidToDate: 3_000);

        plan.Delta.ShouldBe(-3_000);
        plan.Type.ShouldBe(PostingType.ResettleDebit);
    }

    [Fact]
    public void A_cashout_credits_the_cashout_amount_under_its_own_key()
    {
        var plan = PayoutPlan.For(Coupon, 1, SettlementKind.CashedOut, targetPayout: 1_450, paidToDate: 0);

        plan.Delta.ShouldBe(1_450);
        plan.IdempotencyKey.ShouldBe("0199aaaa000070008000000000000001_bet1_CASHOUT_1");
    }

    [Fact]
    public void A_cashout_already_paid_moves_nothing()
    {
        var plan = PayoutPlan.For(Coupon, 1, SettlementKind.CashedOut, targetPayout: 1_450, paidToDate: 1_450);

        plan.Type.ShouldBe(PostingType.None);
        plan.Delta.ShouldBe(0);
    }
}
