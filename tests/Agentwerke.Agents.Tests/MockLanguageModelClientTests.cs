using Agentwerke.Agents.Models;
using Microsoft.Extensions.Options;

namespace Agentwerke.Agents.Tests;

public sealed class MockLanguageModelClientTests
{
    private static MockLanguageModelClient Create() =>
        new(Options.Create(new LanguageModelOptions { Provider = "mock" }));

    [Fact]
    public async Task RunAsync_NoTools_ReturnsDeterministicSuccessWithZeroTokens()
    {
        var client = Create();

        var response = await client.RunAsync(
            new LanguageModelRequest("system", "do the task", Tools: []),
            (_, _) => throw new InvalidOperationException("no tools offered"),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(response.Output));
        Assert.Equal(0, response.Usage.InputTokens);
        Assert.Equal(0, response.Usage.OutputTokens);
        Assert.Empty(response.AllToolCalls);
        Assert.Equal("agentwerke-mock-1", response.ModelId);
    }

    [Fact]
    public async Task RunAsync_IsDeterministic_AcrossCalls()
    {
        var client = Create();
        var req = new LanguageModelRequest("system", "do the task", Tools: []);

        var a = await client.RunAsync(req, (_, _) => throw new InvalidOperationException(), CancellationToken.None);
        var b = await client.RunAsync(req, (_, _) => throw new InvalidOperationException(), CancellationToken.None);

        Assert.Equal(a.Output, b.Output);
    }

    [Fact]
    public async Task RunAsync_WithWritableTool_InvokesItOnce()
    {
        var client = Create();
        var calls = new List<LanguageModelToolCall>();
        var tools = new[]
        {
            new LanguageModelToolDefinition(
                "sandbox.file_write", "write a file",
                new[]
                {
                    new LanguageModelToolParameter("path", "string", "path", true),
                    new LanguageModelToolParameter("content", "string", "content", true),
                }),
        };

        var response = await client.RunAsync(
            new LanguageModelRequest("system", "implement", tools),
            (call, _) =>
            {
                calls.Add(call);
                return Task.FromResult(new LanguageModelToolResult(call.Id, "ok"));
            },
            CancellationToken.None);

        Assert.True(response.Succeeded);
        var call = Assert.Single(calls);
        Assert.Equal("sandbox.file_write", call.Name);
        Assert.Equal("AGENTWERKE_MOCK.md", call.Input["path"]);
        Assert.False(string.IsNullOrWhiteSpace(call.Input["content"]));
    }
}
