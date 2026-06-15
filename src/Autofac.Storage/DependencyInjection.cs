using Autofac.Storage.Artifacts;
using Amazon;
using Amazon.S3;
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

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value.S3;
            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
                ForcePathStyle = options.ForcePathStyle,
                ServiceURL = string.IsNullOrWhiteSpace(options.ServiceUrl) ? null : options.ServiceUrl
            };

            return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
        });

        services.AddSingleton<IArtifactStorage>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
            return string.Equals(options.Provider, "s3", StringComparison.OrdinalIgnoreCase)
                ? new S3ArtifactStorage(
                    sp.GetRequiredService<IAmazonS3>(),
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>())
                : new LocalFileArtifactStorage(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>());
        });

        return services;
    }
}
