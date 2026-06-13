using Autofac.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/agents")]
public sealed class AgentsController : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        var profiles = AgentRegistry.All().Select(p => new
        {
            agentId = p.AgentId,
            name = p.Name,
            description = p.Description,
            category = p.Category,
            skills = p.Skills.Select(s => new
            {
                skillId = s.SkillId,
                name = s.Name,
                description = s.Description,
                supportedActions = s.SupportedActions
            }),
            supportedEnvironments = p.SupportedEnvironments,
            supportedPolicyTags = p.SupportedPolicyTags
        });

        return Ok(profiles);
    }

    [HttpGet("{agentId}")]
    public IActionResult Get(string agentId)
    {
        var profile = AgentRegistry.Find(agentId);
        if (profile is null)
        {
            return NotFound(new { message = $"Agent '{agentId}' not found." });
        }

        return Ok(new
        {
            agentId = profile.AgentId,
            name = profile.Name,
            description = profile.Description,
            category = profile.Category,
            skills = profile.Skills.Select(s => new
            {
                skillId = s.SkillId,
                name = s.Name,
                description = s.Description,
                supportedActions = s.SupportedActions
            }),
            supportedEnvironments = profile.SupportedEnvironments,
            supportedPolicyTags = profile.SupportedPolicyTags
        });
    }
}
