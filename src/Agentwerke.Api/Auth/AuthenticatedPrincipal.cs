using System.Security.Claims;

namespace Agentwerke.Api.Auth;

internal static class AuthenticatedPrincipal
{
    public static string ResolveSubject(ClaimsPrincipal principal)
    {
        return FirstClaimValue(
                principal,
                ClaimTypes.NameIdentifier,
                "sub",
                "oid",
                "preferred_username",
                ClaimTypes.Upn,
                ClaimTypes.Email,
                ClaimTypes.Name)
            ?? principal.Identity?.Name
            ?? "unknown-user";
    }

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
