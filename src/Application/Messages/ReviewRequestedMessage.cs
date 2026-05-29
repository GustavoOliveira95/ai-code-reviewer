// ============================================================
// APPLICATION LAYER — ReviewRequestedMessage (Message Contract)
//
// This is the MESSAGE that travels through RabbitMQ between
// the Api (producer) and the Worker (consumer).
//
// KEY CONCEPT — Message Contract:
// Both sides of the queue must agree on the message structure.
// By placing this record in the Application project (shared by
// both Api and Worker), we have a single source of truth for
// the contract — no duplication, no deserialization mismatches.
//
// KEY CONCEPT — C# record:
// A 'record' is a reference type with built-in value equality,
// immutability, and a concise primary constructor syntax.
// Perfect for messages and DTOs because:
//   - Immutable by default (properties are init-only)
//   - Two records with the same values are considered equal
//   - MassTransit can serialize/deserialize them easily
// ============================================================

namespace Application.Messages;

// This is all the information the Worker needs to:
//   1. Fetch the PR diff from GitHub (owner + repo + prNumber)
//   2. Post the review back to the correct commit (headSha)
public record ReviewRequestedMessage(
    string Owner,           // GitHub username or organization (e.g., "GustavoOliveira95")
    string Repo,            // Repository name (e.g., "ai-code-reviewer")
    int PullRequestNumber,  // PR number in that repo (e.g., 42)
    string HeadSha          // Git commit hash at the tip of the PR branch
);
