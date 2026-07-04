using Agentwerke.Storage.Artifacts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agentwerke.Storage;

public static class DependencyInjection
{
    public static IServiceCollection AddAgentwerkeStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionName));

        services.AddSingleton<IArtifactStorage, LocalFileArtifactStorage>();

        return services;
    }
}
