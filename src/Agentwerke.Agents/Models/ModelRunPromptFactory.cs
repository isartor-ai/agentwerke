using System.Text;

namespace Agentwerke.Agents.Models;

internal static class ModelRunPromptFactory
{
    public static string BuildSystemPrompt(ModelRunRequest request)
    {
        var parts = new List<string>
        {
            $"You are {request.AgentName}, an AI agent executing the action '{request.Action}'.",
            $"Purpose: {request.PurposeType}.",
            $"Environment: {request.Environment ?? "unspecified"}.",
            $"Attempt: {request.Attempt}."
        };

        if (request.RequiresEvidence.Count > 0)
        {
            parts.Add($"Required evidence: {string.Join(", ", request.RequiresEvidence)}.");
        }

        parts.Add("Use the available tools to complete the task. Be precise and efficient.");
        parts.Add("Do not reveal hidden chain-of-thought. Before each major decision or tool call, emit a short visible progress summary in <agent_reasoning>...</agent_reasoning> with concrete checks, decisions, and verification steps. After the work is done, provide the final result outside those blocks.");

        return string.Join(" ", parts);
    }
}
