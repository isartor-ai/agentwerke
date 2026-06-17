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

    Task<IReadOnlyList<CamundaActivatedJob>> ActivateJobsAsync(
        CamundaJobActivationRequest request,
        CancellationToken cancellationToken = default);

    Task CompleteJobAsync(
        string jobKey,
        CamundaJobCompletionRequest request,
        CancellationToken cancellationToken = default);
}
