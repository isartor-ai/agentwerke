using System.Collections.Generic;

namespace Autofac.Api.Contracts.Runs;

public sealed record StartRunErrorResponse(
    string Message,
    IReadOnlyList<string> Errors);
