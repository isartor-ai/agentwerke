using System.Text;

namespace Autofac.Agents.Models;

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

        return string.Join(" ", parts);
    }
}
