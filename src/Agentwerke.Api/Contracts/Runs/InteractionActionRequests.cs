namespace Agentwerke.Api.Contracts.Runs;

public sealed record RejectInteractionRequest(string Reason);

public sealed record CancelInteractionRequest(string Reason);
