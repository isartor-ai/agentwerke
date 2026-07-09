using Agentwerke.Domain.Persistence;

namespace Agentwerke.Api.Controllers;

internal sealed class RunEventStreamState(string runId)
{
    internal const int PollIntervalMs = 1_000;
    internal const int HeartbeatEveryPolls = 15;

    private int _deliveredCount;

    public IQueryable<WorkflowEvent> BuildFreshEventsQuery(IQueryable<WorkflowEvent> workflowEvents) =>
        workflowEvents
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Skip(_deliveredCount);

    public void MarkDelivered(IReadOnlyCollection<WorkflowEvent> delivered) =>
        _deliveredCount += delivered.Count;

    public static bool ShouldCheckRunStatus(int freshEventCount) => freshEventCount == 0;
}
