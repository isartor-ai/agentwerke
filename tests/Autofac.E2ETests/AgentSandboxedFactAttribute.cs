namespace Autofac.E2ETests;

public sealed class AgentSandboxedFactAttribute : FactAttribute
{
    public AgentSandboxedFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable("AUTOFAC_AGENT_SANDBOXED_E2E");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Skipped: set AUTOFAC_AGENT_SANDBOXED_E2E=true to run agent_sandboxed workflow E2E tests.";
        }
    }
}
