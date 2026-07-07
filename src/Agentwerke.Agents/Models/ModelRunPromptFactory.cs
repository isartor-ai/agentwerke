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
        parts.Add("Do not reveal hidden chain-of-thought. If you need to explain your work, include a short visible progress summary in <agent_reasoning>...</agent_reasoning> with concrete checks, decisions, and verification steps, then provide the final result outside that block.");

        return string.Join(" ", parts);
    }
}
