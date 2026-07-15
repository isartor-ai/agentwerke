using Agentwerke.Application.Agents;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Agents.Tools;

public sealed class HumanAskTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentInteractionRepository _interactions;
    private readonly IRunContextRepository _runContext;
    private readonly IInteractionRouter _router;
    private readonly InteractionOptions _options;

    public HumanAskTool(
        IAgentInteractionRepository interactions,
        IRunContextRepository runContext,
        IInteractionRouter router,
        InteractionOptions options)
    {
        _interactions = interactions;
        _runContext = runContext;
        _router = router;
        _options = options;
    }

    public string Name => "human.ask";
    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("question", "string", "The question to ask the human. The run pauses until it is answered.", true),
        new("options", "string", "Optional comma-separated choices to offer the human."),
        new("channels", "string", "Optional comma-separated delivery channels (UI is always available)."),
        new("timeout_seconds", "integer", "Optional timeout in seconds; omitted uses the configured default."),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input) =>
        HumanInteractionToolSupport.ValidateRequired(input, "question");

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var question = input["question"].Trim();
        var key = HumanInteractionToolSupport.PendingKey(context);
        var existing = await HumanInteractionToolSupport.FindPendingAsync(
            context.RunId, key, _runContext, _interactions, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == AgentInteractionStatuses.Pending)
            {
                throw new AgentInteractionRequiredException(existing.Id, existing.Prompt);
            }

            await _runContext.DeleteAsync(context.RunId, key, cancellationToken);
            return HumanInteractionToolSupport.ResolveAsk(existing);
        }

        var choices = HumanInteractionToolSupport.ParseCsv(input, "options");
        var interaction = HumanInteractionToolSupport.CreateHumanInteraction(
            context,
            choices.Count == 0 ? AgentInteractionKinds.Question : AgentInteractionKinds.Choice,
            question,
            blocking: true,
            choices,
            HumanInteractionToolSupport.ParseCsv(input, "channels"),
            HumanInteractionToolSupport.ResolveTimeout(input, _options));

        await HumanInteractionToolSupport.PersistAndRouteAsync(
            interaction, key, _interactions, _runContext, _router, cancellationToken);
        throw new AgentInteractionRequiredException(interaction.Id, question);
    }
}

public sealed class HumanConfirmTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentInteractionRepository _interactions;
    private readonly IRunContextRepository _runContext;
    private readonly IInteractionRouter _router;
    private readonly InteractionOptions _options;

    public HumanConfirmTool(
        IAgentInteractionRepository interactions,
        IRunContextRepository runContext,
        IInteractionRouter router,
        InteractionOptions options)
    {
        _interactions = interactions;
        _runContext = runContext;
        _router = router;
        _options = options;
    }

    public string Name => "human.confirm";
    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("question", "string", "A confirmation that must be explicitly approved or rejected.", true),
        new("channels", "string", "Optional comma-separated delivery channels (UI is always available)."),
        new("timeout_seconds", "integer", "Optional timeout in seconds; omitted uses the configured default."),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input) =>
        HumanInteractionToolSupport.ValidateRequired(input, "question");

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var question = input["question"].Trim();
        var key = HumanInteractionToolSupport.PendingKey(context);
        var existing = await HumanInteractionToolSupport.FindPendingAsync(
            context.RunId, key, _runContext, _interactions, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == AgentInteractionStatuses.Pending)
            {
                throw new AgentInteractionRequiredException(existing.Id, existing.Prompt);
            }

            await _runContext.DeleteAsync(context.RunId, key, cancellationToken);
            return HumanInteractionToolSupport.ResolveConfirm(existing);
        }

        var interaction = HumanInteractionToolSupport.CreateHumanInteraction(
            context,
            AgentInteractionKinds.Confirm,
            question,
            blocking: true,
            ["approve", "reject"],
            HumanInteractionToolSupport.ParseCsv(input, "channels"),
            HumanInteractionToolSupport.ResolveTimeout(input, _options));

        await HumanInteractionToolSupport.PersistAndRouteAsync(
            interaction, key, _interactions, _runContext, _router, cancellationToken);
        throw new AgentInteractionRequiredException(interaction.Id, question);
    }
}

public sealed class HumanNotifyTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentInteractionRepository _interactions;
    private readonly IInteractionRouter _router;
    private readonly InteractionOptions _options;

    public HumanNotifyTool(
        IAgentInteractionRepository interactions,
        IInteractionRouter router,
        InteractionOptions options)
    {
        _interactions = interactions;
        _router = router;
        _options = options;
    }

    public string Name => "human.notify";
    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("message", "string", "A short heads-up for the human. Does not pause the run.", true),
        new("channels", "string", "Optional comma-separated delivery channels (UI is always available)."),
        new("timeout_seconds", "integer", "Optional delivery timeout in seconds; omitted uses the configured default."),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input) =>
        HumanInteractionToolSupport.ValidateRequired(input, "message");

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var interaction = HumanInteractionToolSupport.CreateHumanInteraction(
            context,
            AgentInteractionKinds.Notify,
            input["message"].Trim(),
            blocking: false,
            [],
            HumanInteractionToolSupport.ParseCsv(input, "channels"),
            HumanInteractionToolSupport.ResolveTimeout(input, _options));
        interaction.Status = AgentInteractionStatuses.Posted;

        await _interactions.AddAsync(interaction, cancellationToken);
        await _interactions.SaveChangesAsync(cancellationToken);
        await HumanInteractionToolSupport.RouteSafelyAsync(_router, interaction, cancellationToken);
        return new(true, "Notified the human.", null);
    }
}

