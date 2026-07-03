namespace Agentwerke.Domain.Persistence;

/// <summary>
/// Immutable record of a user or agent action for compliance and incident review.
/// </summary>
public sealed class AuditRecord
{
    public string Id { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    /// <summary>"user" | "agent" | "system"</summary>
    public string ActorType { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    /// <summary>"success" | "failure" | "escalated" | "rejected"</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>JSON blob with action-specific details.</summary>
    public string? Details { get; set; }

    public string Timestamp { get; set; } = string.Empty;
}
