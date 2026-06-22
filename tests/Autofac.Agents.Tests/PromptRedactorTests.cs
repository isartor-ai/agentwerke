using Autofac.Agents.Security;

namespace Autofac.Agents.Tests;

public sealed class PromptRedactorTests
{
    [Fact]
    public void Redact_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PromptRedactor.Redact(null));
        Assert.Equal(string.Empty, PromptRedactor.Redact(string.Empty));
    }

    [Fact]
    public void Redact_NoSecrets_ReturnsInputUnchanged()
    {
        const string text = "Review the pull request and provide feedback.";
        Assert.Equal(text, PromptRedactor.Redact(text));
    }

    [Fact]
    public void Redact_AnthropicApiKey_IsReplaced()
    {
        const string input = "Use apiKey=sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWXYZ to call the model.";
        var result = PromptRedactor.Redact(input);
        Assert.DoesNotContain("sk-ant-api03-", result);
        Assert.Contains("[redacted]", result);
    }

    [Fact]
    public void Redact_GitHubToken_IsReplaced()
    {
        const string input = "Export GH_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz1234567890 before running.";
        var result = PromptRedactor.Redact(input);
        Assert.DoesNotContain("ghp_", result);
        Assert.Contains("[redacted]", result);
    }

    [Fact]
    public void Redact_AwsAccessKey_IsReplaced()
    {
        const string input = "Set AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE in your environment.";
        var result = PromptRedactor.Redact(input);
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", result);
        Assert.Contains("[redacted]", result);
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("api_key=supersecret")]
    [InlineData("token=eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("secret=xK9#mP2!vL")]
    [InlineData("ACCESS_TOKEN=abc123xyz")]
    [InlineData("Authorization: Bearer sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    public void Redact_GenericSecretAssignment_IsReplaced(string secretFragment)
    {
        var input = $"Run the task with {secretFragment} in the config.";
        var result = PromptRedactor.Redact(input);
        Assert.Contains("[redacted]", result);
        if (secretFragment.Contains('='))
        {
            var key = secretFragment.Split('=')[0];
            Assert.Contains(key, result, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("Authorization: Bearer", result, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Redact_MultipleSecrets_AllReplaced()
    {
        const string input = """
            Authenticate with sk-ant-api03-TESTKEY1234567890ABCDEF.
            Also set password=correct-horse-battery-staple.
            GitHub token: ghp_abcdefghijklmnopqrstuvwxyz123456789012
            """;

        var result = PromptRedactor.Redact(input);

        Assert.DoesNotContain("sk-ant-", result);
        Assert.DoesNotContain("correct-horse-battery-staple", result);
        Assert.DoesNotContain("ghp_", result);
        Assert.Equal(3, CountOccurrences(result, "[redacted]"));
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
