namespace Domain.Entities;

public enum Severity { Info, Warning, Error }

public sealed class ReviewComment
{
    public string FilePath { get; init; } = string.Empty;
    public int Line { get; init; }
    public Severity Severity { get; init; }
    public string Body { get; init; } = string.Empty;
}
