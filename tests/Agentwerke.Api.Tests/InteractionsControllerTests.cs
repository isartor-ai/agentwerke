using System.Security.Claims;
using Agentwerke.Api.Controllers;
using Agentwerke.Api.Contracts.Runs;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Tests;

public sealed class InteractionsControllerTests
{
    [Fact]
    public async Task List_ReturnsPendingInteractionsAcrossRunsWithWorkflowAndDeliveries()
    {
        var interaction = Pending();
        var controller = Build(
            interactions: new StubInteractions(interaction),
            deliveries: new StubDeliveries(new InteractionDelivery
            {
                InteractionId = interaction.Id,
                Channel = InteractionChannels.Slack,
                Status = InteractionDeliveryStatuses.Failed,
                Attempts = 3,
                LastError = "Slack returned 503"
            }),
            runs: new StubRuns(new WorkflowRun { Id = "run_1", WorkflowName = "Deploy" }));

        var result = await controller.List(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.Single(Assert.IsAssignableFrom<IEnumerable<InteractionSummary>>(ok.Value));
        Assert.Equal("Deploy", summary.WorkflowName);
        var delivery = Assert.Single(summary.Deliveries!);
        Assert.Equal(InteractionChannels.Slack, delivery.Channel);
        Assert.Equal("Slack returned 503", delivery.LastError);
    }

    [Fact]
    public async Task Cancel_ForwardsReasonAndActor()
    {
        var orchestration = new StubOrchestration();
        var controller = Build(orchestration: orchestration);

        var result = await controller.Cancel(
            "int_1", new CancelInteractionRequest("No longer needed"), CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal("No longer needed", orchestration.CancelCommand?.Reason);
        Assert.Equal("operator-1", orchestration.CancelCommand?.CancelledBy);
    }

    [Fact]
    public async Task Cancel_AlreadyAnswered_ReturnsConflictWithActualStatus()
    {
        var controller = Build(orchestration: new StubOrchestration
        {
            Exception = new InteractionNotPendingException(
                "int_1", AgentInteractionStatuses.Answered, InteractionChannels.Slack, "dana")
        });

        var result = await controller.Cancel(
            "int_1", new CancelInteractionRequest("Too late"), CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("slack", System.Text.Json.JsonSerializer.Serialize(conflict.Value), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dana", System.Text.Json.JsonSerializer.Serialize(conflict.Value), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryDelivery_ReturnsProviderResultAndNotFoundMapsTo404()
    {
        var router = new StubRouter(InteractionDeliveryResult.Delivered("message-2"));
        var controller = Build(router: router);
        var success = await controller.RetryDelivery("int_1", "slack", CancellationToken.None);
        Assert.IsType<OkObjectResult>(success);
        Assert.Equal(("int_1", "slack"), router.LastRetry);

        controller = Build(router: new StubRouter(exception: new InteractionNotFoundException("missing")));
        Assert.IsType<NotFoundResult>(
            await controller.RetryDelivery("missing", "slack", CancellationToken.None));
    }

    private static InteractionsController Build(
        IAgentInteractionRepository? interactions = null,
        IInteractionDeliveryRepository? deliveries = null,
        IWorkflowRunRepository? runs = null,
        IWorkflowRunOrchestrationService? orchestration = null,
        IInteractionRouter? router = null) => new(
            interactions ?? new StubInteractions(),
            deliveries ?? new StubDeliveries(),
            runs ?? new StubRuns(),
            orchestration ?? new StubOrchestration(),
            router ?? new StubRouter(InteractionDeliveryResult.Delivered()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "operator-1")], "test"))
                }
            }
        };

    private static AgentInteraction Pending() => new()
    {
        Id = "int_1",
        RunId = "run_1",
        StepId = "step_1",
        FromAgent = "reviewer",
        Kind = AgentInteractionKinds.Question,
        AddresseeType = AgentInteractionAddresseeTypes.Human,
        Blocking = true,
        Prompt = "Ship?",
        Status = AgentInteractionStatuses.Pending,
        RequestedChannels = [InteractionChannels.Slack],
        CreatedAt = DateTimeOffset.UtcNow.ToString("o")
    };

    private sealed class StubInteractions(params AgentInteraction[] items) : IAgentInteractionRepository
    {
        public Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(string? runId, string? addresseeType, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AgentInteraction>>(items.Where(i =>
                (runId is null || i.RunId == runId) &&
                (addresseeType is null || i.AddresseeType == addresseeType)).ToArray());
        public Task<AgentInteraction?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(items.FirstOrDefault(i => i.Id == id));
        public Task AddAsync(AgentInteraction interaction, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(string runId, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(string runId, string? from, CancellationToken ct) => throw new NotImplementedException();
        public Task<AgentInteraction?> GetPendingForRunAsync(string runId, CancellationToken ct) => throw new NotImplementedException();
        public Task<InteractionTransitionResult> TryTransitionAsync(string id, string status, string? response, string? by, string? channel, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(string now, CancellationToken ct) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubDeliveries(params InteractionDelivery[] items) : IInteractionDeliveryRepository
    {
        public Task<IReadOnlyList<InteractionDelivery>> GetByInteractionAsync(string id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InteractionDelivery>>(items.Where(i => i.InteractionId == id).ToArray());
        public Task UpsertAsync(InteractionDelivery delivery, CancellationToken ct) => throw new NotImplementedException();
        public Task<InteractionDelivery?> GetByChannelMessageAsync(string channel, string id, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubRuns(params WorkflowRun[] items) : IWorkflowRunRepository
    {
        public Task<WorkflowRun?> GetRunAsync(string id, CancellationToken ct) => Task.FromResult(items.FirstOrDefault(i => i.Id == id));
        public Task<WorkflowRun> CreatePendingRunAsync(string a, string b, string c, string d, string? e, List<string> f, string? g, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateRunStatusAsync(string a, string b, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateCurrentStepAsync(string a, string? b, CancellationToken ct) => throw new NotImplementedException();
        public Task IncrementPendingApprovalsAsync(string a, CancellationToken ct) => throw new NotImplementedException();
        public Task DecrementPendingApprovalsAsync(string a, CancellationToken ct) => throw new NotImplementedException();
        public Task AppendEventAsync(string a, string b, string c, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubRouter(InteractionDeliveryResult? result = null, Exception? exception = null) : IInteractionRouter
    {
        public (string, string)? LastRetry { get; private set; }
        public Task RouteAsync(AgentInteraction interaction, CancellationToken ct) => throw new NotImplementedException();
        public Task<InteractionDeliveryResult> RetryAsync(string id, string channel, CancellationToken ct)
        {
            if (exception is not null) throw exception;
            LastRetry = (id, channel);
            return Task.FromResult(result!);
        }
    }

    private sealed class StubOrchestration : IWorkflowRunOrchestrationService
    {
        public Exception? Exception { get; init; }
        public CancelInteractionCommand? CancelCommand { get; private set; }
        public Task<CancelInteractionResult> CancelInteractionAsync(CancelInteractionCommand command, CancellationToken ct = default)
        {
            if (Exception is not null) throw Exception;
            CancelCommand = command;
            return Task.FromResult(new CancelInteractionResult("run_1", command.InteractionId, AgentInteractionStatuses.Cancelled));
        }
        public Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<AnswerInteractionResult> AnswerInteractionAsync(AnswerInteractionCommand command, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RejectInteractionResult> RejectInteractionAsync(RejectInteractionCommand command, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ExpireInteractionResult> ExpireInteractionAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ResumeExternalRunResult> ResumeExternalRunAsync(ResumeExternalRunCommand command, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<RecoverRunResult> RecoverRunAsync(string id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
