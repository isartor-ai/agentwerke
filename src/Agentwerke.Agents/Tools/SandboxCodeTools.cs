using System.Diagnostics;
using System.Text;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Tools;

/// <summary>
/// Code-writing tools for the <c>agent_sandboxed</c> in-sandbox tool loop (#130, #140) — file
/// read/write/edit, a constrained git tool, an allow-listed shell, and a test runner. These only
/// make sense inside an ephemeral sandbox container with a mounted /workspace; they are
/// constructed directly by <c>Agentwerke.AgentRunner.RunnerToolFactory</c>, never registered in
/// Agentwerke.Agents' DI container, so the in-process (non-sandboxed) orchestrator never sees them.
/// </summary>
internal static class SandboxWorkspace
{
    public const string DefaultRoot = "/workspace";

    /// <summary>
    /// Resolves a workspace-relative path and rejects any path that escapes the workspace root
    /// (e.g. via "../"), since these tools run with the agent's own input as the path.
    /// </summary>
    public static string ResolvePath(string workspaceRoot, string relativePath)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));

        if (combined != root && !combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Path '{relativePath}' escapes the sandbox workspace.");
        }

        return combined;
    }

    public static void Require(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Tool input is missing required field '{key}'.");
        }
    }

    public static string? ReadOptional(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Runs a process with no shell interpretation (no metacharacters, pipes, or redirects take effect).</summary>
    public static async Task<ProcessRunResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(-1, string.Empty, $"Failed to start '{fileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessRunResult(process.ExitCode, stdOut.ToString().TrimEnd(), stdErr.ToString().TrimEnd());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup; the sandbox container is torn down regardless.
        }
    }
}

internal sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput =>
        StandardError.Length == 0 ? StandardOutput : $"{StandardOutput}\n{StandardError}".Trim();
}

public sealed class SandboxFileReadTool(string workspaceRoot = SandboxWorkspace.DefaultRoot) : IAgentTool, IToolSchemaProvider
{
    private const int MaxCharacters = 256_000;

    public string Name => "sandbox.file_read";

    public string Category => AgentToolCategories.Read;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("path", "string", "Path to read, relative to the workspace root.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input) => SandboxWorkspace.Require(input, "path");

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var resolved = SandboxWorkspace.ResolvePath(workspaceRoot, input["path"]);
        if (!File.Exists(resolved))
        {
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: false,
                Output: null,
                FailureReason: $"File '{input["path"]}' does not exist."));
        }

        var content = File.ReadAllText(resolved);
        var truncated = content.Length > MaxCharacters;
        var output = truncated ? content[..MaxCharacters] + "\n…(truncated)" : content;

        return Task.FromResult(new AgentToolExecutionResult(Succeeded: true, Output: output, FailureReason: null));
    }
}

public sealed class SandboxFileWriteTool(string workspaceRoot = SandboxWorkspace.DefaultRoot) : IAgentTool, IToolSchemaProvider
{
    public string Name => "sandbox.file_write";

    public string Category => AgentToolCategories.Write;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("path", "string", "Path to write, relative to the workspace root.", Required: true),
        new("content", "string", "File content to write (overwrites any existing file).", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        SandboxWorkspace.Require(input, "path");
        SandboxWorkspace.Require(input, "content");
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var resolved = SandboxWorkspace.ResolvePath(workspaceRoot, input["path"]);
        var directory = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolved, input["content"]);

        return Task.FromResult(new AgentToolExecutionResult(
            Succeeded: true,
            Output: $"Wrote {input["content"].Length} character(s) to {input["path"]}.",
            FailureReason: null));
    }
}

/// <summary>
/// Find-and-replace edit mirroring Claude Code's Edit tool semantics: <c>old_text</c> must match
/// exactly once, so an ambiguous edit fails loudly instead of silently changing the wrong spot.
/// </summary>
public sealed class SandboxFileEditTool(string workspaceRoot = SandboxWorkspace.DefaultRoot) : IAgentTool, IToolSchemaProvider
{
    public string Name => "sandbox.file_edit";

