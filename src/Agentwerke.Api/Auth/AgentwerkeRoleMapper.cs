using System.Text.Json;

namespace Agentwerke.Api.Auth;

internal static class AgentwerkeRoleMapper
{
    private static readonly string[] CanonicalRoles =
    [
        AgentwerkeRoles.Viewer,
        AgentwerkeRoles.Operator,
        AgentwerkeRoles.Approver,
        AgentwerkeRoles.Admin
    ];

    public static IReadOnlyList<string> ResolveRoles(IEnumerable<string> claimValues, JwtOptions opts)
    {
        var roles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claimValue in claimValues)
        {
            foreach (var value in ExpandClaimValue(claimValue))
            {
                foreach (var mappedRole in ResolveSingleValue(value, opts))
                {
                    var canonicalRole = CanonicalizeRole(mappedRole);
                    if (canonicalRole is not null && seen.Add(canonicalRole))
                    {
                        roles.Add(canonicalRole);
                    }
                }
            }
        }

        return roles;
    }

    public static string? CanonicalizeRole(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return CanonicalRoles.FirstOrDefault(role => role.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ResolveSingleValue(string value, JwtOptions opts)
    {
        var canonicalRole = CanonicalizeRole(value);
        if (canonicalRole is not null)
        {
            yield return canonicalRole;
        }

        if (opts.RoleMappings.TryGetValue(value.Trim(), out var mappedRoles))
        {
            foreach (var mappedRole in mappedRoles)
            {
                yield return mappedRole;
            }
        }
    }

    private static IEnumerable<string> ExpandClaimValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            string[]? roles = null;
            try
            {
                roles = JsonSerializer.Deserialize<string[]>(trimmed);
            }
            catch (JsonException)
            {
                // The token emitted a single claim that happened to start with '['.
            }

            if (roles is not null)
            {
                foreach (var role in roles.Where(role => !string.IsNullOrWhiteSpace(role)))
                {
                    yield return role;
                }

                yield break;
            }
        }

        yield return trimmed;
    }
}
