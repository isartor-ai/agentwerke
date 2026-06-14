using System.Reflection;

namespace Autofac.E2ETests;

/// <summary>
/// Base class that resolves the API URL from the environment variable set by docker-compose.e2e.yml
/// and waits for the API to be ready before any test runs.
/// </summary>
public abstract class E2ETestBase
{
    protected static readonly string ApiBaseUrl =
        Environment.GetEnvironmentVariable("AUTOFAC_API_URL") ?? "http://localhost:8081";

    protected ApiClient Api { get; } = new(ApiBaseUrl);

    protected static string LoadFixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
