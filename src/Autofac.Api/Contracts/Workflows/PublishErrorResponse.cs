using System.Collections.Generic;

namespace Autofac.Api.Contracts.Workflows;

public sealed record PublishErrorResponse(
    string Message,
    IReadOnlyList<string> Errors);
