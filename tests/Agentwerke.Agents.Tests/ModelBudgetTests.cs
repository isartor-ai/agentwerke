using Agentwerke.Agents.Models;

namespace Agentwerke.Agents.Tests;

public sealed class ModelBudgetTests
{
    [Fact]
    public void CalculateCostUsd_PricesEachTokenClass()
    {
        var options = new LanguageModelOptions
        {
            InputCostPerMillionTokens = 3.00m,
            OutputCostPerMillionTokens = 15.00m,
            CacheReadCostPerMillionTokens = 0.30m,
            CacheWriteCostPerMillionTokens = 3.75m,
        };

        var usage = new LanguageModelTokenUsage(
            InputTokens: 1_000_000,
            OutputTokens: 1_000_000,
            CacheCreationInputTokens: 1_000_000,
            CacheReadInputTokens: 1_000_000);

        var cost = ModelCostCalculator.CalculateCostUsd(usage, options);

        Assert.Equal(3.00m + 15.00m + 3.75m + 0.30m, cost);
        Assert.Equal(4_000_000L, ModelCostCalculator.TotalTokens(usage));
    }

    [Fact]
    public void Evaluate_FreshRun_IsNotExceeded()
    {
        var budget = new ModelRunBudget();

        var status = budget.Evaluate("run-1", maxCostUsd: 10m, maxTokens: 1000);

        Assert.False(status.Exceeded);
        Assert.Equal(0m, status.AccumulatedCostUsd);
        Assert.Equal(0L, status.AccumulatedTokens);
    }

    [Fact]
    public void Evaluate_ZeroLimits_AreUnlimited()
    {
        var budget = new ModelRunBudget();
        budget.Record("run-1", costUsd: 999m, tokens: 9_999_999);

        var status = budget.Evaluate("run-1", maxCostUsd: 0m, maxTokens: 0);

        Assert.False(status.Exceeded);
    }

    [Fact]
    public void Evaluate_ExceedsByCost_OnceAccumulatedReachesBudget()
    {
        var budget = new ModelRunBudget();
        budget.Record("run-1", costUsd: 4m, tokens: 100);

        Assert.False(budget.Evaluate("run-1", maxCostUsd: 5m, maxTokens: 0).Exceeded);

        budget.Record("run-1", costUsd: 1m, tokens: 100); // total 5.00 == budget

        var status = budget.Evaluate("run-1", maxCostUsd: 5m, maxTokens: 0);
        Assert.True(status.Exceeded);
        Assert.Equal(5m, status.AccumulatedCostUsd);
        Assert.Contains("budget", status.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ExceedsByTokens()
    {
        var budget = new ModelRunBudget();
        budget.Record("run-1", costUsd: 0m, tokens: 500);

        var status = budget.Evaluate("run-1", maxCostUsd: 0m, maxTokens: 500);

        Assert.True(status.Exceeded);
        Assert.Equal(500L, status.AccumulatedTokens);
    }

    [Fact]
    public void Record_KeepsRunsIsolated()
    {
        var budget = new ModelRunBudget();
        budget.Record("run-1", costUsd: 100m, tokens: 100);

        Assert.False(budget.Evaluate("run-2", maxCostUsd: 1m, maxTokens: 1).Exceeded);
        Assert.True(budget.Evaluate("run-1", maxCostUsd: 1m, maxTokens: 0).Exceeded);
    }
}
