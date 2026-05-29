// ============================================================
// INFRASTRUCTURE LAYER — OllamaReviewService
//
// Implements ILlmReviewService using Semantic Kernel + Ollama.
//
// KEY CONCEPT — Semantic Kernel:
// Semantic Kernel (SK) is Microsoft's AI orchestration SDK.
// It provides abstractions over different LLM providers
// (Ollama, OpenAI, Azure OpenAI, Anthropic, etc.) through a
// unified interface (IChatCompletionService).
// We use it here so switching from Ollama to Claude API would
// only require changing DependencyInjection.cs — nothing else.
//
// KEY CONCEPT — Prompt Engineering for Structured Output:
// LLMs don't natively return structured data — they generate text.
// To get a reliable JSON response, we use a technique called
// "structured output prompting":
//   1. Tell the model exactly what format to return
//   2. Provide a concrete JSON example in the prompt
//   3. Explicitly forbid extra text or markdown wrappers
//   4. Add a defensive JSON parser that handles imperfect responses
// ============================================================

using System.Text.Json;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Infrastructure.Llm;

public sealed class OllamaReviewService : ILlmReviewService
{
    // IChatCompletionService is Semantic Kernel's abstraction for chat-based LLMs.
    // It is registered in DependencyInjection.cs via kernelBuilder.AddOllamaChatCompletion().
    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<OllamaReviewService> _logger;

    // KEY CONCEPT — System Prompt:
    // The system prompt sets the LLM's "persona" and rules for the entire conversation.
    // It is sent before any user message and is typically followed more reliably than
    // user-level instructions. Here we use it to:
    //   1. Define the role: "You are an expert .NET/C# code reviewer"
    //   2. Specify what to look for: quality, bugs, security, performance
    //   3. Enforce output format: only a JSON array, no extra text
    //   4. Provide a concrete example of the expected JSON structure
    //
    // C# raw string literal (""") lets us write multiline strings without escaping.
    private const string SystemPrompt =
        """
        You are an expert .NET/C# code reviewer. Analyze the following git diff and return a JSON array of review comments.

        Review for:
        1. Code quality and best practices (naming, structure, SOLID principles)
        2. Potential bugs or logic errors (nulls, off-by-one, wrong conditions)
        3. Security vulnerabilities (injection, missing validation, exposed secrets)
        4. Performance issues (N+1 queries, unnecessary allocations, blocking calls)

        Rules:
        - Comment only on ADDED lines (starting with +)
        - Be specific and actionable
        - Return ONLY a valid JSON array, no extra text, no markdown fences

        Format:
        [{"filePath": "src/Foo.cs", "line": 42, "severity": "warning", "body": "explanation"}]

        Severity values: "info", "warning", "error"
        If there are no issues, return an empty array: []
        """;

    public OllamaReviewService(IChatCompletionService chatCompletion, ILogger<OllamaReviewService> logger)
    {
        _chatCompletion = chatCompletion;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReviewComment>> ReviewDiffAsync(string diff, CancellationToken cancellationToken = default)
    {
        // KEY CONCEPT — ChatHistory:
        // LLMs work with a conversation context — a list of messages with roles:
        //   - System: sets the model's behavior (our system prompt above)
        //   - User: the input from the user (the diff we want reviewed)
        //   - Assistant: the model's response (what we'll parse)
        //
        // ChatHistory manages this message list.
        // The system prompt is passed in the constructor, then we add the user message.
        var history = new ChatHistory(SystemPrompt);
        history.AddUserMessage($"Review this diff:\n\n{diff}");

        _logger.LogDebug("Sending diff to LLM ({Chars} chars)", diff.Length);

        // GetChatMessageContentAsync sends the full conversation to the LLM
        // and waits for the complete response (non-streaming).
        // This can take 1–5 minutes on CPU for a typical PR diff.
        var response = await _chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        var raw = response.Content ?? string.Empty;

        _logger.LogDebug("LLM raw response: {Raw}", raw);

        return ParseComments(raw);
    }

    // KEY CONCEPT — Defensive JSON Parsing:
    // LLMs are probabilistic — even with strict instructions they sometimes:
    //   - Wrap the JSON in markdown code fences (```json ... ```)
    //   - Add a preamble like "Here are the issues I found:"
    //   - Return slightly malformed JSON
    //
    // We handle these cases defensively instead of crashing.
    private IReadOnlyList<ReviewComment> ParseComments(string raw)
    {
        try
        {
            var json = raw.Trim();

            // Strip markdown code fences if the LLM added them despite our instructions
            // Example: ```json\n[...]\n``` → [...]
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
            json = json.Trim();

            // PropertyNameCaseInsensitive: accept both "filePath" and "FilePath"
            // in case the LLM uses different casing than what we specified.
            var items = JsonSerializer.Deserialize<List<CommentDto>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return items?.Select(d => new ReviewComment
            {
                FilePath = d.FilePath ?? string.Empty,
                Line = d.Line,
                // Enum.TryParse safely converts "warning" → Severity.Warning.
                // Falls back to Info if the LLM returned an unexpected value.
                Severity = Enum.TryParse<Severity>(d.Severity, ignoreCase: true, out var s) ? s : Severity.Info,
                Body = d.Body ?? string.Empty
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            // If the LLM response is completely unparseable, we log the error
            // and return an empty list — the review simply won't post any comments,
            // which is better than crashing the Worker and dropping the message.
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON. Raw: {Raw}", raw);
            return [];
        }
    }

    // Private DTO used only for JSON deserialization.
    // Nullable strings handle cases where the LLM omits a field.
    // Using a nested private record keeps this implementation detail hidden.
    private sealed record CommentDto(string? FilePath, int Line, string? Severity, string? Body);
}
