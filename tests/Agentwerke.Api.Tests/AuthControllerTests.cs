using Agentwerke.Api.Auth;
using Agentwerke.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Agentwerke.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public void GetCurrentUser_RequiresViewerPolicy()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.GetCurrentUser));

        Assert.NotNull(method);
        Assert.Contains(
            method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false).Cast<AuthorizeAttribute>(),
            attribute => attribute.Policy == AgentwerkePolicies.Viewer);
    }

    [Fact]
    public void GetCurrentUser_ReturnsCanonicalUserRoles()
    {
        var controller = new AuthController(Options.Create(new JwtOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = Principal(
                        new Claim("sub", "subject-123"),
                        new Claim("preferred_username", "alex@example.com"),
                        new Claim(ClaimTypes.Role, AgentwerkeRoles.Operator),
                        new Claim(ClaimTypes.Role, "raw-enterprise-group"))
                }
            }
        };

        var result = Assert.IsType<OkObjectResult>(controller.GetCurrentUser());
        var user = Assert.IsType<CurrentUserResponse>(result.Value);

        Assert.Equal("subject-123", user.Id);
        Assert.Equal("alex@example.com", user.Name);
        Assert.Equal("alex@example.com", user.Email);
        Assert.Equal([AgentwerkeRoles.Operator], user.Roles);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }
}
