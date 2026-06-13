using System;

namespace Autofac.Api.Contracts.Runs;

public sealed record RunDetail(
    string RunId,
    string WorkflowId,
    string Status,
    DateTimeOffset StartedAtUtc);
