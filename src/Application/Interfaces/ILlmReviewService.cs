// ============================================================
// APPLICATION LAYER — ILlmReviewService (Interface / Port)
//
// Abstracts the LLM interaction from the rest of the application.
// The Application layer only knows it can send a diff and get
// back a list of ReviewComments — it doesn't care if it's
// Ollama, OpenAI, Claude, or any other model.
//
// This abstraction also makes future improvements easy:
//   - Switch from llama3.2:3b to a better model → only change Infrastructure
//   - Add streaming support → extend this interface, implement in Infrastructure
//   - Mock the LLM in unit tests → implement a FakeLlmReviewService
// ============================================================

using Domain.Entities;

namespace Application.Interfaces;

public interface ILlmReviewService
{
    // Takes a git diff string and returns a structured list of
    // code review comments. The LLM is expected to analyze the
    // diff and produce actionable feedback for each issue found.
    // Returns an empty list (not null) if no issues are detected.
    Task<IReadOnlyList<ReviewComment>> ReviewDiffAsync(
        string diff,
        CancellationToken cancellationToken = default);
}
