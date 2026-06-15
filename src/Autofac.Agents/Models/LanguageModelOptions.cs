namespace Autofac.Agents.Models;

public sealed class LanguageModelOptions
{
    public const string Section = "Anthropic";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "claude-sonnet-4-6";

    public int MaxTokens { get; set; } = 4096;

    public int MaxToolIterations { get; set; } = 10;
}
