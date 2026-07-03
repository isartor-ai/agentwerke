using Agentwerke.Application.Agents;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tools;

/// <summary>
/// Lets an agent pause mid-step to ask a human a free-form question and receive the answer (#192).
/// First call persists a pending <see cref="AgentInteraction"/> and throws
/// <see cref="AgentInteractionRequiredException"/> to suspend the run. When the human answers, the
/// step re-runs (Phase 2 strategy) and this tool finds the answered interaction and returns it, so
/// the agent proceeds without re-asking.
/// </summary>
public sealed class HumanAskTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentInteractionRepository _interactions;

    public HumanAskTool(IAgentInteractionRepository interactions) => _interactions = interactions;

    public string Name => "human.ask";

    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("question", "string", "The question to ask the human. The run pauses until it is answered.", Required: true),
        new("options", "string", "Optional comma-separated choices to offer the human.", Required: false),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("question", out var question) || string.IsNullOrWhiteSpace(question))
        {
            throw new InvalidOperationException("Tool input is missing required field 'question'.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var question = input["question"].Trim();

        // On a re-run the agent asks the same question again; match by run + kind + prompt so we
        // return the answer instead of asking twice. StepId is not a key — it changes per re-run.
        var existing = (await _interactions.GetByRunAsync(context.RunId, cancellationToken))
            .LastOrDefault(i =>
                i.AddresseeType == AgentInteractionAddresseeTypes.Human &&
                (i.Kind == AgentInteractionKinds.Question || i.Kind == AgentInteractionKinds.Choice) &&
                string.Equals(i.Prompt, question, StringComparison.Ordinal));

        if (existing is not null)
        {
            if (existing.Status == AgentInteractionStatuses.Answered)
            {
                return new AgentToolExecutionResult(
                    Succeeded: true,
                    Output: $"Human answered: {existing.Response}",
                    FailureReason: null);
            }

            // Still pending — keep waiting.
            throw new AgentInteractionRequiredException(existing.Id, question);
        }

        var options = ParseOptions(input);
        var interaction = new AgentInteraction
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = context.RunId,
            StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
            FromAgent = context.AgentName,
            Kind = options.Count > 0 ? AgentInteractionKinds.Choice : AgentInteractionKinds.Question,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Addressee = null,
            Blocking = true,
            Prompt = question,
            Options = options,
            Status = AgentInteractionStatuses.Pending,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        await _interactions.AddAsync(interaction, cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);

        throw new AgentInteractionRequiredException(interaction.Id, question);
    }

    private static List<string> ParseOptions(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("options", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

/// <summary>
/// Sends a human a non-blocking heads-up from an agent (#192). Persists a <c>notify</c>
/// interaction; the run does not suspend.
/// </summary>
public sealed class HumanNotifyTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentInteractionRepository _interactions;

    public HumanNotifyTool(IAgentInteractionRepository interactions) => _interactions = interactions;

    public string Name => "human.notify";

    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("message", "string", "A short heads-up for the human. Does not pause the run.", Required: true),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("message", out var message) || string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Tool input is missing required field 'message'.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var interaction = new AgentInteraction
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = context.RunId,
            StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
            FromAgent = context.AgentName,
            Kind = AgentInteractionKinds.Notify,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Addressee = null,
            Blocking = false,
            Prompt = input["message"].Trim(),
            Status = AgentInteractionStatuses.Posted,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        await _interactions.AddAsync(interaction, cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: "Notified the human.",
            FailureReason: null);
    }
}
