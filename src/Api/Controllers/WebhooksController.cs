using Application.UseCases.HandleWebhook;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    public WebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Receives GitHub webhook events.</summary>
    [HttpPost("github")]
    public async Task<IActionResult> GitHub(CancellationToken cancellationToken)
    {
        // Read raw body bytes (needed for HMAC validation)
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        var rawBody = ms.ToArray();

        var signature  = Request.Headers["X-Hub-Signature-256"].ToString();
        var eventType  = Request.Headers["X-GitHub-Event"].ToString();

        var result = await _mediator.Send(
            new HandleWebhookCommand(rawBody, signature, eventType),
            cancellationToken);

        if (!result.Accepted)
            return Unauthorized(new { error = result.Reason });

        return Ok(new { message = "Webhook received", reason = result.Reason });
    }
}
