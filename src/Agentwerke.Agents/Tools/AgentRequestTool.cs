using System.Text;
using Agentwerke.Agents.Models;
using Agentwerke.Application.Agents;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tools;

public sealed class AgentRequestOptions
{
    public const string Section = "Agents:Delegation";
    public int MaxDelegationDepth { get; set; } = 3;
}

/// <summary>
/// Delegates to another registered agent. Blocking requests run the callee inline and return its
/// reply. Non-blocking requests only post the request; no reply is delivered to the caller.
/// </summary>
public sealed class AgentRequestTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentRegistry _registry;
    private readonly Lazy<IAgentModelRunner> _modelRunner;
    private readonly IAgentInteractionRepository _interactions;
    private readonly AgentRequestOptions _options;

    public AgentRequestTool(
        IAgentRegistry registry,
        Lazy<IAgentModelRunner> modelRunner,
        IAgentInteractionRepository interactions,
        IOptions<AgentRequestOptions> options)
    {
        _registry = registry;
        _modelRunner = modelRunner;
        _interactions = interactions;
        _options = options.Value;
    }

    public string Name => "agent.request";
    public string Category => AgentToolCategories.SubAgent;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("to", "string", "The id of the agent to delegate the task to.", true),
        new("task", "string", "The task for the delegated agent to complete.", true),
        new("blocking", "boolean", "Whether to wait for and return the reply. Defaults to true; false dispatches without delivering a reply."),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        ValidateRequired(input, "to");
        ValidateRequired(input, "task");
        if (input.TryGetValue("blocking", out var raw) && !bool.TryParse(raw, out _))
        {
            throw new InvalidOperationException("Tool input 'blocking' must be true or false.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var to = input["to"].Trim();
        var task = input["task"].Trim();
        var blocking = !input.TryGetValue("blocking", out var rawBlocking) || bool.Parse(rawBlocking);

        if (string.Equals(to, context.AgentName, StringComparison.OrdinalIgnoreCase))
        {
            return Failure($"Agent '{context.AgentName}' cannot delegate to itself.");
        }

        if (context.DelegationDepth >= Math.Max(0, _options.MaxDelegationDepth))
        {
            return Failure($"The maximum delegation depth of {_options.MaxDelegationDepth} has been reached; '{to}' was not invoked.");
        }

        if ((context.DelegationChain ?? []).Contains(to, StringComparer.OrdinalIgnoreCase))
        {
            var chain = string.Join(" -> ", (context.DelegationChain ?? []).Append(to));
            return Failure($"Delegation cycle detected ({chain}); '{to}' was not invoked.");
        }

        var profile = _registry.Find(to);
        if (profile is null)
        {
            return Failure($"Unknown agent '{to}'. Delegation was not performed.");
        }

        var correlationId = Guid.NewGuid().ToString("n");
        var request = CreateRequest(context, correlationId, to, task, blocking);
        await PersistAsync(request, cancellationToken);

        if (!blocking)
        {
            return new(true, $"Dispatched to {to}.", null);
        }

        ModelRunResult result;
        try
        {
            result = await _modelRunner.Value.RunAsync(
                BuildDelegatedRequest(context, profile, task), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = new ModelRunResult(
                false, null, ex.Message, [], null, null);
        }

        var reply = result.Succeeded
            ? result.Output ?? "(no output)"
            : $"Delegation failed: {result.FailureReason}";

        request.Status = AgentInteractionStatuses.Answered;
        request.Response = reply;
        request.RespondedBy = to;
        request.RespondedChannel = InteractionChannels.Agent;
        request.RespondedAt = DateTimeOffset.UtcNow.ToString("o");
        request.Version++;

        await _interactions.AddAsync(CreateReply(context, correlationId, to, reply), cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);

        return new AgentToolExecutionResult(
            result.Succeeded,
            result.Succeeded ? $"{to} replied: {reply}" : null,
            result.Succeeded ? null : reply);
    }

    private ModelRunRequest BuildDelegatedRequest(
        AgentToolExecutionContext context,
        AgentProfile profile,
        string task)
    {
        var promptSnapshot = new AgentPromptSnapshot(
            BuildDelegatedPrompt(context.AgentName, profile, task),
            DateTimeOffset.UtcNow.ToString("o"), [], new Dictionary<string, string>(), []);

        var contract = new AgentRuntimeContract
        {
            Permissions = new AgentPermissionContract
            {
                Level = AgentPermissionLevels.ReadOnly,
                DeniedTools = ["human.ask", "human.confirm"],
            },
        };

        return new ModelRunRequest(
            context.RunId,
            context.StepId,
            profile.AgentId,
            "delegated_task",
            context.Environment,
            context.PurposeType,
            context.PolicyTag,
            [],
            1,
            promptSnapshot,
            contract,
            NodeId: context.NodeId,
            DelegationDepth: context.DelegationDepth + 1,
            DelegationChain: (context.DelegationChain ?? []).Append(context.AgentName).ToArray());
    }

    private static AgentInteraction CreateRequest(
        AgentToolExecutionContext context,
        string correlationId,
        string to,
        string task,
        bool blocking) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        RunId = context.RunId,
        StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
        FromAgent = context.AgentName,
        Kind = AgentInteractionKinds.AgentRequest,
        AddresseeType = AgentInteractionAddresseeTypes.Agent,
        Addressee = to,
        Blocking = blocking,
        Prompt = task,
        CorrelationId = correlationId,
        Status = blocking ? AgentInteractionStatuses.Pending : AgentInteractionStatuses.Posted,
        DelegationDepth = context.DelegationDepth,
        CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
    };

    private static AgentInteraction CreateReply(
        AgentToolExecutionContext context,
        string correlationId,
        string from,
        string reply) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        RunId = context.RunId,
        StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
        FromAgent = from,
        Kind = AgentInteractionKinds.AgentRequest,
        AddresseeType = AgentInteractionAddresseeTypes.Agent,
        Addressee = context.AgentName,
        Blocking = false,
        Prompt = reply,
        Response = reply,
        CorrelationId = correlationId,
        Status = AgentInteractionStatuses.Answered,
        RespondedBy = from,
        RespondedChannel = InteractionChannels.Agent,
        RespondedAt = DateTimeOffset.UtcNow.ToString("o"),
        DelegationDepth = context.DelegationDepth + 1,
        CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
    };

    private async Task PersistAsync(AgentInteraction interaction, CancellationToken cancellationToken)
    {
        await _interactions.AddAsync(interaction, cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRequired(IReadOnlyDictionary<string, string> input, string field)
    {
        if (!input.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{field}'.");
        }
    }

    private static string BuildDelegatedPrompt(string fromAgent, AgentProfile profile, string task)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(profile.SystemPrompt))
        {
            builder.AppendLine(profile.SystemPrompt).AppendLine();
        }

        builder.AppendLine($"Agent '{fromAgent}' has delegated the following task to you. Complete it and report the result concisely.")
            .AppendLine().AppendLine("Task:").Append(task);
        return builder.ToString();
    }

    private static AgentToolExecutionResult Failure(string reason) => new(false, null, reason);
}
