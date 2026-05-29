using Application.Messages;

namespace Application.Interfaces;

public interface IReviewPublisher
{
    Task PublishAsync(ReviewRequestedMessage message, CancellationToken cancellationToken = default);
}
