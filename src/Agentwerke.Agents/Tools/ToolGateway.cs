using System.Diagnostics;
using System.Text.Json;
using Agentwerke.AgentSecOps;
using Agentwerke.Domain.AgentRuntime;
using Agentwerke.Domain.Persistence;
using Agentwerke.Sandboxes;

namespace Agentwerke.Agents.Tools;

public interface IToolGateway
{
    Task<ToolGatewayResult> ExecuteAsync(ToolGatewayRequest request, CancellationToken cancellationToken);
}

public sealed class ToolGateway : IToolGateway
{
    private const string SandboxExecuteToolName = "sandbox.execute";
    private const string SandboxProfileInputKey = "sandbox_profile";
    private const string SandboxProfileRationaleInputKey = "sandbox_profile_rationale";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IToolRegistry _toolRegistry;
    private readonly IPolicyEvaluationService _policyEvaluationService;
    private readonly ISandboxProfileSelector _sandboxProfileSelector;

    public ToolGateway(
        IToolRegistry toolRegistry,
        IPolicyEvaluationService policyEvaluationService,
        ISandboxProfileSelector sandboxProfileSelector)
    {
        _toolRegistry = toolRegistry;
        _policyEvaluationService = policyEvaluationService;
        _sandboxProfileSelector = sandboxProfileSelector;
    }

