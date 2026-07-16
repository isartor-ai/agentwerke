using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace Agentwerke.Application.Agents;

public interface IInteractionRouter
{
    /// <summary>
    /// Delivers an interaction to every resolved channel, recording one delivery row each. Never
    /// throws: a channel outage must not fail the agent step that asked the question.
    /// </summary>
    Task RouteAsync(AgentInteraction interaction, CancellationToken cancellationToken);

    /// <summary>Re-attempts one channel's delivery. Backs the operator retry action (#227, #229).</summary>
    Task<InteractionDeliveryResult> RetryAsync(
        string interactionId,
        string channel,
        CancellationToken cancellationToken);
}

/// <summary>
/// Fans an interaction out to its resolved channels (#220).
///
/// Modelled on ConnectorApprovalNotifier, with one deliberate difference: that notifier swallows
/// failures silently, which is fine for a notification and not fine for a question a run is parked on.
/// Every attempt here leaves a delivery row, so a failed Slack post is visible and retryable rather
/// than lost.
/// </summary>
public sealed class InteractionRouter : IInteractionRouter
{
    private readonly IReadOnlyList<IInteractionChannel> _channels;
    private readonly IInteractionChannelResolver _resolver;
    private readonly IInteractionDeliveryRepository _deliveries;
    private readonly IAgentInteractionRepository _interactions;
    private readonly IWorkflowRunRepository _runs;
    private readonly InteractionOptions _options;
    private readonly ILogger<InteractionRouter> _logger;

    public InteractionRouter(
        IEnumerable<IInteractionChannel> channels,
        IInteractionChannelResolver resolver,
        IInteractionDeliveryRepository deliveries,
        IAgentInteractionRepository interactions,
        IWorkflowRunRepository runs,
        InteractionOptions options,
        ILogger<InteractionRouter> logger)
    {
        _channels = channels.ToArray();
        _resolver = resolver;
        _deliveries = deliveries;
        _interactions = interactions;
        _runs = runs;
        _options = options;
        _logger = logger;
    }

    public async Task RouteAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        try
        {
            var run = await _runs.GetRunAsync(interaction.RunId, cancellationToken);
            var resolved = _resolver.Resolve(interaction, run?.WorkflowName);

            var deliverable = new List<IInteractionChannel>();

            foreach (var channelId in resolved)
            {
                // The UI needs no delivery — the persisted interaction is what the UI reads. Record the
                // row anyway so every surface can show "available in: ui, slack" uniformly.
                if (string.Equals(channelId, InteractionChannels.Ui, StringComparison.OrdinalIgnoreCase))
                {
                    await RecordAsync(interaction.Id, channelId, InteractionDeliveryResult.Delivered(), 0, cancellationToken);
                    continue;
                }

                var channel = _channels.FirstOrDefault(c =>
                    string.Equals(c.ChannelId, channelId, StringComparison.OrdinalIgnoreCase));
                if (channel is null)
                {
                    continue;
                }

                if (interaction.Blocking && !channel.CanCarryResponse)
                {
                    await RecordAsync(
                        interaction.Id,
                        channelId,
                        InteractionDeliveryResult.NotSupported(
                            $"Channel '{channelId}' cannot accept responses; answer in the Agentwerke UI."),
                        0,
                        cancellationToken);
                    continue;
                }

                // Persist the row as pending before any network call, so a crash mid-fan-out leaves a
                // retryable record rather than a silently undelivered question.
                await RecordAsync(interaction.Id, channelId, null, 0, cancellationToken);
                deliverable.Add(channel);
            }

            foreach (var channel in deliverable)
            {
                await DeliverWithRetryAsync(interaction, channel, cancellationToken);
            }

            await WarnIfUnreachableAsync(interaction, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Routing is best-effort by design. The interaction is already persisted and answerable in
            // the UI, so a routing bug must never surface as a failed agent step.
            _logger.LogError(
                ex,
                "Interaction routing failed. InteractionId={InteractionId} RunId={RunId}",
                interaction.Id, interaction.RunId);
        }
    }

