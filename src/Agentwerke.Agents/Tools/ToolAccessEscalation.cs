using System.Text.Json;

namespace Agentwerke.Agents.Tools;

/// <summary>
/// Shared vocabulary for the tool-access escalation flow (#202): when an agent calls a tool
/// that exists but is not allowed for its step, the run pauses on a blocking
/// <c>tool_access</c> interaction instead of failing. The prompt is built deterministically
/// from (agent, tool) so a re-run of the step finds the operator's answer for the same request.
/// Hallucinated tool names (tools that do not exist at all) are NOT escalated — those get a
/// self-correction error listing the available tools.
/// </summary>
public static class ToolAccessEscalation
{
    public const string ApproveOption = "approve";

    public const string DenyOption = "deny";

    /// <summary>Operator chose to fail the step outright instead of approving or coaching the agent.</summary>
    public const string FailOption = "fail";

    /// <summary>Escalate to a human when a tool call is contract-denied (default).</summary>
    public const string EscalateMode = "escalate";

    /// <summary>Fail the tool call immediately without human escalation (pre-#202 behavior).</summary>
    public const string FailFastMode = "fail";

    private const int IntentMaxLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Options => [ApproveOption, DenyOption, FailOption];

    public static IReadOnlyList<string> Modes => [EscalateMode, FailFastMode];

    /// <summary>Deterministic per (agent, tool): a step re-run must find the same interaction.</summary>
    public static string BuildPrompt(string agentName, string toolName) =>
        $"Tool access request: agent '{agentName}' needs tool '{toolName}', which is not allowed "
        + "for this step. Reply 'approve' to allow it for the rest of this run, 'fail' to fail "
        + "the step, or reply with guidance for the agent (treated as a denial).";

    /// <summary>The model's stated intent: its tool input, serialized and truncated for operators.</summary>
    public static string BuildIntent(IReadOnlyDictionary<string, string> input)
    {
        if (input.Count == 0)
        {
            return "(no tool input provided)";
        }

        var json = JsonSerializer.Serialize(input, SerializerOptions);
        return json.Length <= IntentMaxLength ? json : json[..IntentMaxLength] + "…";
    }

    public static bool IsApproved(string? response) =>
        string.Equals(response?.Trim(), ApproveOption, StringComparison.OrdinalIgnoreCase);

    public static bool IsStepFailure(string? response) =>
        string.Equals(response?.Trim(), FailOption, StringComparison.OrdinalIgnoreCase);

    public static bool IsFailFastMode(string? mode) =>
        string.Equals(mode?.Trim(), FailFastMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>The tool result the model sees when the operator declined the request.</summary>
    public static string BuildDenialResult(string toolName, string? response)
    {
        var guidance = string.IsNullOrWhiteSpace(response) || IsDeny(response)
            ? "Proceed without it and note the limitation in your final answer."
            : response.Trim();
        return $"An operator declined access to tool '{toolName}'. Operator guidance: {guidance}";
    }

    private static bool IsDeny(string response) =>
        string.Equals(response.Trim(), DenyOption, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Raised when an operator answered a tool-access escalation with 'fail' (#202): the step must
/// fail instead of feeding a denial back into the model loop.
/// </summary>
public sealed class ToolAccessStepFailedException : Exception
{
    public ToolAccessStepFailedException(string toolName, string? respondedBy)
        : base($"An operator failed this step: access to tool '{toolName}' was refused"
               + (string.IsNullOrWhiteSpace(respondedBy) ? "." : $" by {respondedBy}."))
    {
        ToolName = toolName;
        RespondedBy = respondedBy;
    }

    public string ToolName { get; }

    public string? RespondedBy { get; }
}
