// ============================================================
// API LAYER — WebhooksController
//
// This is the HTTP entry point of the entire system.
// Its sole responsibility is to accept an HTTP request from
// GitHub, extract the relevant data, and hand it off to MediatR.
//
// KEY CONCEPT — Thin Controller:
// The controller does NOT contain any business logic. It only:
//   1. Reads raw bytes from the request body
//   2. Extracts headers
//   3. Delegates to the Application layer via MediatR
//   4. Translates the result into an HTTP response
//
// This keeps the controller testable (no HTTP mocking needed
// to test the business logic) and follows the Single Responsibility Principle.
//
// KEY CONCEPT — Why read the body as raw bytes?
// GitHub's HMAC signature is computed over the exact bytes of the
// request body. If we parse the body as a string first, encoding
// conversions could change the bytes and break signature validation.
// We must read the raw bytes and pass them untouched to the handler.
// ============================================================

using Application.UseCases.HandleWebhook;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// [ApiController] enables automatic model validation, problem details
// responses, and removes the need for [FromBody] on action parameters.
[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    // IMediator is the MediatR interface. The controller doesn't know
    // anything about HandleWebhookHandler — MediatR routes the command.
    private readonly IMediator _mediator;

    public WebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Receives GitHub webhook events.</summary>
    // POST /api/webhooks/github
    // GitHub will call this URL for every event configured in the webhook settings.
    [HttpPost("github")]
    public async Task<IActionResult> GitHub(CancellationToken cancellationToken)
    {
        // KEY POINT: We read the body as a raw byte array, not as a deserialized object.
        // MemoryStream buffers the stream so we can convert it to byte[].
        // CopyToAsync is async — we don't block the thread while reading.
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        var rawBody = ms.ToArray();

        // GitHub sends two important headers with every webhook:
        //   X-Hub-Signature-256: sha256=<HMAC hex digest of the body>
        //   X-GitHub-Event:      the event type (e.g., "pull_request", "push")
        var signature  = Request.Headers["X-Hub-Signature-256"].ToString();
        var eventType  = Request.Headers["X-GitHub-Event"].ToString();

        // Send the command to MediatR — it will find HandleWebhookHandler
        // and call its Handle() method. The controller has no idea what
        // happens inside the handler.
        var result = await _mediator.Send(
            new HandleWebhookCommand(rawBody, signature, eventType),
            cancellationToken);

        // Translate the handler result into an HTTP response.
        // GitHub marks a webhook delivery as "failed" if it receives
        // anything outside 2xx — so we carefully choose the right status code.
        if (!result.Accepted)
            return Unauthorized(new { error = result.Reason });  // 401

        return Ok(new { message = "Webhook received", reason = result.Reason });  // 200
    }
}
