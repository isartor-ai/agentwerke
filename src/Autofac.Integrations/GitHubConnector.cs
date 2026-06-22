using Autofac.Application.Secrets;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autofac.AgentSecOps;
using Autofac.Application.Observability;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

public interface IGitHubConnector
{
    Task<GitHubBranchResult> CreateBranchAsync(
        CreateGitHubBranchCommand command,
        CancellationToken cancellationToken = default);

    Task<GitHubPullRequestResult> CreatePullRequestAsync(
        CreateGitHubPullRequestCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a pull request's current state — used to detect merge for the SDLC "wait for PR merge" gate (#136).</summary>
    Task<GitHubPullRequestStatusResult> GetPullRequestAsync(
        int pullNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Reads aggregated check-run status for a ref/sha — used for the SDLC "wait for CI green" gate (#136).</summary>
    Task<GitHubCheckStatusResult> GetCheckStatusAsync(
        string @ref,
        CancellationToken cancellationToken = default);
}

public sealed record CreateGitHubBranchCommand(
    string BranchName,
    string? BaseBranch);

public sealed record GitHubBranchResult(
    string BranchName,
    string BaseBranch,
    string CommitSha,
    string BranchUrl,
    bool AlreadyExisted);

public sealed record CreateGitHubPullRequestCommand(
    string RunId,
    string StepId,
    int Attempt,
    string HeadBranch,
    string? BaseBranch,
    string Title,
    string Body,
    string CommitMessage);

public sealed record GitHubPullRequestResult(
    int Number,
    string PullRequestUrl,
    string HeadBranch,
    string BaseBranch,
    string CommitSha,
    string MarkerPath,
    bool AlreadyExisted);

public sealed record GitHubPullRequestStatusResult(
    int Number,
    string State,
    bool Merged,
    string? MergeCommitSha,
    string HeadBranch,
    string HeadSha,
    string BaseBranch);

public sealed record GitHubCheckStatusResult(
    string Ref,
    int TotalCount,
    /// <summary>"queued" | "in_progress" | "completed" — "completed" only once every check run is.</summary>
    string Status,
    /// <summary>Null until <see cref="Status"/> is "completed"; "failure" if any run failed, else "success".</summary>
    string? Conclusion,
    IReadOnlyList<GitHubCheckRunSummary> CheckRuns);

public sealed record GitHubCheckRunSummary(
    string Name,
    string Status,
    string? Conclusion);

public sealed record GetGitHubPullRequestCommand(int PullNumber);

public sealed record GetGitHubCheckStatusCommand(string Ref);

public sealed class GitHubConnector : ConnectorBase, IGitHubConnector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly GitHubOptions _options;
    private readonly ISecretStore _secretStore;

    public GitHubConnector(
        HttpClient httpClient,
        IOptions<IntegrationOptions> options,
        ISecretStore secretStore,
        IPolicyEvaluationService policyEvaluationService,
        IAuditRepository auditRepository,
        IWorkflowMetrics metrics,
        ICorrelationContext correlationContext,
        IWorkflowTracer tracer)
        : base(policyEvaluationService, auditRepository, metrics, correlationContext, tracer)
    {
        _httpClient = httpClient;
        _options = options.Value.GitHub;
        _secretStore = secretStore;

        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Autofac/1.0");
        }

        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }
    }

    public override string ConnectorId => "github";

    public override string DisplayName => "GitHub";

    public override bool Enabled => _options.Enabled;

    public override IReadOnlyList<string> SupportedOperations =>
        ["create_branch", "create_pull_request", "get_pull_request", "get_check_status"];

    public async Task<GitHubBranchResult> CreateBranchAsync(
        CreateGitHubBranchCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureConfigured();

        var baseBranch = ResolveBaseBranch(command.BaseBranch);
        var baseRef = await GetBranchReferenceAsync(baseBranch, cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            @ref = $"refs/heads/{command.BranchName}",
            sha = baseRef.Sha
        });

        using var request = await CreateRequestAsync(HttpMethod.Post, "git/refs", payload, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return new GitHubBranchResult(
                BranchName: command.BranchName,
                BaseBranch: baseBranch,
                CommitSha: baseRef.Sha,
                BranchUrl: BuildBranchUrl(command.BranchName),
                AlreadyExisted: true);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        return new GitHubBranchResult(
            BranchName: command.BranchName,
            BaseBranch: baseBranch,
            CommitSha: baseRef.Sha,
            BranchUrl: BuildBranchUrl(command.BranchName),
            AlreadyExisted: false);
    }

