// ============================================================
// APPLICATION LAYER — ProcessReviewHandler
//
// This is the CORE of the Worker side. It orchestrates the
// full review pipeline once a message is dequeued:
//
//   RabbitMQ message
//     → ReviewRequestConsumer (Infrastructure)
//       → ProcessReviewCommand (via MediatR)
//         → ProcessReviewHandler (this class)
//           → IGitHubService.GetPullRequestDiffAsync()
//           → ILlmReviewService.ReviewDiffAsync()
//           → IGitHubService.PostReviewCommentsAsync()
//
// Just like HandleWebhookHandler, this class only uses
// interfaces — it has no direct dependency on Octokit,
// Ollama, or any external library.
// ============================================================

using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.UseCases.ProcessReview;

public sealed class ProcessReviewHandler : IRequestHandler<ProcessReviewCommand>
{
    private readonly IGitHubService _gitHubService;
    private readonly ILlmReviewService _llmReviewService;
    private readonly ILogger<ProcessReviewHandler> _logger;

    public ProcessReviewHandler(
        IGitHubService gitHubService,
        ILlmReviewService llmReviewService,
        ILogger<ProcessReviewHandler> logger)
    {
        _gitHubService = gitHubService;
        _llmReviewService = llmReviewService;
        _logger = logger;
    }

    public async Task Handle(ProcessReviewCommand request, CancellationToken cancellationToken)
    {
        var msg = request.Message;
        _logger.LogInformation("Processing review for PR #{PrNumber} on {Owner}/{Repo}", msg.PullRequestNumber, msg.Owner, msg.Repo);

        // ── Step 1: Fetch the diff from GitHub ────────────────────────────────
        // We call the GitHub API to get the list of changed files and their
        // patches (the actual lines added/removed). This is what the LLM will analyze.
        var diff = await _gitHubService.GetPullRequestDiffAsync(
            msg.Owner, msg.Repo, msg.PullRequestNumber, cancellationToken);

        // Guard: if the PR has no changed files (e.g., only metadata was updated),
        // there is nothing to review — we bail out early.
        if (string.IsNullOrWhiteSpace(diff))
        {
            _logger.LogWarning("PR #{PrNumber} has an empty diff — skipping review", msg.PullRequestNumber);
            return;
        }

        // ── Step 2: Send the diff to the LLM for review ───────────────────────
        // The LLM receives the raw diff and returns a structured list of comments.
        // This is the most time-consuming step — on CPU, it can take 1–5 minutes.
        // Because we're running in the Worker (not the API), the user's PR is
        // already responding normally while we wait here.
        var comments = await _llmReviewService.ReviewDiffAsync(diff, cancellationToken);

        // If the LLM finds nothing wrong, we log it and stop — no need to post
        // an empty review to the PR.
        if (comments.Count == 0)
        {
            _logger.LogInformation("PR #{PrNumber}: LLM found no issues", msg.PullRequestNumber);
            return;
        }

        _logger.LogInformation("PR #{PrNumber}: posting {Count} comment(s)", msg.PullRequestNumber, comments.Count);

        // ── Step 3: Post the comments back to GitHub ──────────────────────────
        // We call the GitHub API to create a PR Review with all the comments.
        // GitHub will display these comments directly on the PR diff view,
        // exactly like a human reviewer would leave feedback.
        await _gitHubService.PostReviewCommentsAsync(
            msg.Owner, msg.Repo, msg.PullRequestNumber, msg.HeadSha, comments, cancellationToken);

        _logger.LogInformation("PR #{PrNumber}: review completed successfully", msg.PullRequestNumber);
    }
}
