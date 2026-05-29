// ============================================================
// DOMAIN LAYER — ReviewComment
//
// The Domain layer contains pure business concepts with zero
// external dependencies. No NuGet packages, no frameworks —
// just plain C# classes and enums that model the problem.
//
// This entity represents a single piece of feedback that the
// LLM produced for a specific line of code in the PR diff.
// ============================================================

namespace Domain.Entities;

// Severity levels used to categorize each comment.
// The LLM is instructed to return one of these three values
// in its JSON response so we can display them with different
// visual cues (🔵 Info, 🟡 Warning, 🔴 Error).
public enum Severity { Info, Warning, Error }

// 'sealed' prevents unintended inheritance — a good practice
// for entity classes that represent a specific concept.
// 'init' properties make the object immutable after creation,
// which is a key principle in Domain-Driven Design.
public sealed class ReviewComment
{
    // Relative path of the file that the LLM is commenting on.
    // Example: "src/Application/UseCases/HandleWebhook/HandleWebhookHandler.cs"
    public string FilePath { get; init; } = string.Empty;

    // Line number in the file where the issue was detected.
    // The LLM is instructed to reference only added lines (starting with '+' in the diff).
    public int Line { get; init; }

    // How critical the issue is.
    // We default to 'string.Empty' instead of null to follow the
    // Null Object pattern and avoid null checks throughout the codebase.
    public Severity Severity { get; init; }

    // The human-readable explanation of the issue.
    public string Body { get; init; } = string.Empty;
}
