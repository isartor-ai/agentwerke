using System.Text.Json;
using Autofac.Agents.Tools;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Mcp;

internal sealed class McpAgentTool : IAgentTool, IToolSchemaProvider
{
    private readonly string[] _requiredArguments;
    private readonly IMcpClientConnection _connection;
    private readonly string _serverToolName;
    private readonly JsonElement? _inputSchema;

    public McpAgentTool(
        string qualifiedName,
        string serverToolName,
        IMcpClientConnection connection,
        JsonElement? inputSchema)
    {
        Name = qualifiedName;
        _serverToolName = serverToolName;
        _connection = connection;
        _inputSchema = inputSchema;
        _requiredArguments = ReadRequiredArguments(inputSchema);
    }

    public string Name { get; }

    public string Category => AgentToolCategories.Mcp;

    public IReadOnlyList<ToolSchemaParameter> GetParameters()
    {
        if (_inputSchema is not { } schema ||
            schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return _requiredArguments
                .Select(static argument => new ToolSchemaParameter(argument, "string", $"Input '{argument}'", Required: true))
                .ToArray();
        }

        var required = new HashSet<string>(_requiredArguments, StringComparer.OrdinalIgnoreCase);
        var parameters = new List<ToolSchemaParameter>();
        foreach (var property in properties.EnumerateObject())
        {
            var type = property.Value.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? "string"
                : "string";
            var description = property.Value.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString() ?? $"Input '{property.Name}'"
                : $"Input '{property.Name}'";
            parameters.Add(new ToolSchemaParameter(property.Name, type, description, required.Contains(property.Name)));
        }

        return parameters;
    }

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        foreach (var argument in _requiredArguments)
        {
            if (!input.ContainsKey(argument))
            {
                throw new InvalidOperationException(
                    $"Tool '{Name}' requires input '{argument}'. Supply it via runtime metadata using the 'tool.input.{argument}' key.");
            }
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var result = await _connection.CallToolAsync(_serverToolName, input, cancellationToken);
        return new AgentToolExecutionResult(
            result.Succeeded,
            result.Output,
            result.FailureReason,
            result.Artifacts);
    }

    private static string[] ReadRequiredArguments(JsonElement? inputSchema)
    {
        if (inputSchema is not { } schema ||
            schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in required.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                values.Add(item.GetString()!);
            }
        }

        return values.ToArray();
    }
}
