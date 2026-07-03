using Agentwerke.Api.Contracts;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Api.Tests;

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
                        ExecutionMode = AgentExecutionModes.AgentSandboxed,
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
                            SourceFiles: ["inline:task_prompt"],
                            MissingVariables: ["output.Build"]),
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
                                InputSummary = "{\"head_branch\":\"agentwerke/run-1\"}",
                                OutputSummary = "created pull request",
                                ArtifactNames = [".agentwerke/runs/run-1/step-1.md"],
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
                        ],
                        Artifacts =
                        [
                            new AgentArtifactRecord
                            {
                                Name = "spec.md",
                                Uri = "/api/runs/run-1/artifacts/spec.md",
                                ContentType = "text/markdown"
                            }
                        ],
                        SandboxExecution = new AgentSandboxExecutionRecord
                        {
                            Provider = "opensandbox",
                            SandboxId = "sbx-123",
                            CommandState = "Completed",
                            ExitCode = 0,
                            DurationMs = 912,
                            Logs =
                            [
                                new AgentSandboxLogRecord
                                {
                                    Stream = "stdout",
                                    Message = "spec generation running",
                                    Timestamp = "2026-06-19T12:00:00.0000000+00:00"
                                }
                            ],
                            Diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["provider"] = "opensandbox",
                                ["sandbox.state"] = "Running"
                            }
                        },
                        TokenUsage = new AgentModelTokenUsage(
                            InputTokens: 512,
                            OutputTokens: 256,
                            ModelId: "claude-sonnet-4-6",
                            ElapsedMs: 1875)
                    }
                }
            ]
        };

        var detail = ApiContractMappings.ToRunDetail(run, Array.Empty<ApprovalRequest>(), Array.Empty<Agentwerke.Storage.Artifacts.ArtifactDescriptor>());

        var step = Assert.Single(detail.Steps);
        Assert.NotNull(step.PromptSnapshot);
        Assert.Equal("assembled prompt", step.PromptSnapshot!.FinalPrompt);
        Assert.Equal("staging", step.PromptSnapshot.Variables["environment"]);
        Assert.Equal("output.Build", Assert.Single(step.PromptSnapshot.MissingVariables));
        Assert.Single(step.PromptSnapshot.Sections);
        var skill = Assert.Single(step.Skills);
        Assert.Equal("shipping-and-launch", skill.SkillId);
        Assert.True(skill.Invoked);
        Assert.Equal("runtime-contract", skill.Source);
        var tool = Assert.Single(step.ToolInvocations);
        Assert.Equal("github.create_pull_request", tool.ToolName);
        Assert.Equal("allow", tool.PolicyDecisionKind);
        Assert.Single(tool.ArtifactNames);
        var hook = Assert.Single(step.HookExecutions);
        Assert.Equal("policy-guard", hook.HookName);
        Assert.True(hook.Blocking);
        Assert.NotNull(step.RuntimeSnapshot);
        Assert.Equal("cloud.deploy_artifact", step.RuntimeSnapshot!.Action);
        Assert.Equal(AgentExecutionModes.AgentSandboxed, step.RuntimeSnapshot.ExecutionMode);
        var artifact = Assert.Single(step.RuntimeSnapshot.StepArtifacts);
        Assert.Equal("spec.md", artifact.Name);
        Assert.NotNull(step.RuntimeSnapshot.SandboxExecution);
        Assert.Equal("opensandbox", step.RuntimeSnapshot.SandboxExecution!.Provider);
        Assert.Equal("sbx-123", step.RuntimeSnapshot.SandboxExecution.SandboxId);
        Assert.Equal("Running", step.RuntimeSnapshot.SandboxExecution.Diagnostics["sandbox.state"]);
        Assert.Single(step.RuntimeSnapshot.SandboxExecution.Logs);
        Assert.NotNull(step.RuntimeSnapshot.TokenUsage);
        Assert.Equal(512, step.RuntimeSnapshot.TokenUsage!.InputTokens);
        Assert.Equal(256, step.RuntimeSnapshot.TokenUsage.OutputTokens);
        Assert.Equal("claude-sonnet-4-6", step.RuntimeSnapshot.TokenUsage.ModelId);
        Assert.Equal(1875, step.RuntimeSnapshot.TokenUsage.ElapsedMs);
    }

    [Fact]
    public void ToRunDetail_WhenTokenUsageIsAbsent_RuntimeSnapshotTokenUsageIsNull()
    {
        var run = new WorkflowRun
        {
            Id = "run-2",
            WorkflowId = "wf-1",
            WorkflowName = "Workflow",
            WorkflowVersion = "v1",
            Status = "running",
            RiskLevel = "low",
            RequestedBy = "tester",
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            Steps =
            [
                new WorkflowRunStep
                {
                    Id = "step-1",
                    Name = "Generate Spec",
                    Type = "serviceTask",
                    Status = "completed",
                    AgentName = "spec-writer",
                    RuntimeSnapshot = new AgentRuntimeSnapshot
                    {
                        RunId = "run-2",
                        StepId = "step-1",
                        NodeId = "GenerateSpec",
                        AgentName = "spec-writer",
                        Action = "spec.generate",
                        ExecutionMode = AgentExecutionModes.Local
                    }
                }
            ]
        };

        var detail = ApiContractMappings.ToRunDetail(run, Array.Empty<ApprovalRequest>(), Array.Empty<Agentwerke.Storage.Artifacts.ArtifactDescriptor>());

        var step = Assert.Single(detail.Steps);
        Assert.NotNull(step.RuntimeSnapshot);
        Assert.Null(step.RuntimeSnapshot!.TokenUsage);
        Assert.Null(step.RuntimeSnapshot.SandboxExecution);
    }

    [Fact]
    public void ToApprovalSummary_ForwardsArtifactName()
    {
        var approval = new ApprovalRequest
        {
            Id = "apr-1",
            RunId = "run-1",
            WorkflowName = "Requirement Design",
            ActionRequested = "requirement-design",
            Requester = "tester",
            AgentName = "business-analyst",
            PolicyRationale = "doc-generation",
            RiskLevel = "low",
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = "pending",
            Priority = "normal",
            ArtifactName = "requirements.md",
        };

        var summary = ApiContractMappings.ToApprovalSummary(approval);

        Assert.Equal("requirements.md", summary.ArtifactName);
    }

    [Fact]
    public void ToApprovalSummary_WhenArtifactNameIsAbsent_SummaryArtifactNameIsNull()
    {
        var approval = new ApprovalRequest
        {
            Id = "apr-2",
            RunId = "run-2",
            WorkflowName = "Deploy",
            ActionRequested = "deploy",
            Requester = "tester",
            AgentName = "deploy-agent",
            PolicyRationale = "production",
            RiskLevel = "high",
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = "pending",
            Priority = "normal",
        };

        var summary = ApiContractMappings.ToApprovalSummary(approval);

        Assert.Null(summary.ArtifactName);
    }
}
