// ============================================================
// DOMAIN LAYER — ReviewRequest
//
// Represents the intent to review a specific Pull Request.
// This entity is created when GitHub fires a webhook event
// (PR opened / updated) and tracks the lifecycle of the review.
//
// In a more complete system, this would be persisted to a
// database so we could query "all pending reviews" or
// "reviews for repo X". For now it lives only in memory
// as it travels through the message queue.
// ============================================================

namespace Domain.Entities;

// Tracks where in the pipeline this review currently is.
// Useful for observability — e.g., if the Worker crashes while
// "Processing", we know which reviews need to be reprocessed.
public enum ReviewStatus { Pending, Processing, Completed, Failed }

public sealed class ReviewRequest
{
    // Globally unique identifier for this review.
    // Generated at construction time so every ReviewRequest
    // has an ID from the moment it is created.
    public Guid Id { get; init; } = Guid.NewGuid();

    // GitHub repository identity — the three pieces of information
    // needed to call the GitHub API: owner/repo#number.
    public string RepositoryOwner { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public int PullRequestNumber { get; init; }

    // The commit SHA at the tip of the PR branch.
    // Required by the GitHub API when posting a review —
    // it ensures the review is pinned to the exact code snapshot.
    public string HeadSha { get; init; } = string.Empty;

    // Status uses 'set' (not 'init') because it changes over time
    // as the review progresses through the pipeline.
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    // DateTimeOffset includes timezone info, which is safer than
    // DateTime when storing or comparing timestamps across systems.
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
