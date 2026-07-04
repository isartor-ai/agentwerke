using Agentwerke.Api.Auth;
using Agentwerke.Application.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Controllers;

/// <summary>
/// Read-only audit / decision-trace explorer (#189). Returns immutable audit records,
/// optionally filtered to a single run (the decision trace for that run).
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize(Policy = AgentwerkePolicies.Operator)]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditQuery _audit;

    public AuditController(IAuditQuery audit)
    {
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? runId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var records = await _audit.QueryAsync(runId, limit, cancellationToken);

        var entries = records.Select(record => new AuditEntry(
            record.Id,
            record.RunId,
            record.Timestamp,
            record.ActorType,
            record.Actor,
            record.Action,
            record.ResourceType,
            record.ResourceId,
            record.Outcome,
            record.Details));

        return Ok(entries);
    }
}

public sealed record AuditEntry(
    string Id,
    string RunId,
    string Timestamp,
    string ActorType,
    string Actor,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Outcome,
    string? Details);
