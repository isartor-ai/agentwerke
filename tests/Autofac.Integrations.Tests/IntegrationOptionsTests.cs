using Autofac.Integrations;

namespace Autofac.Integrations.Tests;

public sealed class IntegrationOptionsTests
{
    [Fact]
    public void JiraOptions_Defaults_TriggerOnIssueCreated()
    {
        var opts = new JiraOptions();

        Assert.False(opts.Enabled);
        Assert.Equal("https://your-domain.atlassian.net/", opts.ApiBaseUrl);
        Assert.Contains("jira:issue_created", opts.TriggerEvents);
        Assert.Equal(string.Empty, opts.WebhookSecret);
    }

    [Fact]
    public void GitHubOptions_Defaults_TriggerOnOpened()
    {
        var opts = new GitHubOptions();

        Assert.False(opts.Enabled);
        Assert.Contains("opened", opts.TriggerActions);
        Assert.Equal("https://api.github.com/", opts.ApiBaseUrl);
        Assert.Equal("main", opts.DefaultBaseBranch);
        Assert.Equal("agentwerke/run-", opts.BranchPrefix);
        Assert.Equal("agentwerke", opts.RequiredLabel);
        Assert.True(opts.CreateDraftPullRequests);
        Assert.Equal(string.Empty, opts.WebhookSecret);
    }

    [Fact]
    public void IntegrationOptions_Section_IsCorrect()
    {
        Assert.Equal("Integrations", IntegrationOptions.Section);
    }

    [Fact]
    public void SlackAndTeams_DefaultToDisabled()
    {
        var opts = new IntegrationOptions();

        Assert.False(opts.Slack.Enabled);
        Assert.False(opts.Teams.Enabled);
    }
}
