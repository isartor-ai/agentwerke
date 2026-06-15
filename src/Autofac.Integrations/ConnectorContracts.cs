using System.Text.Json;
using Autofac.Domain.Persistence;

namespace Autofac.Integrations;

public interface IConnector
{
    string ConnectorId { get; }

    string DisplayName { get; }

    bool Enabled { get; }

    IReadOnlyList<string> SupportedOperations { get; }

    Task<ConnectorExecutionResult> ExecuteAsync(
        ConnectorExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IConnectorRegistry
{
    IReadOnlyList<ConnectorDescriptor> List();

    IConnector? Find(string connectorId);
}

public sealed record ConnectorDescriptor(
    string ConnectorId,
    string DisplayName,
    bool Enabled,
    IReadOnlyList<string> SupportedOperations);

public sealed record ConnectorExecutionRequest(
    string RunId,
    string Actor,
    string Operation,
    string? Environment,
    string PurposeType,
    string PolicyTag,
    IReadOnlyList<string> RequiresEvidence,
    JsonElement Payload);

public sealed record ConnectorExecutionResult(
    bool Succeeded,
    string Status,
    string Summary,
    string? ExternalId = null,
    string? ExternalUrl = null,
    PolicyDecision? PolicyDecision = null,
    string? FailureReason = null);
