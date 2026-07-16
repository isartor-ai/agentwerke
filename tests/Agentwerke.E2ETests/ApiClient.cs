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

    // ── Interactions (#215) ─────────────────────────────────────────────────────

    /// <summary>Pending interactions across runs — the decision inbox surface (#227).</summary>
    public async Task<(int Status, JsonArray Items)> ListPendingInteractionsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/interactions?status=pending", ct);
        var items = resp.IsSuccessStatusCode
            ? (await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts, ct)) ?? []
            : [];
        return ((int)resp.StatusCode, items);
    }

    public async Task<JsonArray> ListRunInteractionsAsync(string runId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/api/runs/{runId}/interactions", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonArray>(JsonOpts, ct)) ?? [];
    }

    public async Task<int> AnswerInteractionAsync(
        string runId, string interactionId, string answer, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync(
            $"/api/runs/{runId}/interactions/{interactionId}/answer", new { answer }, JsonOpts, ct);
        return (int)resp.StatusCode;
    }

    /// <summary>
    /// Posts a response to the generic inbound webhook (#224), HMAC-signed exactly as the outbound
    /// channel signs: sha256 over "{unixSeconds}.{rawBody}", so a test exercises the real verifier.
    /// </summary>
    public async Task<int> PostSignedWebhookResponseAsync(
        object payload,
        string secret,
        long? unixTimestampOverride = null,
        string? signatureOverride = null,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        var timestamp = (unixTimestampOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var signature = signatureOverride ?? SignWebhook(body, timestamp, secret);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/webhooks/interactions/response")
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        message.Headers.TryAddWithoutValidation("X-Agentwerke-Signature", signature);
        message.Headers.TryAddWithoutValidation("X-Agentwerke-Timestamp", timestamp);

        var resp = await _http.SendAsync(message, ct);
        return (int)resp.StatusCode;
    }

    private static string SignWebhook(byte[] body, string timestamp, string secret)
    {
        var material = new byte[timestamp.Length + 1 + body.Length];
        var prefix = System.Text.Encoding.UTF8.GetBytes(timestamp + ".");
        Buffer.BlockCopy(prefix, 0, material, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, material, prefix.Length, body.Length);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(secret), material);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
