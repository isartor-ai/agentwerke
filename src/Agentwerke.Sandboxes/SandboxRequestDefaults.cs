namespace Agentwerke.Sandboxes;

internal static class SandboxRequestDefaults
{
    public static Dictionary<string, string> BuildExecutionEnvironment(SandboxExecutionRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AGENTWERKE_RUN_ID"] = request.RunId,
            ["AGENTWERKE_STEP_ID"] = request.StepId,
            ["AGENTWERKE_AGENT"] = request.AgentName,
            ["AGENTWERKE_ACTION"] = request.Action,
            ["AGENTWERKE_ENVIRONMENT"] = request.Environment ?? string.Empty,
            ["AGENTWERKE_PURPOSE"] = request.PurposeType,
            ["AGENTWERKE_POLICY"] = request.PolicyTag,
            ["AGENTWERKE_ATTEMPT"] = request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static SandboxCommandSpec ResolveCommand(SandboxExecutionRequest request)
    {
        if (request.Command is not null)
        {
            return request.Command;
        }

        return new SandboxCommandSpec(
            Arguments: ["sh", "-c", BuildPlaceholderEntrypointScript()],
            WorkingDirectory: "/",
            EnvironmentVariables: null,
            StandardInput: null,
            StreamOutput: true);
    }

    public static IReadOnlyList<string> ResolveArtifactPaths(
        SandboxExecutionRequest request,
        IReadOnlyList<string>? fallbackPaths = null) =>
        request.ArtifactPaths is { Count: > 0 }
            ? request.ArtifactPaths
            : fallbackPaths is { Count: > 0 }
                ? fallbackPaths
                : ["/output"];

    public static string BuildPlaceholderEntrypointScript() =>
        """
        set -e
        echo "agentwerke-sandbox: starting task"
        echo "agent=$AGENTWERKE_AGENT action=$AGENTWERKE_ACTION env=$AGENTWERKE_ENVIRONMENT attempt=$AGENTWERKE_ATTEMPT"
        mkdir -p /output
        cat > /output/result.json <<EOF
        {
          "runId": "$AGENTWERKE_RUN_ID",
          "stepId": "$AGENTWERKE_STEP_ID",
          "agent": "$AGENTWERKE_AGENT",
          "action": "$AGENTWERKE_ACTION",
          "environment": "$AGENTWERKE_ENVIRONMENT",
          "status": "completed",
          "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        }
        EOF
        echo "agentwerke-sandbox: task complete"
        """;
}
