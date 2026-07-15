using Agentwerke.Api.Controllers;
using Agentwerke.Api.Contracts.Runs;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Security.Claims;

namespace Agentwerke.Api.Tests;

public sealed class RunsControllerTests
{
    [Fact]
    public void RunEventStreamState_BuildFreshEventsQuery_UsesStableOrderingAndDeliveredOffset()
    {
        var state = new RunEventStreamState("run_1");
        var events = new[]
        {
            new WorkflowEvent { Id = "evt_2", RunId = "run_1", Type = "log", Message = "second", CreatedAt = "2026-07-08T11:00:00.0000000Z" },
            new WorkflowEvent { Id = "evt_3", RunId = "run_1", Type = "log", Message = "third", CreatedAt = "2026-07-08T11:00:00.0000000Z" },
            new WorkflowEvent { Id = "evt_1", RunId = "run_1", Type = "log", Message = "first", CreatedAt = "2026-07-08T10:59:59.0000000Z" },
            new WorkflowEvent { Id = "evt_other", RunId = "run_2", Type = "log", Message = "other", CreatedAt = "2026-07-08T10:00:00.0000000Z" }
        }.AsQueryable();

        var ordered = state.BuildFreshEventsQuery(events).ToList();

        Assert.Collection(
            ordered,
            evt => Assert.Equal("evt_1", evt.Id),
            evt => Assert.Equal("evt_2", evt.Id),
            evt => Assert.Equal("evt_3", evt.Id));

        state.MarkDelivered(ordered.Take(2).ToList());

        var remaining = state.BuildFreshEventsQuery(events).ToList();

        Assert.Collection(
            remaining,
            evt => Assert.Equal("evt_3", evt.Id));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(4, false)]
    public void RunEventStreamState_ShouldCheckRunStatus_OnlyWhenStreamIsIdle(int freshEventCount, bool expected)
    {
        Assert.Equal(expected, RunEventStreamState.ShouldCheckRunStatus(freshEventCount));
    }

