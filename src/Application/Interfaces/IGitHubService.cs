// ============================================================
// APPLICATION LAYER — IGitHubService (Interface / Port)
//
// KEY CONCEPT — Dependency Inversion Principle (the 'D' in SOLID):
// The Application layer defines WHAT it needs (this interface),
// but does NOT know HOW it is implemented. The Infrastructure
// layer provides the actual implementation using Octokit.NET.
//
// This means:
//   - Application has NO reference to Octokit or the GitHub API
//   - You can swap the implementation (e.g., use a mock in tests)
//     without touching the Application code at all
//   - The dependency arrow points inward (infra → app), never outward
// ============================================================

using Domain.Entities;

namespace Application.Interfaces;

public interface IGitHubService
{
    // Fetches the raw diff of a Pull Request.
    // Returns a unified diff string (the same format as 'git diff'),
    // which is then sent to the LLM for analysis.
    // Example output:
    //   --- a/src/Foo.cs
    //   +++ b/src/Foo.cs
    //   @@ -10,6 +10,7 @@
    //   + var result = DoSomething();
    Task<string> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default);

    // Posts the LLM-generated review comments back to the PR on GitHub.
    // headSha ties the review to a specific commit snapshot —
    // required by the GitHub API to place inline comments correctly.
    Task PostReviewCommentsAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string headSha,
        IReadOnlyList<ReviewComment> comments,
        CancellationToken cancellationToken = default);
}
