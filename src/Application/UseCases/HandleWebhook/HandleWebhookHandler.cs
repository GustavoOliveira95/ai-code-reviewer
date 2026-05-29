// ============================================================
// APPLICATION LAYER — HandleWebhookHandler
//
// This is the CORE of the API side of the system.
// It orchestrates three responsibilities:
//   1. Security: validate the GitHub HMAC-SHA256 signature
//   2. Filtering: ignore events/actions we don't care about
//   3. Publishing: send a message to RabbitMQ for async processing
//
// Notice this handler has NO knowledge of:
//   - HTTP (no HttpContext, no Request)
//   - RabbitMQ internals (uses IReviewPublisher interface)
//   - GitHub API (doesn't fetch diffs — that's the Worker's job)
//
// This is Clean Architecture in practice: the handler only
// orchestrates business logic using abstractions.
// ============================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Application.Messages;
using Application.Settings;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.UseCases.HandleWebhook;

public sealed class HandleWebhookHandler : IRequestHandler<HandleWebhookCommand, HandleWebhookResult>
{
    private readonly IReviewPublisher _publisher;
    private readonly GitHubSettings _settings;
    private readonly ILogger<HandleWebhookHandler> _logger;

    // Dependencies are injected by the DI container.
    // IOptions<GitHubSettings> gives us access to the typed config.
    // .Value unwraps the IOptions wrapper and gives us the settings object.
    public HandleWebhookHandler(
        IReviewPublisher publisher,
        IOptions<GitHubSettings> settings,
        ILogger<HandleWebhookHandler> logger)
    {
        _publisher = publisher;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<HandleWebhookResult> Handle(HandleWebhookCommand request, CancellationToken cancellationToken)
    {
        // ── Step 1: Security check ────────────────────────────────────────────
        // Before doing anything, verify the request really came from GitHub.
        // If the signature is invalid, we reject immediately with 401.
        // This prevents attackers from injecting fake PR events into our system.
        if (!IsSignatureValid(request.RawBody, request.Signature))
        {
            _logger.LogWarning("Webhook rejected: invalid signature");
            return new HandleWebhookResult(false, "Invalid signature");
        }

        // ── Step 2: Filter by event type ─────────────────────────────────────
        // GitHub can send dozens of event types (push, issue, star, etc.).
        // We only care about 'pull_request' events.
        // Returning Accepted=true here is intentional: GitHub expects 200 OK
        // for all webhooks it delivers, even if we choose to ignore them.
        if (!string.Equals(request.EventType, "pull_request", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Webhook ignored: event type '{EventType}' is not handled", request.EventType);
            return new HandleWebhookResult(true, "Event ignored");
        }

        // ── Step 3: Parse the JSON payload ───────────────────────────────────
        // JsonDocument.Parse is low-allocation: it parses the JSON without
        // deserializing into a full object graph. We use 'using' to ensure
        // the underlying memory is returned to the pool when we're done.
        using var doc = JsonDocument.Parse(request.RawBody);
        var root = doc.RootElement;

        // Filter by PR action — we only review when code changes:
        //   'opened'      → new PR just created
        //   'synchronize' → new commits pushed to an existing PR
        //   'reopened'    → a previously closed PR was reopened
        var action = root.GetProperty("action").GetString();
        if (action is not ("opened" or "synchronize" or "reopened"))
        {
            _logger.LogDebug("Webhook ignored: PR action '{Action}' is not handled", action);
            return new HandleWebhookResult(true, "PR action ignored");
        }

        // Extract the fields we need to identify the PR.
        // The GitHub webhook payload structure:
        //   { "repository": { "owner": { "login": "..." }, "name": "..." },
        //     "pull_request": { "number": 42, "head": { "sha": "abc123" } } }
        var owner    = root.GetProperty("repository").GetProperty("owner").GetProperty("login").GetString()!;
        var repo     = root.GetProperty("repository").GetProperty("name").GetString()!;
        var prNumber = root.GetProperty("pull_request").GetProperty("number").GetInt32();
        var headSha  = root.GetProperty("pull_request").GetProperty("head").GetProperty("sha").GetString()!;

        _logger.LogInformation("Queuing review for PR #{PrNumber} on {Owner}/{Repo}", prNumber, owner, repo);

        // ── Step 4: Publish to RabbitMQ ───────────────────────────────────────
        // KEY CONCEPT — Asynchronous decoupling:
        // We publish the message and return 200 OK immediately.
        // GitHub requires a response within 10 seconds — we must not block here
        // waiting for the LLM to finish (which can take minutes on CPU).
        // The Worker will process the review independently and at its own pace.
        await _publisher.PublishAsync(new ReviewRequestedMessage(owner, repo, prNumber, headSha), cancellationToken);

        return new HandleWebhookResult(true);
    }

    // ── HMAC-SHA256 Signature Validation ─────────────────────────────────────
    // KEY CONCEPT — Webhook Security with HMAC:
    // When you configure a webhook in GitHub, you provide a "secret" string.
    // For every event, GitHub computes: HMAC-SHA256(secret, requestBody)
    // and sends the result in the 'X-Hub-Signature-256' header.
    //
    // We do the same computation on our end and compare results.
    // If they match, we know:
    //   1. The request came from GitHub (they know the secret)
    //   2. The body wasn't tampered with in transit (HMAC covers the whole body)
    private bool IsSignatureValid(byte[] body, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            _logger.LogError("GitHub:WebhookSecret is not configured");
            return false;
        }

        // GitHub sends the signature as "sha256=<hexdigest>"
        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        // Extract the hex part (skip the "sha256=" prefix — 7 characters)
        var expectedHex = signatureHeader[7..];
        var secretBytes = Encoding.UTF8.GetBytes(_settings.WebhookSecret);

        // Compute our own HMAC of the raw body using the shared secret
        using var hmac = new HMACSHA256(secretBytes);
        var hash      = hmac.ComputeHash(body);
        var actualHex = Convert.ToHexString(hash).ToLowerInvariant();

        // KEY CONCEPT — Timing-safe comparison (FixedTimeEquals):
        // We MUST NOT use == or string.Equals() here!
        // Regular string comparison short-circuits on the first mismatch,
        // creating a timing side-channel: an attacker could measure response
        // times to guess the correct signature byte by byte.
        // FixedTimeEquals always takes the same time regardless of where
        // the strings differ, preventing this class of attack.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }
}
