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
        IReadOnlyDictionary<string, string>? createDiagnostics = null;
        var succeeded = false;

        try
        {
            var createRequest = _mapper.MapCreateRequest(request);
            var commandRequest = _mapper.MapRunCommandRequest(request);
            var artifactRequest = _mapper.MapCollectArtifactsRequest(request);

            var sandbox = await _client.CreateAsync(createRequest, cancellationToken);
            sandboxId = sandbox.SandboxId;
            createDiagnostics = sandbox.Metadata;

            var commandResult = await _client.RunCommandAsync(sandboxId, commandRequest, cancellationToken);
            commandState = commandResult.State;

            var artifacts = await _client.CollectArtifactsAsync(sandboxId, artifactRequest, cancellationToken);
            var endpoints = await ResolveEndpointsAsync(sandboxId, request, cancellationToken);

            succeeded = commandResult.State == SandboxCommandState.Completed
                && (commandResult.ExitCode is null or 0);

            var diagnostics = MergeDiagnostics(
                createDiagnostics,
                commandResult.Diagnostics,
                succeeded || !ShouldCaptureDiagnosticsOnFailure(request)
                    ? null
                    : (await _client.GetDiagnosticsAsync(sandboxId, cancellationToken)).Entries);

            return new SandboxExecutionResult(
                Succeeded: succeeded,
                Logs: commandResult.Logs,
                FailureReason: succeeded
                    ? null
                    : commandResult.FailureReason ?? $"OpenSandbox command finished with state {commandResult.State}.",
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var diagnostics = MergeDiagnostics(
                createDiagnostics,
                sandboxId is null || !ShouldCaptureDiagnosticsOnFailure(request)
                    ? null
                    : await TryGetDiagnosticsAsync(sandboxId));

            return new SandboxExecutionResult(
                Succeeded: false,
                Logs: string.Empty,
                FailureReason: "OpenSandbox execution was cancelled.",
                Artifacts: new Dictionary<string, string>(),
                ExitCode: null,
                Duration: DateTimeOffset.UtcNow - started,
                ProviderSandboxId: sandboxId,
                CommandState: SandboxCommandState.Cancelled,
                StructuredLogs: [],
                ProviderDiagnostics: diagnostics,
                Endpoints: [],
                Provider: ProviderKind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenSandbox execution failed for step {StepId}", request.StepId);

            var diagnostics = MergeDiagnostics(
                createDiagnostics,
                BuildExceptionDiagnostics(ex),
                sandboxId is null || !ShouldCaptureDiagnosticsOnFailure(request)
                    ? null
                    : await TryGetDiagnosticsAsync(sandboxId));

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
                ProviderDiagnostics: diagnostics,
                Endpoints: [],
                Provider: ProviderKind);
        }
        finally
        {
            if (sandboxId is not null && ShouldDeleteOnCompletion(request, succeeded))
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

    private static IReadOnlyDictionary<string, string> MergeDiagnostics(
        params IReadOnlyDictionary<string, string>?[] sources)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = SandboxProviderKind.OpenSandbox.ToConfigValue()
        };

        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var (key, value) in source)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> BuildExceptionDiagnostics(Exception exception)
    {
        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exception"] = exception.GetType().Name
        };

        if (exception is OpenSandboxApiException apiException)
        {
            diagnostics["status_code"] = apiException.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (!string.IsNullOrWhiteSpace(apiException.RequestId))
            {
                diagnostics["request_id"] = apiException.RequestId;
            }

            if (!string.IsNullOrWhiteSpace(apiException.ErrorCode))
            {
                diagnostics["error_code"] = apiException.ErrorCode;
            }
        }

        return diagnostics;
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryGetDiagnosticsAsync(string sandboxId)
    {
        try
        {
            return (await _client.GetDiagnosticsAsync(sandboxId, CancellationToken.None)).Entries;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to collect OpenSandbox diagnostics for sandbox {SandboxId}", sandboxId);
            return null;
        }
    }

    private static bool ShouldCaptureDiagnosticsOnFailure(SandboxExecutionRequest request) =>
        request.Profile?.CleanupPolicy?.CaptureDiagnosticsOnFailure ?? true;

    private static bool ShouldDeleteOnCompletion(SandboxExecutionRequest request, bool succeeded)
    {
        var cleanupPolicy = request.Profile?.CleanupPolicy;
        if (!succeeded && cleanupPolicy?.RetainSandboxOnFailure == true)
        {
            return false;
        }

        return cleanupPolicy?.DeleteSandboxOnCompletion ?? true;
    }
}
