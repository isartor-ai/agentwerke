using System.Collections.Generic;

namespace Agentwerke.Domain.Persistence;

public sealed class ApprovalRequest
{
    public string Id { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public string WorkflowName { get; set; } = string.Empty;

    public string ActionRequested { get; set; } = string.Empty;

    public string Requester { get; set; } = string.Empty;

    public string AgentName { get; set; } = string.Empty;

    public string PolicyRationale { get; set; } = string.Empty;

    /// <summary>Name of the artifact the preceding step produced, for the approval card to render (#134).</summary>
    public string? ArtifactName { get; set; }

    public int RiskScore { get; set; }

    public string RiskLevel { get; set; } = "low";

    public List<string> RiskFactors { get; set; } = new();

    public List<string> AffectedSystems { get; set; } = new();

    public string SlaDeadline { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public string Priority { get; set; } = "normal";

    public string? DecisionComment { get; set; }

    public string? DecidedAt { get; set; }

    public string? DecidedBy { get; set; }
}
