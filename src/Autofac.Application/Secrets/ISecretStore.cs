namespace Autofac.Application.Secrets;

public interface ISecretStore
{
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
}
