using System.Diagnostics;
using System.Text.Json;
using Agentwerke.Agents.Mcp;
using Agentwerke.Agents.Tools;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Models;

public sealed class SandboxedAgentRuntimeExecutor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ILanguageModelClient _modelClient;
    private readonly IMcpToolSessionFactory _mcpToolSessionFactory;
    private readonly IToolRegistry _toolRegistry;

    public SandboxedAgentRuntimeExecutor(
        ILanguageModelClient modelClient,
        IMcpToolSessionFactory mcpToolSessionFactory,
        IToolRegistry toolRegistry)
    {
        _modelClient = modelClient;
        _mcpToolSessionFactory = mcpToolSessionFactory;
        _toolRegistry = toolRegistry;
    }

    public Task<SandboxedAgentRunResult> ExecuteAsync(
        SandboxedAgentRunEnvelope envelope,
        CancellationToken cancellationToken) =>
        ExecuteCoreAsync(envelope, cancellationToken);

    private async Task<SandboxedAgentRunResult> ExecuteCoreAsync(
        SandboxedAgentRunEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var contract = envelope.Contract ?? new AgentRuntimeContract();

        await using var mcpSession = await PrepareMcpToolsAsync(contract.McpServers, cancellationToken);
        if (mcpSession.Result is not null && !mcpSession.Result.Succeeded)
        {
            return new SandboxedAgentRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: mcpSession.Result.FailureReason,
                TokenUsage: null,
                Artifacts: null,
                ToolInvocations: []);
        }

        var descriptors = BuildToolDescriptors(
            contract,
            _toolRegistry.All(),
            mcpSession.Session?.Tools ?? [],
            envelope.SubAgents,
            envelope.RemainingSubAgentDepth);
        var toolDefinitions = BuildToolDefinitions(contract.Permissions, descriptors);
        var invocations = new List<AgentToolInvocationRecord>();
        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        var response = await _modelClient.RunAsync(
            new LanguageModelRequest(
                SystemPrompt: envelope.SystemPrompt,
                UserPrompt: envelope.UserPrompt,
                Tools: toolDefinitions,
                MaxTokens: envelope.MaxTokens),
            (call, ct) => ExecuteToolCallAsync(call, envelope, descriptors, invocations, artifacts, ct),
            cancellationToken);
        sw.Stop();

        var tokenUsage = new AgentModelTokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.ModelId ?? envelope.Model,
            sw.Elapsed.TotalMilliseconds);

        return new SandboxedAgentRunResult(
            Succeeded: response.Succeeded,
            Output: response.Output,
            FailureReason: response.FailureReason,
            TokenUsage: tokenUsage,
            Artifacts: artifacts.Count == 0 ? null : artifacts,
            ToolInvocations: invocations);
    }

    private async Task<LanguageModelToolResult> ExecuteToolCallAsync(
        LanguageModelToolCall call,
        SandboxedAgentRunEnvelope envelope,
        IReadOnlyDictionary<string, SandboxedToolDescriptor> descriptors,
        List<AgentToolInvocationRecord> invocations,
        Dictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        if (!descriptors.TryGetValue(call.Name, out var descriptor))
        {
            var missingInvocation = CreateInvocation(
                ToolName: call.Name,
                Category: AgentToolCategories.Read,
                Status: "missing",
                Input: call.Input,
                OutputSummary: null,
                ErrorMessage: $"Tool '{call.Name}' is not registered in the sandboxed runtime.",
                DurationMs: 0,
                ArtifactNames: []);
            invocations.Add(missingInvocation);
            return new LanguageModelToolResult(call.Id, missingInvocation.ErrorMessage!, IsError: true);
        }

        var enrichedInput = EnrichToolInput(call.Input, envelope);
        var started = Stopwatch.StartNew();
        try
        {
            descriptor.Validate(enrichedInput);

            return descriptor.Kind switch
            {
                SandboxedToolKind.Direct => await ExecuteDirectToolAsync(descriptor, call, enrichedInput, envelope, invocations, artifacts, started, cancellationToken),
                SandboxedToolKind.SubAgent => await ExecuteSubAgentToolAsync(descriptor, call, enrichedInput, envelope, invocations, artifacts, started, cancellationToken),
                _ => throw new InvalidOperationException($"Unsupported sandboxed tool kind '{descriptor.Kind}'.")
            };
        }
        catch (Exception ex)
        {
            started.Stop();
            var invalidInvocation = CreateInvocation(
                ToolName: descriptor.Name,
                Category: descriptor.Category,
                Status: "invalid_input",
                Input: enrichedInput,
                OutputSummary: null,
                ErrorMessage: ex.Message,
                DurationMs: (int)started.ElapsedMilliseconds,
                ArtifactNames: []);
            invocations.Add(invalidInvocation);
            return new LanguageModelToolResult(call.Id, ex.Message, IsError: true);
        }
    }

    private static IReadOnlyDictionary<string, string> EnrichToolInput(
        IReadOnlyDictionary<string, string> input,
        SandboxedAgentRunEnvelope envelope)
    {
        var enriched = new Dictionary<string, string>(input, StringComparer.OrdinalIgnoreCase)
        {
            ["run_id"] = envelope.RunId,
            ["step_id"] = envelope.StepId,
            ["attempt"] = envelope.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return enriched;
    }

    private async Task<LanguageModelToolResult> ExecuteDirectToolAsync(
        SandboxedToolDescriptor descriptor,
        LanguageModelToolCall call,
        IReadOnlyDictionary<string, string> input,
        SandboxedAgentRunEnvelope envelope,
        List<AgentToolInvocationRecord> invocations,
        Dictionary<string, string> artifacts,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var result = await descriptor.Tool!.ExecuteAsync(
            new AgentToolExecutionContext(
                RunId: envelope.RunId,
                StepId: envelope.StepId,
                AgentName: envelope.AgentName,
                Action: call.Name,
                Environment: envelope.Environment,
                PurposeType: envelope.PurposeType,
                PolicyTag: envelope.PolicyTag,
                Attempt: envelope.Attempt),
            input,
            cancellationToken);
        started.Stop();

        var artifactNames = MergeArtifacts(result.Artifacts, artifacts);
        var invocation = CreateInvocation(
            ToolName: descriptor.Name,
            Category: descriptor.Category,
            Status: result.Succeeded ? "completed" : "failed",
            Input: input,
            OutputSummary: result.Output,
            ErrorMessage: result.FailureReason,
            DurationMs: (int)started.ElapsedMilliseconds,
            ArtifactNames: artifactNames);
        invocations.Add(invocation);

        return new LanguageModelToolResult(
            call.Id,
            result.Output ?? result.FailureReason ?? $"Tool '{descriptor.Name}' completed.",
            IsError: !result.Succeeded);
    }

    private async Task<LanguageModelToolResult> ExecuteSubAgentToolAsync(
        SandboxedToolDescriptor descriptor,
        LanguageModelToolCall call,
        IReadOnlyDictionary<string, string> input,
        SandboxedAgentRunEnvelope envelope,
        List<AgentToolInvocationRecord> invocations,
        Dictionary<string, string> artifacts,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        if (envelope.RemainingSubAgentDepth <= 0)
        {
            started.Stop();
            var depthInvocation = CreateInvocation(
                ToolName: descriptor.Name,
                Category: descriptor.Category,
                Status: "failed",
                Input: input,
                OutputSummary: null,
                ErrorMessage: "Sub-agent delegation depth has been exhausted.",
                DurationMs: (int)started.ElapsedMilliseconds,
                ArtifactNames: []);
            invocations.Add(depthInvocation);
            return new LanguageModelToolResult(call.Id, depthInvocation.ErrorMessage!, IsError: true);
        }

        var delegatedPrompt = BuildSubAgentPrompt(input);
        var delegatedEnvelope = new SandboxedAgentRunEnvelope(
            envelope.RunId,
            envelope.StepId,
            descriptor.SubAgent!.AgentId,
            call.Name,
            envelope.Environment,
            envelope.PurposeType,
            envelope.PolicyTag,
            envelope.Attempt,
            BuildSubAgentSystemPrompt(descriptor.SubAgent),
            delegatedPrompt,
            descriptor.SubAgent.Model ?? envelope.Model,
            envelope.MaxTokens,
            envelope.Contract with
            {
                SubAgents = envelope.Contract.SubAgents is null
                    ? null
                    : envelope.Contract.SubAgents with
                    {
                        Enabled = envelope.RemainingSubAgentDepth > 1,
                        MaxDepth = Math.Max(0, envelope.RemainingSubAgentDepth - 1)
                    }
            },
            envelope.ResolvedTools,
            envelope.SubAgents,
            Math.Max(0, envelope.RemainingSubAgentDepth - 1));

        var subResult = await ExecuteCoreAsync(delegatedEnvelope, cancellationToken);
        started.Stop();

        var artifactNames = MergeArtifacts(subResult.Artifacts, artifacts);
        var invocation = CreateInvocation(
            ToolName: descriptor.Name,
            Category: descriptor.Category,
            Status: subResult.Succeeded ? "completed" : "failed",
            Input: input,
            OutputSummary: subResult.Output,
            ErrorMessage: subResult.FailureReason,
            DurationMs: (int)started.ElapsedMilliseconds,
            ArtifactNames: artifactNames);
        invocations.Add(invocation);

        if (subResult.ToolInvocations is { Count: > 0 })
        {
            invocations.AddRange(subResult.ToolInvocations);
        }

        return new LanguageModelToolResult(
            call.Id,
            subResult.Output ?? subResult.FailureReason ?? $"Sub-agent '{descriptor.SubAgent.AgentId}' completed.",
            IsError: !subResult.Succeeded);
    }

    private static IReadOnlyDictionary<string, SandboxedToolDescriptor> BuildToolDescriptors(
        AgentRuntimeContract contract,
        IReadOnlyList<IAgentTool> directTools,
        IReadOnlyList<IAgentTool> mcpTools,
        IReadOnlyList<SandboxedSubAgentProfile> subAgents,
        int remainingSubAgentDepth)
    {
        var descriptors = new Dictionary<string, SandboxedToolDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in directTools)
        {
            descriptors[tool.Name] = new SandboxedToolDescriptor(
                tool.Name,
                tool.Category,
                SandboxedToolKind.Direct,
                Tool: tool,
                Parameters: (tool as IToolSchemaProvider)?.GetParameters() ?? []);
        }

        foreach (var tool in mcpTools)
        {
            descriptors[tool.Name] = new SandboxedToolDescriptor(
                tool.Name,
                tool.Category,
                SandboxedToolKind.Direct,
                Tool: tool,
                Parameters: (tool as IToolSchemaProvider)?.GetParameters() ?? []);
        }

        if (contract.SubAgents?.Enabled == true && remainingSubAgentDepth > 0)
        {
            foreach (var subAgent in subAgents)
            {
                var toolName = BuildSubAgentToolName(subAgent.AgentId);
                descriptors[toolName] = new SandboxedToolDescriptor(
                    toolName,
                    AgentToolCategories.SubAgent,
                    SandboxedToolKind.SubAgent,
                    Tool: null,
                    Parameters:
                    [
                        new ToolSchemaParameter("prompt", "string", $"Delegated work for sub-agent '{subAgent.AgentId}'.", Required: true)
                    ],
                    SubAgent: subAgent);
            }
        }

        return descriptors;
    }

    private static IReadOnlyList<LanguageModelToolDefinition> BuildToolDefinitions(
        AgentPermissionContract permissions,
        IReadOnlyDictionary<string, SandboxedToolDescriptor> descriptors)
    {
        return descriptors.Values
            .Where(descriptor => !permissions.DeniedTools.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase))
            .Where(descriptor => permissions.AllowedTools.Count == 0 || permissions.AllowedTools.Contains(descriptor.Name, StringComparer.OrdinalIgnoreCase))
            .OrderBy(static descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static descriptor => new LanguageModelToolDefinition(
                descriptor.Name,
                $"[{descriptor.Category}] {descriptor.Name}",
                descriptor.Parameters
                    .Select(static parameter => new LanguageModelToolParameter(
                        parameter.Name,
                        parameter.Type,
                        parameter.Description,
                        parameter.Required))
                    .ToArray()))
            .ToArray();
    }

    private async Task<McpPreparationResult> PrepareMcpToolsAsync(
        IReadOnlyList<AgentMcpServerContract> servers,
        CancellationToken cancellationToken)
    {
        if (servers.Count == 0)
        {
            return new McpPreparationResult(null, null);
        }

        var result = await _mcpToolSessionFactory.CreateAsync(servers, cancellationToken);
        return new McpPreparationResult(result, result.Session);
    }

    private static string BuildSubAgentPrompt(IReadOnlyDictionary<string, string> input)
    {
        if (input.TryGetValue("prompt", out var prompt) && !string.IsNullOrWhiteSpace(prompt))
        {
            if (input.Count == 1)
            {
                return prompt;
            }

            var extra = input
                .Where(static pair => !string.Equals(pair.Key, "prompt", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return $"{prompt}\n\nAdditional inputs:\n{JsonSerializer.Serialize(extra, SerializerOptions)}";
        }

        return JsonSerializer.Serialize(input, SerializerOptions);
    }

    private static string BuildSubAgentSystemPrompt(SandboxedSubAgentProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SystemPrompt))
        {
            return profile.SystemPrompt!;
        }

        return $"You are sub-agent '{profile.AgentId}'. {profile.Description}".Trim();
    }

    private static IReadOnlyList<string> MergeArtifacts(
        IReadOnlyDictionary<string, string>? source,
        IDictionary<string, string> target)
    {
        if (source is not { Count: > 0 })
        {
            return [];
        }

        foreach (var (name, value) in source)
        {
            target[name] = value;
        }

        return source.Keys
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AgentToolInvocationRecord CreateInvocation(
        string ToolName,
        string Category,
        string Status,
        IReadOnlyDictionary<string, string> Input,
        string? OutputSummary,
        string? ErrorMessage,
        int DurationMs,
        IReadOnlyList<string> ArtifactNames) =>
        new()
        {
            ToolName = ToolName,
            Category = Category,
            Status = Status,
            InputSummary = JsonSerializer.Serialize(Input, SerializerOptions),
            OutputSummary = OutputSummary,
            ErrorMessage = ErrorMessage,
            DurationMs = DurationMs,
            ArtifactNames = ArtifactNames
        };

    private static string BuildSubAgentToolName(string agentId) => $"subagent.{agentId}";

    private sealed record SandboxedToolDescriptor(
        string Name,
        string Category,
        SandboxedToolKind Kind,
        IAgentTool? Tool,
        IReadOnlyList<ToolSchemaParameter> Parameters,
        SandboxedSubAgentProfile? SubAgent = null)
    {
        public void Validate(IReadOnlyDictionary<string, string> input)
        {
            switch (Kind)
            {
                case SandboxedToolKind.Direct:
                    Tool!.Validate(input);
                    return;
                case SandboxedToolKind.SubAgent:
                    if (!input.TryGetValue("prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
                    {
                        throw new InvalidOperationException($"Tool '{Name}' requires input 'prompt'.");
                    }

                    return;
            }
        }
    }

    private enum SandboxedToolKind
    {
        Direct,
        SubAgent
    }

    private sealed record McpPreparationResult(
        McpToolSessionResult? Result,
        IMcpToolSession? Session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (Session is not null)
            {
                await Session.DisposeAsync();
            }
        }
    }
}
