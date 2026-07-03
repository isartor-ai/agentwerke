using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agentwerke.Sandboxes;

public sealed class OpenSandboxApiClient : IOpenSandboxClient
{
    private const string LifecycleApiKeyHeader = "OPEN-SANDBOX-API-KEY";
    private const string ExecdApiKeyHeader = "X-EXECD-ACCESS-TOKEN";
    private const string RequestIdHeader = "X-Request-ID";
    private const string BackgroundLogCursorHeader = "EXECD-COMMANDS-TAIL-CURSOR";
    private const int ExecdPort = 44_772;
    private static readonly IReadOnlyList<string> DefaultEntrypoint = ["tail", "-f", "/dev/null"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OpenSandboxProviderOptions _options;
    private readonly Uri _lifecycleBaseUri;
    private readonly Dictionary<string, Dictionary<string, string>> _sandboxDiagnostics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResolvedExecdEndpoint> _execdEndpoints = new(StringComparer.Ordinal);

    public OpenSandboxApiClient(
        HttpClient httpClient,
        IOptions<SandboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options.Value.OpenSandbox;
        _lifecycleBaseUri = NormalizeLifecycleBaseUri(_options.ServerUrl);
    }

    public async Task<OpenSandboxSandboxHandle> CreateAsync(
        OpenSandboxCreateSandboxRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new LifecycleCreateSandboxRequestDto
        {
            Image = new LifecycleImageSpecDto(request.Image),
            Timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : null,
            ResourceLimits = BuildResourceLimits(request.ResourceLimits),
            Env = request.EnvironmentVariables.Count > 0
                ? new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
                : null,
            Metadata = request.Metadata.Count > 0
                ? new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
                : null,
            Entrypoint = DefaultEntrypoint,
            NetworkPolicy = MapNetworkPolicy(request.NetworkPolicy),
            SecureAccess = request.SecureAccess,
            Volumes = MapVolumes(request.Volumes)
        };

        var response = await SendLifecycleAsync<LifecycleCreateSandboxResponseDto>(
            HttpMethod.Post,
            "sandboxes",
            payload,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Body?.Id))
        {
            throw new InvalidOperationException("OpenSandbox create response did not include a sandbox id.");
        }

        var sandboxId = response.Body.Id;
        RecordRequestId(sandboxId, "lifecycle.create.request_id", response.RequestId);

        await WaitForSandboxRunningAsync(sandboxId, cancellationToken);
        await EnsureExecdReadyAsync(sandboxId, cancellationToken);

        return new OpenSandboxSandboxHandle(sandboxId, SnapshotDiagnostics(sandboxId));
    }

    public async Task<OpenSandboxCommandResult> RunCommandAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ArgumentNullException.ThrowIfNull(request);

        await EnsureExecdReadyAsync(sandboxId, cancellationToken);

