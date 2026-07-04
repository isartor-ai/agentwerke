using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agentwerke.E2ETests;

/// <summary>Thin typed wrapper around the Agentwerke REST API for E2E tests.</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Correlation-Id", "e2e-test");
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("/api/health/live", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHealthyAsync(ct)) return;
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new TimeoutException($"API did not become healthy within {timeout}.");
    }

    // ── Workflows ─────────────────────────────────────────────────────────────

    public async Task<string> ImportWorkflowAsync(string fileName, string bpmnXml, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/workflows/import",
            new { fileName, bpmnXml }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
        return body!["workflowId"]!.GetValue<string>();
    }

    public async Task PublishWorkflowAsync(string workflowId, string bpmnXml, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/workflows/{workflowId}/publish",
            new { bpmnXml, description = "E2E publish" }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Agents ───────────────────────────────────────────────────────────────

    public async Task<JsonObject> UploadAgentAsync(string fileName, string content, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/agents/upload",
            new { fileName, content }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct))!;
    }

    public async Task<JsonObject> GetAgentAsync(string agentId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/agents/{agentId}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct))!;
    }

    // ── Runs ──────────────────────────────────────────────────────────────────

    public async Task<(string RunId, string Status)> StartRunAsync(string workflowId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/runs",
            new { workflowId }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct);
        return (body!["runId"]!.GetValue<string>(), body!["status"]!.GetValue<string>());
    }

    public async Task<JsonObject> GetRunAsync(string runId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/runs/{runId}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct))!;
    }

    public async Task<JsonObject> PollRunUntilAsync(
        string runId,
        Func<JsonObject, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var run = await GetRunAsync(runId, ct);
            if (predicate(run)) return run;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new TimeoutException($"Run '{runId}' did not reach expected state within {timeout}.");
    }

    // ── Approvals ─────────────────────────────────────────────────────────────

    public async Task<JsonArray> ListApprovalsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/approvals", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts, ct))!;
    }

    public async Task<JsonObject> DecideApprovalAsync(
        string approvalId, string decision, string? comment = null, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/api/approvals/{approvalId}/decision",
            new { decision, comment }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts, ct))!;
    }
}
