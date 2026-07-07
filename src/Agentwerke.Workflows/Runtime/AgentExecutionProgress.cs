namespace Agentwerke.Workflows.Runtime;

public delegate Task AgentExecutionProgressReporter(
    AgentExecutionProgressUpdate update,
    CancellationToken cancellationToken);

public sealed record AgentExecutionProgressUpdate(
    string Kind,
    string Summary,
    string? ToolName = null,
    string? ToolCallId = null,
    string? Status = null,
    // Concise, redacted one-liner about the concrete action — the file edited, the command run,
    // the PR opened — so the run timeline reads like a live activity log, not just tool names.
    string? Detail = null);

public static class AgentExecutionProgressKinds
{
    public const string Reasoning = "reasoning";
    public const string ToolStarted = "tool_started";
    public const string ToolFinished = "tool_finished";
}
