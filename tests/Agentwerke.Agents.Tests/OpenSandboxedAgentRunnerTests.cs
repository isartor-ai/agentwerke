using System.Text;
using System.Text.Json;
using Agentwerke.Agents;
using Agentwerke.Agents.Models;
using Agentwerke.Application.Secrets;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Integrations;
using Agentwerke.Sandboxes;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class OpenSandboxedAgentRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenProfileUsesUnsupportedTool_ReturnsClearUnsupportedFailure()
    {
        var runner = CreateRunner(new RecordingSandboxExecutor());

        var result = await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code",
                Tools = ["web_search"]
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not support tool(s)", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_BuildsSandboxRequestAndParsesResultArtifact()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(sandbox);

        var result = await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code",
                DockerImage = "agentwerke/agent-runner:test"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("sandboxed spec", result.Output);
        Assert.NotNull(result.TokenUsage);
        Assert.Equal("agentwerke/agent-runner:test", sandbox.LastRequest!.Image);
        Assert.Equal(AgentExecutionModes.AgentSandboxed, sandbox.LastRequest.Metadata!["agentwerke.executionMode"]);
        Assert.Equal("offline", sandbox.LastRequest.Metadata["agentwerke.sandboxProfile"]);
        Assert.Equal("dotnet", sandbox.LastRequest.Command!.Arguments[0]);
        Assert.NotNull(result.SandboxExecution);
        Assert.Equal("opensandbox", result.SandboxExecution!.Provider);
        Assert.Single(result.ToolInvocations);
        Assert.Equal("mcp.weather.lookup", result.ToolInvocations[0].ToolName);
        Assert.DoesNotContain("token=secret", result.ToolInvocations[0].InputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", result.ToolInvocations[0].InputSummary);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesSupportedGitHubTool_EmbedsResolvedToolAndGitHubConfig()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            secretStore: new StubSecretStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Anthropic:ApiKey"] = "sk-ant-api03-SECRET-FROM-STORE",
                ["Integrations:GitHub:PersonalAccessToken"] = "ghp_secret_token_abcdefghijklmnopqrstuvwxyz123456"
            }),
            integrationOptions: new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    ApiBaseUrl = "https://api.github.com/",
                    RepositoryOwner = "isartor-ai",
                    RepositoryName = "agentwerke",
                    PersonalAccessToken = "gh-test",
                    DefaultBaseBranch = "main",
                    BranchPrefix = "agentwerke/run-",
                    CreateDraftPullRequests = true
                }
            });

        await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "github-agent",
                Runner = "claude-code",
                Tools = ["github.create_branch"]
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        var payload = sandbox.LastRequest!.EnvironmentVariables!["AGENTWERKE_AGENT_RUN_ENVELOPE_B64"];
        var envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(
            Encoding.UTF8.GetString(Convert.FromBase64String(payload)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        var tool = Assert.Single(envelope!.ResolvedTools);
        Assert.Equal("github.create_branch", tool.Name);
        Assert.Equal(AgentToolCategories.Integration, tool.Category);
        Assert.Equal("sk-ant-api03-SECRET-FROM-STORE", sandbox.LastRequest.EnvironmentVariables["AGENTWERKE_MODEL_API_KEY"]);
        Assert.DoesNotContain(sandbox.LastRequest.EnvironmentVariables.Keys, key => string.Equals(key, "ANTHROPIC_API_KEY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("anthropic", sandbox.LastRequest.EnvironmentVariables["AGENTWERKE_MODEL_PROVIDER"]);
        Assert.Equal("isartor-ai", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryOwner"]);
        Assert.Equal("agentwerke", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryName"]);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesIssueLifecycleTools_EmbedsThemForSandboxRunner()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            integrationOptions: new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    RepositoryOwner = "isartor-ai",
                    RepositoryName = "agentwerke-demo",
                    PersonalAccessToken = "gh-test"
                }
            });

        await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "issue-agent",
                Runner = "claude-code",
                Tools = ["github.comment_issue", "github.close_issue"]
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        var payload = sandbox.LastRequest!.EnvironmentVariables!["AGENTWERKE_AGENT_RUN_ENVELOPE_B64"];
        var envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(
            Encoding.UTF8.GetString(Convert.FromBase64String(payload)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Contains(envelope!.ResolvedTools, t => t.Name == "github.comment_issue");
        Assert.Contains(envelope.ResolvedTools, t => t.Name == "github.close_issue");
        Assert.Equal("isartor-ai", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryOwner"]);
        Assert.Equal("agentwerke-demo", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryName"]);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesSandboxCodeTools_ResolvesThemAllWithoutFailing()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(sandbox);

        var result = await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "implementation-engineer",
                Runner = "claude-code",
                Tools = ["sandbox.file_read", "sandbox.file_write", "sandbox.file_edit", "sandbox.shell", "sandbox.run_tests"]
            },
            SandboxProfileNames.RepoWrite,
            CancellationToken.None);

        Assert.True(result.Succeeded);

        var payload = sandbox.LastRequest!.EnvironmentVariables!["AGENTWERKE_AGENT_RUN_ENVELOPE_B64"];
        var envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(
            Encoding.UTF8.GetString(Convert.FromBase64String(payload)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Equal(5, envelope!.ResolvedTools.Count);
        Assert.Contains(envelope.ResolvedTools, t => t.Name == "sandbox.file_edit");
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesSandboxGitTool_EmbedsGitHubConfigJustLikeGitHubTools()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            secretStore: new StubSecretStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Integrations:GitHub:PersonalAccessToken"] = "ghp_secret_token_abcdefghijklmnopqrstuvwxyz123456"
            }),
            integrationOptions: new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    RepositoryOwner = "isartor-ai",
                    RepositoryName = "agentwerke"
                }
            });

        await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "implementation-engineer",
                Runner = "claude-code",
                Tools = ["sandbox.git"]
            },
            SandboxProfileNames.RepoWrite,
            CancellationToken.None);

        Assert.Equal("isartor-ai", sandbox.LastRequest!.EnvironmentVariables!["Integrations__GitHub__RepositoryOwner"]);
        Assert.Equal("agentwerke", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryName"]);
        Assert.Equal("ghp_secret_token_abcdefghijklmnopqrstuvwxyz123456", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__PersonalAccessToken"]);
    }

    [Fact]
    public async Task RunAsync_WhenCustomModelEndpointConfigured_AllowlistsHostAndRedactsDiagnostics()
    {
        var sandbox = new RecordingSandboxExecutor(
            logs: "Authorization: Bearer sk-ant-api03-VERY-SECRET",
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider"] = "opensandbox",
                ["execd.last_error"] = "token=sk-ant-api03-VERY-SECRET"
            });
        var runner = CreateRunner(
            sandbox,
            secretStore: new StubSecretStore(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Anthropic:ApiKey"] = "sk-ant-api03-VERY-SECRET"
            }),
            languageModelOptions: new LanguageModelOptions
            {
                Model = "claude-sonnet-4-6",
                MaxTokens = 2048,
                ApiBaseUrl = "https://sandbox.anthropic.internal/v1/"
            });

        var result = await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.Contains("sandbox.anthropic.internal", sandbox.LastRequest!.Profile!.NetworkPolicy!.AllowedHosts!);
        Assert.Equal("https://sandbox.anthropic.internal/v1/", result.SandboxExecution!.Diagnostics["model.api_base_url"]);
        Assert.Equal("sandbox.anthropic.internal", result.SandboxExecution.Diagnostics["model.endpoint_host"]);
        Assert.Equal("secret-store", result.SandboxExecution.Diagnostics["model.credential_source"]);
        Assert.DoesNotContain("sk-ant-api03-", result.SandboxExecution.Diagnostics["execd.last_error"]);
        Assert.Contains("[redacted]", result.SandboxExecution.Diagnostics["execd.last_error"]);
        Assert.DoesNotContain("sk-ant-api03-", Assert.Single(result.SandboxExecution.Logs).Message);
        Assert.Contains("[redacted]", Assert.Single(result.SandboxExecution.Logs).Message);
    }

    [Fact]
    public async Task RunAsync_WhenLiteLlmProviderConfigured_EmbedsProviderForSandboxRunner()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            languageModelOptions: new LanguageModelOptions
            {
                Provider = "litellm",
                ApiKey = "litellm-proxy-key",
                ApiBaseUrl = "http://litellm:4000/v1",
                Model = "z-ai/glm-5.2",
                MaxTokens = 1024
            });

        var result = await runner.RunAsync(
            MakeRequest(),
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.Equal("litellm", sandbox.LastRequest!.EnvironmentVariables!["AGENTWERKE_MODEL_PROVIDER"]);
        Assert.Equal("http://litellm:4000/v1", sandbox.LastRequest.EnvironmentVariables["AGENTWERKE_MODEL_API_BASE_URL"]);
        Assert.Contains("litellm", sandbox.LastRequest.Profile!.NetworkPolicy!.AllowedHosts!);
        Assert.Equal("litellm", result.SandboxExecution!.Diagnostics["model.provider"]);
    }

    [Fact]
    public async Task RunAsync_WhenSubAgentsConfigured_EmbedsResolvedProfilesInEnvelope()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            registry: new FileAgentRegistry(
            [
                new AgentProfile
                {
                    AgentId = "weather-agent",
                    Name = "Weather Agent",
                    SystemPrompt = "You are a weather specialist."
                }
            ]));

        await runner.RunAsync(
            MakeRequest() with
            {
                Contract = new AgentRuntimeContract
                {
                    McpServers =
                    [
                        new AgentMcpServerContract { Name = "weather", Transport = "http", Url = "https://example.test/mcp" }
                    ],
                    SubAgents = new AgentSubAgentContract
                    {
                        Enabled = true,
                        MaxDepth = 2,
                        AllowedAgents = ["weather-agent"]
                    }
                }
            },
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        var payload = sandbox.LastRequest!.EnvironmentVariables!["AGENTWERKE_AGENT_RUN_ENVELOPE_B64"];
        var envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(
            Encoding.UTF8.GetString(Convert.FromBase64String(payload)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        Assert.Single(envelope!.Contract.McpServers);
        Assert.Equal(2, envelope.RemainingSubAgentDepth);
        Assert.Single(envelope.SubAgents);
        Assert.Equal("weather-agent", envelope.SubAgents[0].AgentId);
    }

    [Fact]
    public async Task RunAsync_WhenAllowedSubAgentIsMissing_ReturnsClearFailure()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(sandbox, registry: new FileAgentRegistry([]));

        var result = await runner.RunAsync(
            MakeRequest() with
            {
                Contract = new AgentRuntimeContract
                {
                    SubAgents = new AgentSubAgentContract
                    {
                        Enabled = true,
                        AllowedAgents = ["missing-agent"]
                    }
                }
            },
            new AgentProfile
            {
                AgentId = "spec-writer",
                Runner = "claude-code"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("missing-agent", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sandbox.LastRequest);
    }

    private static OpenSandboxedAgentRunner CreateRunner(
        RecordingSandboxExecutor sandbox,
        IAgentRegistry? registry = null,
        IntegrationOptions? integrationOptions = null,
        LanguageModelOptions? languageModelOptions = null,
        ISecretStore? secretStore = null) =>
        new(
            sandbox,
            secretStore ?? new StubSecretStore(),
            registry ?? new FileAgentRegistry([]),
            Options.Create(integrationOptions ?? new IntegrationOptions()),
            Options.Create(languageModelOptions ?? new LanguageModelOptions
            {
                ApiKey = "test-key",
                Model = "claude-sonnet-4-6",
                MaxTokens = 2048
            }),
            Options.Create(new SandboxOptions
            {
                OpenSandbox = new OpenSandboxProviderOptions
                {
                    AgentRunnerImage = "agentwerke/agent-runner:latest"
                }
            }));

    private static ModelRunRequest MakeRequest() =>
        new(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "spec-writer",
            Action: "spec.generate",
            Environment: "ci",
            PurposeType: "specification",
            PolicyTag: "doc-generation",
            RequiresEvidence: [],
            Attempt: 1,
            PromptSnapshot: new AgentPromptSnapshot(
                "Write a concise spec.",
                DateTimeOffset.UtcNow.ToString("o"),
                [],
                new Dictionary<string, string>(),
                []),
            Contract: new AgentRuntimeContract());

    private sealed class StubSecretStore : ISecretStore
    {
        private readonly IReadOnlyDictionary<string, string> _secrets;

        public StubSecretStore(IReadOnlyDictionary<string, string>? secrets = null)
        {
            _secrets = secrets ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_secrets.TryGetValue(key, out var value) ? value : null);
    }

    private sealed class RecordingSandboxExecutor : ISandboxExecutor
    {
        private readonly string _logs;
        private readonly IReadOnlyDictionary<string, string> _diagnostics;

        public RecordingSandboxExecutor(
            string logs = "sandbox logs",
            IReadOnlyDictionary<string, string>? diagnostics = null)
        {
            _logs = logs;
            _diagnostics = diagnostics ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["provider"] = "opensandbox"
            };
        }

        public SandboxExecutionRequest? LastRequest { get; private set; }

        public Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var payload = new SandboxedAgentRunResult(
                Succeeded: true,
                Output: "sandboxed spec",
                FailureReason: null,
                TokenUsage: new AgentModelTokenUsage(11, 29, "claude-sonnet-4-6"),
                ToolInvocations:
                [
                    new AgentToolInvocationRecord
                    {
                        ToolName = "mcp.weather.lookup",
                        Category = AgentToolCategories.Mcp,
                        Status = "completed",
                        InputSummary = "token=ghp_abcdefghijklmnopqrstuvwxyz123456789012",
                        OutputSummary = "Authorization: Bearer sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                    }
                ]);

            return Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                Logs: _logs,
                FailureReason: null,
                Artifacts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agent-run-result.json"] = JsonSerializer.Serialize(payload)
                },
                ExitCode: 0,
                Duration: TimeSpan.FromSeconds(1),
                ProviderSandboxId: "sbx-123",
                CommandState: SandboxCommandState.Completed,
                StructuredLogs:
                [
                    new SandboxLogEntry("stdout", _logs, DateTimeOffset.UtcNow)
                ],
                ProviderDiagnostics: new Dictionary<string, string>(_diagnostics, StringComparer.OrdinalIgnoreCase),
                Provider: SandboxProviderKind.OpenSandbox));
        }
    }
}
