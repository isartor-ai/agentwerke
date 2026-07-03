using Agentwerke.Api.Contracts;
using Agentwerke.Api.Contracts.Templates;
using Agentwerke.Api.Controllers;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Tests;

public sealed class TemplatesControllerTests
{
    [Fact]
    public void List_ReturnsOkWithAllTemplates()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        var result = Assert.IsType<OkObjectResult>(controller.List());
        var items = Assert.IsAssignableFrom<IEnumerable<TemplateSummary>>(result.Value);

        Assert.Equal(2, items.Count());
    }

    [Fact]
    public void Get_KnownId_ReturnsOkWithDetail()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        var result = Assert.IsType<OkObjectResult>(controller.Get("alpha"));
        var detail = Assert.IsType<TemplateDetail>(result.Value);

        Assert.Equal("alpha", detail.Id);
        Assert.NotEmpty(detail.BpmnXml);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNotFound()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        Assert.IsType<NotFoundResult>(controller.Get("ghost"));
    }

    [Fact]
    public async Task Clone_KnownId_Returns201WithWorkflowId()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        var result = await controller.Clone("alpha", new CloneTemplateRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<CloneTemplateResponse>(created.Value);
        Assert.Equal("wf_test", response.WorkflowId);
        Assert.Equal("Alpha", response.Name);
    }

    [Fact]
    public async Task Clone_UnknownId_Returns404()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        var result = await controller.Clone("ghost", null, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Clone_NullBody_StillSucceeds()
    {
        var catalog = new FakeTemplateCatalogService();
        var controller = new TemplatesController(catalog);

        var result = await controller.Clone("alpha", null, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public void ToTemplateSummary_MapsAllFields()
    {
        var template = SdlcTemplateSeeds.IssueToPr;

        var summary = ApiContractMappings.ToTemplateSummary(template);

        Assert.Equal(template.Id, summary.Id);
        Assert.Equal(template.Name, summary.Name);
        Assert.Equal(template.Description, summary.Description);
        Assert.Equal(template.Trigger, summary.Trigger);
        Assert.Equal(template.PolicyLevel, summary.PolicyLevel);
        Assert.Equal(template.Tags.ToArray(), summary.Tags);
        Assert.Equal(template.AgentRoles.ToArray(), summary.AgentRoles);
        Assert.Equal(template.ApprovalRoles.ToArray(), summary.ApprovalRoles);
    }

    [Fact]
    public void ToTemplateDetail_MapsAllFieldsIncludingBpmnXml()
    {
        var template = SdlcTemplateSeeds.SecurityReview;

        var detail = ApiContractMappings.ToTemplateDetail(template);

        Assert.Equal(template.Id, detail.Id);
        Assert.Equal(template.BpmnXml, detail.BpmnXml);
        Assert.Equal(template.RequiredInputs.ToArray(), detail.RequiredInputs);
        Assert.Equal(template.EvidenceExpectations.ToArray(), detail.EvidenceExpectations);
    }

    private sealed class FakeTemplateCatalogService : ITemplateCatalogService
    {
        private static readonly SdlcTemplate Alpha = new()
        {
            Id = "alpha",
            Name = "Alpha",
            Description = "Alpha template",
            Trigger = "manual",
            PolicyLevel = "standard",
            Tags = ["test"],
            AgentRoles = ["agent-a"],
            ApprovalRoles = ["approver-a"],
            RequiredInputs = ["input_a"],
            EvidenceExpectations = ["evidence_a"],
            BpmnXml = "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" />",
        };

        private static readonly SdlcTemplate Beta = new()
        {
            Id = "beta",
            Name = "Beta",
            Description = "Beta template",
            Trigger = "manual",
            PolicyLevel = "elevated",
            Tags = ["test"],
            AgentRoles = ["agent-b"],
            ApprovalRoles = ["approver-b"],
            RequiredInputs = [],
            EvidenceExpectations = [],
            BpmnXml = "<bpmn:definitions xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" />",
        };

        private static readonly Dictionary<string, SdlcTemplate> _templates =
            new(StringComparer.OrdinalIgnoreCase) { ["alpha"] = Alpha, ["beta"] = Beta };

        public IReadOnlyList<SdlcTemplate> ListTemplates() => [Alpha, Beta];

        public SdlcTemplate? GetTemplate(string templateId) =>
            _templates.TryGetValue(templateId, out var t) ? t : null;

        public Task<CloneTemplateResult> CloneTemplateAsync(
            CloneTemplateCommand command,
            CancellationToken cancellationToken = default)
        {
            if (!_templates.ContainsKey(command.TemplateId))
            {
                throw new TemplateNotFoundException(command.TemplateId);
            }

            return Task.FromResult(new CloneTemplateResult("wf_test", "Alpha"));
        }
    }
}