    public string Category => AgentToolCategories.Write;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("path", "string", "Path to edit, relative to the workspace root.", Required: true),
        new("old_text", "string", "Exact text to replace. Must occur exactly once in the file.", Required: true),
        new("new_text", "string", "Replacement text.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        SandboxWorkspace.Require(input, "path");
        SandboxWorkspace.Require(input, "old_text");
        if (!input.ContainsKey("new_text"))
        {
            throw new InvalidOperationException("Tool input is missing required field 'new_text'.");
        }
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var resolved = SandboxWorkspace.ResolvePath(workspaceRoot, input["path"]);
        if (!File.Exists(resolved))
        {
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: false,
                Output: null,
                FailureReason: $"File '{input["path"]}' does not exist."));
        }

        var content = File.ReadAllText(resolved);
        var oldText = input["old_text"];
        var occurrences = CountOccurrences(content, oldText);

        if (occurrences == 0)
        {
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: false,
                Output: null,
                FailureReason: $"old_text was not found in '{input["path"]}'."));
        }

        if (occurrences > 1)
        {
            return Task.FromResult(new AgentToolExecutionResult(
                Succeeded: false,
                Output: null,
                FailureReason: $"old_text occurs {occurrences} times in '{input["path"]}'; it must be unique. Add more surrounding context."));
        }

        var newText = input["new_text"];
        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        var updated = string.Concat(content.AsSpan(0, index), newText, content.AsSpan(index + oldText.Length));
        File.WriteAllText(resolved, updated);

        return Task.FromResult(new AgentToolExecutionResult(
            Succeeded: true,
            Output: $"Edited {input["path"]}.",
            FailureReason: null));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}

