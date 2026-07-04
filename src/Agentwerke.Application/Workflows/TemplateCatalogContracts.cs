using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Workflows;

public interface ITemplateCatalogService
{
    IReadOnlyList<SdlcTemplate> ListTemplates();

    SdlcTemplate? GetTemplate(string templateId);

    Task<CloneTemplateResult> CloneTemplateAsync(
        CloneTemplateCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record CloneTemplateCommand(
    string TemplateId,
    string? Name = null,
    string? Description = null,
    string? Owner = null);

public sealed record CloneTemplateResult(string WorkflowId, string Name);

public sealed class TemplateNotFoundException : Exception
{
    public TemplateNotFoundException(string templateId)
        : base($"Template '{templateId}' was not found in the catalog.")
    {
        TemplateId = templateId;
    }

    public string TemplateId { get; }
}
