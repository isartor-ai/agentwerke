using Agentwerke.AgentSecOps;
using Agentwerke.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize(Policy = AgentwerkePolicies.Admin)]
public sealed class PoliciesController : ControllerBase
{
    private readonly IPolicyRuleStore _store;

    public PoliciesController(IPolicyRuleStore store)
    {
        _store = store;
    }

    [HttpGet]
    public IActionResult List()
    {
        return Ok(_store.GetSnapshot());
    }

    [HttpGet("{policyId}")]
    public IActionResult Get(string policyId)
    {
        var rule = _store.FindById(policyId);
        return rule is null ? NotFound() : Ok(rule);
    }

    [HttpPut("{policyId}")]
    public IActionResult Upsert(string policyId, [FromBody] PolicyRule rule)
    {
        if (!string.Equals(policyId, rule.Id, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Route policy id must match the body id." });
        }

        _store.Upsert(rule);
        return Ok(rule);
    }

    [HttpDelete("{policyId}")]
    public IActionResult Delete(string policyId)
    {
        return _store.Delete(policyId)
            ? NoContent()
            : NotFound();
    }

    /// <summary>
    /// Impact analysis (#34): replay scenarios against the active rules and a proposed
    /// rule set, returning the decisions that would change. Lets an operator validate a
    /// policy change before activating it. The store is not modified.
    /// </summary>
    [HttpPost("simulate")]
    public IActionResult Simulate([FromBody] PolicyRuleSimulationRequest? request)
    {
        if (request?.Scenarios is null || request.Scenarios.Count == 0)
        {
            return BadRequest(new { message = "At least one scenario is required." });
        }

        var current = _store.GetSnapshot();
        var proposed = request.ProposedRules ?? current;

        var scenarios = request.Scenarios
            .Select(s => new PolicySimulationScenario(
                string.IsNullOrWhiteSpace(s.Name) ? s.Action ?? string.Empty : s.Name,
                new PolicyEvaluationRequest(
                    AgentName: s.AgentName ?? string.Empty,
                    Action: s.Action ?? string.Empty,
                    Environment: s.Environment,
                    PurposeType: s.PurposeType ?? string.Empty,
                    PolicyTag: s.PolicyTag ?? string.Empty,
                    RequiresEvidence: s.RequiresEvidence ?? [],
                    Attempt: s.Attempt <= 0 ? 1 : s.Attempt)))
            .ToList();

        return Ok(PolicySimulator.Simulate(current, proposed, scenarios));
    }

    /// <summary>Activate a rule (lifecycle: draft → published). #34</summary>
    [HttpPost("{policyId}/publish")]
    public IActionResult Publish(string policyId) => SetEnabled(policyId, true);

    /// <summary>Revert a rule to draft (disabled) without deleting it. #34</summary>
    [HttpPost("{policyId}/unpublish")]
    public IActionResult Unpublish(string policyId) => SetEnabled(policyId, false);

    private IActionResult SetEnabled(string policyId, bool enabled)
    {
        var rule = _store.FindById(policyId);
        if (rule is null)
        {
            return NotFound();
        }

        rule.Enabled = enabled;
        _store.Upsert(rule);
        return Ok(rule);
    }
}

/// <summary>Request body for <c>POST /api/policies/simulate</c> (#34).</summary>
public sealed record PolicyRuleSimulationRequest(
    PolicyRuleSet? ProposedRules,
    IReadOnlyList<PolicyRuleSimulationScenario> Scenarios);

public sealed record PolicyRuleSimulationScenario(
    string? Name,
    string? AgentName,
    string? Action,
    string? Environment,
    string? PurposeType,
    string? PolicyTag,
    IReadOnlyList<string>? RequiresEvidence,
    int Attempt = 1);
