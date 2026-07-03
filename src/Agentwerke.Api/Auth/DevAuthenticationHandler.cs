using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Agentwerke.Api.Auth;

internal sealed class DevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AgentwerkeDevIdentity";

    private readonly JwtOptions _jwtOptions;

    public DevAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<JwtOptions> jwtOptions)
        : base(options, logger, encoder)
    {
        _jwtOptions = jwtOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = string.IsNullOrWhiteSpace(_jwtOptions.DevIdentitySubject)
            ? "dev:admin"
            : _jwtOptions.DevIdentitySubject;

        var claims = new List<Claim>
        {
            new("sub", subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject)
        };

        var roles = _jwtOptions.DevIdentityRoles.Length == 0
            ? [AgentwerkeRoles.Admin]
            : _jwtOptions.DevIdentityRoles;

        claims.AddRange(roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
