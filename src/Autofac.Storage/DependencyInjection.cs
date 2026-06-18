using Autofac.Storage.Artifacts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac.Storage;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(
            configuration.GetSection(StorageOptions.SectionName));

        services.AddSingleton<IArtifactStorage, LocalFileArtifactStorage>();

        return services;
    }
}
