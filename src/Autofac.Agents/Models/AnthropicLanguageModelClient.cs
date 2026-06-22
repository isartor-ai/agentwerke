using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Models;

public sealed class AnthropicLanguageModelClient : ILanguageModelClient
{
    private readonly LanguageModelOptions _options;

    public AnthropicLanguageModelClient(IOptions<LanguageModelOptions> options)
    {
        _options = options.Value;
    }

    public async Task<LanguageModelResponse> RunAsync(
        LanguageModelRequest request,
        Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.ApiKey
            ?? throw new InvalidOperationException(
                "Anthropic API key is not configured. Set 'Anthropic:ApiKey' in configuration.");

        using var httpClient = BuildHttpClient();
        using var client = new AnthropicClient(new APIAuthentication(apiKey), httpClient, null);

        var tools = BuildTools(request.Tools);
        var messages = new List<Message>
        {
            new(RoleType.User, request.UserPrompt, null!)
        };

        // Anthropic requires tool names to match ^[a-zA-Z0-9_-]{1,64}$.
        // Dots in names like "github.create_branch" are replaced with "__".
        // A reverse map lets us restore the original name when tool calls come back.
        var sanitizedToOriginal = request.Tools.ToDictionary(
            t => SanitizeName(t.Name),
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

        var allToolCalls = new List<LanguageModelToolCall>();
        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        string? modelId = null;

        for (int iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            var parameters = new MessageParameters
            {
                Model = _options.Model,
                MaxTokens = request.MaxTokens,
                System = [new SystemMessage(request.SystemPrompt)],
                Messages = messages,
                Tools = tools.Count > 0 ? tools : null!
            };

            MessageResponse response;
            try
            {
                response = await client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
            }
            catch (Exception ex)
            {
                return new LanguageModelResponse(
                    Succeeded: false,
                    Output: null,
                    FailureReason: $"LLM call failed: {ex.Message}",
                    AllToolCalls: allToolCalls,
                    Usage: new LanguageModelTokenUsage(totalInputTokens, totalOutputTokens),
                    ModelId: modelId);
            }

            totalInputTokens += response.Usage.InputTokens;
            totalOutputTokens += response.Usage.OutputTokens;
            modelId ??= response.Model;

            var toolUses = response.Content.OfType<ToolUseContent>().ToArray();

            if (toolUses.Length == 0 || response.StopReason == "end_turn")
            {
                var text = response.Content.OfType<TextContent>().FirstOrDefault()?.Text;
                return new LanguageModelResponse(
                    Succeeded: true,
                    Output: text,
                    FailureReason: null,
                    AllToolCalls: allToolCalls,
                    Usage: new LanguageModelTokenUsage(totalInputTokens, totalOutputTokens),
                    ModelId: modelId);
            }

            // Append assistant turn to conversation
            messages.Add(response.Message);

            // Execute each tool call and collect results
            var toolResults = new List<ContentBase>();
            foreach (var toolUse in toolUses)
            {
                // Restore the original dotted tool name before dispatching
                var originalName = sanitizedToOriginal.TryGetValue(toolUse.Name, out var orig)
                    ? orig
                    : toolUse.Name;

                var call = new LanguageModelToolCall(
                    Id: toolUse.Id,
                    Name: originalName,
                    Input: ExtractInput(toolUse.Input));

                allToolCalls.Add(call);

                var toolResult = await toolExecutor(call, cancellationToken);

                toolResults.Add(new ToolResultContent
                {
                    ToolUseId = toolResult.ToolCallId,
                    Content = [new TextContent { Text = toolResult.Content }],
                    IsError = toolResult.IsError
                });
            }

            // Append tool results as next user turn
            messages.Add(new Message
            {
                Role = RoleType.User,
                Content = toolResults
            });
        }

        return new LanguageModelResponse(
            Succeeded: false,
            Output: null,
            FailureReason: $"Agent exceeded maximum tool iterations ({_options.MaxToolIterations}).",
            AllToolCalls: allToolCalls,
            Usage: new LanguageModelTokenUsage(totalInputTokens, totalOutputTokens),
            ModelId: modelId);
    }

    private static string SanitizeName(string name) =>
        name.Replace('.', '_').Replace(' ', '_');

    private HttpClient BuildHttpClient()
    {
        var client = new HttpClient();
        if (Uri.TryCreate(_options.ApiBaseUrl, UriKind.Absolute, out var apiBaseUri))
        {
            client.BaseAddress = apiBaseUri;
        }

        return client;
    }

    private static List<Anthropic.SDK.Common.Tool> BuildTools(
        IReadOnlyList<LanguageModelToolDefinition> tools)
    {
        return tools
            .Select(t => new Anthropic.SDK.Common.Tool(
                new Function(SanitizeName(t.Name), t.Description, BuildSchemaJson(t.Parameters))))
            .ToList();
    }

    private static JsonNode BuildSchemaJson(IReadOnlyList<LanguageModelToolParameter> parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var p in parameters)
        {
            properties[p.Name] = new JsonObject
            {
                ["type"] = p.Type,
                ["description"] = p.Description
            };

            if (p.Required) required.Add(p.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static IReadOnlyDictionary<string, string> ExtractInput(JsonNode? input)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (input is not JsonObject obj) return result;
        foreach (var (key, value) in obj)
            result[key] = value?.ToString() ?? string.Empty;
        return result;
    }
}

public sealed class NullLanguageModelClient : ILanguageModelClient
{
    public Task<LanguageModelResponse> RunAsync(
        LanguageModelRequest request,
        Func<LanguageModelToolCall, CancellationToken, Task<LanguageModelToolResult>> toolExecutor,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new LanguageModelResponse(
            Succeeded: false,
            Output: null,
            FailureReason: "No language model client is configured. Set 'Anthropic:ApiKey' in configuration.",
            AllToolCalls: [],
            Usage: new LanguageModelTokenUsage(0, 0),
            ModelId: null));
    }
}
