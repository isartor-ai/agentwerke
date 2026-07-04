using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        CancellationToken cancellationToken)
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
                ["model"] = _options.Model,
                ["max_tokens"] = request.MaxTokens,
                ["messages"] = messages.DeepClone(),
            };
            if (tools.Count > 0)
            {
                body["tools"] = new JsonArray(tools.Select(t => t.DeepClone()).ToArray());
                body["tool_choice"] = "auto";
            }

            JsonDocument doc;
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                }

                using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
                var payload = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    return Failure($"LLM call failed: {(int)httpResponse.StatusCode} {payload}", allToolCalls, totalInput, totalOutput, modelId);
                }

                doc = JsonDocument.Parse(payload);
            }
            catch (Exception ex)
            {
                return Failure($"LLM call failed: {ex.Message}", allToolCalls, totalInput, totalOutput, modelId);
            }

            using (doc)
            {
                var root = doc.RootElement;
                (var input, var output) = ReadUsage(root);
                totalInput += input;
                totalOutput += output;
                modelId ??= root.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    return Failure("LLM response contained no choices.", allToolCalls, totalInput, totalOutput, modelId);
                }

                var message = choices[0].GetProperty("message");
                var toolCalls = message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array
                    ? tc
                    : default;

                if (toolCalls.ValueKind != JsonValueKind.Array || toolCalls.GetArrayLength() == 0)
                {
                    var text = message.TryGetProperty("content", out var content) ? content.GetString() : null;
                    return new LanguageModelResponse(
                        Succeeded: true,
                        Output: text,
                        FailureReason: null,
                        AllToolCalls: allToolCalls,
                        Usage: new LanguageModelTokenUsage(totalInput, totalOutput),
                        ModelId: modelId);
                }

                // Append the assistant turn (verbatim tool_calls) so the model sees its own call.
                messages.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = message.TryGetProperty("content", out var ac) && ac.ValueKind == JsonValueKind.String
                        ? ac.GetString()
                        : null,
                    ["tool_calls"] = JsonNode.Parse(toolCalls.GetRawText()),
                });

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var id = toolCall.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var function = toolCall.GetProperty("function");
                    var sentName = function.GetProperty("name").GetString() ?? string.Empty;
                    var originalName = sanitizedToOriginal.TryGetValue(sentName, out var orig) ? orig : sentName;
                    var arguments = function.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() : null;

                    var call = new LanguageModelToolCall(id, originalName, ExtractInput(arguments));
                    allToolCalls.Add(call);

                    var result = await toolExecutor(call, cancellationToken);

                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = id,
                        ["content"] = result.Content,
                    });
                }
            }
        }

        return Failure(
            $"Agent exceeded maximum tool iterations ({_options.MaxToolIterations}).",
            allToolCalls, totalInput, totalOutput, modelId);
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

    private static (int Input, int Output) ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return (0, 0);
        }

        var input = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
        var output = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv) ? cv : 0;
        return (input, output);
    }

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
}
