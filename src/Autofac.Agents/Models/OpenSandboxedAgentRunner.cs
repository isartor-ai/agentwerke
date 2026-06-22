using System.Text;
using System.Text.Json;
using Autofac.Application.Secrets;
using Autofac.Domain.AgentRuntime;
using Autofac.Sandboxes;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Models;

public sealed class OpenSandboxedAgentRunner : ISandboxedAgentRunner
{
    private const string ResultArtifactName = "agent-run-result.json";
    private const string EnvelopeEnvironmentVariable = "AUTOFAC_AGENT_RUN_ENVELOPE_B64";
    private const string DefaultModelApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";

    private readonly ISandboxExecutor _sandboxExecutor;
    private readonly ISecretStore _secretStore;
    private readonly IAgentRegistry _agentRegistry;
    private readonly LanguageModelOptions _languageModelOptions;
    private readonly SandboxOptions _sandboxOptions;

    public OpenSandboxedAgentRunner(
        ISandboxExecutor sandboxExecutor,
        ISecretStore secretStore,
        IAgentRegistry agentRegistry,
        IOptions<LanguageModelOptions> languageModelOptions,
        IOptions<SandboxOptions> sandboxOptions)
    {
        _sandboxExecutor = sandboxExecutor;
        _secretStore = secretStore;
        _agentRegistry = agentRegistry;
        _languageModelOptions = languageModelOptions.Value;
        _sandboxOptions = sandboxOptions.Value;
    }

