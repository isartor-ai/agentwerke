namespace Autofac.Sandboxes;

public interface ISandboxExecutor
{
    Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken);
}
