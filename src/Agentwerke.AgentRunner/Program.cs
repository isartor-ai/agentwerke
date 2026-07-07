using System.Text;
using System.Text.Json;
using Agentwerke.AgentRunner;
using Agentwerke.Agents.Mcp;
using Agentwerke.Agents.Models;
using Microsoft.Extensions.Options;

const string EnvelopeEnvironmentVariable = "AGENTWERKE_AGENT_RUN_ENVELOPE_B64";
const string ModelApiKeyEnvironmentVariable = "AGENTWERKE_MODEL_API_KEY";
const string LegacyModelApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
const string ModelProviderEnvironmentVariable = "AGENTWERKE_MODEL_PROVIDER";
const string ModelApiBaseUrlEnvironmentVariable = "AGENTWERKE_MODEL_API_BASE_URL";
const string ModelTimeoutSecondsEnvironmentVariable = "AGENTWERKE_MODEL_TIMEOUT_SECONDS";
const string ModelMaxToolIterationsEnvironmentVariable = "AGENTWERKE_MODEL_MAX_TOOL_ITERATIONS";
const string OutputDirectory = "/output";
const string ResultFileName = "agent-run-result.json";

var result = await RunAsync();
Directory.CreateDirectory(OutputDirectory);
var resultPath = Path.Combine(OutputDirectory, ResultFileName);
// Encoding.UTF8 (unlike the parameterless WriteAllTextAsync overload) writes a
// byte-order mark, which OpenSandboxedAgentRunner's JsonSerializer.Deserialize
// of this same file rejects as invalid JSON — use the BOM-less encoding instead.
await File.WriteAllTextAsync(
    resultPath,
    JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

if (!string.IsNullOrWhiteSpace(result.Output))
{
    Console.WriteLine(result.Output);
}

return result.Succeeded ? 0 : 1;

static async Task<SandboxedAgentRunResult> RunAsync()
{
    var envelopePayload = Environment.GetEnvironmentVariable(EnvelopeEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(envelopePayload))
    {
        return new SandboxedAgentRunResult(false, null, $"Missing {EnvelopeEnvironmentVariable}.", null);
    }

    SandboxedAgentRunEnvelope? envelope;
    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(envelopePayload));
        envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch (Exception ex)
    {
        return new SandboxedAgentRunResult(false, null, $"Failed to parse sandboxed agent envelope: {ex.Message}", null);
    }

    if (envelope is null)
    {
        return new SandboxedAgentRunResult(false, null, "Sandboxed agent envelope was empty.", null);
    }

    var apiKey = Environment.GetEnvironmentVariable(ModelApiKeyEnvironmentVariable)
        ?? Environment.GetEnvironmentVariable(LegacyModelApiKeyEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return new SandboxedAgentRunResult(false, null, $"Missing {ModelApiKeyEnvironmentVariable}.", null);
    }

    var languageModelOptions = new LanguageModelOptions
    {
        ApiKey = apiKey,
        Provider = Environment.GetEnvironmentVariable(ModelProviderEnvironmentVariable) ?? "anthropic",
        ApiBaseUrl = Environment.GetEnvironmentVariable(ModelApiBaseUrlEnvironmentVariable) ?? LanguageModelOptions.DefaultApiBaseUrl,
        Model = envelope.Model,
        MaxTokens = envelope.MaxTokens
    };
    if (int.TryParse(Environment.GetEnvironmentVariable(ModelTimeoutSecondsEnvironmentVariable), out var timeoutSeconds) && timeoutSeconds > 0)
    {
        languageModelOptions.TimeoutSeconds = timeoutSeconds;
    }
    if (int.TryParse(Environment.GetEnvironmentVariable(ModelMaxToolIterationsEnvironmentVariable), out var maxToolIterations) && maxToolIterations > 0)
    {
        languageModelOptions.MaxToolIterations = maxToolIterations;
    }
    var modelOptions = Options.Create(languageModelOptions);

    // No DI container in the sandboxed runner, so build the resilient HTTP pipeline by hand:
    // retry handler (429/529/5xx) over the default handler, with the configured timeout.
    var httpClient = new HttpClient(new AnthropicRetryHandler(modelOptions) { InnerHandler = new HttpClientHandler() })
    {
        Timeout = TimeSpan.FromSeconds(Math.Max(1, modelOptions.Value.TimeoutSeconds))
    };
    ILanguageModelClient client = IsOpenAiCompatibleProvider(modelOptions.Value.Provider)
        ? new OpenAiCompatibleLanguageModelClient(httpClient, modelOptions)
        : new AnthropicLanguageModelClient(httpClient, modelOptions);

    var executor = new SandboxedAgentRuntimeExecutor(
        client,
        new McpToolSessionFactory(new McpClientFactory()),
        RunnerToolFactory.CreateRegistry(envelope));
    return await executor.ExecuteAsync(envelope, CancellationToken.None);
}

static bool IsOpenAiCompatibleProvider(string? provider) =>
    string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "litellm", StringComparison.OrdinalIgnoreCase);
