using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public interface IServiceTaskExecutor
{
    Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken);
}

public sealed record AgentTaskOutcome(
    bool Succeeded,
    string? Output,
    string? FailureReason,
    IReadOnlyDictionary<string, string>? Artifacts = null);
