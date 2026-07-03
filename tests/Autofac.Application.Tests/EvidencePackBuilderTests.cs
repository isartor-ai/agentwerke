using Autofac.Application.Workflows;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Autofac.Application.Tests;

public sealed class EvidencePackBuilderTests
{
    [Fact]
    public void Build_DefaultRuntimePack_IncludesAuditApprovalsToolsArtifactsAndBpmnHash()
    {
        const string bpmnXml = "<bpmn:definitions id=\"test\" />";
        var generatedAt = DateTimeOffset.Parse("2026-06-18T10:00:00.0000000Z");
        var run = new WorkflowRun
        {
            Id = "run_1",
            WorkflowId = "wf_1",
            WorkflowName = "Release",
            WorkflowVersion = "v2.0.0",
            Status = "completed",
            RiskLevel = "high",
            RequestedBy = "operator@example.com",
            StartedAt = "2026-06-18T09:00:00.0000000Z",
            CompletedAt = "2026-06-18T09:05:00.0000000Z",
            DurationMs = 300000,
            CorrelationId = "corr_1",
            Tags = ["release"],
            Steps =
            [
                new WorkflowRunStep
                {
                    Id = "step_1",
                    Name = "Deploy",
                    Type = "serviceTask",
                    Status = "completed",
                    AgentName = "DeployAgent",
                    StartedAt = "2026-06-18T09:01:00.0000000Z",
                    PolicyDecision = new PolicyDecision
                    {
                        Kind = "allow",
                        PolicyId = "policy_release",
                        PolicyName = "Release policy",
                        Rationale = "Allowed after checks.",
                        RiskScore = 42,
                        RiskLevel = "medium",
                        RiskFactors = ["production"],
                        DecidedAt = "2026-06-18T09:01:30.0000000Z",
                        Constraints = ["approval_required"]
                    },
                    RuntimeSnapshot = new AgentRuntimeSnapshot
                    {
                        RunId = "run_1",
                        StepId = "step_1",
                        NodeId = "DeployNode",
                        AgentName = "DeployAgent",
                        Action = "cloud.deploy_artifact",
                        ToolInvocations =
                        [
                            new AgentToolInvocationRecord
                            {
                                ToolName = "github.create_pull_request",
                                Category = AgentToolCategories.Integration,
                                Status = "completed",
                                PolicyDecisionId = "policy_release",
                                PolicyDecisionKind = "allow",
                                InputSummary = "branch=release token=ghp_abcdefghijklmnopqrstuvwxyz123456789012",
                                OutputSummary = "pull request created with Authorization: Bearer sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                                ArtifactNames = ["release-notes.md"],
                                DurationMs = 125
                            }
                        ],
                        Artifacts =
                        [
                            new AgentArtifactRecord
                            {
                                Name = "agent-output.json",
                                Uri = "artifact://run_1/agent-output.json",
                                ContentType = "application/json"
                            }
                        ],
                        SandboxExecution = new AgentSandboxExecutionRecord
                        {
                            Provider = "opensandbox",
                            SandboxId = "sbx-1",
                            CommandState = "Completed",
                            ExitCode = 0,
                            DurationMs = 1450,
                            Logs =
                            [
                                new AgentSandboxLogRecord
                                {
                                    Stream = "stdout",
                                    Message = "deploy running with token=ghp_abcdefghijklmnopqrstuvwxyz123456789012",
                                    Timestamp = "2026-06-18T09:01:10.0000000Z"
                                }
                            ],
                            Diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["model.credential_source"] = "secret-store api_key=supersecret"
                            }
                        },
                        TokenUsage = new AgentModelTokenUsage(
                            InputTokens: 300,
                            OutputTokens: 150,
                            ModelId: "claude-sonnet-4-6",
                            ElapsedMs: 980)
                    }
                }
            ],
            Events =
            [
                new WorkflowEvent
                {
                    Id = "evt_1",
                    Type = "run_completed",
                    Message = "Run completed with secret=hunter2.",
                    CreatedAt = "2026-06-18T09:05:00.0000000Z"
                }
            ]
        };
        var workflow = new WorkflowDefinition
        {
            Id = "wf_1",
            Name = "Release",
            Version = "v2.0.0",
            BpmnXml = bpmnXml
        };
        var approvals = new[]
        {
            new ApprovalRequest
            {
                Id = "apr_1",
                RunId = "run_1",
                ActionRequested = "Deploy to production",
                Requester = "operator@example.com",
                AgentName = "DeployAgent",
                Status = "approved",
                RiskLevel = "high",
                RiskScore = 80,
                RiskFactors = ["production"],
                AffectedSystems = ["api"],
                PolicyRationale = "Human approval required.",
                CreatedAt = "2026-06-18T09:02:00.0000000Z",
                DecidedAt = "2026-06-18T09:03:00.0000000Z",
                DecidedBy = "approver@example.com",
                DecisionComment = "Approved."
            }
        };
        var auditRecords = new[]
        {
            new AuditRecord
            {
                Id = "aud_1",
                RunId = "run_1",
                CorrelationId = "corr_1",
                ActorType = "connector",
                Actor = "github",
                Action = "connector.github.create_pull_request",
                ResourceType = "connector",
                ResourceId = "github",
                Outcome = "success",
                Details = "PR created with api_key=supersecret",
                Timestamp = "2026-06-18T09:04:00.0000000Z"
            }
        };

