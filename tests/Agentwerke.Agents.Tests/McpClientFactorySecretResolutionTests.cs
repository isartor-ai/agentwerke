using Agentwerke.Agents.Mcp;

namespace Agentwerke.Agents.Tests;

public sealed class McpClientFactorySecretResolutionTests
{
    [Fact]
    public void ResolveSecretReferences_ExpandsEnvironmentVariablePlaceholders()
    {
        var envName = $"AGENTWERKE_MCP_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, "tok_live_12345");
        try
        {
            var resolved = McpClientFactory.ResolveSecretReferences(
                "github-local",
                $"Bearer ${{env:{envName}}}");

            Assert.Equal("Bearer tok_live_12345", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public void ResolveSecretReferences_WhenEnvironmentVariableIsMissing_ThrowsActionableError()
    {
        var envName = $"AGENTWERKE_MCP_MISSING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(envName, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            McpClientFactory.ResolveSecretReferences("github-local", $"Bearer ${{env:{envName}}}"));

        Assert.Contains("github-local", ex.Message, StringComparison.Ordinal);
        Assert.Contains(envName, ex.Message, StringComparison.Ordinal);
    }
}
