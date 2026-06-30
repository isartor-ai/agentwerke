using System.Text.Json;

namespace Autofac.Api.Settings;

public interface ISettingsOverrideStore
{
    Task<SettingsOverrideDocument> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(SettingsOverrideDocument document, CancellationToken cancellationToken);
}

public sealed class SettingsOverrideDocument
{
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FileSettingsOverrideStore : ISettingsOverrideStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SettingsFilePaths _paths;

    public FileSettingsOverrideStore(SettingsFilePaths paths)
    {
        _paths = paths;
    }

    public async Task<SettingsOverrideDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.OverridesPath))
        {
            return new SettingsOverrideDocument();
        }

        await using var stream = File.OpenRead(_paths.OverridesPath);
        var document = await JsonSerializer.DeserializeAsync<SettingsOverrideDocument>(
            stream,
            SerializerOptions,
            cancellationToken) ?? new SettingsOverrideDocument();

        document.Values = new Dictionary<string, JsonElement>(
            document.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        return document;
    }

    public async Task SaveAsync(SettingsOverrideDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.OverridesPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_paths.OverridesPath);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
    }
}
