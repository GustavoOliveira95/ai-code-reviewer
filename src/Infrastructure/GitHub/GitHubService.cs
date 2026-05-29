using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

namespace Infrastructure.GitHub;

public sealed class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _logger = logger;

        var token = configuration["GitHub:Token"]
            ?? throw new InvalidOperationException("GitHub:Token is not configured.");

        _client = new GitHubClient(new ProductHeaderValue("ai-code-reviewer"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<string> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching diff for PR #{PrNumber} on {Owner}/{Repo}", pullRequestNumber, owner, repo);

        var files = await _client.PullRequest.Files(owner, repo, pullRequestNumber);

        var sb = new System.Text.StringBuilder();
        foreach (var file in files)
        {
            sb.AppendLine($"--- a/{file.FileName}");
            sb.AppendLine($"+++ b/{file.FileName}");
            if (!string.IsNullOrWhiteSpace(file.Patch))
                sb.AppendLine(file.Patch);
        }

        return sb.ToString();
    }

    public async Task PostReviewCommentsAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string headSha,
        IReadOnlyList<ReviewComment> comments,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Posting {Count} comment(s) to PR #{PrNumber}", comments.Count, pullRequestNumber);

        // Build a single review body from all comments
        var body = new System.Text.StringBuilder();
        body.AppendLine("## AI Code Review");
        body.AppendLine();

        foreach (var c in comments)
        {
            var icon = c.Severity switch
            {
                Severity.Error   => "🔴",
                Severity.Warning => "🟡",
                _                => "🔵"
            };
            body.AppendLine($"{icon} **[{c.Severity}]** `{c.FilePath}` (line {c.Line})");
            body.AppendLine($"> {c.Body}");
            body.AppendLine();
        }

        var reviewCreate = new PullRequestReviewCreate
        {
            CommitId = headSha,
            Body = body.ToString(),
            Event = PullRequestReviewEvent.Comment
        };

        await _client.PullRequest.Review.Create(owner, repo, pullRequestNumber, reviewCreate);
    }
}
