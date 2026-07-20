using Agentwerke.Application.Agents;
using Agentwerke.Application.Observability;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentwerke.Application.Tests;

public sealed class WorkflowRunOrchestrationServiceTests
{
    [Fact]
    public async Task StartRunAsync_SeedsCustomInputsAsInputContext()
    {
        var runContext = new CapturingRunContextRepository();
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new SingleWorkflowDefinitionRepository(new WorkflowDefinition
            {
                Id = "wf_1",
                Name = "Autonomous SDLC",
                Version = "v1",
                Status = "active",
                Tags = ["github"]
            }),
            new InMemoryWorkflowRunRepository(),
            runContext,
            new InMemoryApprovalRepository(new ApprovalRequest { Id = "unused", RunId = "unused", Status = "pending", RiskLevel = "low" }),
            new InMemoryAgentInteractionRepository(),
            new CapturingAuditRepository(),
            outbox,
            new StubCorrelationContext("corr_start"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.StartRunAsync(new StartRunCommand(
            WorkflowId: "wf_1",
            Initiator: "operator-1",
            Inputs: new Dictionary<string, string>
            {
                ["branch_name"] = "feature/issue-142",
                ["input.repository"] = "isartor-ai/agentwerke"
            }));

        Assert.Equal("pending", result.Status);
        Assert.Contains(runContext.Writes, write =>
            write.RunId == result.RunId &&
            write.Key == "input.branch_name" &&
            write.Value == "feature/issue-142" &&
            write.Kind == RunContextKinds.Input);
        Assert.Contains(runContext.Writes, write =>
            write.RunId == result.RunId &&
            write.Key == "input.repository" &&
            write.Value == "isartor-ai/agentwerke" &&
            write.Kind == RunContextKinds.Input);
        Assert.DoesNotContain(runContext.Writes, static write => write.Key == "input.input.repository");
        Assert.Single(outbox.Enqueued);
        Assert.Equal("start", outbox.Enqueued[0].Operation);
    }

    [Fact]
    public async Task StartRunAsync_WhenTriggerCarriesInputs_SeedsFixedAndCustomTriggerContext()
    {
        var runContext = new CapturingRunContextRepository();
        var service = new WorkflowRunOrchestrationService(
            new SingleWorkflowDefinitionRepository(new WorkflowDefinition
            {
                Id = "wf_github",
                Name = "GitHub Trigger",
                Version = "v1",
                Status = "active",
                Tags = ["github-trigger"]
            }),
            new InMemoryWorkflowRunRepository(),
            runContext,
            new InMemoryApprovalRepository(new ApprovalRequest { Id = "unused", RunId = "unused", Status = "pending", RiskLevel = "low" }),
            new InMemoryAgentInteractionRepository(),
            new CapturingAuditRepository(),
            new CapturingRunOutbox(),
            new StubCorrelationContext("corr_trigger"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.StartRunAsync(new StartRunCommand(
            WorkflowId: "wf_github",
            Initiator: "github-webhook",
            Trigger: new TriggerMetadata(
                Source: "github",
                EventType: "issues.opened",
                ExternalId: "isartor-ai/agentwerke#142",
                ExternalUrl: "https://github.com/isartor-ai/agentwerke/issues/142",
                Title: "Seed custom inputs",
                Body: "Issue body",
                Inputs: new Dictionary<string, string>
                {
                    ["repository"] = "isartor-ai/agentwerke",
                    ["issue_url"] = "https://github.com/isartor-ai/agentwerke/issues/142"
                })));

        Assert.Contains(runContext.Writes, write =>
            write.RunId == result.RunId &&
            write.Key == "input.source" &&
            write.Value == "github");
        Assert.Contains(runContext.Writes, write =>
            write.RunId == result.RunId &&
            write.Key == "input.repository" &&
            write.Value == "isartor-ai/agentwerke");
        Assert.Contains(runContext.Writes, write =>
            write.RunId == result.RunId &&
            write.Key == "input.issue_url" &&
            write.Value == "https://github.com/isartor-ai/agentwerke/issues/142");
    }

    [Fact]
    public async Task ResumeRunAsync_RecordsApprovalDecisionUnderAuthenticatedPrincipal()
    {
        var approval = new ApprovalRequest
        {
            Id = "approval_1",
            RunId = "run_1",
            Status = "pending",
            RiskLevel = "high"
        };
        var approvalRepository = new InMemoryApprovalRepository(approval);
        var auditRepository = new CapturingAuditRepository();
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new UnusedWorkflowDefinitionRepository(),
            new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_1", Status = "waiting" }),
            new NoOpRunContextRepository(),
            approvalRepository,
            new InMemoryAgentInteractionRepository(),
            auditRepository,
            outbox,
            new StubCorrelationContext("corr_1"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.ResumeRunAsync(new ResumeRunCommand(
            RunId: "run_1",
            ApprovalId: "approval_1",
            Decision: "approve",
            Comment: "Ship it.",
            DecidedBy: "entra-user-42"));

        Assert.Equal("pending", result.Status);
        Assert.Equal("approved", approval.Status);
        Assert.Equal("entra-user-42", approval.DecidedBy);
        Assert.Single(auditRepository.Records);
        Assert.Equal("user", auditRepository.Records[0].ActorType);
        Assert.Equal("entra-user-42", auditRepository.Records[0].Actor);
        Assert.Equal("approval.approve", auditRepository.Records[0].Action);
        Assert.Single(outbox.Enqueued);
        Assert.Equal("resume", outbox.Enqueued[0].Operation);
        Assert.Equal("entra-user-42", OutboxResumePayload.Deserialize(outbox.Enqueued[0].Payload)?.ApprovedBy);
    }

    [Fact]
    public async Task AnswerInteractionAsync_MarksAnsweredAndEnqueuesResumeWithAnswerer()
    {
        var interaction = new AgentInteraction
        {
            Id = "int_1",
            RunId = "run_1",
            FromAgent = "reviewer",
            Kind = AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = true,
            Prompt = "Ship or add tests?",
            Status = AgentInteractionStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
        var interactions = new InMemoryAgentInteractionRepository(interaction);
        var auditRepository = new CapturingAuditRepository();
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new UnusedWorkflowDefinitionRepository(),
            new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_1", Status = "waiting_user" }),
            new NoOpRunContextRepository(),
            new InMemoryApprovalRepository(new ApprovalRequest { Id = "unused", RunId = "unused", Status = "pending", RiskLevel = "low" }),
            interactions,
            auditRepository,
            outbox,
            new StubCorrelationContext("corr_ans"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.AnswerInteractionAsync(new AnswerInteractionCommand(
            RunId: "run_1",
            InteractionId: "int_1",
            Answer: "add tests",
            AnsweredBy: "entra-user-9"));

        Assert.Equal("pending", result.Status);
        Assert.Equal(AgentInteractionStatuses.Answered, interaction.Status);
        Assert.Equal("add tests", interaction.Response);
        Assert.Equal("entra-user-9", interaction.RespondedBy);
        Assert.Single(outbox.Enqueued);
        Assert.Equal("resume", outbox.Enqueued[0].Operation);
        Assert.Equal("entra-user-9", OutboxResumePayload.Deserialize(outbox.Enqueued[0].Payload)?.ApprovedBy);
        Assert.Contains(auditRepository.Records, static r => r.Action == "interaction.answer");
    }

    [Fact]
    public async Task AnswerInteractionAsync_WhenNotPending_ThrowsAndDoesNotResume()
    {
        var interaction = new AgentInteraction
        {
            Id = "int_1",
            RunId = "run_1",
            Kind = AgentInteractionKinds.Question,
            Status = AgentInteractionStatuses.Answered,
            Prompt = "already answered",
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new UnusedWorkflowDefinitionRepository(),
            new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_1", Status = "waiting_user" }),
            new NoOpRunContextRepository(),
            new InMemoryApprovalRepository(new ApprovalRequest { Id = "unused", RunId = "unused", Status = "pending", RiskLevel = "low" }),
            new InMemoryAgentInteractionRepository(interaction),
            new CapturingAuditRepository(),
            outbox,
            new StubCorrelationContext("corr_ans2"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        await Assert.ThrowsAsync<InteractionNotPendingException>(() =>
            service.AnswerInteractionAsync(new AnswerInteractionCommand("run_1", "int_1", "x", "u")));
        Assert.Empty(outbox.Enqueued);
    }

    [Fact]
    public async Task ResumeExternalRunAsync_EnqueuesResumeWithCorrelationPayloadAndAuditActor()
    {
        var auditRepository = new CapturingAuditRepository();
        var outbox = new CapturingRunOutbox();
        var service = new WorkflowRunOrchestrationService(
            new UnusedWorkflowDefinitionRepository(),
            new InMemoryWorkflowRunRepository(new WorkflowRun { Id = "run_2", Status = "waiting_external" }),
            new NoOpRunContextRepository(),
            new InMemoryApprovalRepository(new ApprovalRequest { Id = "unused", RunId = "run_2", Status = "pending", RiskLevel = "low" }),
            new InMemoryAgentInteractionRepository(),
            auditRepository,
            outbox,
            new StubCorrelationContext("corr_2"),
            new NoOpWorkflowMetrics(),
            NullLogger<WorkflowRunOrchestrationService>.Instance);

        var result = await service.ResumeExternalRunAsync(new ResumeExternalRunCommand(
            RunId: "run_2",
            CorrelationKey: "feature/external-wait",
            Payload: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pull_number"] = "42"
            },
            ResumedBy: "operator-7"));

        Assert.Equal("pending", result.Status);
        Assert.Single(outbox.Enqueued);
        Assert.Equal("resume", outbox.Enqueued[0].Operation);

        var payload = OutboxResumePayload.Deserialize(outbox.Enqueued[0].Payload);
        Assert.NotNull(payload);
        Assert.Null(payload!.ApprovedBy);
        Assert.Equal("feature/external-wait", payload.ExternalCorrelationKey);
        Assert.Equal("42", payload.ExternalPayload!["pull_number"]);
        Assert.Equal("operator-7", payload.ResumedBy);

        Assert.Contains(auditRepository.Records, static record =>
            record.Action == "workflow.resume_external" &&
            record.Actor == "operator-7" &&
            record.ActorType == "operator");
    }
}
