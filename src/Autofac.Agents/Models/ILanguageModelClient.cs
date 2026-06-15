namespace Autofac.Agents.Models;

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
    string? ModelId);

public sealed record LanguageModelToolDefinition(
    string Name,
    string Description,
    IReadOnlyList<LanguageModelToolParameter> Parameters);

public sealed record LanguageModelToolParameter(
    string Name,
    string Type,
    string Description,
    bool Required = false);

public sealed record LanguageModelToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Input);

public sealed record LanguageModelToolResult(
    string ToolCallId,
    string Content,
    bool IsError = false);

public sealed record LanguageModelTokenUsage(int InputTokens, int OutputTokens);
