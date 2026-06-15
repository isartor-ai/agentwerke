using Autofac.Application.Secrets;
using Microsoft.Extensions.Configuration;

namespace Autofac.Infrastructure.Secrets;

public sealed class ConfigurationSecretStore : ISecretStore
{
    private readonly IConfiguration _configuration;

    public ConfigurationSecretStore(IConfiguration configuration) => _configuration = configuration;

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_configuration[key]);
}
