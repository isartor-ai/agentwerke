using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowInstanceEngine
{
    Task<WorkflowExecutionState> StartAsync(
        string workflowDefinitionId,
        BpmnWorkflowDefinition definition,
        string? initiator,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> ResumeAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        string? approvedBy,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> RecoverAsync(
        string runId,
        BpmnWorkflowDefinition definition,
        CancellationToken cancellationToken);
}