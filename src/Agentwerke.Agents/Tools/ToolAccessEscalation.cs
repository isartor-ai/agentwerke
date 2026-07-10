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

    public static IReadOnlyList<string> Options => [ApproveOption, DenyOption];

    /// <summary>Deterministic per (agent, tool): a step re-run must find the same interaction.</summary>
    public static string BuildPrompt(string agentName, string toolName) =>
        $"Tool access request: agent '{agentName}' needs tool '{toolName}', which is not allowed "
        + "for this step. Reply 'approve' to allow it for the rest of this run, or reply with "
        + "guidance for the agent (treated as a denial).";

    public static bool IsApproved(string? response) =>
        string.Equals(response?.Trim(), ApproveOption, StringComparison.OrdinalIgnoreCase);

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
