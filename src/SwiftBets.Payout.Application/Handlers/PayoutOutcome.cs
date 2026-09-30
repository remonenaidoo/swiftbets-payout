namespace SwiftBets.Payout.Application.Handlers;

public enum PayoutOutcome
{
    Paid,
    NothingToPay,
    AlreadyApplied,
    Scheduled,
    DeadLettered,
}
