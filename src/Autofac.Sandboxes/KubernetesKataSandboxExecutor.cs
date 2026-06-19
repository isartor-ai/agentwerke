namespace Autofac.Sandboxes;

public sealed class KubernetesKataSandboxExecutor : ISandboxProviderExecutor
{
    public SandboxProviderKind ProviderKind => SandboxProviderKind.KubernetesKata;

    public Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var result = new SandboxExecutionResult(
            Succeeded: false,
            Logs: string.Empty,
            FailureReason: "The reserved 'kubernetes-kata' sandbox provider is not implemented yet. Use 'docker' or 'opensandbox'.",
            Artifacts: new Dictionary<string, string>(),
            ExitCode: null,
            Duration: TimeSpan.Zero,
            ProviderSandboxId: null,
            CommandState: SandboxCommandState.Unknown,
            StructuredLogs: [],
            ProviderDiagnostics: new Dictionary<string, string>
            {
                ["provider"] = ProviderKind.ToConfigValue(),
                ["status"] = "reserved"
            },
            Endpoints: [],
            Provider: ProviderKind);

        return Task.FromResult(result);
    }
}
