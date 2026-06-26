using Autofac.Agents;

namespace Autofac.Agents.Tests;

/// <summary>
/// #150: agent output is gathered from run context for inclusion in the created PR.
/// </summary>
public sealed class AgentOutputForPrTests
{
    private static readonly Dictionary<string, string> RunContext = new(StringComparer.OrdinalIgnoreCase)
    {
        ["input.title"] = "Add health endpoint",
        ["output.Analyze"] = "Spec: expose GET /health returning 200.",
        ["output.Implement"] = "Added HealthController.",
    };

    [Fact]
    public void CollectAgentOutput_WithSpecificNode_ReturnsThatOutput()
    {
        var result = AgentOrchestrator.CollectAgentOutput("Implement", RunContext);

        Assert.Equal("Added HealthController.", result);
    }

    [Fact]
    public void CollectAgentOutput_WithoutNode_ConcatenatesAllOutputsUnderHeadings()
    {
        var result = AgentOrchestrator.CollectAgentOutput(null, RunContext);

        Assert.Contains("### Analyze", result);
        Assert.Contains("Spec: expose GET /health returning 200.", result);
        Assert.Contains("### Implement", result);
        Assert.Contains("Added HealthController.", result);
        // input.* entries are not agent output and must not leak in.
        Assert.DoesNotContain("Add health endpoint", result);
    }

    [Fact]
    public void CollectAgentOutput_UnknownNode_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AgentOrchestrator.CollectAgentOutput("DoesNotExist", RunContext));
    }

    [Fact]
    public void CollectAgentOutput_NoOutputs_ReturnsEmpty()
    {
        var ctx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["input.title"] = "x" };

        Assert.Equal(string.Empty, AgentOrchestrator.CollectAgentOutput(null, ctx));
    }
}
