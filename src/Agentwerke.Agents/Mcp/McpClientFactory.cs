using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Mcp;

public sealed class McpClientFactory : IMcpClientFactory
{
    public Task<IMcpClientConnection> CreateAsync(AgentMcpServerContract server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        return Task.FromResult<IMcpClientConnection>(server.Transport.Trim().ToLowerInvariant() switch
        {
            "http" or "https" => new HttpMcpClientConnection(server),
            "stdio" => new StdioMcpClientConnection(server),
            _ => throw new InvalidOperationException(
                $"MCP server '{server.Name}' uses unsupported transport '{server.Transport}'.")
        });
    }

    private abstract class McpClientConnectionBase : IMcpClientConnection
    {
        private const string DefaultProtocolVersion = "2025-03-26";
        private int _nextId = 1;

        protected static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        protected McpClientConnectionBase(string serverName)
        {
            ServerName = serverName;
        }

        public string ServerName { get; }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await SendRequestAsync(
                "initialize",
                new Dictionary<string, object?>
                {
                    ["protocolVersion"] = DefaultProtocolVersion,
                    ["capabilities"] = new Dictionary<string, object?>(),
                    ["clientInfo"] = new Dictionary<string, object?>
                    {
                        ["name"] = "Agentwerke",
                        ["version"] = "1.0"
                    }
                },
                cancellationToken);

            await SendNotificationAsync("notifications/initialized", null, cancellationToken);
        }

        public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
        {
            using var document = await SendRequestAsync("tools/list", null, cancellationToken);
            if (!document.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("tools", out var toolsElement) ||
                toolsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var tools = new List<McpToolDefinition>();
            foreach (var toolElement in toolsElement.EnumerateArray())
            {
                var name = toolElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                tools.Add(new McpToolDefinition(
                    name!,
                    toolElement.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null,
                    toolElement.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() : null,
                    toolElement.TryGetProperty("inputSchema", out var inputSchema)
                        ? JsonSerializer.Deserialize<JsonElement>(inputSchema.GetRawText(), SerializerOptions)
                        : null));
            }

            return tools;
        }

        public async Task<McpToolCallResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, string> arguments,
            CancellationToken cancellationToken)
        {
            using var document = await SendRequestAsync(
                "tools/call",
                new Dictionary<string, object?>
                {
                    ["name"] = toolName,
                    ["arguments"] = arguments
                },
                cancellationToken);

            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return new McpToolCallResult(false, null, $"MCP server '{ServerName}' returned no result for tool '{toolName}'.");
            }

            var output = ExtractOutput(result);
            return new McpToolCallResult(
                Succeeded: !result.TryGetProperty("isError", out var isError) || !isError.GetBoolean(),
                Output: output,
                FailureReason: result.TryGetProperty("isError", out var errorFlag) && errorFlag.GetBoolean()
                    ? output ?? $"MCP tool '{toolName}' failed."
                    : null);
        }

        public abstract ValueTask DisposeAsync();

        protected async Task<JsonDocument> SendRequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Interlocked.Increment(ref _nextId),
                ["method"] = method
            };

            if (parameters is not null)
            {
                payload["params"] = parameters;
            }

            var response = await SendAsync(payload, expectResponse: true, cancellationToken);
            if (response.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : error.GetRawText();
                throw new InvalidOperationException(message ?? $"MCP request '{method}' failed.");
            }

            return response;
        }

        protected Task SendNotificationAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken)
        {
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };

            if (parameters is not null)
            {
                payload["params"] = parameters;
            }

            return SendAsync(payload, expectResponse: false, cancellationToken);
        }

        protected abstract Task<JsonDocument> SendAsync(
            IReadOnlyDictionary<string, object?> payload,
            bool expectResponse,
            CancellationToken cancellationToken);

        private static string? ExtractOutput(JsonElement result)
        {
            if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return result.GetRawText();
            }

            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeElement))
                {
                    parts.Add(item.GetRawText());
                    continue;
                }

                var type = typeElement.GetString();
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) &&
                    item.TryGetProperty("text", out var textElement))
                {
                    parts.Add(textElement.GetString() ?? string.Empty);
                    continue;
                }

                parts.Add(item.GetRawText());
            }

            return string.Join(Environment.NewLine, parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    private sealed class HttpMcpClientConnection : McpClientConnectionBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _url;
        private string? _sessionId;

        public HttpMcpClientConnection(AgentMcpServerContract server)
            : base(server.Name)
        {
            if (string.IsNullOrWhiteSpace(server.Url))
            {
                throw new InvalidOperationException(
                    $"MCP server '{server.Name}' requires a Url when transport is '{server.Transport}'.");
            }

            _url = server.Url;
            _httpClient = new HttpClient();
            foreach (var header in server.Headers)
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        public override ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            return ValueTask.CompletedTask;
        }

        protected override async Task<JsonDocument> SendAsync(
            IReadOnlyDictionary<string, object?> payload,
            bool expectResponse,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, SerializerOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
            {
                _sessionId = values.FirstOrDefault();
            }

            if (!expectResponse)
            {
                return JsonDocument.Parse("{}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private sealed class StdioMcpClientConnection : McpClientConnectionBase
    {
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private readonly StringBuilder _stderr = new();

        public StdioMcpClientConnection(AgentMcpServerContract server)
            : base(server.Name)
        {
            if (string.IsNullOrWhiteSpace(server.Command))
            {
                throw new InvalidOperationException(
                    $"MCP server '{server.Name}' requires a Command when transport is '{server.Transport}'.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = server.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in server.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            _process = new Process { StartInfo = startInfo };
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    _stderr.AppendLine(args.Data);
                }
            };

            if (!_process.Start())
            {
                throw new InvalidOperationException($"MCP server '{server.Name}' could not be started.");
            }

            _process.BeginErrorReadLine();
            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
        }

        protected override async Task<JsonDocument> SendAsync(
            IReadOnlyDictionary<string, object?> payload,
            bool expectResponse,
            CancellationToken cancellationToken)
        {
            var line = JsonSerializer.Serialize(payload, SerializerOptions);
            await _stdin.WriteLineAsync(line);
            await _stdin.FlushAsync();

            if (!expectResponse)
            {
                return JsonDocument.Parse("{}");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var responseLine = await _stdout.ReadLineAsync(cancellationToken);
                if (responseLine is null)
                {
                    var stderr = _stderr.ToString().Trim();
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(stderr)
                            ? $"MCP server '{ServerName}' closed the stdio transport unexpectedly."
                            : stderr);
                }

                var document = JsonDocument.Parse(responseLine);
                if (document.RootElement.TryGetProperty("id", out _))
                {
                    return document;
                }

                document.Dispose();
            }
        }
    }
}
