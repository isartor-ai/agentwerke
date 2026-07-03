using Agentwerke.Api.Settings;
using Agentwerke.Application.Observability;
using Agentwerke.Domain.Persistence;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Text.Json;

namespace Agentwerke.Api.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"agentwerke-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetSnapshotAsync_RedactsSecretValues()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Anthropic:Provider"] = "anthropic",
            ["Anthropic:ApiKey"] = "sk-live-super-secret",
            ["Integrations:GitHub:PersonalAccessToken"] = "ghp_super_secret"
        });

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        var serialized = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("sk-live-super-secret", serialized);
        Assert.DoesNotContain("ghp_super_secret", serialized);

        var apiKey = snapshot.Categories
            .SelectMany(category => category.Fields)
            .Single(field => field.Path == "Anthropic:ApiKey");
        Assert.True(apiKey.IsSecret);
        Assert.Null(apiKey.Value);
        Assert.NotNull(apiKey.Secret);
        Assert.True(apiKey.Secret!.Configured);
        Assert.Equal("configuration", apiKey.Secret.Source);
        Assert.False(string.IsNullOrWhiteSpace(apiKey.Secret.Fingerprint));
    }

    [Fact]
    public async Task UpdateAsync_RejectsSecretsInValuePayload()
    {
        var service = CreateService();
        var request = DeserializeRequest("""
        {
          "values": {
            "Anthropic:ApiKey": "must-not-be-here"
          }
        }
        """);

        var exception = await Assert.ThrowsAsync<SettingsValidationException>(() =>
            service.UpdateAsync(request, AdminPrincipal(), CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Path == "Anthropic:ApiKey");
    }

    [Fact]
    public async Task UpdateAsync_PersistsOverridesAndAuditsWithoutRawSecrets()
    {
        var audit = new CapturingAuditRepository();
        var paths = CreatePaths();
        var service = CreateService(
            initialValues: new Dictionary<string, string?> { ["Anthropic:Provider"] = "anthropic" },
            paths: paths,
            audit: audit);
        var request = DeserializeRequest("""
        {
          "values": {
            "Anthropic:Provider": "mock",
            "Integrations:GitHub:Enabled": true
          },
          "secrets": {
            "Anthropic:ApiKey": "new-api-key"
          }
        }
        """);

        var response = await service.UpdateAsync(request, AdminPrincipal(), CancellationToken.None);

        Assert.Contains("Anthropic:Provider", response.ChangedValues);
        Assert.Contains("Integrations:GitHub:Enabled", response.ChangedValues);
        Assert.Contains("Anthropic:ApiKey", response.RotatedSecrets);
        Assert.DoesNotContain("new-api-key", JsonSerializer.Serialize(response));

        var overrides = await File.ReadAllTextAsync(paths.OverridesPath);
        Assert.Contains("Anthropic:Provider", overrides);
        Assert.Contains("mock", overrides);

        var secrets = await File.ReadAllTextAsync(paths.SecretsPath);
        Assert.Contains("Anthropic:ApiKey", secrets);

        var auditRecord = Assert.Single(audit.Records);
        Assert.Equal("settings.update", auditRecord.Action);
        Assert.Equal("admin@example.com", auditRecord.Actor);
        Assert.Contains("Anthropic:ApiKey", auditRecord.Details);
        Assert.DoesNotContain("new-api-key", auditRecord.Details);
    }

    [Fact]
    public async Task TestTargetAsync_ReturnsDryRunReadinessForGitHub()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Integrations:GitHub:Enabled"] = "true",
            ["Integrations:GitHub:RepositoryOwner"] = "isartor-ai",
            ["Integrations:GitHub:RepositoryName"] = "agentwerke"
        });

        var result = await service.TestTargetAsync("github", AdminPrincipal(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Messages, message => message.Contains("PersonalAccessToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ghp_", JsonSerializer.Serialize(result));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private SettingsService CreateService(
        Dictionary<string, string?>? initialValues = null,
        SettingsFilePaths? paths = null,
        CapturingAuditRepository? audit = null)
    {
        Directory.CreateDirectory(_tempDirectory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(initialValues ?? new Dictionary<string, string?>())
            .Build();

        var resolvedPaths = paths ?? CreatePaths();
        var store = new FileSettingsOverrideStore(resolvedPaths);
        var secretStore = new FileSettingsSecretStore(configuration, resolvedPaths, canWrite: true);

        return new SettingsService(
            configuration,
            SettingsCatalog.Default,
            store,
            secretStore,
            audit ?? new CapturingAuditRepository(),
            new StaticCorrelationContext("correlation-test"));
    }

    private SettingsFilePaths CreatePaths()
    {
        Directory.CreateDirectory(_tempDirectory);
        return new SettingsFilePaths(
            Path.Combine(_tempDirectory, "settings.overrides.json"),
            Path.Combine(_tempDirectory, "settings.secrets.json"));
    }

    private static SettingsUpdateRequest DeserializeRequest(string json)
        => JsonSerializer.Deserialize<SettingsUpdateRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static ClaimsPrincipal AdminPrincipal()
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "admin@example.com"),
                new Claim(ClaimTypes.Name, "Admin User"),
                new Claim(ClaimTypes.Role, "Admin")
            ],
            "test"));

    private sealed class CapturingAuditRepository : IAuditRepository
    {
        public List<AuditRecord> Records { get; } = [];

        public Task AddAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; } = correlationId;
    }
}
