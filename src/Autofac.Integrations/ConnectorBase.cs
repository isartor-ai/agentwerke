using System.Diagnostics;
using Autofac.AgentSecOps;
using Autofac.Application.Observability;

namespace Autofac.Integrations;

public abstract class ConnectorBase : IConnector
{
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly IAuditRepository _auditRepository;
    private readonly IWorkflowMetrics _metrics;
    private readonly ICorrelationContext _correlationContext;
    private readonly IWorkflowTracer _tracer;

    protected ConnectorBase(
        IPolicyEvaluationService policyEvaluationService,
        IAuditRepository auditRepository,
        IWorkflowMetrics metrics,
        ICorrelationContext correlationContext,
        IWorkflowTracer tracer)
    {
        _policyEvaluationService = policyEvaluationService;
        _auditRepository = auditRepository;
        _metrics = metrics;
        _correlationContext = correlationContext;
        _tracer = tracer;
    }

    public abstract string ConnectorId { get; }

    public abstract string DisplayName { get; }

    public abstract bool Enabled { get; }

    public abstract IReadOnlyList<string> SupportedOperations { get; }

    public async Task<ConnectorExecutionResult> ExecuteAsync(
        ConnectorExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        using var span = _tracer.StartSpan($"connector.{ConnectorId}.{request.Operation}");
        span.SetTag("connector.id", ConnectorId);
        span.SetTag("connector.operation", request.Operation);
        span.SetTag("autofac.run_id", request.RunId);
        span.SetTag("autofac.correlation_id", _correlationContext.CorrelationId);

        if (!Enabled)
        {
            var disabled = new ConnectorExecutionResult(
                Succeeded: false,
                Status: "disabled",
                Summary: $"{DisplayName} is disabled.",
                FailureReason: $"{DisplayName} is disabled.");

            await RecordAsync(request, disabled, started.Elapsed.TotalMilliseconds, cancellationToken);
            return disabled;
        }

        var decision = _policyEvaluationService.Evaluate(new PolicyEvaluationRequest(
            AgentName: ConnectorId,
            Action: $"{ConnectorId}.{request.Operation}",
            Environment: request.Environment,
            PurposeType: request.PurposeType,
            PolicyTag: request.PolicyTag,
            RequiresEvidence: request.RequiresEvidence,
            Attempt: 1));

        span.SetTag("policy.decision", decision.Kind);
        span.SetTag("policy.id", decision.PolicyId);

        if (!string.Equals(decision.Kind, "allow", StringComparison.Ordinal))
        {
            var blocked = new ConnectorExecutionResult(
                Succeeded: false,
                Status: decision.Kind,
                Summary: decision.Rationale,
                PolicyDecision: decision,
                FailureReason: decision.Rationale);

            await RecordAsync(request, blocked, started.Elapsed.TotalMilliseconds, cancellationToken);
            return blocked;
        }

        try
        {
            var result = await ExecuteAllowedAsync(request, cancellationToken);
            var completed = result with { PolicyDecision = decision };
            span.SetTag("connector.status", completed.Status);
            await RecordAsync(request, completed, started.Elapsed.TotalMilliseconds, cancellationToken);
            return completed;
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            var failed = new ConnectorExecutionResult(
                Succeeded: false,
                Status: "failed",
                Summary: ex.Message,
                PolicyDecision: decision,
                FailureReason: ex.Message);
            await RecordAsync(request, failed, started.Elapsed.TotalMilliseconds, cancellationToken);
            return failed;
        }
    }

    protected abstract Task<ConnectorExecutionResult> ExecuteAllowedAsync(
        ConnectorExecutionRequest request,
        CancellationToken cancellationToken);

    private async Task RecordAsync(
        ConnectorExecutionRequest request,
        ConnectorExecutionResult result,
        double durationMs,
        CancellationToken cancellationToken)
    {
        _metrics.ConnectorInvoked(ConnectorId, request.Operation, durationMs, result.Succeeded);

        await _auditRepository.AddAsync(new Domain.Persistence.AuditRecord
        {
            Id = $"audit_{Guid.NewGuid():N}",
            RunId = request.RunId,
            CorrelationId = _correlationContext.CorrelationId,
            ActorType = "connector",
            Actor = ConnectorId,
            Action = $"connector.{ConnectorId}.{request.Operation}",
            ResourceType = "connector",
            ResourceId = ConnectorId,
            Outcome = result.Status,
            Details = result.Summary,
            Timestamp = DateTimeOffset.UtcNow.ToString("o")
        }, cancellationToken);

        await _auditRepository.SaveChangesAsync(cancellationToken);
    }
}
