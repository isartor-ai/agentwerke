using Autofac.Workflows.Bpmn;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IBpmnWorkflowValidator _validator;

    public WorkflowsController(IBpmnWorkflowValidator validator)
    {
        _validator = validator;
    }

    [HttpGet]
    public IActionResult List()
    {
        var workflows = new[]
        {
            new WorkflowSummary("wf_bootstrap", "Bootstrap Platform", "draft"),
            new WorkflowSummary("wf_review", "Policy Review", "draft")
        };

        return Ok(workflows);
    }

    [HttpGet("{workflowId}")]
    public IActionResult Get(string workflowId)
    {
        return Ok(new WorkflowDetail(
            workflowId,
            $"Workflow {workflowId}",
            "draft",
            DateTimeOffset.UtcNow));
    }

    [HttpPost("import")]
    public IActionResult Import([FromBody] ImportWorkflowRequest request)
    {
        var validation = _validator.Validate(request.BpmnXml);
        var workflowId = $"wf_{Guid.NewGuid():N}";

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

    public sealed record WorkflowSummary(string WorkflowId, string Name, string Status);

    public sealed record WorkflowDetail(
        string WorkflowId,
        string Name,
        string Status,
        DateTimeOffset UpdatedAtUtc);

    public sealed record ImportWorkflowRequest(string FileName, string BpmnXml);

    public sealed record ValidateWorkflowRequest(string? WorkflowId, string BpmnXml);

    public sealed record ImportWorkflowResponse(string WorkflowId, ValidationResponse Validation);

    public sealed record ValidationResponse(
        bool IsValid,
        string? ProcessId,
        string? ProcessName,
        IReadOnlyList<ValidationErrorResponse> Errors);

    public sealed record ValidationErrorResponse(
        string Message,
        string? ElementId,
        string ElementName,
        int? LineNumber,
        int? LinePosition);
}
