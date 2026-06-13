namespace Autofac.Api.Contracts.Runs;

public sealed record RunEvent(
    string Id,
    string Type,
    string Message,
    string CreatedAt);
