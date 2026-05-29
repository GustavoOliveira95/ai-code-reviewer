// ============================================================
// APPLICATION LAYER — IReviewPublisher (Interface / Port)
//
// Abstracts the message publishing mechanism from the Application.
// The Handler just calls PublishAsync() — it has no idea whether
// the message goes to RabbitMQ, Azure Service Bus, AWS SQS,
// or a simple in-memory channel.
//
// KEY CONCEPT — Producer / Consumer pattern:
// This interface represents the PRODUCER side of a message queue.
// The Api calls this to drop a message into the queue and return
// immediately (fire-and-forget from the API's perspective).
// The Worker picks up the message asynchronously — this is what
// decouples the API response time from the LLM processing time.
// ============================================================

using Application.Messages;

namespace Application.Interfaces;

public interface IReviewPublisher
{
    // Publishes a review request message to the message broker.
    // After this call returns, the message is safely in the queue —
    // the Worker will process it independently, even if the API restarts.
    Task PublishAsync(ReviewRequestedMessage message, CancellationToken cancellationToken = default);
}
