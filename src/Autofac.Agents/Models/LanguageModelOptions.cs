namespace Autofac.Agents.Models;

public sealed class LanguageModelOptions
{
    public const string Section = "Anthropic";
    public const string DefaultApiBaseUrl = "https://api.anthropic.com/";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Selects the language-model backend: <c>anthropic</c>, <c>mock</c>, or empty/<c>auto</c>
    /// (default). <c>auto</c> resolves to Anthropic when an <see cref="ApiKey"/> is configured,
    /// otherwise a null client. Set to <c>mock</c> for tokenless, deterministic runs in demos
    /// and CI.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public string Model { get; set; } = "claude-sonnet-4-6";

    public int MaxTokens { get; set; } = 4096;

    public int MaxToolIterations { get; set; } = 10;

    /// <summary>Cost in USD per million input tokens. Used for the agent.model.cost_usd metric.</summary>
    public decimal InputCostPerMillionTokens { get; set; } = 3.00m;

    /// <summary>Cost in USD per million output tokens. Used for the agent.model.cost_usd metric.</summary>
    public decimal OutputCostPerMillionTokens { get; set; } = 15.00m;

    /// <summary>
    /// Cost in USD per million cache-read input tokens. Anthropic prices cache reads at
    /// roughly 0.1x the base input rate.
    /// </summary>
    public decimal CacheReadCostPerMillionTokens { get; set; } = 0.30m;

    /// <summary>
    /// Cost in USD per million cache-write (creation) input tokens. Anthropic prices the
    /// 5-minute cache write at roughly 1.25x the base input rate.
    /// </summary>
    public decimal CacheWriteCostPerMillionTokens { get; set; } = 3.75m;

    /// <summary>
    /// When true, the system prompt and tool definitions are marked for Anthropic prompt
    /// caching so they are not re-billed at full rate on every tool-loop iteration and step.
    /// </summary>
    public bool EnablePromptCaching { get; set; } = true;

    /// <summary>Per-request HTTP timeout, in seconds, applied to the Anthropic HTTP client.</summary>
    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// Maximum number of automatic retries on transient failures (HTTP 429, 529 overloaded,
    /// and 5xx). Set to 0 to disable retries. Non-transient 4xx responses are never retried.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for exponential backoff between retries, in milliseconds.</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Upper bound for a single backoff delay, in milliseconds.</summary>
    public int RetryMaxDelayMs { get; set; } = 20_000;
}
