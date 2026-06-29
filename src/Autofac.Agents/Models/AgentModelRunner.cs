using System.Diagnostics;
using Autofac.Agents.Tools;
using Autofac.Application.Observability;
using Autofac.Domain.AgentRuntime;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Models;

public sealed class AgentModelRunner : IAgentModelRunner
{
    private readonly ILanguageModelClient _modelClient;
    private readonly IToolGateway _toolGateway;
    private readonly IToolRegistry _toolRegistry;
    private readonly IWorkflowMetrics _metrics;
    private readonly LanguageModelOptions _options;

    public AgentModelRunner(
        ILanguageModelClient modelClient,
        IToolGateway toolGateway,
        IToolRegistry toolRegistry,
        IWorkflowMetrics metrics,
        IOptions<LanguageModelOptions> options)
    {
        _modelClient = modelClient;
        _toolGateway = toolGateway;
        _toolRegistry = toolRegistry;
        _metrics = metrics;
        _options = options.Value;
    }

    public async Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        CancellationToken cancellationToken)
    {
        var toolDefinitions = BuildToolDefinitions(request.Contract);
        var systemPrompt = ModelRunPromptFactory.BuildSystemPrompt(request);

        var invocations = new List<AgentToolInvocationRecord>();
        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var llmRequest = new LanguageModelRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: request.PromptSnapshot.FinalPrompt,
            Tools: toolDefinitions,
            MaxTokens: _options.MaxTokens);

        var sw = Stopwatch.StartNew();
        var response = await _modelClient.RunAsync(
            llmRequest,
            toolExecutor: (call, ct) => ExecuteToolCallAsync(call, request, invocations, artifacts, ct),
            cancellationToken);
        sw.Stop();

        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        var tokenUsage = ToTokenUsage(response, elapsedMs);
        var costUsd = CalculateCost(response.Usage);
        _metrics.ModelInvoked(
            request.AgentName,
            response.ModelId ?? _options.Model,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            elapsedMs,
            costUsd,
            response.Succeeded);

        if (!response.Succeeded)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: response.FailureReason,
                ToolInvocations: invocations,
                Artifacts: artifacts.Count > 0 ? artifacts : null,
                TokenUsage: tokenUsage,
                ElapsedMs: elapsedMs,
                StepStatus: response.StepStatus);
        }

        return new ModelRunResult(
            Succeeded: true,
            Output: response.Output,
            FailureReason: null,
            ToolInvocations: invocations,
            Artifacts: artifacts.Count > 0 ? artifacts : null,
            TokenUsage: tokenUsage,
            ElapsedMs: elapsedMs);
    }

    private async Task<LanguageModelToolResult> ExecuteToolCallAsync(
        LanguageModelToolCall call,
        ModelRunRequest request,
        List<AgentToolInvocationRecord> invocations,
        Dictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        var contract = request.Contract;
        var permissions = contract.Permissions;
        var enrichedInput = EnrichToolInput(call.Input, request);

        var gatewayRequest = new ToolGatewayRequest(
            ToolName: call.Name,
            Action: call.Name,
            RunId: request.RunId,
            StepId: request.StepId,
            AgentName: request.AgentName,
            Environment: request.Environment,
            PurposeType: request.PurposeType,
            PolicyTag: request.PolicyTag,
            RequiresEvidence: request.RequiresEvidence,
            Attempt: request.Attempt,
            PermissionLevel: permissions.Level,
            AllowedTools: permissions.AllowedTools,
            DeniedTools: permissions.DeniedTools,
            Input: enrichedInput);

        var result = await _toolGateway.ExecuteAsync(gatewayRequest, cancellationToken);

        invocations.Add(result.Invocation);

        if (result.Artifacts is { Count: > 0 })
        {
            foreach (var (name, uri) in result.Artifacts)
                artifacts[name] = uri;
        }

        // Emit a policy-denial metric whenever the gateway rejected or escalated the tool call.
        var kind = result.Invocation.PolicyDecisionKind;
        if (!string.IsNullOrEmpty(kind) &&
            !string.Equals(kind, "allow", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.ToolPolicyDenied(request.AgentName, request.PolicyTag, kind);
        }

        return new LanguageModelToolResult(
            ToolCallId: call.Id,
            Content: result.Output ?? result.FailureReason ?? $"Tool '{call.Name}' completed.",
            IsError: !result.Succeeded);
    }

    private static IReadOnlyDictionary<string, string> EnrichToolInput(
        IReadOnlyDictionary<string, string> input,
        ModelRunRequest request)
    {
        var enriched = new Dictionary<string, string>(input, StringComparer.OrdinalIgnoreCase)
        {
            ["run_id"] = request.RunId,
            ["step_id"] = request.StepId,
            ["attempt"] = request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return enriched;
    }

    private IReadOnlyList<LanguageModelToolDefinition> BuildToolDefinitions(AgentRuntimeContract contract)
    {
        var allowed = contract.Permissions.AllowedTools;
        var denied = contract.Permissions.DeniedTools;

        return _toolRegistry.All()
            .Where(t => !denied.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .Where(t => allowed.Count == 0 || allowed.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .Select(t => new LanguageModelToolDefinition(
                Name: t.Name,
                Description: BuildToolDescription(t),
                Parameters: (t as IToolSchemaProvider)?.GetParameters()
                    .Select(p => new LanguageModelToolParameter(
                        p.Name, p.Type, p.Description, p.Required, p.EnumValues, p.ItemType))
                    .ToArray() ?? []))
            .ToArray();
    }

    private static string BuildToolDescription(IAgentTool tool) =>
        $"[{tool.Category}] {tool.Name}";

    private static AgentModelTokenUsage? ToTokenUsage(LanguageModelResponse response, double elapsedMs)
    {
        if (response.Usage.InputTokens == 0 && response.Usage.OutputTokens == 0)
            return null;

        return new AgentModelTokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.ModelId,
            elapsedMs);
    }

    // Prices uncached input, output, and the two prompt-cache token classes separately so the
    // agent.model.cost_usd metric reflects the savings (and write premium) from prompt caching.
    private double CalculateCost(LanguageModelTokenUsage usage) =>
        (double)((usage.InputTokens * _options.InputCostPerMillionTokens +
                  usage.OutputTokens * _options.OutputCostPerMillionTokens +
                  usage.CacheReadInputTokens * _options.CacheReadCostPerMillionTokens +
                  usage.CacheCreationInputTokens * _options.CacheWriteCostPerMillionTokens) / 1_000_000m);
}
