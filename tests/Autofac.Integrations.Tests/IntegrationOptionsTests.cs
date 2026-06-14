using Autofac.Integrations;

namespace Autofac.Integrations.Tests;

public sealed class IntegrationOptionsTests
{
    [Fact]
    public void JiraOptions_Defaults_TriggerOnIssueCreated()
    {
        var opts = new JiraOptions();

        Assert.Contains("jira:issue_created", opts.TriggerEvents);
        Assert.Equal(string.Empty, opts.WebhookSecret);
    }

    [Fact]
    public void GitHubOptions_Defaults_TriggerOnOpened()
    {
        var opts = new GitHubOptions();

        Assert.Contains("opened", opts.TriggerActions);
        Assert.Equal("https://api.github.com/", opts.ApiBaseUrl);
        Assert.Equal("main", opts.DefaultBaseBranch);
        Assert.Equal("autofac/run-", opts.BranchPrefix);
        Assert.True(opts.CreateDraftPullRequests);
        Assert.Equal(string.Empty, opts.WebhookSecret);
    }

    [Fact]
    public void IntegrationOptions_Section_IsCorrect()
    {
        Assert.Equal("Integrations", IntegrationOptions.Section);
    }
}
