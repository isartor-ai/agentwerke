using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

public sealed class TemplateCatalogService : ITemplateCatalogService
{
    private static readonly Dictionary<string, SdlcTemplate> _catalog =
        SdlcTemplateSeeds.All.ToDictionary(static t => t.Id, StringComparer.OrdinalIgnoreCase);

    private readonly IWorkflowAuthoringService _authoring;

    public TemplateCatalogService(IWorkflowAuthoringService authoring)
    {
        _authoring = authoring;
    }

    public IReadOnlyList<SdlcTemplate> ListTemplates() => SdlcTemplateSeeds.All;

    public SdlcTemplate? GetTemplate(string templateId) =>
        _catalog.TryGetValue(templateId, out var t) ? t : null;

    public async Task<CloneTemplateResult> CloneTemplateAsync(
        CloneTemplateCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_catalog.TryGetValue(command.TemplateId, out var template))
        {
            throw new TemplateNotFoundException(command.TemplateId);
        }

        var name = string.IsNullOrWhiteSpace(command.Name) ? template.Name : command.Name;
        var result = await _authoring.ImportWorkflowAsync(
            new ImportWorkflowCommand(
                FileName: $"{command.TemplateId}.bpmn",
                BpmnXml: template.BpmnXml,
                Description: string.IsNullOrWhiteSpace(command.Description)
                    ? template.Description
                    : command.Description,
                Owner: command.Owner),
            cancellationToken);

        return new CloneTemplateResult(result.WorkflowId, name);
    }
}
