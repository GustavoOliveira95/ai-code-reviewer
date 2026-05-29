namespace Domain.Entities;

public enum ReviewStatus { Pending, Processing, Completed, Failed }

public sealed class ReviewRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RepositoryOwner { get; init; } = string.Empty;
    public string RepositoryName { get; init; } = string.Empty;
    public int PullRequestNumber { get; init; }
    public string HeadSha { get; init; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
