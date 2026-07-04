namespace Agentwerke.Integrations.Webhooks;

/// <summary>
/// Finds the workflow that should be started for a given trigger source.
/// Workflows opt in by carrying a tag matching the source name (e.g. "jira-trigger", "github-trigger").
/// </summary>
public interface ITriggerRouter
{
    /// <summary>
    /// Returns the workflow ID to start, or null if no active workflow is configured for this source.
    /// </summary>
    Task<string?> ResolveWorkflowIdAsync(string triggerSource, CancellationToken cancellationToken);
}
