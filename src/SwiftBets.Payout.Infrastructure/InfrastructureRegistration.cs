using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.BuildingBlocks.Outbox;
using SwiftBets.BuildingBlocks.Persistence;
using SwiftBets.BuildingBlocks.Resilience;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Payout;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application.Ports;
using SwiftBets.Payout.Domain;
using SwiftBets.Payout.Infrastructure.Consumers;
using SwiftBets.Payout.Infrastructure.Messaging;
using SwiftBets.Payout.Infrastructure.Persistence;
using SwiftBets.Payout.Infrastructure.Wallet;
using WalletGrpc = SwiftBets.Contracts.Grpc.Wallet.V1.Wallet;

namespace SwiftBets.Payout.Infrastructure;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddPayoutInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSqlServerPersistence(Required(configuration, "ConnectionStrings:SbPayout"));
        services.AddKafkaMessaging(configuration);
        services.AddSqlServerOutbox(configuration);
        services.AddFaultInjection(configuration);
        services.AddValidatedOptions<WalletOptions>(configuration, WalletOptions.SectionName);
        services.AddValidatedOptions<LadderOptions>(configuration, LadderOptions.SectionName);
        services.AddSingleton(sp => new RetryLadder([.. sp.GetRequiredService<IOptions<LadderOptions>>().Value.RungSeconds.Select(s => TimeSpan.FromSeconds(s))]));
        services.AddSingleton<IPayoutStore, SqlPayoutStore>();
        services.AddSingleton<IPayoutLadder, KafkaPayoutLadder>();
        services.AddScoped<IWalletPayments, GrpcWalletPayments>();

        services.AddClientCredentials(configuration);
        services.AddGrpcClient<WalletGrpc.WalletClient>((sp, grpc) => grpc.Address = new Uri(sp.GetRequiredService<IOptions<WalletOptions>>().Value.GrpcAddress))
            .ConfigureChannel(channel =>
            {
                channel.ServiceConfig = GrpcResilience.KeyedServiceConfig;
                channel.UnsafeUseInsecureChannelCallCredentials = true;
            })
            .AddCallCredentials(async (context, metadata, sp) =>
                metadata.Add("Authorization", $"Bearer {await sp.GetRequiredService<ClientCredentialsTokenProvider>().GetTokenAsync(context.CancellationToken)}"))
            .AddKeyedGrpcResilience();

        if (configuration.GetValue("Payout:RunConsumers", true))
        {
            services.AddKafkaConsumer<CouponSettledV2, CouponSettledConsumer>(Topics.CouponSettledV2, "swiftbets.payout.settled-v2");
            for (var rung = 0; rung < KafkaPayoutLadder.RungTopics.Length; rung++)
            {
                AddLadderConsumer(services, KafkaPayoutLadder.RungTopics[rung]);
            }
        }

        return services;
    }

    private static void AddLadderConsumer(IServiceCollection services, string topic)
    {
        services.AddScoped<IEventHandler<PayoutAttemptV1>, PayoutAttemptConsumer>();
        services.AddSingleton<IHostedService>(sp => new KafkaConsumerHost<PayoutAttemptV1>(
            new ConsumerRegistration(topic, $"swiftbets.payout.{topic}"),
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<IOptions<KafkaOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<KafkaConsumerHost<PayoutAttemptV1>>>()));
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configuration '{key}' is required.");
}
