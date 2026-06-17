using Autofac.Api.Contracts;
using Autofac.Api.Contracts.Templates;
using Autofac.Application.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/templates")]
public sealed class TemplatesController : ControllerBase
{
    private readonly ITemplateCatalogService _catalog;

    public TemplatesController(ITemplateCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public IActionResult List()
    {
        var summaries = _catalog.ListTemplates()
            .Select(ApiContractMappings.ToTemplateSummary)
            .ToList();

        return Ok(summaries);
    }

    [HttpGet("{templateId}")]
    public IActionResult Get(string templateId)
    {
        var template = _catalog.GetTemplate(templateId);

        if (template is null)
        {
            return NotFound();
        }

        return Ok(ApiContractMappings.ToTemplateDetail(template));
    }

    [HttpPost("{templateId}/clone")]
    public async Task<IActionResult> Clone(string templateId, [FromBody] CloneTemplateRequest? request, CancellationToken ct)
    {
        try
        {
            var result = await _catalog.CloneTemplateAsync(
                new CloneTemplateCommand(
                    TemplateId: templateId,
                    Name: request?.Name,
                    Description: request?.Description,
                    Owner: request?.Owner),
                ct);

            var response = new CloneTemplateResponse(result.WorkflowId, result.Name);
            return CreatedAtAction(
                actionName: nameof(Get),
                controllerName: "Workflows",
                routeValues: new { workflowId = result.WorkflowId },
                value: response);
        }
        catch (TemplateNotFoundException)
        {
            return NotFound();
        }
    }
}
