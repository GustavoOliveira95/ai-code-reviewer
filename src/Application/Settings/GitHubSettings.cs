namespace Application.Settings;

public sealed class GitHubSettings
{
    public const string Section = "GitHub";
    public string Token { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
}