internal static class HumanInteractionToolSupport
{
    private const int MaxHumanResponseLength = 8192;

    public static string PendingKey(AgentToolExecutionContext context) =>
        $"interaction.pending.{context.NodeId ?? context.StepId}";

    public static void ValidateRequired(IReadOnlyDictionary<string, string> input, string field)
    {
        if (!input.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{field}'.");
        }

        if (input.TryGetValue("timeout_seconds", out var timeout) &&
            (!int.TryParse(timeout, out var parsed) || parsed <= 0))
        {
            throw new InvalidOperationException("Tool input 'timeout_seconds' must be a positive integer.");
        }
    }

    public static List<string> ParseCsv(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];

    public static int? ResolveTimeout(IReadOnlyDictionary<string, string> input, InteractionOptions options) =>
        input.TryGetValue("timeout_seconds", out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)
            : options.DefaultTimeoutSeconds;

    public static AgentInteraction CreateHumanInteraction(
        AgentToolExecutionContext context,
        string kind,
        string prompt,
        bool blocking,
        List<string> choices,
        List<string> channels,
        int? timeoutSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentInteraction
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = context.RunId,
            StepId = string.IsNullOrWhiteSpace(context.StepId) ? null : context.StepId,
            FromAgent = context.AgentName,
            Kind = kind,
            AddresseeType = AgentInteractionAddresseeTypes.Human,
            Blocking = blocking,
            Prompt = prompt,
            Options = choices,
            RequestedChannels = channels,
            Status = blocking ? AgentInteractionStatuses.Pending : AgentInteractionStatuses.Posted,
            TimeoutAt = timeoutSeconds is null ? null : now.AddSeconds(timeoutSeconds.Value).ToString("o"),
            ExpiresAction = blocking ? InteractionExpiryActions.Fail : null,
            CreatedAt = now.ToString("o"),
        };
    }

    public static async Task<AgentInteraction?> FindPendingAsync(
        string runId,
        string key,
        IRunContextRepository runContext,
        IAgentInteractionRepository interactions,
        CancellationToken cancellationToken)
    {
        var entry = await runContext.GetAsync(runId, key, cancellationToken);
        if (entry is null) return null;
        var interaction = await interactions.GetByIdAsync(entry.Value, cancellationToken);
        if (interaction is null) await runContext.DeleteAsync(runId, key, cancellationToken);
        return interaction;
    }

    public static async Task PersistAndRouteAsync(
        AgentInteraction interaction,
        string key,
        IAgentInteractionRepository interactions,
        IRunContextRepository runContext,
        IInteractionRouter router,
        CancellationToken cancellationToken)
    {
        await interactions.AddAsync(interaction, cancellationToken);
        await interactions.SaveChangesAsync(cancellationToken);
        await runContext.SetAsync(interaction.RunId, key, interaction.Id, RunContextKinds.Interaction, cancellationToken);
        await RouteSafelyAsync(router, interaction, cancellationToken);
    }

    public static async Task RouteSafelyAsync(
        IInteractionRouter router,
        AgentInteraction interaction,
        CancellationToken cancellationToken)
    {
        try { await router.RouteAsync(interaction, cancellationToken); }
        catch { /* The persisted UI interaction remains usable if external delivery fails. */ }
    }

    public static AgentToolExecutionResult ResolveAsk(AgentInteraction interaction) => interaction.Status switch
    {
        AgentInteractionStatuses.Answered => new(true,
            $"Human answered via {interaction.RespondedChannel ?? "unknown"}:\n--- BEGIN UNTRUSTED HUMAN RESPONSE ---\n{Bound(interaction.Response)}\n--- END UNTRUSTED HUMAN RESPONSE ---", null),
        AgentInteractionStatuses.Rejected => throw new ConfirmationRejectedException("The human rejected the request."),
        AgentInteractionStatuses.Expired when interaction.ExpiresAction == InteractionExpiryActions.DefaultAnswer =>
            new(true, Bound(interaction.DefaultAnswer), null),
        AgentInteractionStatuses.Expired when interaction.ExpiresAction == InteractionExpiryActions.Continue =>
            new(true, "No answer was received; proceed without it.", null),
        AgentInteractionStatuses.Expired => new(false, null, "No answer was received before the request expired."),
        AgentInteractionStatuses.Cancelled => new(false, null, "The request was cancelled."),
        _ => throw new AgentInteractionRequiredException(interaction.Id, interaction.Prompt),
    };

    public static AgentToolExecutionResult ResolveConfirm(AgentInteraction interaction)
    {
        if (interaction.Status == AgentInteractionStatuses.Answered &&
            string.Equals(interaction.Response?.Trim(), "approve", StringComparison.OrdinalIgnoreCase))
        {
            return new(true,
                $"Confirmed by {interaction.RespondedBy ?? "unknown"} via {interaction.RespondedChannel ?? "unknown"}.", null);
        }

        if (interaction.Status is AgentInteractionStatuses.Rejected or AgentInteractionStatuses.Answered)
        {
            throw new ConfirmationRejectedException("The human rejected the confirmation request.");
        }

        return ResolveAsk(interaction);
    }

    private static string Bound(string? value)
    {
        value ??= string.Empty;
        return value.Length <= MaxHumanResponseLength ? value : value[..MaxHumanResponseLength];
    }
}
