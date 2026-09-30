using Microsoft.Extensions.Options;
using SwiftBets.BuildingBlocks.Core;
using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Messaging;
using SwiftBets.Contracts.Payout;
using SwiftBets.Payout.Application.Ports;

namespace SwiftBets.Payout.Infrastructure.Messaging;

/// <summary>Each rung is its own topic; the attempt carries a due time and the rung's consumer pauses its partition until then.</summary>
public sealed class KafkaPayoutLadder(IEventPublisher publisher, IOptions<KafkaOptions> kafka, TimeProvider time) : IPayoutLadder
{
    public static readonly string[] RungTopics = [Topics.PayoutRetry5Seconds, Topics.PayoutRetry1Minute, Topics.PayoutRetry15Minutes];

    public Task ScheduleAsync(PayoutAttemptV1 attempt, int rung, TimeSpan delay)
    {
        var envelope = EventEnvelope<PayoutAttemptV1>.Create(attempt, time.GetUtcNow(), CorrelationContext.CorrelationId ?? CorrelationContext.NewId());
        var headers = EnvelopeSerializer.Headers(envelope);
        headers[MessageHeaders.RetryDueAt] = DeferredDelivery.Format(time.GetUtcNow() + delay);
        headers[MessageHeaders.RetryStep] = attempt.Step.ToString();
        headers[MessageHeaders.RetryAttempt] = attempt.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return publisher.PublishRawAsync(
            new OutgoingMessage(TopicName.For(RungTopics[rung], kafka.Value.Environment).Value, attempt.CouponId.ToString(), EnvelopeSerializer.Serialize(envelope), headers),
            CancellationToken.None);
    }
}
