using Autofac.Agents.Models;
using Microsoft.Extensions.Options;

namespace Autofac.Agents.Tests;

/// <summary>
/// Issue #143 acceptance check #4 (automatable portion): proves the *real* Claude client drives a
/// tool-use loop end-to-end through the configured resilience pipeline. Gated on an API key so it is
/// a no-op in CI/dev without credentials — set <c>AUTOFAC_E2E_ANTHROPIC_API_KEY</c> to enable.
///
/// The full BPMN-template-to-real-PR proof requires infrastructure this repo does not own
/// (a disposable GitHub repo, webhook delivery, a reachable host) and is documented as a manual
/// procedure in docs/manual-test-sdlc-e2e.md.
/// </summary>
public sealed class RealClaudeIntegrationTests
{
    private const string ApiKeyEnvVar = "AUTOFAC_E2E_ANTHROPIC_API_KEY";

    [Fact]
    public async Task RealModel_DrivesToolUseLoop_AndReturnsFinalText()
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No credentials configured — skip (treated as passing no-op).
            return;
        }

        var options = Options.Create(new LanguageModelOptions
        {
            ApiKey = apiKey,
            Model = Environment.GetEnvironmentVariable("AUTOFAC_E2E_ANTHROPIC_MODEL") ?? "claude-sonnet-4-6",
            MaxTokens = 512,
            MaxToolIterations = 5
        });

        using var httpClient = new HttpClient(
            new AnthropicRetryHandler(options) { InnerHandler = new HttpClientHandler() })
        {
            Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds)
        };
        var client = new AnthropicLanguageModelClient(httpClient, options);

        var tools = new[]
        {
            new LanguageModelToolDefinition(
                Name: "record_decision",
                Description: "Records the review decision. Must be called before replying.",
                Parameters: new[]
                {
                    new LanguageModelToolParameter(
                        Name: "decision",
                        Type: "string",
                        Description: "The review outcome",
                        Required: true,
                        EnumValues: new[] { "approve", "request_changes" })
                })
        };

        var toolCalls = new List<LanguageModelToolCall>();
        var response = await client.RunAsync(
            new LanguageModelRequest(
                SystemPrompt: "You are a senior code reviewer. You must call the record_decision " +
                              "tool exactly once with decision='approve' before giving your final reply.",
                UserPrompt: "The change is a trivial, well-tested README typo fix. Record your decision.",
                Tools: tools,
                MaxTokens: 512),
            toolExecutor: (call, _) =>
            {
                toolCalls.Add(call);
                return Task.FromResult(new LanguageModelToolResult(call.Id, "recorded"));
            },
            CancellationToken.None);

        Assert.True(response.Succeeded, response.FailureReason);
        Assert.Contains(toolCalls, c => c.Name == "record_decision");
        Assert.False(string.IsNullOrWhiteSpace(response.Output));
        Assert.True(response.Usage.InputTokens > 0, "Expected real token usage to be reported.");
    }
}
