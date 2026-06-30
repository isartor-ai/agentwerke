using Autofac.Api.Auth;
using Autofac.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Autofac.Api.Controllers;

/// <summary>
/// Read-only connector inventory for the Integrations dashboard (#188): which external
/// connectors are registered, whether they are enabled, and what operations they support.
/// Credentials are edited in Settings; this just reports status.
/// </summary>
[ApiController]
[Route("api/connectors")]
[Authorize(Policy = AutofacPolicies.Viewer)]
public sealed class ConnectorsController : ControllerBase
{
    private readonly IConnectorRegistry _registry;

    public ConnectorsController(IConnectorRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult List() => Ok(_registry.List());
}
