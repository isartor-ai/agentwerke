using Microsoft.Extensions.Logging;

namespace Autofac.Sandboxes;

public sealed class OpenSandboxSandboxExecutor : ISandboxProviderExecutor
{
    private readonly IOpenSandboxClient _client;
    private readonly OpenSandboxRequestMapper _mapper;
    private readonly ILogger<OpenSandboxSandboxExecutor> _logger;

    public OpenSandboxSandboxExecutor(
        IOpenSandboxClient client,
        OpenSandboxRequestMapper mapper,
        ILogger<OpenSandboxSandboxExecutor> logger)
    {
        _client = client;
        _mapper = mapper;
        _logger = logger;
    }

    public SandboxProviderKind ProviderKind => SandboxProviderKind.OpenSandbox;

    public async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        string? sandboxId = null;
        SandboxCommandState commandState = SandboxCommandState.Unknown;

        try
        {
            var createRequest = _mapper.MapCreateRequest(request);
            var commandRequest = _mapper.MapRunCommandRequest(request);
            var artifactRequest = _mapper.MapCollectArtifactsRequest(request);

            var sandbox = await _client.CreateAsync(createRequest, cancellationToken);
            sandboxId = sandbox.SandboxId;

            var commandResult = await _client.RunCommandAsync(sandboxId, commandRequest, cancellationToken);
            commandState = commandResult.State;

            var artifacts = await _client.CollectArtifactsAsync(sandboxId, artifactRequest, cancellationToken);
            var endpoints = await ResolveEndpointsAsync(sandboxId, request, cancellationToken);

            var succeeded = commandResult.State == SandboxCommandState.Completed
                && (commandResult.ExitCode is null or 0);

            var diagnostics = succeeded
                ? new Dictionary<string, string>()
                : (await _client.GetDiagnosticsAsync(sandboxId, cancellationToken)).Entries;

            return new SandboxExecutionResult(
                Succeeded: succeeded,
                Logs: commandResult.Logs,
                FailureReason: succeeded ? null : $"OpenSandbox command finished with state {commandResult.State}.",
                Artifacts: artifacts.ToDictionary(static item => item.Path, static item => item.Content),
                ExitCode: commandResult.ExitCode,
                Duration: DateTimeOffset.UtcNow - started,
                ProviderSandboxId: sandboxId,
                CommandState: commandResult.State,
                StructuredLogs: commandResult.StructuredLogs,
                ProviderDiagnostics: diagnostics,
                Endpoints: endpoints,
                Provider: ProviderKind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSandbox execution failed for step {StepId}", request.StepId);

            return new SandboxExecutionResult(
                Succeeded: false,
                Logs: string.Empty,
                FailureReason: ex.Message,
                Artifacts: new Dictionary<string, string>(),
                ExitCode: null,
                Duration: DateTimeOffset.UtcNow - started,
                ProviderSandboxId: sandboxId,
                CommandState: commandState == SandboxCommandState.Unknown ? SandboxCommandState.Failed : commandState,
                StructuredLogs: [],
                ProviderDiagnostics: new Dictionary<string, string>
                {
                    ["exception"] = ex.GetType().Name,
                    ["provider"] = ProviderKind.ToConfigValue()
                },
                Endpoints: [],
                Provider: ProviderKind);
        }
        finally
        {
            if (sandboxId is not null && ShouldDeleteOnCompletion(request))
            {
                try
                {
                    await _client.DeleteAsync(sandboxId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete OpenSandbox sandbox {SandboxId}", sandboxId);
                }
            }
        }
    }

    private async Task<IReadOnlyList<SandboxEndpointMetadata>> ResolveEndpointsAsync(
        string sandboxId,
        SandboxExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.EndpointRequests is not { Count: > 0 })
        {
            return [];
        }

        var endpoints = new List<SandboxEndpointMetadata>(request.EndpointRequests.Count);
        foreach (var endpointRequest in request.EndpointRequests)
        {
            var endpoint = await _client.ResolveEndpointAsync(
                sandboxId,
                new OpenSandboxResolveEndpointRequest(endpointRequest.Port, endpointRequest.Name, endpointRequest.SecureAccess),
                cancellationToken);

            endpoints.Add(new SandboxEndpointMetadata(
                Port: endpoint.Port,
                Uri: endpoint.Uri,
                Name: endpoint.Name,
                Headers: endpoint.Headers));
        }

        return endpoints;
    }

    private static bool ShouldDeleteOnCompletion(SandboxExecutionRequest request) =>
        request.Profile?.CleanupPolicy?.DeleteSandboxOnCompletion ?? true;
}
