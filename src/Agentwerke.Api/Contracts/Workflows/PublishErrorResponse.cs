using System.Collections.Generic;

namespace Agentwerke.Api.Contracts.Workflows;

public sealed record PublishErrorResponse(
    string Message,
    IReadOnlyList<string> Errors);
