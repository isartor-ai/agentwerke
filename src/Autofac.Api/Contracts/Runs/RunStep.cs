namespace Autofac.Api.Contracts.Runs;

public sealed record RunStep(
    string Id,
    string Name,
    string Type,
    string Status,
    string? StartedAt,
    string? CompletedAt,
    string? AgentName,
    string? Output,
    string? Error,
    PolicyDecision? PolicyDecision,
    PromptSnapshot? PromptSnapshot);