    public async Task<InteractionDeliveryResult> RetryAsync(
        string interactionId,
        string channelId,
        CancellationToken cancellationToken)
    {
        var interaction = await _interactions.GetByIdAsync(interactionId, cancellationToken)
            ?? throw new InteractionNotFoundException(interactionId);

        var channel = _channels.FirstOrDefault(c =>
            string.Equals(c.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Channel '{channelId}' is not registered.");

        // Retrying a settled question would post a message nobody can act on.
        if (AgentInteractionStatuses.IsTerminal(interaction.Status))
        {
            return InteractionDeliveryResult.Failed(
                $"Interaction is '{interaction.Status}'; delivery was not retried.");
        }

        return await DeliverWithRetryAsync(interaction, channel, cancellationToken);
    }

    private async Task<InteractionDeliveryResult> DeliverWithRetryAsync(
        AgentInteraction interaction,
        IInteractionChannel channel,
        CancellationToken cancellationToken)
    {
        var request = new InteractionDeliveryRequest(
            interaction,
            interaction.RunId,
            WorkflowName: null,
            RespondUrl: BuildRespondUrl(interaction));

        InteractionDeliveryResult result = InteractionDeliveryResult.Failed("No delivery attempt was made.");

        for (var attempt = 1; attempt <= Math.Max(1, _options.MaxDeliveryAttempts); attempt++)
        {
            try
            {
                result = await channel.DeliverAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A channel that throws is a channel that failed. Adapters should return Failed, but
                // the router must not depend on their discipline.
                result = InteractionDeliveryResult.Failed(ex.Message);
            }

            await RecordAsync(interaction.Id, channel.ChannelId, result, attempt, cancellationToken);

            if (!string.Equals(result.Status, InteractionDeliveryStatuses.Failed, StringComparison.Ordinal))
            {
                return result;
            }

            if (attempt < _options.MaxDeliveryAttempts)
            {
                await Task.Delay(BackoffFor(attempt), cancellationToken);
            }
        }

        _logger.LogWarning(
            "Interaction delivery failed after {Attempts} attempts. InteractionId={InteractionId} "
            + "Channel={Channel} Error={Error}",
            _options.MaxDeliveryAttempts, interaction.Id, channel.ChannelId, result.Error);

        return result;
    }

    /// <summary>
    /// Flags the case a human should know about: a blocking question that reached no channel able to
    /// answer it. The run is not stuck — the UI can still answer — but nobody has been told.
    /// </summary>
    private async Task WarnIfUnreachableAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        if (!interaction.Blocking)
        {
            return;
        }

        var rows = await _deliveries.GetByInteractionAsync(interaction.Id, cancellationToken);
        var anyResponsive = rows.Any(r =>
            string.Equals(r.Status, InteractionDeliveryStatuses.Delivered, StringComparison.Ordinal)
            && !string.Equals(r.Channel, InteractionChannels.Ui, StringComparison.OrdinalIgnoreCase));

        var externalAttempted = rows.Any(r =>
            !string.Equals(r.Channel, InteractionChannels.Ui, StringComparison.OrdinalIgnoreCase));

        if (externalAttempted && !anyResponsive)
        {
            _logger.LogError(
                "Blocking interaction reached no external channel that can carry a response; it is "
                + "answerable in the UI only. InteractionId={InteractionId} RunId={RunId}",
                interaction.Id, interaction.RunId);
        }
    }

    private TimeSpan BackoffFor(int attempt)
    {
        var exponential = _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * _options.RetryBaseDelayMs;
        return TimeSpan.FromMilliseconds(exponential + jitter);
    }

    private string BuildRespondUrl(AgentInteraction interaction) =>
        string.IsNullOrWhiteSpace(_options.RespondUrlBase)
            ? string.Empty
            : $"{_options.RespondUrlBase.TrimEnd('/')}/runs/{interaction.RunId}";

    private Task RecordAsync(
        string interactionId,
        string channelId,
        InteractionDeliveryResult? result,
        int attempts,
        CancellationToken cancellationToken)
    {
        return _deliveries.UpsertAsync(
            new InteractionDelivery
            {
                InteractionId = interactionId,
                Channel = channelId,
                Status = result?.Status ?? InteractionDeliveryStatuses.Pending,
                ChannelMessageId = result?.ChannelMessageId,
                Attempts = attempts,
                LastAttemptAt = attempts > 0 ? DateTimeOffset.UtcNow.ToString("o") : null,
                LastError = result?.Error,
                CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            },
            cancellationToken);
    }
}
