using Autofac.Agents.Skills;
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
    private readonly ISkillRepository _skillRepository;

    public AgentOrchestrator(ISkillRepository skillRepository)
    {
        _skillRepository = skillRepository;
    }

    public Task<AgentTaskOutcome> ExecuteAsync(
        string runId,
        string stepId,
        BpmnNodeDefinition node,
        int attempt,
        CancellationToken cancellationToken)
    {
        var metadata = node.Metadata;
        if (metadata is null)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: "Service task is missing autofac:agentTask metadata."));
        }

        // Honour the BPMN test-scenario flags so existing fixtures keep working.
        if (attempt <= metadata.FailUntilAttempt)
        {
            return Task.FromResult(new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: $"Simulated failure on attempt {attempt} (failUntilAttempt={metadata.FailUntilAttempt})"));
        }

        var profile = AgentRegistry.Find(metadata.Agent);
        var matchedSkillRef = ResolveSkillRef(profile, metadata.Action);
        var skillManifest = matchedSkillRef?.SkillManifestId is not null
            ? _skillRepository.FindById(matchedSkillRef.SkillManifestId)
            : null;

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

        var output = BuildExecutionOutput(request, profile, matchedSkillRef, skillManifest);

        return Task.FromResult(new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null));
    }

    private static AgentSkillRef? ResolveSkillRef(AgentProfile? profile, string action)
    {
        if (profile is null)
        {
            return null;
        }

        return profile.Skills.FirstOrDefault(s =>
            s.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            ?? profile.Skills.FirstOrDefault();
    }

    private static string BuildExecutionOutput(
        AgentExecutionRequest request,
        AgentProfile? profile,
        AgentSkillRef? skillRef,
        SkillManifest? manifest)
    {
        var skillLine = manifest is not null
            ? $"{manifest.Name} (id={manifest.SkillId} fingerprint={manifest.Fingerprint[..12]}…)"
            : skillRef?.Name ?? request.Action;

        var contextSection = manifest is not null
            ? $"""

              skill_context:
                id: {manifest.SkillId}
                name: {manifest.Name}
                fingerprint: {manifest.Fingerprint}
                source: {manifest.FilePath}
              """
            : string.Empty;

        return $"""
            agent: {request.AgentName}
            skill: {skillLine}
            action: {request.Action}
            environment: {request.Environment ?? "unspecified"}
            purpose: {request.PurposeType}
            policy: {request.PolicyTag}
            attempt: {request.Attempt}
            status: completed
            timestamp: {DateTimeOffset.UtcNow:o}{contextSection}
            """;
    }
}
