using System.Text.Json;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Infrastructure.Llm;

public sealed class OllamaReviewService : ILlmReviewService
{
    private readonly IChatCompletionService _chatCompletion;
    private readonly ILogger<OllamaReviewService> _logger;

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
        var history = new ChatHistory(SystemPrompt);
        history.AddUserMessage($"Review this diff:\n\n{diff}");

        _logger.LogDebug("Sending diff to LLM ({Chars} chars)", diff.Length);

        var response = await _chatCompletion.GetChatMessageContentAsync(history, cancellationToken: cancellationToken);
        var raw = response.Content ?? string.Empty;

        _logger.LogDebug("LLM raw response: {Raw}", raw);

        return ParseComments(raw);
    }

    private IReadOnlyList<ReviewComment> ParseComments(string raw)
    {
        try
        {
            // Strip markdown code fences if LLM added them anyway
            var json = raw.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
            json = json.Trim();

            var items = JsonSerializer.Deserialize<List<CommentDto>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return items?.Select(d => new ReviewComment
            {
                FilePath = d.FilePath ?? string.Empty,
                Line = d.Line,
                Severity = Enum.TryParse<Severity>(d.Severity, ignoreCase: true, out var s) ? s : Severity.Info,
                Body = d.Body ?? string.Empty
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON. Raw: {Raw}", raw);
            return [];
        }
    }

    private sealed record CommentDto(string? FilePath, int Line, string? Severity, string? Body);
}
