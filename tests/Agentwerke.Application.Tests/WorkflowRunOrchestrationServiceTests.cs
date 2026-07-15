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

    private sealed class UnusedWorkflowDefinitionRepository : IWorkflowDefinitionRepository
    {
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class SingleWorkflowDefinitionRepository(WorkflowDefinition workflow) : IWorkflowDefinitionRepository
    {
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>([workflow]);

        public Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(workflow.Id, workflowId, StringComparison.Ordinal) ? workflow : null);

        public Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default) =>
            GetAsync(workflowId, cancellationToken);

        public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class InMemoryWorkflowRunRepository : IWorkflowRunRepository
    {
        private WorkflowRun? _run;

        public InMemoryWorkflowRunRepository(WorkflowRun? run = null)
        {
            _run = run;
        }

        public int PendingApprovalDecrements { get; private set; }
        public List<(string RunId, string Type, string Message)> Events { get; } = [];

        public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_run is not null && string.Equals(_run.Id, runId, StringComparison.Ordinal) ? _run : null);
        }

        public Task<WorkflowRun> CreatePendingRunAsync(
            string runId,
            string workflowId,
            string workflowName,
            string workflowVersion,
            string? initiator,
            List<string> tags,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            _run = new WorkflowRun
            {
                Id = runId,
                WorkflowId = workflowId,
                WorkflowName = workflowName,
                WorkflowVersion = workflowVersion,
                Status = "pending",
                RequestedBy = initiator ?? string.Empty,
                StartedAt = DateTimeOffset.UtcNow.ToString("o"),
                Tags = tags,
                CorrelationId = correlationId
            };
            return Task.FromResult(_run);
        }

        public Task UpdateRunStatusAsync(string runId, string status, CancellationToken cancellationToken)
        {
            if (_run is null)
            {
                throw new WorkflowRunNotFoundException(runId);
            }

            _run.Status = status;
            return Task.CompletedTask;
        }

        public Task UpdateCurrentStepAsync(string runId, string? currentStep, CancellationToken cancellationToken)
        {
            if (_run is null)
            {
                throw new WorkflowRunNotFoundException(runId);
            }

            _run.CurrentStep = currentStep ?? string.Empty;
            return Task.CompletedTask;
        }

        public Task IncrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task DecrementPendingApprovalsAsync(string runId, CancellationToken cancellationToken)
        {
            PendingApprovalDecrements++;
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(string runId, string type, string message, CancellationToken cancellationToken)
        {
            Events.Add((runId, type, message));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRunContextRepository : IRunContextRepository
    {
        public List<(string RunId, string Key, string Value, string Kind)> Writes { get; } = [];

        public Task SetAsync(
            string runId,
            string key,
            string value,
            string kind,
            CancellationToken cancellationToken)
        {
            Writes.Add((runId, key, value, kind));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(
            string runId,
            CancellationToken cancellationToken)
        {
            var entries = Writes
                .Where(write => write.RunId == runId)
                .Select(static write => new RunContextEntry
                {
                    RunId = write.RunId,
                    Key = write.Key,
                    Value = write.Value,
                    Kind = write.Kind
                })
                .ToArray();
            return Task.FromResult<IReadOnlyList<RunContextEntry>>(entries);
        }
    }

    private sealed class NoOpRunContextRepository : IRunContextRepository
    {
        public Task SetAsync(
            string runId,
            string key,
            string value,
            string kind,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunContextEntry>> GetAllAsync(
            string runId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<RunContextEntry>>([]);
        }
    }

    private sealed class InMemoryApprovalRepository : IApprovalRepository
    {
        private readonly ApprovalRequest _approval;

        public InMemoryApprovalRepository(ApprovalRequest approval)
        {
            _approval = approval;
        }

        public Task<ApprovalRequest?> GetApprovalAsync(string approvalId, CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Equals(_approval.Id, approvalId, StringComparison.Ordinal) ? _approval : null);
        }

        public Task<ApprovalRequest?> GetPendingApprovalForRunAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(
                string.Equals(_approval.RunId, runId, StringComparison.Ordinal) &&
                string.Equals(_approval.Status, "pending", StringComparison.Ordinal)
                    ? _approval
                    : null);
        }

        public Task AddApprovalAsync(ApprovalRequest approval, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAgentInteractionRepository : IAgentInteractionRepository
    {
        private readonly Lock _gate = new();

        public List<AgentInteraction> Items { get; } = new();

        public InMemoryAgentInteractionRepository(params AgentInteraction[] seed) => Items.AddRange(seed);

        public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken)
        {
            Items.Add(interaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(Items.Where(i => i.RunId == runId).ToList());

        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
            string runId, string? fromFilter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(
                Items.Where(i => i.RunId == runId && i.Kind == AgentInteractionKinds.Post).ToList());

        public Task<AgentInteraction?> GetByIdAsync(string interactionId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == interactionId));

        public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i =>
                i.RunId == runId && i.Status == AgentInteractionStatuses.Pending));

        /// <summary>
        /// Reads and writes under one lock so this fake reproduces the database's atomicity: exactly
        /// one concurrent caller may win a transition (#218).
        /// </summary>
        public Task<InteractionTransitionResult> TryTransitionAsync(
            string interactionId,
            string toStatus,
            string? response,
            string? respondedBy,
            string? respondedChannel,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var interaction = Items.FirstOrDefault(i => i.Id == interactionId);
                if (interaction is null)
                {
                    return Task.FromResult(
                        new InteractionTransitionResult(InteractionTransitionOutcome.NotFound, null));
                }

                if (AgentInteractionStatuses.IsTerminal(interaction.Status))
                {
                    return Task.FromResult(
                        new InteractionTransitionResult(InteractionTransitionOutcome.AlreadyTerminal, interaction));
                }

                if (response is not null)
                {
                    interaction.Response = response;
                }

                if (respondedBy is not null)
                {
                    interaction.RespondedBy = respondedBy;
                    interaction.RespondedAt = DateTimeOffset.UtcNow.ToString("o");
                }

                if (respondedChannel is not null)
                {
                    interaction.RespondedChannel = respondedChannel;
                }

                interaction.Status = toStatus;
                interaction.Version++;

                return Task.FromResult(
                    new InteractionTransitionResult(InteractionTransitionOutcome.Won, interaction));
            }
        }

        public Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
            string? runId, string? addresseeType, CancellationToken cancellationToken)
        {
            var query = Items.Where(i => i.Status == AgentInteractionStatuses.Pending);

            if (!string.IsNullOrWhiteSpace(runId))
            {
                query = query.Where(i => i.RunId == runId);
            }

            if (!string.IsNullOrWhiteSpace(addresseeType))
            {
                query = query.Where(i => i.AddresseeType == addresseeType);
            }

            return Task.FromResult<IReadOnlyList<AgentInteraction>>(query.OrderBy(i => i.CreatedAt).ToList());
        }

        public Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
            string nowIso, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(Items
                .Where(i => i.Status == AgentInteractionStatuses.Pending
                            && i.TimeoutAt is not null
                            && string.CompareOrdinal(i.TimeoutAt, nowIso) <= 0)
                .OrderBy(i => i.TimeoutAt)
                .ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingAuditRepository : IAuditRepository
    {
        public List<AuditRecord> Records { get; } = [];

        public Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingRunOutbox : IRunOutbox
    {
        public List<(string Operation, string RunId, string? Payload)> Enqueued { get; } = [];

        public Task EnqueueAsync(
            string operation,
            string runId,
            string? payload = null,
            DateTimeOffset? visibleAfter = null,
            CancellationToken ct = default)
        {
            Enqueued.Add((operation, runId, payload));
            return Task.CompletedTask;
        }

        public Task<OutboxEntry?> TryClaimNextAsync(string workerId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkCompletedAsync(string entryId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkFailedAsync(string entryId, string error, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<string>> ListStuckRunIdsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubCorrelationContext : ICorrelationContext
    {
        public StubCorrelationContext(string correlationId)
        {
            CorrelationId = correlationId;
        }

        public string CorrelationId { get; }
    }

    private sealed class NoOpWorkflowMetrics : IWorkflowMetrics
    {
        public void RunStarted(string workflowId, string workflowName) { }
        public void RunCompleted(string workflowId, string workflowName, double durationMs) { }
        public void RunFailed(string workflowId, string workflowName, string reason) { }
        public void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded) { }
        public void ApprovalCreated(string riskLevel) { }
        public void ApprovalDecided(string decision, string riskLevel) { }
        public void WebhookReceived(string source, bool triggered) { }
        public void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded) { }
        public void ModelInvoked(string agentName, string modelId, int inputTokens, int outputTokens, double latencyMs, double costUsd, bool succeeded) { }
        public void ToolPolicyDenied(string agentName, string policyTag, string kind) { }
    }
}
