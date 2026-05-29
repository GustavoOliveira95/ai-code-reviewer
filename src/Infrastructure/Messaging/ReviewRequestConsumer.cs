// ============================================================
// INFRASTRUCTURE LAYER — ReviewRequestConsumer
//
// This is the ENTRY POINT of the Worker side.
// It implements MassTransit's IConsumer<T> interface, which
// tells MassTransit: "when a ReviewRequestedMessage arrives
// in the queue, call my Consume() method".
//
// KEY CONCEPT — Consumer in MassTransit:
// A Consumer is the receiving side of the message queue.
// MassTransit handles all the low-level work:
//   - Listening to the RabbitMQ queue
//   - Deserializing the JSON message back into ReviewRequestedMessage
//   - Calling Consume() with a ConsumeContext (message + metadata)
//   - Acknowledging (ACK) the message on success
//   - Returning it to the queue (NACK) if an exception is thrown
//
// KEY CONCEPT — ACK / NACK:
// RabbitMQ uses acknowledgements to guarantee delivery:
//   - ACK: "I processed this message successfully, remove it from the queue"
//   - NACK: "Something went wrong, put it back so it can be retried"
// If the Worker crashes mid-processing, the message is automatically
// re-queued and retried — no message is lost.
//
// KEY CONCEPT — Consumer as thin adapter:
// The Consumer itself does very little: it just bridges MassTransit
// into MediatR by dispatching a ProcessReviewCommand.
// This keeps the business logic in ProcessReviewHandler and makes
// the consumer easy to understand at a glance.
// ============================================================

using Application.Messages;
using Application.UseCases.ProcessReview;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging;

// IConsumer<ReviewRequestedMessage> registers this class as a
// handler for messages of type ReviewRequestedMessage in MassTransit.
// The queue name is derived automatically from the message type.
public sealed class ReviewRequestConsumer : IConsumer<ReviewRequestedMessage>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReviewRequestConsumer> _logger;

    public ReviewRequestConsumer(IMediator mediator, ILogger<ReviewRequestConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    // ConsumeContext<T> wraps the message and provides additional
    // metadata: message ID, send time, retry count, headers, etc.
    // context.CancellationToken is signaled if the host is shutting
    // down — the Worker will complete the current message before stopping.
    public async Task Consume(ConsumeContext<ReviewRequestedMessage> context)
    {
        _logger.LogInformation(
            "Received review request for PR #{PrNumber} on {Owner}/{Repo}",
            context.Message.PullRequestNumber,
            context.Message.Owner,
            context.Message.Repo);

        // Dispatch to the application layer via MediatR.
        // If ProcessReviewHandler throws, MassTransit will catch it and
        // NACK the message, triggering a retry based on the configured policy.
        await _mediator.Send(new ProcessReviewCommand(context.Message), context.CancellationToken);
    }
}
