using System.Text;
using System.Text.Json;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Security;
using Agentwerke.Integrations;
using Agentwerke.Sandboxes;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Models;

public sealed class OpenSandboxedAgentRunner : ISandboxedAgentRunner
{
    private const string ResultArtifactName = "agent-run-result.json";
    private const string EnvelopeEnvironmentVariable = "AGENTWERKE_AGENT_RUN_ENVELOPE_B64";
    private const string ModelApiKeyEnvironmentVariable = "AGENTWERKE_MODEL_API_KEY";
    private const string ModelProviderEnvironmentVariable = "AGENTWERKE_MODEL_PROVIDER";
    private const string ModelIdEnvironmentVariable = "AGENTWERKE_MODEL_ID";
    private const string ModelApiBaseUrlEnvironmentVariable = "AGENTWERKE_MODEL_API_BASE_URL";
    private const string ModelTimeoutSecondsEnvironmentVariable = "AGENTWERKE_MODEL_TIMEOUT_SECONDS";
    private const string ModelMaxToolIterationsEnvironmentVariable = "AGENTWERKE_MODEL_MAX_TOOL_ITERATIONS";

    private readonly ISandboxExecutor _sandboxExecutor;
    private readonly ISecretStore _secretStore;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IntegrationOptions _integrationOptions;
    private readonly LanguageModelOptions _languageModelOptions;
    private readonly SandboxOptions _sandboxOptions;

