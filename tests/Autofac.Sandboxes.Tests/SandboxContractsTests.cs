using Autofac.Sandboxes;

namespace Autofac.Sandboxes.Tests;

public sealed class SandboxContractsTests
{
    [Fact]
    public void SandboxOptions_Defaults_AreConservative()
    {
        var opts = new SandboxOptions();

        Assert.False(opts.Enabled);
        Assert.Equal("alpine:3.19", opts.DefaultImage);
        Assert.Equal(60, opts.TimeoutSeconds);
        Assert.Equal(256, opts.MemoryLimitMb);
        Assert.Equal(50_000L, opts.CpuQuota);
        Assert.Equal("unix:///var/run/docker.sock", opts.DockerEndpoint);
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
            Image: "my-image:latest");

        Assert.Equal("run-1", req.RunId);
        Assert.Equal("step-1", req.StepId);
        Assert.Equal("deploy-agent", req.AgentName);
        Assert.Equal("deploy", req.Action);
        Assert.Equal("prod", req.Environment);
        Assert.Equal("deployment", req.PurposeType);
        Assert.Equal("deploy-prod", req.PolicyTag);
        Assert.Equal(2, req.Attempt);
        Assert.Equal("my-image:latest", req.Image);
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
            Duration: TimeSpan.FromSeconds(3));

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Artifacts);
        Assert.Equal("{}", result.Artifacts["result.json"]);
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
