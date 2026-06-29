using Autofac.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Autofac.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly JwtOptions _jwt;

    public AuthController(IOptions<JwtOptions> jwt)
    {
        _jwt = jwt.Value;
    }

    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult GetAuthConfig()
    {
        var mode = !string.IsNullOrWhiteSpace(_jwt.Authority)
            ? "oidc"
            : !string.IsNullOrWhiteSpace(_jwt.SecretKey)
                ? "symmetric-jwt"
                : _jwt.DevIdentityEnabled
                    ? "development-identity"
                    : "unconfigured";

        return Ok(new
        {
            authentication = mode,
            issuer = _jwt.Issuer,
            audience = _jwt.Audience,
            authority = _jwt.Authority,
            devTokensEnabled = _jwt.DevTokensEnabled,
            devIdentityEnabled = _jwt.DevIdentityEnabled,
            roles = new[] { AutofacRoles.Viewer, AutofacRoles.Operator, AutofacRoles.Approver, AutofacRoles.Admin }
        });
    }

    [HttpGet("me")]
    [Authorize(Policy = AutofacPolicies.Viewer)]
    public IActionResult GetCurrentUser()
    {
        var subject = AuthenticatedPrincipal.ResolveSubject(User);
        var roles = User.FindAll(ClaimTypes.Role)
            .Select(claim => AutofacRoleMapper.CanonicalizeRole(claim.Value))
            .Where(role => role is not null)
            .Select(role => role!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new CurrentUserResponse(
            Id: subject,
            Name: ResolveDisplayName(subject),
            Email: ResolveEmail(),
            Roles: roles));
    }

    [HttpPost("token")]
    [AllowAnonymous]
    public IActionResult IssueDevToken([FromBody] DevTokenRequest request)
    {
        if (!_jwt.DevTokensEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Dev token issuance is disabled. Set Jwt:DevTokensEnabled=true to enable."
            });
        }

        if (string.IsNullOrWhiteSpace(_jwt.SecretKey))
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = "Jwt:SecretKey is not configured. Cannot issue dev tokens."
            });
        }

        var validRoles = new[] { AutofacRoles.Viewer, AutofacRoles.Operator, AutofacRoles.Approver, AutofacRoles.Admin };
        if (!validRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = $"Unknown role '{request.Role}'. Valid roles: {string.Join(", ", validRoles)}." });
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var subject = request.Subject ?? $"dev:{request.Role.ToLowerInvariant()}";
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim(ClaimTypes.Name, subject),
            new Claim(ClaimTypes.Role, request.Role)
        };

        var expiry = DateTime.UtcNow.AddHours(request.ExpiryHours > 0 ? request.ExpiryHours : 8);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: credentials);

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            subject,
            role = request.Role,
            expiresAt = expiry.ToString("o")
        });
    }

    private string ResolveDisplayName(string subject)
    {
        return FirstClaimValue(
                ClaimTypes.Name,
                "name",
                "preferred_username",
                ClaimTypes.Upn,
                ClaimTypes.Email)
            ?? User.Identity?.Name
            ?? subject;
    }

    private string? ResolveEmail()
    {
        var value = FirstClaimValue(ClaimTypes.Email, "email", ClaimTypes.Upn, "preferred_username");
        return value?.Contains('@', StringComparison.Ordinal) == true ? value : null;
    }

    private string? FirstClaimValue(params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = User.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

public sealed record DevTokenRequest(string Role, string? Subject = null, int ExpiryHours = 8);
public sealed record CurrentUserResponse(string Id, string Name, string? Email, IReadOnlyList<string> Roles);
