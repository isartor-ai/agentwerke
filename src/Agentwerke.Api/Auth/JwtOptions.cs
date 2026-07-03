namespace Agentwerke.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SecretKey { get; set; }
    public string? Authority { get; set; }
    public bool DevTokensEnabled { get; set; }
    public bool DevIdentityEnabled { get; set; }
    public string DevIdentitySubject { get; set; } = "dev:admin";
    public string[] DevIdentityRoles { get; set; } = [AgentwerkeRoles.Admin];
    public string[] RoleClaimTypes { get; set; } =
    [
        System.Security.Claims.ClaimTypes.Role,
        "role",
        "roles",
        "groups"
    ];
    public Dictionary<string, string[]> RoleMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] NameClaimTypes { get; set; } =
    [
        System.Security.Claims.ClaimTypes.Name,
        System.Security.Claims.ClaimTypes.NameIdentifier,
        "sub",
        "oid",
        "preferred_username",
        "upn",
        "email"
    ];
}
