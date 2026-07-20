using Agentwerke.Domain.Persistence;

namespace Agentwerke.Api.Contracts.Runs;

/// <summary>One entry in a run's agent conversation (#192): a coordination post, a delegation, or a human ask.</summary>
public sealed record InteractionSummary(
    string Id,
    string RunId,
    string? StepId,
    string From,
    string Kind,
    string AddresseeType,
    string? Addressee,
    bool Blocking,
    string Prompt,
    IReadOnlyList<string> Options,
    string Status,
    string? Response,
    string? RespondedBy,
    string? RespondedAt,
    string CreatedAt,
    /// <summary>For tool_access interactions (#202): the tool the agent asked for.</summary>
    string? ToolName = null,
    /// <summary>For tool_access interactions (#202): the model's stated intent (truncated tool input).</summary>
    string? Intent = null,
    IReadOnlyList<string>? RequestedChannels = null,
    string? RespondedChannel = null,
    string? TimeoutAt = null,
    string? CancelledAt = null,
    string? ResumedAt = null,
    int DelegationDepth = 0,
    IReadOnlyList<InteractionDeliverySummary>? Deliveries = null,
    string? WorkflowName = null,
    string? CorrelationId = null,
    string? ExpiresAction = null,
    string? DefaultAnswer = null);

public sealed record InteractionDeliverySummary(
    string Channel,
    string Status,
    string? ChannelMessageId,
    int Attempts,
    string? LastAttemptAt,
    string? LastError);

public static class InteractionSummaryMappings
{
    public static InteractionSummary ToSummary(
        AgentInteraction interaction,
        IReadOnlyList<InteractionDelivery>? deliveries = null,
        string? workflowName = null) => new(
            interaction.Id,
            interaction.RunId,
            interaction.StepId,
            interaction.FromAgent,
            interaction.Kind,
            interaction.AddresseeType,
            interaction.Addressee,
            interaction.Blocking,
            interaction.Prompt,
            interaction.Options,
            interaction.Status,
            interaction.Response,
            interaction.RespondedBy,
            interaction.RespondedAt,
            interaction.CreatedAt,
            interaction.ToolName,
            interaction.Intent,
            interaction.RequestedChannels,
            interaction.RespondedChannel,
            interaction.TimeoutAt,
            interaction.CancelledAt,
            interaction.ResumedAt,
            interaction.DelegationDepth,
            (deliveries ?? []).Select(ToSummary).ToArray(),
            workflowName,
            interaction.CorrelationId,
            interaction.ExpiresAction,
            interaction.DefaultAnswer);

    private static InteractionDeliverySummary ToSummary(InteractionDelivery delivery) => new(
        delivery.Channel,
        delivery.Status,
        delivery.ChannelMessageId,
        delivery.Attempts,
        delivery.LastAttemptAt,
        delivery.LastError);
}
