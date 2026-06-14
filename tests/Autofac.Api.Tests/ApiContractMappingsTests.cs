using Autofac.Api.Contracts;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;

namespace Autofac.Api.Tests;

public sealed class ApiContractMappingsTests
{
    [Fact]
    public void ToRunDetail_MapsPromptSnapshotFromRuntimeSnapshot()
    {
        var run = new WorkflowRun
        {
            Id = "run-1",
            WorkflowId = "wf-1",
            WorkflowName = "Workflow",
            WorkflowVersion = "v1",
            Status = "running",
            RiskLevel = "low",
            CurrentStep = "Deploy",
            RequestedBy = "tester",
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            Steps =
            [
                new WorkflowRunStep
                {
                    Id = "step-1",
                    Name = "Deploy",
                    Type = "serviceTask",
                    Status = "completed",
                    AgentName = "deploy-agent",
                    RuntimeSnapshot = new AgentRuntimeSnapshot
                    {
                        RunId = "run-1",
                        StepId = "step-1",
                        NodeId = "Deploy",
                        AgentName = "deploy-agent",
                        Action = "cloud.deploy_artifact",
                        Prompt = new AgentPromptSnapshot(
                            FinalPrompt: "assembled prompt",
                            RenderedAt: "2026-06-14T00:00:00.0000000+00:00",
                            Sections:
                            [
                                new AgentPromptSectionSnapshot("task_prompt", "Do the thing", "inline:task_prompt")
                            ],
                            Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["environment"] = "staging"
                            },
                            SourceFiles: ["inline:task_prompt"])
                    }
                }
            ]
        };

        var detail = ApiContractMappings.ToRunDetail(run, Array.Empty<ApprovalRequest>(), Array.Empty<Autofac.Storage.Artifacts.ArtifactDescriptor>());

        var step = Assert.Single(detail.Steps);
        Assert.NotNull(step.PromptSnapshot);
        Assert.Equal("assembled prompt", step.PromptSnapshot!.FinalPrompt);
        Assert.Equal("staging", step.PromptSnapshot.Variables["environment"]);
        Assert.Single(step.PromptSnapshot.Sections);
    }
}