/// <summary>
/// A constrained git tool: a fixed set of operations mapped to safe, argument-array git
/// invocations (never a shell string), so the agent can clone/checkout/commit/push without
/// being handed an unconstrained shell. Authenticates clone/push with the same GitHub PAT the
/// github.* tools use (#140); push targets the same repository the run's branch lives in.
/// </summary>
public sealed class SandboxGitTool(
    string? repositoryOwner,
    string? repositoryName,
    string? personalAccessToken,
    string workspaceRoot = SandboxWorkspace.DefaultRoot,
    string apiHost = "github.com") : IAgentTool, IToolSchemaProvider
{
    private static readonly IReadOnlyList<string> SupportedOperations =
        ["clone", "init", "checkout", "add", "commit", "push", "pull", "status", "diff"];

    public string Name => "sandbox.git";

    public string Category => AgentToolCategories.Shell;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("operation", "string", $"One of: {string.Join(", ", SupportedOperations)}.", Required: true),
        new("branch", "string", "Branch name for clone/checkout/push. Defaults to agentwerke/run-<run_id>.", Required: false),
        new("message", "string", "Commit message (required for commit).", Required: false),
        new("path", "string", "Path to stage for add. Defaults to '.' (everything).", Required: false)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        SandboxWorkspace.Require(input, "operation");
        var operation = input["operation"].Trim().ToLowerInvariant();
        if (!SupportedOperations.Contains(operation, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unsupported git operation '{operation}'. Supported: {string.Join(", ", SupportedOperations)}.");
        }

        if (operation == "commit")
        {
            SandboxWorkspace.Require(input, "message");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workspaceRoot);
        var operation = input["operation"].Trim().ToLowerInvariant();
        var defaultBranch = $"agentwerke/run-{context.RunId}";
        var branch = SandboxWorkspace.ReadOptional(input, "branch") ?? defaultBranch;

        return operation switch
        {
            "clone" => await CloneAsync(branch, cancellationToken),
            "init" => await RunGitAsync(["init", "."], cancellationToken, configureIdentityAfter: true),
            "checkout" => await CheckoutAsync(branch, cancellationToken),
            "add" => await RunGitAsync(["add", SandboxWorkspace.ReadOptional(input, "path") ?? "."], cancellationToken),
            "commit" => await RunGitAsync(["commit", "-m", input["message"]], cancellationToken),
            "push" => await RunGitAsync(["push", "origin", $"HEAD:{branch}"], cancellationToken),
            "pull" => await RunGitAsync(["pull"], cancellationToken),
            "status" => await RunGitAsync(["status", "--short"], cancellationToken),
            "diff" => await RunGitAsync(["diff"], cancellationToken),
            _ => new AgentToolExecutionResult(false, null, $"Unsupported git operation '{operation}'.")
        };
    }

    private async Task<AgentToolExecutionResult> CloneAsync(string branch, CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(workspaceRoot, ".git")))
        {
            return new AgentToolExecutionResult(true, "Workspace is already a git checkout; skipping clone.", null);
        }

        if (string.IsNullOrWhiteSpace(repositoryOwner) || string.IsNullOrWhiteSpace(repositoryName))
        {
            return new AgentToolExecutionResult(false, null, "GitHub repository owner/name is not configured for this sandbox.");
        }

        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            return new AgentToolExecutionResult(false, null, "GitHub personal access token is not configured for this sandbox.");
        }

        var url = $"https://x-access-token:{personalAccessToken}@{apiHost}/{repositoryOwner}/{repositoryName}.git";
        var result = await SandboxWorkspace.RunProcessAsync(
            "git",
            ["clone", "--branch", branch, url, "."],
            workspaceRoot,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            // Branch may not exist yet (e.g. first commit on a new run branch) — fall back to a
            // default-branch clone followed by creating the branch locally.
            var fallback = await SandboxWorkspace.RunProcessAsync("git", ["clone", url, "."], workspaceRoot, cancellationToken);
            if (fallback.ExitCode != 0)
            {
                return new AgentToolExecutionResult(false, null, Redact(fallback.CombinedOutput, personalAccessToken));
            }

            await ConfigureIdentityAsync(cancellationToken);
            var created = await SandboxWorkspace.RunProcessAsync("git", ["checkout", "-b", branch], workspaceRoot, cancellationToken);
            return created.ExitCode == 0
                ? new AgentToolExecutionResult(true, $"Cloned and created branch '{branch}'.", null)
                : new AgentToolExecutionResult(false, null, Redact(created.CombinedOutput, personalAccessToken));
        }

        await ConfigureIdentityAsync(cancellationToken);
        return new AgentToolExecutionResult(true, $"Cloned branch '{branch}'.", null);
    }

    private async Task<AgentToolExecutionResult> CheckoutAsync(string branch, CancellationToken cancellationToken)
    {
        var existing = await SandboxWorkspace.RunProcessAsync("git", ["checkout", branch], workspaceRoot, cancellationToken);
        if (existing.ExitCode == 0)
        {
            return new AgentToolExecutionResult(true, existing.CombinedOutput, null);
        }

        var created = await SandboxWorkspace.RunProcessAsync("git", ["checkout", "-b", branch], workspaceRoot, cancellationToken);
        return created.ExitCode == 0
            ? new AgentToolExecutionResult(true, created.CombinedOutput, null)
            : new AgentToolExecutionResult(false, null, created.CombinedOutput);
    }

    private async Task ConfigureIdentityAsync(CancellationToken cancellationToken)
    {
        await SandboxWorkspace.RunProcessAsync("git", ["config", "user.email", "agentwerke-bot@agentwerke.de"], workspaceRoot, cancellationToken);
        await SandboxWorkspace.RunProcessAsync("git", ["config", "user.name", "Agentwerke"], workspaceRoot, cancellationToken);
    }

    private async Task<AgentToolExecutionResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool configureIdentityAfter = false)
    {
        var result = await SandboxWorkspace.RunProcessAsync("git", arguments, workspaceRoot, cancellationToken);
        if (configureIdentityAfter && result.ExitCode == 0)
        {
            await ConfigureIdentityAsync(cancellationToken);
        }

        return result.ExitCode == 0
            ? new AgentToolExecutionResult(true, result.CombinedOutput, null)
            : new AgentToolExecutionResult(false, null, Redact(result.CombinedOutput, personalAccessToken));
    }

    private static string Redact(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "***", StringComparison.Ordinal);
}

