using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agentwerke.Workflows.Runtime;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Models;

/// <summary>
/// <see cref="ILanguageModelClient"/> for any OpenAI Chat Completions-compatible endpoint —
/// OpenAI, Azure OpenAI, or a LiteLLM proxy fronting many providers (#174). Selected via
/// <c>Anthropic:Provider = "openai" | "litellm"</c>; the endpoint comes from
/// <c>Anthropic:ApiBaseUrl</c> (e.g. <c>https://api.openai.com/v1</c> or
/// <c>http://litellm:4000/v1</c>) and the key from <c>Anthropic:ApiKey</c>.
///
/// Drives the same policy-gated tool-use loop as the Anthropic client, mapping to/from the
/// OpenAI <c>tools</c> / <c>tool_calls</c> shapes with raw JSON so no extra SDK is needed.
/// </summary>
public sealed class OpenAiCompatibleLanguageModelClient : ILanguageModelClient
{
    private readonly HttpClient _httpClient;
    private readonly LanguageModelOptions _options;

    public OpenAiCompatibleLanguageModelClient(HttpClient httpClient, IOptions<LanguageModelOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<LanguageModelResponse> RunAsync(
        LanguageModelRequest request,
        Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
        CancellationToken cancellationToken,
        AgentExecutionProgressReporter? progressReporter = null)
    {
        var url = $"{_options.ApiBaseUrl.TrimEnd('/')}/chat/completions";

        // OpenAI function names must match ^[a-zA-Z0-9_-]{1,64}$; dots become "__".
        var sanitizedToOriginal = request.Tools.ToDictionary(
            t => SanitizeName(t.Name), t => t.Name, StringComparer.OrdinalIgnoreCase);
        var tools = BuildTools(request.Tools);

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt },
        };

        var allToolCalls = new List<LanguageModelToolCall>();
        var totalInput = 0;
        var totalOutput = 0;
        string? modelId = null;

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var body = new JsonObject
            {
                ["model"] = request.ModelOverride ?? _options.Model,
                ["max_tokens"] = request.MaxTokens,
                // Stream so the model's chain of thought (reasoning_content / <think> / the
                // <agent_reasoning> convention) reaches the run timeline live rather than only
                // after the whole turn completes.
                ["stream"] = true,
                ["stream_options"] = new JsonObject { ["include_usage"] = true },
                ["messages"] = messages.DeepClone(),
            };
            if (tools.Count > 0)
            {
                body["tools"] = new JsonArray(tools.Select(t => t.DeepClone()).ToArray());
                body["tool_choice"] = "auto";
            }

