using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autofac.Application.Workflows;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Autofac.Workflows.Bpmn;
using Autofac.Workflows.Runtime;
using Microsoft.Extensions.Logging;

namespace Autofac.Infrastructure.Workers;

public interface ICamundaAgentJobExecutor
{
    Task ExecuteAsync(CamundaActivatedJob job, CancellationToken cancellationToken);
}

public sealed class CamundaAgentJobExecutor : ICamundaAgentJobExecutor
{
    private const string RunningStatus = "running";
    private const string CompletedStatus = "completed";
    private const string FailedStatus = "failed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkflowRuntimeStore _runtimeStore;
    private readonly IWorkflowRunRepository _runRepository;
    private readonly IRunContextRepository _runContextRepository;
    private readonly IServiceTaskExecutor _serviceTaskExecutor;
    private readonly ICamundaClient _camundaClient;
    private readonly ILogger<CamundaAgentJobExecutor> _logger;

    public CamundaAgentJobExecutor(
        IWorkflowRuntimeStore runtimeStore,
        IWorkflowRunRepository runRepository,
        IRunContextRepository runContextRepository,
        IServiceTaskExecutor serviceTaskExecutor,
        ICamundaClient camundaClient,
        ILogger<CamundaAgentJobExecutor> logger)
    {
        _runtimeStore = runtimeStore;
        _runRepository = runRepository;
        _runContextRepository = runContextRepository;
        _serviceTaskExecutor = serviceTaskExecutor;
        _camundaClient = camundaClient;
        _logger = logger;
    }

