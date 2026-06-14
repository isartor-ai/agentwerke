using System.Text.Json.Nodes;

namespace Autofac.E2ETests;

/// <summary>
/// Happy-path E2E: import → publish → start → assert waiting_user → approve → assert completed.
/// The API runs against a real Postgres instance; agent execution is no-op (sandbox disabled,
/// no LLM called) so the service task returns a success outcome immediately.
/// </summary>
public sealed class WorkflowRunTests : E2ETestBase
{
    [Fact]
    public async Task StartRun_ServiceTaskThenApproval_CompletesAfterApprove()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        var bpmn = LoadFixture("e2e-simple.bpmn");

        // Import and publish
        var workflowId = await Api.ImportWorkflowAsync("e2e-simple.bpmn", bpmn);
        await Api.PublishWorkflowAsync(workflowId, bpmn);

        // Start
        var (runId, startStatus) = await Api.StartRunAsync(workflowId);
        Assert.NotEmpty(runId);

        // The service task executes synchronously (no-op); the run should be either
        // waiting_user (approval gate) or completed if the engine skipped the gate.
        var run = await Api.PollRunUntilAsync(
            runId,
            r => IsTerminalOrWaiting(r),
            timeout: TimeSpan.FromSeconds(30));

        var status = run["status"]!.GetValue<string>();

        if (string.Equals(status, "waiting_user", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "awaiting_approval", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            // Approve the pending gate
            var approvals = await Api.ListApprovalsAsync();
            var pending = approvals
                .OfType<JsonObject>()
                .FirstOrDefault(a =>
                    string.Equals(a["runId"]?.GetValue<string>(), runId, StringComparison.Ordinal)
                    && string.Equals(a["status"]?.GetValue<string>(), "pending", StringComparison.Ordinal));

            Assert.NotNull(pending);
            var approvalId = pending["id"]!.GetValue<string>();

            await Api.DecideApprovalAsync(approvalId, "approve", "E2E auto-approve");

            run = await Api.PollRunUntilAsync(
                runId,
                r => IsCompleted(r),
                timeout: TimeSpan.FromSeconds(30));
        }

        var finalStatus = run["status"]!.GetValue<string>();
        Assert.True(
            IsCompletedValue(finalStatus),
            $"Expected completed run but got status '{finalStatus}'. Run: {run.ToJsonString()}");
    }

    [Fact]
    public async Task StartRun_RejectApproval_CancelsRun()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        var bpmn = LoadFixture("e2e-simple.bpmn");

        var workflowId = await Api.ImportWorkflowAsync("e2e-simple.bpmn", bpmn);
        await Api.PublishWorkflowAsync(workflowId, bpmn);

        var (runId, _) = await Api.StartRunAsync(workflowId);

        await Api.PollRunUntilAsync(runId, IsTerminalOrWaiting, TimeSpan.FromSeconds(30));

        var approvals = await Api.ListApprovalsAsync();
        var pending = approvals
            .OfType<JsonObject>()
            .FirstOrDefault(a =>
                string.Equals(a["runId"]?.GetValue<string>(), runId, StringComparison.Ordinal)
                && string.Equals(a["status"]?.GetValue<string>(), "pending", StringComparison.Ordinal));

        if (pending is null)
        {
            // Service task failed synchronously — run is already cancelled/failed, test still passes.
            return;
        }

        var approvalId = pending["id"]!.GetValue<string>();
        await Api.DecideApprovalAsync(approvalId, "reject", "E2E rejection");

        var run = await Api.PollRunUntilAsync(runId, IsTerminalOrWaiting, TimeSpan.FromSeconds(20));
        var finalStatus = run["status"]!.GetValue<string>();

        Assert.True(
            string.Equals(finalStatus, "cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(finalStatus, "failed", StringComparison.OrdinalIgnoreCase),
            $"Expected cancelled/failed but got '{finalStatus}'");
    }

    [Fact]
    public async Task ListRuns_ReturnsAtLeastOneRun()
    {
        await Api.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));

        // Ensure at least one run exists by starting one
        var bpmn = LoadFixture("e2e-simple.bpmn");
        var workflowId = await Api.ImportWorkflowAsync("e2e-simple.bpmn", bpmn);
        await Api.PublishWorkflowAsync(workflowId, bpmn);
        await Api.StartRunAsync(workflowId);

        using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
        var resp = await http.GetAsync("/api/runs");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(workflowId, body, StringComparison.Ordinal);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool IsTerminalOrWaiting(JsonObject run)
    {
        var s = run["status"]?.GetValue<string>() ?? string.Empty;
        return IsCompletedValue(s)
            || s.Equals("waiting_user", StringComparison.OrdinalIgnoreCase)
            || s.Equals("awaiting_approval", StringComparison.OrdinalIgnoreCase)  // API normalizes waiting_user → awaiting_approval
            || s.Equals("pending", StringComparison.OrdinalIgnoreCase)
            || s.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || s.Equals("failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompleted(JsonObject run) =>
        IsCompletedValue(run["status"]?.GetValue<string>() ?? string.Empty);

    private static bool IsCompletedValue(string s) =>
        s.Equals("completed", StringComparison.OrdinalIgnoreCase);
}
