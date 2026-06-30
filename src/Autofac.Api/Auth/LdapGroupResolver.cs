using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;

namespace Autofac.Api.Auth;

/// <summary>Resolves the directory groups a user belongs to (#178).</summary>
public interface ILdapGroupResolver
{
    IReadOnlyList<string> ResolveGroups(string username);
}

/// <summary>No-op default used when LDAP is not configured.</summary>
public sealed class NullLdapGroupResolver : ILdapGroupResolver
{
    public IReadOnlyList<string> ResolveGroups(string username) => [];
}

/// <summary>
/// Binds to LDAP/AD and reads the user's group memberships. The group values
/// (typically group DNs) are mapped to Autofac roles by <see cref="JwtOptions.RoleMappings"/>.
///
/// Short-circuits to an empty result when disabled/misconfigured, and never throws —
/// a directory outage must not block authentication. The bind/search path is
/// compile-verified against the client; end-to-end use needs a real directory.
/// </summary>
public sealed class LdapGroupResolver : ILdapGroupResolver
{
    private readonly LdapOptions _options;
    private readonly ILogger<LdapGroupResolver> _logger;

    public LdapGroupResolver(IOptions<LdapOptions> options, ILogger<LdapGroupResolver> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<string> ResolveGroups(string username)
    {
        if (!_options.Enabled
            || string.IsNullOrWhiteSpace(_options.Host)
            || string.IsNullOrWhiteSpace(username))
        {
            return [];
        }

        try
        {
            using var connection = new LdapConnection { SecureSocketLayer = _options.UseSsl };
            connection.Connect(_options.Host, _options.Port);
            connection.Bind(_options.BindDn, _options.BindPassword);

            var filter = string.Format(_options.UserSearchFilter, EscapeFilter(username));
            var results = connection.Search(
                _options.BaseDn,
                LdapConnection.ScopeSub,
                filter,
                [_options.GroupMemberAttribute],
                typesOnly: false);

            var groups = new List<string>();
            while (results.HasMore())
            {
                LdapEntry entry;
                try
                {
                    entry = results.Next();
                }
                catch (LdapException)
                {
                    continue; // referral or transient entry error — skip
                }

                var attribute = entry.GetAttribute(_options.GroupMemberAttribute);
                if (attribute?.StringValueArray is { } values)
                {
                    groups.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)));
                }
            }

            return groups;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LDAP group lookup failed for {Username}; continuing without LDAP roles.", username);
            return [];
        }
    }

    // Minimal RFC 4515 escaping for the username injected into the search filter.
    private static string EscapeFilter(string value) => value
        .Replace("\\", "\\5c")
        .Replace("*", "\\2a")
        .Replace("(", "\\28")
        .Replace(")", "\\29")
        .Replace("\0", "\\00");
}
