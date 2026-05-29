// ============================================================
// APPLICATION LAYER — ProcessReviewCommand
//
// This is the WORKER side command. It is dispatched by the
// ReviewRequestConsumer (Infrastructure) after it picks up
// a ReviewRequestedMessage from RabbitMQ.
//
// Notice the separation:
//   - HandleWebhookCommand → triggered by HTTP (API side)
//   - ProcessReviewCommand  → triggered by the message queue (Worker side)
//
// Both commands use MediatR, which means the Worker also benefits
// from the same clean handler pattern — no framework code leaks
// into the business logic.
//
// IRequest (without a type parameter) means this command does not
// return a value — it's a "fire and complete" operation.
// The result (comments posted to GitHub) is a side effect.
// ============================================================

using Application.Messages;
using MediatR;

namespace Application.UseCases.ProcessReview;

public record ProcessReviewCommand(
    // Carries all the information needed to process the review.
    // This is the deserialized message that arrived from RabbitMQ.
    ReviewRequestedMessage Message
) : IRequest;
