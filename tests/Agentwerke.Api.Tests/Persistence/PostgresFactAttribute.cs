namespace Agentwerke.Api.Tests.Persistence;

/// <summary>
/// Runs the test only when AGENTWERKE_TEST_POSTGRES holds a connection string; otherwise skips with
/// a descriptive message. Mirrors the CamundaFactAttribute convention in Agentwerke.E2ETests.
///
/// This exists because the single-winner guarantee (#218) is a property of the *database*, not of
/// our code: it holds only if Postgres rejects a stale write on the Version concurrency token. Every
/// other test fake in this repo is hand-rolled and cannot prove that. CI stays green without a
/// database; run it with one to actually verify the guarantee.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "AGENTWERKE_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Skipped: set {ConnectionStringVariable} to a Postgres connection string to run "
                + "interaction concurrency tests, e.g. "
                + "\"Host=localhost;Port=55432;Database=agentwerke;Username=postgres;Password=postgres\".";
        }
    }
}