    public async Task<GitHubPullRequestResult> CreatePullRequestAsync(
        CreateGitHubPullRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureConfigured();

        var baseBranch = ResolveBaseBranch(command.BaseBranch);
        var markerPath = $".autofac/runs/{SanitizePathSegment(command.RunId)}/{SanitizePathSegment(command.StepId)}-attempt-{command.Attempt}.md";
        var commitSha = await CommitMarkerFileAsync(command, markerPath, cancellationToken);

        var existingPullRequest = await FindExistingPullRequestAsync(command.HeadBranch, baseBranch, cancellationToken);
        if (existingPullRequest is not null)
        {
            return new GitHubPullRequestResult(
                Number: existingPullRequest.Number,
                PullRequestUrl: existingPullRequest.HtmlUrl,
                HeadBranch: command.HeadBranch,
                BaseBranch: baseBranch,
                CommitSha: commitSha,
                MarkerPath: markerPath,
                AlreadyExisted: true);
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = command.Title,
            head = command.HeadBranch,
            @base = baseBranch,
            body = command.Body,
            draft = _options.CreateDraftPullRequests
        });

        using var request = await CreateRequestAsync(HttpMethod.Post, "pulls", payload, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await DeserializeAsync<PullRequestResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("GitHub did not return a pull request payload.");

        return new GitHubPullRequestResult(
            Number: created.Number,
            PullRequestUrl: created.HtmlUrl,
            HeadBranch: command.HeadBranch,
            BaseBranch: baseBranch,
            CommitSha: commitSha,
            MarkerPath: markerPath,
            AlreadyExisted: false);
    }

    public async Task<GitHubPullRequestStatusResult> GetPullRequestAsync(
        int pullNumber,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = await CreateRequestAsync(HttpMethod.Get, $"pulls/{pullNumber}", cancellationToken: cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var pullRequest = await DeserializeAsync<PullRequestStatusResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException($"GitHub did not return pull request #{pullNumber}.");

        return new GitHubPullRequestStatusResult(
            Number: pullRequest.Number,
            State: pullRequest.State,
            Merged: pullRequest.Merged,
            MergeCommitSha: pullRequest.MergeCommitSha,
            HeadBranch: pullRequest.Head.Ref,
            HeadSha: pullRequest.Head.Sha,
            BaseBranch: pullRequest.Base.Ref);
    }

    public async Task<GitHubCheckStatusResult> GetCheckStatusAsync(
        string @ref,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@ref);
        EnsureConfigured();

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"commits/{Uri.EscapeDataString(@ref)}/check-runs",
            cancellationToken: cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var checkRunsResponse = await DeserializeAsync<CheckRunsResponse>(response, cancellationToken)
            ?? new CheckRunsResponse(0, []);

        return AggregateCheckStatus(@ref, checkRunsResponse);
    }

    private static GitHubCheckStatusResult AggregateCheckStatus(string @ref, CheckRunsResponse response)
    {
        var checkRuns = (response.CheckRuns ?? [])
            .Select(static run => new GitHubCheckRunSummary(run.Name, run.Status, run.Conclusion))
            .ToArray();

        if (checkRuns.Length == 0)
        {
            return new GitHubCheckStatusResult(@ref, 0, "queued", null, checkRuns);
        }

        var allCompleted = checkRuns.All(static run => string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase));
        if (!allCompleted)
        {
            var status = checkRuns.Any(static run => string.Equals(run.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
                ? "in_progress"
                : "queued";
            return new GitHubCheckStatusResult(@ref, checkRuns.Length, status, null, checkRuns);
        }

        var conclusion = checkRuns.Any(static run => !string.Equals(run.Conclusion, "success", StringComparison.OrdinalIgnoreCase))
            ? "failure"
            : "success";
        return new GitHubCheckStatusResult(@ref, checkRuns.Length, "completed", conclusion, checkRuns);
    }

    protected override async Task<ConnectorExecutionResult> ExecuteAllowedAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        return request.Operation switch
        {
            "create_branch" => await ExecuteCreateBranchAsync(request, cancellationToken),
            "create_pull_request" => await ExecuteCreatePullRequestAsync(request, cancellationToken),
            "get_pull_request" => await ExecuteGetPullRequestAsync(request, cancellationToken),
            "get_check_status" => await ExecuteGetCheckStatusAsync(request, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported GitHub operation '{request.Operation}'.")
        };
    }

    private async Task<ConnectorExecutionResult> ExecuteCreateBranchAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<CreateGitHubBranchCommand>(SerializerOptions)
            ?? throw new InvalidOperationException("GitHub branch payload was empty.");

        var result = await CreateBranchAsync(command, cancellationToken);
        return new ConnectorExecutionResult(
            Succeeded: true,
            Status: result.AlreadyExisted ? "already_exists" : "completed",
            Summary: $"GitHub branch {result.BranchName} prepared.",
            ExternalId: result.BranchName,
            ExternalUrl: result.BranchUrl);
    }

    private async Task<ConnectorExecutionResult> ExecuteCreatePullRequestAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<CreateGitHubPullRequestCommand>(SerializerOptions)
            ?? throw new InvalidOperationException("GitHub pull request payload was empty.");

