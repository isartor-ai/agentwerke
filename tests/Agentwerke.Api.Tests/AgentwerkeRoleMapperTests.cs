using Agentwerke.Api.Auth;

namespace Agentwerke.Api.Tests;

public sealed class AgentwerkeRoleMapperTests
{
    [Fact]
    public void ResolveRoles_MapsEnterpriseGroupClaimsToAutofacRoles()
    {
        var opts = new JwtOptions
        {
            RoleMappings =
            {
                ["entra-autofac-admins"] = [AgentwerkeRoles.Admin],
                ["sg-approval-board"] = [AgentwerkeRoles.Approver, AgentwerkeRoles.Viewer]
            }
        };

        var roles = AgentwerkeRoleMapper.ResolveRoles(
            ["[\"entra-autofac-admins\",\"sg-approval-board\",\"unmapped-group\"]"],
            opts);

        Assert.Contains(AgentwerkeRoles.Admin, roles);
        Assert.Contains(AgentwerkeRoles.Approver, roles);
        Assert.Contains(AgentwerkeRoles.Viewer, roles);
        Assert.DoesNotContain("unmapped-group", roles);
    }

    [Fact]
    public void ResolveRoles_OnlyAcceptsCanonicalDirectRoles()
    {
        var roles = AgentwerkeRoleMapper.ResolveRoles(
            [AgentwerkeRoles.Operator, "ShadowAdmin"],
            new JwtOptions());

        var role = Assert.Single(roles);
        Assert.Equal(AgentwerkeRoles.Operator, role);
    }

    [Fact]
    public void ResolveRoles_IgnoresInvalidMappedRoles()
    {
        var opts = new JwtOptions
        {
            RoleMappings =
            {
                ["platform-team"] = [AgentwerkeRoles.Admin, "Owner"]
            }
        };

        var roles = AgentwerkeRoleMapper.ResolveRoles(["platform-team"], opts);

        var role = Assert.Single(roles);
        Assert.Equal(AgentwerkeRoles.Admin, role);
    }
}
