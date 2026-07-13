namespace Agentwerke.Api.Contracts.Runs;

/// <summary>One entry in a run's agent conversation (#192): a coordination post, a delegation, or a human ask.</summary>
public sealed record InteractionSummary(
    string Id,
    string RunId,
    string? StepId,
    string From,
    string Kind,
    string AddresseeType,
    string? Addressee,
    bool Blocking,
    string Prompt,
    IReadOnlyList<string> Options,
    string Status,
    string? Response,
    string? RespondedBy,
    string? RespondedAt,
    string CreatedAt,
    /// <summary>For tool_access interactions (#202): the tool the agent asked for.</summary>
    string? ToolName = null,
    /// <summary>For tool_access interactions (#202): the model's stated intent (truncated tool input).</summary>
    string? Intent = null);
