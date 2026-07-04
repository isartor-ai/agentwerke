using Agentwerke.Infrastructure.Workflows;
using Agentwerke.Workflows.Bpmn;
using Agentwerke.Workflows.Runtime;

namespace Agentwerke.Workflows.Tests;

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
                                  xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1">
                  <bpmn:process id="Process_1" name="Adapter Flow">
                    <bpmn:startEvent id="Start" />
                    <bpmn:serviceTask id="ServiceA" name="Do Work">
                      <bpmn:extensionElements>
                        <agentwerke:agentTask agent="implementation-agent"
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

    [Fact]
    public async Task StartAsync_WhenEngineReturnsWaitingApprovalArtifactName_ForwardsItOnWaitingApprovalInfo()
    {
        var engine = new RecordingWorkflowEngineAdapter
        {
            NextResult = new WorkflowExecutionState(
                "run-456", "waiting_user", "Finalize", "ApprovalGate", null,
                WaitingApprovalArtifactName: "requirements.md"),
        };
        var runner = new WorkflowRunnerAdapter(new PassThroughValidator(), engine);

        var result = await runner.StartAsync(
            workflowDefinitionId: "wf-456",
            bpmnXml: """
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1">
                  <bpmn:process id="Process_1" name="Adapter Flow">
                    <bpmn:startEvent id="Start" />
                    <bpmn:serviceTask id="ServiceA" name="Do Work">
                      <bpmn:extensionElements>
                        <agentwerke:agentTask agent="business-analyst"
                                           action="requirement.design"
                                           purposeType="requirement-design"
                                           policyTag="doc-generation" />
                      </bpmn:extensionElements>
                    </bpmn:serviceTask>
                    <bpmn:userTask id="ApprovalGate" name="Review Requirements">
                      <bpmn:extensionElements>
                        <agentwerke:approvalTask purposeType="requirement-design"
                                              policyTag="doc-generation" />
                      </bpmn:extensionElements>
                    </bpmn:userTask>
                  </bpmn:process>
                </bpmn:definitions>
                """,
            initiator: "api-user",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(result.WaitingApproval);
        Assert.Equal("ApprovalGate", result.WaitingApproval!.NodeId);
        Assert.Equal("business-analyst", result.WaitingApproval.AgentName);
        Assert.Equal("requirements.md", result.WaitingApproval.ArtifactName);
    }

    [Fact]
    public async Task StartAsync_WhenEngineReturnsWaitingExternalState_ForwardsCorrelationKeyAndMessageName()
    {
        var engine = new RecordingWorkflowEngineAdapter
        {
            NextResult = new WorkflowExecutionState(
                "run-789", "waiting_external", "End", "WaitForMerge", null,
                WaitingExternalCorrelationKey: "agentwerke/run-789",
                WaitingExternalMessageName: "github.pull_request.merged"),
        };
        var runner = new WorkflowRunnerAdapter(new PassThroughValidator(), engine);

        var result = await runner.StartAsync(
            workflowDefinitionId: "wf-789",
            bpmnXml: """
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
                  <bpmn:process id="Process_1" name="Adapter Flow">
                    <bpmn:startEvent id="Start" />
                  </bpmn:process>
                </bpmn:definitions>
                """,
            initiator: "api-user",
            cancellationToken: CancellationToken.None);

        Assert.Equal("waiting_external", result.Status);
        Assert.Equal("agentwerke/run-789", result.WaitingExternalCorrelationKey);
        Assert.Equal("github.pull_request.merged", result.WaitingExternalMessageName);
    }

    private sealed class RecordingWorkflowEngineAdapter : IWorkflowEngineAdapter
    {
        public string EngineId => "recording";

        public WorkflowEngineStartRequest? StartRequest { get; private set; }

        public WorkflowExecutionState? NextResult { get; set; }

        public Task<WorkflowExecutionState> StartAsync(WorkflowEngineStartRequest request, CancellationToken cancellationToken)
        {
            StartRequest = request;
            return Task.FromResult(
                NextResult ?? new WorkflowExecutionState("run-123", "completed", null, null, DateTime.UtcNow.ToString("o")));
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
