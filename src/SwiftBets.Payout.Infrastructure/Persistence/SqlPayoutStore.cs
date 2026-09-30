using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Serialization;
using SwiftBets.Payout.Application.Ports;

namespace SwiftBets.Payout.Infrastructure.Persistence;

public sealed class SqlPayoutStore(ISqlConnectionFactory connections, IOutbox outbox, TimeProvider time) : IPayoutStore
{
    private static readonly SqlResources Sql = SqlResources.For<SqlPayoutStore>();

    public async Task<PayoutState?> TryLeaseAsync(Guid couponId, Guid punterId, string holder, TimeSpan lease)
    {
        await using var connection = await connections.OpenAsync(CancellationToken.None);
        try
        {
            await connection.ExecuteAsync(Sql.Get("Payout.EnsureRow"), new { CouponId = couponId, PunterId = punterId });
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
        }

        var now = time.GetUtcNow();
        var row = await connection.QuerySingleOrDefaultAsync<(long PaidToDate, int LastVersion)?>(Sql.Get("Payout.Lease"), new { CouponId = couponId, Holder = holder, Until = now + lease, Now = now });
        return row is { } state ? new PayoutState(state.PaidToDate, state.LastVersion) : null;
    }

    public async Task ReleaseLeaseAsync(Guid couponId, string holder)
    {
        await using var connection = await connections.OpenAsync(CancellationToken.None);
        await connection.ExecuteAsync(Sql.Get("Payout.ReleaseLease"), new { CouponId = couponId, Holder = holder });
    }

    public async Task<bool> RecordAsync(Guid couponId, int version, long targetPayout, long delta, string idempotencyKey, PayoutCompletedV1 completed)
    {
        await using var connection = await connections.OpenAsync(CancellationToken.None);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var recorded = await connection.ExecuteScalarAsync<int>(Sql.Get("Payout.Record"),
            new { CouponId = couponId, Version = version, TargetPayout = targetPayout, Delta = delta, IdempotencyKey = idempotencyKey, Now = time.GetUtcNow() }, transaction) == 1;
        if (recorded)
        {
            await outbox.EnqueueAsync(transaction, Topics.PayoutCompleted, couponId.ToString(), Envelope(completed), CancellationToken.None);
        }

        await transaction.CommitAsync();
        return recorded;
    }

    public async Task ParkAsync(PayoutAttemptV1 attempt, string reason)
    {
        await using var connection = await connections.OpenAsync(CancellationToken.None);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        var parked = await connection.ExecuteScalarAsync<int>(Sql.Get("Payout.Park"), new
        {
            attempt.CouponId, Version = attempt.SettlementVersion, AttemptJson = JsonSerializer.Serialize(attempt, ContractJson.Options), Reason = reason.Length > 500 ? reason[..500] : reason, Now = time.GetUtcNow(),
        }, transaction);
        if (parked == 1)
        {
            await outbox.EnqueueAsync(transaction, Topics.PayoutDeadLetter, attempt.CouponId.ToString(), Envelope(attempt with { LastError = reason }), CancellationToken.None);
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<(PayoutAttemptV1 Attempt, string Reason, DateTimeOffset ParkedAt)>> ListDeadLettersAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<(string AttemptJson, string Reason, DateTimeOffset ParkedAt)>(new CommandDefinition(Sql.Get("Payout.ListDeadLetters"), new { Limit = limit }, cancellationToken: cancellationToken));
        return [.. rows.Select(r => (JsonSerializer.Deserialize<PayoutAttemptV1>(r.AttemptJson, ContractJson.Options)!, r.Reason, r.ParkedAt))];
    }

    public async Task<PayoutAttemptV1?> TakeDeadLetterAsync(Guid couponId, int version)
    {
        await using var connection = await connections.OpenAsync(CancellationToken.None);
        var json = await connection.QuerySingleOrDefaultAsync<string>(Sql.Get("Payout.TakeDeadLetter"), new { CouponId = couponId, Version = version });
        return json is null ? null : JsonSerializer.Deserialize<PayoutAttemptV1>(json, ContractJson.Options);
    }

    private EventEnvelope<T> Envelope<T>(T payload)
        where T : IEventContract =>
        EventEnvelope<T>.Create(payload, time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId());
}
