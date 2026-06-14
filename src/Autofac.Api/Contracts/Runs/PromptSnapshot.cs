using System.Collections.Generic;

namespace Autofac.Api.Contracts.Runs;

public sealed record PromptSnapshot(
    string FinalPrompt,
    string RenderedAt,
    IReadOnlyList<PromptSection> Sections,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> SourceFiles);

public sealed record PromptSection(
    string Name,
    string Content,
    string Source);