        var result = await CreatePullRequestAsync(command, cancellationToken);
        return new ConnectorExecutionResult(
            Succeeded: true,
            Status: result.AlreadyExisted ? "already_exists" : "completed",
            Summary: $"GitHub pull request #{result.Number} prepared.",
            ExternalId: result.Number.ToString(),
            ExternalUrl: result.PullRequestUrl);
    }

    private async Task<ConnectorExecutionResult> ExecuteGetPullRequestAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<GetGitHubPullRequestCommand>(SerializerOptions)
            ?? throw new InvalidOperationException("GitHub get-pull-request payload was empty.");

        var result = await GetPullRequestAsync(command.PullNumber, cancellationToken);
        return new ConnectorExecutionResult(
            Succeeded: true,
            Status: result.Merged ? "merged" : result.State,
            Summary: $"GitHub pull request #{result.Number} is {(result.Merged ? "merged" : result.State)}.",
            ExternalId: result.Number.ToString());
    }

    private async Task<ConnectorExecutionResult> ExecuteGetCheckStatusAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<GetGitHubCheckStatusCommand>(SerializerOptions)
            ?? throw new InvalidOperationException("GitHub get-check-status payload was empty.");

        var result = await GetCheckStatusAsync(command.Ref, cancellationToken);
        return new ConnectorExecutionResult(
            Succeeded: true,
            Status: result.Conclusion ?? result.Status,
            Summary: $"GitHub checks for {result.Ref}: {result.Status}/{result.Conclusion ?? "pending"} ({result.TotalCount} run(s)).",
            ExternalId: result.Ref);
    }

    private async Task<string> CommitMarkerFileAsync(
        CreateGitHubPullRequestCommand command,
        string markerPath,
        CancellationToken cancellationToken)
    {
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(command.Body));
        var payload = JsonSerializer.Serialize(new
        {
            message = command.CommitMessage,
            content,
            branch = command.HeadBranch
        });

        using var request = await CreateRequestAsync(HttpMethod.Put, $"contents/{markerPath}", payload, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var created = await DeserializeAsync<CreateContentResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("GitHub did not return commit details for the marker file.");

        return created.Commit.Sha;
    }

    private async Task<PullRequestResponse?> FindExistingPullRequestAsync(
        string headBranch,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var query = $"pulls?state=open&head={Uri.EscapeDataString($"{_options.RepositoryOwner}:{headBranch}")}&base={Uri.EscapeDataString(baseBranch)}";
        using var request = await CreateRequestAsync(HttpMethod.Get, query, cancellationToken: cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var existing = await DeserializeAsync<List<PullRequestResponse>>(response, cancellationToken);
        return existing?.FirstOrDefault();
    }

    private async Task<GitReferenceResponse> GetBranchReferenceAsync(string branchName, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"git/ref/heads/{Uri.EscapeDataString(branchName)}", cancellationToken: cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await DeserializeAsync<GitReferenceResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException($"GitHub did not return a reference for branch '{branchName}'.");
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relativePath, string? json = null, CancellationToken cancellationToken = default)
    {
        var pat = await _secretStore.GetSecretAsync("Integrations:GitHub:PersonalAccessToken", cancellationToken)
                  ?? _options.PersonalAccessToken;

        var request = new HttpRequestMessage(
            method,
            $"repos/{_options.RepositoryOwner}/{_options.RepositoryName}/{relativePath}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private string ResolveBaseBranch(string? overrideBranch) =>
        string.IsNullOrWhiteSpace(overrideBranch) ? _options.DefaultBaseBranch : overrideBranch.Trim();

    private string BuildBranchUrl(string branchName) =>
        $"https://github.com/{_options.RepositoryOwner}/{_options.RepositoryName}/tree/{branchName}";

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.RepositoryOwner) ||
            string.IsNullOrWhiteSpace(_options.RepositoryName))
        {
            throw new InvalidOperationException(
                "GitHub outbound integration is not configured. Set Integrations:GitHub:RepositoryOwner and RepositoryName.");
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadErrorMessageAsync(response, cancellationToken);
        throw new InvalidOperationException(message);
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"GitHub API call failed with status {(int)response.StatusCode}.";
        }

        try
        {
            var payload = JsonSerializer.Deserialize<GitHubErrorResponse>(text, SerializerOptions);
            if (!string.IsNullOrWhiteSpace(payload?.Message))
            {
                return payload.Message;
            }
        }
        catch
        {
            // Fall back to raw text below.
        }

        return text;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken);
    }

    private sealed record GitReferenceResponse(string Ref, GitReferenceObject Object)
    {
        public string Sha => Object.Sha;
    }

    private sealed record GitReferenceObject(string Sha);

    private sealed record CreateContentResponse(CreateContentCommit Commit);

    private sealed record CreateContentCommit(string Sha);

    private sealed record PullRequestResponse(
        int Number,
        [property: JsonPropertyName("html_url")] string HtmlUrl);

    private sealed record PullRequestStatusResponse(
        int Number,
        string State,
        bool Merged,
        [property: JsonPropertyName("merge_commit_sha")] string? MergeCommitSha,
        PullRequestRefResponse Head,
        PullRequestRefResponse Base);

    private sealed record PullRequestRefResponse(string Ref, string Sha);

    private sealed record CheckRunsResponse(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("check_runs")] List<CheckRunResponse>? CheckRuns);

    private sealed record CheckRunResponse(string Name, string Status, string? Conclusion);

    private sealed record GitHubErrorResponse(string? Message);
}
