using Application.Messages;
using MediatR;

namespace Application.UseCases.ProcessReview;

public record ProcessReviewCommand(ReviewRequestedMessage Message) : IRequest;
