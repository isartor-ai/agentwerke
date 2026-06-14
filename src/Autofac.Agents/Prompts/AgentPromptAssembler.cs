using System.Text;
using System.Text.RegularExpressions;
using Autofac.Domain.AgentRuntime;

namespace Autofac.Agents.Prompts;

public sealed partial class AgentPromptAssembler : IAgentPromptAssembler
{
    [GeneratedRegex("{{\\s*([a-zA-Z0-9_.-]+)\\s*}}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();

    public AgentPromptAssemblyResult Assemble(AgentPromptAssemblyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = request.Prompt;
        var variables = BuildVariables(request, prompt);
        var sources = new List<string>();
        var missingVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sections = new List<AgentPromptSectionSnapshot>();

        sections.Add(new AgentPromptSectionSnapshot(
            Name: "system_preamble",
            Content: "You are executing an Autofac agent task. Follow the provided task, skill, and runtime context carefully.",
            Source: "generated:system"));

        if (!string.IsNullOrWhiteSpace(request.AgentDescription) || !string.IsNullOrWhiteSpace(request.AgentCategory))
        {
            sections.Add(new AgentPromptSectionSnapshot(
                Name: "agent_profile",
                Content: $"""
                    Agent: {request.AgentName}
                    Category: {request.AgentCategory ?? "unspecified"}
                    Description: {request.AgentDescription ?? "unspecified"}
                    """,
                Source: "generated:agent_profile"));
        }

        if (!string.IsNullOrWhiteSpace(prompt?.File))
        {
            var promptFile = ResolvePromptFile(prompt.File!);
            sources.Add(promptFile);
            var rawFilePrompt = File.ReadAllText(promptFile);
            sections.Add(new AgentPromptSectionSnapshot(
                Name: "task_prompt",
                Content: Render(rawFilePrompt, variables, missingVariables),
                Source: promptFile));
        }
        else if (!string.IsNullOrWhiteSpace(prompt?.Inline))
        {
            sections.Add(new AgentPromptSectionSnapshot(
                Name: "task_prompt",
                Content: Render(prompt.Inline!, variables, missingVariables),
                Source: "inline:task_prompt"));
        }
        else
        {
            sections.Add(new AgentPromptSectionSnapshot(
                Name: "task_prompt",
                Content: BuildDefaultTaskPrompt(request),
                Source: "generated:task_prompt"));
        }

        if (request.Skill is not null)
        {
            sections.Add(new AgentPromptSectionSnapshot(
                Name: "skill_context",
                Content: $"""
                    Skill: {request.Skill.Name}
                    SkillId: {request.Skill.SkillId}
                    Description: {request.Skill.Description}
                    Fingerprint: {request.Skill.Fingerprint}

                    {request.Skill.Content}
                    """,
                Source: request.Skill.FilePath));
            sources.Add(request.Skill.FilePath);
        }

        sections.Add(new AgentPromptSectionSnapshot(
            Name: "runtime_context",
            Content: $"""
                RunId: {request.RunId}
                StepId: {request.StepId}
                NodeId: {request.NodeId}
                NodeName: {request.NodeName ?? "unspecified"}
                Agent: {request.AgentName}
                Action: {request.Action}
                Environment: {request.Environment ?? "unspecified"}
                PurposeType: {request.PurposeType}
                PolicyTag: {request.PolicyTag}
                Attempt: {request.Attempt}
                RequiresEvidence: {variables["requires_evidence_csv"]}
                """,
            Source: "generated:runtime_context"));

        if (missingVariables.Count > 0)
        {
            return new AgentPromptAssemblyResult(
                Succeeded: false,
                PromptSnapshot: new AgentPromptSnapshot(
                    FinalPrompt: string.Empty,
                    RenderedAt: DateTimeOffset.UtcNow.ToString("o"),
                    Sections: sections.AsReadOnly(),
                    Variables: new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase),
                    SourceFiles: sources.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static s => s, StringComparer.OrdinalIgnoreCase).ToArray()),
                FailureReason: $"Prompt assembly failed: missing variables: {string.Join(", ", missingVariables.OrderBy(static v => v, StringComparer.OrdinalIgnoreCase))}.",
                MissingVariables: missingVariables.OrderBy(static v => v, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var builder = new StringBuilder();
        foreach (var section in sections)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine($"## {section.Name}");
            builder.Append(section.Content.Trim());
        }

        return new AgentPromptAssemblyResult(
            Succeeded: true,
            PromptSnapshot: new AgentPromptSnapshot(
                FinalPrompt: builder.ToString(),
                RenderedAt: DateTimeOffset.UtcNow.ToString("o"),
                Sections: sections.AsReadOnly(),
                Variables: new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase),
                SourceFiles: sources.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static s => s, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    private static Dictionary<string, string> BuildVariables(
        AgentPromptAssemblyRequest request,
        AgentPromptContract? prompt)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["run_id"] = request.RunId,
            ["step_id"] = request.StepId,
            ["node_id"] = request.NodeId,
            ["node_name"] = request.NodeName ?? string.Empty,
            ["agent_name"] = request.AgentName,
            ["action"] = request.Action,
            ["environment"] = request.Environment ?? string.Empty,
            ["purpose_type"] = request.PurposeType,
            ["policy_tag"] = request.PolicyTag,
            ["attempt"] = request.Attempt.ToString(),
            ["requires_evidence_csv"] = string.Join(", ", request.RequiresEvidence)
        };

        if (prompt?.Variables is not null)
        {
            foreach (var pair in prompt.Variables)
            {
                variables[pair.Key] = pair.Value;
            }
        }

        return variables;
    }

    private static string Render(
        string template,
        IReadOnlyDictionary<string, string> variables,
        ISet<string> missingVariables)
    {
        return VariablePattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (variables.TryGetValue(key, out var value))
            {
                return value;
            }

            missingVariables.Add(key);
            return match.Value;
        });
    }

    private static string BuildDefaultTaskPrompt(AgentPromptAssemblyRequest request)
    {
        var evidence = request.RequiresEvidence.Count == 0
            ? "none"
            : string.Join(", ", request.RequiresEvidence);

        return $"""
            Complete the requested Autofac agent task.

            Task:
            - Node: {request.NodeName ?? request.NodeId}
            - Agent: {request.AgentName}
            - Action: {request.Action}
            - Environment: {request.Environment ?? "unspecified"}
            - Purpose: {request.PurposeType}
            - Policy tag: {request.PolicyTag}
            - Evidence required: {evidence}
            """;
    }

    private static string ResolvePromptFile(string promptFile) =>
        Path.IsPathRooted(promptFile)
            ? promptFile
            : Path.GetFullPath(promptFile);
}