    public async Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        AgentProfile? profile,
        string sandboxProfileName,
        CancellationToken cancellationToken)
    {
        var unsupportedReason = GetUnsupportedReason(request, profile);
        if (unsupportedReason is not null)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: unsupportedReason,
                ToolInvocations: [],
                Artifacts: null,
                TokenUsage: null);
        }

        var subAgentResolution = ResolveSubAgents(request.Contract.SubAgents);
        if (subAgentResolution.FailureReason is not null)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: subAgentResolution.FailureReason,
                ToolInvocations: [],
                Artifacts: null,
                TokenUsage: null);
        }

        var effectiveProfile = BuildEffectiveSandboxProfile(sandboxProfileName, request.RunId);
        var environmentVariables = await BuildEnvironmentVariablesAsync(profile, cancellationToken);
        var envelope = new SandboxedAgentRunEnvelope(
            request.RunId,
            request.StepId,
            request.AgentName,
            request.Action,
            request.Environment,
            request.PurposeType,
            request.PolicyTag,
            request.Attempt,
            ModelRunPromptFactory.BuildSystemPrompt(request),
            request.PromptSnapshot.FinalPrompt,
            profile?.Model ?? _languageModelOptions.Model,
            _languageModelOptions.MaxTokens,
            request.Contract,
            subAgentResolution.Profiles,
            request.Contract.SubAgents?.Enabled == true
                ? Math.Max(0, request.Contract.SubAgents.MaxDepth)
                : 0);

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope)));
        environmentVariables[EnvelopeEnvironmentVariable] = payload;

        var sandboxResult = await _sandboxExecutor.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: request.RunId,
                StepId: request.StepId,
                AgentName: request.AgentName,
                Action: request.Action,
                Environment: request.Environment,
                PurposeType: request.PurposeType,
                PolicyTag: request.PolicyTag,
                Attempt: request.Attempt,
                Image: profile?.DockerImage ?? _sandboxOptions.OpenSandbox.AgentRunnerImage,
                Profile: effectiveProfile,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["autofac.executionMode"] = AgentExecutionModes.AgentSandboxed,
                    ["autofac.sandboxProfile"] = sandboxProfileName
                },
                Command: new SandboxCommandSpec(
                    Arguments: ["dotnet", "Autofac.AgentRunner.dll"],
                    WorkingDirectory: "/app"),
                EnvironmentVariables: environmentVariables,
                ArtifactPaths: ["/output"]),
            cancellationToken);

        var sandboxExecution = ToSandboxExecutionRecord(sandboxResult);

        if (!sandboxResult.Artifacts.TryGetValue(ResultArtifactName, out var resultPayload))
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: sandboxResult.Logs,
                FailureReason: sandboxResult.FailureReason ?? "Sandboxed agent run did not produce an agent-run-result.json artifact.",
                ToolInvocations: [],
                Artifacts: RemoveInternalArtifacts(sandboxResult.Artifacts),
                TokenUsage: null,
                ElapsedMs: sandboxResult.Duration.TotalMilliseconds,
                SandboxExecution: sandboxExecution);
        }

        SandboxedAgentRunResult? runResult;
        try
        {
            runResult = JsonSerializer.Deserialize<SandboxedAgentRunResult>(resultPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: sandboxResult.Logs,
                FailureReason: $"Sandboxed agent run returned invalid result JSON: {ex.Message}",
                ToolInvocations: [],
                Artifacts: RemoveInternalArtifacts(sandboxResult.Artifacts),
                TokenUsage: null,
                ElapsedMs: sandboxResult.Duration.TotalMilliseconds,
                SandboxExecution: sandboxExecution);
        }

        if (runResult is null)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: sandboxResult.Logs,
                FailureReason: "Sandboxed agent run returned an empty result payload.",
                ToolInvocations: [],
                Artifacts: RemoveInternalArtifacts(sandboxResult.Artifacts),
                TokenUsage: null,
                ElapsedMs: sandboxResult.Duration.TotalMilliseconds,
                SandboxExecution: sandboxExecution);
        }

        var artifacts = RemoveInternalArtifacts(sandboxResult.Artifacts);
        if (runResult.Artifacts is { Count: > 0 })
        {
            foreach (var (name, content) in runResult.Artifacts)
            {
                artifacts[name] = content;
            }
        }

        return new ModelRunResult(
            Succeeded: sandboxResult.Succeeded && runResult.Succeeded,
            Output: runResult.Output,
            FailureReason: runResult.FailureReason ?? sandboxResult.FailureReason,
            ToolInvocations: runResult.ToolInvocations ?? [],
            Artifacts: artifacts.Count == 0 ? null : artifacts,
            TokenUsage: runResult.TokenUsage,
            ElapsedMs: sandboxResult.Duration.TotalMilliseconds,
            SandboxExecution: sandboxExecution);
    }

    private async Task<Dictionary<string, string>> BuildEnvironmentVariablesAsync(
        AgentProfile? profile,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_languageModelOptions.ApiKey))
        {
            environment[DefaultModelApiKeyEnvironmentVariable] = _languageModelOptions.ApiKey;
        }

        foreach (var secretName in profile?.Secrets ?? [])
        {
            var secretValue = await _secretStore.GetSecretAsync(secretName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                environment[secretName] = secretValue;
            }
        }

        return environment;
    }

    private static string? GetUnsupportedReason(ModelRunRequest request, AgentProfile? profile)
    {
        if ((profile?.Tools.Count ?? 0) > 0 || request.Contract.Tools.Count > 0)
        {
            return "Sandboxed agent runtime does not support in-sandbox tool execution yet. Use local/tool_sandboxed execution for tool-enabled agents.";
        }

        return null;
    }

    private SubAgentResolution ResolveSubAgents(AgentSubAgentContract? contract)
    {
        if (contract?.Enabled != true || contract.AllowedAgents.Count == 0)
        {
            return new SubAgentResolution([], null);
        }

        var profiles = new List<SandboxedSubAgentProfile>();
        var missing = new List<string>();
        foreach (var agentId in contract.AllowedAgents)
        {
            var profile = _agentRegistry.Find(agentId);
            if (profile is null)
            {
                missing.Add(agentId);
                continue;
            }

            profiles.Add(new SandboxedSubAgentProfile(
                profile.AgentId,
                profile.Name,
                profile.Description,
                profile.SystemPrompt,
                profile.Model));
        }

        if (missing.Count > 0)
        {
            return new SubAgentResolution(
                [],
                $"Sandboxed agent runtime could not resolve sub-agent profile(s): {string.Join(", ", missing)}.");
        }

        return new SubAgentResolution(profiles, null);
    }

    private static SandboxExecutionProfile BuildEffectiveSandboxProfile(string sandboxProfileName, string runId)
    {
        var profile = SandboxProfileCatalog.Resolve(sandboxProfileName, runId);
        var networkPolicy = profile.NetworkPolicy;
        var anthropicHosts = new[] { "api.anthropic.com" };

        SandboxNetworkPolicy? effectiveNetwork = networkPolicy switch
        {
            null => new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, anthropicHosts),
            { Mode: SandboxNetworkAccessMode.None } => new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, anthropicHosts),
            { Mode: SandboxNetworkAccessMode.Restricted } => new SandboxNetworkPolicy(
                SandboxNetworkAccessMode.Restricted,
                networkPolicy.AllowedHosts?
                    .Concat(anthropicHosts)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? anthropicHosts),
            _ => networkPolicy
        };

        return profile with
        {
            NetworkPolicy = effectiveNetwork
        };
    }

    private static Dictionary<string, string> RemoveInternalArtifacts(IReadOnlyDictionary<string, string> artifacts) =>
        artifacts
            .Where(static item => !string.Equals(item.Key, ResultArtifactName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static AgentSandboxExecutionRecord ToSandboxExecutionRecord(SandboxExecutionResult result) =>
        new()
        {
            Provider = result.Provider.ToConfigValue(),
            SandboxId = result.ProviderSandboxId,
            CommandState = result.CommandState.ToString(),
            ExitCode = result.ExitCode,
            DurationMs = (int)Math.Round(result.Duration.TotalMilliseconds),
            Logs = (result.StructuredLogs ?? [])
                .Select(static entry => new AgentSandboxLogRecord
                {
                    Stream = entry.Stream,
                    Message = entry.Message,
                    Timestamp = entry.Timestamp.ToString("o")
                })
                .ToArray(),
            Diagnostics = result.ProviderDiagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    private sealed record SubAgentResolution(
        IReadOnlyList<SandboxedSubAgentProfile> Profiles,
        string? FailureReason);
}
