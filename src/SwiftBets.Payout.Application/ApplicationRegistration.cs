using Microsoft.Extensions.DependencyInjection;

namespace SwiftBets.Payout.Application;

public static class ApplicationRegistration
{
    public static IServiceCollection AddPayoutApplication(this IServiceCollection services) => services;
}