        var pack = EvidencePackBuilder.Build(
            run,
            workflow,
            approvals,
            auditRecords,
            Array.Empty<RunContextEntry>(),
            [new EvidenceArtifactInput("scan-report.json", 2048, "2026-06-18T09:04:30.0000000Z")],
            runtimeMode: "Agentwerke",
            camundaEnabled: false,
            generatedAt);

        Assert.Equal(EvidencePackBuilder.SchemaVersion, pack.SchemaVersion);
        Assert.Equal("run_1", pack.RunId);
        Assert.Equal("completed", pack.Run.Status);
        Assert.Equal("Agentwerke", pack.Runtime.Mode);
        Assert.False(pack.Runtime.CamundaEnabled);
        Assert.Null(pack.Camunda);
        Assert.Equal(ExpectedSha256(bpmnXml), pack.Workflow.BpmnSha256);
        Assert.Equal("approver@example.com", Assert.Single(pack.Approvals).DecidedBy);
        Assert.Equal("allow", Assert.Single(pack.PolicyDecisions).Kind);
        var toolCall = Assert.Single(pack.ToolCalls);
        Assert.Equal("github.create_pull_request", toolCall.ToolName);
        Assert.DoesNotContain("ghp_", toolCall.InputSummary);
        Assert.DoesNotContain("sk-ant-", toolCall.OutputSummary);
        Assert.Contains("[redacted]", toolCall.InputSummary);
        Assert.Contains("[redacted]", toolCall.OutputSummary);
        var connector = Assert.Single(pack.ConnectorCalls);
        Assert.Equal("github", connector.ConnectorId);
        Assert.Equal("create_pull_request", connector.Operation);
        Assert.DoesNotContain("supersecret", connector.Details);
        Assert.Contains("[redacted]", connector.Details);
        Assert.Contains(pack.Artifacts, artifact => artifact.Name == "scan-report.json" && artifact.Source == "artifact-storage");
        Assert.Contains(pack.Artifacts, artifact => artifact.Name == "agent-output.json" && artifact.Source == "agent-runtime-snapshot");
        Assert.Contains(pack.Logs, log => log.Source == "workflow-event" && log.Type == "run_completed" && log.Message.Contains("[redacted]", StringComparison.Ordinal));
        Assert.Contains(pack.AuditLog, audit => audit.Action == "connector.github.create_pull_request" && audit.Details!.Contains("[redacted]", StringComparison.Ordinal));

