using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Autofac.Api.Settings;

public interface ISettingsSecretStore
{
    bool CanWrite { get; }

    Task<SettingsSecretStatus> GetStatusAsync(string path, CancellationToken cancellationToken);

    Task SetSecretAsync(string path, string value, CancellationToken cancellationToken);
}

public sealed record SettingsSecretStatus(
    bool Configured,
    string Source,
    string? Fingerprint);

public sealed class SettingsSecretDocument
{
    public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileSettingsSecretStore : ISettingsSecretStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly SettingsFilePaths _paths;

    public FileSettingsSecretStore(
        IConfiguration configuration,
        SettingsFilePaths paths,
        bool canWrite)
    {
        _configuration = configuration;
        _paths = paths;
        CanWrite = canWrite;
    }

    public bool CanWrite { get; }

    public async Task<SettingsSecretStatus> GetStatusAsync(string path, CancellationToken cancellationToken)
    {
        var localSecrets = await ReadAsync(cancellationToken);
        if (localSecrets.Secrets.TryGetValue(path, out var localValue) &&
            !string.IsNullOrWhiteSpace(localValue))
        {
            return new SettingsSecretStatus(true, "settings-secret-file", Fingerprint(localValue));
        }

        var configuredValue = _configuration[path];
        return string.IsNullOrWhiteSpace(configuredValue)
            ? new SettingsSecretStatus(false, "missing", null)
            : new SettingsSecretStatus(true, "configuration", Fingerprint(configuredValue));
    }

    public async Task SetSecretAsync(string path, string value, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            throw new InvalidOperationException("Local secret writes are disabled.");
        }

        var document = await ReadAsync(cancellationToken);
        document.Secrets[path] = value;
        await SaveAsync(document, cancellationToken);
    }

    private async Task<SettingsSecretDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SecretsPath))
        {
            return new SettingsSecretDocument();
        }

        await using var stream = File.OpenRead(_paths.SecretsPath);
        var document = await JsonSerializer.DeserializeAsync<SettingsSecretDocument>(
            stream,
            SerializerOptions,
            cancellationToken) ?? new SettingsSecretDocument();

        document.Secrets = new Dictionary<string, string>(document.Secrets, StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private async Task SaveAsync(SettingsSecretDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.SecretsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_paths.SecretsPath);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
    }

    private static string Fingerprint(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
