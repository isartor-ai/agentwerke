using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autofac.Sandboxes.Tests;

public sealed class OpenSandboxSandboxExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_FailedCommand_MergesDiagnosticsAndDeletesSandbox()
    {
        var client = new FakeOpenSandboxClient
        {
            CreateResult = new OpenSandboxSandboxHandle(
                "sbx-1",
                new Dictionary<string, string> { ["lifecycle.create.request_id"] = "create-req" }),
            CommandResult = new OpenSandboxCommandResult(
                State: SandboxCommandState.Failed,
                ExitCode: 2,
                Logs: "bad run",
                StructuredLogs: [],
                ExecutionId: "cmd-1",
                SessionId: null,
                FailureReason: "command exploded",
                Diagnostics: new Dictionary<string, string> { ["execd.run.request_id"] = "run-req" }),
            DiagnosticsResult = new OpenSandboxDiagnosticsResult(
                new Dictionary<string, string> { ["sandbox.state"] = "Failed" })
        };

        var executor = CreateExecutor(client);

        var result = await executor.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: "run-126",
                StepId: "step-7",
                AgentName: "sandbox-agent",
                Action: "execute",
                Environment: "test",
                PurposeType: "implementation",
                PolicyTag: "issue-126",
                Attempt: 1,
                Command: new SandboxCommandSpec(["sh", "-c", "exit 2"])),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("command exploded", result.FailureReason);
        Assert.Equal("sbx-1", result.ProviderSandboxId);
        Assert.Equal(SandboxCommandState.Failed, result.CommandState);
        Assert.NotNull(result.ProviderDiagnostics);
        Assert.Equal("opensandbox", result.ProviderDiagnostics!["provider"]);
        Assert.Equal("create-req", result.ProviderDiagnostics["lifecycle.create.request_id"]);
        Assert.Equal("run-req", result.ProviderDiagnostics["execd.run.request_id"]);
        Assert.Equal("Failed", result.ProviderDiagnostics["sandbox.state"]);
        Assert.Equal(1, client.DeleteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetainSandboxOnFailure_SkipsDelete()
    {
        var client = new FakeOpenSandboxClient
        {
            CreateResult = new OpenSandboxSandboxHandle("sbx-2"),
            CommandResult = new OpenSandboxCommandResult(
                State: SandboxCommandState.Failed,
                ExitCode: 1,
                Logs: "boom",
                StructuredLogs: [],
                FailureReason: "boom")
        };

        var executor = CreateExecutor(client);

        var result = await executor.ExecuteAsync(
            new SandboxExecutionRequest(
                RunId: "run-126",
                StepId: "step-8",
                AgentName: "sandbox-agent",
                Action: "execute",
                Environment: null,
                PurposeType: "implementation",
                PolicyTag: "issue-126",
                Attempt: 1,
                Profile: new SandboxExecutionProfile(
                    CleanupPolicy: new SandboxCleanupPolicy(
                        DeleteSandboxOnCompletion: true,
                        RetainSandboxOnFailure: true)),
                Command: new SandboxCommandSpec(["sh", "-c", "exit 1"])),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, client.DeleteCallCount);
    }

    private static OpenSandboxSandboxExecutor CreateExecutor(FakeOpenSandboxClient client)
    {
        var options = Options.Create(new SandboxOptions());
        return new OpenSandboxSandboxExecutor(
            client,
            new OpenSandboxRequestMapper(options),
            NullLogger<OpenSandboxSandboxExecutor>.Instance);
    }

    private sealed class FakeOpenSandboxClient : IOpenSandboxClient
    {
        public OpenSandboxSandboxHandle CreateResult { get; set; } = new("sbx-default");

        public OpenSandboxCommandResult CommandResult { get; set; } = new(
            State: SandboxCommandState.Completed,
            ExitCode: 0,
            Logs: string.Empty,
            StructuredLogs: []);

        public IReadOnlyList<OpenSandboxArtifactFile> Artifacts { get; set; } = [];

        public IReadOnlyList<OpenSandboxEndpointResult> Endpoints { get; set; } = [];

        public OpenSandboxDiagnosticsResult DiagnosticsResult { get; set; } = new(
            new Dictionary<string, string>());

        public int DeleteCallCount { get; private set; }

        public Task<OpenSandboxSandboxHandle> CreateAsync(
            OpenSandboxCreateSandboxRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateResult);

        public Task<OpenSandboxCommandResult> RunCommandAsync(
            string sandboxId,
            OpenSandboxRunCommandRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CommandResult);

        public Task<IReadOnlyList<OpenSandboxArtifactFile>> CollectArtifactsAsync(
            string sandboxId,
            OpenSandboxCollectArtifactsRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Artifacts);

        public Task<OpenSandboxEndpointResult> ResolveEndpointAsync(
            string sandboxId,
            OpenSandboxResolveEndpointRequest request,
            CancellationToken cancellationToken)
        {
            var endpoint = Endpoints.FirstOrDefault(static endpoint => endpoint.Port == 8080)
                ?? new OpenSandboxEndpointResult(request.Port, $"http://sandbox.local:{request.Port}", request.Name);
            return Task.FromResult(endpoint);
        }

        public Task<OpenSandboxDiagnosticsResult> GetDiagnosticsAsync(
            string sandboxId,
            CancellationToken cancellationToken) =>
            Task.FromResult(DiagnosticsResult);

        public Task InterruptCommandAsync(
            string sandboxId,
            OpenSandboxInterruptCommandRequest request,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(
            string sandboxId,
            CancellationToken cancellationToken)
        {
            DeleteCallCount++;
            return Task.CompletedTask;
        }
    }
}
