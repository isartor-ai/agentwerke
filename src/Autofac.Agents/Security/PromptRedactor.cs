using Autofac.Domain.Security;

namespace Autofac.Agents.Security;

/// <summary>
/// Strips well-known secret patterns from text before it is persisted in agent runtime snapshots.
/// Applied to prompt content only — the live prompt sent to the model is never modified.
/// </summary>
public static partial class PromptRedactor
{
    public static string Redact(string? input) => SecretRedactor.Redact(input);
}
