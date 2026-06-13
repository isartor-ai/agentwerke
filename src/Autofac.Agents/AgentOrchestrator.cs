using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;

namespace Autofac.Agents;

/// <summary>
/// Bridges BPMN service-task execution to the agent layer.
/// MVP implementation simulates execution; a future milestone will dispatch
/// to real agent runtimes via the Autofac agent protocol.
/// </summary>
public sealed class AgentOrchestrator : IServiceTaskExecutor
{
    public async Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata;
        if (metadata is null)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: "Service task is missing autofac:agentTask metadata.");
        }

        var request = new AgentExecutionRequest(
            RunId: runId,
            StepId: stepId,
            NodeId: node.Id,
            NodeName: node.Name,
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            RequiresEvidence: metadata.RequiresEvidence,
            Attempt: attempt);

        return await SimulateExecutionAsync(request, metadata, cancellationToken);
    }

    private static Task<AgentTaskOutcome> SimulateExecutionAsync(
        AgentExecutionRequest request,
        AutofacTaskMetadata metadata,
        CancellationToken cancellationToken)
    {
        // Honour the BPMN test-scenario flags so existing BPMN fixtures keep working.
        var att = request.Attempt;
        if (att <= metadata.FailUntilAttempt)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: $"Simulated failure on attempt {att} (failUntilAttempt={metadata.FailUntilAttempt})"));
        }

        var profile = AgentRegistry.Find(request.AgentName);
        var output = BuildSimulatedOutput(request, profile);

        return Task.FromResult(new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null));
    }

    private static string BuildSimulatedOutput(AgentExecutionRequest request, AgentProfile? profile)
    {
        var skillName = profile?.Skills
            .FirstOrDefault(s => s.SupportedActions.Contains(request.Action, StringComparer.OrdinalIgnoreCase))
            ?.Name ?? request.Action;

        return $"""
            agent: {request.AgentName}
            skill: {skillName}
            action: {request.Action}
            environment: {request.Environment ?? "unspecified"}
            purpose: {request.PurposeType}
            policy: {request.PolicyTag}
            attempt: {request.Attempt}
            status: completed
            timestamp: {DateTimeOffset.UtcNow:o}
            """;
    }
}
