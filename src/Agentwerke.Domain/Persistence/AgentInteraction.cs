using System.Collections.Generic;

namespace Agentwerke.Domain.Persistence;

/// <summary>
/// A run-scoped, persisted unit of agent communication (#192). One primitive covers every
/// direction: an agent posting to the run bus, one agent asking/delegating to another, and
/// (later phases) an agent asking a human. Approvals stay in their own table for now; they are
/// conceptually <see cref="AgentInteractionKinds.Approval"/> and will be folded in as a follow-up.
/// </summary>
public sealed class AgentInteraction
{
    public string Id { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    /// <summary>Node/step that produced the interaction, so the UI can anchor it to execution.</summary>
    public string? StepId { get; set; }

    /// <summary>The agent that raised the interaction.</summary>
    public string FromAgent { get; set; } = string.Empty;

    /// <summary>One of <see cref="AgentInteractionKinds"/>.</summary>
    public string Kind { get; set; } = AgentInteractionKinds.Post;

    /// <summary><c>human</c> or <c>agent</c> — who is expected to read/answer.</summary>
    public string AddresseeType { get; set; } = AgentInteractionAddresseeTypes.Agent;

    /// <summary>Agent name or human role/user; null means broadcast to the run.</summary>
    public string? Addressee { get; set; }

    /// <summary>Whether the sender suspends until this interaction is answered.</summary>
    public bool Blocking { get; set; }

    /// <summary>The message / question text.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Optional choices offered to the responder.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>Links a request to its reply (e.g. agent.request ↔ its result).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>One of <see cref="AgentInteractionStatuses"/>.</summary>
    public string Status { get; set; } = AgentInteractionStatuses.Pending;

    public string? Response { get; set; }

    public string? RespondedBy { get; set; }

    public string? RespondedAt { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
}

public static class AgentInteractionKinds
{
    public const string Post = "post";
    public const string Question = "question";
    public const string Choice = "choice";
    public const string Notify = "notify";
    public const string AgentRequest = "agent_request";
    public const string Approval = "approval";

    /// <summary>
    /// An agent needs a tool that exists but is not allowed for its step (#202). Blocking; a
    /// human replies "approve" to allow the tool for the rest of the run, or with guidance text
    /// that is fed back to the agent as the tool result.
    /// </summary>
    public const string ToolAccess = "tool_access";
}

public static class AgentInteractionAddresseeTypes
{
    public const string Human = "human";
    public const string Agent = "agent";
}

public static class AgentInteractionStatuses
{
    /// <summary>Awaiting an answer (blocking interactions).</summary>
    public const string Pending = "pending";

    /// <summary>Answered by a human or agent.</summary>
    public const string Answered = "answered";

    /// <summary>A fire-and-forget message that needs no answer.</summary>
    public const string Posted = "posted";

    /// <summary>Timed out / abandoned without an answer.</summary>
    public const string Expired = "expired";
}
