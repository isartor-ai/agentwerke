using System.Collections.Concurrent;

namespace Agentwerke.Agents.Models;

/// <summary>
/// Tracks cumulative model spend (USD + tokens) per run and reports when a run has reached
/// its configured budget, so further model calls can be halted before more is spent (#175).
/// A limit of <c>&lt;= 0</c> means "unlimited".
/// </summary>
public interface IModelRunBudget
{
    BudgetStatus Evaluate(string runId, decimal maxCostUsd, long maxTokens);

    void Record(string runId, decimal costUsd, long tokens);
}

public sealed record BudgetStatus(
    bool Exceeded,
    decimal AccumulatedCostUsd,
    long AccumulatedTokens,
    string? Reason);

public sealed class ModelRunBudget : IModelRunBudget
{
    private readonly ConcurrentDictionary<string, Totals> _totals = new(StringComparer.Ordinal);

    public BudgetStatus Evaluate(string runId, decimal maxCostUsd, long maxTokens)
    {
        var (cost, tokens) = Read(runId);

        if (maxCostUsd > 0 && cost >= maxCostUsd)
        {
            return new BudgetStatus(true, cost, tokens,
                $"Run cost ${cost:0.0000} has reached the budget of ${maxCostUsd:0.0000}.");
        }

        if (maxTokens > 0 && tokens >= maxTokens)
        {
            return new BudgetStatus(true, cost, tokens,
                $"Run has used {tokens} tokens, reaching the budget of {maxTokens}.");
        }

        return new BudgetStatus(false, cost, tokens, null);
    }

    public void Record(string runId, decimal costUsd, long tokens)
    {
        var totals = _totals.GetOrAdd(runId, static _ => new Totals());
        lock (totals)
        {
            totals.CostUsd += costUsd;
            totals.Tokens += tokens;
        }
    }

    private (decimal Cost, long Tokens) Read(string runId)
    {
        if (_totals.TryGetValue(runId, out var totals))
        {
            lock (totals)
            {
                return (totals.CostUsd, totals.Tokens);
            }
        }

        return (0m, 0L);
    }

    private sealed class Totals
    {
        public decimal CostUsd;
        public long Tokens;
    }
}
