namespace Autofac.Domain.Persistence;

public sealed class WorkflowRun
{
    public Guid Id { get; set; }

    public Guid WorkflowDefinitionId { get; set; }

    public string Status { get; set; } = "created";

    public string? Initiator { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public ICollection<WorkflowEvent> Events { get; set; } = new List<WorkflowEvent>();

    public ICollection<ApprovalRequest> Approvals { get; set; } = new List<ApprovalRequest>();

    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();

    public ICollection<PolicyDecision> PolicyDecisions { get; set; } = new List<PolicyDecision>();
}
