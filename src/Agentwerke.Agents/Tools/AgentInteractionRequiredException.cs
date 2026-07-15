namespace Agentwerke.Agents.Tools;

/// <summary>
/// Raised by a blocking tool (e.g. human.ask) when the agent must pause until a human or another
/// agent answers (#192). It unwinds the tool-use turn; the Tool Gateway re-throws it rather than
/// converting it to a failed tool result, so <c>AgentOrchestrator</c> can suspend the run
/// (<c>waiting_user</c>) with the step set to re-run on resume. The pending <c>AgentInteraction</c>
/// is already persisted by the tool that throws.
/// </summary>
public sealed class AgentInteractionRequiredException : Exception
{
    public AgentInteractionRequiredException(string interactionId, string prompt)
        : base($"Agent interaction '{interactionId}' requires a response before the run can continue.")
    {
        InteractionId = interactionId;
        Prompt = prompt;
    }

    public string InteractionId { get; }

    public string Prompt { get; }
}


public sealed class ConfirmationRejectedException : Exception
{
    public ConfirmationRejectedException(string message) : base(message) { }
}
