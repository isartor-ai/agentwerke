using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpGet("config")]
    public IActionResult GetAuthConfig()
    {
        return Ok(new
        {
            authentication = "hook-enabled",
            providers = Array.Empty<string>()
        });
    }

    [HttpPost("token")]
    public IActionResult ExchangeToken()
    {
        return StatusCode(StatusCodes.Status501NotImplemented, new
        {
            error = "Auth provider integration is not configured yet."
        });
    }
}
