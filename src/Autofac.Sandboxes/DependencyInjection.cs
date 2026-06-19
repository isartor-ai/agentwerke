using Docker.DotNet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace Autofac.Sandboxes;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofacSandboxes(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SandboxOptions>(o =>
            configuration.GetSection(SandboxOptions.Section).Bind(o));

        services.AddSingleton<IDockerClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SandboxOptions>>().Value;
            var uri = new Uri(opts.DockerEndpoint);
            return new DockerClientConfiguration(uri).CreateClient();
        });

        services.AddSingleton(_ => new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        });

        services.AddScoped<IOpenSandboxClient, OpenSandboxApiClient>();
        services.AddScoped<OpenSandboxRequestMapper>();
        services.AddScoped<ISandboxProviderExecutor, DockerSandboxExecutor>();
        services.AddScoped<ISandboxProviderExecutor, OpenSandboxSandboxExecutor>();
        services.AddScoped<ISandboxProviderExecutor, KubernetesKataSandboxExecutor>();
        services.AddScoped<ConfiguredSandboxExecutor>();
        services.AddScoped<ISandboxExecutor>(sp => sp.GetRequiredService<ConfiguredSandboxExecutor>());

        return services;
    }
}
