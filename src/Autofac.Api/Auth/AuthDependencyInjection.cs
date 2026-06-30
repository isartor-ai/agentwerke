using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace Autofac.Api.Auth;

public static class AuthDependencyInjection
{
    public const string PolicyScheme = "AutofacAuth";

    public static IServiceCollection AddAutofacAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var opts = new JwtOptions();
        configuration.GetSection(JwtOptions.Section).Bind(opts);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        // LDAP/AD directory-group integration (#178). The resolver enriches authenticated
        // principals with directory groups, which RoleMappings turns into Autofac roles.
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.Section));
        if (configuration.GetValue<bool>($"{LdapOptions.Section}:Enabled"))
        {
            services.AddSingleton<ILdapGroupResolver, LdapGroupResolver>();
        }
        else
        {
            services.AddSingleton<ILdapGroupResolver, NullLdapGroupResolver>();
        }

        var authBuilder = services.AddAuthentication(o =>
        {
            o.DefaultScheme = PolicyScheme;
            o.DefaultChallengeScheme = PolicyScheme;
            o.DefaultAuthenticateScheme = PolicyScheme;
        });

        authBuilder.AddPolicyScheme(PolicyScheme, "Autofac auth selector", o =>
        {
            o.ForwardDefaultSelector = context =>
            {
                var authorization = context.Request.Headers.Authorization.FirstOrDefault();
                if (authorization is not null &&
                    authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return JwtBearerDefaults.AuthenticationScheme;
                }

                return environment.IsDevelopment() && opts.DevIdentityEnabled
                    ? DevAuthenticationHandler.SchemeName
                    : JwtBearerDefaults.AuthenticationScheme;
            };
        });

        authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevAuthenticationHandler>(
            DevAuthenticationHandler.SchemeName,
            options => { });

        if (!string.IsNullOrWhiteSpace(opts.SecretKey))
        {
            authBuilder.AddJwtBearer(o =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey));
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = !string.IsNullOrWhiteSpace(opts.Issuer),
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(opts.Audience),
                    ValidAudience = opts.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
                o.Events = CreateJwtBearerEvents(opts);
            });
        }
        else if (!string.IsNullOrWhiteSpace(opts.Authority))
        {
            authBuilder.AddJwtBearer(o =>
            {
                o.Authority = opts.Authority;
                o.Audience = opts.Audience;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = !string.IsNullOrWhiteSpace(opts.Audience),
                    ValidAudience = opts.Audience,
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
                o.Events = CreateJwtBearerEvents(opts);
            });
        }
        else
        {
            authBuilder.AddJwtBearer();
        }

        services.AddAuthorization(o =>
        {
            o.AddPolicy(AutofacPolicies.Viewer, p =>
                p.RequireAuthenticatedUser()
                    .RequireRole(
                        AutofacRoles.Viewer,
                        AutofacRoles.Operator,
                        AutofacRoles.Approver,
                        AutofacRoles.Admin));

            o.AddPolicy(AutofacPolicies.Operator, p =>
                p.RequireAuthenticatedUser()
                    .RequireRole(AutofacRoles.Operator, AutofacRoles.Admin));

            o.AddPolicy(AutofacPolicies.Approver, p =>
                p.RequireAuthenticatedUser()
                    .RequireRole(AutofacRoles.Approver, AutofacRoles.Admin));

            o.AddPolicy(AutofacPolicies.Admin, p =>
                p.RequireAuthenticatedUser()
                    .RequireRole(AutofacRoles.Admin));
        });

        return services;
    }

    private static JwtBearerEvents CreateJwtBearerEvents(JwtOptions opts)
    {
        return new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    NormalizeRoleClaims(identity, opts);
                    EnrichRolesFromLdap(identity, opts, context.HttpContext.RequestServices);
                    NormalizeNameClaim(identity, opts);
                }

                return Task.CompletedTask;
            }
        };
    }

    private static void NormalizeRoleClaims(ClaimsIdentity identity, JwtOptions opts)
    {
        var existingRoles = identity.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roleClaimValues = opts.RoleClaimTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct()
            .SelectMany(type => identity.FindAll(type).Select(claim => claim.Value));

        foreach (var role in AutofacRoleMapper.ResolveRoles(roleClaimValues, opts))
        {
            if (existingRoles.Add(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }

    private static void EnrichRolesFromLdap(ClaimsIdentity identity, JwtOptions opts, IServiceProvider services)
    {
        var ldapOptions = services.GetService<IOptions<LdapOptions>>()?.Value;
        var resolver = services.GetService<ILdapGroupResolver>();
        if (ldapOptions is null || resolver is null || !ldapOptions.Enabled)
        {
            return;
        }

        var username = ResolveLdapUsername(identity, ldapOptions);
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var groups = resolver.ResolveGroups(username);
        if (groups.Count == 0)
        {
            return;
        }

        var existingRoles = identity.FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in AutofacRoleMapper.ResolveRoles(groups, opts))
        {
            if (existingRoles.Add(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }
    }

    private static string? ResolveLdapUsername(ClaimsIdentity identity, LdapOptions ldap)
    {
        foreach (var claimType in ldap.UsernameClaimTypes)
        {
            var value = identity.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return identity.Name;
    }

    private static void NormalizeNameClaim(ClaimsIdentity identity, JwtOptions opts)
    {
        if (identity.HasClaim(claim => claim.Type == ClaimTypes.Name))
        {
            return;
        }

        var nameClaim = opts.NameClaimTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => identity.FindFirst(type))
            .FirstOrDefault(claim => !string.IsNullOrWhiteSpace(claim?.Value));

        if (nameClaim is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, nameClaim.Value));
        }
    }
}
