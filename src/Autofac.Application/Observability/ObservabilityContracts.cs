using Autofac.Domain.Persistence;

namespace Autofac.Application.Observability;

/// <summary>
/// Carries the correlation ID for the current unit of work (HTTP request or background job).
/// Set by middleware at the request boundary; injected into services that need to tag their output.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

/// <summary>Mutable implementation populated by middleware.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Persists immutable audit records for user and agent actions.
/// Implemented in Autofac.Infrastructure.
/// </summary>
public interface IAuditRepository
{
    Task AddAsync(AuditRecord record, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Records workflow execution metrics. Implemented in Autofac.Observability (singleton backed by System.Diagnostics.Metrics).
/// </summary>
public interface IWorkflowMetrics
{
    void RunStarted(string workflowId, string workflowName);
    void RunCompleted(string workflowId, string workflowName, double durationMs);
    void RunFailed(string workflowId, string workflowName, string reason);
    void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded);
    void ApprovalCreated(string riskLevel);
    void ApprovalDecided(string decision, string riskLevel);
    void WebhookReceived(string source, bool triggered);
    void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded);
}
