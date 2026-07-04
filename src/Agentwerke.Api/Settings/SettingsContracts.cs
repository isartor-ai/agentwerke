using System.Security.Claims;
using System.Text.Json;

namespace Agentwerke.Api.Settings;

public sealed record SettingsFilePaths(string OverridesPath, string SecretsPath)
{
    private const string DefaultOverridesPath = "config/settings.overrides.json";
    private const string DefaultSecretsPath = "config/settings.secrets.json";

    public static SettingsFilePaths Resolve(IConfiguration configuration, string contentRootPath)
    {
        return new SettingsFilePaths(
            ResolvePath(configuration["Settings:OverridesPath"], DefaultOverridesPath, contentRootPath),
            ResolvePath(configuration["Settings:SecretsPath"], DefaultSecretsPath, contentRootPath));
    }

    private static string ResolvePath(string? configuredPath, string fallback, string contentRootPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(contentRootPath, value));
    }
}

public sealed record SettingsSnapshotResponse(
    string GeneratedAt,
    IReadOnlyList<SettingsCategoryResponse> Categories);

public sealed record SettingsCategoryResponse(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<SettingsFieldResponse> Fields);

public sealed record SettingsFieldResponse(
    string Path,
    string Label,
    string Description,
    string ValueType,
    object? Value,
    bool IsSecret,
    bool IsEditable,
    bool RequiresRestart,
    string Source,
    IReadOnlyList<string> Options,
    SettingsSecretStatusResponse? Secret);

public sealed record SettingsSecretStatusResponse(
    bool Configured,
    string Source,
    string? Fingerprint,
    bool CanWrite);

public sealed record SettingsUpdateRequest(
    IReadOnlyDictionary<string, JsonElement>? Values = null,
    IReadOnlyDictionary<string, string>? Secrets = null);

public sealed record SettingsUpdateResponse(
    SettingsSnapshotResponse Snapshot,
    IReadOnlyList<string> ChangedValues,
    IReadOnlyList<string> RotatedSecrets,
    bool RestartRequired,
    string AuditId);

public sealed record SettingsValidationError(string Path, string Message);

public sealed record SettingsValidationProblemResponse(IReadOnlyList<SettingsValidationError> Errors);

public sealed record SettingsTestResponse(
    string Target,
    bool Succeeded,
    IReadOnlyList<string> Messages,
    string TestedAt,
    string AuditId);

public interface ISettingsService
{
    Task<SettingsSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<SettingsUpdateResponse> UpdateAsync(
        SettingsUpdateRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken);

    Task<SettingsTestResponse> TestTargetAsync(
        string target,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken);
}

public sealed class SettingsValidationException : Exception
{
    public SettingsValidationException(IReadOnlyList<SettingsValidationError> errors)
        : base("Settings validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<SettingsValidationError> Errors { get; }
}
