using Autofac.Application.Workflows;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;

namespace Autofac.Infrastructure.Workflows;

public sealed class WorkflowRunnerAdapter : IWorkflowRunner
{
    private const string WaitingUserStatus = "waiting_user";

    private readonly IBpmnWorkflowValidator _validator;
    private readonly IWorkflowInstanceEngine _engine;

    public WorkflowRunnerAdapter(IBpmnWorkflowValidator validator, IWorkflowInstanceEngine engine)
    {
        _validator = validator;
        _engine = engine;
    }

    public async Task<WorkflowRunnerResult> StartAsync(
        string workflowDefinitionId,
        string bpmnXml,
        string? initiator,
        CancellationToken cancellationToken)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.StartAsync(workflowDefinitionId, definition, initiator, cancellationToken);
        return ToResult(state, definition);
    }

    public async Task<WorkflowRunnerResult> ResumeAsync(
        string runId,
        string bpmnXml,
        string? approvedBy,
        CancellationToken cancellationToken)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.ResumeAsync(runId, definition, approvedBy, cancellationToken);
        return ToResult(state, definition);
    }

    public async Task<WorkflowRunnerResult> RecoverAsync(
        string runId,
        string bpmnXml,
        CancellationToken cancellationToken)
    {
        var definition = ParseOrThrow(bpmnXml);
        var state = await _engine.RecoverAsync(runId, definition, cancellationToken);
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
            var node = definition.Nodes.FirstOrDefault(n =>
                string.Equals(n.Id, state.WaitingOnNodeId, StringComparison.Ordinal));

            waitingApproval = new WaitingApprovalInfo(
                NodeId: state.WaitingOnNodeId,
                NodeName: node?.Name,
                PurposeType: node?.ApprovalMetadata?.PurposeType ?? string.Empty,
                PolicyTag: node?.ApprovalMetadata?.PolicyTag ?? string.Empty);
        }

        return new WorkflowRunnerResult(state.RunId, state.Status, waitingApproval);
    }
}
