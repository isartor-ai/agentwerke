using Autofac.Infrastructure.Workflows;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;

namespace Autofac.Workflows.Tests;

public sealed class WorkflowRunnerAdapterTests
{
    [Fact]
    public async Task StartAsync_ForwardsParsedDefinitionToWorkflowEngineAdapter()
    {
        var engine = new RecordingWorkflowEngineAdapter();
        var runner = new WorkflowRunnerAdapter(new PassThroughValidator(), engine);

        var result = await runner.StartAsync(
            workflowDefinitionId: "wf-123",
            bpmnXml: """
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
                  <bpmn:process id="Process_1" name="Adapter Flow">
                    <bpmn:startEvent id="Start" />
                    <bpmn:serviceTask id="ServiceA" name="Do Work">
                      <bpmn:extensionElements>
                        <autofac:agentTask agent="implementation-agent"
                                           action="code.generate"
                                           purposeType="implementation"
                                           policyTag="repo-change" />
                      </bpmn:extensionElements>
                    </bpmn:serviceTask>
                  </bpmn:process>
                </bpmn:definitions>
                """,
            initiator: "api-user",
            cancellationToken: CancellationToken.None);

        Assert.Equal("run-123", result.RunId);
        Assert.NotNull(engine.StartRequest);
        Assert.Equal("wf-123", engine.StartRequest!.WorkflowDefinitionId);
        Assert.Equal("api-user", engine.StartRequest.Initiator);
        Assert.Equal(2, engine.StartRequest.Definition.Nodes.Count);
        Assert.Equal("ServiceA", engine.StartRequest.Definition.Nodes[1].Id);
    }

    private sealed class RecordingWorkflowEngineAdapter : IWorkflowEngineAdapter
    {
        public string EngineId => "recording";

        public WorkflowEngineStartRequest? StartRequest { get; private set; }

        public Task<WorkflowExecutionState> StartAsync(WorkflowEngineStartRequest request, CancellationToken cancellationToken)
        {
            StartRequest = request;
            return Task.FromResult(new WorkflowExecutionState("run-123", "completed", request.Definition.Nodes.Count, null, DateTime.UtcNow.ToString("o")));
        }

        public Task<WorkflowExecutionState> ResumeAsync(WorkflowEngineResumeRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<WorkflowExecutionState> RecoverAsync(WorkflowEngineRecoverRequest request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class PassThroughValidator : IBpmnWorkflowValidator
    {
        public BpmnValidationResult Validate(string bpmnXml)
        {
            return new BpmnWorkflowValidator().Validate(bpmnXml);
        }
    }
}
