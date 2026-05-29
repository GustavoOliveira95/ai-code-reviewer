using Application.Messages;
using Application.UseCases.ProcessReview;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging;

public sealed class ReviewRequestConsumer : IConsumer<ReviewRequestedMessage>
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReviewRequestConsumer> _logger;

    public ReviewRequestConsumer(IMediator mediator, ILogger<ReviewRequestConsumer> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReviewRequestedMessage> context)
    {
        _logger.LogInformation(
            "Received review request for PR #{PrNumber} on {Owner}/{Repo}",
            context.Message.PullRequestNumber,
            context.Message.Owner,
            context.Message.Repo);

        await _mediator.Send(new ProcessReviewCommand(context.Message), context.CancellationToken);
    }
}
