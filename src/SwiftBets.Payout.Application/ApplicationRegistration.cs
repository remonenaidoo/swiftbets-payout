using Microsoft.Extensions.DependencyInjection;
using SwiftBets.Payout.Application.Handlers;

namespace SwiftBets.Payout.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddPayoutApplication(this IServiceCollection services)
    {
        services.AddScoped<ProcessPayoutHandler>();
        return services;
    }
}
