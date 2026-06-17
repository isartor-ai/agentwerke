using System.Net;
using Autofac.Application.Workflows;
using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure.Workflows;

public sealed class CamundaWorkflowProcessStartService : IWorkflowProcessStartService
{
    private readonly ICamundaClient _camundaClient;
    private readonly IOptions<CamundaOptions> _options;

    public CamundaWorkflowProcessStartService(
        ICamundaClient camundaClient,
        IOptions<CamundaOptions> options)
    {
        _camundaClient = camundaClient;
        _options = options;
    }

    public async Task<WorkflowProcessStartResult> StartAsync(
        WorkflowProcessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Value.IsConfigured)
        {
            throw new WorkflowRunStartException(
                "Run start failed.",
                [
                    new WorkflowRunStartError(
                        "camunda_not_configured",
                        "Camunda runtime is disabled or not fully configured.")
                ]);
        }

        try
        {
            var response = await _camundaClient.StartProcessInstanceAsync(
                new CamundaProcessStartRequest(
                    request.ProcessDefinitionKey,
                    request.Variables),
                cancellationToken);

            return new WorkflowProcessStartResult(
                response.ProcessInstanceKey,
                response.ProcessDefinitionKey,
                response.ProcessDefinitionId,
                response.ProcessDefinitionVersion);
        }
        catch (CamundaApiException ex)
        {
            throw new WorkflowRunStartException(
                "Run start failed.",
                BuildErrors(ex));
        }
        catch (HttpRequestException ex)
        {
            throw new WorkflowRunStartException(
                "Run start failed.",
                [
                    new WorkflowRunStartError(
                        "camunda_unreachable",
                        $"Camunda process start endpoint could not be reached: {ex.Message}")
                ]);
        }
        catch (TaskCanceledException ex)
        {
            throw new WorkflowRunStartException(
                "Run start failed.",
                [
                    new WorkflowRunStartError(
                        "camunda_timeout",
                        $"Camunda process start timed out: {ex.Message}")
                ]);
        }
    }

    private static IReadOnlyList<WorkflowRunStartError> BuildErrors(CamundaApiException ex)
    {
        var code = ex.StatusCode switch
        {
            HttpStatusCode.BadRequest => "camunda_bad_request",
            HttpStatusCode.Conflict => "camunda_conflict",
            HttpStatusCode.ServiceUnavailable => "camunda_unavailable",
            HttpStatusCode.GatewayTimeout => "camunda_gateway_timeout",
            _ => "camunda_request_failed"
        };

        var errors = new List<WorkflowRunStartError>
        {
            new(
                code,
                !string.IsNullOrWhiteSpace(ex.ProblemDetails?.Detail)
                    ? ex.ProblemDetails.Detail
                    : ex.Message)
        };

        if (!string.IsNullOrWhiteSpace(ex.ProblemDetails?.Title) &&
            !string.Equals(ex.ProblemDetails.Title, errors[0].Message, StringComparison.Ordinal))
        {
            errors.Insert(0, new WorkflowRunStartError(code, ex.ProblemDetails.Title));
        }

        return errors;
    }
}
