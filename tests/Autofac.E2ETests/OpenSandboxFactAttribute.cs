namespace Autofac.E2ETests;

public sealed class OpenSandboxFactAttribute : FactAttribute
{
    public OpenSandboxFactAttribute()
    {
        var enabled = Environment.GetEnvironmentVariable("AUTOFAC_OPEN_SANDBOX_E2E");
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Skipped: set AUTOFAC_OPEN_SANDBOX_E2E=true to run OpenSandbox workflow E2E tests.";
        }
    }
}