        using var readinessCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ReadinessTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readinessCts.Token);

        while (true)
        {
            try
            {
                return request.Mode switch
                {
                    SandboxCommandExecutionMode.Background => await RunBackgroundCommandAsync(sandboxId, request, linkedCts.Token),
                    SandboxCommandExecutionMode.Session => await RunInSessionAsync(sandboxId, request, linkedCts.Token),
                    _ => await RunForegroundCommandAsync(sandboxId, request, linkedCts.Token)
                };
            }
            catch (OpenSandboxApiException ex) when (IsTransientApiFailure(ex))
            {
                RecordDiagnostic(sandboxId, "execd.last_error", ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(250), linkedCts.Token);
            }
        }
    }

    public async Task<IReadOnlyList<OpenSandboxArtifactFile>> CollectArtifactsAsync(
        string sandboxId,
        OpenSandboxCollectArtifactsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Paths.Count == 0)
        {
            return [];
        }

        await EnsureExecdReadyAsync(sandboxId, cancellationToken);

        var artifacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in request.Paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                await CollectPathArtifactsAsync(sandboxId, path, artifacts, cancellationToken);
            }
            catch (OpenSandboxApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
            {
                RecordDiagnostic(sandboxId, $"artifacts.missing.{NormalizeArtifactKey(path)}", "true");
            }
        }

        return artifacts
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new OpenSandboxArtifactFile(pair.Key, pair.Value))
            .ToArray();
    }

    public async Task<OpenSandboxEndpointResult> ResolveEndpointAsync(
        string sandboxId,
        OpenSandboxResolveEndpointRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = await ResolveSandboxEndpointAsync(sandboxId, request.Port, cancellationToken);
        return new OpenSandboxEndpointResult(
            request.Port,
            endpoint.Endpoint,
            request.Name,
            endpoint.Headers);
    }

    public async Task<OpenSandboxDiagnosticsResult> GetDiagnosticsAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);

        var diagnostics = SnapshotDiagnostics(sandboxId);

        try
        {
            var response = await SendLifecycleAsync<LifecycleSandboxDto>(
                HttpMethod.Get,
                $"sandboxes/{Uri.EscapeDataString(sandboxId)}",
                body: null,
                cancellationToken);

            RecordRequestId(sandboxId, "lifecycle.inspect.request_id", response.RequestId);

            if (response.Body?.Status is not null)
            {
                diagnostics["sandbox.state"] = response.Body.Status.State ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(response.Body.Status.Reason))
                {
                    diagnostics["sandbox.reason"] = response.Body.Status.Reason;
                }

                if (!string.IsNullOrWhiteSpace(response.Body.Status.Message))
                {
                    diagnostics["sandbox.message"] = response.Body.Status.Message;
                }
            }
        }
        catch (OpenSandboxApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            diagnostics["sandbox.state"] = "NotFound";
        }

        return new OpenSandboxDiagnosticsResult(diagnostics);
    }

    public async Task InterruptCommandAsync(
        string sandboxId,
        OpenSandboxInterruptCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CommandId);

        await EnsureExecdReadyAsync(sandboxId, cancellationToken);

        var query = $"command?id={Uri.EscapeDataString(request.CommandId)}";
        var response = await SendExecdAsync<object>(
            sandboxId,
            HttpMethod.Delete,
            query,
            body: null,
            accept: "application/json",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.interrupt.request_id", response.RequestId);
    }

    public async Task DeleteAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxId);

        var response = await SendLifecycleAsync<object>(
            HttpMethod.Delete,
            $"sandboxes/{Uri.EscapeDataString(sandboxId)}",
            body: null,
            cancellationToken);

        RecordRequestId(sandboxId, "lifecycle.delete.request_id", response.RequestId);
        _execdEndpoints.Remove(sandboxId);
    }

    private async Task<OpenSandboxCommandResult> RunForegroundCommandAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken)
    {
        var response = await StreamExecdAsync(
            sandboxId,
            "command",
            new ExecdRunCommandRequestDto
            {
                Command = BuildShellCommand(request.Arguments, request.StandardInput),
                Cwd = request.WorkingDirectory,
                Background = false,
                Timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds * 1000L : null,
                Envs = request.EnvironmentVariables.Count > 0
                    ? new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
                    : null
            },
            "execd.run.request_id",
            cancellationToken);

        return ToForegroundCommandResult(sandboxId, response, sessionId: null, request.TimeoutSeconds);
    }

    private async Task<OpenSandboxCommandResult> RunBackgroundCommandAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken)
    {
        var response = await StreamExecdAsync(
            sandboxId,
            "command",
            new ExecdRunCommandRequestDto
            {
                Command = BuildShellCommand(request.Arguments, request.StandardInput),
                Cwd = request.WorkingDirectory,
                Background = true,
                Timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds * 1000L : null,
                Envs = request.EnvironmentVariables.Count > 0
                    ? new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.OrdinalIgnoreCase)
                    : null
            },
            "execd.run.request_id",
            cancellationToken);

        var commandId = response.ExecutionId;
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return ToForegroundCommandResult(sandboxId, response, sessionId: null, request.TimeoutSeconds);
        }

        var structuredLogs = new List<SandboxLogEntry>(response.StructuredLogs);
        var combinedLogs = new StringBuilder(response.Logs);
        long? cursor = null;

        while (true)
        {
            var status = await GetCommandStatusAsync(sandboxId, commandId, cancellationToken);
            var logs = await GetBackgroundLogsAsync(sandboxId, commandId, cursor, cancellationToken);

            if (!string.IsNullOrEmpty(logs.Content))
            {
                if (combinedLogs.Length > 0 && !combinedLogs.ToString().EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    combinedLogs.AppendLine();
                }

                combinedLogs.Append(logs.Content);
                structuredLogs.Add(new SandboxLogEntry("stdout", logs.Content, DateTimeOffset.UtcNow));
            }

            cursor = logs.Cursor;

            if (!status.Running)
            {
                var state = DetermineBackgroundState(status.Error, status.ExitCode);
                var failureReason = BuildFailureReason(
                    state,
                    status.Error,
                    request.TimeoutSeconds);

                return new OpenSandboxCommandResult(
                    State: state,
                    ExitCode: status.ExitCode,
                    Logs: combinedLogs.ToString(),
                    StructuredLogs: structuredLogs,
                    ExecutionId: commandId,
                    SessionId: null,
                    FailureReason: failureReason,
                    Diagnostics: SnapshotDiagnostics(sandboxId));
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private async Task<OpenSandboxCommandResult> RunInSessionAsync(
        string sandboxId,
        OpenSandboxRunCommandRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = await CreateSessionAsync(sandboxId, request.WorkingDirectory, cancellationToken);

        try
        {
            var response = await StreamExecdAsync(
                sandboxId,
                $"session/{Uri.EscapeDataString(sessionId)}/run",
                new ExecdRunInSessionRequestDto
                {
                    Command = BuildShellCommand(request.Arguments, request.StandardInput),
                    Cwd = request.WorkingDirectory,
                    Timeout = request.TimeoutSeconds > 0 ? request.TimeoutSeconds * 1000L : null
                },
                "execd.run.request_id",
                cancellationToken);

            return ToForegroundCommandResult(sandboxId, response, sessionId, request.TimeoutSeconds);
        }
        finally
        {
            try
            {
                var response = await SendExecdAsync<object>(
                    sandboxId,
                    HttpMethod.Delete,
                    $"session/{Uri.EscapeDataString(sessionId)}",
                    body: null,
                    accept: "application/json",
                    CancellationToken.None);

                RecordRequestId(sandboxId, "execd.delete_session.request_id", response.RequestId);
            }
            catch (Exception)
            {
                // Session cleanup is best-effort; sandbox deletion remains the final safety net.
            }
        }
    }

    private OpenSandboxCommandResult ToForegroundCommandResult(
        string sandboxId,
        ExecdStreamResult response,
        string? sessionId,
        int? timeoutSeconds)
    {
        var state = DetermineForegroundState(response);
        var exitCode = response.ExitCode ?? (state == SandboxCommandState.Completed ? 0 : null);
        var failureReason = BuildFailureReason(
            state,
            response.ErrorMessage,
            timeoutSeconds);

        return new OpenSandboxCommandResult(
            State: state,
            ExitCode: exitCode,
            Logs: response.Logs,
            StructuredLogs: response.StructuredLogs,
            ExecutionId: response.ExecutionId,
            SessionId: sessionId,
            FailureReason: failureReason,
            Diagnostics: SnapshotDiagnostics(sandboxId));
    }

    private async Task CollectPathArtifactsAsync(
        string sandboxId,
        string requestedPath,
        IDictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        // The deployed execd (opensandbox/execd:v1.0.18) does not return a "type"
        // field on files/info or files/search entries, so directory vs. file can't
        // be distinguished from response shape alone. Try files/search first —
        // if requestedPath is a directory, this enumerates its files; if it's a
        // file, search legitimately finds nothing and the path is downloaded
        // directly instead. (An earlier version of this method branched on
        // info.Type, which was always empty against this execd build and made
        // every requested path — including plain directories like the default
        // "/output" — fall through to downloading the directory itself as if it
        // were a file, which execd does not support.)
        var searchResponse = await SendExecdAsync<List<ExecdFileInfoDto>>(
            sandboxId,
            HttpMethod.Get,
            $"files/search?path={Uri.EscapeDataString(requestedPath)}&pattern={Uri.EscapeDataString("**")}",
            body: null,
            accept: "application/json",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.files.search.request_id", searchResponse.RequestId);

        if (searchResponse.Body is { Count: > 0 } files)
        {
            foreach (var file in files)
            {
                await DownloadArtifactAsync(sandboxId, requestedPath, file.Path, artifacts, cancellationToken);
            }

            return;
        }

        await DownloadArtifactAsync(sandboxId, requestedPath, requestedPath, artifacts, cancellationToken);
    }

    private async Task DownloadArtifactAsync(
        string sandboxId,
        string requestedPath,
        string filePath,
        IDictionary<string, string> artifacts,
        CancellationToken cancellationToken)
    {
        var response = await SendExecdForBytesAsync(
            sandboxId,
            HttpMethod.Get,
            $"files/download?path={Uri.EscapeDataString(filePath)}",
            accept: "application/octet-stream",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.files.download.request_id", response.RequestId);

        var content = TryDecodeArtifactContent(response.Content);
        if (content is null)
        {
            return;
        }

        var key = BuildArtifactKey(requestedPath, filePath);
        if (artifacts.ContainsKey(key))
        {
            key = NormalizeSandboxPath(filePath).TrimStart('/');
        }

        artifacts[key] = content;
    }

    private async Task WaitForSandboxRunningAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        using var readinessCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ReadinessTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readinessCts.Token);

        try
        {
            while (true)
            {
                try
                {
                    var response = await SendLifecycleAsync<LifecycleSandboxDto>(
                        HttpMethod.Get,
                        $"sandboxes/{Uri.EscapeDataString(sandboxId)}",
                        body: null,
                        linkedCts.Token);

                    RecordRequestId(sandboxId, "lifecycle.inspect.request_id", response.RequestId);

                    var state = response.Body?.Status?.State;
                    if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(state, "Terminated", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"OpenSandbox sandbox {sandboxId} failed to start: {response.Body?.Status?.Message ?? response.Body?.Status?.Reason ?? state}.");
                    }
                }
                catch (OpenSandboxApiException ex) when (
                    ex.StatusCode == (int)HttpStatusCode.NotFound ||
                    ex.StatusCode == (int)HttpStatusCode.Conflict ||
                    ex.StatusCode == (int)HttpStatusCode.BadGateway ||
                    ex.StatusCode == (int)HttpStatusCode.ServiceUnavailable)
                {
                    RecordDiagnostic(sandboxId, "lifecycle.inspect.last_error", ex.Message);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (readinessCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"OpenSandbox sandbox {sandboxId} was not ready within {_options.ReadinessTimeoutSeconds}s.");
        }
    }

    private async Task EnsureExecdReadyAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        using var readinessCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ReadinessTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readinessCts.Token);

        try
        {
            while (true)
            {
                try
                {
                    var endpoint = await ResolveExecdEndpointAsync(sandboxId, linkedCts.Token);
                    var response = await SendExecdAsync<object>(
                        sandboxId,
                        HttpMethod.Get,
                        "ping",
                        body: null,
                        accept: "application/json",
                        linkedCts.Token,
                        endpoint);

                    RecordRequestId(sandboxId, "execd.ping.request_id", response.RequestId);
                    return;
                }
                catch (OpenSandboxApiException ex) when (
                    ex.StatusCode == (int)HttpStatusCode.NotFound ||
                    ex.StatusCode == (int)HttpStatusCode.Conflict ||
                    ex.StatusCode == (int)HttpStatusCode.BadGateway ||
                    ex.StatusCode == (int)HttpStatusCode.ServiceUnavailable)
                {
                    RecordDiagnostic(sandboxId, "execd.last_error", ex.Message);
                }
                catch (HttpRequestException ex)
                {
                    RecordDiagnostic(sandboxId, "execd.last_error", ex.Message);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), linkedCts.Token);
            }
        }
        catch (OperationCanceledException) when (readinessCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"OpenSandbox execd endpoint for sandbox {sandboxId} was not ready within {_options.ReadinessTimeoutSeconds}s.");
        }
    }

    private async Task<ResolvedExecdEndpoint> ResolveExecdEndpointAsync(
        string sandboxId,
        CancellationToken cancellationToken)
    {
        if (_execdEndpoints.TryGetValue(sandboxId, out var cached))
        {
            return cached;
        }

        var resolved = await ResolveSandboxEndpointAsync(sandboxId, ExecdPort, cancellationToken);
        var headers = new Dictionary<string, string>(resolved.Headers, StringComparer.OrdinalIgnoreCase);

        if (_options.UseServerProxy &&
            !headers.ContainsKey(ExecdApiKeyHeader) &&
            !string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            headers[ExecdApiKeyHeader] = _options.ApiKey;
        }

        cached = new ResolvedExecdEndpoint(resolved.Endpoint, headers);
        _execdEndpoints[sandboxId] = cached;
        return cached;
    }

    private async Task<ResolvedLifecycleEndpoint> ResolveSandboxEndpointAsync(
        string sandboxId,
        int port,
        CancellationToken cancellationToken)
    {
        var query = _options.UseServerProxy ? "?use_server_proxy=true" : string.Empty;
        var response = await SendLifecycleAsync<LifecycleEndpointDto>(
            HttpMethod.Get,
            $"sandboxes/{Uri.EscapeDataString(sandboxId)}/endpoints/{port}{query}",
            body: null,
            cancellationToken);

        RecordRequestId(sandboxId, port == ExecdPort
            ? "lifecycle.execd_endpoint.request_id"
            : $"lifecycle.endpoint.{port}.request_id", response.RequestId);

        if (string.IsNullOrWhiteSpace(response.Body?.Endpoint))
        {
            throw new InvalidOperationException(
                $"OpenSandbox did not return an endpoint URL for sandbox {sandboxId} port {port}.");
        }

        return new ResolvedLifecycleEndpoint(
            EnsureAbsoluteEndpoint(response.Body.Endpoint, _lifecycleBaseUri),
            response.Body.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<string> CreateSessionAsync(
        string sandboxId,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var response = await SendExecdAsync<ExecdCreateSessionResponseDto>(
            sandboxId,
            HttpMethod.Post,
            "session",
            string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : new ExecdCreateSessionRequestDto { Cwd = workingDirectory },
            accept: "application/json",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.create_session.request_id", response.RequestId);

        if (string.IsNullOrWhiteSpace(response.Body?.SessionId))
        {
            throw new InvalidOperationException("OpenSandbox create session response did not include a session_id.");
        }

        return response.Body.SessionId;
    }

    private async Task<ExecdCommandStatusDto> GetCommandStatusAsync(
        string sandboxId,
        string commandId,
        CancellationToken cancellationToken)
    {
        var response = await SendExecdAsync<ExecdCommandStatusDto>(
            sandboxId,
            HttpMethod.Get,
            $"command/status/{Uri.EscapeDataString(commandId)}",
            body: null,
            accept: "application/json",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.status.request_id", response.RequestId);
        return response.Body ?? new ExecdCommandStatusDto();
    }

    private async Task<BackgroundCommandLogs> GetBackgroundLogsAsync(
        string sandboxId,
        string commandId,
        long? cursor,
        CancellationToken cancellationToken)
    {
        var path = cursor.HasValue
            ? $"command/{Uri.EscapeDataString(commandId)}/logs?cursor={cursor.Value}"
            : $"command/{Uri.EscapeDataString(commandId)}/logs";

        var response = await SendExecdForBytesAsync(
            sandboxId,
            HttpMethod.Get,
            path,
            accept: "text/plain",
            cancellationToken);

        RecordRequestId(sandboxId, "execd.logs.request_id", response.RequestId);

        long? nextCursor = null;
        if (response.Headers.TryGetValue(BackgroundLogCursorHeader, out var cursorHeader) &&
            long.TryParse(cursorHeader, out var parsedCursor))
        {
            nextCursor = parsedCursor;
        }

        return new BackgroundCommandLogs(
            Encoding.UTF8.GetString(response.Content),
            nextCursor);
    }

    private async Task<ExecdStreamResult> StreamExecdAsync(
        string sandboxId,
        string relativePath,
        object payload,
        string requestIdDiagnosticKey,
        CancellationToken cancellationToken)
    {
        var endpoint = await ResolveExecdEndpointAsync(sandboxId, cancellationToken);
        var requestUri = BuildExecdUri(endpoint.Endpoint, relativePath);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        ApplyHeaders(request.Headers, endpoint.Headers);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var requestId = GetRequestId(response.Headers);
        RecordRequestId(sandboxId, requestIdDiagnosticKey, requestId);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response);
        }

        return await ReadExecdStreamAsync(response, cancellationToken);
    }

    private async Task<OpenSandboxHttpResponse<T?>> SendLifecycleAsync<T>(
        HttpMethod method,
        string relativePath,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_lifecycleBaseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(LifecycleApiKeyHeader, _options.ApiKey);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response);
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            return new OpenSandboxHttpResponse<T?>(default, GetRequestId(response.Headers));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var content = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return new OpenSandboxHttpResponse<T?>(content, GetRequestId(response.Headers));
    }

    private async Task<OpenSandboxHttpResponse<T?>> SendExecdAsync<T>(
        string sandboxId,
        HttpMethod method,
        string relativePath,
        object? body,
        string accept,
        CancellationToken cancellationToken,
        ResolvedExecdEndpoint? endpoint = null)
    {
        endpoint ??= await ResolveExecdEndpointAsync(sandboxId, cancellationToken);

        using var request = new HttpRequestMessage(method, BuildExecdUri(endpoint.Endpoint, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        ApplyHeaders(request.Headers, endpoint.Headers);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response);
        }

        if (response.Content.Headers.ContentLength == 0)
        {
            return new OpenSandboxHttpResponse<T?>(default, GetRequestId(response.Headers));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var content = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return new OpenSandboxHttpResponse<T?>(content, GetRequestId(response.Headers));
    }

    private async Task<OpenSandboxExecdBytesResponse> SendExecdForBytesAsync(
        string sandboxId,
        HttpMethod method,
        string relativePath,
        string accept,
        CancellationToken cancellationToken)
    {
        var endpoint = await ResolveExecdEndpointAsync(sandboxId, cancellationToken);

        using var request = new HttpRequestMessage(method, BuildExecdUri(endpoint.Endpoint, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        ApplyHeaders(request.Headers, endpoint.Headers);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = response.Headers
            .ToDictionary(static pair => pair.Key, static pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase);

        return new OpenSandboxExecdBytesResponse(
            bytes,
            response.Content.Headers.ContentType?.MediaType,
            GetRequestId(response.Headers),
            headers);
    }

    private static async Task<ExecdStreamResult> ReadExecdStreamAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var combinedLogs = new StringBuilder();
        var structuredLogs = new List<SandboxLogEntry>();
        string? executionId = null;
        string? errorMessage = null;
        int? exitCode = null;
        bool completed = false;

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = await ReadNextEventPayloadAsync(reader, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            var @event = JsonSerializer.Deserialize<ExecdStreamEventDto>(payload, JsonOptions);
            if (@event is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(executionId) &&
                string.Equals(@event.Type, "init", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(@event.Text))
            {
                executionId = @event.Text;
            }

            switch (@event.Type?.ToLowerInvariant())
            {
                case "stdout":
                case "stderr":
                    if (!string.IsNullOrEmpty(@event.Text))
                    {
                        combinedLogs.Append(@event.Text);
                        structuredLogs.Add(new SandboxLogEntry(
                            @event.Type.ToLowerInvariant(),
                            @event.Text,
                            ToTimestamp(@event.Timestamp)));
                    }
                    break;

                case "execution_complete":
                    completed = true;
                    break;

                case "error":
                    errorMessage = BuildExecdErrorMessage(@event.Error);
                    if (TryParseExitCode(@event.Error?.Value, out var parsedExitCode))
                    {
                        exitCode = parsedExitCode;
                    }
                    break;
            }
        }

        return new ExecdStreamResult(
            ExecutionId: executionId,
            Logs: combinedLogs.ToString(),
            StructuredLogs: structuredLogs,
            Completed: completed,
            ExitCode: exitCode,
            ErrorMessage: errorMessage);
    }

    private static async Task<string?> ReadNextEventPayloadAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        string? explicitEvent = null;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawLine = await reader.ReadLineAsync(cancellationToken);
            if (rawLine is null)
            {
                break;
            }

            if (rawLine.Length == 0)
            {
                break;
            }

            if (rawLine.StartsWith(":", StringComparison.Ordinal))
            {
                continue;
            }

            if (rawLine.StartsWith("{", StringComparison.Ordinal))
            {
                lines.Add(rawLine);
                continue;
            }

            if (rawLine.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                explicitEvent = rawLine["event:".Length..].Trim();
                continue;
            }

            if (rawLine.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(rawLine["data:".Length..].TrimStart());
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var payload = string.Join(Environment.NewLine, lines);
        if (!string.IsNullOrWhiteSpace(explicitEvent))
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("type", out _))
            {
                using var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, new { type = explicitEvent, text = payload }, JsonOptions, cancellationToken);
                payload = Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        return payload;
    }

    private static string BuildShellCommand(
        IReadOnlyList<string> arguments,
        string? standardInput)
    {
        if (arguments.Count == 0)
        {
            throw new InvalidOperationException("OpenSandbox command execution requires at least one argument.");
        }

        var command = string.Join(" ", arguments.Select(EscapeShellArgument));
        if (string.IsNullOrEmpty(standardInput))
        {
            return command;
        }

        var marker = $"AUTOFAC_STDIN_{Guid.NewGuid():N}";
        return $"""
               cat <<'{marker}' | {command}
               {standardInput}
               {marker}
               """;
    }

    private static string EscapeShellArgument(string argument)
    {
        if (argument.Length == 0)
        {
            return "''";
        }

        return $"'{argument.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static Dictionary<string, string> BuildResourceLimits(OpenSandboxResourceLimits resourceLimits)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (resourceLimits.CpuMilliCores.HasValue)
        {
            result["cpu"] = $"{resourceLimits.CpuMilliCores.Value}m";
        }

        if (resourceLimits.MemoryMb.HasValue)
        {
            result["memory"] = $"{resourceLimits.MemoryMb.Value}Mi";
        }

        if (resourceLimits.GpuCount.HasValue)
        {
            result["gpu"] = resourceLimits.GpuCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static LifecycleNetworkPolicyDto? MapNetworkPolicy(OpenSandboxNetworkPolicy? networkPolicy)
    {
        if (networkPolicy is null)
        {
            return null;
        }

        return networkPolicy.Mode switch
        {
            SandboxNetworkAccessMode.Open => new LifecycleNetworkPolicyDto
            {
                DefaultAction = "allow"
            },
            SandboxNetworkAccessMode.Restricted => new LifecycleNetworkPolicyDto
            {
                DefaultAction = "deny",
                Egress = networkPolicy.AllowedHosts
                    .Where(static host => !string.IsNullOrWhiteSpace(host))
                    .Select(static host => new LifecycleNetworkRuleDto("allow", host))
                    .ToArray()
            },
            _ => new LifecycleNetworkPolicyDto
            {
                DefaultAction = "deny"
            }
        };
    }

    private static IReadOnlyList<LifecycleVolumeDto>? MapVolumes(IReadOnlyList<OpenSandboxVolumeMount> volumes)
    {
        if (volumes.Count == 0)
        {
            return null;
        }

        return volumes.Select(MapVolume).ToArray();
    }

    private static LifecycleVolumeDto MapVolume(OpenSandboxVolumeMount volume)
    {
        return volume.SourceKind switch
        {
            SandboxFilesystemMountSourceKind.HostPath => new LifecycleVolumeDto
            {
                Name = NormalizeArtifactKey(volume.MountPath),
                MountPath = volume.MountPath,
                ReadOnly = volume.ReadOnly,
                Host = new LifecycleHostVolumeDto(volume.Source)
            },
            SandboxFilesystemMountSourceKind.NamedVolume or SandboxFilesystemMountSourceKind.PersistentVolumeClaim => new LifecycleVolumeDto
            {
                Name = NormalizeArtifactKey(volume.MountPath),
                MountPath = volume.MountPath,
                ReadOnly = volume.ReadOnly,
                Pvc = new LifecyclePersistentVolumeDto(volume.Source)
            },
            SandboxFilesystemMountSourceKind.Ossfs => throw new InvalidOperationException(
                "OpenSandbox OSSFS mounts are not supported by Autofac yet because the current sandbox contract does not carry the required bucket and credential fields."),
            _ => throw new ArgumentOutOfRangeException(nameof(volume.SourceKind), volume.SourceKind, "Unsupported OpenSandbox volume source.")
        };
    }

    private static string? TryDecodeArtifactContent(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (bytes.Any(static b => b == 0))
        {
            return null;
        }

        // The deployed execd (opensandbox/execd:v1.0.18) serves every files/download
        // response as "application/octet-stream" regardless of the file's actual
        // content — it does not sniff or declare per-file content types (the same
        // execd quirk noted on CollectPathArtifactsAsync's "type" field above). A
        // content-type allowlist against that header would reject every artifact
        // unconditionally, which is exactly what was happening: agent-run-result.json
        // (and any other text artifact) downloaded fine but was silently dropped here,
        // so OpenSandboxedAgentRunner never saw it and fell back to the sandbox's
        // generic command-failure message instead of the real failure reason inside
        // it. The null-byte check above is the actual binary/text discriminator;
        // content-type isn't a reliable signal from this execd build and is no longer
        // consulted.
        return Encoding.UTF8.GetString(bytes);
    }

    private static string BuildArtifactKey(string requestedPath, string filePath)
    {
        var normalizedRequested = NormalizeSandboxPath(requestedPath);
        var normalizedFile = NormalizeSandboxPath(filePath);
        var prefix = normalizedRequested.TrimEnd('/') + "/";

        if (normalizedFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFile[prefix.Length..];
        }

        if (string.Equals(normalizedRequested, normalizedFile, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(normalizedFile);
        }

        return normalizedFile.TrimStart('/');
    }

    private static string NormalizeArtifactKey(string path) =>
        NormalizeSandboxPath(path).Trim('/').Replace("/", "-", StringComparison.Ordinal);

    private static string NormalizeSandboxPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
    }

    private static string EnsureAbsoluteEndpoint(string endpoint, Uri lifecycleBaseUri)
    {
        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (endpoint.StartsWith("/", StringComparison.Ordinal))
        {
            var origin = new Uri($"{lifecycleBaseUri.Scheme}://{lifecycleBaseUri.Authority}");
            return new Uri(origin, endpoint).ToString();
        }

        var firstSegment = endpoint.Split('/', 2)[0];
        if (firstSegment.Contains('.', StringComparison.Ordinal) ||
            firstSegment.Contains(':', StringComparison.Ordinal))
        {
            return $"{lifecycleBaseUri.Scheme}://{endpoint}";
        }

        return new Uri(lifecycleBaseUri, endpoint).ToString();
    }

    private static DateTimeOffset ToTimestamp(long? unixMilliseconds) =>
        unixMilliseconds.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value)
            : DateTimeOffset.UtcNow;

    private static bool TryParseExitCode(string? value, out int exitCode) =>
        int.TryParse(value, out exitCode);

    private static string? BuildExecdErrorMessage(ExecdErrorDto? error)
    {
        if (error is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(error.Value) && error.Traceback is { Count: > 0 })
        {
            return $"OpenSandbox command failed with exit code {error.Value}: {error.Traceback[0]}";
        }

        if (!string.IsNullOrWhiteSpace(error.Value))
        {
            return $"OpenSandbox command failed with exit code {error.Value}.";
        }

        if (!string.IsNullOrWhiteSpace(error.Name))
        {
            return $"OpenSandbox command failed: {error.Name}.";
        }

        return "OpenSandbox command failed.";
    }

    private static SandboxCommandState DetermineForegroundState(ExecdStreamResult response)
    {
        if (LooksLikeTimeout(response.ErrorMessage))
        {
            return SandboxCommandState.TimedOut;
        }

        if (LooksLikeCancellation(response.ErrorMessage))
        {
            return SandboxCommandState.Cancelled;
        }

        if (!string.IsNullOrWhiteSpace(response.ErrorMessage) || response.ExitCode is > 0)
        {
            return SandboxCommandState.Failed;
        }

        return response.Completed
            ? SandboxCommandState.Completed
            : SandboxCommandState.Unknown;
    }

    private static SandboxCommandState DetermineBackgroundState(string? error, int? exitCode)
    {
        if (LooksLikeTimeout(error))
        {
            return SandboxCommandState.TimedOut;
        }

        if (LooksLikeCancellation(error))
        {
            return SandboxCommandState.Cancelled;
        }

        if (exitCode is null or 0 && string.IsNullOrWhiteSpace(error))
        {
            return SandboxCommandState.Completed;
        }

        return SandboxCommandState.Failed;
    }

    private static string? BuildFailureReason(
        SandboxCommandState state,
        string? detail,
        int? timeoutSeconds)
    {
        return state switch
        {
            SandboxCommandState.Completed => null,
            SandboxCommandState.TimedOut when timeoutSeconds.HasValue =>
                $"OpenSandbox command timed out after {timeoutSeconds.Value}s.",
            SandboxCommandState.TimedOut =>
                detail ?? "OpenSandbox command timed out.",
            SandboxCommandState.Cancelled =>
                detail ?? "OpenSandbox command was cancelled.",
            SandboxCommandState.Failed =>
                detail ?? "OpenSandbox command failed.",
            _ => detail ?? "OpenSandbox command ended without a final completion signal."
        };
    }

    private static bool LooksLikeTimeout(string? message) =>
        message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("deadline", StringComparison.OrdinalIgnoreCase) == true;

    private static bool LooksLikeCancellation(string? message) =>
        message?.Contains("cancel", StringComparison.OrdinalIgnoreCase) == true ||
        message?.Contains("interrupt", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTransientApiFailure(OpenSandboxApiException exception) =>
        exception.StatusCode is (int)HttpStatusCode.NotFound
            or (int)HttpStatusCode.Conflict
            or (int)HttpStatusCode.BadGateway
            or (int)HttpStatusCode.ServiceUnavailable;

    private static Uri NormalizeLifecycleBaseUri(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new InvalidOperationException("OpenSandbox server URL is not configured.");
        }

        var builder = new UriBuilder(serverUrl);
        var path = builder.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || string.Equals(path, "/", StringComparison.Ordinal))
        {
            path = "/v1";
        }
        else if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path += "/v1";
        }

        builder.Path = path.TrimEnd('/') + "/";
        return builder.Uri;
    }

    private Uri BuildExecdUri(string endpoint, string relativePath)
    {
        var builder = new UriBuilder(endpoint);
        var basePath = builder.Path.TrimEnd('/');
        var queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
        var query = queryIndex >= 0 ? relativePath[(queryIndex + 1)..] : string.Empty;

        if (path.StartsWith("/", StringComparison.Ordinal))
        {
            builder.Path = basePath + path;
        }
        else
        {
            builder.Path = string.IsNullOrEmpty(basePath)
                ? "/" + path
                : basePath + "/" + path;
        }

        builder.Query = query;
        return builder.Uri;
    }

    private static void ApplyHeaders(HttpRequestHeaders target, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            target.TryAddWithoutValidation(key, value);
        }
    }

    private static string? GetRequestId(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues(RequestIdHeader, out var values))
        {
            return values.FirstOrDefault();
        }

        if (headers.TryGetValues("X-Request-Id", out values))
        {
            return values.FirstOrDefault();
        }

        return null;
    }

    private void RecordRequestId(string sandboxId, string key, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        RecordDiagnostic(sandboxId, key, requestId);
    }

    private void RecordDiagnostic(string sandboxId, string key, string value)
    {
        if (!_sandboxDiagnostics.TryGetValue(sandboxId, out var diagnostics))
        {
            diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sandboxDiagnostics[sandboxId] = diagnostics;
        }

        diagnostics[key] = value;
    }

    private Dictionary<string, string> SnapshotDiagnostics(string sandboxId) =>
        _sandboxDiagnostics.TryGetValue(sandboxId, out var diagnostics)
            ? new Dictionary<string, string>(diagnostics, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static async Task<OpenSandboxApiException> CreateApiExceptionAsync(HttpResponseMessage response)
    {
        var requestId = GetRequestId(response.Headers);
        var body = await response.Content.ReadAsStringAsync();
        var requestUri = response.RequestMessage?.RequestUri?.ToString();
        var requestMethod = response.RequestMessage?.Method.Method;

        string? errorCode = null;
        string? errorMessage = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LifecycleErrorResponseDto>(body, JsonOptions);
                errorCode = parsed?.Code;
                errorMessage = parsed?.Message;
            }
            catch (JsonException)
            {
                errorMessage = body;
            }
        }

        errorMessage ??= response.ReasonPhrase ?? "OpenSandbox request failed.";

        return new OpenSandboxApiException(
            statusCode: (int)response.StatusCode,
            requestId: requestId,
            errorCode: errorCode,
            message: string.IsNullOrWhiteSpace(requestUri)
                ? errorMessage
                : $"{errorMessage} [request: {requestMethod ?? "?"} {requestUri}]");
    }

    private sealed record OpenSandboxHttpResponse<T>(T Body, string? RequestId);

    private sealed record OpenSandboxExecdBytesResponse(
        byte[] Content,
        string? ContentType,
        string? RequestId,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record ResolvedExecdEndpoint(
        string Endpoint,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record ResolvedLifecycleEndpoint(
        string Endpoint,
        IReadOnlyDictionary<string, string> Headers);

    private sealed record BackgroundCommandLogs(
        string Content,
        long? Cursor);

    private sealed record ExecdStreamResult(
        string? ExecutionId,
        string Logs,
        IReadOnlyList<SandboxLogEntry> StructuredLogs,
        bool Completed,
        int? ExitCode,
        string? ErrorMessage);

    private sealed class LifecycleCreateSandboxRequestDto
    {
        public LifecycleImageSpecDto? Image { get; set; }

        public int? Timeout { get; set; }

        public IReadOnlyDictionary<string, string>? ResourceLimits { get; set; }

        public IReadOnlyDictionary<string, string>? Env { get; set; }

        public IReadOnlyDictionary<string, string>? Metadata { get; set; }

        public IReadOnlyList<string>? Entrypoint { get; set; }

        public LifecycleNetworkPolicyDto? NetworkPolicy { get; set; }

        public bool SecureAccess { get; set; }

        public IReadOnlyList<LifecycleVolumeDto>? Volumes { get; set; }
    }

    private sealed record LifecycleImageSpecDto(string Uri);

    private sealed class LifecycleNetworkPolicyDto
    {
        public string? DefaultAction { get; set; }

        public IReadOnlyList<LifecycleNetworkRuleDto>? Egress { get; set; }
    }

    private sealed record LifecycleNetworkRuleDto(
        string Action,
        string Target);

    private sealed class LifecycleVolumeDto
    {
        public string? Name { get; set; }

        public LifecycleHostVolumeDto? Host { get; set; }

        public LifecyclePersistentVolumeDto? Pvc { get; set; }

        public string? MountPath { get; set; }

        public bool ReadOnly { get; set; }
    }

    private sealed record LifecycleHostVolumeDto(string Path);

    private sealed record LifecyclePersistentVolumeDto(string ClaimName);

    private sealed class LifecycleCreateSandboxResponseDto
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class LifecycleSandboxDto
    {
        public LifecycleSandboxStatusDto? Status { get; set; }
    }

    private sealed class LifecycleSandboxStatusDto
    {
        public string? State { get; set; }

        public string? Reason { get; set; }

        public string? Message { get; set; }
    }

    private sealed class LifecycleEndpointDto
    {
        public string Endpoint { get; set; } = string.Empty;

        public Dictionary<string, string>? Headers { get; set; }
    }

    private sealed class LifecycleErrorResponseDto
    {
        public string? Code { get; set; }

        public string? Message { get; set; }
    }

    private sealed class ExecdCreateSessionRequestDto
    {
        public string? Cwd { get; set; }
    }

    private sealed class ExecdCreateSessionResponseDto
    {
        [JsonPropertyName("session_id")]
        public string SessionId { get; set; } = string.Empty;
    }

    private sealed class ExecdRunCommandRequestDto
    {
        public string Command { get; set; } = string.Empty;

        public string? Cwd { get; set; }

        public bool Background { get; set; }

        public long? Timeout { get; set; }

        public Dictionary<string, string>? Envs { get; set; }
    }

    private sealed class ExecdRunInSessionRequestDto
    {
        public string Command { get; set; } = string.Empty;

        public string? Cwd { get; set; }

        public long? Timeout { get; set; }
    }

    private sealed class ExecdCommandStatusDto
    {
        public bool Running { get; set; }

        [JsonPropertyName("exit_code")]
        public int? ExitCode { get; set; }

        public string? Error { get; set; }
    }

    private sealed class ExecdFileInfoDto
    {
        public string Path { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class ExecdStreamEventDto
    {
        public string? Type { get; set; }

        public string? Text { get; set; }

        public long? Timestamp { get; set; }

        [JsonPropertyName("execution_time")]
        public long? ExecutionTime { get; set; }

        public ExecdErrorDto? Error { get; set; }
    }

    private sealed class ExecdErrorDto
    {
        [JsonPropertyName("ename")]
        public string? Name { get; set; }

        [JsonPropertyName("evalue")]
        public string? Value { get; set; }

        [JsonPropertyName("traceback")]
        public List<string>? Traceback { get; set; }
    }
}

public sealed class OpenSandboxApiException : InvalidOperationException
{
    public OpenSandboxApiException(
        int statusCode,
        string? requestId,
        string? errorCode,
        string message)
        : base(BuildMessage(statusCode, requestId, errorCode, message))
    {
        StatusCode = statusCode;
        RequestId = requestId;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }

    public string? RequestId { get; }

    public string? ErrorCode { get; }

    private static string BuildMessage(
        int statusCode,
        string? requestId,
        string? errorCode,
        string message)
    {
        var builder = new StringBuilder();
        builder.Append("OpenSandbox API request failed (")
            .Append(statusCode)
            .Append(')');

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            builder.Append(" [").Append(errorCode).Append(']');
        }

        builder.Append(": ").Append(message);

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            builder.Append(" (request_id: ").Append(requestId).Append(')');
        }

        return builder.ToString();
    }
}
