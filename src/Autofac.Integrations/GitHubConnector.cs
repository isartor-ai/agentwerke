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

    public override IReadOnlyList<string> SupportedOperations => ["create_branch", "create_pull_request"];

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

    protected override async Task<ConnectorExecutionResult> ExecuteAllowedAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        return request.Operation switch
        {
            "create_branch" => await ExecuteCreateBranchAsync(request, cancellationToken),
            "create_pull_request" => await ExecuteCreatePullRequestAsync(request, cancellationToken),
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

    private sealed record GitHubErrorResponse(string? Message);
}
