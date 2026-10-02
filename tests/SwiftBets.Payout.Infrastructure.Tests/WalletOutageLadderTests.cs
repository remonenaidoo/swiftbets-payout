using System.Collections.Concurrent;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Money;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application;
using SwiftBets.Payout.Application.Ports;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]
[assembly: AssemblyFixture(typeof(RedpandaFixture))]

namespace SwiftBets.Payout.Infrastructure.Tests;

/// <summary>Phase 2 gate: settlements that arrive during a wallet outage drain through the Kafka retry ladder once it recovers, each paid exactly once.</summary>
public sealed class WalletOutageLadderTests(SqlServerFixture sql, RedpandaFixture redpanda)
{
    [Fact]
    public async Task Outage_drains_through_the_ladder_with_zero_double_payment()
    {
        var environment = "t" + Guid.NewGuid().ToString("N")[..10];
        var connectionString = await sql.CreateDatabaseAsync("payout_" + environment);
        var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbPayout={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        await redpanda.CreateTopicsAsync(3, [.. new[] { Topics.CouponSettledV2, Topics.PayoutRetry5Seconds, Topics.PayoutRetry1Minute, Topics.PayoutRetry15Minutes, Topics.PayoutCompleted, Topics.PayoutDeadLetter }
            .SelectMany(t => new[] { TopicName.For(t, environment).Value, TopicName.For(t, environment).DeadLetter().Value })]);

        var wallet = new OutageWallet { IsDown = true };
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SbPayout"] = connectionString,
                ["Kafka:BootstrapServers"] = redpanda.BootstrapServers,
                ["Kafka:Environment"] = environment,
                ["Kafka:ClientId"] = "payout-test",
                ["Kafka:MaxTransientBackoffSeconds"] = "1",
                ["Wallet:GrpcAddress"] = "http://127.0.0.1:1",
                ["ServiceIdentity:TokenEndpoint"] = "http://127.0.0.1:1/auth/token",
                ["ServiceIdentity:ClientId"] = "payout",
                ["ServiceIdentity:ClientSecret"] = "unused",
                ["Payout:Ladder:RungSeconds:0"] = "1",
                ["Payout:Ladder:RungSeconds:1"] = "2",
                ["Payout:Ladder:RungSeconds:2"] = "3",
            }))
            .ConfigureServices((context, services) =>
            {
                services.AddPayoutApplication();
                services.AddPayoutInfrastructure(context.Configuration);
                services.RemoveAll<IWalletPayments>();
                services.AddSingleton<IWalletPayments>(wallet);
            })
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredService<IEventPublisher>();
        var coupons = Enumerable.Range(1, 5).Select(i => new CouponSettledV2(Guid.NewGuid(), Guid.NewGuid(), 1, CouponOutcome.Won, new Money(1_000, "ZAR"), new Money(1_000 * (2 + i), "ZAR"), [], DateTimeOffset.UtcNow)).ToList();
        foreach (var coupon in coupons)
        {
            await publisher.PublishAsync(Topics.CouponSettledV2, coupon.CouponId.ToString(), EventEnvelope<CouponSettledV2>.Create(coupon, DateTimeOffset.UtcNow, "outage-test"), TestContext.Current.CancellationToken);
        }

        await WaitUntilAsync(() => wallet.RefusedWhileDown >= coupons.Count);
        wallet.IsDown = false;
        await WaitUntilAsync(async () => await PaymentsAsync(connectionString) == coupons.Count);
        await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);

        wallet.Applied.Count.ShouldBe(coupons.Count);
        wallet.Applied.Values.Order().ShouldBe(coupons.Select(c => c.TargetPayout.MinorUnits).Order());
        (await PaymentsAsync(connectionString)).ShouldBe(coupons.Count);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> PaymentsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM payout.Payments");
    }

    private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!await condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not reached in time");
            await Task.Delay(250);
        }
    }

    private sealed class OutageWallet : IWalletPayments
    {
        public volatile bool IsDown;

        public int RefusedWhileDown;

        public ConcurrentDictionary<string, long> Applied { get; } = new(StringComparer.Ordinal);

        public Task<(WalletPaymentStatus Status, string? FailureCode)> CreditAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference)
        {
            if (IsDown)
            {
                Interlocked.Increment(ref RefusedWhileDown);
                return Task.FromResult<(WalletPaymentStatus, string?)>((WalletPaymentStatus.Unavailable, null));
            }

            Applied.TryAdd(idempotencyKey, amount);
            return Task.FromResult<(WalletPaymentStatus, string?)>((WalletPaymentStatus.Succeeded, null));
        }

        public Task<(WalletPaymentStatus Status, string? FailureCode)> DebitAsync(string idempotencyKey, Guid accountId, long amount, string currency, string reference) =>
            CreditAsync(idempotencyKey, accountId, -amount, currency, reference);
    }
}
