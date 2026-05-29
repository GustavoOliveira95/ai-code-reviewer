// ============================================================
// INFRASTRUCTURE LAYER — GitHubService
//
// This is the concrete implementation of IGitHubService.
// It uses Octokit.NET — the official GitHub API client library.
//
// KEY CONCEPT — Infrastructure layer responsibility:
// All external I/O lives here: HTTP calls, database queries,
// file system access, message brokers, etc.
// The Application layer never imports Octokit — it only knows
// the IGitHubService interface.
//
// How authentication works:
//   - We create a GitHubClient with a ProductHeaderValue (your app name)
//   - GitHub requires this User-Agent string to identify API callers
//   - We authenticate with a Personal Access Token (PAT)
//   - The PAT must have 'repo' scope to read PRs and post reviews
// ============================================================

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

        // ProductHeaderValue is the User-Agent for GitHub API requests.
        // GitHub requires a meaningful name here to identify your application.
        _client = new GitHubClient(new ProductHeaderValue("ai-code-reviewer"))
        {
            // Credentials wraps the PAT for every API call made by this client.
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

        // PullRequest.Files() calls the GitHub API endpoint:
        //   GET /repos/{owner}/{repo}/pulls/{pull_number}/files
        // It returns a list of PullRequestFile objects, each containing:
        //   - FileName: relative path of the changed file
        //   - Patch: the unified diff for that file (the actual code changes)
        //   - Status: "added", "modified", "removed", etc.
        var files = await _client.PullRequest.Files(owner, repo, pullRequestNumber);

        // Reconstruct the diff in unified format so the LLM receives something
        // it was trained to understand:
        //   --- a/src/Foo.cs   (original file)
        //   +++ b/src/Foo.cs   (new file)
        //   @@ -10,6 +10,7 @@  (hunk header)
        //   + new line of code  (added line, prefixed with +)
        //   - removed line      (deleted line, prefixed with -)
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

        // Instead of posting each comment separately (which would spam
        // the GitHub notifications), we batch them all into a single Review.
        // A Review is a top-level PR comment that can contain a summary
        // and references multiple files/lines.
        var body = new System.Text.StringBuilder();
        body.AppendLine("## AI Code Review");
        body.AppendLine();

        foreach (var c in comments)
        {
            // Visual severity indicator using emoji, mapping our Severity enum
            var icon = c.Severity switch
            {
                Severity.Error   => "🔴",
                Severity.Warning => "🟡",
                _                => "🔵"  // Info
            };
            body.AppendLine($"{icon} **[{c.Severity}]** `{c.FilePath}` (line {c.Line})");
            // Blockquote (>) indents the explanation visually on GitHub's markdown renderer
            body.AppendLine($"> {c.Body}");
            body.AppendLine();
        }

        // PullRequestReviewCreate is the Octokit DTO for the GitHub API endpoint:
        //   POST /repos/{owner}/{repo}/pulls/{pull_number}/reviews
        var reviewCreate = new PullRequestReviewCreate
        {
            // CommitId pins the review to the exact commit snapshot.
            // Without this, GitHub can't show inline comments on the correct lines.
            CommitId = headSha,
            Body = body.ToString(),
            // PullRequestReviewEvent.Comment means "leave a comment without approving or rejecting"
            // Other options: Approve, RequestChanges
            Event = PullRequestReviewEvent.Comment
        };

        await _client.PullRequest.Review.Create(owner, repo, pullRequestNumber, reviewCreate);
    }
}
