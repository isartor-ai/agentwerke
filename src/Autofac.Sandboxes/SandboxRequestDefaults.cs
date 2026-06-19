namespace Autofac.Sandboxes;

internal static class SandboxRequestDefaults
{
    public static Dictionary<string, string> BuildExecutionEnvironment(SandboxExecutionRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AUTOFAC_RUN_ID"] = request.RunId,
            ["AUTOFAC_STEP_ID"] = request.StepId,
            ["AUTOFAC_AGENT"] = request.AgentName,
            ["AUTOFAC_ACTION"] = request.Action,
            ["AUTOFAC_ENVIRONMENT"] = request.Environment ?? string.Empty,
            ["AUTOFAC_PURPOSE"] = request.PurposeType,
            ["AUTOFAC_POLICY"] = request.PolicyTag,
            ["AUTOFAC_ATTEMPT"] = request.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)
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
            WorkingDirectory: null,
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
        echo "autofac-sandbox: starting task"
        echo "agent=$AUTOFAC_AGENT action=$AUTOFAC_ACTION env=$AUTOFAC_ENVIRONMENT attempt=$AUTOFAC_ATTEMPT"
        mkdir -p /output
        cat > /output/result.json <<EOF
        {
          "runId": "$AUTOFAC_RUN_ID",
          "stepId": "$AUTOFAC_STEP_ID",
          "agent": "$AUTOFAC_AGENT",
          "action": "$AUTOFAC_ACTION",
          "environment": "$AUTOFAC_ENVIRONMENT",
          "status": "completed",
          "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        }
        EOF
        echo "autofac-sandbox: task complete"
        """;
}
