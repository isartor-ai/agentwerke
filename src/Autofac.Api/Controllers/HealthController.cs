using Autofac.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly ICamundaRuntimeStatusService _camundaRuntimeStatusService;
    private readonly WorkflowRuntimeOptions _runtimeOptions;

    public HealthController(
        ICamundaRuntimeStatusService camundaRuntimeStatusService,
        WorkflowRuntimeOptions runtimeOptions)
    {
        _camundaRuntimeStatusService = camundaRuntimeStatusService;
        _runtimeOptions = runtimeOptions;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "ok" });

    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new
    {
        status = "ready",
        runtimeMode = _runtimeOptions.Mode.ToString()
    });

    [HttpGet("runtime")]
    public IActionResult Runtime() => Ok(new
    {
        mode = _runtimeOptions.Mode.ToString(),
        camundaEnabled = _runtimeOptions.IsCamundaMode
    });

    [HttpGet("camunda")]
    public async Task<IActionResult> Camunda(CancellationToken cancellationToken)
    {
        var status = await _camundaRuntimeStatusService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }
}