    public async Task<ToolGatewayResult> ExecuteAsync(ToolGatewayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tool = _toolRegistry.Find(request.ToolName);
        if (tool is null)
        {
            return Failure(
                request,
                Category: AgentToolCategories.Read,
                FailureReason: $"Tool '{request.ToolName}' is not registered.",
                Status: "missing");
        }

        if (IsDenied(request, tool, out var permissionFailure))
        {
            return Failure(request, tool.Category, permissionFailure, "blocked");
        }

        try
        {
            tool.Validate(request.Input);
        }
        catch (Exception ex)
        {
            return Failure(request, tool.Category, ex.Message, "invalid_input");
        }

        var policyDecision = _policyEvaluationService.Evaluate(new PolicyEvaluationRequest(
            AgentName: request.AgentName,
            Action: request.Action,
            Environment: request.Environment,
            PurposeType: request.PurposeType,
            PolicyTag: request.PolicyTag,
            RequiresEvidence: request.RequiresEvidence,
            Attempt: request.Attempt));

        if (!string.Equals(policyDecision.Kind, "allow", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolGatewayResult(
                Succeeded: false,
                Output: null,
                FailureReason: policyDecision.Rationale,
                PolicyDecision: policyDecision,
                Invocation: CreateInvocation(
                    request,
                    tool.Category,
                    Status: "blocked",
                    policyDecision.PolicyId,
                    policyDecision.Kind,
                    InputSummary: Serialize(request.Input),
                    OutputSummary: null,
                    ErrorMessage: policyDecision.Rationale,
                    DurationMs: 0,
                    ArtifactNames: []));
        }

        if (string.Equals(request.ToolName, SandboxExecuteToolName, StringComparison.OrdinalIgnoreCase))
        {
            var (gatedRequest, rejection) = GateSandboxProfile(request, tool.Category, policyDecision);
            if (rejection is not null)
            {
                return rejection;
            }

            request = gatedRequest;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await tool.ExecuteAsync(
                new AgentToolExecutionContext(
                    request.RunId,
                    request.StepId,
                    request.AgentName,
                    request.Action,
                    request.Environment,
                    request.PurposeType,
                    request.PolicyTag,
                    request.Attempt),
                request.Input,
                cancellationToken);

            stopwatch.Stop();
            var artifactNames = (result.Artifacts?.Keys ?? []).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new ToolGatewayResult(
                Succeeded: result.Succeeded,
                Output: result.Output,
                FailureReason: result.FailureReason,
                PolicyDecision: policyDecision,
                Invocation: CreateInvocation(
                    request,
                    tool.Category,
                    Status: result.Succeeded ? "completed" : "failed",
                    policyDecision.PolicyId,
                    policyDecision.Kind,
                    InputSummary: Serialize(request.Input),
                    OutputSummary: result.Output,
                    ErrorMessage: result.FailureReason,
                    DurationMs: (int)stopwatch.ElapsedMilliseconds,
                    ArtifactNames: artifactNames),
                Artifacts: result.Artifacts,
                ExternalActions: result.ExternalActions,
                SandboxExecution: result.SandboxExecution);
        }
        catch (AgentInteractionRequiredException)
        {
            // A blocking tool is asking for human/agent input (#192). This is control flow, not a
            // tool failure — let it unwind so the orchestrator can suspend the run for re-run.
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ToolGatewayResult(
                Succeeded: false,
                Output: null,
                FailureReason: ex.Message,
                PolicyDecision: policyDecision,
                Invocation: CreateInvocation(
                    request,
                    tool.Category,
                    Status: "failed",
                    policyDecision.PolicyId,
                    policyDecision.Kind,
                    InputSummary: Serialize(request.Input),
                    OutputSummary: null,
                    ErrorMessage: ex.Message,
                    DurationMs: (int)stopwatch.ElapsedMilliseconds,
                    ArtifactNames: []));
        }
    }

    private (ToolGatewayRequest Request, ToolGatewayResult? Rejection) GateSandboxProfile(
        ToolGatewayRequest request,
        string toolCategory,
        PolicyDecision policyDecision)
    {
        var requestedProfile = request.Input.TryGetValue(SandboxProfileInputKey, out var rawProfile) && !string.IsNullOrWhiteSpace(rawProfile)
            ? rawProfile
            : SandboxProfileCatalog.Default;

        if (!SandboxProfileCatalog.TryGet(requestedProfile, out _))
        {
            var unknownRationale = $"Unknown sandbox profile '{requestedProfile}'. Known profiles: {string.Join(", ", SandboxProfileCatalog.Names)}.";
            return (request, RejectSandboxProfile(request, toolCategory, policyDecision, unknownRationale));
        }

        var selection = _sandboxProfileSelector.Select(new SandboxProfileSelectionRequest(
            AgentName: request.AgentName,
            Action: request.Action,
            RequestedProfile: requestedProfile,
            AgentAllowedProfiles: request.AllowedSandboxProfiles ?? [],
            Environment: request.Environment,
            PolicyTag: request.PolicyTag,
            PurposeType: request.PurposeType,
            RiskLevel: policyDecision.RiskLevel));

        if (!selection.Allowed)
        {
            return (request, RejectSandboxProfile(request, toolCategory, policyDecision, selection.Rationale));
        }

        var enrichedInput = new Dictionary<string, string>(request.Input, StringComparer.OrdinalIgnoreCase)
        {
            [SandboxProfileInputKey] = selection.SelectedProfile!,
            [SandboxProfileRationaleInputKey] = selection.Rationale
        };

        return (request with { Input = enrichedInput }, null);
    }

    private static ToolGatewayResult RejectSandboxProfile(
        ToolGatewayRequest request,
        string toolCategory,
        PolicyDecision policyDecision,
        string rationale)
    {
        return new ToolGatewayResult(
            Succeeded: false,
            Output: null,
            FailureReason: rationale,
            PolicyDecision: policyDecision,
            Invocation: CreateInvocation(
                request,
                toolCategory,
                Status: "profile_rejected",
                policyDecision.PolicyId,
                policyDecision.Kind,
                InputSummary: Serialize(request.Input),
                OutputSummary: null,
                ErrorMessage: rationale,
                DurationMs: 0,
                ArtifactNames: []));
    }

    private static bool IsDenied(ToolGatewayRequest request, IAgentTool tool, out string failureReason)
    {
        if (request.DeniedTools.Contains(request.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            failureReason = $"Tool '{request.ToolName}' is denied by the runtime contract.";
            return true;
        }

        if (request.AllowedTools.Count > 0 &&
            !request.AllowedTools.Contains(request.ToolName, StringComparer.OrdinalIgnoreCase))
        {
            failureReason = $"Tool '{request.ToolName}' is not included in the runtime contract allowlist.";
            return true;
        }

        if (string.Equals(tool.Category, AgentToolCategories.Shell, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(request.PermissionLevel, AgentPermissionLevels.ReadOnly, StringComparison.OrdinalIgnoreCase))
        {
            failureReason = $"Tool '{request.ToolName}' requires elevated permissions beyond '{request.PermissionLevel}'.";
            return true;
        }

        failureReason = string.Empty;
        return false;
    }

    private static ToolGatewayResult Failure(
        ToolGatewayRequest request,
        string Category,
        string FailureReason,
        string Status)
    {
        return new ToolGatewayResult(
            Succeeded: false,
            Output: null,
            FailureReason: FailureReason,
            PolicyDecision: null,
            Invocation: CreateInvocation(
                request,
                Category,
                Status,
                PolicyDecisionId: null,
                PolicyDecisionKind: null,
                InputSummary: Serialize(request.Input),
                OutputSummary: null,
                ErrorMessage: FailureReason,
                DurationMs: 0,
                ArtifactNames: []));
    }

    private static AgentToolInvocationRecord CreateInvocation(
        ToolGatewayRequest request,
        string Category,
        string Status,
        string? PolicyDecisionId,
        string? PolicyDecisionKind,
        string InputSummary,
        string? OutputSummary,
        string? ErrorMessage,
        int DurationMs,
        IReadOnlyList<string> ArtifactNames)
    {
        return new AgentToolInvocationRecord
        {
            ToolName = request.ToolName,
            Category = Category,
            Status = Status,
            PolicyDecisionId = PolicyDecisionId,
            PolicyDecisionKind = PolicyDecisionKind,
            InputSummary = InputSummary,
            OutputSummary = OutputSummary,
            ErrorMessage = ErrorMessage,
            DurationMs = DurationMs,
            ArtifactNames = ArtifactNames
        };
    }

    private static string Serialize(IReadOnlyDictionary<string, string> input) =>
        JsonSerializer.Serialize(input, SerializerOptions);
}
