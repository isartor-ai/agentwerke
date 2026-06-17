namespace Autofac.Infrastructure;

public interface ICamundaClient
{
    Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default);

    Task<CamundaDeploymentResponse> DeployWorkflowAsync(
        CamundaDeploymentRequest request,
        CancellationToken cancellationToken = default);

    Task<CamundaProcessStartResponse> StartProcessInstanceAsync(
        CamundaProcessStartRequest request,
        CancellationToken cancellationToken = default);
}
