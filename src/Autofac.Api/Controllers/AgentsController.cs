using Autofac.Api.Auth;
using Autofac.Api.Contracts.Agents;
using Autofac.Agents;
using Autofac.Agents.Skills;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/agents")]
[Authorize(Policy = AutofacPolicies.Viewer)]
public sealed class AgentsController : ControllerBase
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IAgentRegistryEditor _editor;
    private readonly ISkillRepository _skillRepository;

    public AgentsController(
        IAgentRegistry agentRegistry,
        IAgentRegistryEditor editor,
        ISkillRepository skillRepository)
    {
        _agentRegistry = agentRegistry;
        _editor = editor;
        _skillRepository = skillRepository;
    }

    [HttpGet]
    public IActionResult List()
    {
        var profiles = _agentRegistry.All().Select(ToSummary);
        return Ok(profiles);
    }

    [HttpGet("{agentId}")]
    public IActionResult Get(string agentId)
    {
        var document = _editor.Find(agentId);
        if (document is null)
        {
            return NotFound(new { message = $"Agent '{agentId}' not found." });
        }

        return Ok(ToDetail(document));
    }

    [Authorize(Policy = AutofacPolicies.Admin)]
    [HttpPut("{agentId}")]
    public IActionResult Upsert(string agentId, [FromBody] UpsertAgentRequest request)
    {
        if (!string.Equals(agentId, request.AgentId, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Route agent id must match the body agent id." });
        }

        try
        {
            var saved = _editor.Save(ToProfile(request));
            return Ok(ToDetail(saved));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AutofacPolicies.Admin)]
    [HttpPost("upload")]
    public IActionResult Upload([FromBody] UploadAgentRequest request)
    {
        try
        {
            var saved = _editor.Upload(request.FileName, request.Content);
            return CreatedAtAction(nameof(Get), new { agentId = saved.Profile.AgentId }, ToDetail(saved));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private AgentSummary ToSummary(AgentProfile profile) =>
        new(
            profile.AgentId,
            profile.Name,
            profile.Description,
            profile.Category,
            profile.Runner,
            profile.Model,
            profile.DockerImage,
            profile.Network,
            profile.Tools.ToArray(),
            profile.DeniedTools.ToArray(),
            profile.SupportedActions.ToArray(),
            profile.Skills.Select(ResolveSkillBinding).ToArray(),
            profile.SupportedEnvironments.ToArray(),
            profile.SupportedPolicyTags.ToArray(),
            profile.Secrets.ToArray(),
            profile.Source,
            profile.Fingerprint);

    private AgentDetail ToDetail(ManagedAgentDocument document)
    {
        var profile = document.Profile;
        return new AgentDetail(
            profile.AgentId,
            profile.Name,
            profile.Description,
            profile.Category,
            profile.Runner,
            profile.Model,
            profile.DockerImage,
            profile.Network,
            profile.Tools.ToArray(),
            profile.DeniedTools.ToArray(),
            profile.SupportedActions.ToArray(),
            profile.Skills.Select(ResolveSkillBinding).ToArray(),
            profile.SupportedEnvironments.ToArray(),
            profile.SupportedPolicyTags.ToArray(),
            profile.Secrets.ToArray(),
            profile.Source,
            profile.Fingerprint,
            profile.SystemPrompt,
            document.RawMarkdown,
            document.EffectiveFilePath,
            document.SourceFilePath);
    }

    private AgentSkillBinding ResolveSkillBinding(AgentSkillRef skill)
    {
        var manifest = !string.IsNullOrWhiteSpace(skill.SkillManifestId)
            ? _skillRepository.FindById(skill.SkillManifestId)
            : _skillRepository.FindByReference(skill.SkillId)
                ?? (string.IsNullOrWhiteSpace(skill.Name) ? null : _skillRepository.FindByReference(skill.Name));

        return new AgentSkillBinding(
            skill.SkillId,
            manifest?.Name ?? skill.Name,
            manifest?.Description ?? skill.Description,
            skill.SupportedActions.ToArray(),
            skill.SkillManifestId ?? manifest?.SkillId);
    }

    private static AgentProfile ToProfile(UpsertAgentRequest request)
    {
        var supportedActions = NormalizeList(request.SupportedActions);

        return new AgentProfile
        {
            AgentId = NormalizeRequiredScalar(request.AgentId, "Agent id"),
            Name = NormalizeRequiredScalar(request.Name, "Agent name"),
            Description = NormalizeOptionalScalar(request.Description) ?? string.Empty,
            Category = NormalizeRequiredScalar(request.Category, "Agent category"),
            Runner = NormalizeRequiredScalar(request.Runner, "Agent runner"),
            Model = NormalizeOptionalScalar(request.Model),
            DockerImage = NormalizeOptionalScalar(request.DockerImage),
            Network = NormalizeOptionalScalar(request.Network) ?? "none",
            Skills = (request.Skills ?? [])
                .Select(skill => new AgentSkillRef(
                    NormalizeRequiredScalar(skill.SkillId, "Skill id"),
                    NormalizeOptionalScalar(skill.Name) ?? NormalizeRequiredScalar(skill.SkillId, "Skill id"),
                    NormalizeOptionalScalar(skill.Description) ?? string.Empty,
                    NormalizeList(skill.SupportedActions).Count == 0 ? supportedActions : NormalizeList(skill.SupportedActions),
                    NormalizeOptionalScalar(skill.SkillManifestId)))
                .ToArray(),
            Tools = NormalizeList(request.Tools),
            DeniedTools = NormalizeList(request.DeniedTools),
            Secrets = NormalizeList(request.Secrets),
            SupportedActions = supportedActions,
            SupportedEnvironments = NormalizeList(request.SupportedEnvironments),
            SupportedPolicyTags = NormalizeList(request.SupportedPolicyTags),
            SystemPrompt = NormalizeOptionalMultiline(request.SystemPrompt),
            Source = "file"
        };
    }

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Select(static value => value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeRequiredScalar(string? value, string fieldName)
    {
        var normalized = NormalizeOptionalScalar(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalScalar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ReplaceLineEndings(" ").Trim();
    }

    private static string? NormalizeOptionalMultiline(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
