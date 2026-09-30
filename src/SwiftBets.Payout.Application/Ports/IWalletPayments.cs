namespace SwiftBets.Payout.Application.Ports;

public interface IWalletPayments
{
    Task<(WalletPaymentStatus Status, string? FailureCode)> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference);

    Task<(WalletPaymentStatus Status, string? FailureCode)> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference);
}
