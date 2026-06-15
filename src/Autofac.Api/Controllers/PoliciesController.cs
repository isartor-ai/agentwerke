using Autofac.AgentSecOps;
using Autofac.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize(Policy = AutofacPolicies.Admin)]
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
}
