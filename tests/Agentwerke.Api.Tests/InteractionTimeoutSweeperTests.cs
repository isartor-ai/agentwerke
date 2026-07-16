using Agentwerke.Application.Agents;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Agentwerke.Infrastructure;
using Agentwerke.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentwerke.Api.Tests;

public sealed class InteractionTimeoutSweeperTests
{
    [Fact]
    public void InteractionOptions_DefaultsAreRolloutSafe()
    {
        var options = new InteractionOptions();

        Assert.Null(options.DefaultTimeoutSeconds);
        Assert.Equal(30, options.SweepIntervalSeconds);
    }

    [Fact]
    public void AddAgentwerkeInfrastructure_BindsAndRegistersTimeoutSweeper()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Database=agentwerke;Username=test;Password=test",
                ["Integrations:Interactions:SweepIntervalSeconds"] = "7"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddAgentwerkeInfrastructure(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(InteractionTimeoutSweeper));

        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            7,
            provider.GetRequiredService<IOptions<InteractionOptions>>().Value.SweepIntervalSeconds);
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public async Task SweepOnceAsync_DrainsDueBatchAndIsolatesRowFailures()
    {
        var now = new DateTimeOffset(2026, 7, 15, 13, 30, 0, TimeSpan.Zero);
        var repository = new DueInteractionRepository(
            Interaction("poison", now.AddMinutes(-3)),
            Interaction("healthy", now.AddMinutes(-2)),
            Interaction("answered-race", now.AddMinutes(-1)),
            Interaction("future", now.AddMinutes(1)),
            Interaction("no-timeout", null));
        var orchestration = new ExpiryOrchestrationService();

        var services = new ServiceCollection();
        services.AddScoped<IAgentInteractionRepository>(_ => repository);
        services.AddScoped<IWorkflowRunOrchestrationService>(_ => orchestration);
        using var provider = services.BuildServiceProvider();

        var worker = new InteractionTimeoutSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InteractionTimeoutSweeper>.Instance,
            Options.Create(new InteractionOptions()),
            new FixedTimeProvider(now));

        await worker.SweepOnceAsync(CancellationToken.None);

        Assert.Equal(now.ToString("o"), repository.LastNowIso);
        Assert.Equal(["poison", "healthy", "answered-race"], orchestration.Attempts);
    }

    private static AgentInteraction Interaction(string id, DateTimeOffset? timeout) => new()
    {
        Id = id,
        RunId = "run-1",
        FromAgent = "test-agent",
        Kind = AgentInteractionKinds.Question,
        AddresseeType = AgentInteractionAddresseeTypes.Human,
        Blocking = true,
        Prompt = "Need an answer",
        Status = AgentInteractionStatuses.Pending,
        TimeoutAt = timeout?.ToString("o"),
        CreatedAt = "2026-07-15T13:00:00.0000000+00:00"
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DueInteractionRepository(params AgentInteraction[] interactions)
        : IAgentInteractionRepository
    {
        public string? LastNowIso { get; private set; }

        public Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
            string nowIso,
            CancellationToken cancellationToken)
        {
            LastNowIso = nowIso;
            return Task.FromResult<IReadOnlyList<AgentInteraction>>(interactions
                .Where(interaction => interaction.Status == AgentInteractionStatuses.Pending
                    && interaction.TimeoutAt is not null
                    && string.CompareOrdinal(interaction.TimeoutAt, nowIso) <= 0)
                .ToArray());
        }

        public Task AddAsync(AgentInteraction interaction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInteraction>> GetByRunAsync(
            string runId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInteraction>> GetPostsForRunAsync(
            string runId,
            string? fromFilter,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AgentInteraction?> GetByIdAsync(
            string interactionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AgentInteraction?> GetPendingForRunAsync(
            string runId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<InteractionTransitionResult> TryTransitionAsync(
            string interactionId,
            string toStatus,
            string? response,
            string? respondedBy,
            string? respondedChannel,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
            string? runId,
            string? addresseeType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ExpiryOrchestrationService : IWorkflowRunOrchestrationService
    {
        public List<string> Attempts { get; } = [];

        public Task<ExpireInteractionResult> ExpireInteractionAsync(
            string interactionId,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(interactionId);
            return interactionId switch
            {
                "poison" => throw new InvalidOperationException("bad row"),
                "answered-race" => throw new InteractionNotPendingException(
                    interactionId, AgentInteractionStatuses.Answered),
                _ => Task.FromResult(new ExpireInteractionResult(
                    "run-1", interactionId, AgentInteractionStatuses.Expired))
            };
        }

        public Task<StartRunResult> StartRunAsync(
            StartRunCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResumeRunResult> ResumeRunAsync(
            ResumeRunCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnswerInteractionResult> AnswerInteractionAsync(
            AnswerInteractionCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RejectInteractionResult> RejectInteractionAsync(
            RejectInteractionCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CancelInteractionResult> CancelInteractionAsync(
            CancelInteractionCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ResumeExternalRunResult> ResumeExternalRunAsync(
            ResumeExternalRunCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RecoverRunResult> RecoverRunAsync(
            string runId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
