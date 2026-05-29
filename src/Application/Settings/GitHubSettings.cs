// ============================================================
// APPLICATION LAYER — GitHubSettings (Options Pattern)
//
// KEY CONCEPT — Options Pattern (IOptions<T>):
// Instead of injecting IConfiguration directly into handlers
// (which would tie them to the configuration system), we define
// a typed settings class and use IOptions<GitHubSettings>.
//
// Benefits:
//   - Strongly-typed: no "magic strings" like configuration["GitHub:Token"]
//   - Validated at startup: missing values fail fast, not at request time
//   - Testable: inject a mock IOptions<GitHubSettings> in unit tests
//
// The values are populated from appsettings.json / environment
// variables in DependencyInjection.cs via:
//   services.Configure<GitHubSettings>(configuration.GetSection("GitHub"))
// ============================================================

namespace Application.Settings;

public sealed class GitHubSettings
{
    // The configuration section key — must match appsettings.json:
    //   "GitHub": { "Token": "...", "WebhookSecret": "..." }
    public const string Section = "GitHub";

    // Personal Access Token used to authenticate with the GitHub API.
    // In production, store this in a secret manager (Azure Key Vault,
    // AWS Secrets Manager, etc.) — never commit it to source control.
    public string Token { get; init; } = string.Empty;

    // The secret configured in GitHub's webhook settings.
    // GitHub uses this to sign each webhook payload with HMAC-SHA256,
    // allowing us to verify the request actually came from GitHub.
    public string WebhookSecret { get; init; } = string.Empty;
}
