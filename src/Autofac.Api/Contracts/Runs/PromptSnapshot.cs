using System.Collections.Generic;

namespace Autofac.Api.Contracts.Runs;

public sealed record PromptSnapshot(
    string FinalPrompt,
    string RenderedAt,
    IReadOnlyList<PromptSection> Sections,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<string>? MissingVariables = null)
{
    public IReadOnlyList<string> MissingVariables { get; init; } = MissingVariables ?? [];
}

public sealed record PromptSection(
    string Name,
    string Content,
    string Source);
