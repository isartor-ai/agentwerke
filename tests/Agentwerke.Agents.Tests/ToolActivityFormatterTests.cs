using System.Collections.Generic;
using Agentwerke.Agents.Models;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Agents.Tests;

public sealed class ToolActivityFormatterTests
{
    [Fact]
    public void DescribeInput_FileWrite_ShowsPathAndSize_NotContent()
    {
        var detail = ToolActivityFormatter.DescribeInput("sandbox.file_write", new Dictionary<string, string>
        {
            ["path"] = "src/App.tsx",
            ["content"] = new string('x', 340),
        });

        Assert.NotNull(detail);
        Assert.Contains("src/App.tsx", detail);
        Assert.Contains("340 B", detail);
        // The file body must never appear in the activity detail.
        Assert.DoesNotContain("xxxx", detail);
    }

    [Fact]
    public void DescribeInput_Shell_ShowsCommand()
    {
        var detail = ToolActivityFormatter.DescribeInput("sandbox.shell", new Dictionary<string, string>
        {
            ["command"] = "npm test",
        });

        Assert.Equal("npm test", detail);
    }

    [Fact]
    public void DescribeInput_PullRequest_ShowsBranchAndTitle()
    {
        var detail = ToolActivityFormatter.DescribeInput("github.create_pull_request", new Dictionary<string, string>
        {
            ["head"] = "agentwerke/todo-8",
            ["title"] = "Add dark mode toggle",
        });

        Assert.NotNull(detail);
        Assert.Contains("agentwerke/todo-8", detail);
        Assert.Contains("Add dark mode toggle", detail);
    }

    [Fact]
    public void DescribeInput_GenericTool_RedactsSecretAndContentValues()
    {
        var detail = ToolActivityFormatter.DescribeInput("custom.tool", new Dictionary<string, string>
        {
            ["token"] = "ghp_supersecretvalue",
            ["region"] = "eu-west",
            ["run_id"] = "run_123",
        });

        Assert.NotNull(detail);
        Assert.DoesNotContain("supersecret", detail);
        Assert.Contains("region=eu-west", detail);
        // Run-scoped plumbing the model didn't choose is not surfaced.
        Assert.DoesNotContain("run_123", detail);
    }

    [Fact]
    public void DescribeResult_UsesErrorThenArtifactsAndSummary()
    {
        var failed = ToolActivityFormatter.DescribeResult(new AgentToolInvocationRecord
        {
            ToolName = "sandbox.shell",
            Status = "failed",
            ErrorMessage = "exit code 1: test failed",
        });
        Assert.Equal("exit code 1: test failed", failed);

        var ok = ToolActivityFormatter.DescribeResult(new AgentToolInvocationRecord
        {
            ToolName = "github.create_pull_request",
            Status = "completed",
            OutputSummary = "opened PR #42",
            ArtifactNames = ["pr-42.json"],
        });
        Assert.NotNull(ok);
        Assert.Contains("pr-42.json", ok);
        Assert.Contains("opened PR #42", ok);
    }
}
