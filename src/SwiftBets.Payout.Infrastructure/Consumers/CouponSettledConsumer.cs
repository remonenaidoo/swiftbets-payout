using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Settlement;
using SwiftBets.Payout.Application.Handlers;

namespace SwiftBets.Payout.Infrastructure.Consumers;

public sealed class CouponSettledConsumer(ProcessPayoutHandler handler, TimeProvider time) : IEventHandler<CouponSettledV2>
{
    public Task HandleAsync(ConsumedEvent<CouponSettledV2> message, CancellationToken cancellationToken) =>
        handler.ExecuteAsync(ProcessPayoutHandler.FirstAttempt(message.Envelope.Payload, time.GetUtcNow()));
}
