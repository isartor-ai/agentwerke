using Autofac.Sandboxes;

namespace Autofac.Sandboxes.Tests;

public sealed class SandboxContractsTests
{
    [Fact]
    public void SandboxOptions_Defaults_AreConservative()
    {
        var opts = new SandboxOptions();

        Assert.False(opts.Enabled);
        Assert.False(opts.IsEnabled);
        Assert.Equal(SandboxProviderNames.Docker, opts.Provider);
        Assert.Equal("alpine:3.19", opts.DefaultImage);
        Assert.Equal("alpine:3.19", opts.Docker.DefaultImage);
        Assert.Equal(60, opts.TimeoutSeconds);
        Assert.Equal(256, opts.MemoryLimitMb);
        Assert.Equal(50_000L, opts.CpuQuota);
        Assert.Equal("unix:///var/run/docker.sock", opts.DockerEndpoint);
        Assert.Equal("http://localhost:8080/v1", opts.OpenSandbox.BaseUrl);
        Assert.Equal("kata", opts.KubernetesKata.RuntimeClassName);
    }

    [Fact]
    public void SandboxOptions_LegacyDockerEnableFlag_StillTurnsExecutionOn()
    {
        var opts = new SandboxOptions
        {
            Docker = new DockerSandboxProviderOptions
            {
                Enabled = true
            }
        };

        Assert.True(opts.IsEnabled);
    }

    [Fact]
    public void SandboxExecutionRequest_StoresAllFields()
    {
        var req = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "deploy-agent",
            Action: "deploy",
            Environment: "prod",
            PurposeType: "deployment",
            PolicyTag: "deploy-prod",
            Attempt: 2,
            Image: "my-image:latest",
            Profile: new SandboxExecutionProfile(
                Resources: new SandboxResourceLimits(CpuMilliCores: 750, MemoryMb: 512, TimeoutSeconds: 120),
                NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, ["github.com"]),
                FilesystemMounts:
                [
                    new SandboxFilesystemMount(SandboxFilesystemMountSourceKind.HostPath, "/tmp/input", "/workspace/input")
                ],
                CredentialBindings:
                [
                    new SandboxCredentialBinding("github-token", "GITHUB_TOKEN", SandboxCredentialBindingMode.EnvironmentVariable)
                ],
                CleanupPolicy: new SandboxCleanupPolicy(DeleteSandboxOnCompletion: false),
                CommandExecutionMode: SandboxCommandExecutionMode.Session),
            Command: new SandboxCommandSpec(["python", "main.py"], WorkingDirectory: "/workspace"),
            Metadata: new Dictionary<string, string> { ["ticket"] = "125" },
            EndpointRequests:
            [
                new SandboxEndpointRequest(8080, "http", SecureAccess: true)
            ],
            EnvironmentVariables: new Dictionary<string, string> { ["TRACE"] = "true" },
            ArtifactPaths: ["/output", "/reports"]);

        Assert.Equal("run-1", req.RunId);
        Assert.Equal("step-1", req.StepId);
        Assert.Equal("deploy-agent", req.AgentName);
        Assert.Equal("deploy", req.Action);
        Assert.Equal("prod", req.Environment);
        Assert.Equal("deployment", req.PurposeType);
        Assert.Equal("deploy-prod", req.PolicyTag);
        Assert.Equal(2, req.Attempt);
        Assert.Equal("my-image:latest", req.Image);
        Assert.Equal(SandboxCommandExecutionMode.Session, req.Profile?.CommandExecutionMode);
        Assert.Equal("/workspace", req.Command?.WorkingDirectory);
        Assert.Equal("125", req.Metadata?["ticket"]);
        Assert.Equal(8080, Assert.Single(req.EndpointRequests!).Port);
        Assert.Equal("true", req.EnvironmentVariables?["TRACE"]);
        Assert.Equal("/reports", req.ArtifactPaths!.Last());
    }

    [Fact]
    public void SandboxExecutionRequest_ImageIsOptional()
    {
        var req = new SandboxExecutionRequest(
            RunId: "r", StepId: "s", AgentName: "a",
            Action: "x", Environment: null, PurposeType: "p",
            PolicyTag: "q", Attempt: 1);

        Assert.Null(req.Image);
    }

    [Fact]
    public void SandboxExecutionResult_SuccessPath()
    {
        var artifacts = new Dictionary<string, string> { ["result.json"] = "{}" };
        var result = new SandboxExecutionResult(
            Succeeded: true,
            Logs: "task complete",
            FailureReason: null,
            Artifacts: artifacts,
            ExitCode: 0,
            Duration: TimeSpan.FromSeconds(3),
            ProviderSandboxId: "sandbox-1",
            CommandState: SandboxCommandState.Completed,
            StructuredLogs:
            [
                new SandboxLogEntry("stdout", "task complete", DateTimeOffset.UtcNow)
            ],
            ProviderDiagnostics: new Dictionary<string, string> { ["provider"] = "docker" },
            Endpoints:
            [
                new SandboxEndpointMetadata(8080, "http://sandbox.local:8080", "http")
            ],
            Provider: SandboxProviderKind.Docker);

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Artifacts);
        Assert.Equal("{}", result.Artifacts["result.json"]);
        Assert.Equal("sandbox-1", result.ProviderSandboxId);
        Assert.Equal(SandboxCommandState.Completed, result.CommandState);
        Assert.Single(result.StructuredLogs!);
        Assert.Single(result.Endpoints!);
    }

    [Fact]
    public void SandboxExecutionResult_FailurePath_HasReason()
    {
        var result = new SandboxExecutionResult(
            Succeeded: false,
            Logs: string.Empty,
            FailureReason: "Container exited with code 1",
            Artifacts: new Dictionary<string, string>(),
            ExitCode: 1,
            Duration: TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.Equal("Container exited with code 1", result.FailureReason);
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public void SandboxExecutionResult_TimeoutPath_HasNullExitCode()
    {
        var result = new SandboxExecutionResult(
            Succeeded: false,
            Logs: "partial logs...",
            FailureReason: "Timed out after 60s",
            Artifacts: new Dictionary<string, string>(),
            ExitCode: null,
            Duration: TimeSpan.FromSeconds(60));

        Assert.False(result.Succeeded);
        Assert.Null(result.ExitCode);
        Assert.Contains("60s", result.FailureReason);
    }
}
