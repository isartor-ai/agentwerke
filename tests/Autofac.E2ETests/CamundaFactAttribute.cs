namespace Autofac.E2ETests;

/// <summary>
/// Runs the test only when CAMUNDA_ENABLED=true; otherwise skips with a descriptive message.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CamundaFactAttribute : FactAttribute
{
    public CamundaFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CAMUNDA_ENABLED"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Skipped: set CAMUNDA_ENABLED=true to run Camunda spike tests "
                + "(docker compose --profile camunda).";
        }
    }
}
