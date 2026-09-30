using Grpc.Core;
using Microsoft.Extensions.Options;
using SwiftBets.Contracts.Grpc.Wallet.V1;
using SwiftBets.Payout.Application.Ports;
using WalletGrpc = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet;

namespace SwiftBets.Payout.Infrastructure.Wallet;

/// <summary>Credits and debits over the wallet's gRPC API. A transport failure is Unavailable: the key makes the retry safe.</summary>
public sealed class GrpcWalletPayments(WalletGrpc.WalletClient client, IOptions<WalletOptions> options) : IWalletPayments
{
    public Task<(WalletPaymentStatus Status, string? FailureCode)> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        CallAsync(deadline => client.CreditAsync(Request(idempotencyKey, accountId, amount, currency, reference, "payout"), deadline: deadline).ResponseAsync);

    public Task<(WalletPaymentStatus Status, string? FailureCode)> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
        CallAsync(deadline => client.DebitAsync(Request(idempotencyKey, accountId, amount, currency, reference, "resettlement"), deadline: deadline).ResponseAsync);

    private static PostingRequest Request(string key, Guid accountId, long amount, string currency, string reference, string reason) => new()
    {
        IdempotencyKey = key, AccountId = accountId.ToString(), Amount = new Money { MinorUnits = amount, Currency = currency }, Reference = reference, Reason = reason,
    };

    private async Task<(WalletPaymentStatus, string?)> CallAsync(Func<DateTime, Task<PostingReply>> call)
    {
        try
        {
            var reply = await call(DateTime.UtcNow.AddSeconds(options.Value.DeadlineSeconds));
            return reply.OutcomeCase == PostingReply.OutcomeOneofCase.Posting
                ? (WalletPaymentStatus.Succeeded, null)
                : (WalletPaymentStatus.Refused, reply.Failure.Code.ToString());
        }
        catch (RpcException ex) when (ex.StatusCode is not (StatusCode.InvalidArgument or StatusCode.PermissionDenied or StatusCode.Unauthenticated))
        {
            return (WalletPaymentStatus.Unavailable, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            return (WalletPaymentStatus.Unavailable, null);
        }
    }
}
