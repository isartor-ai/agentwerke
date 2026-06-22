using System.Text;
using System.Text.Json;
using Autofac.AgentRunner;
using Autofac.Agents.Mcp;
using Autofac.Agents.Models;
using Microsoft.Extensions.Options;

const string EnvelopeEnvironmentVariable = "AUTOFAC_AGENT_RUN_ENVELOPE_B64";
const string ModelApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
const string OutputDirectory = "/output";
const string ResultFileName = "agent-run-result.json";

var result = await RunAsync();
Directory.CreateDirectory(OutputDirectory);
var resultPath = Path.Combine(OutputDirectory, ResultFileName);
await File.WriteAllTextAsync(
    resultPath,
    JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
    Encoding.UTF8);

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

    var apiKey = Environment.GetEnvironmentVariable(ModelApiKeyEnvironmentVariable);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return new SandboxedAgentRunResult(false, null, $"Missing {ModelApiKeyEnvironmentVariable}.", null);
    }

    var client = new AnthropicLanguageModelClient(Options.Create(new LanguageModelOptions
    {
        ApiKey = apiKey,
        Model = envelope.Model,
        MaxTokens = envelope.MaxTokens
    }));

    var executor = new SandboxedAgentRuntimeExecutor(
        client,
        new McpToolSessionFactory(new McpClientFactory()),
        RunnerToolFactory.CreateRegistry(envelope));
    return await executor.ExecuteAsync(envelope, CancellationToken.None);
}
