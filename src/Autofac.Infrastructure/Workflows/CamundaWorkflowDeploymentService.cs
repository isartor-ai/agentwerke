using System.Net;
using Autofac.Application.Workflows;
using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure.Workflows;

public sealed class CamundaWorkflowDeploymentService : IWorkflowDeploymentService
{
    private readonly ICamundaClient _camundaClient;
    private readonly IOptions<CamundaOptions> _options;

    public CamundaWorkflowDeploymentService(
        ICamundaClient camundaClient,
        IOptions<CamundaOptions> options)
    {
        _camundaClient = camundaClient;
        _options = options;
    }

    public async Task<WorkflowDeploymentResult> DeployAsync(
        WorkflowDeploymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        if (!options.IsConfigured)
        {
            throw new WorkflowDeploymentException(
                "Workflow deployment failed.",
                [
                    new WorkflowDeploymentError(
                        "camunda_not_configured",
                        "Camunda runtime is disabled or not fully configured.")
                ]);
        }

        try
        {
            var response = await _camundaClient.DeployWorkflowAsync(
                new CamundaDeploymentRequest(request.ResourceName, request.ProjectedBpmnXml),
                cancellationToken);

            var processDefinition = response.Deployments
                .Select(item => item.ProcessDefinition)
                .FirstOrDefault(process =>
                    process is not null &&
                    string.Equals(process.ProcessDefinitionId, request.ProcessDefinitionId, StringComparison.Ordinal))
                ?? response.Deployments
                    .Select(item => item.ProcessDefinition)
                    .FirstOrDefault(process => process is not null);

            if (processDefinition is null)
            {
                throw new WorkflowDeploymentException(
                    "Workflow deployment failed.",
                    [
                        new WorkflowDeploymentError(
                            "camunda_missing_process_definition",
                            "Camunda deployment succeeded but did not return a deployed process definition.")
                    ]);
            }

            return new WorkflowDeploymentResult(
                response.DeploymentKey,
                processDefinition.ProcessDefinitionId,
                processDefinition.ProcessDefinitionKey,
                processDefinition.ProcessDefinitionVersion,
                DateTimeOffset.UtcNow.ToString("o"));
        }
        catch (CamundaApiException ex)
        {
            throw new WorkflowDeploymentException(
                "Workflow deployment failed.",
                BuildErrors(ex));
        }
        catch (HttpRequestException ex)
        {
            throw new WorkflowDeploymentException(
                "Workflow deployment failed.",
                [
                    new WorkflowDeploymentError(
                        "camunda_unreachable",
                        $"Camunda deployment endpoint could not be reached: {ex.Message}")
                ]);
        }
        catch (TaskCanceledException ex)
        {
            throw new WorkflowDeploymentException(
                "Workflow deployment failed.",
                [
                    new WorkflowDeploymentError(
                        "camunda_timeout",
                        $"Camunda deployment timed out: {ex.Message}")
                ]);
        }
    }

    private static IReadOnlyList<WorkflowDeploymentError> BuildErrors(CamundaApiException ex)
    {
        var code = ex.StatusCode switch
        {
            HttpStatusCode.BadRequest => "camunda_bad_request",
            HttpStatusCode.ServiceUnavailable => "camunda_unavailable",
            _ => "camunda_request_failed"
        };

        var details = new List<WorkflowDeploymentError>
        {
            new(
                code,
                !string.IsNullOrWhiteSpace(ex.ProblemDetails?.Detail)
                    ? ex.ProblemDetails.Detail
                    : ex.Message)
        };

        if (!string.IsNullOrWhiteSpace(ex.ProblemDetails?.Title) &&
            !string.Equals(ex.ProblemDetails.Title, details[0].Message, StringComparison.Ordinal))
        {
            details.Insert(0, new WorkflowDeploymentError(code, ex.ProblemDetails.Title));
        }

        return details;
    }
}
