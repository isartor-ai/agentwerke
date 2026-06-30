using Autofac.Api.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Autofac.Api.Tests;

public sealed class LdapGroupResolverTests
{
    [Fact]
    public void RoleMappings_MapLdapGroupDnsToAutofacRoles()
    {
        var opts = new JwtOptions
        {
            RoleMappings =
            {
                ["cn=Autofac-Approvers,ou=Groups,dc=corp,dc=example"] = [AutofacRoles.Approver],
                ["cn=Autofac-Admins,ou=Groups,dc=corp,dc=example"] = [AutofacRoles.Admin],
            },
        };

        var roles = AutofacRoleMapper.ResolveRoles(
            [
                "cn=Autofac-Approvers,ou=Groups,dc=corp,dc=example",
                "cn=Some-Other-Group,ou=Groups,dc=corp,dc=example",
            ],
            opts);

        Assert.Contains(AutofacRoles.Approver, roles);
        Assert.DoesNotContain(AutofacRoles.Admin, roles);
    }

    [Fact]
    public void NullLdapGroupResolver_ReturnsNoGroups()
    {
        Assert.Empty(new NullLdapGroupResolver().ResolveGroups("alice"));
    }

    [Fact]
    public void LdapGroupResolver_WhenDisabled_ReturnsEmptyWithoutConnecting()
    {
        var resolver = new LdapGroupResolver(
            Options.Create(new LdapOptions { Enabled = false, Host = "ldap.invalid" }),
            NullLogger<LdapGroupResolver>.Instance);

        // Disabled: must short-circuit (no connection attempt to the unreachable host).
        Assert.Empty(resolver.ResolveGroups("alice"));
    }

    [Fact]
    public void LdapGroupResolver_WhenUsernameEmpty_ReturnsEmpty()
    {
        var resolver = new LdapGroupResolver(
            Options.Create(new LdapOptions { Enabled = true, Host = "ldap.example" }),
            NullLogger<LdapGroupResolver>.Instance);

        Assert.Empty(resolver.ResolveGroups(string.Empty));
    }
}
