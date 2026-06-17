using System.Text.Json;
using Autofac.Api.Contracts.Runs;
using Autofac.Api.Controllers;
using Autofac.Application.Workflows;
using Autofac.Storage.Artifacts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Tests;

public sealed class RunsControllerTests
{
    [Fact]
    public async Task Start_returns_structured_bad_request_when_run_start_fails()
    {
        var controller = new RunsController(
            dbContext: null!,
            artifactStorage: new NoopArtifactStorage(),
            orchestrationService: new ThrowingWorkflowRunOrchestrationService(
                new WorkflowRunStartException(
                    "Run start failed.",
                    [
                        new WorkflowRunStartError(
                        "invalid_input",
                        "Run input must be a JSON object when provided.")
                    ])));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        using var inputDocument = JsonDocument.Parse("""["bad","input"]""");

        var result = await controller.Start(
            new StartRunRequest(
                WorkflowId: "wf_123",
                Input: inputDocument.RootElement.Clone()));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<StartRunErrorResponse>(badRequest.Value);
        Assert.Equal("Run start failed.", payload.Message);
        Assert.Single(payload.Errors);
        Assert.Equal("Run input must be a JSON object when provided.", payload.Errors[0]);
    }

    private sealed class ThrowingWorkflowRunOrchestrationService : IWorkflowRunOrchestrationService
    {
        private readonly Exception _exception;

        public ThrowingWorkflowRunOrchestrationService(Exception exception)
        {
            _exception = exception;
        }

        public Task<StartRunResult> StartRunAsync(StartRunCommand command, CancellationToken cancellationToken = default)
            => Task.FromException<StartRunResult>(_exception);

        public Task<ResumeRunResult> ResumeRunAsync(ResumeRunCommand command, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RecoverRunResult> RecoverRunAsync(string runId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopArtifactStorage : IArtifactStorage
    {
        public Task<IReadOnlyList<ArtifactDescriptor>> ListAsync(string runId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ArtifactDescriptor>>([]);

        public Task SaveAsync(string runId, string artifactName, Stream content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string runId, string artifactName, CancellationToken cancellationToken)
            => Task.FromResult<Stream>(Stream.Null);

        public Task<bool> ExistsAsync(string runId, string artifactName, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
