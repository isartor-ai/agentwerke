using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;

namespace Autofac.Application.Tests;

public sealed class TemplateCatalogServiceTests
{
    private static readonly WorkflowValidationResult ValidResult = new(
        true, "proc", "Proc", [], []);

    [Fact]
    public void ListTemplates_ReturnsAllSevenSeededTemplates()
    {
        var service = BuildService();

        var templates = service.ListTemplates();

        Assert.Equal(7, templates.Count);
        Assert.Contains(templates, static t => t.Id == "issue-to-pr");
        Assert.Contains(templates, static t => t.Id == "bugfix");
        Assert.Contains(templates, static t => t.Id == "hotfix");
        Assert.Contains(templates, static t => t.Id == "deployment-approval");
        Assert.Contains(templates, static t => t.Id == "security-review");
        Assert.Contains(templates, static t => t.Id == "release-approval");
        Assert.Contains(templates, static t => t.Id == "autonomous-sdlc");
    }

    [Fact]
    public void GetTemplate_KnownId_ReturnsTemplate()
    {
        var service = BuildService();

        var template = service.GetTemplate("issue-to-pr");

        Assert.NotNull(template);
        Assert.Equal("issue-to-pr", template!.Id);
        Assert.Equal("Issue to Pull Request", template.Name);
        Assert.NotEmpty(template.BpmnXml);
    }

    [Fact]
    public void GetTemplate_UnknownId_ReturnsNull()
    {
        var service = BuildService();

        Assert.Null(service.GetTemplate("no-such-template"));
    }

    [Fact]
    public void GetTemplate_IsCaseInsensitive()
    {
        var service = BuildService();

        Assert.NotNull(service.GetTemplate("ISSUE-TO-PR"));
        Assert.NotNull(service.GetTemplate("Issue-To-Pr"));
    }

    [Fact]
    public async Task CloneTemplateAsync_KnownId_DelegatesToAuthoringService()
    {
        var authoring = new StubWorkflowAuthoringService(ValidResult);
        var service = new TemplateCatalogService(authoring);

        var result = await service.CloneTemplateAsync(new CloneTemplateCommand("bugfix"));

        Assert.Equal("wf_stub", result.WorkflowId);
        Assert.Equal("Bugfix", result.Name);
        Assert.Equal(1, authoring.ImportCallCount);

        var cmd = authoring.LastImportCommand!;
        Assert.Equal("bugfix.bpmn", cmd.FileName);
        Assert.Contains("<bpmn:", cmd.BpmnXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CloneTemplateAsync_OverridesNameAndDescription()
    {
        var authoring = new StubWorkflowAuthoringService(ValidResult);
        var service = new TemplateCatalogService(authoring);

        var result = await service.CloneTemplateAsync(
            new CloneTemplateCommand("bugfix", Name: "My Bugfix", Description: "custom", Owner: "alice"));

        Assert.Equal("My Bugfix", result.Name);
        Assert.Equal("custom", authoring.LastImportCommand!.Description);
        Assert.Equal("alice", authoring.LastImportCommand.Owner);
    }

    [Fact]
    public async Task CloneTemplateAsync_UnknownId_ThrowsTemplateNotFoundException()
    {
        var service = BuildService();

        var ex = await Assert.ThrowsAsync<TemplateNotFoundException>(() =>
            service.CloneTemplateAsync(new CloneTemplateCommand("ghost")));

        Assert.Equal("ghost", ex.TemplateId);
    }

    [Theory]
    [InlineData("issue-to-pr")]
    [InlineData("bugfix")]
    [InlineData("hotfix")]
    [InlineData("deployment-approval")]
    [InlineData("security-review")]
    [InlineData("release-approval")]
    [InlineData("autonomous-sdlc")]
    public void AllTemplates_HaveNonEmptyRequiredMetadata(string id)
    {
        var template = SdlcTemplateSeeds.All.Single(t => t.Id == id);

        Assert.NotEmpty(template.Id);
        Assert.NotEmpty(template.Name);
        Assert.NotEmpty(template.Description);
        Assert.NotEmpty(template.Trigger);
        Assert.NotEmpty(template.PolicyLevel);
        Assert.NotEmpty(template.BpmnXml);
        Assert.NotEmpty(template.Tags);
        Assert.NotEmpty(template.AgentRoles);
    }

    [Theory]
    [InlineData("hotfix", "critical")]
    [InlineData("release-approval", "critical")]
    [InlineData("deployment-approval", "elevated")]
    [InlineData("security-review", "elevated")]
    [InlineData("autonomous-sdlc", "elevated")]
    [InlineData("issue-to-pr", "standard")]
    [InlineData("bugfix", "standard")]
    public void Templates_HaveExpectedPolicyLevel(string id, string expectedLevel)
    {
        var template = SdlcTemplateSeeds.All.Single(t => t.Id == id);

        Assert.Equal(expectedLevel, template.PolicyLevel);
    }

    private static TemplateCatalogService BuildService() =>
        new(new StubWorkflowAuthoringService(ValidResult));

    private sealed class StubWorkflowAuthoringService : IWorkflowAuthoringService
    {
        private readonly WorkflowValidationResult _validation;

        public StubWorkflowAuthoringService(WorkflowValidationResult validation)
        {
            _validation = validation;
        }

        public int ImportCallCount { get; private set; }
        public ImportWorkflowCommand? LastImportCommand { get; private set; }

        public Task<WorkflowImportResult> ImportWorkflowAsync(
            ImportWorkflowCommand command,
            CancellationToken cancellationToken = default)
        {
            ImportCallCount++;
            LastImportCommand = command;
            return Task.FromResult(new WorkflowImportResult("wf_stub", _validation));
        }

        public Task<IReadOnlyList<WorkflowDefinition>> ListWorkflowsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);

        public Task<WorkflowDefinition?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default)
            => Task.FromResult<WorkflowDefinition?>(null);

        public WorkflowValidationResult ValidateWorkflow(string bpmnXml) => _validation;

        public Task<WorkflowPublishResult> PublishWorkflowAsync(
            string workflowId,
            PublishWorkflowCommand command,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkflowPublishResult(workflowId, "v1.0.0", DateTimeOffset.UtcNow.ToString("o")));
    }
}
