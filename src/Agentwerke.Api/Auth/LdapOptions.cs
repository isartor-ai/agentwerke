namespace Agentwerke.Api.Auth;

/// <summary>
/// LDAP / Active Directory directory-group integration (#178). When enabled, an
/// authenticated user's directory groups are resolved and mapped to Agentwerke roles
/// via the existing <see cref="JwtOptions.RoleMappings"/> (group DN/name → role).
/// </summary>
public sealed class LdapOptions
{
    public const string Section = "Ldap";

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 389;

    public bool UseSsl { get; set; }

    /// <summary>Service-account DN used to bind before searching. Empty = anonymous bind.</summary>
    public string BindDn { get; set; } = string.Empty;

    public string BindPassword { get; set; } = string.Empty;

    /// <summary>Search base for user lookups, e.g. <c>ou=Users,dc=corp,dc=example</c>.</summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>User search filter; <c>{0}</c> is the username, e.g. <c>(sAMAccountName={0})</c>.</summary>
    public string UserSearchFilter { get; set; } = "(sAMAccountName={0})";

    /// <summary>Attribute on the user entry listing group memberships, e.g. <c>memberOf</c>.</summary>
    public string GroupMemberAttribute { get; set; } = "memberOf";

    /// <summary>Claim types inspected (in order) to derive the LDAP username from the token.</summary>
    public string[] UsernameClaimTypes { get; set; } = ["preferred_username", "upn", "email", "name"];
}
