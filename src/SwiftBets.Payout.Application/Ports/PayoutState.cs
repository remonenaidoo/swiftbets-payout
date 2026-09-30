namespace SwiftBets.Payout.Application.Ports;

public sealed record PayoutState(long PaidToDate, int LastVersion);
