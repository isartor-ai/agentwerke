namespace Autofac.Infrastructure;

public interface ICamundaClient
{
    Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default);
}
