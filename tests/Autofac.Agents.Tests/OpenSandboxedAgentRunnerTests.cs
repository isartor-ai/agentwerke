using Autofac.Agents.Models;
using Autofac.Application.Secrets;
using Autofac.Domain.AgentRuntime;
using Autofac.Integrations;
using Autofac.Sandboxes;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Autofac.Agents.Tests;

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
                DockerImage = "autofac/agent-runner:test"
            },
            SandboxProfileNames.Offline,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("sandboxed spec", result.Output);
        Assert.NotNull(result.TokenUsage);
        Assert.Equal("autofac/agent-runner:test", sandbox.LastRequest!.Image);
        Assert.Equal(AgentExecutionModes.AgentSandboxed, sandbox.LastRequest.Metadata!["autofac.executionMode"]);
        Assert.Equal("offline", sandbox.LastRequest.Metadata["autofac.sandboxProfile"]);
        Assert.Equal("dotnet", sandbox.LastRequest.Command!.Arguments[0]);
        Assert.NotNull(result.SandboxExecution);
        Assert.Equal("opensandbox", result.SandboxExecution!.Provider);
        Assert.Single(result.ToolInvocations);
        Assert.Equal("github.create_branch", result.ToolInvocations[0].ToolName);
    }

    [Fact]
    public async Task RunAsync_WhenProfileUsesSupportedGitHubTool_EmbedsResolvedToolAndGitHubConfig()
    {
        var sandbox = new RecordingSandboxExecutor();
        var runner = CreateRunner(
            sandbox,
            integrationOptions: new IntegrationOptions
            {
                GitHub = new GitHubOptions
                {
                    Enabled = true,
                    ApiBaseUrl = "https://api.github.com/",
                    RepositoryOwner = "isartor-ai",
                    RepositoryName = "autofac",
                    PersonalAccessToken = "gh-test",
                    DefaultBaseBranch = "main",
                    BranchPrefix = "autofac/run-",
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

        var payload = sandbox.LastRequest!.EnvironmentVariables!["AUTOFAC_AGENT_RUN_ENVELOPE_B64"];
        var envelope = JsonSerializer.Deserialize<SandboxedAgentRunEnvelope>(
            Encoding.UTF8.GetString(Convert.FromBase64String(payload)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(envelope);
        var tool = Assert.Single(envelope!.ResolvedTools);
        Assert.Equal("github.create_branch", tool.Name);
        Assert.Equal(AgentToolCategories.Integration, tool.Category);
        Assert.Equal("isartor-ai", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryOwner"]);
        Assert.Equal("autofac", sandbox.LastRequest.EnvironmentVariables["Integrations__GitHub__RepositoryName"]);
    }

    private static OpenSandboxedAgentRunner CreateRunner(
        RecordingSandboxExecutor sandbox,
        IntegrationOptions? integrationOptions = null) =>
        new(
            sandbox,
            new StubSecretStore(),
            Options.Create(integrationOptions ?? new IntegrationOptions()),
            Options.Create(new LanguageModelOptions
            {
                ApiKey = "test-key",
                Model = "claude-sonnet-4-6",
                MaxTokens = 2048
            }),
            Options.Create(new SandboxOptions
            {
                OpenSandbox = new OpenSandboxProviderOptions
                {
                    AgentRunnerImage = "autofac/agent-runner:latest"
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
        public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RecordingSandboxExecutor : ISandboxExecutor
    {
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
                        ToolName = "github.create_branch",
                        Category = AgentToolCategories.Integration,
                        Status = "completed"
                    }
                ]);

            return Task.FromResult(new SandboxExecutionResult(
                Succeeded: true,
                Logs: "sandbox logs",
                FailureReason: null,
                Artifacts: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["agent-run-result.json"] = System.Text.Json.JsonSerializer.Serialize(payload)
                },
                ExitCode: 0,
                Duration: TimeSpan.FromSeconds(1),
                ProviderSandboxId: "sbx-123",
                CommandState: SandboxCommandState.Completed,
                StructuredLogs:
                [
                    new SandboxLogEntry("stdout", "sandboxed spec", DateTimeOffset.UtcNow)
                ],
                ProviderDiagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider"] = "opensandbox"
                },
                Provider: SandboxProviderKind.OpenSandbox));
        }
    }
}
