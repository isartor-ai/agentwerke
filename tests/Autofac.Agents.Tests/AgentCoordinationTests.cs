using Autofac.Agents.Coordination;
using Autofac.Agents.Tools;

namespace Autofac.Agents.Tests;

public sealed class AgentCoordinationTests
{
    private static AgentToolExecutionContext Context(string runId, string agent) =>
        new(runId, "step", agent, "agent.post_message", null, "general", "tag", 1);

    [Fact]
    public void Channel_AppendsAndReadsInOrder_PerRun()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        channel.Post("run-1", "ba", "requirements ready");
        channel.Post("run-1", "architect", "design ready");
        channel.Post("run-2", "other", "different run");

        var run1 = channel.Read("run-1");
        Assert.Equal(2, run1.Count);
        Assert.Equal("ba", run1[0].From);
        Assert.Equal("architect", run1[1].From);
        Assert.Single(channel.Read("run-2"));
    }

    [Fact]
    public void Channel_FiltersBySender()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        channel.Post("run-1", "ba", "a");
        channel.Post("run-1", "architect", "b");

        var fromBa = channel.Read("run-1", "ba");
        Assert.Single(fromBa);
        Assert.Equal("ba", fromBa[0].From);
    }

    [Fact]
    public async Task PostAndReadTools_RoundTrip()
    {
        var channel = new InMemoryAgentCoordinationChannel();
        var post = new AgentPostMessageTool(channel);
        var read = new AgentReadMessagesTool(channel);

        await post.ExecuteAsync(
            Context("run-1", "ba"),
            new Dictionary<string, string> { ["text"] = "requirements ready" },
            CancellationToken.None);

        var result = await read.ExecuteAsync(
            Context("run-1", "architect"),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("ba: requirements ready", result.Output);
    }

    [Fact]
    public void PostTool_MissingText_Throws()
    {
        var tool = new AgentPostMessageTool(new InMemoryAgentCoordinationChannel());
        Assert.Throws<InvalidOperationException>(() => tool.Validate(new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ReadTool_NoMessages_ReturnsFriendlyMessage()
    {
        var read = new AgentReadMessagesTool(new InMemoryAgentCoordinationChannel());
        var result = await read.ExecuteAsync(
            Context("run-empty", "architect"),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("No coordination messages", result.Output!);
    }
}
