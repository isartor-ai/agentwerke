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

    /// <summary>For tool_access interactions (#202): the tool the agent asked for.</summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// For tool_access interactions (#202): the model's stated intent — a truncated JSON
    /// summary of the tool input it attempted — so an operator can judge the request.
    /// </summary>
    public string? Intent { get; set; }

    /// <summary>Optional choices offered to the responder.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>Channels on which the interaction should be delivered.</summary>
    public List<string> RequestedChannels { get; set; } = new();

    /// <summary>Links a request to its reply (e.g. agent.request ↔ its result).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>One of <see cref="AgentInteractionStatuses"/>.</summary>
    public string Status { get; set; } = AgentInteractionStatuses.Pending;

    public string? Response { get; set; }

    public string? RespondedBy { get; set; }

    public string? RespondedAt { get; set; }

    public string? RespondedChannel { get; set; }

    /// <summary>ISO-8601 timestamp after which the interaction expires; null means never.</summary>
    public string? TimeoutAt { get; set; }

    /// <summary>Action to take when the interaction expires.</summary>
    public string? ExpiresAction { get; set; }

    /// <summary>Answer supplied on expiry when <see cref="ExpiresAction"/> is default_answer.</summary>
    public string? DefaultAnswer { get; set; }

    public string? CancelledAt { get; set; }

    public string? CancelledBy { get; set; }

    public string? ResumedAt { get; set; }

    /// <summary>Current depth of an agent-to-agent delegation chain.</summary>
    public int DelegationDepth { get; set; }

    /// <summary>Optimistic concurrency token for interaction state transitions.</summary>
    public int Version { get; set; }

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
    /// A confirmation boundary: the agent must not proceed without an explicit approve/reject.
    /// Unlike question/choice, a rejection fails the step rather than feeding text back to the model.
    /// </summary>
    public const string Confirm = "confirm";

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

    /// <summary>Timed out / abandoned without an answer; written by InteractionTimeoutSweeper.</summary>
    public const string Expired = "expired";

    /// <summary>A confirmation the responder explicitly declined. The step fails.</summary>
    public const string Rejected = "rejected";

    /// <summary>Withdrawn by the originating agent, an operator, or a cancelled run.</summary>
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) =>
        status is Answered or Rejected or Expired or Cancelled or Posted;
}

public static class InteractionChannels
{
    public const string Ui = "ui";
    public const string Slack = "slack";
    public const string Teams = "teams";
    public const string Webhook = "webhook";
    public const string Agent = "agent";
}

public static class InteractionExpiryActions
{
    public const string Fail = "fail";
    public const string Continue = "continue";
    public const string DefaultAnswer = "default_answer";
}
