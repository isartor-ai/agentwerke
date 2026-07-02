namespace Autofac.Api.Contracts.Runs;

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
    string CreatedAt);
