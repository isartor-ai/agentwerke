using Autofac.Api.Auth;
using Autofac.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Authorize(Policy = AutofacPolicies.Viewer)]
public sealed class AgentsController : ControllerBase
{
    private readonly IAgentRegistry _agentRegistry;

    public AgentsController(IAgentRegistry agentRegistry)
    {
        _agentRegistry = agentRegistry;
    }

    [HttpGet]
    public IActionResult List()
    {
        var profiles = _agentRegistry.All().Select(Project);
        return Ok(profiles);
    }

    [HttpGet("{agentId}")]
    public IActionResult Get(string agentId)
    {
        var profile = _agentRegistry.Find(agentId);
        if (profile is null)
        {
            return NotFound(new { message = $"Agent '{agentId}' not found." });
        }

        return Ok(Project(profile));
    }

    private static object Project(AgentProfile p) => new
    {
        agentId = p.AgentId,
        name = p.Name,
        description = p.Description,
        category = p.Category,
        runner = p.Runner,
        model = p.Model,
        dockerImage = p.DockerImage,
        tools = p.Tools,
        deniedTools = p.DeniedTools,
        supportedActions = p.SupportedActions,
        skills = p.Skills.Select(s => new
        {
            skillId = s.SkillId,
            name = s.Name,
            description = s.Description,
            supportedActions = s.SupportedActions,
            skillManifestId = s.SkillManifestId
        }),
        supportedEnvironments = p.SupportedEnvironments,
        supportedPolicyTags = p.SupportedPolicyTags,
        source = p.Source,
        fingerprint = p.Fingerprint
    };
}
