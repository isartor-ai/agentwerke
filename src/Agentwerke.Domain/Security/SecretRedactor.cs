using System.Text.RegularExpressions;

namespace Agentwerke.Domain.Security;

public static partial class SecretRedactor
{
    private const string Placeholder = "[redacted]";

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        input = AnthropicKey().Replace(input, Placeholder);
        input = GitHubToken().Replace(input, Placeholder);
        input = AwsAccessKey().Replace(input, Placeholder);
        input = BearerToken().Replace(input, match => $"{match.Groups["prefix"].Value}{Placeholder}");
        input = GenericSecretAssignment().Replace(input, match => $"{match.Groups["key"].Value}={Placeholder}");

        return input;
    }

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]{10,}", RegexOptions.None)]
    private static partial Regex AnthropicKey();

    [GeneratedRegex(@"(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{36,}", RegexOptions.None)]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"AKIA[A-Z0-9]{16}", RegexOptions.None)]
    private static partial Regex AwsAccessKey();

    [GeneratedRegex(@"(?i)(?<prefix>authorization\s*:\s*bearer\s+)(?<token>\S+)", RegexOptions.None)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)(?<key>password|secret|token|api[_\-]?key|api[_\-]?token|access[_\-]?token|auth[_\-]?token|bearer[_\-]?token)(?:\s*[=:]\s*)(?<val>\S+)", RegexOptions.None)]
    private static partial Regex GenericSecretAssignment();
}
