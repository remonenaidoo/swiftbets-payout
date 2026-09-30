using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Payout;
using SwiftBets.Payout.Application.Handlers;

namespace SwiftBets.Payout.Infrastructure.Consumers;

public sealed class PayoutAttemptConsumer(ProcessPayoutHandler handler) : IEventHandler<PayoutAttemptV1>
{
    public Task HandleAsync(ConsumedEvent<PayoutAttemptV1> message, CancellationToken cancellationToken) =>
        handler.ExecuteAsync(message.Envelope.Payload);
}
