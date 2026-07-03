using Agentwerke.Agents.Tools;

namespace Agentwerke.Agents.Tests;

public sealed class SandboxCodeToolsTests
{
    private static AgentToolExecutionContext Context(string runId = "run_1") =>
        new(RunId: runId, StepId: "step_1", AgentName: "implementation-engineer", Action: "implement",
            Environment: "sandbox", PurposeType: "implementation", PolicyTag: "implementation", Attempt: 1);

    // ── file_read ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadTool_ReadsAnExistingFile()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, "notes.txt"), "hello world");
        var tool = new SandboxFileReadTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["path"] = "notes.txt" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("hello world", result.Output);
    }

    [Fact]
    public async Task FileReadTool_WhenFileDoesNotExist_FailsWithoutThrowing()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxFileReadTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["path"] = "missing.txt" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.FailureReason);
    }

    [Fact]
    public async Task FileReadTool_WhenPathEscapesWorkspace_Throws()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxFileReadTool(workspace.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["path"] = "../../../etc/passwd" }, CancellationToken.None));
    }

    // ── file_write ───────────────────────────────────────────────────────────

    [Fact]
    public async Task FileWriteTool_CreatesParentDirectoriesAndWritesContent()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxFileWriteTool(workspace.Path);

        var result = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["path"] = "src/new/file.cs", ["content"] = "class C {}" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("class C {}", File.ReadAllText(Path.Combine(workspace.Path, "src", "new", "file.cs")));
    }

    [Fact]
    public async Task FileWriteTool_OverwritesAnExistingFile()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Path, "existing.txt");
        File.WriteAllText(path, "old");
        var tool = new SandboxFileWriteTool(workspace.Path);

        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["path"] = "existing.txt", ["content"] = "new" }, CancellationToken.None);

        Assert.Equal("new", File.ReadAllText(path));
    }

    // ── file_edit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileEditTool_ReplacesAUniqueMatch()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Path, "code.cs");
        File.WriteAllText(path, "int Add(int a, int b) => a - b;");
        var tool = new SandboxFileEditTool(workspace.Path);

        var result = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["path"] = "code.cs", ["old_text"] = "a - b", ["new_text"] = "a + b" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("int Add(int a, int b) => a + b;", File.ReadAllText(path));
    }

    [Fact]
    public async Task FileEditTool_WhenOldTextOccursMultipleTimes_FailsWithoutEditing()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Path, "code.cs");
        File.WriteAllText(path, "x = 1; x = 1;");
        var tool = new SandboxFileEditTool(workspace.Path);

        var result = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["path"] = "code.cs", ["old_text"] = "x = 1;", ["new_text"] = "x = 2;" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("occurs 2 times", result.FailureReason);
        Assert.Equal("x = 1; x = 1;", File.ReadAllText(path));
    }

    [Fact]
    public async Task FileEditTool_WhenOldTextNotFound_Fails()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Path, "code.cs");
        File.WriteAllText(path, "unchanged");
        var tool = new SandboxFileEditTool(workspace.Path);

        var result = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["path"] = "code.cs", ["old_text"] = "not present", ["new_text"] = "x" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("was not found", result.FailureReason);
    }

    // ── git ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GitTool_Init_CreatesRepoAndConfiguresIdentity()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxGitTool(null, null, null, workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "init" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(Path.Combine(workspace.Path, ".git")));
    }

    [Fact]
    public async Task GitTool_AddCommit_RecordsACommit()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxGitTool(null, null, null, workspace.Path);
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "init" }, CancellationToken.None);
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "content");

        var added = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "add" }, CancellationToken.None);
        Assert.True(added.Succeeded);

        var committed = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["operation"] = "commit", ["message"] = "add a.txt" },
            CancellationToken.None);
        Assert.True(committed.Succeeded);

        var log = await SandboxTestProcess.RunAsync("git", ["log", "--oneline"], workspace.Path);
        Assert.Contains("add a.txt", log.CombinedOutput);
    }

    [Fact]
    public async Task GitTool_Checkout_CreatesABranchWhenItDoesNotExist()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxGitTool(null, null, null, workspace.Path);
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "init" }, CancellationToken.None);
        await SandboxTestProcess.RunAsync("git", ["commit", "--allow-empty", "-m", "root"], workspace.Path);

        var result = await tool.ExecuteAsync(
            Context(),
            new Dictionary<string, string> { ["operation"] = "checkout", ["branch"] = "feature/x" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var branch = await SandboxTestProcess.RunAsync("git", ["branch", "--show-current"], workspace.Path);
        Assert.Equal("feature/x", branch.StandardOutput.Trim());
    }

    [Fact]
    public async Task GitTool_Push_PublishesCommitsToALocalRemote()
    {
        using var workspace = new TempWorkspace();
        using var remote = new TempWorkspace();
        await SandboxTestProcess.RunAsync("git", ["init", "--bare", "."], remote.Path);

        var tool = new SandboxGitTool(null, null, null, workspace.Path);
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "init" }, CancellationToken.None);
        await SandboxTestProcess.RunAsync("git", ["remote", "add", "origin", remote.Path], workspace.Path);
        File.WriteAllText(Path.Combine(workspace.Path, "a.txt"), "content");
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "add" }, CancellationToken.None);
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "commit", ["message"] = "initial" }, CancellationToken.None);

        var result = await tool.ExecuteAsync(
            Context(runId: "run_push"),
            new Dictionary<string, string> { ["operation"] = "push", ["branch"] = "agentwerke/run-run_push" },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var branches = await SandboxTestProcess.RunAsync("git", ["branch", "--list", "agentwerke/run-run_push"], remote.Path);
        Assert.Contains("agentwerke/run-run_push", branches.StandardOutput);
    }

    [Fact]
    public async Task GitTool_Clone_WhenRepositoryNotConfigured_FailsWithActionableMessage()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxGitTool(repositoryOwner: null, repositoryName: null, personalAccessToken: null, workspaceRoot: workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "clone" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.FailureReason);
    }

    [Fact]
    public async Task GitTool_Clone_WhenWorkspaceAlreadyHasAGitCheckout_SkipsWithoutError()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxGitTool(null, null, null, workspace.Path);
        await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "init" }, CancellationToken.None);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["operation"] = "clone" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("already a git checkout", result.Output);
    }

    [Fact]
    public void GitTool_Validate_RejectsAnUnsupportedOperation()
    {
        var tool = new SandboxGitTool(null, null, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["operation"] = "rebase" }));
        Assert.Contains("Unsupported git operation", ex.Message);
    }

    [Fact]
    public void GitTool_Validate_RequiresMessageForCommit()
    {
        var tool = new SandboxGitTool(null, null, null);

        Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["operation"] = "commit" }));
    }

    // ── shell ────────────────────────────────────────────────────────────────

    [Fact]
    public void ShellTool_Validate_RejectsACommandNotOnTheAllowlist()
    {
        var tool = new SandboxShellTool();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["command"] = "rm -rf /" }));
        Assert.Contains("not allow-listed", ex.Message);
    }

    [Fact]
    public async Task ShellTool_RunsAnAllowedCommandAndCapturesOutput()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxShellTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["command"] = "dotnet --version" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("exit_code: 0", result.Output);
    }

    [Fact]
    public async Task ShellTool_WhenCommandFails_ReportsNonZeroExitWithoutThrowing()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxShellTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["command"] = "dotnet not-a-real-command" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureReason);
    }

    // ── run_tests ────────────────────────────────────────────────────────────

    [Fact]
    public void RunTestsTool_Validate_RejectsACommandNotOnTheAllowlist()
    {
        var tool = new SandboxRunTestsTool();

        Assert.Throws<InvalidOperationException>(() =>
            tool.Validate(new Dictionary<string, string> { ["command"] = "curl https://example.test" }));
    }

    [Fact]
    public async Task RunTestsTool_WhenCommandSucceeds_ReportsPassed()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxRunTestsTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["command"] = "dotnet --version" }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.StartsWith("PASSED", result.Output);
    }

    [Fact]
    public async Task RunTestsTool_WhenCommandFails_ReportsFailed()
    {
        using var workspace = new TempWorkspace();
        var tool = new SandboxRunTestsTool(workspace.Path);

        var result = await tool.ExecuteAsync(Context(), new Dictionary<string, string> { ["command"] = "dotnet not-a-real-command" }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.StartsWith("FAILED", result.Output);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("agentwerke-sandbox-tool-tests-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}

/// <summary>Test-only process helper, separate from the internal one in SandboxCodeTools.cs.</summary>
internal static class SandboxTestProcess
{
    public static async Task<(string StandardOutput, string CombinedOutput)> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
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

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var stdOut = await process.StandardOutput.ReadToEndAsync();
        var stdErr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var combined = stdErr.Length == 0 ? stdOut : $"{stdOut}\n{stdErr}";
        return (stdOut, combined);
    }
}