    [Fact]
    public async Task Start_UsesAuthenticatedPrincipalAsInitiator()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", "user-123")],
                        "test"))
                }
            }
        };

        var result = await controller.Start(new StartRunRequest("wf_1"));

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<StartRunResponse>(accepted.Value);
        Assert.Equal("run_1", response.RunId);
        Assert.NotNull(orchestration.StartCommand);
        Assert.Equal("user-123", orchestration.StartCommand!.Initiator);
    }

    [Fact]
    public async Task Start_ForwardsCustomInputsToStartCommand()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", "operator-456")],
                        "test"))
                }
            }
        };

        var result = await controller.Start(new StartRunRequest(
            "wf_1",
            new Dictionary<string, string>
            {
                ["branch_name"] = "feature/issue-142",
                ["repository"] = "isartor-ai/agentwerke"
            }));

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(orchestration.StartCommand);
        Assert.NotNull(orchestration.StartCommand!.Inputs);
        Assert.Equal("feature/issue-142", orchestration.StartCommand.Inputs["branch_name"]);
        Assert.Equal("isartor-ai/agentwerke", orchestration.StartCommand.Inputs["repository"]);
    }

    [Fact]
    public async Task GetEvidencePack_KnownRun_ReturnsGeneratedPack()
    {
        var pack = SampleEvidencePack("run_1");
        var controller = BuildEvidenceController(new FakeEvidencePackService(pack));

        var result = await controller.GetEvidencePack("run_1");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(pack, ok.Value);
    }

    [Fact]
    public async Task DownloadEvidencePack_KnownRun_ReturnsJsonFile()
    {
        var controller = BuildEvidenceController(new FakeEvidencePackService(SampleEvidencePack("run_1")));

        var result = await controller.DownloadEvidencePack("run_1");

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", file.ContentType);
        Assert.Equal("run_1-evidence-pack.json", file.FileDownloadName);
        var json = JsonSerializer.Deserialize<JsonElement>(file.FileContents);
        Assert.Equal("run_1", json.GetProperty("runId").GetString());
        Assert.Equal(EvidencePackBuilder.SchemaVersion, json.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public async Task GetEvidencePack_UnknownRun_ReturnsNotFound()
    {
        var controller = BuildEvidenceController(new FakeEvidencePackService());

        var result = await controller.GetEvidencePack("missing");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ResumeExternal_UsesAuthenticatedPrincipalWhenRequesterOmitsResumedBy()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", "operator-123")],
                        "test"))
                }
            }
        };

        var result = await controller.ResumeExternal("run_1", new ResumeExternalRunRequest(
            CorrelationKey: "feature/external-wait",
            Payload: new Dictionary<string, string> { ["pull_number"] = "42" },
            ResumedBy: null));

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(orchestration.ResumeExternalCommand);
        Assert.Equal("run_1", orchestration.ResumeExternalCommand!.RunId);
        Assert.Equal("feature/external-wait", orchestration.ResumeExternalCommand.CorrelationKey);
        Assert.Equal("42", orchestration.ResumeExternalCommand.Payload["pull_number"]);
        Assert.Equal("operator-123", orchestration.ResumeExternalCommand.ResumedBy);
        Assert.NotNull(accepted.Value);
    }

    [Fact]
    public async Task AnswerInteraction_ForwardsCommandWithAuthenticatedPrincipal()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "operator-9")], "test"))
                }
            }
        };

        var result = await controller.AnswerInteraction(
            "run_1", "int_1", new AnswerInteractionRequest("add tests"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(orchestration.AnswerCommand);
        Assert.Equal("run_1", orchestration.AnswerCommand!.RunId);
        Assert.Equal("int_1", orchestration.AnswerCommand.InteractionId);
        Assert.Equal("add tests", orchestration.AnswerCommand.Answer);
        Assert.Equal("operator-9", orchestration.AnswerCommand.AnsweredBy);
    }

    [Fact]
    public async Task AnswerInteraction_WithEmptyAnswer_ReturnsBadRequestWithoutForwarding()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService());

        var result = await controller.AnswerInteraction(
            "run_1", "int_1", new AnswerInteractionRequest("   "), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(orchestration.AnswerCommand);
    }

    [Fact]
    public async Task AnswerInteraction_WithAnswerOutsideOptions_ReturnsBadRequestWithValidOptions()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService
        {
            InteractionException = new InvalidInteractionAnswerException("int_1", ["approve", "reject"])
        };
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService());

        var result = await controller.AnswerInteraction(
            "run_1", "int_1", new AnswerInteractionRequest("maybe"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("approve", JsonSerializer.Serialize(badRequest.Value));
        Assert.Contains("reject", JsonSerializer.Serialize(badRequest.Value));
    }

    [Fact]
    public async Task RejectInteraction_ForwardsReasonAndAuthenticatedPrincipal()
    {
        var orchestration = new CapturingWorkflowRunOrchestrationService();
        var controller = new RunsController(null!, null!, orchestration, new FakeEvidencePackService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "approver-7")], "test"))
                }
            }
        };

        var result = await controller.RejectInteraction(
            "run_1", "int_1", new RejectInteractionRequest("Unsafe rollout"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal("Unsafe rollout", orchestration.RejectCommand?.Reason);
        Assert.Equal("approver-7", orchestration.RejectCommand?.RejectedBy);
    }

    private static RunsController BuildEvidenceController(IEvidencePackService evidencePackService)
    {
        return new RunsController(
            dbContext: null!,
            artifactStorage: null!,
            orchestrationService: new CapturingWorkflowRunOrchestrationService(),
            evidencePackService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static EvidencePack SampleEvidencePack(string runId)
    {
        return new EvidencePack(
            SchemaVersion: EvidencePackBuilder.SchemaVersion,
            RunId: runId,
            GeneratedAt: "2026-06-18T10:00:00.0000000Z",
            Workflow: new EvidenceWorkflow(
                WorkflowId: "wf_1",
                Name: "Workflow",
                Version: "v1",
                DefinitionVersion: "v1",
                BpmnSha256: "abc123",
                HashAlgorithm: "SHA-256"),
            Runtime: new EvidenceRuntime("Agentwerke", CamundaEnabled: false),
            Run: new EvidenceRun(
                RunId: runId,
                Status: "completed",
                RiskLevel: "low",
                RequestedBy: "operator@example.com",
                StartedAt: "2026-06-18T09:00:00.0000000Z",
                CompletedAt: "2026-06-18T09:01:00.0000000Z",
                DurationMs: 60000,
                PendingApprovals: 0,
                CorrelationId: "corr_1",
                Tags: ["test"]),
            AgentSnapshots: [],
            Approvals: [],
            PolicyDecisions: [],
            ToolCalls: [],
            ConnectorCalls: [],
            SandboxExecutions: [],
            ModelUsage: [],
            Artifacts: [],
            AuditLog: [],
            Logs: [],
            RunEvents: [],
            Camunda: null);
    }

    private sealed class CapturingWorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
    {
        public Exception? InteractionException { get; init; }
        public StartRunCommand? StartCommand { get; private set; }
        public ResumeExternalRunCommand? ResumeExternalCommand { get; private set; }
        public AnswerInteractionCommand? AnswerCommand { get; private set; }

        public Task<StartRunResult> StartRunAsync(
            StartRunCommand command,
            CancellationToken cancellationToken = default)
        {
            StartCommand = command;
            return Task.FromResult(new StartRunResult("run_1", command.WorkflowId, "pending", null));
        }

        public Task<ResumeRunResult> ResumeRunAsync(
            ResumeRunCommand command,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<RecoverRunResult> RecoverRunAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<ResumeExternalRunResult> ResumeExternalRunAsync(
            ResumeExternalRunCommand command,
            CancellationToken cancellationToken = default)
        {
            ResumeExternalCommand = command;
            return Task.FromResult(new ResumeExternalRunResult(command.RunId, "pending"));
        }

        public Task<AnswerInteractionResult> AnswerInteractionAsync(
            AnswerInteractionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (InteractionException is not null) throw InteractionException;
            AnswerCommand = command;
            return Task.FromResult(
                new AnswerInteractionResult(command.RunId, command.InteractionId, "pending", command.Channel));
        }

        public RejectInteractionCommand? RejectCommand { get; private set; }

        public Task<RejectInteractionResult> RejectInteractionAsync(
            RejectInteractionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (InteractionException is not null) throw InteractionException;
            RejectCommand = command;
            return Task.FromResult(
                new RejectInteractionResult(command.RunId, command.InteractionId, "pending", command.Channel));
        }

        public CancelInteractionCommand? CancelCommand { get; private set; }

        public Task<CancelInteractionResult> CancelInteractionAsync(
            CancelInteractionCommand command,
            CancellationToken cancellationToken = default)
        {
            CancelCommand = command;
            return Task.FromResult(new CancelInteractionResult("run-1", command.InteractionId, "cancelled"));
        }

        public Task<ExpireInteractionResult> ExpireInteractionAsync(
            string interactionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExpireInteractionResult("run-1", interactionId, "expired"));
    }

    private sealed class FakeEvidencePackService : IEvidencePackService
    {
        private readonly EvidencePack? _pack;

        public FakeEvidencePackService(EvidencePack? pack = null)
        {
            _pack = pack;
        }

        public Task<EvidencePack> GenerateAsync(
            string runId,
            CancellationToken cancellationToken = default)
        {
            if (_pack is null || !string.Equals(_pack.RunId, runId, StringComparison.Ordinal))
            {
                throw new EvidencePackNotFoundException(runId);
            }

            return Task.FromResult(_pack);
        }
    }
}
