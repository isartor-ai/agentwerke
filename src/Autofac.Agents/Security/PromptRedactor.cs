using System.Text.RegularExpressions;

namespace Autofac.Agents.Security;

/// <summary>
/// Strips well-known secret patterns from text before it is persisted in agent runtime snapshots.
/// Applied to prompt content only — the live prompt sent to the model is never modified.
/// </summary>
public static partial class PromptRedactor
{
    private const string Placeholder = "[redacted]";

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        input = AnthropicKey().Replace(input, Placeholder);
        input = GitHubToken().Replace(input, Placeholder);
        input = AwsAccessKey().Replace(input, Placeholder);
        input = GenericSecretAssignment().Replace(input, m => $"{m.Groups["key"].Value}={Placeholder}");

        return input;
    }

    // sk-ant-api03-... (Anthropic API key)
    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{10,}", RegexOptions.None)]
    private static partial Regex AnthropicKey();

    // ghp_..., gho_..., github_pat_... (GitHub personal access tokens)
    [GeneratedRegex(@"(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{36,}", RegexOptions.None)]
    private static partial Regex GitHubToken();

    // AKIA... (AWS access key ID)
    [GeneratedRegex(@"AKIA[A-Z0-9]{16}", RegexOptions.None)]
    private static partial Regex AwsAccessKey();

    // password=..., token=..., api_key=..., secret=... (generic key=value pairs)
    [GeneratedRegex(@"(?i)(?<key>password|secret|token|api[_\-]?key|api[_\-]?token|access[_\-]?token|auth[_\-]?token|bearer[_\-]?token)(?:\s*[=:]\s*)(?<val>\S+)", RegexOptions.None)]
    private static partial Regex GenericSecretAssignment();
}
