using System.Text;
using Autofac.Agents.Coordination;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Tools;

/// <summary>
/// Posts a message to the run's coordination channel so another agent can read it (#173).
/// The sender is the calling agent; runs through the Tool Gateway like any tool.
/// </summary>
public sealed class AgentPostMessageTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentCoordinationChannel _channel;

    public AgentPostMessageTool(IAgentCoordinationChannel channel) => _channel = channel;

    public string Name => "agent.post_message";

    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("text", "string", "The coordination message to post for other agents in this run.", Required: true),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        if (!input.TryGetValue("text", out var text) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Tool input is missing required field 'text'.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var message = await _channel.PostAsync(
            context.RunId, context.AgentName, input["text"], context.StepId, cancellationToken);
        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: $"Posted coordination message as '{message.From}'.",
            FailureReason: null);
    }
}

/// <summary>
/// Reads coordination messages posted by other agents in this run (#173), optionally filtered
/// to a specific sender — e.g. an agent waiting on another agent's output.
/// </summary>
public sealed class AgentReadMessagesTool : IAgentTool, IToolSchemaProvider
{
    private readonly IAgentCoordinationChannel _channel;

    public AgentReadMessagesTool(IAgentCoordinationChannel channel) => _channel = channel;

    public string Name => "agent.read_messages";

    public string Category => AgentToolCategories.Coordination;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("from", "string", "Optional: only return messages from this agent.", Required: false),
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        // No required input.
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var fromFilter = input.TryGetValue("from", out var from) && !string.IsNullOrWhiteSpace(from) ? from : null;
        var messages = await _channel.ReadAsync(context.RunId, fromFilter, cancellationToken);

        if (messages.Count == 0)
        {
            return new AgentToolExecutionResult(
                Succeeded: true,
                Output: "No coordination messages yet.",
                FailureReason: null);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"{messages.Count} coordination message(s):");
        foreach (var message in messages)
        {
            builder.AppendLine($"- [{message.CreatedAt}] {message.From}: {message.Text}");
        }

        return new AgentToolExecutionResult(
            Succeeded: true,
            Output: builder.ToString().TrimEnd(),
            FailureReason: null);
    }
}
