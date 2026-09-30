namespace SwiftBets.Payout.Application.Ports;

public sealed record PaymentView(int Version, long TargetPayout, long Delta, string IdempotencyKey, DateTimeOffset RecordedAt);
