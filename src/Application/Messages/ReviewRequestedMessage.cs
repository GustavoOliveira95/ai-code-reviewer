namespace Application.Messages;

public record ReviewRequestedMessage(
    string Owner,
    string Repo,
    int PullRequestNumber,
    string HeadSha
);
