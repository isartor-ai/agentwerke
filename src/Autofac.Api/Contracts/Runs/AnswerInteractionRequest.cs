namespace Autofac.Api.Contracts.Runs;

/// <summary>Body for answering a pending agent interaction (#192): the human's free-text or chosen option.</summary>
public sealed record AnswerInteractionRequest(string Answer);
