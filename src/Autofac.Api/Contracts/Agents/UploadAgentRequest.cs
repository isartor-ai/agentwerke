namespace Autofac.Api.Contracts.Agents;

public sealed record UploadAgentRequest(
    string FileName,
    string Content);
