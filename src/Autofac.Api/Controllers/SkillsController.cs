using Autofac.Agents.Skills;
using Autofac.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/skills")]
[Authorize(Policy = AutofacPolicies.Viewer)]
public sealed class SkillsController : ControllerBase
{
    private readonly ISkillRepository _repository;

    public SkillsController(ISkillRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public IActionResult List()
    {
        var skills = _repository.All().Select(ToSummary);
        return Ok(skills);
    }

    [HttpGet("{skillId}")]
    public IActionResult Get(string skillId)
    {
        var skill = _repository.FindById(skillId);
        if (skill is null)
        {
            return NotFound(new { message = $"Skill '{skillId}' not found." });
        }

        return Ok(new
        {
            skillId = skill.SkillId,
            name = skill.Name,
            description = skill.Description,
            version = skill.Version,
            invocationRules = skill.InvocationRules,
            requiredFiles = skill.RequiredFiles,
            optionalTools = skill.OptionalTools,
            fingerprint = skill.Fingerprint,
            filePath = skill.FilePath,
            content = skill.Content
        });
    }

    private static object ToSummary(SkillManifest skill) => new
    {
        skillId = skill.SkillId,
        name = skill.Name,
        description = skill.Description,
        version = skill.Version,
        invocationRules = skill.InvocationRules,
        requiredFiles = skill.RequiredFiles,
        optionalTools = skill.OptionalTools,
        fingerprint = skill.Fingerprint,
        filePath = skill.FilePath
    };
}