        var sandboxExecution = Assert.Single(pack.SandboxExecutions);
        Assert.Equal("step_1", sandboxExecution.StepId);
        Assert.Equal("DeployAgent", sandboxExecution.AgentName);
        Assert.Equal("opensandbox", sandboxExecution.Provider);
        Assert.Equal("sbx-1", sandboxExecution.SandboxId);
        Assert.Equal("Completed", sandboxExecution.CommandState);
        var sandboxLog = Assert.Single(sandboxExecution.Logs);
        Assert.DoesNotContain("ghp_", sandboxLog.Message);
        Assert.Contains("[redacted]", sandboxLog.Message);
        Assert.DoesNotContain("supersecret", sandboxExecution.Diagnostics["model.credential_source"]);
        Assert.Contains("[redacted]", sandboxExecution.Diagnostics["model.credential_source"]);

        var modelUsage = Assert.Single(pack.ModelUsage);
        Assert.Equal("step_1", modelUsage.StepId);
        Assert.Equal("DeployAgent", modelUsage.AgentName);
        Assert.Equal("claude-sonnet-4-6", modelUsage.ModelId);
        Assert.Equal(300, modelUsage.InputTokens);
        Assert.Equal(150, modelUsage.OutputTokens);
        Assert.Equal(980, modelUsage.ElapsedMs);
    }

    [Fact]
    public void Build_WhenStepHasNoSandboxExecutionOrTokenUsage_SandboxAndModelSectionsAreEmpty()
    {
        var run = new WorkflowRun
        {
            Id = "run_2",
            WorkflowId = "wf_1",
            WorkflowName = "Release",
            WorkflowVersion = "v2.0.0",
            Status = "completed",
            RiskLevel = "low",
            RequestedBy = "operator@example.com",
            StartedAt = "2026-06-18T09:00:00.0000000Z",
            Steps =
            [
                new WorkflowRunStep
                {
                    Id = "step_1",
                    Name = "Generate Spec",
                    Type = "serviceTask",
                    Status = "completed",
                    AgentName = "spec-writer",
                    RuntimeSnapshot = new AgentRuntimeSnapshot
                    {
                        RunId = "run_2",
                        StepId = "step_1",
                        NodeId = "GenerateSpec",
                        AgentName = "spec-writer",
                        Action = "spec.generate"
                    }
                }
            ]
        };

        var pack = EvidencePackBuilder.Build(
            run,
            workflow: null,
            Array.Empty<ApprovalRequest>(),
            Array.Empty<AuditRecord>(),
            Array.Empty<RunContextEntry>(),
            Array.Empty<EvidenceArtifactInput>(),
            runtimeMode: "Agentwerke",
            camundaEnabled: false,
            DateTimeOffset.Parse("2026-06-18T10:00:00.0000000Z"));

        Assert.Empty(pack.SandboxExecutions);
        Assert.Empty(pack.ModelUsage);
    }

    [Fact]
    public void Build_CamundaRuntime_IncludesCamundaContextMetadata()
    {
        var run = new WorkflowRun
        {
            Id = "run_2",
            WorkflowId = "wf_2",
            WorkflowName = "Camunda run",
            WorkflowVersion = "v1",
            Status = "completed",
            StartedAt = "2026-06-18T09:00:00.0000000Z"
        };
        var runContext = new[]
        {
            new RunContextEntry { RunId = "run_2", Key = "camunda.processInstanceKey", Value = "2251799813685250" },
            new RunContextEntry { RunId = "run_2", Key = "input.title", Value = "Ignored" }
        };

        var pack = EvidencePackBuilder.Build(
            run,
            workflow: null,
            approvals: Array.Empty<ApprovalRequest>(),
            auditRecords: Array.Empty<AuditRecord>(),
            runContext,
            storageArtifacts: Array.Empty<EvidenceArtifactInput>(),
            runtimeMode: "Camunda",
            camundaEnabled: true,
            generatedAt: DateTimeOffset.Parse("2026-06-18T10:00:00.0000000Z"));

        Assert.NotNull(pack.Camunda);
        Assert.Equal("camunda", pack.Camunda!.Adapter);
        Assert.Equal("2251799813685250", pack.Camunda.Metadata["camunda.processInstanceKey"]);
        Assert.DoesNotContain("input.title", pack.Camunda.Metadata.Keys);
    }

    private static string ExpectedSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
