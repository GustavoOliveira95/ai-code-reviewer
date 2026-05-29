using MediatR;

namespace Application.UseCases.HandleWebhook;

public record HandleWebhookCommand(
    byte[] RawBody,
    string Signature,
    string EventType
) : IRequest<HandleWebhookResult>;

public record HandleWebhookResult(bool Accepted, string? Reason = null);
