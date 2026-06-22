using System.Text;
using System.Text.Json;
using Autofac.AgentRunner;
using Autofac.AgentSecOps;
using Autofac.Agents.Tools;
using Autofac.Agents.Models;
using Autofac.Sandboxes;
using Autofac.Domain.AgentRuntime;
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

    var toolRegistry = RunnerToolFactory.CreateRegistry(envelope);
    var toolGateway = new ToolGateway(toolRegistry, new PolicyEvaluationService(), new SandboxProfileSelector());
    var modelRunner = new AgentModelRunner(
        client,
        toolGateway,
        toolRegistry,
        new RunnerWorkflowMetrics(),
        Options.Create(new LanguageModelOptions
        {
            ApiKey = apiKey,
            Model = envelope.Model,
            MaxTokens = envelope.MaxTokens
        }));

    var executor = new SandboxedAgentRuntimeExecutor(modelRunner);
    return await executor.ExecuteAsync(envelope, CancellationToken.None);
}

file sealed class RunnerWorkflowMetrics : Autofac.Application.Observability.IWorkflowMetrics
{
    public void RunStarted(string workflowId, string workflowName) { }
    public void RunCompleted(string workflowId, string workflowName, double durationMs) { }
    public void RunFailed(string workflowId, string workflowName, string reason) { }
    public void StepCompleted(string stepType, string agentName, double durationMs, bool succeeded) { }
    public void ApprovalCreated(string riskLevel) { }
    public void ApprovalDecided(string decision, string riskLevel) { }
    public void WebhookReceived(string source, bool triggered) { }
    public void ConnectorInvoked(string connectorId, string operation, double durationMs, bool succeeded) { }
    public void ModelInvoked(string agentName, string modelId, int inputTokens, int outputTokens, double latencyMs, double costUsd, bool succeeded) { }
    public void ToolPolicyDenied(string agentName, string policyTag, string kind) { }
}
