using Domain.Entities;

namespace Application.Interfaces;

public interface ILlmReviewService
{
    Task<IReadOnlyList<ReviewComment>> ReviewDiffAsync(
        string diff,
        CancellationToken cancellationToken = default);
}
