using System.Text.Json;

namespace Autofac.Infrastructure;

public sealed record CamundaJobActivationRequest(
    string Type,
    string Worker,
    int Timeout,
    int MaxJobsToActivate,
    IReadOnlyList<string>? FetchVariables = null);

public sealed class CamundaActivatedJob
{
    public string JobKey { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string ProcessInstanceKey { get; set; } = string.Empty;

    public string ProcessDefinitionId { get; set; } = string.Empty;

    public int ProcessDefinitionVersion { get; set; }

    public string ProcessDefinitionKey { get; set; } = string.Empty;

    public string ElementId { get; set; } = string.Empty;

    public string ElementInstanceKey { get; set; } = string.Empty;

    public Dictionary<string, string> CustomHeaders { get; set; } = new(StringComparer.Ordinal);

    public string Worker { get; set; } = string.Empty;

    public int Retries { get; set; }

    public string Deadline { get; set; } = string.Empty;

    public JsonElement Variables { get; set; }

    public string TenantId { get; set; } = string.Empty;
}

public sealed record CamundaJobCompletionRequest(
    JsonElement Variables);
