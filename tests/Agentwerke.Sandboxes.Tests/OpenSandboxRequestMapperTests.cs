using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes.Tests;

public sealed class OpenSandboxRequestMapperTests
{
    [Fact]
    public void MapCreateRequest_MapsProfileToProviderNeutralOpenSandboxShape()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions
        {
            OpenSandbox = new OpenSandboxProviderOptions
            {
                DefaultImage = "opensandbox:agent",
                DefaultTimeoutSeconds = 45,
                DefaultCpuMilliCores = 500,
                DefaultMemoryLimitMb = 256,
                WorkingDirectory = "/workspace"
            }
        }));

        var request = new SandboxExecutionRequest(
            RunId: "run-125",
            StepId: "step-2",
            AgentName: "implementation-engineer",
            Action: "sandbox.execute",
            Environment: "staging",
            PurposeType: "implementation",
            PolicyTag: "issue-125",
            Attempt: 3,
            Image: null,
            Profile: new SandboxExecutionProfile(
                Resources: new SandboxResourceLimits(CpuMilliCores: 750, MemoryMb: 1024, TimeoutSeconds: 120, GpuCount: 1),
                NetworkPolicy: new SandboxNetworkPolicy(SandboxNetworkAccessMode.Restricted, ["api.github.com", "github.com"]),
                FilesystemMounts:
                [
                    new SandboxFilesystemMount(SandboxFilesystemMountSourceKind.HostPath, "/tmp/work", "/workspace", ReadOnly: false)
                ],
                CredentialBindings:
                [
                    new SandboxCredentialBinding("github-token", "GITHUB_TOKEN", SandboxCredentialBindingMode.EnvironmentVariable)
                ],
                CleanupPolicy: new SandboxCleanupPolicy(DeleteSandboxOnCompletion: false),
                CommandExecutionMode: SandboxCommandExecutionMode.Session),
            Command: new SandboxCommandSpec(
                Arguments: ["python", "main.py"],
                WorkingDirectory: "/repo",
                EnvironmentVariables: new Dictionary<string, string> { ["PYTHONUNBUFFERED"] = "1" }),
            Metadata: new Dictionary<string, string> { ["ticket"] = "125" },
            EndpointRequests:
            [
                new SandboxEndpointRequest(8080, "http", SecureAccess: true)
            ],
            EnvironmentVariables: new Dictionary<string, string> { ["TRACE"] = "true" },
            ArtifactPaths: ["/output", "/reports"]);

        var mapped = mapper.MapCreateRequest(request);

        Assert.Equal("opensandbox:agent", mapped.Image);
        Assert.Equal(120, mapped.TimeoutSeconds);
        Assert.Equal(750, mapped.ResourceLimits.CpuMilliCores);
        Assert.Equal(1024, mapped.ResourceLimits.MemoryMb);
        Assert.Equal(1, mapped.ResourceLimits.GpuCount);
        Assert.Equal("/repo", mapped.WorkingDirectory);
        Assert.Equal(SandboxCommandExecutionMode.Session, mapped.CommandExecutionMode);
        Assert.Equal("run-125", mapped.Metadata["agentwerke.run"]);
        Assert.Equal("125", mapped.Metadata["ticket"]);
        Assert.Equal("true", mapped.EnvironmentVariables["TRACE"]);
        Assert.Equal("staging", mapped.EnvironmentVariables["AGENTWERKE_ENVIRONMENT"]);
        Assert.Equal(SandboxNetworkAccessMode.Restricted, mapped.NetworkPolicy?.Mode);
        Assert.Equal("/workspace", Assert.Single(mapped.Volumes).MountPath);
        Assert.Equal("github-token", Assert.Single(mapped.CredentialBindings).Name);
        Assert.Equal(8080, Assert.Single(mapped.RequestedEndpoints).Port);
    }

    [Fact]
    public void MapRunCommandRequest_UsesExplicitCommandAndExecutionMode()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions()));
        var request = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "agent",
            Action: "build",
            Environment: null,
            PurposeType: "implementation",
            PolicyTag: "policy",
            Attempt: 1,
            Profile: new SandboxExecutionProfile(CommandExecutionMode: SandboxCommandExecutionMode.Background),
            Command: new SandboxCommandSpec(
                Arguments: ["bash", "script.sh"],
                WorkingDirectory: "/repo",
                EnvironmentVariables: new Dictionary<string, string> { ["DEBUG"] = "1" },
                StandardInput: "stdin",
                StreamOutput: false));

        var mapped = mapper.MapRunCommandRequest(request);

        Assert.Equal(["bash", "script.sh"], mapped.Arguments);
        Assert.Equal(SandboxCommandExecutionMode.Background, mapped.Mode);
        Assert.Equal("/repo", mapped.WorkingDirectory);
        Assert.Equal("1", mapped.EnvironmentVariables["DEBUG"]);
        Assert.Equal("stdin", mapped.StandardInput);
        Assert.False(mapped.StreamOutput);
    }

    [Fact]
    public void MapCreateRequest_SanitizesMetadataValuesForOpenSandboxLabels()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions()));
        var request = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "opensandbox-e2e-688c0bf7d7b748de84497646bf317f20",
            Action: "run-open-sandbox",
            Environment: "ci",
            PurposeType: "verification",
            PolicyTag: "opensandbox-e2e",
            Attempt: 1,
            Metadata: new Dictionary<string, string>
            {
                ["agentwerke.sandboxProfileRationale"] =
                    "Sandbox profile 'offline' is authorized for agent 'opensandbox-e2e-688c0bf7d7b748de84497646bf317f20'."
            });

        var mapped = mapper.MapCreateRequest(request);
        var value = mapped.Metadata["agentwerke.sandboxProfileRationale"];

        Assert.True(value.Length <= 63);
        Assert.All(value, ch => Assert.True(
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.',
            $"Unexpected metadata character '{ch}' in '{value}'."));
        Assert.True(char.IsAsciiLetterOrDigit(value[0]));
        Assert.True(char.IsAsciiLetterOrDigit(value[^1]));
        Assert.DoesNotContain(" ", value, StringComparison.Ordinal);
        Assert.DoesNotContain("'", value, StringComparison.Ordinal);
    }

    [Fact]
    public void MapRunCommandRequest_WithoutExplicitCommand_UsesPlaceholderScript()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions()));
        var request = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "agent",
            Action: "deploy",
            Environment: "dev",
            PurposeType: "implementation",
            PolicyTag: "policy",
            Attempt: 1);

        var mapped = mapper.MapRunCommandRequest(request);

        Assert.Equal("sh", mapped.Arguments[0]);
        Assert.Equal("-c", mapped.Arguments[1]);
        Assert.Equal("/", mapped.WorkingDirectory);
        Assert.Contains("agentwerke-sandbox: starting task", mapped.Arguments[2]);
        Assert.Equal("deploy", mapped.EnvironmentVariables["AGENTWERKE_ACTION"]);
    }

    [Fact]
    public void MapCollectArtifactsRequest_UsesExplicitArtifactPathsOrProviderDefaults()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions
        {
            OpenSandbox = new OpenSandboxProviderOptions
            {
                DefaultArtifactPaths = ["/output", "/logs"]
            }
        }));

        var explicitRequest = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "agent",
            Action: "test",
            Environment: null,
            PurposeType: "implementation",
            PolicyTag: "policy",
            Attempt: 1,
            ArtifactPaths: ["/reports"]);

        var implicitRequest = explicitRequest with { ArtifactPaths = null };

        Assert.Equal(["/reports"], mapper.MapCollectArtifactsRequest(explicitRequest).Paths);
        Assert.Equal(["/output", "/logs"], mapper.MapCollectArtifactsRequest(implicitRequest).Paths);
    }

    [Fact]
    public void MapRunCommandRequest_UsesOpenSandboxDefaultProfileWhenRequestProfileMissing()
    {
        var mapper = new OpenSandboxRequestMapper(Options.Create(new SandboxOptions
        {
            OpenSandbox = new OpenSandboxProviderOptions
            {
                WorkingDirectory = "/workspace",
                DefaultTimeoutSeconds = 60,
                DefaultProfile = new SandboxExecutionProfile(
                    Resources: new SandboxResourceLimits(TimeoutSeconds: 90),
                    CommandExecutionMode: SandboxCommandExecutionMode.Session)
            }
        }));

        var request = new SandboxExecutionRequest(
            RunId: "run-1",
            StepId: "step-1",
            AgentName: "agent",
            Action: "test",
            Environment: null,
            PurposeType: "implementation",
            PolicyTag: "policy",
            Attempt: 1,
            Command: new SandboxCommandSpec(["python", "main.py"]));

        var mapped = mapper.MapRunCommandRequest(request);

        Assert.Equal(SandboxCommandExecutionMode.Session, mapped.Mode);
        Assert.Equal(90, mapped.TimeoutSeconds);
        Assert.Equal("/workspace", mapped.WorkingDirectory);
    }
}
