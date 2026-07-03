using Agentwerke.Application.Workflows;

namespace Agentwerke.Integrations.Webhooks;

/// <summary>
/// Resolves a workflow to trigger by scanning active workflows for a tag that matches
/// the trigger source (e.g. tag "jira-trigger" maps to source "jira").
/// When multiple workflows match, the most recently edited one wins.
/// </summary>
public sealed class TagBasedTriggerRouter : ITriggerRouter
{
    private readonly IWorkflowDefinitionRepository _definitions;

    public TagBasedTriggerRouter(IWorkflowDefinitionRepository definitions)
    {
        _definitions = definitions;
    }

    public async Task<string?> ResolveWorkflowIdAsync(string triggerSource, CancellationToken cancellationToken)
    {
        var tag = $"{triggerSource}-trigger";
        var all = await _definitions.ListAsync(cancellationToken);

        return all
            .Where(w => string.Equals(w.Status, "active", StringComparison.OrdinalIgnoreCase)
                && w.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(w => w.LastEditedAt)
            .Select(w => w.Id)
            .FirstOrDefault();
    }
}
