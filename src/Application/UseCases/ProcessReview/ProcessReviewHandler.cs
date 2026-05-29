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

        // 1. Fetch the diff from GitHub
        var diff = await _gitHubService.GetPullRequestDiffAsync(
            msg.Owner, msg.Repo, msg.PullRequestNumber, cancellationToken);

        if (string.IsNullOrWhiteSpace(diff))
        {
            _logger.LogWarning("PR #{PrNumber} has an empty diff — skipping review", msg.PullRequestNumber);
            return;
        }

        // 2. Send diff to LLM for review
        var comments = await _llmReviewService.ReviewDiffAsync(diff, cancellationToken);

        if (comments.Count == 0)
        {
            _logger.LogInformation("PR #{PrNumber}: LLM found no issues", msg.PullRequestNumber);
            return;
        }

        _logger.LogInformation("PR #{PrNumber}: posting {Count} comment(s)", msg.PullRequestNumber, comments.Count);

        // 3. Post comments back to the PR
        await _gitHubService.PostReviewCommentsAsync(
            msg.Owner, msg.Repo, msg.PullRequestNumber, msg.HeadSha, comments, cancellationToken);

        _logger.LogInformation("PR #{PrNumber}: review completed successfully", msg.PullRequestNumber);
    }
}