/// <summary>
/// Runs an allow-listed binary with the given arguments — never a shell string, so shell
/// metacharacters (pipes, redirects, command chaining) have no effect. The agent gets common
/// build/lint tooling, not an unconstrained shell.
/// </summary>
public sealed class SandboxShellTool(string workspaceRoot = SandboxWorkspace.DefaultRoot) : IAgentTool, IToolSchemaProvider
{
    internal static readonly IReadOnlyList<string> AllowedCommands =
        ["dotnet", "npm", "npx", "yarn", "pnpm", "node", "python", "python3", "pip", "pytest", "make", "go", "cargo", "mvn", "gradle"];

    public string Name => "sandbox.shell";

    public string Category => AgentToolCategories.Shell;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("command", "string", $"A command line to run. The first word must be one of: {string.Join(", ", AllowedCommands)}.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        SandboxWorkspace.Require(input, "command");
        var tokens = Tokenize(input["command"]);
        if (tokens.Count == 0 || !AllowedCommands.Contains(tokens[0], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Command '{(tokens.Count > 0 ? tokens[0] : input["command"])}' is not allow-listed. Allowed: {string.Join(", ", AllowedCommands)}.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var tokens = Tokenize(input["command"]);
        var result = await SandboxWorkspace.RunProcessAsync(tokens[0], tokens.Skip(1).ToArray(), workspaceRoot, cancellationToken);

        return new AgentToolExecutionResult(
            Succeeded: result.ExitCode == 0,
            Output: $"exit_code: {result.ExitCode}\n{result.CombinedOutput}",
            FailureReason: result.ExitCode == 0 ? null : $"Command exited with code {result.ExitCode}.");
    }

    /// <summary>Minimal whitespace tokenizer with double-quote support. No shell metacharacters are interpreted.</summary>
    internal static IReadOnlyList<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

/// <summary>
/// Runs the repo's test command and reports pass/fail for the Tester agent (#140). Shares
/// <see cref="SandboxShellTool"/>'s allow-list — a test command is still just a command.
/// </summary>
public sealed class SandboxRunTestsTool(string workspaceRoot = SandboxWorkspace.DefaultRoot) : IAgentTool, IToolSchemaProvider
{
    public string Name => "sandbox.run_tests";

    public string Category => AgentToolCategories.Shell;

    public IReadOnlyList<ToolSchemaParameter> GetParameters() =>
    [
        new("command", "string", $"The test command to run, e.g. 'dotnet test'. The first word must be one of: {string.Join(", ", SandboxShellTool.AllowedCommands)}.", Required: true)
    ];

    public void Validate(IReadOnlyDictionary<string, string> input)
    {
        SandboxWorkspace.Require(input, "command");
        var tokens = SandboxShellTool.Tokenize(input["command"]);
        if (tokens.Count == 0 || !SandboxShellTool.AllowedCommands.Contains(tokens[0], StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Command '{(tokens.Count > 0 ? tokens[0] : input["command"])}' is not allow-listed. Allowed: {string.Join(", ", SandboxShellTool.AllowedCommands)}.");
        }
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken)
    {
        var tokens = SandboxShellTool.Tokenize(input["command"]);
        var result = await SandboxWorkspace.RunProcessAsync(tokens[0], tokens.Skip(1).ToArray(), workspaceRoot, cancellationToken);
        var passed = result.ExitCode == 0;

        return new AgentToolExecutionResult(
            Succeeded: passed,
            Output: $"{(passed ? "PASSED" : "FAILED")} (exit_code: {result.ExitCode})\n{result.CombinedOutput}",
            FailureReason: passed ? null : $"Test command exited with code {result.ExitCode}.");
    }
}
