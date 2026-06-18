using Autofac.Api.Auth;
using System.Security.Claims;

namespace Autofac.Api.Tests;

public sealed class AuthenticatedPrincipalTests
{
    [Fact]
    public void ResolveSubject_PrefersStableSubjectClaim()
    {
        var principal = Principal(
            new Claim("sub", "subject-123"),
            new Claim(ClaimTypes.Name, "Display Name"));

        Assert.Equal("subject-123", AuthenticatedPrincipal.ResolveSubject(principal));
    }

    [Fact]
    public void ResolveSubject_UsesEnterpriseObjectIdWhenSubjectIsMissing()
    {
        var principal = Principal(
            new Claim("oid", "entra-object-id"),
            new Claim("preferred_username", "alex@example.com"));

        Assert.Equal("entra-object-id", AuthenticatedPrincipal.ResolveSubject(principal));
    }

    [Fact]
    public void ResolveSubject_FallsBackToUnknownUser()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal("unknown-user", AuthenticatedPrincipal.ResolveSubject(principal));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }
}
