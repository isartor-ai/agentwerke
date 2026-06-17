namespace Autofac.Api.Contracts.Templates;

public sealed record CloneTemplateRequest(
    string? Name = null,
    string? Description = null,
    string? Owner = null);
