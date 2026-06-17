using System.Text.Json;

namespace Autofac.Api.Contracts.Runs;

public sealed record StartRunRequest(
    string WorkflowId,
    JsonElement? Input = null);
