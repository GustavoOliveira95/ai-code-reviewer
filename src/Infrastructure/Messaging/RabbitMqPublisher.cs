using Application.Interfaces;
using Application.Messages;
using MassTransit;

namespace Infrastructure.Messaging;

public sealed class RabbitMqPublisher : IReviewPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public RabbitMqPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishAsync(ReviewRequestedMessage message, CancellationToken cancellationToken = default)
        => _publishEndpoint.Publish(message, cancellationToken);
}