            StreamedTurn turn;
            try
            {
                turn = await StreamTurnAsync(url, body, progressReporter, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Failure($"LLM call failed: {ex.Message}", allToolCalls, totalInput, totalOutput, modelId);
            }

            if (!turn.Succeeded)
            {
                return Failure(turn.FailureReason ?? "LLM call failed.", allToolCalls, totalInput, totalOutput, modelId);
            }

            totalInput += turn.InputTokens;
            totalOutput += turn.OutputTokens;
            modelId ??= turn.ModelId;

            if (turn.ToolCalls.Count == 0)
            {
                // Final turn: reasoning is the native reasoning_content, else <think>, else the
                // <agent_reasoning> convention. Output is the content with reasoning stripped.
                var reasoning = ResolveReasoning(turn.NativeReasoning, turn.Content);
                var visibleOutput = StripReasoning(turn.Content);
                await ReportVisibleReasoningAsync(progressReporter, reasoning, cancellationToken);
                return new LanguageModelResponse(
                    Succeeded: true,
                    Output: visibleOutput,
                    FailureReason: null,
                    AllToolCalls: allToolCalls,
                    Usage: new LanguageModelTokenUsage(totalInput, totalOutput),
                    ModelId: modelId,
                    ReasoningSummary: reasoning);
            }

            // Append the assistant turn (verbatim tool_calls) so the model sees its own call.
            var toolCallsJson = new JsonArray();
            foreach (var pending in turn.ToolCalls)
            {
                toolCallsJson.Add(new JsonObject
                {
                    ["id"] = pending.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = pending.SentName,
                        ["arguments"] = pending.Arguments,
                    },
                });
            }

            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = turn.Content,
                ["tool_calls"] = toolCallsJson,
            });

            foreach (var pending in turn.ToolCalls)
            {
                var originalName = sanitizedToOriginal.TryGetValue(pending.SentName, out var orig)
                    ? orig
                    : pending.SentName;
                var call = new LanguageModelToolCall(pending.Id, originalName, ExtractInput(pending.Arguments));
                allToolCalls.Add(call);

                var result = await toolExecutor(call, cancellationToken);

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = pending.Id,
                    ["content"] = result.Content,
                });
            }
        }

        return Failure(
            $"Agent exceeded maximum tool iterations ({_options.MaxToolIterations}).",
            allToolCalls, totalInput, totalOutput, modelId);
    }

    /// <summary>
    /// Sends one streaming chat-completions request and accumulates the SSE deltas into a single
    /// turn. Reasoning text (native <c>reasoning_content</c> or <c>&lt;think&gt;</c>/<c>&lt;agent_reasoning&gt;</c>
    /// in the content) is emitted to <paramref name="progressReporter"/> as it grows, so the UI —
    /// which collapses successive prefix-extending reasoning deltas into one live block — shows the
    /// model thinking in real time.
    /// </summary>
    private async Task<StreamedTurn> StreamTurnAsync(
        string url,
        JsonObject body,
        AgentExecutionProgressReporter? progressReporter,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        using var httpResponse = await _httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var payload = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            return StreamedTurn.Fail($"LLM call failed: {(int)httpResponse.StatusCode} {payload}");
        }

        var contentBuilder = new StringBuilder();
        var nativeReasoning = new StringBuilder();
        var toolCalls = new SortedDictionary<int, PendingToolCall>();
        var inputTokens = 0;
        var outputTokens = 0;
        string? modelId = null;
        var sawUsage = false;

        // Throttle live reasoning emissions: only push a cumulative update once ~24 new chars have
        // accumulated or a line break appears, so the event store isn't flooded token-by-token.
        var lastEmittedReasoningLength = 0;
        async Task MaybeEmitReasoningAsync(bool force)
        {
            var current = CurrentReasoning(nativeReasoning, contentBuilder);
            if (string.IsNullOrWhiteSpace(current) || current.Length <= lastEmittedReasoningLength)
            {
                return;
            }

            var grew = current.Length - lastEmittedReasoningLength;
            var atBoundary = current.EndsWith('\n') || current.EndsWith('.') || current.EndsWith('。');
            if (!force && grew < 24 && !atBoundary)
            {
                return;
            }

            lastEmittedReasoningLength = current.Length;
            await ReportVisibleReasoningAsync(progressReporter, current, cancellationToken);
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0 || string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
                {
                    break;
                }
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
                {
                    modelId ??= modelEl.GetString();
                }

                if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                {
                    if (usageEl.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv))
                    {
                        inputTokens = pv;
                        sawUsage = true;
                    }
                    if (usageEl.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv))
                    {
                        outputTokens = cv;
                        sawUsage = true;
                    }
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                {
                    nativeReasoning.Append(rc.GetString());
                }
                // Some providers name the field "reasoning" instead of "reasoning_content".
                else if (delta.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    nativeReasoning.Append(r.GetString());
                }

                if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    contentBuilder.Append(contentEl.GetString());
                }

                if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        var index = tc.TryGetProperty("index", out var idxEl) && idxEl.TryGetInt32(out var idx) ? idx : 0;
                        if (!toolCalls.TryGetValue(index, out var pending))
                        {
                            pending = new PendingToolCall();
                            toolCalls[index] = pending;
                        }

                        if (tc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        {
                            pending.Id = idEl.GetString() ?? pending.Id;
                        }
                        if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                        {
                            if (fn.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            {
                                pending.Name = nameEl.GetString() ?? pending.Name;
                            }
                            if (fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            {
                                pending.ArgumentsBuilder.Append(argsEl.GetString());
                            }
                        }
                    }
                }

                await MaybeEmitReasoningAsync(force: false);
            }
        }

        await MaybeEmitReasoningAsync(force: true);

        var resolvedToolCalls = toolCalls.Values
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => new ResolvedPendingToolCall(
                string.IsNullOrWhiteSpace(t.Id) ? Guid.NewGuid().ToString("n") : t.Id,
                t.Name,
                t.ArgumentsBuilder.ToString()))
            .ToArray();

        return new StreamedTurn(
            Succeeded: true,
            FailureReason: null,
            Content: contentBuilder.Length == 0 ? null : contentBuilder.ToString(),
            NativeReasoning: nativeReasoning.Length == 0 ? null : nativeReasoning.ToString(),
            ToolCalls: resolvedToolCalls,
            InputTokens: sawUsage ? inputTokens : 0,
            OutputTokens: sawUsage ? outputTokens : 0,
            ModelId: modelId);
    }

    /// <summary>Cumulative reasoning-so-far for live emission: native reasoning wins; otherwise the
    /// growing <c>&lt;think&gt;</c>/<c>&lt;agent_reasoning&gt;</c> candidate parsed from the content buffer.</summary>
    private static string? CurrentReasoning(StringBuilder nativeReasoning, StringBuilder content)
    {
        if (nativeReasoning.Length > 0)
        {
            var value = nativeReasoning.ToString().Trim();
            return value.Length == 0 ? null : value;
        }

        var text = content.ToString();
        var think = LanguageModelReasoningParser.ExtractThink(text).ReasoningSummary;
        if (!string.IsNullOrWhiteSpace(think))
        {
            return think.Trim();
        }

        return LanguageModelReasoningParser.ExtractLatestVisibleSummaryCandidate(text);
    }

    /// <summary>Final reasoning for the turn: native reasoning, else the completed
    /// <c>&lt;think&gt;</c> block, else the <c>&lt;agent_reasoning&gt;</c> convention.</summary>
    private static string? ResolveReasoning(string? nativeReasoning, string? content)
    {
        if (!string.IsNullOrWhiteSpace(nativeReasoning))
        {
            return nativeReasoning.Trim();
        }

        var think = LanguageModelReasoningParser.ExtractThink(content).ReasoningSummary;
        if (!string.IsNullOrWhiteSpace(think))
        {
            return think;
        }

        return LanguageModelReasoningParser.ExtractVisibleSummary(content, allowPlainTextFallback: false);
    }

    /// <summary>Visible output with reasoning markup removed (<c>&lt;think&gt;</c> then <c>&lt;agent_reasoning&gt;</c>).</summary>
    private static string? StripReasoning(string? content)
    {
        var withoutThink = LanguageModelReasoningParser.ExtractThink(content).Output;
        return LanguageModelReasoningParser.Extract(withoutThink).Output;
    }

    private sealed class PendingToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder ArgumentsBuilder { get; } = new();
    }

    private sealed record ResolvedPendingToolCall(string Id, string SentName, string Arguments);

    private sealed record StreamedTurn(
        bool Succeeded,
        string? FailureReason,
        string? Content,
        string? NativeReasoning,
        IReadOnlyList<ResolvedPendingToolCall> ToolCalls,
        int InputTokens,
        int OutputTokens,
        string? ModelId)
    {
        public static StreamedTurn Fail(string reason) =>
            new(false, reason, null, null, [], 0, 0, null);
    }

    private static LanguageModelResponse Failure(
        string reason, IReadOnlyList<LanguageModelToolCall> calls, int input, int output, string? modelId) =>
        new(
            Succeeded: false,
            Output: null,
            FailureReason: reason,
            AllToolCalls: calls,
            Usage: new LanguageModelTokenUsage(input, output),
            ModelId: modelId);

    private static string SanitizeName(string name) => name.Replace('.', '_').Replace(' ', '_');

    private static List<JsonObject> BuildTools(IReadOnlyList<LanguageModelToolDefinition> tools) =>
        tools
            .Select(t => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = SanitizeName(t.Name),
                    ["description"] = t.Description,
                    ["parameters"] = BuildSchemaJson(t.Parameters),
                },
            })
            .ToList();

    private static JsonNode BuildSchemaJson(IReadOnlyList<LanguageModelToolParameter> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in parameters)
        {
            properties[p.Name] = BuildParameterSchema(p);
            if (p.Required)
            {
                required.Add(p.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };
    }

    private static JsonObject BuildParameterSchema(LanguageModelToolParameter p)
    {
        var schema = new JsonObject
        {
            ["type"] = p.Type,
            ["description"] = p.Description,
        };

        if (p.EnumValues is { Count: > 0 })
        {
            var values = new JsonArray();
            foreach (var value in p.EnumValues)
            {
                values.Add(value);
            }

            schema["enum"] = values;
        }

        if (string.Equals(p.Type, "array", StringComparison.OrdinalIgnoreCase))
        {
            schema["items"] = new JsonObject
            {
                ["type"] = string.IsNullOrWhiteSpace(p.ItemType) ? "string" : p.ItemType,
            };
        }

        return schema;
    }

    private static IReadOnlyDictionary<string, string> ExtractInput(string? argumentsJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return result;
        }

        try
        {
            if (JsonNode.Parse(argumentsJson) is JsonObject obj)
            {
                foreach (var (key, value) in obj)
                {
                    result[key] = value?.ToString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Leave the input empty if the model emitted malformed arguments.
        }

        return result;
    }

    private static Task ReportVisibleReasoningAsync(
        AgentExecutionProgressReporter? progressReporter,
        string? summary,
        CancellationToken cancellationToken)
    {
        if (progressReporter is null || string.IsNullOrWhiteSpace(summary))
        {
            return Task.CompletedTask;
        }

        return progressReporter(
            new AgentExecutionProgressUpdate(
                AgentExecutionProgressKinds.Reasoning,
                summary),
            cancellationToken);
    }
}
