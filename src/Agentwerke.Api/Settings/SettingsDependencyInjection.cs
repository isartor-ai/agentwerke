using Microsoft.Extensions.Configuration.Json;

namespace Agentwerke.Api.Settings;

public static class SettingsDependencyInjection
{
    public static SettingsFilePaths AddAgentwerkeSettingsConfiguration(
        this ConfigurationManager configuration,
        IWebHostEnvironment environment)
    {
        var paths = SettingsFilePaths.Resolve(configuration, environment.ContentRootPath);
        AddJsonFileIfPresent(configuration, paths.OverridesPath);
        AddJsonFileIfPresent(configuration, paths.SecretsPath);
        return paths;
    }

    public static IServiceCollection AddAgentwerkeSettings(
        this IServiceCollection services,
        IConfiguration configuration,
        SettingsFilePaths filePaths)
    {
        services.AddSingleton(filePaths);
        services.AddSingleton(SettingsCatalog.Default);
        services.AddSingleton<ISettingsOverrideStore, FileSettingsOverrideStore>();
        services.AddSingleton<ISettingsSecretStore>(_ =>
            new FileSettingsSecretStore(
                configuration,
                filePaths,
                ResolveBool(configuration["Settings:AllowLocalSecretWrites"], defaultValue: true)));
        services.AddScoped<ISettingsService, SettingsService>();
        return services;
    }

    private static void AddJsonFileIfPresent(ConfigurationManager configuration, string path)
    {
        configuration.Sources.Add(new JsonConfigurationSource
        {
            Path = path,
            Optional = true,
            ReloadOnChange = false
        });
    }

    private static bool ResolveBool(string? value, bool defaultValue)
        => bool.TryParse(value, out var parsed) ? parsed : defaultValue;
}
