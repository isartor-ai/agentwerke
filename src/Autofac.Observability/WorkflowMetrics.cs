using Autofac.Application.Observability;
using System.Diagnostics.Metrics;

namespace Autofac.Observability;

public sealed class WorkflowMetrics : IWorkflowMetrics, IDisposable
{
    public const string MeterName = "Autofac.Workflows";

    private readonly Meter _meter;
    private readonly Counter<long> _runsStarted;
    private readonly Counter<long> _runsCompleted;
    private readonly Counter<long> _runsFailed;
    private readonly Histogram<double> _runDurationMs;
    private readonly Histogram<double> _stepDurationMs;
    private readonly Counter<long> _stepsSucceeded;
    private readonly Counter<long> _stepsFailed;
    private readonly Counter<long> _approvalsCreated;
    private readonly Counter<long> _approvalsDecided;
    private readonly Counter<long> _webhooksReceived;
    private readonly Counter<long> _connectorsSucceeded;
    private readonly Counter<long> _connectorsFailed;
    private readonly Histogram<double> _connectorDurationMs;

    public WorkflowMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _runsStarted = _meter.CreateCounter<long>("workflow.runs.started", description: "Total workflow runs started.");
        _runsCompleted = _meter.CreateCounter<long>("workflow.runs.completed", description: "Workflow runs completed successfully.");
        _runsFailed = _meter.CreateCounter<long>("workflow.runs.failed", description: "Workflow runs that ended in failure.");
        _runDurationMs = _meter.CreateHistogram<double>("workflow.run.duration_ms", unit: "ms", description: "End-to-end workflow run duration.");

        _stepsSucceeded = _meter.CreateCounter<long>("workflow.steps.succeeded", description: "Service task steps completed successfully.");
        _stepsFailed = _meter.CreateCounter<long>("workflow.steps.failed", description: "Service task steps that failed.");
        _stepDurationMs = _meter.CreateHistogram<double>("workflow.step.duration_ms", unit: "ms", description: "Service task step execution duration.");

        _approvalsCreated = _meter.CreateCounter<long>("workflow.approvals.created", description: "Approval requests created.");
        _approvalsDecided = _meter.CreateCounter<long>("workflow.approvals.decided", description: "Approval decisions recorded.");

        _webhooksReceived = _meter.CreateCounter<long>("workflow.webhooks.received", description: "Inbound webhooks received.");
        _connectorsSucceeded = _meter.CreateCounter<long>("workflow.connectors.succeeded", description: "Connector invocations completed successfully.");
        _connectorsFailed = _meter.CreateCounter<long>("workflow.connectors.failed", description: "Connector invocations that failed or were blocked.");
        _connectorDurationMs = _meter.CreateHistogram<double>("workflow.connector.duration_ms", unit: "ms", description: "Connector invocation duration.");
    }

    public void RunStarted(string workflowId, string workflowName)
    {
        _runsStarted.Add(1,
            new KeyValuePair<string, object?>("workflow.id", workflowId),
            new KeyValuePair<string, object?>("workflow.name", workflowName));
    }

    public void RunCompleted(string workflowId, string workflowName, double durationMs)
    {
        _runsCompleted.Add(1,
            new KeyValuePair<string, object?>("workflow.id", workflowId),
            new KeyValuePair<string, object?>("workflow.name", workflowName));
        _runDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("workflow.id", workflowId),
            new KeyValuePair<string, object?>("workflow.name", workflowName));
    }

    public void RunFailed(string workflowId, string workflowName, string reason)
    {
        _runsFailed.Add(1,
            new KeyValuePair<string, object?>("workflow.id", workflowId),
            new KeyValuePair<string, object?>("workflow.name", workflowName),
            new KeyValuePair<string, object?>("failure.reason", reason));
    }

    public void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("step.type", stepType),
            new KeyValuePair<string, object?>("agent.name", agentName)
        };

        if (succeeded)
            _stepsSucceeded.Add(1, tags);
        else
            _stepsFailed.Add(1, tags);

        _stepDurationMs.Record(durationMs, tags);
    }

    public void ApprovalCreated(string riskLevel)
    {
        _approvalsCreated.Add(1, new KeyValuePair<string, object?>("risk.level", riskLevel));
    }

    public void ApprovalDecided(string decision, string riskLevel)
    {
        _approvalsDecided.Add(1,
            new KeyValuePair<string, object?>("decision", decision),
            new KeyValuePair<string, object?>("risk.level", riskLevel));
    }

    public void WebhookReceived(string source, bool triggered)
    {
        _webhooksReceived.Add(1,
            new KeyValuePair<string, object?>("webhook.source", source),
            new KeyValuePair<string, object?>("workflow.triggered", triggered));
    }

    public void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("connector.id", connectorId),
            new KeyValuePair<string, object?>("connector.operation", operation)
        };

        if (succeeded)
        {
            _connectorsSucceeded.Add(1, tags);
        }
        else
        {
            _connectorsFailed.Add(1, tags);
        }

        _connectorDurationMs.Record(durationMs, tags);
    }

    public void Dispose() => _meter.Dispose();
}
