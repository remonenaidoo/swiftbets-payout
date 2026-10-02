using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Testing;
using SwiftBets.Payout.Infrastructure.Persistence;

namespace SwiftBets.Payout.Infrastructure.Tests;

public sealed class IntegrityDigestTests(SqlServerFixture sql)
{
    [Fact]
    public async Task A_paid_coupon_reports_its_total()
    {
        var (store, paid) = await SeedAsync();

        (await store.GetPaidTotalsAsync([paid], TestContext.Current.CancellationToken)).ShouldHaveSingleItem().ShouldBe(new(paid, 4000, 2));
    }

    [Fact]
    public async Task A_coupon_never_paid_is_absent()
    {
        var (store, _) = await SeedAsync();

        (await store.GetPaidTotalsAsync([Guid.NewGuid()], TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    private async Task<(SqlPayoutStore Store, Guid Paid)> SeedAsync()
    {
        var connectionString = await sql.CreateDatabaseAsync("payout_digest_" + Guid.NewGuid().ToString("N")[..8]);
        var entry = typeof(Program).Assembly.EntryPoint!.Invoke(null, [new[] { $"--ConnectionStrings:SbPayout={connectionString}" }]);
        (entry is Task<int> task ? await task : (int)entry!).ShouldBe(0);
        var paid = Guid.NewGuid();
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.ExecuteAsync("INSERT INTO payout.CouponPayouts (CouponId, PunterId, PaidToDate, LastVersion) VALUES (@paid, @punter, 4000, 2)", new { paid, punter = Guid.NewGuid() });
        }

        return (new SqlPayoutStore(new SqlServerConnectionFactory(connectionString), null!, TimeProvider.System), paid);
    }
}
