using Agentwerke.Api.Auth;
using Agentwerke.Api.Contracts;
using Agentwerke.Api.Contracts.Workflows;
using Agentwerke.Application.Workflows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize(Policy = AgentwerkePolicies.Viewer)]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowAuthoringService _workflowAuthoringService;

    public WorkflowsController(IWorkflowAuthoringService workflowAuthoringService)
    {
        _workflowAuthoringService = workflowAuthoringService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var workflows = await _workflowAuthoringService.ListWorkflowsAsync();
        return Ok(workflows.Select(ApiContractMappings.ToWorkflowSummary).ToList());
    }

    [HttpGet("{workflowId}")]
    public async Task<IActionResult> Get(string workflowId)
    {
        var workflow = await _workflowAuthoringService.GetWorkflowAsync(workflowId);

        if (workflow == null)
        {
            return NotFound();
        }

        return Ok(ApiContractMappings.ToWorkflowDetail(workflow));
    }

    [Authorize(Policy = AgentwerkePolicies.Operator)]
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportWorkflowRequest request)
    {
        var result = await _workflowAuthoringService.ImportWorkflowAsync(
            new ImportWorkflowCommand(request.FileName, request.BpmnXml));

        return Ok(ApiContractMappings.ToImportWorkflowResponse(result));
    }

    [Authorize(Policy = AgentwerkePolicies.Operator)]
    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ValidateWorkflowRequest request)
    {
        var validation = _workflowAuthoringService.ValidateWorkflow(request.BpmnXml);
        return Ok(ApiContractMappings.ToValidationResponse(validation));
    }

    [Authorize(Policy = AgentwerkePolicies.Operator)]
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

    [Authorize(Policy = AgentwerkePolicies.Admin)]
    [HttpPost("{workflowId}/publish")]
    public async Task<IActionResult> Publish(string workflowId, [FromBody] PublishWorkflowRequest request)
    {
        try
        {
            var result = await _workflowAuthoringService.PublishWorkflowAsync(
                workflowId,
                new PublishWorkflowCommand(request.BpmnXml, request.Description));

            return Ok(ApiContractMappings.ToPublishWorkflowResponse(result));
        }
        catch (WorkflowValidationException ex)
        {
            return BadRequest(new PublishErrorResponse(
                Message: ex.Message,
                Errors: ex.Validation.Errors.Select(error => error.Message).ToArray()));
        }
        catch (WorkflowNotFoundException)
        {
            return NotFound();
        }
    }
}
