namespace Agentwerke.Sandboxes;

public interface ISandboxExecutor
{
    Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken,
        SandboxLogReporter? logReporter = null);
}

public interface ISandboxProviderExecutor : ISandboxExecutor
{
    SandboxProviderKind ProviderKind { get; }
}
