using System.Text;
using System.Text.Json;
using Autofac.Application.Secrets;
using Autofac.Domain.AgentRuntime;
using Autofac.Integrations;
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
    private readonly IntegrationOptions _integrationOptions;
    private readonly LanguageModelOptions _languageModelOptions;
    private readonly SandboxOptions _sandboxOptions;

    public OpenSandboxedAgentRunner(
        ISandboxExecutor sandboxExecutor,
        ISecretStore secretStore,
        IOptions<IntegrationOptions> integrationOptions,
        IOptions<LanguageModelOptions> languageModelOptions,
        IOptions<SandboxOptions> sandboxOptions)
    {
        _sandboxExecutor = sandboxExecutor;
        _secretStore = secretStore;
        _integrationOptions = integrationOptions.Value;
        _languageModelOptions = languageModelOptions.Value;
        _sandboxOptions = sandboxOptions.Value;
    }

    public async Task<ModelRunResult> RunAsync(
        ModelRunRequest request,
        AgentProfile? profile,
        string sandboxProfileName,
        CancellationToken cancellationToken)
    {
        var toolResolution = ResolveTools(request, profile);
        if (toolResolution.FailureReason is not null)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: toolResolution.FailureReason,
                ToolInvocations: [],
                Artifacts: null,
                TokenUsage: null);
        }

        var effectiveProfile = BuildEffectiveSandboxProfile(sandboxProfileName, request.RunId);
        var environmentVariables = await BuildEnvironmentVariablesAsync(profile, toolResolution.Tools, cancellationToken);
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
            toolResolution.Tools,
            [],
            0);

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
        IReadOnlyList<SandboxedToolContract> resolvedTools,
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

        if (resolvedTools.Any(static tool =>
                string.Equals(tool.Name, "github.create_branch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.create_pull_request", StringComparison.OrdinalIgnoreCase)))
        {
            environment["Integrations__GitHub__Enabled"] = _integrationOptions.GitHub.Enabled ? "true" : "false";
            environment["Integrations__GitHub__ApiBaseUrl"] = _integrationOptions.GitHub.ApiBaseUrl;
            environment["Integrations__GitHub__RepositoryOwner"] = _integrationOptions.GitHub.RepositoryOwner;
            environment["Integrations__GitHub__RepositoryName"] = _integrationOptions.GitHub.RepositoryName;
            environment["Integrations__GitHub__DefaultBaseBranch"] = _integrationOptions.GitHub.DefaultBaseBranch;
            environment["Integrations__GitHub__BranchPrefix"] = _integrationOptions.GitHub.BranchPrefix;
            environment["Integrations__GitHub__CreateDraftPullRequests"] = _integrationOptions.GitHub.CreateDraftPullRequests ? "true" : "false";

            var pat = await _secretStore.GetSecretAsync("Integrations:GitHub:PersonalAccessToken", cancellationToken)
                ?? _integrationOptions.GitHub.PersonalAccessToken;
            if (!string.IsNullOrWhiteSpace(pat))
            {
                environment["Integrations__GitHub__PersonalAccessToken"] = pat;
            }
        }

        return environment;
    }

    private static ToolResolution ResolveTools(ModelRunRequest request, AgentProfile? profile)
    {
        if (request.Contract.McpServers.Count > 0)
        {
            return new ToolResolution([], "Sandboxed agent runtime does not support MCP servers yet.");
        }

        if (request.Contract.SubAgents?.Enabled == true)
        {
            return new ToolResolution([], "Sandboxed agent runtime does not support sub-agents yet.");
        }

        var requestedToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in profile?.Tools ?? [])
        {
            requestedToolNames.Add(CanonicalizeToolName(tool));
        }

        foreach (var tool in request.Contract.Tools)
        {
            requestedToolNames.Add(CanonicalizeToolName(tool.Name));
        }

        foreach (var tool in request.Contract.Permissions.AllowedTools)
        {
            requestedToolNames.Add(CanonicalizeToolName(tool));
        }

        var deniedToolNames = new HashSet<string>(request.Contract.Permissions.DeniedTools, StringComparer.OrdinalIgnoreCase);
        foreach (var tool in profile?.DeniedTools ?? [])
        {
            deniedToolNames.Add(CanonicalizeToolName(tool));
        }

        var resolvedTools = new List<SandboxedToolContract>();
        var unsupported = new List<string>();
        foreach (var toolName in requestedToolNames)
        {
            if (deniedToolNames.Contains(toolName))
            {
                continue;
            }

            if (!TryResolveSupportedTool(toolName, out var tool))
            {
                unsupported.Add(toolName);
                continue;
            }

            resolvedTools.Add(tool!);
        }

        if (unsupported.Count > 0)
        {
            return new ToolResolution(
                [],
                $"Sandboxed agent runtime does not support tool(s): {string.Join(", ", unsupported)}. Supported tools: github.create_branch, github.create_pull_request.");
        }

        return new ToolResolution(
            resolvedTools
                .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            null);
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

    private static string CanonicalizeToolName(string toolName) =>
        string.Equals(toolName, "github.create_pr", StringComparison.OrdinalIgnoreCase)
            ? "github.create_pull_request"
            : toolName;

    private static bool TryResolveSupportedTool(string toolName, out SandboxedToolContract? tool)
    {
        tool = toolName switch
        {
            "github.create_branch" => new SandboxedToolContract("github.create_branch", AgentToolCategories.Integration),
            "github.create_pull_request" => new SandboxedToolContract("github.create_pull_request", AgentToolCategories.Integration),
            _ => null
        };

        return tool is not null;
    }

    private sealed record ToolResolution(
        IReadOnlyList<SandboxedToolContract> Tools,
        string? FailureReason);
}
