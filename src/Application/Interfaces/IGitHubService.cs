using Domain.Entities;

namespace Application.Interfaces;

public interface IGitHubService
{
    Task<string> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default);

    Task PostReviewCommentsAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string headSha,
        IReadOnlyList<ReviewComment> comments,
        CancellationToken cancellationToken = default);
}
