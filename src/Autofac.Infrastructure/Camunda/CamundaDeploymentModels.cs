using System.Net;
using System.Text.Json;

namespace Autofac.Infrastructure;

public sealed record CamundaDeploymentRequest(
    string ResourceName,
    string BpmnXml);

public sealed class CamundaDeploymentResponse
{
    public string DeploymentKey { get; set; } = string.Empty;

    public List<CamundaDeploymentItem> Deployments { get; set; } = [];

    public string TenantId { get; set; } = string.Empty;
}

public sealed record CamundaProcessStartRequest(
    string ProcessDefinitionKey,
    JsonElement Variables);

public sealed class CamundaProcessStartResponse
{
    public string ProcessDefinitionKey { get; set; } = string.Empty;

    public string ProcessDefinitionId { get; set; } = string.Empty;

    public int ProcessDefinitionVersion { get; set; }

    public string ProcessInstanceKey { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public JsonElement Variables { get; set; }
}

public sealed class CamundaDeploymentItem
{
    public CamundaProcessDeployment? ProcessDefinition { get; set; }
}

public sealed class CamundaProcessDeployment
{
    public string ProcessDefinitionId { get; set; } = string.Empty;

    public int ProcessDefinitionVersion { get; set; }

    public string ProcessDefinitionKey { get; set; } = string.Empty;

    public string ResourceName { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;
}

public sealed class CamundaProblemDetails
{
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Status { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string Instance { get; set; } = string.Empty;
}

public sealed class CamundaApiException : Exception
{
    public CamundaApiException(
        string message,
        HttpStatusCode statusCode,
        CamundaProblemDetails? problemDetails = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProblemDetails = problemDetails;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public CamundaProblemDetails? ProblemDetails { get; }

    public string? ResponseBody { get; }
}
