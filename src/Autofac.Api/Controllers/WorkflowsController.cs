using Autofac.Api.Contracts.Workflows;
using Autofac.Domain.Persistence;
using Autofac.Infrastructure.Persistence;
using Autofac.Workflows.Bpmn;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly AutofacDbContext _dbContext;
    private readonly IBpmnWorkflowValidator _validator;

    public WorkflowsController(AutofacDbContext dbContext, IBpmnWorkflowValidator validator)
    {
        _dbContext = dbContext;
        _validator = validator;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var workflows = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Select(w => new WorkflowSummary(w.Id, w.Name, w.Status))
            .ToListAsync();

        return Ok(workflows);
    }

    [HttpGet("{workflowId}")]
    public async Task<IActionResult> Get(string workflowId)
    {
        var workflow = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workflowId);

        if (workflow == null)
        {
            return NotFound();
        }

        return Ok(new WorkflowDetail(workflow.Id, workflow.Name, workflow.Status, DateTimeOffset.Parse(workflow.LastEditedAt)));
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportWorkflowRequest request)
    {
        var validation = _validator.Validate(request.BpmnXml);
        var workflowId = $"wf_{Guid.NewGuid():N}";

        var workflow = new WorkflowDefinition
        {
            Id = workflowId,
            Name = validation.Definition?.ProcessName ?? request.FileName,
            Description = string.Empty,
            Version = "v1.0.0",
            Status = "draft",
            Owner = "system",
            CreatedAt = DateTime.UtcNow.ToString("o"),
            LastEditedAt = DateTime.UtcNow.ToString("o"),
            ValidationState = validation.IsValid ? "valid" : "invalid",
            BpmnXml = request.BpmnXml
        };

        _dbContext.WorkflowDefinitions.Add(workflow);
        await _dbContext.SaveChangesAsync();

        return Ok(new ImportWorkflowResponse(
            workflowId,
            ToValidationResponse(validation)));
    }

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ValidateWorkflowRequest request)
    {
        var validation = _validator.Validate(request.BpmnXml);
        return Ok(ToValidationResponse(validation));
    }

    [HttpPost("{workflowId}/policy-simulation")]
    public IActionResult PolicySimulation(string workflowId, [FromBody] PolicySimulationRequest? request = null)
    {
        var tasks = new[]
        {
            new PolicySimulationTask(
                NodeId: "task-deploy",
                RiskLevel: "Critical",
                RequiredApprovals: new[] { "Release Manager", "SRE Lead" },
                RequiredEvidence: new[] { "ci_passed", "sast_passed", "artifact_signed" }),
        };

        return Ok(new PolicySimulationResponse(tasks));
    }

    [HttpPost("{workflowId}/publish")]
    public async Task<IActionResult> Publish(string workflowId, [FromBody] PublishWorkflowRequest request)
    {
        var validation = _validator.Validate(request.BpmnXml);
        if (!validation.IsValid)
        {
            return BadRequest(new PublishErrorResponse(
                Message: "Workflow validation failed. Fix errors before publishing.",
                Errors: validation.Errors.Select(e => e.Message).ToArray()));
        }

        var workflow = await _dbContext.WorkflowDefinitions.FirstAsync(w => w.Id == workflowId);
        workflow.Status = "active";
        workflow.LastEditedAt = DateTime.UtcNow.ToString("o");
        workflow.BpmnXml = request.BpmnXml;
        // A more sophisticated versioning strategy would be needed here
        workflow.Version = "v" + (int.Parse(workflow.Version.Substring(1).Split('.')[0]) + 1) + ".0.0";

        await _dbContext.SaveChangesAsync();

        return Ok(new PublishWorkflowResponse(
            WorkflowId: workflowId,
            Version: workflow.Version,
            PublishedAt: DateTimeOffset.Parse(workflow.LastEditedAt)));
    }

    private static ValidationResponse ToValidationResponse(BpmnValidationResult validation)
    {
        return new ValidationResponse(
            IsValid: validation.IsValid,
            ProcessId: validation.Definition?.ProcessId,
            ProcessName: validation.Definition?.ProcessName,
            Errors: validation.Errors.Select(error => new ValidationErrorResponse(
                error.Message,
                error.ElementId,
                error.ElementName,
                error.LineNumber,
                error.LinePosition)).ToArray());
    }
}
