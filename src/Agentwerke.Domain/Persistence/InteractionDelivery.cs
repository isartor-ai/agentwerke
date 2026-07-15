namespace Agentwerke.Domain.Persistence;

/// <summary>Tracks delivery of an interaction to one requested channel.</summary>
public sealed class InteractionDelivery
{
    public string Id { get; set; } = string.Empty;

    public string InteractionId { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public string Status { get; set; } = InteractionDeliveryStatuses.Pending;

    public string? ChannelMessageId { get; set; }

    public int Attempts { get; set; }

    public string? LastAttemptAt { get; set; }

    public string? LastError { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
}

public static class InteractionDeliveryStatuses
{
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Failed = "failed";

    /// <summary>
    /// The channel cannot carry a response for this interaction kind
    /// (for example, Teams outbound-only in v1).
    /// </summary>
    public const string NotSupported = "not_supported";
}
