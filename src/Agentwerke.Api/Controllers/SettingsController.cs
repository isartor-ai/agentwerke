using Agentwerke.Api.Auth;
using Agentwerke.Api.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Policy = AgentwerkePolicies.Admin)]
public sealed class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;

    public SettingsController(ISettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsSnapshotResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await _settings.GetSnapshotAsync(cancellationToken));
    }

    [HttpPatch]
    public async Task<IActionResult> Update(
        [FromBody] SettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _settings.UpdateAsync(request, User, cancellationToken);
            return Ok(response);
        }
        catch (SettingsValidationException ex)
        {
            return BadRequest(new SettingsValidationProblemResponse(ex.Errors));
        }
    }

    [HttpPost("tests/{target}")]
    public async Task<ActionResult<SettingsTestResponse>> TestTarget(
        string target,
        CancellationToken cancellationToken)
    {
        return Ok(await _settings.TestTargetAsync(target, User, cancellationToken));
    }
}
