namespace Domain.Entities;

public sealed class ReviewResult
{
    public Guid ReviewRequestId { get; init; }
    public IReadOnlyList<ReviewComment> Comments { get; init; } = [];
    public DateTimeOffset PostedAt { get; init; } = DateTimeOffset.UtcNow;
}
