namespace Agentwerke.Agents.Models;

/// <summary>
/// Prices model token usage in USD from the per-million rates in
/// <see cref="LanguageModelOptions"/>. Uncached input, output, and the two prompt-cache
/// token classes are priced separately so the cost reflects caching savings (and the write
/// premium). Pure and deterministic (#175).
/// </summary>
public static class ModelCostCalculator
{
    public static decimal CalculateCostUsd(LanguageModelTokenUsage usage, LanguageModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(options);

        return (usage.InputTokens * options.InputCostPerMillionTokens
                + usage.OutputTokens * options.OutputCostPerMillionTokens
                + usage.CacheReadInputTokens * options.CacheReadCostPerMillionTokens
                + usage.CacheCreationInputTokens * options.CacheWriteCostPerMillionTokens)
               / 1_000_000m;
    }

    /// <summary>Total billable tokens across all classes, used for token-budget accounting.</summary>
    public static long TotalTokens(LanguageModelTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return (long)usage.InputTokens
               + usage.OutputTokens
               + usage.CacheReadInputTokens
               + usage.CacheCreationInputTokens;
    }
}
