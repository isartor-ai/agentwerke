using System.Text.Json;
using Agentwerke.Integrations.Webhooks;

namespace Agentwerke.Integrations.Tests;

public sealed class WebhookPayloadTests
{
    // ── Jira ─────────────────────────────────────────────────────────────────

    [Fact]
    public void JiraPayload_Deserializes_FullPayload()
    {
        const string json = """
            {
              "webhookEvent": "jira:issue_created",
              "issue": {
                "id": "10001",
                "key": "PROJ-123",
                "self": "https://example.atlassian.net/rest/api/2/issue/10001",
                "fields": {
                  "summary": "Deploy new release",
                  "description": "Needs review",
                  "issuetype": { "name": "Task" },
                  "project": { "key": "PROJ", "name": "Project" }
                }
              },
              "user": {
                "displayName": "Jane Doe",
                "emailAddress": "jane@example.com"
              }
            }
            """;

        var payload = JsonSerializer.Deserialize<JiraWebhookPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("jira:issue_created", payload.WebhookEvent);
        Assert.Equal("PROJ-123", payload.Issue?.Key);
        Assert.Equal("Deploy new release", payload.Issue?.Fields?.Summary);
        Assert.Equal("Jane Doe", payload.User?.DisplayName);
    }

    [Fact]
    public void JiraPayload_MinimalPayload_DoesNotThrow()
    {
        const string json = """{"webhookEvent":"jira:issue_updated"}""";
        var payload = JsonSerializer.Deserialize<JiraWebhookPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("jira:issue_updated", payload.WebhookEvent);
        Assert.Null(payload.Issue);
    }

    // ── GitHub ────────────────────────────────────────────────────────────────

    [Fact]
    public void GitHubPayload_Deserializes_FullPayload()
    {
        const string json = """
            {
              "action": "opened",
              "issue": {
                "number": 42,
                "html_url": "https://github.com/org/repo/issues/42",
                "title": "Provision new environment",
                "body": "Please provision staging-v2",
                "state": "open",
                "labels": [{ "name": "infra" }]
              },
              "repository": {
                "full_name": "org/repo",
                "html_url": "https://github.com/org/repo"
              },
              "sender": { "login": "alice" }
            }
            """;

        var payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("opened", payload.Action);
        Assert.Equal(42, payload.Issue?.Number);
        Assert.Equal("Provision new environment", payload.Issue?.Title);
        Assert.Equal("org/repo", payload.Repository?.FullName);
        Assert.Equal("alice", payload.Sender?.Login);
        Assert.Contains("infra", payload.Issue?.Labels?.Select(l => l.Name) ?? []);
    }

    [Fact]
    public void GitHubPayload_MinimalPayload_DoesNotThrow()
    {
        const string json = """{"action":"closed"}""";
        var payload = JsonSerializer.Deserialize<GitHubWebhookPayload>(json);

        Assert.NotNull(payload);
        Assert.Equal("closed", payload.Action);
        Assert.Null(payload.Issue);
    }
}
