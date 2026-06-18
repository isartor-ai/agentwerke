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
        var systemPrompt = BuildSystemPrompt(request);

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
        var costUsd = CalculateCost(response.Usage.InputTokens, response.Usage.OutputTokens);
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
                ElapsedMs: elapsedMs);
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
            Input: call.Input);

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
                    .Select(p => new LanguageModelToolParameter(p.Name, p.Type, p.Description, p.Required))
                    .ToArray() ?? []))
            .ToArray();
    }

    private static string BuildToolDescription(IAgentTool tool) =>
        $"[{tool.Category}] {tool.Name}";

    private static string BuildSystemPrompt(ModelRunRequest request)
    {
        var parts = new List<string>
        {
            $"You are {request.AgentName}, an AI agent executing the action '{request.Action}'.",
            $"Purpose: {request.PurposeType}.",
            $"Environment: {request.Environment ?? "unspecified"}.",
            $"Attempt: {request.Attempt}."
        };

        if (request.RequiresEvidence.Count > 0)
        {
            parts.Add($"Required evidence: {string.Join(", ", request.RequiresEvidence)}.");
        }

        parts.Add("Use the available tools to complete the task. Be precise and efficient.");

        return string.Join(" ", parts);
    }

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

    private double CalculateCost(int inputTokens, int outputTokens) =>
        (double)((inputTokens * _options.InputCostPerMillionTokens +
                  outputTokens * _options.OutputCostPerMillionTokens) / 1_000_000m);
}
