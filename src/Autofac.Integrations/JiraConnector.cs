using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Autofac.AgentSecOps;
using Autofac.Application.Observability;
using Autofac.Application.Secrets;
using Microsoft.Extensions.Options;

namespace Autofac.Integrations;

public interface IJiraConnector
{
    Task<JiraStatusUpdateResult> UpdateIssueStatusAsync(
        UpdateJiraIssueStatusCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record UpdateJiraIssueStatusCommand(
    string IssueKey,
    string? TransitionId,
    string? Comment);

public sealed record JiraStatusUpdateResult(
    string IssueKey,
    bool TransitionApplied,
    bool CommentAdded,
    string IssueUrl);

public sealed class JiraConnector : ConnectorBase, IJiraConnector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly JiraOptions _options;
    private readonly ISecretStore _secretStore;

    public JiraConnector(
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
        _options = options.Value.Jira;
        _secretStore = secretStore;
    }

    public override string ConnectorId => "jira";

    public override string DisplayName => "Jira";

    public override bool Enabled => _options.Enabled;

    public override IReadOnlyList<string> SupportedOperations => ["update_issue_status"];

    public async Task<JiraStatusUpdateResult> UpdateIssueStatusAsync(
        UpdateJiraIssueStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(cancellationToken);

        var transitionApplied = false;
        var commentAdded = false;

        if (!string.IsNullOrWhiteSpace(command.TransitionId))
        {
            using var transitionRequest = new HttpRequestMessage(HttpMethod.Post, $"rest/api/3/issue/{command.IssueKey}/transitions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        transition = new
                        {
                            id = command.TransitionId
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            using var transitionResponse = await _httpClient.SendAsync(transitionRequest, cancellationToken);
            transitionResponse.EnsureSuccessStatusCode();
            transitionApplied = true;
        }

        if (!string.IsNullOrWhiteSpace(command.Comment))
        {
            // Jira REST API v3 requires Atlassian Document Format (ADF) for comment bodies.
            using var commentRequest = new HttpRequestMessage(HttpMethod.Post, $"rest/api/3/issue/{command.IssueKey}/comment")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        body = new
                        {
                            type = "doc",
                            version = 1,
                            content = new[]
                            {
                                new
                                {
                                    type = "paragraph",
                                    content = new[]
                                    {
                                        new { type = "text", text = command.Comment }
                                    }
                                }
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            using var commentResponse = await _httpClient.SendAsync(commentRequest, cancellationToken);
            commentResponse.EnsureSuccessStatusCode();
            commentAdded = true;
        }

        return new JiraStatusUpdateResult(
            command.IssueKey,
            transitionApplied,
            commentAdded,
            $"{_options.ApiBaseUrl.TrimEnd('/')}/browse/{command.IssueKey}");
    }

    protected override async Task<ConnectorExecutionResult> ExecuteAllowedAsync(ConnectorExecutionRequest request, CancellationToken cancellationToken)
    {
        var command = request.Payload.Deserialize<UpdateJiraIssueStatusCommand>(SerializerOptions)
            ?? throw new InvalidOperationException("Jira payload was empty.");

        var result = await UpdateIssueStatusAsync(command, cancellationToken);
        return new ConnectorExecutionResult(
            Succeeded: true,
            Status: "completed",
            Summary: $"Updated Jira issue {result.IssueKey}.",
            ExternalId: result.IssueKey,
            ExternalUrl: result.IssueUrl);
    }

    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        var apiToken = await _secretStore.GetSecretAsync("Integrations:Jira:ApiToken", cancellationToken)
            ?? _options.ApiToken;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{apiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}
