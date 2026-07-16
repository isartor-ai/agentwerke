using Agentwerke.Api.Auth;
using Agentwerke.Api.Contracts.Runs;
using Agentwerke.Application.Agents;
using Agentwerke.Application.Workflows;
using Agentwerke.Domain.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agentwerke.Api.Controllers;

[ApiController]
[Route("api/interactions")]
[Authorize(Policy = AgentwerkePolicies.Viewer)]
public sealed class InteractionsController : ControllerBase
{
    private readonly IAgentInteractionRepository _interactions;
    private readonly IInteractionDeliveryRepository _deliveries;
    private readonly IWorkflowRunRepository _runs;
    private readonly IWorkflowRunOrchestrationService _orchestration;
    private readonly IInteractionRouter _router;

    public InteractionsController(
        IAgentInteractionRepository interactions,
        IInteractionDeliveryRepository deliveries,
        IWorkflowRunRepository runs,
        IWorkflowRunOrchestrationService orchestration,
        IInteractionRouter router)
    {
        _interactions = interactions;
        _deliveries = deliveries;
        _runs = runs;
        _orchestration = orchestration;
        _router = router;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status = AgentInteractionStatuses.Pending,
        [FromQuery] string? addresseeType = AgentInteractionAddresseeTypes.Human,
        [FromQuery] string? runId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, AgentInteractionStatuses.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Only status 'pending' is supported by this inbox endpoint." });
        }

        var interactions = await _interactions.GetPendingAsync(runId, addresseeType, cancellationToken);
        var summaries = new List<InteractionSummary>(interactions.Count);
        var workflowNames = new Dictionary<string, string?>();
        foreach (var interaction in interactions)
        {
            // The repositories share the request-scoped EF DbContext, which does not support
            // concurrent operations. Keep enrichment sequential until batch repository methods exist.
            var deliveryRows = await _deliveries.GetByInteractionAsync(interaction.Id, cancellationToken);
            if (!workflowNames.TryGetValue(interaction.RunId, out var workflowName))
            {
                workflowName = (await _runs.GetRunAsync(interaction.RunId, cancellationToken))?.WorkflowName;
                workflowNames[interaction.RunId] = workflowName;
            }
            summaries.Add(InteractionSummaryMappings.ToSummary(interaction, deliveryRows, workflowName));
        }

        return Ok(summaries);
    }

    [Authorize(Policy = AgentwerkePolicies.Approver)]
    [HttpPost("{interactionId}/cancel")]
    public async Task<IActionResult> Cancel(
        string interactionId,
        [FromBody] CancelInteractionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Reason))
        {
            return BadRequest(new { message = "A cancellation reason is required." });
        }

        try
        {
            var actor = AuthenticatedPrincipal.ResolveSubject(User);
            return Accepted(await _orchestration.CancelInteractionAsync(
                new CancelInteractionCommand(interactionId, request.Reason.Trim(), actor), cancellationToken));
        }
        catch (InteractionNotFoundException)
        {
            return NotFound();
        }
        catch (InteractionNotPendingException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [Authorize(Policy = AgentwerkePolicies.Operator)]
    [HttpPost("{interactionId}/deliveries/{channel}/retry")]
    public async Task<IActionResult> RetryDelivery(
        string interactionId,
        string channel,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _router.RetryAsync(interactionId, channel, cancellationToken));
        }
        catch (InteractionNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
