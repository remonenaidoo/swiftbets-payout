using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Payout.Application;
using SwiftBets.Payout.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-payout");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddPayoutApplication();
builder.Services.AddPayoutInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.MapSwiftBetsOperationalEndpoints();
app.MapGet("/", () => Results.Ok(new { service = "swiftbets-payout" })).ExcludeFromDescription();

await app.RunAsync();
return 0;

public partial class Program;
