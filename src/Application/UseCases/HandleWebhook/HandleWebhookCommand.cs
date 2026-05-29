// ============================================================
// APPLICATION LAYER — HandleWebhookCommand (CQRS with MediatR)
//
// KEY CONCEPT — CQRS (Command Query Responsibility Segregation):
// We separate write operations (Commands) from read operations (Queries).
// A Command represents an intent to change something in the system.
// Here, the "change" is: validate the webhook and enqueue a review.
//
// KEY CONCEPT — MediatR:
// MediatR is a mediator pattern implementation that decouples the
// sender (WebhooksController) from the handler (HandleWebhookHandler).
// The controller just calls: _mediator.Send(command)
// It doesn't know anything about the handler — MediatR routes it.
//
// Flow:
//   WebhooksController.GitHub()
//     → _mediator.Send(new HandleWebhookCommand(...))
//       → HandleWebhookHandler.Handle(...)
// ============================================================

using MediatR;

namespace Application.UseCases.HandleWebhook;

// IRequest<HandleWebhookResult> tells MediatR this command
// expects a response of type HandleWebhookResult.
// The raw body is kept as byte[] (not string) because HMAC
// validation must run on the exact bytes received from GitHub —
// any encoding conversion could corrupt the signature check.
public record HandleWebhookCommand(
    byte[] RawBody,     // Raw HTTP body bytes — untouched, for HMAC validation
    string Signature,   // Value of the 'X-Hub-Signature-256' header from GitHub
    string EventType    // Value of the 'X-GitHub-Event' header (e.g., "pull_request")
) : IRequest<HandleWebhookResult>;

// Simple result object returned to the controller.
// 'Accepted' tells the controller whether to return 200 OK or 401.
// 'Reason' is an optional human-readable message (useful for debugging).
public record HandleWebhookResult(bool Accepted, string? Reason = null);
