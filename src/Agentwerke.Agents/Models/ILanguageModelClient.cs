namespace Agentwerke.Agents.Models;

public interface ILanguageModelClient
{
    Task<LanguageModelResponse> RunAsync(
        LanguageModelRequest request,
        Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
        CancellationToken cancellationToken);
}

public sealed record LanguageModelRequest(
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyList<LanguageModelToolDefinition> Tools,
    int MaxTokens = 4096);

public sealed record LanguageModelResponse(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyList<LanguageModelToolCall> AllToolCalls,
    LanguageModelTokenUsage Usage,
    string? ModelId,
    string? StepStatus = null);

public sealed record LanguageModelToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<LanguageModelToolParameter> Parameters);

public sealed record LanguageModelToolParameter(
    string Name,
    string Type,
    string Description,
    bool Required = false,
    /// <summary>Allowed values for an enum-constrained parameter, or null/empty for none.</summary>
    IReadOnlyList<string>? EnumValues = null,
    /// <summary>For <c>Type == "array"</c>, the JSON schema type of each item (e.g. "string").</summary>
    string? ItemType = null);

public sealed record LanguageModelToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Input);

public sealed record LanguageModelToolResult(
    string ToolCallId,
    string Content,
    bool IsError = false);

public sealed record LanguageModelTokenUsage(
    int InputTokens,
    int OutputTokens,
    int CacheCreationInputTokens = 0,
    int CacheReadInputTokens = 0);
