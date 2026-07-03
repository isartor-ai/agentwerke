namespace Agentwerke.Domain.Persistence;

/// <summary>
/// A runtime-neutral, immutable SDLC template from the built-in catalog.
/// Templates are the primary user-facing entry point: users start from a governed
/// golden path and clone it into an editable workflow draft.
/// </summary>
public sealed class SdlcTemplate
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Intended trigger event, e.g. "manual", "jira_issue_created".</summary>
    public string Trigger { get; init; } = "manual";

    /// <summary>Inputs the initiator must supply when starting a run from this template.</summary>
    public IReadOnlyList<string> RequiredInputs { get; init; } = [];

    /// <summary>Agent role identifiers used in this template's service tasks.</summary>
    public IReadOnlyList<string> AgentRoles { get; init; } = [];

    /// <summary>Human approval roles expected by user tasks in this template.</summary>
    public IReadOnlyList<string> ApprovalRoles { get; init; } = [];

    /// <summary>Evidence keys expected to be produced before or during the run.</summary>
    public IReadOnlyList<string> EvidenceExpectations { get; init; } = [];

    /// <summary>"standard" | "elevated" | "critical"</summary>
    public string PolicyLevel { get; init; } = "standard";

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Validated BPMN XML that defines the workflow graph for this template.</summary>
    public string BpmnXml { get; init; } = string.Empty;
}
