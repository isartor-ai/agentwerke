using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Logging;

namespace Agentwerke.Application.Agents;

public interface IInteractionChannelResolver
{
    /// <summary>
    /// The channels an interaction should be delivered on, most specific configuration winning. The
    /// UI is always included.
    /// </summary>
    IReadOnlyList<string> Resolve(AgentInteraction interaction, string? workflowName);
}

/// <summary>
/// Decides where an interaction goes (#220), layered most-specific-first:
/// per-interaction request → per-workflow → per-agent → configured default.
/// </summary>
public sealed class InteractionChannelResolver : IInteractionChannelResolver
{
    private readonly IReadOnlyList<IInteractionChannel> _channels;
    private readonly InteractionOptions _options;
    private readonly ILogger<InteractionChannelResolver> _logger;

    public InteractionChannelResolver(
        IEnumerable<IInteractionChannel> channels,
        InteractionOptions options,
        ILogger<InteractionChannelResolver> logger)
    {
        _channels = channels.ToArray();
        _options = options;
        _logger = logger;
    }

    public IReadOnlyList<string> Resolve(AgentInteraction interaction, string? workflowName)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        // The UI is always a target and cannot be configured away. It is the fallback that makes every
        // other channel optional: if Slack is misconfigured or down, the question is still answerable.
        var resolved = new List<string> { InteractionChannels.Ui };

        if (!_options.Enabled)
        {
            return resolved;
        }

        foreach (var requested in SelectLayer(interaction, workflowName))
        {
            if (string.Equals(requested, InteractionChannels.Ui, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (resolved.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var channel = _channels.FirstOrDefault(c =>
                string.Equals(c.ChannelId, requested, StringComparison.OrdinalIgnoreCase));

            // Warn rather than drop silently: a channels:["slack"] that quietly does nothing because
            // Slack is unregistered or disabled is a bad afternoon of debugging.
            if (channel is null)
            {
                _logger.LogWarning(
                    "Interaction requested unknown channel '{Channel}'; dropping it. InteractionId={InteractionId}",
                    requested, interaction.Id);
                continue;
            }

            if (!channel.Enabled)
            {
                _logger.LogWarning(
                    "Interaction requested channel '{Channel}' which is registered but disabled; dropping it. "
                    + "InteractionId={InteractionId}",
                    requested, interaction.Id);
                continue;
            }

            resolved.Add(channel.ChannelId);
        }

        return resolved;
    }

    private IReadOnlyList<string> SelectLayer(AgentInteraction interaction, string? workflowName)
    {
        if (interaction.RequestedChannels.Count > 0)
        {
            return interaction.RequestedChannels;
        }

        if (workflowName is not null
            && _options.ChannelsByWorkflow.TryGetValue(workflowName, out var byWorkflow)
            && byWorkflow.Count > 0)
        {
            return byWorkflow;
        }

        if (!string.IsNullOrWhiteSpace(interaction.FromAgent)
            && _options.ChannelsByAgent.TryGetValue(interaction.FromAgent, out var byAgent)
            && byAgent.Count > 0)
        {
            return byAgent;
        }

        return _options.DefaultChannels;
    }
}
