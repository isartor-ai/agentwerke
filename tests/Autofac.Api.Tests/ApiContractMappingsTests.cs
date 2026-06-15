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
                            SourceFiles: ["inline:task_prompt"]),
                        Skills =
                        [
                            new AgentSkillUsageRecord
                            {
                                SkillId = "shipping-and-launch",
                                Name = "Shipping and Launch",
                                Description = "Ship changes safely.",
                                Version = "1.2.0",
                                Fingerprint = new string('a', 64),
                                InvocationRules = ["deploy", "release"],
                                RequiredFiles = ["checklist.md"],
                                OptionalTools = ["rg"],
                                Source = "runtime-contract",
                                Available = true,
                                Selected = true,
                                Invoked = true
                            }
                        ],
                        ToolInvocations =
                        [
                            new AgentToolInvocationRecord
                            {
                                ToolName = "github.create_pull_request",
                                Category = AgentToolCategories.Integration,
                                Status = "completed",
                                PolicyDecisionId = "test-policy",
                                PolicyDecisionKind = "allow",
                                InputSummary = "{\"head_branch\":\"autofac/run-1\"}",
                                OutputSummary = "created pull request",
                                ArtifactNames = [".autofac/runs/run-1/step-1.md"],
                                DurationMs = 125
                            }
                        ],
                        HookExecutions =
                        [
                            new AgentHookExecutionRecord
                            {
                                HookName = "policy-guard",
                                Event = "before_agent_run",
                                Type = "internal-policy",
                                Decision = "proceed",
                                Blocking = true,
                                OutputSummary = "validated",
                                DurationMs = 5
                            }
                        ]
                    }
                }
            ]
        };

        var detail = ApiContractMappings.ToRunDetail(run, Array.Empty<ApprovalRequest>(), Array.Empty<Autofac.Storage.Artifacts.ArtifactDescriptor>());

        var step = Assert.Single(detail.Steps);
        Assert.NotNull(step.RuntimeSnapshot);
        var snap = step.RuntimeSnapshot!;
        Assert.NotNull(snap.Prompt);
        Assert.Equal("assembled prompt", snap.Prompt!.FinalPrompt);
        Assert.Equal("staging", snap.Prompt.Variables["environment"]);
        Assert.Single(snap.Prompt.Sections);
        var skill = Assert.Single(snap.Skills);
        Assert.Equal("shipping-and-launch", skill.SkillId);
        Assert.True(skill.Invoked);
        Assert.Equal("runtime-contract", skill.Source);
        var tool = Assert.Single(snap.ToolInvocations);
        Assert.Equal("github.create_pull_request", tool.ToolName);
        Assert.Equal("allow", tool.PolicyDecisionKind);
        Assert.Single(tool.ArtifactNames);
        var hook = Assert.Single(step.HookExecutions);
        Assert.Equal("policy-guard", hook.HookName);
        Assert.True(hook.Blocking);
    }
}
