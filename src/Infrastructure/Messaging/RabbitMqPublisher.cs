// ============================================================
// INFRASTRUCTURE LAYER — RabbitMqPublisher
//
// Implements IReviewPublisher using MassTransit's IPublishEndpoint.
//
// KEY CONCEPT — MassTransit:
// MassTransit is a .NET message bus abstraction that sits on top
// of message brokers like RabbitMQ, Azure Service Bus, Amazon SQS.
// It provides:
//   - Automatic serialization/deserialization of messages (JSON by default)
//   - Automatic queue/exchange creation in RabbitMQ
//   - Retry policies, error queues, and dead-letter handling
//   - A simple IPublishEndpoint for fire-and-forget publishing
//
// KEY CONCEPT — Publish vs Send in MassTransit:
//   - Publish: fan-out pattern — any registered consumer of this message type receives it
//   - Send: point-to-point — you specify the exact destination queue
// We use Publish here because we have one consumer, but it would
// naturally scale to multiple consumers if needed.
//
// KEY CONCEPT — Thin adapter:
// This class is intentionally minimal. Its only job is to translate
// the IReviewPublisher.PublishAsync() call into a MassTransit call.
// Business logic stays in the Application layer.
// ============================================================

using Application.Interfaces;
using Application.Messages;
using MassTransit;

namespace Infrastructure.Messaging;

public sealed class RabbitMqPublisher : IReviewPublisher
{
    // IPublishEndpoint is MassTransit's abstraction for publishing messages.
    // It is automatically registered by services.AddMassTransit() in DI.
    private readonly IPublishEndpoint _publishEndpoint;

    public RabbitMqPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    // MassTransit handles all the complexity:
    //   - Serializes ReviewRequestedMessage to JSON
    //   - Connects to RabbitMQ
    //   - Creates the exchange/queue if they don't exist
    //   - Publishes the message with delivery guarantee
    //   - Returns only after the broker confirms receipt
    public Task PublishAsync(ReviewRequestedMessage message, CancellationToken cancellationToken = default)
        => _publishEndpoint.Publish(message, cancellationToken);
}
