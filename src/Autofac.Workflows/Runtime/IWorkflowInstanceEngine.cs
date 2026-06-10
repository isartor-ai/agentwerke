using Autofac.Workflows.Bpmn;

namespace Autofac.Workflows.Runtime;

public interface IWorkflowInstanceEngine
{
    Task<WorkflowExecutionState> StartAsync(
        Guid workflowDefinitionId,
        BpmnWorkflowDefinition definition,
        string? initiator,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> ResumeAsync(
        Guid runId,
        BpmnWorkflowDefinition definition,
        string? approvedBy,
        CancellationToken cancellationToken);

    Task<WorkflowExecutionState> RecoverAsync(
        Guid runId,
        BpmnWorkflowDefinition definition,
        CancellationToken cancellationToken);
}