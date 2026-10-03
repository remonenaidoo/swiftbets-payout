using SwiftBets.Payout.Application.Handlers;
using SwiftBets.BuildingBlocks.Observability;
using SwiftBets.BuildingBlocks.Web;
using SwiftBets.Contracts.Errors;
using SwiftBets.Contracts.Serialization;
using SwiftBets.Payout.Application;
using SwiftBets.Payout.Application.Ports;
using SwiftBets.Payout.Infrastructure;

if (HealthProbe.TryRun(args) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateBuilder(args);
builder.AddSwiftBetsObservability("swiftbets-payout");
builder.Services.AddSwiftBetsWeb();
builder.Services.AddSwiftBetsJwtBearer(builder.Configuration);
builder.Services.AddPayoutApplication();
builder.Services.AddPayoutInfrastructure(builder.Configuration);

var app = builder.Build();
app.UseSwiftBetsObservability();
app.UseSwiftBetsWeb();
app.UseAuthentication();
app.UseAuthorization();
app.MapSwiftBetsOperationalEndpoints();
app.MapGet("/coupons/{couponId:guid}/payout", async (Guid couponId, IPayoutStore store, HttpContext context, CancellationToken cancellationToken) =>
        await store.GetCouponPayoutAsync(couponId, cancellationToken) is { } view
            ? Results.Json(view, ContractJson.Options)
            : Error.NotFound("payout_not_found", "No payout has been attempted for that coupon.").ToHttpResult(context))
    .RequireAuthorization(Roles.OperatorOrService);
app.MapPost("/internal/integrity/payouts", async (IntegrityRequest request, IPayoutStore store, HttpContext context, CancellationToken cancellationToken) =>
        request.CouponIds is not { Count: > 0 and <= 500 }
            ? Error.Validation("invalid_coupon_ids", "Ask for 1 to 500 coupon ids.").ToHttpResult(context)
            : Results.Json(await store.GetPaidTotalsAsync(request.CouponIds, cancellationToken), ContractJson.Options))
    .RequireAuthorization(Roles.Service);
app.MapSwiftBetsFaultEndpoints();
app.MapGet("/dead-letters", async (IPayoutStore store, CancellationToken cancellationToken) =>
        Results.Json((await store.ListDeadLettersAsync(100, cancellationToken)).Select(d => new { d.Attempt, d.Reason, d.ParkedAt }), ContractJson.Options))
    .RequireAuthorization(Roles.OperatorOrService);
app.MapPost("/dead-letters/redrive", async (int? limit, RedriveDeadLettersHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(new { redriven = await handler.HandleAsync(limit ?? 100, cancellationToken) }))
    .RequireAuthorization(Roles.OperatorOrService);
app.MapPost("/dead-letters/{couponId:guid}/{version:int}/replay", async (Guid couponId, int version, IPayoutStore store, IPayoutLadder ladder, HttpContext context) =>
        await store.TakeDeadLetterAsync(couponId, version) is { } attempt
            ? await ReplayAsync(ladder, attempt)
            : Error.NotFound("dead_letter_not_found", "No parked payout for that coupon and version.").ToHttpResult(context))
    .RequireAuthorization(Roles.OperatorOrService);

await app.RunAsync();
return 0;

static async Task<IResult> ReplayAsync(IPayoutLadder ladder, SwiftBets.Contracts.Payout.PayoutAttemptV1 attempt)
{
    await ladder.ScheduleAsync(attempt with { Attempt = 0, LastError = null }, rung: 0, TimeSpan.Zero);
    return Results.Accepted(value: new { attempt.CouponId, attempt.SettlementVersion, replayedAtStep = attempt.Step.ToString() });
}

public partial class Program;

internal sealed record IntegrityRequest(IReadOnlyList<Guid>? CouponIds);