    public OpenSandboxedAgentRunner(
        ISandboxExecutor sandboxExecutor,
        ISecretStore secretStore,
        IAgentRegistry agentRegistry,
        IOptions<IntegrationOptions> integrationOptions,
        IOptions<LanguageModelOptions> languageModelOptions,
        IOptions<SandboxOptions> sandboxOptions)
    {
        _sandboxExecutor = sandboxExecutor;
        _secretStore = secretStore;
        _agentRegistry = agentRegistry;
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

        var modelRuntime = await ResolveModelRuntimeAsync(profile, cancellationToken);
        if (modelRuntime.FailureReason is not null)
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: null,
                FailureReason: modelRuntime.FailureReason,
                ToolInvocations: [],
                Artifacts: null,
                TokenUsage: null);
        }

        var effectiveProfile = BuildEffectiveSandboxProfile(sandboxProfileName, request.RunId, modelRuntime.EndpointHost);
        var environmentVariables = await BuildEnvironmentVariablesAsync(profile, toolResolution.Tools, modelRuntime, cancellationToken);
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
                    ["agentwerke.executionMode"] = AgentExecutionModes.AgentSandboxed,
                    ["agentwerke.sandboxProfile"] = sandboxProfileName
                },
                Command: new SandboxCommandSpec(
                    Arguments: ["dotnet", "Agentwerke.AgentRunner.dll"],
                    WorkingDirectory: "/app"),
                EnvironmentVariables: environmentVariables,
                ArtifactPaths: ["/output"]),
            cancellationToken);

        var sandboxExecution = ToSandboxExecutionRecord(sandboxResult, modelRuntime, effectiveProfile);

        if (!sandboxResult.Artifacts.TryGetValue(ResultArtifactName, out var resultPayload))
        {
            return new ModelRunResult(
                Succeeded: false,
                Output: SecretRedactor.Redact(sandboxResult.Logs),
                FailureReason: SecretRedactor.Redact(sandboxResult.FailureReason ?? "Sandboxed agent run did not produce an agent-run-result.json artifact."),
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
                Output: SecretRedactor.Redact(sandboxResult.Logs),
                FailureReason: SecretRedactor.Redact($"Sandboxed agent run returned invalid result JSON: {ex.Message}"),
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
                Output: SecretRedactor.Redact(sandboxResult.Logs),
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
            FailureReason: RedactOptional(runResult.FailureReason ?? sandboxResult.FailureReason),
            ToolInvocations: SanitizeToolInvocations(runResult.ToolInvocations ?? []),
            Artifacts: artifacts.Count == 0 ? null : artifacts,
            TokenUsage: runResult.TokenUsage,
            ElapsedMs: sandboxResult.Duration.TotalMilliseconds,
            SandboxExecution: sandboxExecution,
            ModelTrace: runResult.ModelTrace);
    }

    private async Task<ModelRuntimeResolution> ResolveModelRuntimeAsync(
        AgentProfile? profile,
        CancellationToken cancellationToken)
    {
        var secretStoreApiKey = await _secretStore.GetSecretAsync("Anthropic:ApiKey", cancellationToken);
        var apiKey = secretStoreApiKey ?? _languageModelOptions.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ModelRuntimeResolution(
                null,
                NormalizeModelApiBaseUrl(_languageModelOptions.ApiBaseUrl),
                ResolveModelEndpointHost(_languageModelOptions.ApiBaseUrl),
                profile?.Model ?? _languageModelOptions.Model,
                "missing",
                "Sandboxed agent runtime model credential is not configured. Set 'Anthropic:ApiKey' via secret store or configuration.");
        }

        return new ModelRuntimeResolution(
            apiKey,
            NormalizeModelApiBaseUrl(_languageModelOptions.ApiBaseUrl),
            ResolveModelEndpointHost(_languageModelOptions.ApiBaseUrl),
            profile?.Model ?? _languageModelOptions.Model,
            !string.IsNullOrWhiteSpace(secretStoreApiKey) ? "secret-store" : "configuration",
            null);
    }

    private async Task<Dictionary<string, string>> BuildEnvironmentVariablesAsync(
        AgentProfile? profile,
        IReadOnlyList<SandboxedToolContract> resolvedTools,
        ModelRuntimeResolution modelRuntime,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        environment[ModelProviderEnvironmentVariable] = ResolveModelProvider(_languageModelOptions.Provider);
        environment[ModelIdEnvironmentVariable] = modelRuntime.ModelId;
        environment[ModelApiBaseUrlEnvironmentVariable] = modelRuntime.ApiBaseUrl;
        environment[ModelApiKeyEnvironmentVariable] = modelRuntime.ApiKey!;
        // The sandboxed runner builds its own HTTP client; without these it falls back to the
        // LanguageModelOptions defaults (100s / 10 iterations) regardless of the API's config.
        environment[ModelTimeoutSecondsEnvironmentVariable] =
            _languageModelOptions.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment[ModelMaxToolIterationsEnvironmentVariable] =
            _languageModelOptions.MaxToolIterations.ToString(System.Globalization.CultureInfo.InvariantCulture);

        foreach (var secretName in profile?.Secrets ?? [])
        {
            if (IsReservedSandboxSecret(secretName))
            {
                continue;
            }

            var secretValue = await _secretStore.GetSecretAsync(secretName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(secretValue))
            {
                environment[secretName] = secretValue;
            }
        }

        if (resolvedTools.Any(static tool =>
                string.Equals(tool.Name, "github.read_issue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.comment_issue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.close_issue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.create_branch", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.create_pull_request", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.request_review", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Name, "github.post_review", StringComparison.OrdinalIgnoreCase) ||
                // sandbox.git clones/pushes against the same repository using the same PAT (#140).
                string.Equals(tool.Name, "sandbox.git", StringComparison.OrdinalIgnoreCase)))
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
        var requestedToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in profile?.Tools ?? [])
        {
            if (!IsExternallyResolvedTool(tool))
            {
                requestedToolNames.Add(CanonicalizeToolName(tool));
            }
        }

        foreach (var tool in request.Contract.Tools)
        {
            if (string.Equals(tool.Category, AgentToolCategories.Mcp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tool.Category, AgentToolCategories.SubAgent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            requestedToolNames.Add(CanonicalizeToolName(tool.Name));
        }

        foreach (var tool in request.Contract.Permissions.AllowedTools)
        {
            if (!IsExternallyResolvedTool(tool))
            {
                requestedToolNames.Add(CanonicalizeToolName(tool));
            }
        }

        var deniedToolNames = new HashSet<string>(request.Contract.Permissions.DeniedTools.Select(CanonicalizeToolName), StringComparer.OrdinalIgnoreCase);
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
                $"Sandboxed agent runtime does not support tool(s): {string.Join(", ", unsupported)}. Supported tools: github.read_issue, github.comment_issue, github.close_issue, github.create_branch, github.create_pull_request, github.request_review, github.post_review, sandbox.file_read, sandbox.file_write, sandbox.file_edit, sandbox.git, sandbox.shell, sandbox.run_tests.");
        }

        return new ToolResolution(
            resolvedTools
                .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            null);
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

    private static SandboxExecutionProfile BuildEffectiveSandboxProfile(string sandboxProfileName, string runId, string modelEndpointHost)
    {
        var profile = SandboxProfileCatalog.Resolve(sandboxProfileName, runId);
        var networkPolicy = profile.NetworkPolicy;
        var modelHosts = new[] { modelEndpointHost };

        SandboxNetworkPolicy? effectiveNetwork = networkPolicy switch
        {
            null => new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, modelHosts),
            { Mode: SandboxNetworkAccessMode.None } => new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, modelHosts),
            { Mode: SandboxNetworkAccessMode.Restricted } => new SandboxNetworkPolicy(
                SandboxNetworkAccessMode.Restricted,
                networkPolicy.AllowedHosts?
                    .Concat(modelHosts)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? modelHosts),
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

    private AgentSandboxExecutionRecord ToSandboxExecutionRecord(
        SandboxExecutionResult result,
        ModelRuntimeResolution modelRuntime,
        SandboxExecutionProfile profile)
    {
        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["model.provider"] = ResolveModelProvider(_languageModelOptions.Provider),
            ["model.id"] = modelRuntime.ModelId,
            ["model.api_base_url"] = modelRuntime.ApiBaseUrl,
            ["model.endpoint_host"] = modelRuntime.EndpointHost,
            ["model.credential_source"] = modelRuntime.CredentialSource,
            ["model.credential_binding"] = ModelApiKeyEnvironmentVariable,
            ["sandbox.network.mode"] = (profile.NetworkPolicy?.Mode ?? SandboxNetworkAccessMode.None).ToString(),
            ["sandbox.network.allowed_hosts"] = string.Join(",", profile.NetworkPolicy?.AllowedHosts ?? [])
        };

        foreach (var pair in result.ProviderDiagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            diagnostics[pair.Key] = SecretRedactor.Redact(pair.Value);
        }

        return new AgentSandboxExecutionRecord
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
                    Message = SecretRedactor.Redact(entry.Message),
                    Timestamp = entry.Timestamp.ToString("o")
                })
                .ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static IReadOnlyList<AgentToolInvocationRecord> SanitizeToolInvocations(IReadOnlyList<AgentToolInvocationRecord> toolInvocations) =>
        toolInvocations
                .Select(static tool => tool with
            {
                InputSummary = RedactOptional(tool.InputSummary),
                OutputSummary = RedactOptional(tool.OutputSummary),
                ErrorMessage = RedactOptional(tool.ErrorMessage)
            })
            .ToArray();

    private static string CanonicalizeToolName(string toolName) =>
        string.Equals(toolName, "github.create_pr", StringComparison.OrdinalIgnoreCase)
            ? "github.create_pull_request"
            : toolName;

    private static bool IsReservedSandboxSecret(string secretName) =>
        string.Equals(secretName, "Anthropic:ApiKey", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(secretName, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(secretName, ModelApiKeyEnvironmentVariable, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModelApiBaseUrl(string? apiBaseUrl)
    {
        if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri))
        {
            return uri.ToString();
        }

        return LanguageModelOptions.DefaultApiBaseUrl;
    }

    private static string ResolveModelEndpointHost(string? apiBaseUrl)
    {
        if (Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host;
        }

        return new Uri(LanguageModelOptions.DefaultApiBaseUrl, UriKind.Absolute).Host;
    }

    private static string ResolveModelProvider(string? provider)
    {
        if (string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "litellm", StringComparison.OrdinalIgnoreCase))
        {
            return provider!.ToLowerInvariant();
        }

        return "anthropic";
    }

    private static string? RedactOptional(string? value) =>
        value is null ? null : SecretRedactor.Redact(value);

    private static bool TryResolveSupportedTool(string toolName, out SandboxedToolContract? tool)
    {
        tool = toolName switch
        {
            "github.read_issue" => new SandboxedToolContract("github.read_issue", AgentToolCategories.Integration),
            "github.comment_issue" => new SandboxedToolContract("github.comment_issue", AgentToolCategories.Integration),
            "github.close_issue" => new SandboxedToolContract("github.close_issue", AgentToolCategories.Integration),
            "github.create_branch" => new SandboxedToolContract("github.create_branch", AgentToolCategories.Integration),
            "github.create_pull_request" => new SandboxedToolContract("github.create_pull_request", AgentToolCategories.Integration),
            "github.request_review" => new SandboxedToolContract("github.request_review", AgentToolCategories.Integration),
            "github.post_review" => new SandboxedToolContract("github.post_review", AgentToolCategories.Integration),
            // Code-writing tools for implementation/review/test agents (#130, #140) — operate on
            // the sandbox's mounted /workspace, not on the host.
            "sandbox.file_read" => new SandboxedToolContract("sandbox.file_read", AgentToolCategories.Read),
            "sandbox.file_write" => new SandboxedToolContract("sandbox.file_write", AgentToolCategories.Write),
            "sandbox.file_edit" => new SandboxedToolContract("sandbox.file_edit", AgentToolCategories.Write),
            "sandbox.git" => new SandboxedToolContract("sandbox.git", AgentToolCategories.Shell),
            "sandbox.shell" => new SandboxedToolContract("sandbox.shell", AgentToolCategories.Shell),
            "sandbox.run_tests" => new SandboxedToolContract("sandbox.run_tests", AgentToolCategories.Shell),
            _ => null
        };

        return tool is not null;
    }

    private static bool IsExternallyResolvedTool(string toolName)
    {
        var canonical = CanonicalizeToolName(toolName);
        return canonical.StartsWith("mcp.", StringComparison.OrdinalIgnoreCase) ||
               canonical.StartsWith("subagent.", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ToolResolution(
        IReadOnlyList<SandboxedToolContract> Tools,
        string? FailureReason);

    private sealed record SubAgentResolution(
        IReadOnlyList<SandboxedSubAgentProfile> Profiles,
        string? FailureReason);

    private sealed record ModelRuntimeResolution(
        string? ApiKey,
        string ApiBaseUrl,
        string EndpointHost,
        string ModelId,
        string CredentialSource,
        string? FailureReason);
}
