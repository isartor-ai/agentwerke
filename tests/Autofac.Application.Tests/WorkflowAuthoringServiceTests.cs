using Autofac.Application.Workflows;
using Autofac.Domain.Persistence;

namespace Autofac.Application.Tests;

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
        var service = new WorkflowAuthoringService(
            repository,
            validator,
            new StubWorkflowDeploymentService());

        var result = await service.ImportWorkflowAsync(
            new ImportWorkflowCommand("invoice.bpmn", "<bpmn />"));

        var workflow = Assert.Single(repository.AddedWorkflows);
        Assert.StartsWith("wf_", workflow.Id, StringComparison.Ordinal);
        Assert.Equal("Invoice Approval", workflow.Name);
        Assert.Equal(string.Empty, workflow.Description);
        Assert.Equal(WorkflowVersioning.InitialVersion, workflow.Version);
        Assert.Equal("draft", workflow.Status);
        Assert.Equal("system", workflow.Owner);
        Assert.Equal("valid", workflow.ValidationState);
        Assert.Equal("<bpmn />", workflow.BpmnXml);
        Assert.True(DateTimeOffset.TryParse(workflow.CreatedAt, out _));
        Assert.Equal(workflow.Id, result.WorkflowId);
        Assert.True(result.Validation.IsValid);
        Assert.Equal("process-invoice", result.Validation.ProcessId);
        Assert.Equal(1, validator.ValidateCallCount);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task PublishWorkflowAsync_deploys_projected_bpmn_and_persists_camunda_metadata()
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
        var preparation = new WorkflowPublishPreparation(validation, "<projected />");
        var deploymentService = new StubWorkflowDeploymentService(
            new WorkflowDeploymentResult(
                DeploymentKey: "2251799813685310",
                ProcessDefinitionId: "process-invoice",
                ProcessDefinitionKey: "2251799813685311",
                ProcessDefinitionVersion: 2,
                DeployedAt: "2026-06-16T13:00:00.0000000Z"));
        var repository = new InMemoryWorkflowDefinitionRepository(workflow);
        var validator = new StubWorkflowValidationService(validation, preparation);
        var service = new WorkflowAuthoringService(repository, validator, deploymentService);

        var result = await service.PublishWorkflowAsync(
            "wf_123",
            new PublishWorkflowCommand("<new />", "Published description"));

        Assert.Equal("wf_123", result.WorkflowId);
        Assert.Equal("v2.0.0", result.Version);
        Assert.Equal(workflow.Version, result.Version);
        Assert.Equal("2251799813685310", result.Camunda.DeploymentKey);
        Assert.Equal("process-invoice", result.Camunda.ProcessDefinitionId);
        Assert.Equal("2251799813685311", result.Camunda.ProcessDefinitionKey);
        Assert.Equal(2, result.Camunda.ProcessDefinitionVersion);
        Assert.Equal("2026-06-16T13:00:00.0000000Z", result.Camunda.DeployedAt);
        Assert.Equal("active", workflow.Status);
        Assert.Equal("Invoice Approval", workflow.Name);
        Assert.Equal("Published description", workflow.Description);
        Assert.Equal("valid", workflow.ValidationState);
        Assert.Equal("<new />", workflow.BpmnXml);
        Assert.Equal("2251799813685310", workflow.CamundaDeploymentKey);
        Assert.Equal("process-invoice", workflow.CamundaProcessDefinitionId);
        Assert.Equal("2251799813685311", workflow.CamundaProcessDefinitionKey);
        Assert.Equal(2, workflow.CamundaProcessDefinitionVersion);
        Assert.Equal("2026-06-16T13:00:00.0000000Z", workflow.CamundaDeployedAt);
        Assert.True(DateTimeOffset.TryParse(result.PublishedAt, out _));
        Assert.Equal(result.PublishedAt, workflow.LastEditedAt);
        Assert.Equal("<projected />", deploymentService.LastRequest?.ProjectedBpmnXml);
        Assert.Equal("process-invoice.bpmn", deploymentService.LastRequest?.ResourceName);
        Assert.Equal(0, validator.ValidateCallCount);
        Assert.Equal(1, validator.PrepareCallCount);
        Assert.Equal(1, deploymentService.CallCount);
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
        var deploymentService = new StubWorkflowDeploymentService();
        var service = new WorkflowAuthoringService(repository, validator, deploymentService);

        var exception = await Assert.ThrowsAsync<WorkflowValidationException>(() =>
            service.PublishWorkflowAsync(
                "wf_123",
                new PublishWorkflowCommand("<broken />")));

        Assert.False(exception.Validation.IsValid);
        Assert.Equal(0, validator.ValidateCallCount);
        Assert.Equal(1, validator.PrepareCallCount);
        Assert.Equal(0, deploymentService.CallCount);
        Assert.Equal(0, repository.FindTrackedCalls);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task PublishWorkflowAsync_leaves_workflow_unpublished_when_camunda_deployment_fails()
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
        var validator = new StubWorkflowValidationService(
            validation,
            new WorkflowPublishPreparation(validation, "<projected />"));
        var deploymentService = new StubWorkflowDeploymentService(
            exception: new WorkflowDeploymentException(
                "Workflow deployment failed.",
                [
                    new WorkflowDeploymentError(
                        "camunda_bad_request",
                        "Camunda rejected the BPMN deployment.")
                ]));
        var service = new WorkflowAuthoringService(repository, validator, deploymentService);

        var exception = await Assert.ThrowsAsync<WorkflowDeploymentException>(() =>
            service.PublishWorkflowAsync(
                "wf_123",
                new PublishWorkflowCommand("<new />", "Published description")));

        Assert.Equal("Workflow deployment failed.", exception.Message);
        Assert.Single(exception.Errors);
        Assert.Equal("draft", workflow.Status);
        Assert.Equal("Draft Workflow", workflow.Name);
        Assert.Equal("Old description", workflow.Description);
        Assert.Equal(WorkflowVersioning.InitialVersion, workflow.Version);
        Assert.Equal("pending", workflow.ValidationState);
        Assert.Equal("<old />", workflow.BpmnXml);
        Assert.Equal("2026-06-13T10:00:00.0000000Z", workflow.LastEditedAt);
        Assert.Null(workflow.CamundaDeploymentKey);
        Assert.Null(workflow.CamundaProcessDefinitionId);
        Assert.Null(workflow.CamundaProcessDefinitionKey);
        Assert.Null(workflow.CamundaProcessDefinitionVersion);
        Assert.Null(workflow.CamundaDeployedAt);
        Assert.Equal(1, validator.PrepareCallCount);
        Assert.Equal(1, deploymentService.CallCount);
        Assert.Equal(1, repository.FindTrackedCalls);
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
        private readonly WorkflowPublishPreparation _preparation;

        public StubWorkflowValidationService(
            WorkflowValidationResult validation,
            WorkflowPublishPreparation? preparation = null)
        {
            _validation = validation;
            _preparation = preparation ?? new WorkflowPublishPreparation(validation, null);
        }

        public int ValidateCallCount { get; private set; }

        public int PrepareCallCount { get; private set; }

        public WorkflowValidationResult Validate(string bpmnXml)
        {
            ValidateCallCount++;
            return _validation;
        }

        public WorkflowPublishPreparation PrepareForPublish(string bpmnXml)
        {
            PrepareCallCount++;
            return _preparation;
        }
    }

    private sealed class StubWorkflowDeploymentService : IWorkflowDeploymentService
    {
        private readonly WorkflowDeploymentResult _result;
        private readonly WorkflowDeploymentException? _exception;

        public StubWorkflowDeploymentService(
            WorkflowDeploymentResult? result = null,
            WorkflowDeploymentException? exception = null)
        {
            _result = result ?? new WorkflowDeploymentResult(
                DeploymentKey: "2251799813685301",
                ProcessDefinitionId: "process",
                ProcessDefinitionKey: "2251799813685302",
                ProcessDefinitionVersion: 1,
                DeployedAt: "2026-06-16T12:00:00.0000000Z");
            _exception = exception;
        }

        public int CallCount { get; private set; }

        public WorkflowDeploymentRequest? LastRequest { get; private set; }

        public Task<WorkflowDeploymentResult> DeployAsync(
            WorkflowDeploymentRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result);
        }
    }
}
