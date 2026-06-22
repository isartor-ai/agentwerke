namespace Autofac.Agents.Models;

public sealed class LanguageModelOptions
{
    public const string Section = "Anthropic";
    public const string DefaultApiBaseUrl = "https://api.anthropic.com/";

    public string? ApiKey { get; set; }

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public string Model { get; set; } = "claude-sonnet-4-6";

    public int MaxTokens { get; set; } = 4096;

    public int MaxToolIterations { get; set; } = 10;

    /// <summary>Cost in USD per million input tokens. Used for the agent.model.cost_usd metric.</summary>
    public decimal InputCostPerMillionTokens { get; set; } = 3.00m;

    /// <summary>Cost in USD per million output tokens. Used for the agent.model.cost_usd metric.</summary>
    public decimal OutputCostPerMillionTokens { get; set; } = 15.00m;
}
