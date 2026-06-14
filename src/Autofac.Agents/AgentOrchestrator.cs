using Autofac.Agents.Skills;
using Autofac.Sandboxes;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Autofac.Agents;

/// <summary>
/// Bridges BPMN service-task execution to the agent layer.
/// When <see cref="SandboxOptions.Enabled"/> is true, dispatches to
/// <see cref="ISandboxExecutor"/> for Docker-isolated execution.
/// </summary>
public sealed class AgentOrchestrator : IServiceTaskExecutor
{
    private readonly ISkillRepository _skillRepository;
    private readonly ISandboxExecutor _sandbox;
    private readonly SandboxOptions _sandboxOptions;

    public AgentOrchestrator(
        ISkillRepository skillRepository,
        ISandboxExecutor sandbox,
        IOptions<SandboxOptions> sandboxOptions)
    {
        _skillRepository = skillRepository;
        _sandbox = sandbox;
        _sandboxOptions = sandboxOptions.Value;
    }

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

        // Honour the BPMN test-scenario flags so existing fixtures keep working.
        if (attempt <= metadata.FailUntilAttempt)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: null,
                FailureReason: $"Simulated failure on attempt {attempt} (failUntilAttempt={metadata.FailUntilAttempt})");
        }

        var profile = AgentRegistry.Find(metadata.Agent);
        var matchedSkillRef = ResolveSkillRef(profile, metadata.Action);
        var skillManifest = matchedSkillRef?.SkillManifestId is not null
            ? _skillRepository.FindById(matchedSkillRef.SkillManifestId)
            : null;

        if (_sandboxOptions.Enabled)
        {
            return await RunInSandboxAsync(runId, stepId, metadata, attempt, skillManifest, cancellationToken);
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

        var output = BuildSimulatedOutput(request, profile, matchedSkillRef, skillManifest);

        return new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null);
    }

    private async Task<AgentTaskOutcome> RunInSandboxAsync(
        string runId,
        string stepId,
        AutofacTaskMetadata metadata,
        int attempt,
        SkillManifest? skillManifest,
        CancellationToken cancellationToken)
    {
        var sandboxRequest = new SandboxExecutionRequest(
            RunId: runId,
            StepId: stepId,
            AgentName: metadata.Agent,
            Action: metadata.Action,
            Environment: metadata.Environment,
            PurposeType: metadata.PurposeType,
            PolicyTag: metadata.PolicyTag,
            Attempt: attempt);

        var result = await _sandbox.ExecuteAsync(sandboxRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return new AgentTaskOutcome(
                Succeeded: false,
                Output: result.Logs,
                FailureReason: result.FailureReason,
                Artifacts: result.Artifacts);
        }

        var output = BuildSandboxOutput(sandboxRequest, result, skillManifest);

        return new AgentTaskOutcome(
            Succeeded: true,
            Output: output,
            FailureReason: null,
            Artifacts: result.Artifacts);
    }

    private static AgentSkillRef? ResolveSkillRef(AgentProfile? profile, string action)
    {
        if (profile is null) return null;

        return profile.Skills.FirstOrDefault(s =>
            s.SupportedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            ?? profile.Skills.FirstOrDefault();
    }

    private static string BuildSimulatedOutput(
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
            mode: simulated
            timestamp: {DateTimeOffset.UtcNow:o}{contextSection}
            """;
    }

    private static string BuildSandboxOutput(
        SandboxExecutionRequest request,
        SandboxExecutionResult result,
        SkillManifest? manifest)
    {
        var skillContext = manifest is not null
            ? $"""

              skill_context:
                id: {manifest.SkillId}
                name: {manifest.Name}
                fingerprint: {manifest.Fingerprint}
              """
            : string.Empty;

        return $"""
            agent: {request.AgentName}
            action: {request.Action}
            environment: {request.Environment ?? "unspecified"}
            purpose: {request.PurposeType}
            policy: {request.PolicyTag}
            attempt: {request.Attempt}
            status: completed
            mode: sandbox
            exit_code: {result.ExitCode}
            duration_ms: {result.Duration.TotalMilliseconds:F0}
            artifact_count: {result.Artifacts.Count}
            timestamp: {DateTimeOffset.UtcNow:o}{skillContext}
            logs: |
            {IndentLogs(result.Logs)}
            """;
    }

    private static string IndentLogs(string logs)
    {
        if (string.IsNullOrWhiteSpace(logs)) return "  (no output)";
        return string.Join('\n', logs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => $"  {l}"));
    }
}