    public async Task ExecuteAsync(CamundaActivatedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var node = BuildNode(job);
        var metadata = node.Metadata
            ?? throw new InvalidOperationException("Camunda job is missing Autofac agent metadata.");
        var runId = ExtractRunId(job.Variables);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["JobKey"] = job.JobKey,
            ["RunId"] = runId,
            ["ElementId"] = node.Id,
            ["AgentId"] = metadata.Agent
        });

        _logger.LogInformation(
            "Executing Camunda job {JobKey} for run {RunId} element {ElementId} agent {AgentId}",
            job.JobKey,
            runId,
            node.Id,
            metadata.Agent);

        await _runRepository.UpdateRunStatusAsync(runId, RunningStatus, cancellationToken);
        await _runRepository.UpdateCurrentStepAsync(runId, node.Id, cancellationToken);
        await _runtimeStore.AppendEventAsync(
            runId,
            "camunda_job_activated",
            Serialize(new
            {
                runId,
                jobKey = job.JobKey,
                elementId = node.Id,
                elementInstanceKey = job.ElementInstanceKey,
                processInstanceKey = job.ProcessInstanceKey,
                agent = metadata.Agent,
                worker = job.Worker,
                remainingRetries = job.Retries
            }),
            cancellationToken);

        var step = await _runtimeStore.CreateStepAsync(
            runId,
            node.Id,
            node.Name,
            node.ElementName,
            metadata.Agent,
            cancellationToken);

        var attempt = await GetNextAttemptAsync(runId, node.Id, cancellationToken);

        await _runtimeStore.AppendEventAsync(
            runId,
            "service_task_attempted",
            Serialize(new
            {
                runId,
                nodeId = node.Id,
                stepId = step.Id,
                attempt,
                agent = metadata.Agent,
                action = metadata.Action,
                environment = metadata.Environment,
                purposeType = metadata.PurposeType,
                policyTag = metadata.PolicyTag,
                requiresEvidence = metadata.RequiresEvidence,
                remainingRetries = job.Retries
            }),
            cancellationToken);

        AgentTaskOutcome outcome;

        try
        {
            outcome = await _serviceTaskExecutor.ExecuteAsync(runId, step.Id, node, attempt, cancellationToken);
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(runId, node, step.Id, attempt, ex.Message, null, null, cancellationToken);
            _logger.LogError(
                ex,
                "Camunda job {JobKey} failed for run {RunId} element {ElementId} agent {AgentId}",
                job.JobKey,
                runId,
                node.Id,
                metadata.Agent);
            throw;
        }

        await RecordOutcomeMetadataAsync(runId, node, step.Id, attempt, outcome, cancellationToken);

        if (!outcome.Succeeded)
        {
            var reason = outcome.FailureReason ?? "execution_error";
            await RecordFailureAsync(
                runId,
                node,
                step.Id,
                attempt,
                reason,
                outcome.PolicyDecision,
                outcome.RuntimeSnapshot,
                cancellationToken);
            _logger.LogWarning(
                "Camunda job {JobKey} failed for run {RunId} element {ElementId} agent {AgentId}: {Reason}",
                job.JobKey,
                runId,
                node.Id,
                metadata.Agent,
                reason);
            throw new InvalidOperationException(reason);
        }

        if (!string.IsNullOrWhiteSpace(outcome.Output))
        {
            await _runtimeStore.AppendEventAsync(
                runId,
                "agent_output_recorded",
                Serialize(new
                {
                    runId,
                    nodeId = node.Id,
                    stepId = step.Id,
                    agent = metadata.Agent,
                    outputLength = outcome.Output.Length
                }),
                cancellationToken);

            await _runContextRepository.SetAsync(
                runId,
                $"output.{node.Id}",
                outcome.Output,
                RunContextKinds.Output,
                cancellationToken);
        }

        var completionVariables = BuildCompletionVariables(job, runId, node, metadata.Agent, outcome.Output, attempt);
        await _camundaClient.CompleteJobAsync(
            job.JobKey,
            new CamundaJobCompletionRequest(completionVariables),
            cancellationToken);

        await _runtimeStore.AppendEventAsync(
            runId,
            "node_completed",
            Serialize(new { runId, nodeId = node.Id, nodeType = node.ElementName }),
            cancellationToken);
        await _runtimeStore.AppendEventAsync(
            runId,
            "camunda_job_completed",
            Serialize(new
            {
                runId,
                jobKey = job.JobKey,
                nodeId = node.Id,
                stepId = step.Id,
                attempt
            }),
            cancellationToken);

        await _runtimeStore.UpdateStepStatusAsync(
            step.Id,
            CompletedStatus,
            outcome.Output,
            null,
            DateTimeOffset.UtcNow.ToString("o"),
            outcome.PolicyDecision,
            outcome.RuntimeSnapshot,
            cancellationToken);
        await _runRepository.UpdateCurrentStepAsync(runId, null, cancellationToken);

        _logger.LogInformation(
            "Completed Camunda job {JobKey} for run {RunId} element {ElementId} agent {AgentId}",
            job.JobKey,
            runId,
            node.Id,
            metadata.Agent);
    }

    private async Task<int> GetNextAttemptAsync(string runId, string elementId, CancellationToken cancellationToken)
    {
        var key = $"camunda.attempt.{elementId}";
        var current = (await _runContextRepository.GetAllAsync(runId, cancellationToken))
            .LastOrDefault(entry => string.Equals(entry.Key, key, StringComparison.Ordinal));

        var attempt = current is not null &&
            int.TryParse(current.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed + 1
            : 1;

        await _runContextRepository.SetAsync(
            runId,
            key,
            attempt.ToString(CultureInfo.InvariantCulture),
            RunContextKinds.Runtime,
            cancellationToken);

        return attempt;
    }

    private async Task RecordOutcomeMetadataAsync(
        string runId,
        BpmnNodeDefinition node,
        string stepId,
        int attempt,
        AgentTaskOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome.PolicyDecision is not null)
        {
            await _runtimeStore.AppendEventAsync(
                runId,
                "policy_decision_recorded",
                Serialize(new
                {
                    runId,
                    nodeId = node.Id,
                    stepId,
                    kind = outcome.PolicyDecision.Kind,
                    policyId = outcome.PolicyDecision.PolicyId,
                    policyName = outcome.PolicyDecision.PolicyName,
                    rationale = outcome.PolicyDecision.Rationale,
                    riskScore = outcome.PolicyDecision.RiskScore,
                    riskLevel = outcome.PolicyDecision.RiskLevel,
                    constraints = outcome.PolicyDecision.Constraints
                }),
                cancellationToken);
        }

        foreach (var action in outcome.ExternalActions ?? [])
        {
            await _runtimeStore.AppendEventAsync(
                runId,
                "external_action_recorded",
                Serialize(new
                {
                    runId,
                    nodeId = node.Id,
                    stepId,
                    provider = action.Provider,
                    action = action.Action,
                    status = action.Status,
                    resourceId = action.ResourceId,
                    resourceUrl = action.ResourceUrl,
                    summary = action.Summary,
                    attempt
                }),
                cancellationToken);
        }
    }

    private async Task RecordFailureAsync(
        string runId,
        BpmnNodeDefinition node,
        string stepId,
        int attempt,
        string reason,
        PolicyDecision? policyDecision,
        AgentRuntimeSnapshot? runtimeSnapshot,
        CancellationToken cancellationToken)
    {
        await _runtimeStore.AppendEventAsync(
            runId,
            "service_task_failed",
            Serialize(new
            {
                runId,
                nodeId = node.Id,
                stepId,
                attempt,
                reason
            }),
            cancellationToken);
        await _runtimeStore.AppendEventAsync(
            runId,
            "camunda_job_execution_failed",
            Serialize(new
            {
                runId,
                nodeId = node.Id,
                stepId,
                attempt,
                reason
            }),
            cancellationToken);

        await _runtimeStore.UpdateStepStatusAsync(
            stepId,
            FailedStatus,
            null,
            reason,
            DateTimeOffset.UtcNow.ToString("o"),
            policyDecision,
            runtimeSnapshot,
            cancellationToken);
        await _runRepository.UpdateCurrentStepAsync(runId, null, cancellationToken);
    }

    private static BpmnNodeDefinition BuildNode(CamundaActivatedJob job)
    {
        var elementId = GetHeaderOrFallback(job.CustomHeaders, "autofac.elementId", job.ElementId);
        var metadata = new AutofacTaskMetadata(
            Agent: GetRequiredHeader(job.CustomHeaders, "autofac.agent"),
            Action: GetRequiredHeader(job.CustomHeaders, "autofac.action"),
            Environment: GetOptionalHeader(job.CustomHeaders, "autofac.environment"),
            PurposeType: GetRequiredHeader(job.CustomHeaders, "autofac.purposeType"),
            PolicyTag: GetRequiredHeader(job.CustomHeaders, "autofac.policyTag"),
            RequiresEvidence: ParseStringList(job.CustomHeaders, "autofac.requiresEvidence"),
            RetryBackoffSeconds: ParseInt(job.CustomHeaders, "autofac.retryBackoffSeconds"),
            FailUntilAttempt: ParseInt(job.CustomHeaders, "autofac.failUntilAttempt"),
            SimulateTimeout: ParseBool(job.CustomHeaders, "autofac.simulateTimeout"),
            TimeoutSeconds: ParseNullableInt(job.CustomHeaders, "autofac.timeoutSeconds"));

        return new BpmnNodeDefinition(
            Id: elementId,
            Name: elementId,
            ElementName: "serviceTask",
            Metadata: metadata);
    }

    private static string ExtractRunId(JsonElement variables)
    {
        if (variables.ValueKind != JsonValueKind.Object ||
            !variables.TryGetProperty("autofac", out var autofac) ||
            autofac.ValueKind != JsonValueKind.Object ||
            !autofac.TryGetProperty("runId", out var runIdElement))
        {
            throw new InvalidOperationException("Camunda job variables are missing autofac.runId.");
        }

        var runId = runIdElement.GetString();
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Camunda job variables are missing autofac.runId.");
        }

        return runId;
    }

    private static JsonElement BuildCompletionVariables(
        CamundaActivatedJob job,
        string runId,
        BpmnNodeDefinition node,
        string agentId,
        string? output,
        int attempt)
    {
        var existingVariables = job.Variables.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(job.Variables.GetRawText()) as JsonObject
            : null;

        var autofacObject = existingVariables?["autofac"] as JsonObject is { } existingAutofac
            ? (JsonObject)existingAutofac.DeepClone()
            : new JsonObject();
        autofacObject["runId"] = runId;
        autofacObject["lastJobKey"] = job.JobKey;
        autofacObject["lastElementId"] = node.Id;
        autofacObject["lastAgent"] = agentId;
        autofacObject["lastAttempt"] = attempt;

        var result = new JsonObject
        {
            ["autofac"] = autofacObject
        };

        if (!string.IsNullOrWhiteSpace(output))
        {
            var outputObject = existingVariables?["output"] as JsonObject is { } existingOutput
                ? (JsonObject)existingOutput.DeepClone()
                : new JsonObject();
            outputObject[node.Id] = output;
            result["output"] = outputObject;
        }

        using var document = JsonDocument.Parse(result.ToJsonString());
        return document.RootElement.Clone();
    }

    private static string GetRequiredHeader(IReadOnlyDictionary<string, string> headers, string key)
    {
        if (!headers.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Camunda job is missing required header '{key}'.");
        }

        return value;
    }

    private static string GetHeaderOrFallback(IReadOnlyDictionary<string, string> headers, string key, string fallback)
    {
        return headers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string? GetOptionalHeader(IReadOnlyDictionary<string, string> headers, string key)
    {
        return headers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static IReadOnlyList<string> ParseStringList(IReadOnlyDictionary<string, string> headers, string key)
    {
        if (!headers.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> headers, string key)
    {
        var value = GetOptionalHeader(headers, key);
        return value is not null
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : 0;
    }

    private static int? ParseNullableInt(IReadOnlyDictionary<string, string> headers, string key)
    {
        var value = GetOptionalHeader(headers, key);
        return value is not null
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : null;
    }

    private static bool ParseBool(IReadOnlyDictionary<string, string> headers, string key)
    {
        var value = GetOptionalHeader(headers, key);
        return value is not null && bool.Parse(value);
    }

    private static string Serialize<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }
}
