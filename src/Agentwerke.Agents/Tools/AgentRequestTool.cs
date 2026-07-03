using System.Text;
using Agentwerke.Agents.Models;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;

// IAgentRegistry and AgentProfile live in the Agentwerke.Agents root namespace.
using Agentwerke.Agents;

namespace Agentwerke.Agents.Tools;

/// <summary>
/// Delegates a task to another agent and returns its result (#192 Phase 4). The callee runs as an
/// inline nested model run in the same run; request and reply are recorded as agent_request
/// interactions linked by a correlation id, so the delegation shows up in the run conversation.
///
/// Depth guard: the callee runs read-only and is denied both <c>agent.request</c> (no further
/// delegation — depth-1 only) and <c>human.ask</c> (a sub-task must not pause the whole run).
/// </summary>
public sealed class AgentRequestTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentRegistry _registry;
    private readonly IAgentModelRunner _modelRunner;
    private readonly IAgentInteractionRepository _interactions;

    public AgentRequestTool(
        IAgentRegistry registry,
        IAgentModelRunner modelRunner,
        IAgentInteractionRepository interactions)
    {
        _registry = registry;
        _modelRunner = modelRunner;
        _interactions = interactions;
    }

    public string Name => "agent.request";

    public string Category => AgentToolCategories.SubAgent;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("to", "string", "The id of the agent to delegate the task to.", Required: true),
        new("task", "string", "The task for the delegated agent to complete.", Required: true),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
        {
            throw new InvalidOperationException("Tool input is missing required field 'to'.");
        }

        if (!input.TryGetValue("task", out var task) || string.IsNullOrWhiteSpace(task))
        {
            throw new InvalidOperationException("Tool input is missing required field 'task'.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var to = input["to"].Trim();
        var task = input["task"].Trim();

        if (string.Equals(to, context.AgentName, StringComparison.OrdinalIgnoreCase))
        {
            return Failure($"Agent '{context.AgentName}' cannot delegate to itself.");
        }

        var profile = _registry.Find(to);
        if (profile is null)
        {
            return Failure($"Unknown agent '{to}'. Delegation was not performed.");
        }

        var correlationId = Guid.NewGuid().ToString("n");
        await RecordAsync(context, correlationId, from: context.AgentName, addressee: to, text: task, cancellationToken);

        var result = await _modelRunner.RunAsync(
            BuildDelegatedRequest(context, profile, task),
            cancellationToken);

        var reply = result.Succeeded
            ? result.Output ?? "(no output)"
            : $"Delegation failed: {result.FailureReason}";

        await RecordAsync(context, correlationId, from: to, addressee: context.AgentName, text: reply, cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: result.Succeeded,
            Output: result.Succeeded ? $"{to} replied: {reply}" : null,
            FailureReason: result.Succeeded ? null : reply);
    }

    private ModelRunRequest BuildDelegatedRequest(
        AgentToolExecutionContext context,
        AgentProfile profile,
        string task)
    {
        var promptSnapshot = new AgentPromptSnapshot(
            FinalPrompt: BuildDelegatedPrompt(context.AgentName, profile, task),
            RenderedAt: DateTimeOffset.UtcNow.ToString("o"),
            Sections: [],
            Variables: new Dictionary<string, string>(),
            SourceFiles: []);

        var contract = new AgentRuntimeContract
        {
            Permissions = new AgentPermissionContract
            {
                Level = AgentPermissionLevels.ReadOnly,
                // Depth-1 guard: the callee can neither delegate again nor pause the run for a human.
                DeniedTools = [Name, "human.ask"],
            },
        };

        return new ModelRunRequest(
            RunId: context.RunId,
            StepId: context.StepId,
            AgentName: profile.AgentId,
            Action: "delegated_task",
            Environment: context.Environment,
            PurposeType: context.PurposeType,
            PolicyTag: context.PolicyTag,
            RequiresEvidence: [],
            Attempt: 1,
            PromptSnapshot: promptSnapshot,
            Contract: contract);
    }

    private static string BuildDelegatedPrompt(string fromAgent, AgentProfile profile, string task)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(profile.SystemPrompt))
        {
            builder.AppendLine(profile.SystemPrompt);
            builder.AppendLine();
        }

        builder.AppendLine(
            $"Agent '{fromAgent}' has delegated the following task to you. Complete it and report the result concisely.");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.Append(task);
        return builder.ToString();
    }

    private Task RecordAsync(
        AgentToolExecutionContext context,
        string correlationId,
        string from,
        string addressee,
        string text,
        CancellationToken cancellationToken)
    {
        var interaction = new AgentInteraction
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = context.RunId,
            StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
            FromAgent = from,
            Kind = AgentInteractionKinds.AgentRequest,
            AddresseeType = AgentInteractionAddresseeTypes.Agent,
            Addressee = addressee,
            Blocking = false,
            Prompt = text,
            CorrelationId = correlationId,
            Status = AgentInteractionStatuses.Posted,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        return PersistAsync(interaction, cancellationToken);
    }

    private async Task PersistAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        await _interactions.AddAsync(interaction, cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);
    }

    private static AgentToolExecutionResult Failure(string reason) =>
        new(Succeeded: false, Output: null, FailureReason: reason);
}
