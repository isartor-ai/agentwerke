using Autofac.Api.Auth;

namespace Autofac.Api.Tests;

public sealed class AutofacRoleMapperTests
{
    [Fact]
    public void ResolveRoles_MapsEnterpriseGroupClaimsToAutofacRoles()
    {
        var opts = new JwtOptions
        {
            RoleMappings =
            {
                ["entra-autofac-admins"] = [AutofacRoles.Admin],
                ["sg-approval-board"] = [AutofacRoles.Approver, AutofacRoles.Viewer]
            }
        };

        var roles = AutofacRoleMapper.ResolveRoles(
            ["[\"entra-autofac-admins\",\"sg-approval-board\",\"unmapped-group\"]"],
            opts);

        Assert.Contains(AutofacRoles.Admin, roles);
        Assert.Contains(AutofacRoles.Approver, roles);
        Assert.Contains(AutofacRoles.Viewer, roles);
        Assert.DoesNotContain("unmapped-group", roles);
    }

    [Fact]
    public void ResolveRoles_OnlyAcceptsCanonicalDirectRoles()
    {
        var roles = AutofacRoleMapper.ResolveRoles(
            [AutofacRoles.Operator, "ShadowAdmin"],
            new JwtOptions());

        var role = Assert.Single(roles);
        Assert.Equal(AutofacRoles.Operator, role);
    }

    [Fact]
    public void ResolveRoles_IgnoresInvalidMappedRoles()
    {
        var opts = new JwtOptions
        {
            RoleMappings =
            {
                ["platform-team"] = [AutofacRoles.Admin, "Owner"]
            }
        };

        var roles = AutofacRoleMapper.ResolveRoles(["platform-team"], opts);

        var role = Assert.Single(roles);
        Assert.Equal(AutofacRoles.Admin, role);
    }
}
