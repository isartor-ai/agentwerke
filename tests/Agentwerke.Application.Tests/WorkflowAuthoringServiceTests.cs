using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;

namespace Agentwerke.Application.Tests;

public sealed class WorkflowAuthoringServiceTests
{
    [Fact]
    public async Task ImportWorkflowAsync_creates_a_draft_workflow_with_initial_version()
    {
        var validation = new WorkflowValidationResult(
            true,
            "process-invoice",
            "Invoice Approval",
            Array.Empty<WorkflowValidationError>(),
            Array.Empty<WorkflowValidationWarning>());
        var repository = new InMemoryWorkflowDefinitionRepository();
        var validator = new StubWorkflowValidationService(validation);
        var service = new WorkflowAuthoringService(repository, validator);

        var result = await service.ImportWorkflowAsync(
            new ImportWorkflowCommand(
                "invoice.bpmn",
                "<bpmn />",
                Tags: ["github-trigger", "todo-e2e", "GitHub-Trigger", " "]));

        var workflow = Assert.Single(repository.AddedWorkflows);
        Assert.StartsWith("wf_", workflow.Id, StringComparison.Ordinal);
        Assert.Equal("Invoice Approval", workflow.Name);
        Assert.Equal(string.Empty, workflow.Description);
        Assert.Equal(WorkflowVersioning.InitialVersion, workflow.Version);
        Assert.Equal("draft", workflow.Status);
        Assert.Equal("system", workflow.Owner);
        Assert.Equal("valid", workflow.ValidationState);
        Assert.Equal(["github-trigger", "todo-e2e"], workflow.Tags);
        Assert.Equal("<bpmn />", workflow.BpmnXml);
        Assert.True(DateTimeOffset.TryParse(workflow.CreatedAt, out _));
        Assert.Equal(workflow.Id, result.WorkflowId);
        Assert.True(result.Validation.IsValid);
        Assert.Equal("process-invoice", result.Validation.ProcessId);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task PublishWorkflowAsync_updates_state_and_increments_version()
    {
        var workflow = new WorkflowDefinition
        {
            Id = "wf_123",
            Name = "Draft Workflow",
            Description = "Old description",
            Version = WorkflowVersioning.InitialVersion,
            Status = "draft",
            Owner = "system",
            CreatedAt = "2026-06-13T10:00:00.0000000Z",
            LastEditedAt = "2026-06-13T10:00:00.0000000Z",
            ValidationState = "pending",
            BpmnXml = "<old />"
        };
        var validation = new WorkflowValidationResult(
            true,
            "process-invoice",
            "Invoice Approval",
            Array.Empty<WorkflowValidationError>(),
            Array.Empty<WorkflowValidationWarning>());
        var repository = new InMemoryWorkflowDefinitionRepository(workflow);
        var validator = new StubWorkflowValidationService(validation);
        var service = new WorkflowAuthoringService(repository, validator);

        var result = await service.PublishWorkflowAsync(
            "wf_123",
            new PublishWorkflowCommand(
                "<new />",
                "Published description",
                ["github-trigger", "nvidia-litellm"]));

        Assert.Equal("wf_123", result.WorkflowId);
        Assert.Equal("v2.0.0", result.Version);
        Assert.Equal(workflow.Version, result.Version);
        Assert.Equal("active", workflow.Status);
        Assert.Equal("Invoice Approval", workflow.Name);
        Assert.Equal("Published description", workflow.Description);
        Assert.Equal(["github-trigger", "nvidia-litellm"], workflow.Tags);
        Assert.Equal("valid", workflow.ValidationState);
        Assert.Equal("<new />", workflow.BpmnXml);
        Assert.True(DateTimeOffset.TryParse(result.PublishedAt, out _));
        Assert.Equal(result.PublishedAt, workflow.LastEditedAt);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(1, repository.FindTrackedCalls);
    }

    [Fact]
    public async Task PublishWorkflowAsync_rejects_invalid_workflows_before_touching_persistence()
    {
        var repository = new InMemoryWorkflowDefinitionRepository(
            new WorkflowDefinition
            {
                Id = "wf_123",
                Name = "Draft Workflow",
                Version = WorkflowVersioning.InitialVersion
            });
        var validation = new WorkflowValidationResult(
            false,
            null,
            null,
            [
                new WorkflowValidationError(
                    "BPMN XML payload is empty.",
                    null,
                    "document",
                    null,
                    null)
            ],
            Array.Empty<WorkflowValidationWarning>());
        var validator = new StubWorkflowValidationService(validation);
        var service = new WorkflowAuthoringService(repository, validator);

        var exception = await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            service.PublishWorkflowAsync(
                "wf_123",
                new PublishWorkflowCommand("<broken />")));

        Assert.False(exception.Validation.IsValid);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(0, repository.FindTrackedCalls);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    private sealed class InMemoryWorkflowDefinitionRepository : IWorkflowDefinitionRepository
    {
        private readonly List<WorkflowDefinition> _workflows = new();

        public InMemoryWorkflowDefinitionRepository(params WorkflowDefinition[] workflows)
        {
            _workflows.AddRange(workflows);
        }

        public int FindTrackedCalls { get; private set; }

        public int SaveChangesCalls { get; private set; }

        public List<WorkflowDefinition> AddedWorkflows { get; } = new();

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_workflows.ToArray());

        public Task<WorkflowDefinition?> GetAsync(string workflowId, CancellationToken cancellationToken = default)
            => Task.FromResult(_workflows.FirstOrDefault(workflow => workflow.Id == workflowId));

        public Task<WorkflowDefinition?> FindTrackedAsync(string workflowId, CancellationToken cancellationToken = default)
        {
            FindTrackedCalls++;
            return Task.FromResult(_workflows.FirstOrDefault(workflow => workflow.Id == workflowId));
        }

        public Task AddAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
        {
            AddedWorkflows.Add(workflow);
            _workflows.Add(workflow);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubWorkflowValidationService : IWorkflowValidationService
    {
        private readonly WorkflowValidationResult _validation;

        public StubWorkflowValidationService(WorkflowValidationResult validation)
        {
            _validation = validation;
        }

        public int CallCount { get; private set; }

        public WorkflowValidationResult Validate(string bpmnXml)
        {
            CallCount++;
            return _validation;
        }
    }
}
