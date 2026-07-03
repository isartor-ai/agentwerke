using Agentwerke.Application.Workflows;
using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Infrastructure.Workflows;

public sealed class WorkflowRunnerAdapter : IWorkflowRunner
{
    private const string WaitingUserStatus = "waiting_user";

    private readonly IBpmnWorkflowValidator _validator;
    private readonly IWorkflowEngineAdapter _engine;

    public WorkflowRunnerAdapter(IBpmnWorkflowValidator validator, IWorkflowEngineAdapter engine)
    {
        _validator = validator;
        _engine = engine;
    }

    public async Task<WorkflowRunnerResult> StartAsync(
        string workflowDefinitionId,
        string bpmnXml,
        string? initiator,
        CancellationToken cancellationToken,
        string? correlationId = null,
        string? existingRunId = null)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.StartAsync(
            new WorkflowEngineStartRequest(workflowDefinitionId, definition, initiator, correlationId, existingRunId),
            cancellationToken);
        return ToResult(state, definition);
    }

    public async Task<WorkflowRunnerResult> ResumeAsync(
        string runId,
        string bpmnXml,
        string? approvedBy,
        IReadOnlyDictionary<string, string>? externalPayload,
        string? externalCorrelationKey,
        string? resumedBy,
        CancellationToken cancellationToken)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.ResumeAsync(
            new WorkflowEngineResumeRequest(runId, definition, approvedBy, externalCorrelationKey, externalPayload, resumedBy),
            cancellationToken);
        return ToResult(state, definition);
    }

    public async Task<WorkflowRunnerResult> RecoverAsync(
        string runId,
        string bpmnXml,
        CancellationToken cancellationToken)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.RecoverAsync(
            new WorkflowEngineRecoverRequest(runId, definition),
            cancellationToken);
        return ToResult(state, definition);
    }

    private BpmnWorkflowDefinition ParseOrThrow(string bpmnXml)
    {
        var result = _validator.Validate(bpmnXml);
        if (!result.IsValid || result.Definition is null)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Workflow BPMN is invalid and cannot be executed: {errors}");
        }

        return result.Definition;
    }

    private static WorkflowRunnerResult ToResult(WorkflowExecutionState state, BpmnWorkflowDefinition definition)
    {
        WaitingApprovalInfo? waitingApproval = null;

        if (string.Equals(state.Status, WaitingUserStatus, StringComparison.Ordinal) &&
            state.WaitingOnNodeId is not null)
        {
            var nodeIndex = definition.Nodes
                .Select((n, i) => (node: n, index: i))
                .FirstOrDefault(x => string.Equals(x.node.Id, state.WaitingOnNodeId, StringComparison.Ordinal));

            var node = nodeIndex.node;

            var precedingNode = definition.FindPrecedingServiceTaskNode(state.WaitingOnNodeId);
            var riskLevel = InferRiskLevel(node?.ApprovalMetadata?.PolicyTag);
            var riskScore = RiskLevelToScore(riskLevel);
            var riskFactors = BuildRiskFactors(node?.ApprovalMetadata?.PolicyTag, node?.ApprovalMetadata?.PurposeType);
            var affectedSystems = BuildAffectedSystems(definition, nodeIndex.index);

            waitingApproval = new WaitingApprovalInfo(
                NodeId: state.WaitingOnNodeId,
                NodeName: node?.Name,
                PurposeType: node?.ApprovalMetadata?.PurposeType ?? string.Empty,
                PolicyTag: node?.ApprovalMetadata?.PolicyTag ?? string.Empty,
                AgentName: precedingNode?.Metadata?.Agent,
                RiskLevel: riskLevel,
                RiskScore: riskScore,
                RiskFactors: riskFactors,
                AffectedSystems: affectedSystems,
                ArtifactName: state.WaitingApprovalArtifactName);
        }

        return new WorkflowRunnerResult(
            state.RunId,
            state.Status,
            waitingApproval,
            state.TimerDueAt,
            state.WaitingExternalCorrelationKey,
            state.WaitingExternalMessageName);
    }

    private static string InferRiskLevel(string? policyTag)
    {
        if (string.IsNullOrWhiteSpace(policyTag))
        {
            return "low";
        }

        var tag = policyTag.ToLowerInvariant();
        if (tag.Contains("critical") || tag.Contains("prod") || tag.Contains("secret") || tag.Contains("credential"))
        {
            return "high";
        }

        if (tag.Contains("deploy") || tag.Contains("access") || tag.Contains("permission") || tag.Contains("infra"))
        {
            return "medium";
        }

        return "low";
    }

    private static int RiskLevelToScore(string riskLevel) => riskLevel switch
    {
        "high" => 75,
        "medium" => 45,
        _ => 15
    };

    private static IReadOnlyList<string> BuildRiskFactors(string? policyTag, string? purposeType)
    {
        var factors = new List<string>();

        if (!string.IsNullOrWhiteSpace(policyTag))
        {
            factors.Add($"Policy tag: {policyTag}");
        }

        if (!string.IsNullOrWhiteSpace(purposeType))
        {
            factors.Add($"Purpose: {purposeType}");
        }

        return factors.AsReadOnly();
    }

    private static IReadOnlyList<string> BuildAffectedSystems(BpmnWorkflowDefinition definition, int userTaskIndex)
    {
        var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < userTaskIndex; i++)
        {
            var node = definition.Nodes[i];
            if (node.Metadata?.Environment is not null)
            {
                systems.Add(node.Metadata.Environment);
            }
        }

        return systems.ToList().AsReadOnly();
    }
}
