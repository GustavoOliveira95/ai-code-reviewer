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
        // 1. Validate HMAC-SHA256 signature
        if (!IsSignatureValid(request.RawBody, request.Signature))
        {
            _logger.LogWarning("Webhook rejected: invalid signature");
            return new HandleWebhookResult(false, "Invalid signature");
        }

        // 2. Only handle pull_request events
        if (!string.Equals(request.EventType, "pull_request", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Webhook ignored: event type '{EventType}' is not handled", request.EventType);
            return new HandleWebhookResult(true, "Event ignored");
        }

        // 3. Parse payload
        using var doc = JsonDocument.Parse(request.RawBody);
        var root = doc.RootElement;

        var action = root.GetProperty("action").GetString();
        if (action is not ("opened" or "synchronize" or "reopened"))
        {
            _logger.LogDebug("Webhook ignored: PR action '{Action}' is not handled", action);
            return new HandleWebhookResult(true, "PR action ignored");
        }

        var owner    = root.GetProperty("repository").GetProperty("owner").GetProperty("login").GetString()!;
        var repo     = root.GetProperty("repository").GetProperty("name").GetString()!;
        var prNumber = root.GetProperty("pull_request").GetProperty("number").GetInt32();
        var headSha  = root.GetProperty("pull_request").GetProperty("head").GetProperty("sha").GetString()!;

        _logger.LogInformation("Queuing review for PR #{PrNumber} on {Owner}/{Repo}", prNumber, owner, repo);

        // 4. Publish to RabbitMQ
        await _publisher.PublishAsync(new ReviewRequestedMessage(owner, repo, prNumber, headSha), cancellationToken);

        return new HandleWebhookResult(true);
    }

    private bool IsSignatureValid(byte[] body, string signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
        {
            _logger.LogError("GitHub:WebhookSecret is not configured");
            return false;
        }

        if (!signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedHex = signatureHeader[7..];
        var secretBytes = Encoding.UTF8.GetBytes(_settings.WebhookSecret);

        using var hmac = new HMACSHA256(secretBytes);
        var hash      = hmac.ComputeHash(body);
        var actualHex = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualHex),
            Encoding.UTF8.GetBytes(expectedHex.ToLowerInvariant()));
    }
}
