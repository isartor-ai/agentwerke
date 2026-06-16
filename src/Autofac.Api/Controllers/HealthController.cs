using Autofac.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly ICamundaRuntimeStatusService _camundaRuntimeStatusService;

    public HealthController(ICamundaRuntimeStatusService camundaRuntimeStatusService)
    {
        _camundaRuntimeStatusService = camundaRuntimeStatusService;
    }

    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "ok" });

    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { status = "ready" });

    [HttpGet("camunda")]
    public async Task<IActionResult> Camunda(CancellationToken cancellationToken)
    {
        var status = await _camundaRuntimeStatusService.GetStatusAsync(cancellationToken);
        return Ok(status);
    }
}
